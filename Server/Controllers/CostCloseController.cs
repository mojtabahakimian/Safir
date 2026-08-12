using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.CostClose;
using Safir.Server.Security;
using Safir.Server.Services;   // IConnectionStringProvider
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;
using System.Data;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// ماژول بستن ماه بهای تمام‌شده.
    /// تمام منطق سنگین در رویه‌های CC_sp_* است؛ این کنترلر فقط آنها را صدا می‌زند.
    /// </summary>
    [ApiController]
    [Route("api/cost-close")]
    [Authorize]
    public class CostCloseController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly ICostCloseQueue _queue;
        private readonly IConnectionStringProvider _csProvider;
        private readonly ILogger<CostCloseController> _logger;

        public CostCloseController(
            IDatabaseService db,
            ICostCloseQueue queue,
            IConnectionStringProvider csProvider,
            ILogger<CostCloseController> logger)
        {
            _db = db;
            _queue = queue;
            _csProvider = csProvider;
            _logger = logger;
        }

        private string CurrentUser =>
            User.FindFirst(BaseknowClaimTypes.UUSER)?.Value
            ?? User.Identity?.Name
            ?? "unknown";

        // ═══════════════════════ اجراها ═══════════════════════

        [HttpGet("runs")]
        [Pay2Authorize(CostForms.History, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostRunDto>>> GetRuns(
            [FromQuery] short? year = null, [FromQuery] byte? month = null)
        {
            const string sql = @"
                SELECT TOP 200 *
                FROM   dbo.CC_Run
                WHERE  (@year  IS NULL OR FiscalYear  = @year)
                  AND  (@month IS NULL OR PeriodMonth = @month)
                ORDER BY RunId DESC";

            return Ok(await _db.DoGetDataSQLAsync<CostRunDto>(sql, new { year, month }));
        }

        [HttpGet("runs/{runId:int}")]
        [Pay2Authorize(CostForms.Run, Pay2Perm.See)]
        public async Task<ActionResult<CostRunStateDto>> GetRunState(int runId)
        {
            const string sql = @"
                SELECT * FROM dbo.CC_Run WHERE RunId = @runId;

                SELECT  s.*
                FROM    dbo.CC_RunStep s
                JOIN   (SELECT StepCode, MAX(Attempt) AS A
                        FROM   dbo.CC_RunStep WHERE RunId = @runId
                        GROUP BY StepCode) x
                       ON x.StepCode = s.StepCode AND x.A = s.Attempt
                WHERE   s.RunId = @runId
                ORDER BY s.SeqNo;

                SELECT  SUM(CASE WHEN Severity = 2 THEN 1 ELSE 0 END) AS Blocking,
                        SUM(CASE WHEN Severity = 1 THEN 1 ELSE 0 END) AS Warning
                FROM    dbo.CC_Exception
                WHERE   RunId = @runId AND IsResolved = 0;

                SELECT  ISNULL(SUM(AmountVariance), 0) AS Remaining
                FROM    dbo.CC_Variance WHERE RunId = @runId;";

            using var grid = await _db.DoGetDataSQLAsyncMultiple(sql, new { runId });

            var state = new CostRunStateDto
            {
                Run   = (await grid.ReadAsync<CostRunDto>()).FirstOrDefault(),
                Steps = (await grid.ReadAsync<CostRunStepDto>()).ToList()
            };

            var counts = (await grid.ReadAsync()).FirstOrDefault();
            state.OpenBlockingCount = (int?)(counts?.Blocking ?? 0) ?? 0;
            state.OpenWarningCount  = (int?)(counts?.Warning  ?? 0) ?? 0;

            var rem = (await grid.ReadAsync()).FirstOrDefault();
            state.RemainingVariance = (decimal?)(rem?.Remaining ?? 0m) ?? 0m;

            if (state.Run is null) return NotFound();
            return Ok(state);
        }

        [HttpPost("runs")]
        [Pay2Authorize(CostForms.Run, Pay2Perm.Inp)]
        [Pay2Authorize(CostForms.ActStart, Pay2Perm.Run)]
        public async Task<ActionResult<int>> CreateRun([FromBody] CreateCostRunRequest req)
        {
            var p = new DynamicParameters();
            p.Add("@FiscalYear", req.FiscalYear);
            p.Add("@Month",      req.PeriodMonth);
            p.Add("@DateFrom",   req.DateFrom);
            p.Add("@DateTo",     req.DateTo);
            p.Add("@RunKind",    req.RunKind);
            p.Add("@UserName",   CurrentUser);
            p.Add("@Note",       req.Note);
            p.Add("@RunId",      dbType: DbType.Int32, direction: ParameterDirection.Output);

            try
            {
                await _db.DoGetStoreProcedureSQLAsync<int>("dbo.CC_sp_RunCreate", p);
                return Ok(p.Get<int>("@RunId"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CreateRun failed for {Year}/{Month}",
                                   req.FiscalYear, req.PeriodMonth);
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// شروع یا ادامه اجرا. کار در صف پس‌زمینه می‌رود و بلافاصله
        /// برمی‌گردد — بستن تب مرورگر اجرا را متوقف نمی‌کند.
        ///
        /// رشته اتصال از درخواست جاری برداشته و همراه کار نگه داشته
        /// می‌شود، چون کار پس‌زمینه HttpContext ندارد.
        /// </summary>
        [HttpPost("runs/{runId:int}/start")]
        [Pay2Authorize(CostForms.ActStart, Pay2Perm.Run)]
        public IActionResult StartRun(int runId, [FromQuery] string[]? onlySteps = null)
        {
            var job = new CostCloseJob(
                runId,
                _csProvider.GetConnectionString(),
                CurrentUser,
                onlySteps is { Length: > 0 } ? onlySteps : null);

            if (!_queue.TryEnqueue(job, out var error))
                return Conflict(error);

            return Accepted(new { runId, queued = true });
        }

        [HttpPost("runs/{runId:int}/resume")]
        [Pay2Authorize(CostForms.ActStart, Pay2Perm.Run)]
        public async Task<IActionResult> ResumeRun(int runId)
        {
            var blocking = await _db.DoGetDataSQLAsyncSingle<int>(
                @"SELECT COUNT(*) FROM dbo.CC_Exception
                  WHERE RunId = @runId AND Severity = 2 AND IsResolved = 0",
                new { runId });

            if (blocking > 0)
                return BadRequest($"{blocking} مورد مسدودکننده هنوز رفع نشده است.");

            return StartRun(runId);
        }

        [HttpPost("runs/{runId:int}/cancel")]
        [Pay2Authorize(CostForms.ActStart, Pay2Perm.Run)]
        public IActionResult CancelRun(int runId)
        {
            _queue.RequestCancel(runId);
            return Ok();
        }

        [HttpGet("runs/{runId:int}/logs")]
        [Pay2Authorize(CostForms.Run, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostRunLogDto>>> GetLogs(
            int runId, [FromQuery] long afterId = 0)
        {
            const string sql = @"
                SELECT TOP 500 *
                FROM   dbo.CC_RunLog
                WHERE  RunId = @runId AND LogId > @afterId
                ORDER BY LogId";

            return Ok(await _db.DoGetDataSQLAsync<CostRunLogDto>(sql, new { runId, afterId }));
        }

        // ═══════════════════════ بازبینی و استثناها ═══════════════════════

        [HttpPost("preflight")]
        [Pay2Authorize(CostForms.Exceptions, Pay2Perm.Run)]
        public async Task<ActionResult<IEnumerable<CostExceptionDto>>> RunPreflight(
            [FromQuery] byte month, [FromQuery] long dateFrom,
            [FromQuery] long dateTo, [FromQuery] int? runId = null)
        {
            await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S00_Preflight",
                new { Month = month, DT1 = dateFrom, DT2 = dateTo, RunId = runId },
                commandTimeout: 600);

            await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_Chk04_MissingFormula",
                new { Month = month, DT1 = dateFrom, DT2 = dateTo, RunId = runId },
                commandTimeout: 600);

            return await GetExceptions(runId, null, false);
        }

        [HttpGet("exceptions")]
        [Pay2Authorize(CostForms.Exceptions, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostExceptionDto>>> GetExceptions(
            [FromQuery] int? runId = null,
            [FromQuery] string? ruleCode = null,
            [FromQuery] bool includeResolved = false)
        {
            const string sql = @"
                SELECT  e.ExceptionId, e.RunId, e.StepCode, e.RuleCode,
                        r.RuleName, e.ExType, e.Severity, e.Anbar, e.Code,
                        s.NAME AS ItemName,
                        e.DocNumber, e.DocTag, e.DocDate, e.Amount,
                        e.RefList, e.CanAutoFix, e.Description,
                        r.RemedyText, r.FixButtonText,
                        e.IsResolved, e.ResolvedBy, e.ResolvedAtUtc, e.ResolutionNote
                FROM    dbo.CC_Exception e
                LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
                LEFT    JOIN dbo.STUF_DEF    s ON TRY_CAST(s.CODE AS BIGINT) = e.Code
                WHERE  (@runId IS NULL AND e.RunId IS NULL OR e.RunId = @runId)
                  AND  (@ruleCode IS NULL OR e.RuleCode = @ruleCode)
                  AND  (@includeResolved = 1 OR e.IsResolved = 0)
                ORDER BY e.Severity DESC, e.RuleCode, ABS(ISNULL(e.Amount,0)) DESC";

            return Ok(await _db.DoGetDataSQLAsync<CostExceptionDto>(
                sql, new { runId, ruleCode, includeResolved }));
        }

        [HttpPost("exceptions/{id:long}/resolve")]
        [Pay2Authorize(CostForms.ActResolve, Pay2Perm.Run)]
        public async Task<IActionResult> ResolveException(
            long id, [FromBody] ResolveExceptionRequest req)
        {
            const string sql = @"
                UPDATE dbo.CC_Exception
                   SET IsResolved     = 1,
                       ResolvedBy     = @user,
                       ResolvedAtUtc  = SYSUTCDATETIME(),
                       ResolutionNote = @note
                 WHERE ExceptionId = @id AND IsResolved = 0";

            var n = await _db.DoExecuteSQLAsync(sql, new { id, user = CurrentUser, note = req.Note });
            return n > 0 ? Ok() : NotFound();
        }

        /// <summary>پذیرش دائمی یک استثنا — دیگر در ماه‌های بعد هشدار نمی‌دهد</summary>
        [HttpPost("exceptions/{id:long}/accept-permanently")]
        [Pay2Authorize(CostForms.ActResolve, Pay2Perm.Upd)]
        public async Task<IActionResult> AcceptPermanently(
            long id, [FromBody] ResolveExceptionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Note))
                return BadRequest("برای پذیرش دائمی، ثبت دلیل الزامی است.");

            const string sql = @"
                INSERT dbo.CC_AcceptedException (RuleCode, Code, Reason, AcceptedBy)
                SELECT e.RuleCode, e.Code, @note, @user
                FROM   dbo.CC_Exception e
                WHERE  e.ExceptionId = @id
                  AND  NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException a
                                   WHERE a.RuleCode = e.RuleCode
                                     AND ISNULL(a.Code,-1) = ISNULL(e.Code,-1));

                UPDATE dbo.CC_Exception
                   SET IsResolved = 1, ResolvedBy = @user,
                       ResolvedAtUtc = SYSUTCDATETIME(),
                       ResolutionNote = N'پذیرش دائمی: ' + @note
                 WHERE ExceptionId = @id;";

            await _db.DoExecuteSQLAsync(sql, new { id, user = CurrentUser, note = req.Note });
            return Ok();
        }

        // ═══════════════════════ اصلاح خودکار ═══════════════════════

        /// <summary>
        /// دکمه «اصلاح خودکار برگه». با WhatIf=true فقط پیش‌نمایش می‌دهد.
        /// </summary>
        [HttpPost("fix/missing-formula")]
        [Pay2Authorize(CostForms.ActAutoFix, Pay2Perm.Run)]
        public async Task<ActionResult<AutoFixResultDto>> FixMissingFormula(
            [FromBody] AutoFixRequest req)
        {
            var result = new AutoFixResultDto { WasPreview = req.WhatIf };

            try
            {
                using var grid = await _db.DoGetDataSQLAsyncMultiple(
                    "EXEC dbo.CC_sp_Fix_MissingFormula " +
                    "@Month=@m, @DT1=@a, @DT2=@b, @RunId=@r, " +
                    "@ExceptionId=@e, @UserName=@u, @WhatIf=@w",
                    new
                    {
                        m = req.PeriodMonth, a = req.DateFrom, b = req.DateTo,
                        r = req.RunId, e = req.ExceptionId,
                        u = CurrentUser, w = req.WhatIf
                    });

                // مجموعه اول ممکن است هشدار «چند فرمولی» باشد
                while (!grid.IsConsumed)
                {
                    var rows = (await grid.ReadAsync()).ToList();
                    if (rows.Count == 0) continue;

                    var first = (IDictionary<string, object>)rows[0];

                    if (first.ContainsKey("هشدار"))
                    {
                        foreach (var r in rows.Cast<IDictionary<string, object>>())
                            result.Warnings.Add(
                                $"{r["نام_کالا"]}: {r["هشدار"]}");
                    }
                    else if (first.ContainsKey("شماره_برگه"))
                    {
                        foreach (var r in rows.Cast<IDictionary<string, object>>())
                            result.Rows.Add(new AutoFixPreviewRow
                            {
                                ProdNo   = Convert.ToInt32 (r["شماره_برگه"]),
                                ProdDate = Convert.ToInt64 (r["تاریخ"]),
                                Code     = Convert.ToInt64 (r["کد_کالا"]),
                                OldFnumb = r["فرمول_فعلی"] is null
                                            ? null : Convert.ToDouble(r["فرمول_فعلی"]),
                                NewFnumb = Convert.ToInt32 (r["فرمول_جدید"]),
                                Meghdar  = Convert.ToDouble(r["مقدار"])
                            });
                    }
                    else if (first.ContainsKey("تعداد_سطر_اصلاح_شده"))
                    {
                        result.RowCount = Convert.ToInt32(first["تعداد_سطر_اصلاح_شده"]);
                    }
                    else if (first.ContainsKey("تعداد_سطر_قابل_اصلاح"))
                    {
                        result.RowCount = Convert.ToInt32(first["تعداد_سطر_قابل_اصلاح"]);
                    }
                }

                result.Message = req.WhatIf
                    ? $"{result.RowCount} سطر قابل اصلاح است."
                    : $"{result.RowCount} سطر اصلاح شد. خروج مواد باید بازسازی شود.";

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoFix failed");
                return BadRequest(ex.Message);
            }
        }

        // ═══════════════════════ انحراف مصرف ═══════════════════════

        [HttpGet("runs/{runId:int}/variances")]
        [Pay2Authorize(CostForms.Variance, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<VarianceRowDto>>> GetVariances(int runId)
        {
            const string sql = @"
                SELECT  v.Code, s.NAME AS ItemName, v.Anbar,
                        v.QtyVariance, v.UnitRate, v.AmountVariance,
                        v.ConsumedQty,
                        CASE WHEN ISNULL(v.ConsumedQty,0) = 0 THEN NULL
                             ELSE v.QtyVariance / v.ConsumedQty * 100 END AS VariancePct,
                        v.IsKeyItem,
                        ISNULL(d.Mode, 2) AS Mode,
                        d.TargetCode,
                        st.NAME AS TargetName,
                        d.TargetFNUMB,
                        d.Note AS LastMonthHint
                FROM    dbo.CC_Variance v
                LEFT    JOIN dbo.CC_VarianceDecision d
                        ON d.Code = v.Code AND d.RunId = v.RunId
                LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = v.Code
                LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = d.TargetCode
                WHERE   v.RunId = @runId
                ORDER BY ABS(ISNULL(v.AmountVariance,0)) DESC";

            return Ok(await _db.DoGetDataSQLAsync<VarianceRowDto>(sql, new { runId }));
        }

        /// <summary>
        /// ثبت گروهی تصمیم‌ها. عمداً یک درخواست برای همه سطرها، نه
        /// یک درخواست به ازای هر سطر — در WASM با تأخیر شبکه تفاوتش
        /// محسوس است.
        /// </summary>
        [HttpPut("runs/{runId:int}/variance-decisions")]
        [Pay2Authorize(CostForms.ActDecide, Pay2Perm.Run)]
        public async Task<IActionResult> SaveDecisions(
            int runId, [FromBody] List<VarianceDecisionInput> items)
        {
            if (items is null || items.Count == 0) return BadRequest("فهرست خالی است.");

            var bad = items.Where(i => i.Mode == 1 && i.TargetCode is null).ToList();
            if (bad.Count > 0)
                return BadRequest($"{bad.Count} کالا حالت «اختصاص» دارد ولی مقصدش تعیین نشده.");

            var month = await _db.DoGetDataSQLAsyncSingle<byte>(
                "SELECT PeriodMonth FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                await conn.ExecuteAsync(
                    "DELETE dbo.CC_VarianceDecision WHERE RunId = @runId",
                    new { runId }, tx);

                // TargetFNUMB از TargetCode مشتق می‌شود، چون GHEYMAT
                // شماره ماه است و فرمول هر ماه FNUMB جداگانه دارد
                const string ins = @"
                    INSERT dbo.CC_VarianceDecision
                        (RunId, Code, Mode, TargetCode, TargetFNUMB, DecidedBy, Note)
                    SELECT @runId, @code, @mode, @targetCode,
                           (SELECT TOP 1 h.FNUMB FROM dbo.HEAD_MANF h
                            WHERE CAST(h.CODE AS BIGINT) = @targetCode
                              AND h.GHEYMAT = @month
                            ORDER BY h.FNUMB DESC),
                           @user, @note";

                foreach (var i in items)
                    await conn.ExecuteAsync(ins, new
                    {
                        runId, i.Code, i.Mode, i.TargetCode,
                        month, user = CurrentUser, i.Note
                    }, tx);
            });

            return Ok(new { saved = items.Count });
        }

        // ═══════════════════════ نتایج محاسبه ═══════════════════════

        [HttpGet("runs/{runId:int}/conversion")]
        [Pay2Authorize(CostForms.Conversion, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<ConversionCostDto>>> GetConversion(int runId)
        {
            const string sql = @"
                SELECT c.UnitId, u.UnitName, c.CostKind, c.AbsorbedAmount,
                       c.AbsorbedFromWip, c.ActualAmount, c.AdjustFactor, c.ApprovedBy
                FROM   dbo.CC_ConversionCost c
                JOIN   dbo.CC_Unit u ON u.UnitId = c.UnitId
                WHERE  c.RunId = @runId
                ORDER BY u.SeqNo, c.CostKind";

            return Ok(await _db.DoGetDataSQLAsync<ConversionCostDto>(sql, new { runId }));
        }

        [HttpGet("runs/{runId:int}/item-costs")]
        [Pay2Authorize(CostForms.Run, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<ItemCostDto>>> GetItemCosts(
            int runId, [FromQuery] short? level = null)
        {
            const string sql = @"
                SELECT ic.Code, s.NAME AS ItemName, ic.LowLevelCode, ic.SourceKind,
                       ic.FNUMB, ic.MaterialCost, ic.WageCost, ic.OverheadCost, ic.TotalCost
                FROM   dbo.CC_ItemCost ic
                LEFT   JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = ic.Code
                WHERE  ic.RunId = @runId
                  AND (@level IS NULL OR ic.LowLevelCode = @level)
                ORDER BY ic.LowLevelCode, ic.TotalCost DESC";

            return Ok(await _db.DoGetDataSQLAsync<ItemCostDto>(sql, new { runId, level }));
        }

        /// <summary>
        /// تاریخچه تغییر یک کالا — پاسخ به «چرا قیمت تمام‌شده این کالا عوض شد؟»
        /// سؤالی که امروز اصلاً قابل جواب نیست.
        /// </summary>
        [HttpGet("runs/{runId:int}/changes")]
        [Pay2Authorize(CostForms.History, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<FormulaChangeDto>>> GetChanges(
            int runId, [FromQuery] long? code = null, [FromQuery] string? stepCode = null)
        {
            const string sql = @"
                SELECT TOP 500
                       f.ChangeId, f.RunId, f.StepCode, f.FNUMB,
                       f.ParentCode, sp.NAME AS ParentName,
                       f.ChildCode,  sc.NAME AS ChildName,
                       f.FieldName, f.OldValue, f.NewValue, f.Reason, f.ChangedAtUtc
                FROM   dbo.CC_FormulaChange f
                LEFT   JOIN dbo.STUF_DEF sp ON TRY_CAST(sp.CODE AS BIGINT) = f.ParentCode
                LEFT   JOIN dbo.STUF_DEF sc ON TRY_CAST(sc.CODE AS BIGINT) = f.ChildCode
                WHERE  f.RunId = @runId
                  AND (@code IS NULL OR f.ParentCode = @code OR f.ChildCode = @code)
                  AND (@stepCode IS NULL OR f.StepCode = @stepCode)
                ORDER BY ABS(ISNULL(f.NewValue,0) - ISNULL(f.OldValue,0)) DESC";

            return Ok(await _db.DoGetDataSQLAsync<FormulaChangeDto>(
                sql, new { runId, code, stepCode }));
        }

        // ═══════════════════════ سود و زیان کالا ═══════════════════════

        [HttpGet("runs/{runId:int}/margins")]
        [Pay2Authorize(CostForms.Margin, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<ItemMarginDto>>> GetMargins(int runId)
        {
            const string sql = @"
                SELECT  m.Code, s.NAME AS ItemName, m.QtySold, m.WeightKg,
                        m.SalesAmount, m.CostAmount, m.Profit,
                        m.UnitCost, m.UnitPrice,
                        m.GrossSales, m.Discount, m.ReturnAmount, m.ReturnQty,
                        ISNULL(t.TargetKind, 3) AS TargetKind,
                        t.TargetPct, t.BalancingCode,
                        sb.NAME AS BalancingName
                FROM    dbo.CC_ItemMargin m
                LEFT    JOIN dbo.CC_MarginTarget t
                        ON t.Code = m.Code AND t.IsActive = 1
                LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = m.Code
                LEFT    JOIN dbo.STUF_DEF sb ON TRY_CAST(sb.CODE AS BIGINT) = t.BalancingCode
                WHERE   m.RunId = @runId
                ORDER BY m.Profit";

            return Ok(await _db.DoGetDataSQLAsync<ItemMarginDto>(sql, new { runId }));
        }

        [HttpPut("margin-targets")]
        [Pay2Authorize(CostForms.Margin, Pay2Perm.Upd)]
        public async Task<IActionResult> SaveMarginTargets(
            [FromBody] List<MarginTargetInput> items)
        {
            if (items is null || items.Count == 0) return BadRequest("فهرست خالی است.");

            var bad = items.Where(i => i.TargetKind is 1 or 2 && i.BalancingCode is null)
                           .ToList();
            if (bad.Count > 0)
                return BadRequest(
                    $"{bad.Count} کالا هدف دارد ولی کالای متعادل‌کننده‌اش تعیین نشده. " +
                    "بدون آن، جمع کل بهای تمام‌شده به هم می‌خورد.");

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                foreach (var i in items)
                {
                    await conn.ExecuteAsync(
                        "UPDATE dbo.CC_MarginTarget SET IsActive = 0 WHERE Code = @Code",
                        new { i.Code }, tx);

                    if (i.TargetKind != 3)
                        await conn.ExecuteAsync(@"
                            INSERT dbo.CC_MarginTarget
                                (Code, TargetKind, TargetPct, BalancingCode, IsActive)
                            VALUES (@Code, @TargetKind, @TargetPct, @BalancingCode, 1)",
                            i, tx);
                }
            });

            return Ok(new { saved = items.Count });
        }

        /// <summary>
        /// اعمال اهداف حاشیه. با whatIf=true فقط پیش‌نمایش و هشدارها.
        /// </summary>
        [HttpPost("runs/{runId:int}/apply-margin-targets")]
        [Pay2Authorize(CostForms.ActApplyRate, Pay2Perm.Run)]
        public async Task<IActionResult> ApplyMarginTargets(
            int runId, [FromQuery] bool whatIf = true)
        {
            var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

            if (run is null) return NotFound();

            var res = await _db.DoGetDataSQLAsync<dynamic>(
                "EXEC dbo.CC_sp_S12b_ApplyMarginTargets @RunId=@r, @Month=@m, " +
                "@DT1=@a, @DT2=@b, @WhatIf=@w",
                new { r = runId, m = run.PeriodMonth,
                      a = run.DateFrom, b = run.DateTo, w = whatIf });

            return Ok(res);
        }

        // ═══════════════════════ گزارش و تأیید ═══════════════════════

        [HttpGet("runs/{runId:int}/report.xlsx")]
        [Pay2Authorize(CostForms.ActExport, Pay2Perm.Run)]
        public async Task<IActionResult> GetReport(
            int runId, [FromServices] IBoardReportBuilder builder)
        {
            var bytes = await builder.BuildAsync(runId);

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"gozaresh-cost-{runId}.xlsx");
        }

        [HttpPost("runs/{runId:int}/approve")]
        [Pay2Authorize(CostForms.ActApprove, Pay2Perm.Run)]
        public async Task<IActionResult> Approve(int runId)
        {
            try
            {
                await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                    "dbo.CC_sp_S14_Approve", new { RunId = runId, UserName = CurrentUser });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ═══════════════════════ بازگردانی ═══════════════════════

        [HttpPost("runs/{runId:int}/rollback")]
        [Pay2Authorize(CostForms.ActRollback, Pay2Perm.Run)]
        public async Task<IActionResult> Rollback(int runId, [FromBody] RollbackRequest req)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            try
            {
                var res = await _db.DoGetDataSQLAsync<dynamic>(
                    "EXEC dbo.CC_sp_Rollback @RunId=@r, @StepCode=@s, " +
                    "@UserName=@u, @WhatIf=@w",
                    new { r = runId, s = req.StepCode, u = CurrentUser, w = req.WhatIf });

                return Ok(res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
        }

        // ═══════════════════════ قواعد ═══════════════════════

        [HttpGet("rules")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostCheckRuleDto>>> GetRules()
        {
            const string sql = "SELECT * FROM dbo.CC_CheckRule ORDER BY SortOrder";
            return Ok(await _db.DoGetDataSQLAsync<CostCheckRuleDto>(sql));
        }

        // ═══════════════════════ واحدهای تولیدی ═══════════════════════

        [HttpGet("units")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostUnitDto>>> GetUnits()
        {
            const string sql = @"
                SELECT * FROM dbo.CC_Unit ORDER BY SeqNo;

                SELECT a.*, n.NAMES AS AnbarName
                FROM   dbo.CC_UnitAnbar a
                LEFT   JOIN dbo.TCOD_ANBAR n ON n.CODE = a.Anbar
                ORDER BY a.UnitId, a.SeqNo;";

            using var grid = await _db.DoGetDataSQLAsyncMultiple(sql);

            var units  = (await grid.ReadAsync<CostUnitDto>()).ToList();
            var anbars = (await grid.ReadAsync<CostUnitAnbarDto>()).ToList();

            foreach (var u in units)
                u.Anbars = anbars.Where(a => a.UnitId == u.UnitId).ToList();

            return Ok(units);
        }
    }
}
