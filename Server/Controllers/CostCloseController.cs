using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.CostClose;
using Safir.Server.Security;
using Safir.Server.Services;   // IConnectionStringProvider
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;
using Safir.Shared.Utility;    // FixPersianChars
using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        // یک کنترلر جدید برای هر درخواست ساخته می‌شود؛ برای جلوگیری از دو اجرای
        // همزمان بازسازی سند برای یک runId باید در سطح فرآیند (static) نگه داشته شود —
        // مشابه الگوی _active در CostCloseQueue.
        private static readonly ConcurrentDictionary<int, byte> _rebuildInProgress = new();

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

        // ═══════════════════════ خواندن ستون‌های فارسی ═══════════════════════

        /// <summary>
        /// خواندن یک ستون فارسی از سطر Dapper، بدون حساسیت به «ی»/«ي» و «ک»/«ك».
        ///
        /// چرا لازم است: رویه‌های CC_sp_* نام‌مستعار فارسی برمی‌گردانند و
        /// پایگاه با Arabic_CI_AS کار می‌کند، ولی دیکشنری Dapper مقایسهٔ
        /// معمولی رشته انجام می‌دهد. یک اختلاف یک‌کاراکتری («تاريخ» با ي عربی
        /// در SQL، در برابر «تاریخ» با ی فارسی در C#) کل «اصلاح خودکار» را با
        /// KeyNotFoundException می‌شکست. از همان FixPersianChars پروژه استفاده
        /// می‌شود که برای همین دسته مشکل نوشته شده است.
        /// </summary>
        private static object? Col(IDictionary<string, object> row, string name)
        {
            if (row.TryGetValue(name, out var direct)) return direct;

            var target = name.FixPersianChars();
            foreach (var kv in row)
                if (kv.Key.FixPersianChars() == target) return kv.Value;

            return null;
        }

        private static bool HasCol(IDictionary<string, object> row, string name)
        {
            if (row.ContainsKey(name)) return true;

            var target = name.FixPersianChars();
            foreach (var kv in row)
                if (kv.Key.FixPersianChars() == target) return true;

            return false;
        }

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

        /// <summary>
        /// ادامه‌ی اجرا همیشه از S00 دوباره شروع می‌شود (CloseOrchestrator با
        /// OnlySteps=null کل زنجیره را می‌سازد)، و S05 دوباره اجرا می‌شود.
        /// اگر اینجا فقط ستون IsResolved را نگاه کنیم، «بستن استثنا» روی یک
        /// مغایرت واقعی (مثلاً CHK-01/02/13) قبولمان می‌کند که راه باز است،
        /// بعد در پس‌زمینه S05 دوباره از داده‌ی واقعی همان مغایرت را بدون حل
        /// می‌سازد و اجرا بی‌هیچ توضیحی دوباره متوقف می‌شود — تجربه‌ای که کاربر
        /// نمی‌تواند بفهمد چرا «رفع‌شده»اش دوباره برگشت. برای اینکه این چک واقعاً
        /// راست بگوید، همان بازبینی‌هایی که ابتدای زنجیره اجرا می‌شوند
        /// (S00Preflight، Chk04، S05Gate) همین‌جا هم زده می‌شوند تا CC_Exception
        /// از روی داده‌ی همین لحظه تازه شود، و شمارش مسدودکننده روی همان نتیجه‌ی
        /// تازه انجام شود.
        /// </summary>
        [HttpPost("runs/{runId:int}/resume")]
        [Pay2Authorize(CostForms.ActStart, Pay2Perm.Run)]
        public async Task<IActionResult> ResumeRun(int runId)
        {
            var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

            if (run is null) return NotFound();

            await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S00_Preflight",
                new { Month = run.PeriodMonth, DT1 = run.DateFrom, DT2 = run.DateTo, RunId = runId },
                commandTimeout: 600);

            await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_Chk04_MissingFormula",
                new { Month = run.PeriodMonth, DT1 = run.DateFrom, DT2 = run.DateTo, RunId = runId },
                commandTimeout: 600);

            await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S05_Gate",
                new { RunId = runId, Month = run.PeriodMonth, DT1 = run.DateFrom, DT2 = run.DateTo },
                commandTimeout: 600);

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
                        r.RuleName, e.ExType, e.Severity, e.Anbar, a.NAMES AS AnbarName, e.Code,
                        s.NAME AS ItemName,
                        e.DocNumber, e.DocTag, e.DocDate, e.Amount,
                        e.RefList, e.CanAutoFix, e.Description,
                        r.RemedyText, r.FixButtonText,
                        e.IsResolved, e.ResolvedBy, e.ResolvedAtUtc, e.ResolutionNote,
                        CASE WHEN e.RuleCode = 'CHK-02' AND EXISTS (
                            SELECT 1
                            FROM   dbo.CC_AnbarHes ah
                            JOIN   dbo.DEED_DTL d ON d.HES_K = ah.HesKol AND d.HES_M = ah.HesMoin
                                                  AND TRY_CAST(d.HES_T AS BIGINT) = e.Code
                            JOIN   dbo.DEED_HED h ON h.N_S = d.N_S
                            WHERE  ah.Anbar = e.Anbar AND (run.DateTo IS NULL OR h.DATE_S <= run.DateTo)
                            GROUP  BY ah.Anbar
                            HAVING COUNT(*) = 1 AND MAX(d.SHARH) LIKE N'%افتتاح%'
                        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsOpeningOnly
                FROM    dbo.CC_Exception e
                LEFT    JOIN dbo.CC_CheckRule r   ON r.RuleCode = e.RuleCode
                LEFT    JOIN dbo.STUF_DEF    s    ON TRY_CAST(s.CODE AS BIGINT) = e.Code
                LEFT    JOIN dbo.CC_Run      run  ON run.RunId = e.RunId
                LEFT    JOIN dbo.TCOD_ANBAR  a    ON a.CODE = e.Anbar
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

        /// <summary>
        /// پذیرش دائمی یک استثنا — دیگر مسدود نمی‌کند، نه در همین اجرا نه در
        /// ماه‌های بعد (چون CC_AcceptedException مقید به RunId نیست). برای
        /// CHK-01/CHK-02 که روی جفت (انبار،کالا) کار می‌کنند، Anbar هم از
        /// خودِ استثنا ثبت می‌شود تا فقط همین انبار خاموش شود، نه همه‌ی
        /// انبارهای آن کالا.
        /// </summary>
        [HttpPost("exceptions/{id:long}/accept-permanently")]
        [Pay2Authorize(CostForms.ActResolvePermanent, Pay2Perm.Run)]
        public async Task<IActionResult> AcceptPermanently(
            long id, [FromBody] ResolveExceptionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Note))
                return BadRequest("برای پذیرش دائمی، ثبت دلیل الزامی است.");

            const string sql = @"
                INSERT dbo.CC_AcceptedException (RuleCode, Code, Anbar, Reason, AcceptedBy)
                SELECT e.RuleCode, e.Code, e.Anbar, @note, @user
                FROM   dbo.CC_Exception e
                WHERE  e.ExceptionId = @id
                  AND  NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException a
                                   WHERE a.RuleCode = e.RuleCode AND a.IsActive = 1
                                     AND ISNULL(a.Code,-1)  = ISNULL(e.Code,-1)
                                     AND ISNULL(a.Anbar,-1) = ISNULL(e.Anbar,-1));

                UPDATE dbo.CC_Exception
                   SET IsResolved = 1, ResolvedBy = @user,
                       ResolvedAtUtc = SYSUTCDATETIME(),
                       ResolutionNote = N'پذیرش دائمی: ' + @note
                 WHERE ExceptionId = @id;";

            await _db.DoExecuteSQLAsync(sql, new { id, user = CurrentUser, note = req.Note });
            return Ok();
        }

        /// <summary>فهرست استثناهای پذیرفته‌شده‌ی فعال — برای صفحه‌ی مدیریت آن‌ها</summary>
        [HttpGet("accepted-exceptions")]
        [Pay2Authorize(CostForms.ActResolve, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<AcceptedExceptionDto>>> GetAcceptedExceptions()
        {
            const string sql = @"
                SELECT  a.Id, a.RuleCode, a.Code, s.NAME AS CodeName, a.Anbar, an.NAMES AS AnbarName,
                        a.Reason, a.AcceptedBy, a.AcceptedAtUtc
                FROM    dbo.CC_AcceptedException a
                LEFT    JOIN dbo.STUF_DEF   s  ON TRY_CAST(s.CODE AS BIGINT) = a.Code
                LEFT    JOIN dbo.TCOD_ANBAR an ON an.CODE = a.Anbar
                WHERE   a.IsActive = 1
                ORDER BY a.AcceptedAtUtc DESC";

            return Ok(await _db.DoGetDataSQLAsync<AcceptedExceptionDto>(sql));
        }

        /// <summary>
        /// لغو پذیرش دائمی — این مورد از دور بعدیِ S05 دوباره به‌عنوان
        /// مسدودکننده نشان داده می‌شود (خودِ ردیف حذف نمی‌شود، فقط IsActive
        /// صفر می‌شود، برای ردیابی این‌که چه کسی/چرا قبلاً پذیرفته بود).
        /// </summary>
        [HttpPost("accepted-exceptions/{id:int}/revoke")]
        [Pay2Authorize(CostForms.ActResolvePermanent, Pay2Perm.Run)]
        public async Task<IActionResult> RevokeAcceptedException(int id)
        {
            var n = await _db.DoExecuteSQLAsync(
                "UPDATE dbo.CC_AcceptedException SET IsActive = 0 WHERE Id = @id AND IsActive = 1",
                new { id });

            return n > 0 ? Ok() : NotFound();
        }

        /// <summary>رفع دسته‌جمعیِ چند استثنا («نادیده گرفتن») — بدون سند اصلاحی.</summary>
        [HttpPost("exceptions/bulk-resolve")]
        [Pay2Authorize(CostForms.ActResolve, Pay2Perm.Run)]
        public async Task<IActionResult> BulkResolve([FromBody] BulkResolveRequest req)
        {
            if (req.ExceptionIds.Count == 0) return Ok(new { count = 0 });

            const string sql = @"
                UPDATE dbo.CC_Exception
                   SET IsResolved     = 1,
                       ResolvedBy     = @user,
                       ResolvedAtUtc  = SYSUTCDATETIME(),
                       ResolutionNote = @note
                 WHERE ExceptionId IN @ids AND IsResolved = 0";

            var n = await _db.DoExecuteSQLAsync(sql, new { ids = req.ExceptionIds, user = CurrentUser, note = req.Note });
            return Ok(new { count = n });
        }

        /// <summary>
        /// پذیرش دائمیِ دسته‌جمعیِ چند استثنا — نسخهٔ چندتاییِ همان اکشن
        /// AcceptPermanently تک‌مورد (نه bulk-resolve؛ آن فقط IsResolved همین
        /// اجرا را می‌زند و ماه بعد که اجرای تازه ساخته می‌شود دوباره برمی‌گردد).
        /// روی هر ExceptionId به‌صورت مجزا INSERT/UPDATE می‌زند تا هر جفت
        /// (RuleCode,Code,Anbar) خودش دوباره چک شود، نه یک شرط مشترک روی کل دسته.
        /// </summary>
        [HttpPost("exceptions/bulk-accept-permanently")]
        [Pay2Authorize(CostForms.ActResolvePermanent, Pay2Perm.Run)]
        public async Task<IActionResult> BulkAcceptPermanently([FromBody] BulkResolveRequest req)
        {
            if (req.ExceptionIds.Count == 0) return Ok(new { count = 0 });
            if (string.IsNullOrWhiteSpace(req.Note))
                return BadRequest("برای پذیرش دائمی، ثبت دلیل الزامی است.");

            const string sql = @"
                INSERT dbo.CC_AcceptedException (RuleCode, Code, Anbar, Reason, AcceptedBy)
                SELECT e.RuleCode, e.Code, e.Anbar, @note, @user
                FROM   dbo.CC_Exception e
                WHERE  e.ExceptionId IN @ids
                  AND  NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException a
                                   WHERE a.RuleCode = e.RuleCode AND a.IsActive = 1
                                     AND ISNULL(a.Code,-1)  = ISNULL(e.Code,-1)
                                     AND ISNULL(a.Anbar,-1) = ISNULL(e.Anbar,-1));

                UPDATE dbo.CC_Exception
                   SET IsResolved = 1, ResolvedBy = @user,
                       ResolvedAtUtc = SYSUTCDATETIME(),
                       ResolutionNote = N'پذیرش دائمی: ' + @note
                 WHERE ExceptionId IN @ids;";

            await _db.DoExecuteSQLAsync(sql, new { ids = req.ExceptionIds, user = CurrentUser, note = req.Note });
            return Ok(new { count = req.ExceptionIds.Count });
        }

        private sealed class DateDriftRef
        {
            [JsonPropertyName("kind")]     public string  Kind    { get; set; } = "";
            [JsonPropertyName("aNumber")]  public long    ANumber { get; set; }
            [JsonPropertyName("aTag")]     public int     ATag    { get; set; }
            [JsonPropertyName("aTable")]   public string  ATable  { get; set; } = "";
            [JsonPropertyName("aDate")]    public long    ADate   { get; set; }
            [JsonPropertyName("bNumber")]  public long    BNumber { get; set; }
            [JsonPropertyName("bTag")]     public int     BTag    { get; set; }
            [JsonPropertyName("bTable")]   public string  BTable  { get; set; } = "";
            [JsonPropertyName("bDate")]    public long    BDate   { get; set; }
        }

        /// <summary>
        /// رفعِ مغایرت CHK-18 (فاصله‌ی بیش از یک ماه بین فاکتور و حواله/رسید یا
        /// برگشت): اپراتور تصمیم می‌گیرد کدام تاریخ درست است — سند «الف» (مثلاً
        /// فاکتور) یا سند «ب» (مثلاً حواله انبار) — و همان تاریخ روی سند دیگر
        /// نوشته می‌شود. جدول هدف (HEAD_LST یا BACK_HEAD) و ستون تگ (TAG یا ta)
        /// از RefList همان استثنا (در CC_sp_S00_Preflight ساخته شده) خوانده
        /// می‌شود تا این یک اکشن عمومی برای هر سه نوع سند (فروش/برگشت فروش/
        /// برگشت خرید) باشد، نه سه مسیر جدا.
        /// </summary>
        [HttpPost("exceptions/{id:long}/fix-date-mismatch")]
        [Pay2Authorize(CostForms.ActFixDateMismatch, Pay2Perm.Run)]
        public async Task<IActionResult> FixDateMismatch(long id, [FromBody] FixDateMismatchRequest req)
        {
            var ex = await _db.DoGetDataSQLAsyncSingle<CostExceptionRefRow>(
                "SELECT ExceptionId, RefList FROM dbo.CC_Exception WHERE ExceptionId = @id", new { id });

            if (ex is null) return NotFound();
            if (string.IsNullOrWhiteSpace(ex.RefList)) return BadRequest("اطلاعات لازم برای اصلاح این مورد ثبت نشده.");

            DateDriftRef refData;
            try
            {
                refData = JsonSerializer.Deserialize<DateDriftRef>(ex.RefList)
                          ?? throw new JsonException("null");
            }
            catch (JsonException)
            {
                return BadRequest("قالب اطلاعاتِ این استثنا برای اصلاح تاریخ مناسب نیست.");
            }

            // اگر UseA=true، سند «ب» با تاریخ «الف» یکی می‌شود؛ وگرنه برعکس.
            var (table, number, tag, newDate) = req.UseA
                ? (refData.BTable, refData.BNumber, refData.BTag, refData.ADate)
                : (refData.ATable, refData.ANumber, refData.ATag, refData.BDate);

            // DEED_HED کلیدش N_S است، نه (NUMBER,TAG) مثل HEAD_LST/BACK_HEAD —
            // «number» همان N_S است و «tag» بی‌معناست (همیشه 0، نادیده گرفته می‌شود).
            string sql = table switch
            {
                "HEAD_LST"  => "UPDATE dbo.HEAD_LST  SET DATE_N = @newDate WHERE NUMBER = @number AND TAG = @tag",
                "BACK_HEAD" => "UPDATE dbo.BACK_HEAD SET DATE_N = @newDate WHERE NUMBER = @number AND ta  = @tag",
                "DEED_HED"  => "UPDATE dbo.DEED_HED  SET DATE_S = @newDate WHERE N_S = @number",
                _ => throw new InvalidOperationException($"جدول ناشناخته: {table}")
            };

            var n = await _db.DoExecuteSQLAsync(sql, new { newDate, number, tag });
            if (n == 0) return BadRequest("سند مقصد برای اصلاح پیدا نشد — شاید قبلاً تغییر کرده.");

            await _db.DoExecuteSQLAsync(
                @"UPDATE dbo.CC_Exception
                     SET IsResolved = 1, ResolvedBy = @user, ResolvedAtUtc = SYSUTCDATETIME(),
                         ResolutionNote = @note
                   WHERE ExceptionId = @id",
                new
                {
                    id,
                    user = CurrentUser,
                    note = $"اصلاح تاریخ: {table} شماره {number} (تگ {tag}) به {newDate} تغییر کرد."
                });

            return Ok();
        }

        private sealed class CostExceptionRefRow
        {
            public long    ExceptionId { get; set; }
            public string? RefList     { get; set; }
        }

        private sealed class ExceptionDocNumberRow
        {
            public long ExceptionId { get; set; }
            public int? DocNumber   { get; set; }
        }

        /// <summary>
        /// رفع CHK-19 (فاکتور فروش با سند حسابداری‌اش یکی نیست): بر خلاف CHK-18،
        /// اینجا نباید مستقیم تاریخ سند حسابداری را UPDATE کرد — یک سند
        /// حسابداری (در حالت «سند روزانه») می‌تواند مشترکِ ده‌ها فاکتورِ دیگر
        /// باشد (نمونه‌ی واقعی: سند ۶۴۱۴، ۴۲ فاکتور)، پس عوض‌کردن تاریخِ آن سند
        /// همه‌ی فاکتورهای دیگرش را هم غلط می‌کرد. راه‌حلِ درست (طبق الگوریتم
        /// اصلیِ GENSANADFROOSH که کاربر ارائه داد) این است که فقط بازسازیِ سند
        /// فروش را برای همین یک فاکتور، با تاریخِ فعلیِ خودش، دوباره اجرا کنیم؛
        /// SaleRebuildService از قبل دقیقاً همین منطق را دارد (سطر‌های داخلی
        /// isDailyMode): اگر سندِ همان روز موجود باشد ردیف‌های این فاکتور را
        /// به آن منتقل می‌کند، وگرنه سند تازه می‌سازد — بدون دست‌زدن به
        /// فاکتورهای دیگرِ سند قدیم. تست شده روی فاکتور ۲۴۶۵: از سند ۶۴۱۴
        /// (۴۲ فاکتوره) به سند تازه‌ی ۷۲۴۴ (فقط همین فاکتور، تاریخ درست)
        /// منتقل شد، ۴۱ فاکتورِ دیگرِ ۶۴۱۴ دست‌نخورده ماندند.
        /// </summary>
        [HttpPost("exceptions/{id:long}/rebuild-sale-doc")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<IActionResult> RebuildSaleDocForException(long id)
        {
            var ex = await _db.DoGetDataSQLAsyncSingle<ExceptionDocNumberRow>(
                "SELECT ExceptionId, DocNumber FROM dbo.CC_Exception WHERE ExceptionId = @id", new { id });

            if (ex is null) return NotFound();
            if (ex.DocNumber is null) return BadRequest("شماره فاکتور برای این مورد ثبت نشده.");

            var inv = await _db.DoGetDataSQLAsyncSingle<long?>(
                "SELECT DATE_N FROM dbo.HEAD_LST WHERE NUMBER = @num AND TAG = 13",
                new { num = ex.DocNumber.Value });

            if (inv is null) return BadRequest("خودِ فاکتور پیدا نشد.");

            var year  = inv.Value / 10000;
            var month = inv.Value / 100 % 100;
            var dt1 = year * 10000 + month * 100 + 1;
            var dt2 = year * 10000 + month * 100 + 31; // کران بالا امن؛ نیازی به شمارش دقیق روزهای ماه نیست

            var svc = new Safir.Server.CostClose.GroupDocuments.SaleRebuildService(_db);
            var res = await svc.RebuildAsync(ex.DocNumber.Value, ex.DocNumber.Value, dt1, dt2);

            if (!res.Success)
                return Ok(new { success = false, error = res.FirstError, log = res.Log });

            await _db.DoExecuteSQLAsync(
                @"UPDATE dbo.CC_Exception
                     SET IsResolved = 1, ResolvedBy = @user, ResolvedAtUtc = SYSUTCDATETIME(),
                         ResolutionNote = N'سند فروش این فاکتور با تاریخ خودش بازسازی شد'
                   WHERE ExceptionId = @id",
                new { id, user = CurrentUser });

            return Ok(new { success = true, error = (string?)null, log = res.Log });
        }

        /// <summary>
        /// رفع مغایرت(های) CHK-02 با یک سند اصلاحی — اختلاف کارت‌انبار/حسابداری
        /// بین حساب موجودیِ همان انبار (CC_AnbarHes) و یک حساب مقصدِ دلخواه
        /// (کل/معین/تفصیلی، مثلاً سود و زیان) جابه‌جا می‌شود. این روی حساب‌های
        /// واقعی می‌نویسد؛ به همین دلیل مجوز جداگانه (نه ActResolve) دارد.
        /// WhatIf=true فقط پیش‌نمایش می‌دهد.
        /// </summary>
        [HttpPost("exceptions/post-correction")]
        [Pay2Authorize(CostForms.ActPostCorrection, Pay2Perm.Run)]
        public async Task<ActionResult<PostCorrectionResultDto>> PostCorrection([FromBody] PostCorrectionRequest req)
        {
            if (req.TargetKol <= 0 || req.TargetMoin <= 0 || req.TargetTaf <= 0)
                return BadRequest("حساب مقصد (کل/معین/تفصیلی) باید مشخص باشد.");

            var svc = new Safir.Server.CostClose.GroupDocuments.CorrectionEntryService(_db);
            var res = await svc.PostAsync(
                req.ExceptionIds, req.TargetKol, req.TargetMoin, req.TargetTaf,
                req.Note, CurrentUser, req.WhatIf, req.DateS);

            return Ok(new PostCorrectionResultDto
            {
                Success     = res.Success,
                Count       = res.Count,
                TotalAbs    = res.TotalAbs,
                SanadNumber = res.SanadNumber,
                FirstError  = res.FirstError,
                Lines = res.Lines.Select(l => new CorrectionPreviewLineDto
                {
                    ExceptionId = l.ExceptionId, Anbar = l.Anbar, Code = l.Code, ItemName = l.ItemName,
                    Amount = l.Amount, AdjustAbs = l.AdjustAbs, DebitIsInventory = l.DebitIsInventory
                }).ToList(),
                Skipped = res.Skipped.Select(s => new CorrectionSkippedDto
                {
                    ExceptionId = s.ExceptionId, Reason = s.Reason
                }).ToList()
            });
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

                    if (HasCol(first, "هشدار"))
                    {
                        foreach (var r in rows.Cast<IDictionary<string, object>>())
                            result.Warnings.Add(
                                $"{Col(r, "نام_کالا")}: {Col(r, "هشدار")}");
                    }
                    else if (HasCol(first, "شماره_برگه"))
                    {
                        foreach (var r in rows.Cast<IDictionary<string, object>>())
                            result.Rows.Add(new AutoFixPreviewRow
                            {
                                ProdNo   = Convert.ToInt32 (Col(r, "شماره_برگه")),
                                ProdDate = Convert.ToInt64 (Col(r, "تاریخ")),
                                Code     = Convert.ToInt64 (Col(r, "کد_کالا")),
                                OldFnumb = Col(r, "فرمول_فعلی") is null
                                            ? null : Convert.ToDouble(Col(r, "فرمول_فعلی")),
                                NewFnumb = Convert.ToInt32 (Col(r, "فرمول_جدید")),
                                Meghdar  = Convert.ToDouble(Col(r, "مقدار"))
                            });
                    }
                    else if (HasCol(first, "تعداد_سطر_اصلاح_شده"))
                    {
                        result.RowCount = Convert.ToInt32(Col(first, "تعداد_سطر_اصلاح_شده"));
                    }
                    else if (HasCol(first, "تعداد_سطر_قابل_اصلاح"))
                    {
                        result.RowCount = Convert.ToInt32(Col(first, "تعداد_سطر_قابل_اصلاح"));
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

        /// <summary>
        /// دکمه «بازسازی نرخ» برای CHK-09 (نرخ منتشرنشده نیمه‌ساخته).
        /// S10 و S11 را دوباره روی داده زنده اجرا می‌کند؛ چون S11 خودش
        /// در انتها استثناهای CHK-09 را پاک و از نو می‌سازد، اگر بعد از
        /// این فراخوانی هنوز چیزی باز مانده باشد یعنی خودِ فرمول ایراد دارد.
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-rates")]
        [Pay2Authorize(CostForms.ActRollup, Pay2Perm.Run)]
        public async Task<IActionResult> RebuildRates(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

            if (run is null) return NotFound();

            try
            {
                await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                    "dbo.CC_sp_S10_BalanceConversion",
                    new { RunId = runId, Month = run.PeriodMonth,
                          DT1 = run.DateFrom, DT2 = run.DateTo, WhatIf = false },
                    commandTimeout: 1800);

                await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                    "dbo.CC_sp_S11_PropagateRates",
                    new { RunId = runId, Month = run.PeriodMonth,
                          DT1 = run.DateFrom, DT2 = run.DateTo, WhatIf = false },
                    commandTimeout: 3600);

                var remaining = await _db.DoGetDataSQLAsyncSingle<int>(
                    @"SELECT COUNT(*) FROM dbo.CC_Exception
                      WHERE RunId = @r AND RuleCode = 'CHK-09' AND IsResolved = 0",
                    new { r = runId });

                return Ok(new { remaining });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildRates failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// بازسازی سند حواله خروج مواد (DEED_HED/DEED_DTL برای HEAD_LST.TAG=10) بعد از
        /// اصلاح نرخ فرمول — تا وقتی این اجرا نشود، سند حسابداری همچنان نرخ قدیمی را
        /// نشان می‌دهد و CHK-02/CHK-08 روی داده کهنه مقایسه می‌کنند.
        /// عمداً فقط برگه‌های همان ماه اجرا (نه کل تاریخچه) بازسازی می‌شوند.
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-material-issue-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<MaterialIssueRebuildResultDto>> RebuildMaterialIssueDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند حواله خروج مواد برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG = 10 AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new MaterialIssueRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "برگه حواله خروج موادی برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.MaterialIssueRebuild.MaterialIssueRebuildService(_db);
                var res = await svc.RebuildAsync(range.MinNum.Value, range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new MaterialIssueRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildMaterialIssueDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        private sealed class MaterialIssueRange
        {
            public long? MinNum { get; set; }
            public long? MaxNum { get; set; }
        }

        /// <summary>
        /// بازسازی سند انتقالی مواد بین انبارها (DEED_HED/DEED_DTL برای
        /// HEAD_LST.TAG=5، NO_S=10). بدون این، CHK-02 برای هر کالایی که
        /// در همین ماه جابه‌جا شده اما سند حسابداری‌اش قدیمی مانده،
        /// مغایرت کاذب نشان می‌دهد. عمداً فقط برگه‌های همان ماه اجرا.
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-transfer-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildTransferDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند انتقالی برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG = 5 AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "برگه انتقالی‌ای برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.TransferRebuildService(_db);
                var res = await svc.RebuildAsync(range.MinNum.Value, range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    SkippedCount = res.SkippedCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildTransferDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// بازسازی سند فروش (DEED_HED/DEED_DTL برای HEAD_LST.TAG=13، NO_S=2).
        /// بدون این، CHK-02 برای هر کالایی که در همین ماه فروخته شده اما سند
        /// حسابداری‌اش قدیمی مانده، مغایرت کاذب نشان می‌دهد. عمداً فقط
        /// برگه‌های همان ماه اجرا.
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-sale-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildSaleDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند فروش برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG = 13 AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "فاکتور فروشی برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.SaleRebuildService(_db);
                var res = await svc.RebuildAsync((long)range.MinNum.Value, (long)range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildSaleDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// بازسازی سند برگشت فروش (DEED_HED/DEED_DTL برای HEAD_LST.TAG=4 و
        /// TAG=25، هر دو NO_S=4). هر دو Pass روی همان بازه‌ی شماره/تاریخ اجرا
        /// می‌شوند؛ TAG=25 (از INVO_LST.TAG=24) روی این دیتابیس مسیر غالب است.
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-sale-return-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildSaleReturnDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند برگشت فروش برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG IN (4, 25) AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "فاکتور برگشت فروشی برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.SaleReturnRebuildService(_db);
                var res = await svc.RebuildAsync((long)range.MinNum.Value, (long)range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildSaleReturnDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// بازسازی سند حواله خروج سایر مواد (DEED_HED/DEED_DTL برای
        /// HEAD_LST.TAG=11، NO_S=12).
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-other-issue-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildOtherIssueDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند حواله خروج سایر مواد برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG = 11 AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "برگه حواله خروج سایر موادی برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.OtherIssueRebuildService(_db);
                var res = await svc.RebuildAsync((long)range.MinNum.Value, (long)range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildOtherIssueDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// بازسازی سند برگشت خرید آزاد (DEED_HED/DEED_DTL برای HEAD_LST.TAG=26، NO_S=3).
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-purchase-return-free-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildPurchaseReturnFreeDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند برگشت خرید آزاد برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG = 26 AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "برگه برگشت خرید آزادی برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.PurchaseReturnFreeRebuildService(_db);
                var res = await svc.RebuildAsync((long)range.MinNum.Value, (long)range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildPurchaseReturnFreeDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// بازسازی سند ورود کالای ساخته‌شده به انبار (DEED_HED/DEED_DTL برای
        /// HEAD_LST.TAG=9، NO_S=9).
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-production-receipt-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildProductionReceiptDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند ورود کالای ساخته‌شده برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(NUMBER) AS MinNum, MAX(NUMBER) AS MaxNum
                      FROM   dbo.HEAD_LST
                      WHERE  TAG = 9 AND DATE_N BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "برگه ورود کالای ساخته‌شده‌ای برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.ProductionReceiptRebuildService(_db);
                var res = await svc.RebuildAsync((long)range.MinNum.Value, (long)range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildProductionReceiptDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// بازسازی سند انبارگردانی (DEED_HED/DEED_DTL برای ANBGRD_HEAD، NO_S=17).
        /// سرِ سند اینجا از HEAD_LST نمی‌آید، بنابراین بازه از ANBGRD_HEAD خودش
        /// استخراج می‌شود (GRD_NUM/GRD_DATE به‌جای NUMBER/DATE_N).
        /// </summary>
        [HttpPost("runs/{runId:int}/rebuild-stock-count-docs")]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<GroupDocumentRebuildResultDto>> RebuildStockCountDocs(int runId)
        {
            if (_queue.IsRunning(runId))
                return Conflict("این اجرا در حال انجام است؛ ابتدا آن را متوقف کنید.");

            if (!_rebuildInProgress.TryAdd(runId, 1))
                return Conflict("بازسازی سند انبارگردانی برای این اجرا از قبل در حال انجام است.");

            try
            {
                var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                    "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

                if (run is null) return NotFound();

                var range = await _db.DoGetDataSQLAsyncSingle<MaterialIssueRange>(
                    @"SELECT MIN(GRD_NUM) AS MinNum, MAX(GRD_NUM) AS MaxNum
                      FROM   dbo.ANBGRD_HEAD
                      WHERE  GRD_DATE BETWEEN @dt1 AND @dt2",
                    new { dt1 = run.DateFrom, dt2 = run.DateTo });

                if (range?.MinNum is null || range.MaxNum is null)
                {
                    return Ok(new GroupDocumentRebuildResultDto
                    {
                        Success = true, SheetCount = 0,
                        Log = new() { "برگه انبارگردانی‌ای برای این ماه یافت نشد." }
                    });
                }

                var svc = new Safir.Server.CostClose.GroupDocuments.StockCountRebuildService(_db);
                var res = await svc.RebuildAsync((long)range.MinNum.Value, (long)range.MaxNum.Value, run.DateFrom, run.DateTo);

                return Ok(new GroupDocumentRebuildResultDto
                {
                    Success = res.Success,
                    SheetCount = res.SheetCount,
                    LastSanadNumber = res.LastSanadNumber,
                    FirstError = res.FirstError,
                    Log = res.Log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RebuildStockCountDocs failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
            finally
            {
                _rebuildInProgress.TryRemove(runId, out _);
            }
        }

        /// <summary>
        /// اصلاح CHK-15 (فرمول با مقدار منفی). کاربر بین «صفر کن» و «حذف کن»
        /// انتخاب می‌کند؛ هر استثنا به یک سطر مشخص از DTL_MANF وصل است.
        /// </summary>
        [HttpPost("fix/negative-formula-qty")]
        [Pay2Authorize(CostForms.ActAutoFix, Pay2Perm.Run)]
        public async Task<ActionResult<FixNegativeFormulaQtyResultDto>> FixNegativeFormulaQty(
            [FromBody] FixNegativeFormulaQtyRequest req)
        {
            if (req.Action != "zero" && req.Action != "delete")
                return BadRequest("Action باید zero یا delete باشد.");

            try
            {
                var rows = (await _db.DoGetDataSQLAsync<dynamic>(
                    "EXEC dbo.CC_sp_Fix_NegativeFormulaQty @ExceptionId=@e, @Action=@a, " +
                    "@RunId=@r, @UserName=@u, @WhatIf=@w",
                    new { e = req.ExceptionId, a = req.Action, r = req.RunId,
                          u = CurrentUser, w = req.WhatIf })).ToList();

                var first = rows.FirstOrDefault() as IDictionary<string, object>;
                if (first is null) return Ok(new FixNegativeFormulaQtyResultDto());

                return Ok(new FixNegativeFormulaQtyResultDto
                {
                    Changed = Convert.ToInt32(Col(first, "تغییر_یافت") ?? 0) == 1,
                    Status  = Col(first, "وضعیت")?.ToString() ?? ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FixNegativeFormulaQty failed for exception {ExceptionId}",
                                   req.ExceptionId);
                return BadRequest(ex.Message);
            }
        }

        // ═══════════════════════ انحراف مصرف ═══════════════════════

        [HttpGet("runs/{runId:int}/variances")]
        [Pay2Authorize(CostForms.Variance, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<VarianceRowDto>>> GetVariances(int runId)
        {
            // یک کالا می‌تواند در چند انبار مانده داشته باشد (CC_Variance یک
            // ردیف به ازای هر (RunId,Anbar,Code) دارد)، ولی این صفحه و
            // CC_VarianceDecision هر دو در سطح کالا کار می‌کنند، نه انبار.
            // اگر مستقیم به CC_Variance جوین بزنیم، کالاهای چندانباره چند
            // بار تکرار می‌شوند — و چون «اعمال و محاسبه مجدد» همین لیست
            // تکراری را عیناً به SaveDecisions برمی‌گرداند، هر بار کلیک
            // تکرار را دوچندان می‌کند (دقیقاً همان چیزی که برای اجرای ۱۶
            // دیده شد: کدهای چندانباره تا ۸ بار در CC_VarianceDecision
            // تکرار شده بودند). اول به ازای هر کد جمع می‌زنیم تا دقیقاً
            // یک ردیف به کاربر برگردد.
            const string sql = @"
                ;WITH VarByCode AS (
                    SELECT  Code,
                            SUM(QtyVariance)    AS QtyVariance,
                            SUM(AmountVariance) AS AmountVariance,
                            SUM(ConsumedQty)    AS ConsumedQty,
                            CAST(MAX(CAST(IsKeyItem AS TINYINT)) AS BIT) AS IsKeyItem,
                            CASE WHEN SUM(ConsumedQty) = 0 THEN NULL
                                 ELSE SUM(AmountVariance) / NULLIF(SUM(QtyVariance),0) END AS UnitRate
                    FROM    dbo.CC_Variance
                    WHERE   RunId = @runId
                    GROUP BY Code
                )
                SELECT  v.Code, s.NAME AS ItemName,
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
                FROM    VarByCode v
                LEFT    JOIN dbo.CC_VarianceDecision d
                        ON d.Code = v.Code AND d.RunId = @runId
                LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = v.Code
                LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = d.TargetCode
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

            try
            {
                var res = await _db.DoGetDataSQLAsync<dynamic>(
                    "EXEC dbo.CC_sp_S12b_ApplyMarginTargets @RunId=@r, @Month=@m, " +
                    "@DT1=@a, @DT2=@b, @WhatIf=@w",
                    new { r = runId, m = run.PeriodMonth,
                          a = run.DateFrom, b = run.DateTo, w = whatIf });

                if (!whatIf)
                {
                    // بدون این، فرمول (IMBIBE_MANF) عوض می‌شود ولی بهای
                    // تمام‌شده‌ی همین صفحه (از CC_ItemMargin) رقم قدیمی را
                    // نشان می‌دهد تا کاربر خودش برود مانیتور اجرا و S11+S12
                    // را دستی بزند. عمداً S10 اینجا نیست: S10 خودش
                    // IMBIBE_MANF را از روی برگه‌های تولید بازمحاسبه می‌کند
                    // و همین تنظیم دستیِ S12b را فوراً خنثی می‌کرد.
                    await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                        "dbo.CC_sp_S11_PropagateRates",
                        new { RunId = runId, Month = run.PeriodMonth,
                              DT1 = run.DateFrom, DT2 = run.DateTo, WhatIf = false },
                        commandTimeout: 3600);

                    await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                        "dbo.CC_sp_S12_CalcMargin",
                        new { RunId = runId, Month = run.PeriodMonth,
                              DT1 = run.DateFrom, DT2 = run.DateTo },
                        commandTimeout: 900);

                    await _db.DoExecuteSQLAsync(
                        "INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message) " +
                        "VALUES (@runId, 'S12b', 1, N'اعمال هدف حاشیه سود — نرخ‌ها دوباره منتشر و سود/زیان بازمحاسبه شد')",
                        new { runId });
                }

                return Ok(res);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ApplyMarginTargets failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// همه‌ی هدف‌های حاشیه‌ی سود فعال — مستقل از فیلتر/صفحه‌بندیِ جدولِ
        /// سود و زیان، چون CC_MarginTarget اصلاً به RunId مقید نیست و هدفی
        /// که امروز روی یه کالای زیان‌ده گذاشته شده، بعداً که اون کالا سودده
        /// بشه (مثلاً با بازسازی نرخ) از فیلتر «زیان‌ده» بیرون می‌ره ولی
        /// خودِ هدف هنوز فعاله — دقیقاً همون چیزی که باعث شد ۱۵ هدفِ
        /// «پخش خودکار» قدیمی نامرئی بمونن و اعمال هدف بعدی رو مسدود کنن.
        /// </summary>
        [HttpGet("margin-targets/active")]
        [Pay2Authorize(CostForms.Margin, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<ActiveMarginTargetDto>>> GetActiveMarginTargets()
        {
            const string sql = @"
                SELECT  t.Id, t.Code, s.NAME AS ItemName, t.TargetKind, t.TargetPct,
                        t.BalancingCode, sb.NAME AS BalancingName
                FROM    dbo.CC_MarginTarget t
                LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = t.Code
                LEFT    JOIN dbo.STUF_DEF sb ON TRY_CAST(sb.CODE AS BIGINT) = t.BalancingCode
                WHERE   t.IsActive = 1
                ORDER BY t.Code";

            return Ok(await _db.DoGetDataSQLAsync<ActiveMarginTargetDto>(sql));
        }

        /// <summary>غیرفعال‌کردن یک هدف حاشیه سود — کالا به «آزاد» برمی‌گردد</summary>
        [HttpPost("margin-targets/{id:int}/deactivate")]
        [Pay2Authorize(CostForms.Margin, Pay2Perm.Upd)]
        public async Task<IActionResult> DeactivateMarginTarget(int id)
        {
            var n = await _db.DoExecuteSQLAsync(
                "UPDATE dbo.CC_MarginTarget SET IsActive = 0 WHERE Id = @id AND IsActive = 1",
                new { id });

            return n > 0 ? Ok() : NotFound();
        }

        // ═══════════════ جابه‌جایی مصرف ماده بین فرمول‌ها ═══════════════

        /// <summary>موادی که در فرمول‌های ماهِ این اجرا مصرف شده‌اند — برای انتخاب ماده</summary>
        [HttpGet("runs/{runId:int}/formula-materials")]
        [Pay2Authorize(CostForms.Margin, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<FormulaMaterialDto>>> GetFormulaMaterials(
            int runId, [FromQuery] string? search = null)
        {
            var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });
            if (run is null) return NotFound();

            const string sql = @"
                SELECT DISTINCT TRY_CAST(d.CODE AS BIGINT) AS Code, s.NAME AS Name
                FROM    dbo.DTL_MANF d
                JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @month
                LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
                WHERE   TRY_CAST(d.CODE AS BIGINT) IS NOT NULL
                  AND   (@search IS NULL OR s.NAME LIKE '%' + @search + '%'
                                          OR d.CODE LIKE '%' + @search + '%')
                ORDER BY s.NAME";

            return Ok(await _db.DoGetDataSQLAsync<FormulaMaterialDto>(
                sql, new { month = run.PeriodMonth, search }));
        }

        /// <summary>فرمول‌هایی که این ماده را در ماهِ این اجرا مصرف کرده‌اند — برای انتخاب فرمول مبدأ/مقصد</summary>
        [HttpGet("runs/{runId:int}/material-consumers/{materialCode:long}")]
        [Pay2Authorize(CostForms.Margin, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<MaterialConsumerDto>>> GetMaterialConsumers(
            int runId, long materialCode)
        {
            var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });
            if (run is null) return NotFound();

            const string sql = @"
                ;WITH Prod AS (
                    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB, SUM(pl.MEGHK) AS ProdQty
                    FROM    dbo.HEAD_LST h
                    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @dt1 AND @dt2
                      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
                    GROUP BY TRY_CAST(pl.N_KOL AS INT)
                    HAVING  SUM(pl.MEGHK) > 0
                )
                SELECT  TRY_CAST(hm.CODE AS BIGINT) AS ParentCode,
                        s.NAME                      AS ParentName,
                        d.MEGHk                     AS MEGHk,
                        ISNULL(d.SMABL, 0)          AS Rate,
                        p.ProdQty                   AS ProdQty
                FROM    dbo.DTL_MANF d
                JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @month
                LEFT    JOIN Prod p ON p.FNUMB = d.FNUMB
                LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = TRY_CAST(hm.CODE AS BIGINT)
                WHERE   TRY_CAST(d.CODE AS BIGINT) = @materialCode
                ORDER BY s.NAME";

            return Ok(await _db.DoGetDataSQLAsync<MaterialConsumerDto>(
                sql, new { month = run.PeriodMonth, dt1 = run.DateFrom, dt2 = run.DateTo, materialCode }));
        }

        /// <summary>
        /// جابه‌جایی مقدار مصرف یک ماده بین دو فرمول (اصلاح روی مواد، نه هزینه تبدیل).
        /// با whatIf=true فقط پیش‌نمایش بها قبل/بعد برای هر دو طرف.
        /// </summary>
        [HttpPost("runs/{runId:int}/rebalance-material")]
        [Pay2Authorize(CostForms.ActApplyRate, Pay2Perm.Run)]
        public async Task<ActionResult<IEnumerable<RebalancePreviewDto>>> RebalanceMaterial(
            int runId, [FromBody] RebalanceMaterialRequest req, [FromQuery] bool whatIf = true)
        {
            var run = await _db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @runId", new { runId });
            if (run is null) return NotFound();

            try
            {
                var res = await _db.DoGetDataSQLAsync<RebalancePreviewDto>(
                    "EXEC dbo.CC_sp_RebalanceMaterialQty @RunId=@r, @Month=@m, @DT1=@a, @DT2=@b, " +
                    "@MaterialCode=@mc, @FromParentCode=@fp, @ToParentCode=@tp, @Qty=@q, @WhatIf=@w",
                    new
                    {
                        r = runId, m = run.PeriodMonth, a = run.DateFrom, b = run.DateTo,
                        mc = req.MaterialCode, fp = req.FromParentCode, tp = req.ToParentCode,
                        q = req.Qty, w = whatIf
                    });

                return Ok(res);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RebalanceMaterial failed for run {RunId}", runId);
                return BadRequest(ex.Message);
            }
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

        /// <summary>
        /// آستانه‌ی یک قاعده رو کاربر تعیین می‌کند — مثلاً برای CHK-01
        /// (کاردکس منفی) که چون مقدارها گاهی به‌خاطر باقیمانده‌ی واقعیِ
        /// تبدیل واحد (نه خطای گرد کردن) دقیقاً صفر نمی‌شوند، آستانه‌ی
        /// خیلی سخت‌گیرانه نویز داده را هم منفی نشان می‌دهد.
        /// </summary>
        [HttpPut("rules/{ruleCode}/threshold")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Upd)]
        public async Task<IActionResult> UpdateRuleThreshold(string ruleCode, [FromBody] UpdateRuleThresholdRequest req)
        {
            var rows = await _db.DoExecuteSQLAsync(
                "UPDATE dbo.CC_CheckRule SET Threshold = @Threshold WHERE RuleCode = @ruleCode",
                new { ruleCode, req.Threshold });

            return rows > 0 ? NoContent() : NotFound();
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
                ORDER BY a.UnitId, a.SeqNo;

                SELECT   m.*,
                         tk.NAME AS KolName, tm.NAME AS MoinName, tt.NAME AS TafsiliName
                FROM     dbo.CC_UnitAcc m
                LEFT     JOIN dbo.TOTA_HES  tk ON tk.NUMBER  = m.HesKol
                LEFT     JOIN dbo.DETA_HES  tm ON tm.N_KOL   = m.HesKol AND tm.NUMBER  = m.HesMoin
                LEFT     JOIN dbo.TDETA_HES tt ON tt.N_KOL   = m.HesKol AND tt.NUMBER  = m.HesMoin
                                                AND tt.TNUMBER = m.HesTafsili
                ORDER BY m.UnitId, m.HesKol;";

            using var grid = await _db.DoGetDataSQLAsyncMultiple(sql);

            var units    = (await grid.ReadAsync<CostUnitDto>()).ToList();
            var anbars   = (await grid.ReadAsync<CostUnitAnbarDto>()).ToList();
            var accounts = (await grid.ReadAsync<CostUnitAccDto>()).ToList();

            foreach (var u in units)
            {
                u.Anbars = anbars.Where(a => a.UnitId == u.UnitId).ToList();
                u.Accounts = accounts.Where(a => a.UnitId == u.UnitId).ToList();
            }

            return Ok(units);
        }

        [HttpPost("units")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Inp)]
        public async Task<ActionResult<int>> CreateUnit([FromBody] UpsertUnitRequest req)
        {
            const string sql = @"
                INSERT dbo.CC_Unit (UnitName, Depatman, SplitMode, IsActive, SeqNo)
                OUTPUT inserted.UnitId
                VALUES (@UnitName, @Depatman, @SplitMode, @IsActive, @SeqNo)";

            return Ok(await _db.DoGetDataSQLAsyncSingle<int>(sql, req));
        }

        [HttpPut("units/{unitId:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Upd)]
        public async Task<IActionResult> UpdateUnit(int unitId, [FromBody] UpsertUnitRequest req)
        {
            const string sql = @"
                UPDATE dbo.CC_Unit
                   SET UnitName = @UnitName, Depatman = @Depatman, SplitMode = @SplitMode,
                       IsActive = @IsActive, SeqNo = @SeqNo
                 WHERE UnitId = @unitId";

            var rows = await _db.DoExecuteSQLAsync(sql,
                new { req.UnitName, req.Depatman, req.SplitMode, req.IsActive, req.SeqNo, unitId });

            return rows > 0 ? NoContent() : NotFound();
        }

        [HttpDelete("units/{unitId:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteUnit(int unitId)
        {
            var used = await _db.DoGetDataSQLAsyncSingle<int>(
                "SELECT COUNT(*) FROM dbo.CC_ConversionCost WHERE UnitId = @unitId",
                new { unitId });

            if (used > 0)
                return BadRequest("این واحد در نتیجهٔ اجراهای قبلی استفاده شده و قابل حذف نیست — می‌توانید غیرفعالش کنید.");

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM dbo.CC_UnitAcc WHERE UnitId = @unitId", new { unitId }, tx);
                    await conn.ExecuteAsync(
                        "DELETE FROM dbo.CC_UnitAnbar WHERE UnitId = @unitId", new { unitId }, tx);
                    await conn.ExecuteAsync(
                        "DELETE FROM dbo.CC_Unit WHERE UnitId = @unitId", new { unitId }, tx);
                });

                return NoContent();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ───────── انبارهای واحد ─────────

        [HttpPost("units/{unitId:int}/warehouses")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Inp)]
        public async Task<IActionResult> AddUnitWarehouse(
            int unitId, [FromBody] UpsertUnitAnbarRequest req)
        {
            const string sql = @"
                INSERT dbo.CC_UnitAnbar (UnitId, Anbar, AnbarRole, DoStockCount, SeqNo)
                VALUES (@unitId, @Anbar, @AnbarRole, @DoStockCount, @SeqNo)";

            try
            {
                await _db.DoExecuteSQLAsync(sql,
                    new { unitId, req.Anbar, req.AnbarRole, req.DoStockCount, req.SeqNo });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("units/{unitId:int}/warehouses/{anbar:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Upd)]
        public async Task<IActionResult> UpdateUnitWarehouse(
            int unitId, int anbar, [FromBody] UpsertUnitAnbarRequest req)
        {
            const string sql = @"
                UPDATE dbo.CC_UnitAnbar
                   SET AnbarRole = @AnbarRole, DoStockCount = @DoStockCount, SeqNo = @SeqNo
                 WHERE UnitId = @unitId AND Anbar = @anbar";

            var rows = await _db.DoExecuteSQLAsync(sql,
                new { unitId, anbar, req.AnbarRole, req.DoStockCount, req.SeqNo });

            return rows > 0 ? NoContent() : NotFound();
        }

        [HttpDelete("units/{unitId:int}/warehouses/{anbar:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteUnitWarehouse(int unitId, int anbar)
        {
            var rows = await _db.DoExecuteSQLAsync(
                "DELETE FROM dbo.CC_UnitAnbar WHERE UnitId = @unitId AND Anbar = @anbar",
                new { unitId, anbar });

            return rows > 0 ? NoContent() : NotFound();
        }

        // ───────── حساب‌های دستمزد/سربار واحد ─────────

        [HttpPost("units/{unitId:int}/accounts")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Inp)]
        public async Task<ActionResult<int>> AddUnitAccount(
            int unitId, [FromBody] UpsertUnitAccRequest req)
        {
            const string sql = @"
                INSERT dbo.CC_UnitAcc
                    (UnitId, HesKol, HesMoin, HesTafsili, CostKind, Ratio, IsActive, Note)
                OUTPUT inserted.Id
                VALUES
                    (@unitId, @HesKol, @HesMoin, @HesTafsili, @CostKind, @Ratio, @IsActive, @Note)";

            try
            {
                return Ok(await _db.DoGetDataSQLAsyncSingle<int>(sql,
                    new { unitId, req.HesKol, req.HesMoin, req.HesTafsili,
                          req.CostKind, req.Ratio, req.IsActive, req.Note }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("units/{unitId:int}/accounts/{accId:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Upd)]
        public async Task<IActionResult> UpdateUnitAccount(
            int unitId, int accId, [FromBody] UpsertUnitAccRequest req)
        {
            const string sql = @"
                UPDATE dbo.CC_UnitAcc
                   SET HesKol = @HesKol, HesMoin = @HesMoin, HesTafsili = @HesTafsili,
                       CostKind = @CostKind, Ratio = @Ratio, IsActive = @IsActive, Note = @Note
                 WHERE UnitId = @unitId AND Id = @accId";

            try
            {
                var rows = await _db.DoExecuteSQLAsync(sql,
                    new { unitId, accId, req.HesKol, req.HesMoin, req.HesTafsili,
                          req.CostKind, req.Ratio, req.IsActive, req.Note });

                return rows > 0 ? NoContent() : NotFound();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("units/{unitId:int}/accounts/{accId:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteUnitAccount(int unitId, int accId)
        {
            var rows = await _db.DoExecuteSQLAsync(
                "DELETE FROM dbo.CC_UnitAcc WHERE UnitId = @unitId AND Id = @accId",
                new { unitId, accId });

            return rows > 0 ? NoContent() : NotFound();
        }

        // ───────── نگاشت انبار به حساب موجودی (CHK-02) ─────────
        // TCOD_ANBAR ستون حسابداری ندارد و هر انبار زیر معین جداگانه‌ای
        // ثبت می‌شود — این نگاشت باید از تنظیمات وارد شود، نه هاردکد.

        [HttpGet("anbar-hes")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostAnbarHesDto>>> GetAnbarHes()
        {
            const string sql = @"
                SELECT   m.Anbar, n.NAMES AS AnbarName,
                         m.HesKol, m.HesMoin, m.Note,
                         tk.NAME AS KolName, tm.NAME AS MoinName
                FROM     dbo.CC_AnbarHes m
                LEFT     JOIN dbo.TCOD_ANBAR n ON n.CODE  = m.Anbar
                LEFT     JOIN dbo.TOTA_HES  tk ON tk.NUMBER = m.HesKol
                LEFT     JOIN dbo.DETA_HES  tm ON tm.N_KOL  = m.HesKol AND tm.NUMBER = m.HesMoin
                ORDER BY m.Anbar";

            return Ok(await _db.DoGetDataSQLAsync<CostAnbarHesDto>(sql));
        }

        [HttpPost("anbar-hes")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Inp)]
        public async Task<IActionResult> AddAnbarHes([FromBody] UpsertAnbarHesRequest req)
        {
            const string sql = @"
                INSERT dbo.CC_AnbarHes (Anbar, HesKol, HesMoin, Note)
                VALUES (@Anbar, @HesKol, @HesMoin, @Note)";

            try
            {
                await _db.DoExecuteSQLAsync(sql, new { req.Anbar, req.HesKol, req.HesMoin, req.Note });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("anbar-hes/{anbar:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Upd)]
        public async Task<IActionResult> UpdateAnbarHes(int anbar, [FromBody] UpsertAnbarHesRequest req)
        {
            const string sql = @"
                UPDATE dbo.CC_AnbarHes
                   SET HesKol = @HesKol, HesMoin = @HesMoin, Note = @Note
                 WHERE Anbar = @anbar";

            var rows = await _db.DoExecuteSQLAsync(sql,
                new { anbar, req.HesKol, req.HesMoin, req.Note });

            return rows > 0 ? NoContent() : NotFound();
        }

        [HttpDelete("anbar-hes/{anbar:int}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteAnbarHes(int anbar)
        {
            var rows = await _db.DoExecuteSQLAsync(
                "DELETE FROM dbo.CC_AnbarHes WHERE Anbar = @anbar", new { anbar });

            return rows > 0 ? NoContent() : NotFound();
        }

        // ───────── نرخ استاندارد دستمزد به تفکیک کالا (تغذیه‌ی
        // HEAD_MANF.IMBIBE_MANF در گام S07B — نگاه کنید CC_sp_S07B_SyncLaborRate) ─────────

        [HttpGet("labor-rates")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<CostLaborRateDto>>> GetLaborRates()
        {
            const string sql = @"
                SELECT   r.UnitId, u.UnitName, r.CODE AS Code, s.NAME AS ItemName,
                         r.Coefficient, r.OverheadCoefficient, r.IsFixed, r.Note
                FROM     dbo.CC_LaborAbsorptionRate r
                LEFT     JOIN dbo.CC_Unit  u ON u.UnitId = r.UnitId
                LEFT     JOIN dbo.STUF_DEF s ON s.CODE = r.CODE
                ORDER BY u.SeqNo, r.CODE";

            return Ok(await _db.DoGetDataSQLAsync<CostLaborRateDto>(sql));
        }

        [HttpPost("labor-rates")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Inp)]
        public async Task<IActionResult> AddLaborRate([FromBody] UpsertLaborRateRequest req)
        {
            const string sql = @"
                INSERT dbo.CC_LaborAbsorptionRate (UnitId, CODE, Coefficient, OverheadCoefficient, IsFixed, Note)
                VALUES (@UnitId, @Code, @Coefficient, @OverheadCoefficient, @IsFixed, @Note)";

            try
            {
                await _db.DoExecuteSQLAsync(sql, new { req.UnitId, req.Code, req.Coefficient, req.OverheadCoefficient, req.IsFixed, req.Note });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("labor-rates/{unitId:int}/{code}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Upd)]
        public async Task<IActionResult> UpdateLaborRate(int unitId, string code, [FromBody] UpsertLaborRateRequest req)
        {
            const string sql = @"
                UPDATE dbo.CC_LaborAbsorptionRate
                   SET Coefficient = @Coefficient, OverheadCoefficient = @OverheadCoefficient,
                       IsFixed = @IsFixed, Note = @Note
                 WHERE UnitId = @unitId AND CODE = @code";

            var rows = await _db.DoExecuteSQLAsync(sql,
                new { unitId, code, req.Coefficient, req.OverheadCoefficient, req.IsFixed, req.Note });

            return rows > 0 ? NoContent() : NotFound();
        }

        [HttpDelete("labor-rates/{unitId:int}/{code}")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteLaborRate(int unitId, string code)
        {
            var rows = await _db.DoExecuteSQLAsync(
                "DELETE FROM dbo.CC_LaborAbsorptionRate WHERE UnitId = @unitId AND CODE = @code",
                new { unitId, code });

            return rows > 0 ? NoContent() : NotFound();
        }

        // هر (واحد، کالا)یی که تا حالا در یک برگه‌ی تولید (TAG=9) ثبت شده
        // (طبق انبار محصولِ آن واحد) ولی هنوز ردیفی در جدول ضریب ندارد،
        // با Coefficient=NULL («هنوز بررسی نشده») ساخته می‌شود — کاربر فقط
        // عدد ضریب هر ردیف را پر می‌کند، خودِ کالا/واحد را دستی اضافه نمی‌کند.
        [HttpPost("labor-rates/sync-from-formulas")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.Inp)]
        public async Task<ActionResult<int>> SyncLaborRatesFromFormulas()
        {
            const string sql = @"
                INSERT dbo.CC_LaborAbsorptionRate (UnitId, CODE, Coefficient, Note)
                SELECT DISTINCT ua.UnitId, hm.CODE, NULL, NULL
                FROM   dbo.HEAD_LST     h
                JOIN   dbo.INVO_LST     pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                JOIN   dbo.HEAD_MANF    hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                JOIN   dbo.CC_UnitAnbar ua ON ua.Anbar  = pl.ANBAR AND ua.AnbarRole = 3
                JOIN   dbo.CC_Unit      u  ON u.UnitId  = ua.UnitId AND u.IsActive = 1
                WHERE  h.TAG = 9
                  AND  NOT EXISTS (
                            SELECT 1 FROM dbo.CC_LaborAbsorptionRate r
                            WHERE r.UnitId = ua.UnitId AND r.CODE = hm.CODE
                       )";

            var rows = await _db.DoExecuteSQLAsync(sql);
            return Ok(rows);
        }

        // برای انتخاب کالا از روی دیتابیس واقعی، نه تایپ دستی کد — تا
        // احتمال خطای تایپی از بین برود (عیناً الگوی جستجوی حساب کل/معین بالا).
        [HttpGet("items/search")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<ItemLookupDto>>> SearchItems(
            [FromQuery] string? q = null)
        {
            const string sql = @"
                SELECT TOP 30 CODE AS Code, NAME AS Name
                FROM   dbo.STUF_DEF
                WHERE  @q IS NULL OR CODE LIKE @q + '%' OR NAME LIKE '%' + @q + '%'
                ORDER BY CODE";

            return Ok(await _db.DoGetDataSQLAsync<ItemLookupDto>(sql, new { q }));
        }

        // ───────── جستجوی زنجیره‌ای حساب (کل/معین/تفصیلی) ─────────
        // برای انتخاب حساب دستمزد/سربار هر واحد از روی دیتابیس واقعی،
        // نه تایپ دستی کد — تا احتمال خطای تایپی از بین برود.

        [HttpGet("accounts/kol")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<AccountLookupDto>>> SearchKol(
            [FromQuery] string? q = null)
        {
            const string sql = @"
                SELECT TOP 30 NUMBER AS Code, NAME AS Name
                FROM   dbo.TOTA_HES
                WHERE  @q IS NULL OR CAST(NUMBER AS NVARCHAR(20)) LIKE @q + '%' OR NAME LIKE '%' + @q + '%'
                ORDER BY NUMBER";

            return Ok(await _db.DoGetDataSQLAsync<AccountLookupDto>(sql, new { q }));
        }

        [HttpGet("accounts/moin")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<AccountLookupDto>>> SearchMoin(
            [FromQuery] int kol, [FromQuery] string? q = null)
        {
            const string sql = @"
                SELECT TOP 30 NUMBER AS Code, NAME AS Name
                FROM   dbo.DETA_HES
                WHERE  N_KOL = @kol
                  AND  (@q IS NULL OR CAST(NUMBER AS NVARCHAR(20)) LIKE @q + '%' OR NAME LIKE '%' + @q + '%')
                ORDER BY NUMBER";

            return Ok(await _db.DoGetDataSQLAsync<AccountLookupDto>(sql, new { kol, q }));
        }

        [HttpGet("accounts/tafsili")]
        [Pay2Authorize(CostForms.Settings, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<AccountLookupDto>>> SearchTafsili(
            [FromQuery] int kol, [FromQuery] int moin, [FromQuery] string? q = null)
        {
            const string sql = @"
                SELECT TOP 30 TNUMBER AS Code, NAME AS Name
                FROM   dbo.TDETA_HES
                WHERE  N_KOL = @kol AND NUMBER = @moin
                  AND  (@q IS NULL OR CAST(TNUMBER AS NVARCHAR(20)) LIKE @q + '%' OR NAME LIKE '%' + @q + '%')
                ORDER BY TNUMBER";

            return Ok(await _db.DoGetDataSQLAsync<AccountLookupDto>(sql, new { kol, moin, q }));
        }
    }
}
