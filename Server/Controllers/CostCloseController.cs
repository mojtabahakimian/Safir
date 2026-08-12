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
