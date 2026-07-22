using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using Safir.Server.Reports;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using Safir.Shared.Models.Salary.Reports;
using Safir.Shared.Utility;
using System.Security.Claims;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/run")]
    [Authorize]
    public class Pay2RunController : ControllerBase
    {
        private readonly IDatabaseService _db;
        public Pay2RunController(IDatabaseService db) => _db = db;

        [HttpGet("period-info")]
        public async Task<ActionResult<Pay2PeriodDto>> GetPeriodInfo([FromQuery] int wsId, [FromQuery] long periodDate)
        {
            var sql = "SELECT * FROM PAY2_PERIOD WHERE WS_ID = @wsId AND PERIOD_DATE = @periodDate";
            var period = await _db.DoGetDataSQLAsyncSingle<Pay2PeriodDto>(sql, new { wsId, periodDate });
            return Ok(period);
        }

        [HttpGet("latest")]
        public async Task<ActionResult<Pay2RunDto>> GetLatestRun([FromQuery] int perId)
        {
            var sql = "SELECT TOP 1 * FROM PAY2_RUN WHERE PER_ID = @perId AND IS_LATEST = 1 ORDER BY RUN_NO DESC";
            var run = await _db.DoGetDataSQLAsyncSingle<Pay2RunDto>(sql, new { perId });
            return Ok(run);
        }

        [HttpGet("{runId:int}/lines")]
        public async Task<ActionResult<Pay2RunResultDto>> GetRunLines(int runId)
        {
            var result = new Pay2RunResultDto();

            // 1. استخراج ستون‌های پویا (فقط آیتم‌هایی که در این ماه برای حداقل یک نفر محاسبه شده‌اند)
            string colSql = @"
                SELECT DISTINCT D.ITEM_ID, I.ITEM_CODE, I.ITEM_NAME, I.SORT_ORDER
                FROM PAY2_RUN_DETAIL D
                INNER JOIN PAY2_ITEM_DEF I ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER";

            result.Columns = (await _db.DoGetDataSQLAsync<Pay2RunColumnDto>(colSql, new { runId })).ToList();

            // 2. استخراج ردیف‌های اصلی فیش حقوقی
            string lineSql = @"
                SELECT L.*, E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME
                FROM PAY2_RUN_LINE L
                INNER JOIN PAY2_EMPLOYEE E ON L.EMP_ID = E.EMP_ID
                WHERE L.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            result.Lines = (await _db.DoGetDataSQLAsync<Pay2RunLineDto>(lineSql, new { runId })).ToList();

            // 3. استخراج مبالغ ریز (Details) و اتصال آن‌ها به ردیف‌ها
            string detSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D
                INNER JOIN PAY2_ITEM_DEF I ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId";

            // استفاده از یک کلاس داخلی موقت برای خواندن سریع داده‌ها از Dapper
            var details = await _db.DoGetDataSQLAsync<RunDetailFlat>(detSql, new { runId });

            // گروه‌بندی داده‌ها بر اساس شناسه پرسنل برای پردازش سریع در RAM
            var groupedDetails = details
                .GroupBy(x => x.EMP_ID)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(x => x.ITEM_CODE, x => x.AMOUNT)
                );

            foreach (var line in result.Lines)
            {
                if (groupedDetails.TryGetValue(line.EMP_ID, out var empDetails))
                {
                    line.Details = empDetails;
                }
            }

            return Ok(result);
        }

        private class RunDetailFlat
        {
            public int EMP_ID { get; set; }
            public string ITEM_CODE { get; set; } = "";
            public long AMOUNT { get; set; }
        }

        // ===================================================================
        // خروجی اکسلِ تحلیلیِ فرمول‌دار — کل اجرا
        // فایل XLSX که در آن «هر عددِ فیش، یک فرمولِ واقعی اکسل» است و زنجیرهٔ
        // محاسبهٔ موتور (SP_PAY2_CALC_RUN) را بازسازی می‌کند. عملیات Read-Only است.
        // ===================================================================
        [HttpGet("{runId:int}/excel-audit")]
        public async Task<IActionResult> GetExcelAudit(int runId)
        {
            var service = new Pay2ExcelAuditService(_db);
            var result = await service.BuildAsync(runId);
            if (result == null)
                return NotFound("داده‌ای برای خروجی اکسل تحلیلی یافت نشد.");

            return File(result.Value.Bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        }

        // ===================================================================
        // فیش حقوقی (PDF) — یک پرسنل در یک اجرا
        // داده آماده می‌شود، PayslipReportDto پر می‌شود و با QuestPDF رندر می‌گردد.
        // قرارداد داده: مزایا از آیتم‌های نوع ۱و۲ (= GROSS_PAY) و کسورات از فیلدهای
        // صریح (= TOTAL_DED) تا هیچ آیتمی دوبار شمرده نشود و جمع مزایا − جمع کسورات = خالص.
        // ===================================================================
        [HttpGet("{runId:int}/employee/{empId:int}/payslip")]
        public async Task<IActionResult> GetPayslip(int runId, int empId, [FromQuery] bool isOfficial = false)
        {
            // ۱. هدر فیش (مشخصات پرسنل/دوره/کارگاه + جمع‌های صریح)
            const string headSql = @"
                SELECT
                    RL.RUN_ID, RL.EMP_ID, RL.WORK_DAYS,
                    RL.GROSS_PAY, RL.INS_BASE, RL.INS_WORKER, RL.TAX_AMOUNT,
                    RL.LOAN_DED, RL.ADVANCE_DED, RL.OTHER_DED, RL.TOTAL_DED, RL.NET_PAY,
                    RL.LEAVE_BAL_DAYS, RL.LOAN_BALANCE,
                    E.EMP_CODE, (E.LAST_NAME + N' ' + E.FIRST_NAME) AS FULL_NAME, E.NATIONAL_CODE,
                    J.JOB_NAME,
                    P.PERIOD_DATE,
                    W.WS_NAME
                FROM PAY2_RUN_LINE RL
                INNER JOIN PAY2_RUN      R ON RL.RUN_ID = R.RUN_ID
                INNER JOIN PAY2_PERIOD   P ON R.PER_ID  = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W ON P.WS_ID   = W.WS_ID
                INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
                LEFT  JOIN PAY2_JOB      J ON E.JOB_ID  = J.JOB_ID
                WHERE RL.RUN_ID = @runId AND RL.EMP_ID = @empId";

            var head = await _db.DoGetDataSQLAsyncSingle<PayslipHeadRow>(headSql, new { runId, empId });
            if (head == null)
                return NotFound("فیش حقوقی برای این پرسنل در این اجرا یافت نشد.");

            // ۲. مزایا = فقط آیتم‌های نوع پرداختی (ITEM_TYPE 1,2)؛ مجموع آن‌ها = GROSS_PAY
            // فیش همیشه مسیر پرداخت واقعی (رسمی) را نشان می‌دهد. پارامتر قدیمی برای
            // سازگاری API نگه داشته شده، اما اجازه تغییر ریل محاسبه را ندارد.
            const string earnSql = @"
                SELECT I.ITEM_NAME AS Title, D.AMOUNT AS Amount
                FROM PAY2_RUN_DETAIL D
                INNER JOIN PAY2_ITEM_DEF I ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId AND D.EMP_ID = @empId
                  AND COALESCE(D.ITEM_TYPE_SNAP,I.ITEM_TYPE) IN (1,2)
                  AND D.AMOUNT <> 0
                  AND (COALESCE(D.ITEM_CODE_SNAP,I.ITEM_CODE) <> 'BASE_SAL'
                       OR NOT EXISTS (SELECT 1 FROM PAY2_RUN_DETAIL X
                                      INNER JOIN PAY2_ITEM_DEF XI ON XI.ITEM_ID=X.ITEM_ID
                                      WHERE X.RUN_ID=D.RUN_ID AND X.EMP_ID=D.EMP_ID
                                        AND COALESCE(X.ITEM_CODE_SNAP,XI.ITEM_CODE)='BASE_SAL_B' AND X.AMOUNT<>0))
                ORDER BY I.SORT_ORDER";

            var earnings = (await _db.DoGetDataSQLAsync<PayslipLineDto>(earnSql, new { runId, empId })).ToList();

            // ۳. ساخت DTO آمادهٔ چاپ
            var dto = new PayslipReportDto
            {
                EmployeeName = head.FULL_NAME ?? "",
                EmployeeCode = head.EMP_CODE ?? "",
                NationalCode = head.NATIONAL_CODE,
                JobTitle = head.JOB_NAME,
                PeriodDate = head.PERIOD_DATE,
                PeriodTitle = BuildPeriodTitle(head.PERIOD_DATE),
                WorkshopName = head.WS_NAME ?? "",
                WorkDays = head.WORK_DAYS,
                Earnings = earnings,
                InsBase = head.INS_BASE,
                LeaveBalanceDays = head.LEAVE_BAL_DAYS,
                LoanBalance = head.LOAN_BALANCE,
                NetPay = head.NET_PAY,
                PrintDate = FormatShamsi(CL_Tarikh.GetCurrentPersianDateAsLong())
            };

            // کسورات از فیلدهای صریح (مجموعشان = TOTAL_DED) — فقط اقلام غیرصفر
            void AddDed(string title, long amount)
            {
                if (amount != 0)
                    dto.Deductions.Add(new PayslipLineDto { Title = title, Amount = amount });
            }
            AddDed("کسر بیمه کارگر", head.INS_WORKER);
            AddDed("کسر مالیات", head.TAX_AMOUNT);
            AddDed("قسط وام", head.LOAN_DED);
            AddDed("مساعده", head.ADVANCE_DED);
            AddDed("سایر کسورات", head.OTHER_DED);

            long rounding = head.NET_PAY - (head.GROSS_PAY - head.TOTAL_DED);
            if (rounding > 0)
                dto.Earnings.Add(new PayslipLineDto { Title = "تعدیل (گرد کردن)", Amount = rounding });
            else if (rounding < 0)
                dto.Deductions.Add(new PayslipLineDto { Title = "تعدیل (گرد کردن)", Amount = -rounding });

            dto.NetPayInWords = CL_HESABDARI.ALPHANUM(dto.NetPay) + " ریال";

            // ۴. تولید PDF
            var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
            byte[] pdfBytes = new PayslipDocument(dto, env).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"Payslip_{dto.EmployeeCode}.pdf");
        }

        // کلاس کمکی Dapper برای هدر فیش
        private class PayslipHeadRow
        {
            public int RUN_ID { get; set; }
            public int EMP_ID { get; set; }
            public decimal WORK_DAYS { get; set; }
            public long GROSS_PAY { get; set; }
            public long INS_BASE { get; set; }
            public long INS_WORKER { get; set; }
            public long TAX_AMOUNT { get; set; }
            public long LOAN_DED { get; set; }
            public long ADVANCE_DED { get; set; }
            public long OTHER_DED { get; set; }
            public long TOTAL_DED { get; set; }
            public long NET_PAY { get; set; }
            public decimal? LEAVE_BAL_DAYS { get; set; }
            public long? LOAN_BALANCE { get; set; }
            public string? EMP_CODE { get; set; }
            public string? FULL_NAME { get; set; }
            public string? NATIONAL_CODE { get; set; }
            public string? JOB_NAME { get; set; }
            public long PERIOD_DATE { get; set; }
            public string? WS_NAME { get; set; }
        }

        private static readonly string[] PersianMonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        // periodDate به فرم YYYYMM00 ⇒ «نام‌ماه YYYY»
        private static string BuildPeriodTitle(long periodDate)
        {
            long year = periodDate / 10000;
            int month = (int)((periodDate / 100) % 100);
            string monthName = (month >= 1 && month <= 12) ? PersianMonthNames[month - 1] : "";
            return string.IsNullOrEmpty(monthName) ? year.ToString() : $"{monthName} {year}";
        }

        // YYYYMMDD ⇒ «YYYY/MM/DD»
        private static string FormatShamsi(long dateLong)
        {
            if (dateLong <= 0) return string.Empty;
            string d = dateLong.ToString();
            return d.Length == 8 ? $"{d[..4]}/{d.Substring(4, 2)}/{d.Substring(6, 2)}" : d;
        }

        [HttpPost("calculate")]
        public async Task<ActionResult<int>> CalculateRun([FromBody] Pay2RunCalcRequest request)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                int newRunId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var periodStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @PER_ID",
                        new { request.PER_ID }, tran);

                    if (periodStatus == null || periodStatus == 1)
                        throw new InvalidOperationException("دوره کارکرد هنوز باز است. لطفاً ابتدا در تب کارکرد، دکمه 'بستن کارکرد' را بزنید.");

                    var latestRunStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_RUN WITH (UPDLOCK) WHERE PER_ID = @PER_ID AND IS_LATEST = 1",
                        new { request.PER_ID }, tran);

                    if (latestRunStatus >= 2)
                        throw new InvalidOperationException("برای این دوره فیش حقوقیِ تأیید شده وجود دارد. امکان بازمحاسبه نیست.");

                    // 🚀 فیکس امنیتی: هرگز به کلاینت اعتماد نکن. سرور خودش وضعیت را بررسی می‌کند!
                    bool isReRun = latestRunStatus.HasValue;

                    var sql = @"
                        DECLARE @NewId INT;
                        EXEC SP_PAY2_CALC_RUN 
                            @WS_ID = @WS_ID, 
                            @PER_ID = @PER_ID, 
                            @PAYROLL_N_S = @PAYROLL_N_S, 
                            @CALC_BY = @UserCod, 
                            @IS_RERUN = @IsReRun, 
                            @NEW_RUN_ID = @NewId OUTPUT;
                        SELECT @NewId;";

                    var p = new DynamicParameters(request);
                    p.Add("UserCod", userCod);
                    p.Add("IsReRun", isReRun); // پاس دادن متغیر امنِ سرور

                    return await conn.QuerySingleAsync<int>(sql, p, tran, commandTimeout: 180);
                });

                return Ok(newRunId);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطای موتور محاسبه: " + ex.Message); }
        }

        [HttpPut("{runId:int}/revert")]
        public async Task<IActionResult> RevertRun(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync("EXEC SP_PAY2_REVERT_RUN @RUN_ID = @runId, @REVERT_BY = @userCod", new { runId, userCod }, tran);
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpPut("{runId:int}/finalize")]
        public async Task<IActionResult> FinalizeRun(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync("EXEC SP_PAY2_FINALIZE_RUN @RUN_ID = @runId, @FINAL_BY = @userCod", new { runId, userCod }, tran);
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        public class RunDeedInfoRow
        {
            public byte STATUS { get; set; }
            public int PER_ID { get; set; }
            public double? DEED_ID_SAL { get; set; }
            public byte? DEED_MODE { get; set; }
            public byte DEFAULT_DEED_MODE { get; set; }
        }

        [HttpGet("{runId:int}/preview-deed")]
        public async Task<ActionResult<Pay2DeedPreviewDto>> PreviewDeed(int runId, [FromQuery] byte? overrideMode = null)
        {
            var result = new Pay2DeedPreviewDto();
            try
            {
                var runInfo = await _db.DoGetDataSQLAsyncSingle<RunDeedInfoRow>(
                    @"SELECT R.STATUS, R.PER_ID, R.DEED_ID_SAL, R.DEED_MODE, W.DEFAULT_DEED_MODE 
                      FROM PAY2_RUN R 
                      INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID 
                      INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID 
                      WHERE R.RUN_ID = @runId", new { runId });

                if (runInfo == null)
                    return NotFound("محاسبه یافت نشد.");

                byte effectiveMode = overrideMode ?? runInfo.DEED_MODE ?? runInfo.DEFAULT_DEED_MODE;

                result.ModeUsed = (Pay2DeedMode)effectiveMode;
                result.ModeTitle = effectiveMode == 1 ? "سند کلی ـ روش فعلی" : "سند نیمه‌تفصیلی اشخاص";

                var articles = await _db.DoGetDataSQLAsync<Pay2DeedArticleDto>(
                    "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @DEED_MODE = @mode",
                    new { runId, mode = effectiveMode });

                result.Articles = articles.ToList();

                foreach (var art in result.Articles)
                {
                    if (art.BED < 0 || art.BES < 0)
                        result.ValidationErrors.Add($"خطای ساختاری: مبلغ منفی در آرتیکل (حساب: {art.HES_CODE}, بدهکار: {art.BED}, بستانکار: {art.BES}). حقوق این شخص نیازمند بررسی است.");

                    var parsed = Pay2AccountHelper.Parse(art.HES_CODE, art.ACC_KEY);
                    if (!parsed.IsValid)
                    {
                        result.ValidationErrors.Add($"خطا در قالب‌بندی حساب {art.ACC_KEY} ({(art.EmployeeName ?? "تجمیعی")}): {parsed.ErrorMessage}");
                    }
                    // 🚀 گارد امنیتی جدید: جلوگیری از ورود حساب ناقص (کمتر از ۳ سطح)
                    else if (parsed.Account!.HesT == null)
                    {
                        result.ValidationErrors.Add($"حساب {art.ACC_KEY} ({(art.EmployeeName ?? "تجمیعی")}) ناقص است ({art.HES_CODE}). ثبت حداقل ۳ سطح (کل-معین-تفصیلی) در حسابداری حقوق الزامی است.");
                    }
                }

                if (!result.IsBalanced)
                    result.ValidationErrors.Add($"سند تراز نیست. جمع بدهکار: {result.TotalDebit:N0}، جمع بستانکار: {result.TotalCredit:N0}، اختلاف: {result.Difference:N0}");

                return Ok(result);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                result.ValidationErrors.Add(ex.Message);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطای سرور: " + ex.Message);
            }
        }

        [HttpPost("{runId:int}/generate-deed")]
        public async Task<IActionResult> GenerateDeed(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();
            var userName = User.Identity?.Name ?? "System";

            try
            {
                var runInfo = await _db.DoGetDataSQLAsyncSingle<RunDeedInfoRow>(
                    @"SELECT R.STATUS, R.PER_ID, R.DEED_ID_SAL, R.DEED_MODE, W.DEFAULT_DEED_MODE 
                      FROM PAY2_RUN R 
                      INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID 
                      INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                      WHERE R.RUN_ID = @runId", new { runId });

                if (runInfo == null)
                    return BadRequest("محاسبه یافت نشد.");

                byte status = runInfo.STATUS;
                if (status != 2 && status != 3)
                    return BadRequest("اجرا باید در وضعیت 'تأیید نهایی' یا 'سند صادر شده' باشد.");

                int perId = runInfo.PER_ID;
                byte effectiveMode = runInfo.DEED_MODE ?? runInfo.DEFAULT_DEED_MODE;

                var periodInfo = await _db.DoGetDataSQLAsyncSingle<dynamic>(
                    "SELECT PERIOD_DATE, DEED_N_S_PAY FROM PAY2_PERIOD WHERE PER_ID = @perId", new { perId });

                long periodDate = (long)periodInfo.PERIOD_DATE;
                double? existingNs = (double?)periodInfo.DEED_N_S_PAY;

                var articles = (await _db.DoGetDataSQLAsync<Pay2DeedArticleDto>(
                    "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @CALC_BY = @userCod, @DEED_MODE = @mode",
                    new { runId, userCod, mode = effectiveMode })).ToList();

                long sumBed = articles.Sum(x => x.BED);
                long sumBes = articles.Sum(x => x.BES);
                if (sumBed != sumBes)
                    return BadRequest($"سند تراز نیست! بدهکار: {sumBed:N0}، بستانکار: {sumBes:N0}");

                var parsedArticles = new List<(int Radif, Pay2ParsedAccount Acc, string Sharh, double Bed, double Bes)>();
                int radif = 1;

                foreach (var art in articles)
                {
                    if (art.BED < 0 || art.BES < 0)
                        return BadRequest($"آرتیکل با مبلغ منفی مجاز نیست (حساب: {art.HES_CODE}).");

                    var parsedAcc = Pay2AccountHelper.Parse(art.HES_CODE, art.ACC_KEY);
                    if (!parsedAcc.IsValid)
                        return BadRequest($"خطا در حساب {art.ACC_KEY}: {parsedAcc.ErrorMessage}");

                    // 🚀 گارد امنیتی جدید (Hard Block)
                    if (parsedAcc.Account!.HesT == null)
                        return BadRequest($"خطا در صدور سند: حساب {art.ACC_KEY} ناقص است ({art.HES_CODE}). دیتابیس حسابداری اجازه ثبت حساب کمتر از ۳ سطح (تفصیلی) را نمی‌دهد.");

                    parsedArticles.Add((radif++, parsedAcc.Account!, art.SHARH, (double)art.BED, (double)art.BES));
                }

                long deedDate = Safir.Shared.Utility.CL_Tarikh.GetPersianMonthEndAsLong(periodDate);
                string hedSharh = $"سند حقوق و دستمزد دوره {periodDate}";

                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var lockCheck = await conn.QuerySingleAsync<byte>(
                        "SELECT STATUS FROM PAY2_RUN WITH (UPDLOCK) WHERE RUN_ID = @runId", new { runId }, tran);

                    if (lockCheck != 2 && lockCheck != 3)
                        throw new InvalidOperationException("وضعیت اجرا در حین پردازش تغییر کرده است.");

                    double targetNs;

                    if (status == 3 && existingNs.HasValue && existingNs.Value > 0)
                    {
                        targetNs = existingNs.Value;

                        var okfStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                            "SELECT OKF FROM DEED_HED WITH (UPDLOCK) WHERE N_S = @N_S", new { N_S = targetNs }, tran);

                        if (okfStatus.HasValue && okfStatus.Value != 1)
                            throw new InvalidOperationException("این سند در سیستم حسابداری بررسی و قطعی شده است. امکان بازصدور وجود ندارد.");

                        await conn.ExecuteAsync("DELETE FROM DEED_DTL WHERE N_S = @N_S", new { N_S = targetNs }, tran);
                        await conn.ExecuteAsync(@"
                            UPDATE DEED_HED SET DATE_S = @DATE_S, SHARH_S = @SHARH, NO_S = 11, USER_NAME = @USER, UID = @UID WHERE N_S = @N_S",
                            new { N_S = targetNs, DATE_S = deedDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);
                    }
                    else
                    {
                        await conn.ExecuteAsync("EXEC sp_getapplock @Resource = 'DeedNumberAllocation', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", null, tran);

                        targetNs = (await conn.QuerySingleOrDefaultAsync<double?>("SELECT MAX(N_S) FROM DEED_HED WITH (UPDLOCK)", null, tran) ?? 0) + 1;

                        await conn.ExecuteAsync(@"
                            INSERT INTO DEED_HED (N_S, DATE_S, SHARH_S, NO_S, USER_NAME, OKF, CRT, UID)
                            VALUES (@N_S, @DATE_S, @SHARH, 11, @USER, 1, GETDATE(), @UID)",
                            new { N_S = targetNs, DATE_S = deedDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);
                    }

                    var finalDetailsToInsert = parsedArticles.Select(a => new
                    {
                        N_S = targetNs,
                        RADIF = a.Radif,
                        HES_K = a.Acc.HesK,
                        HES_M = a.Acc.HesM,
                        HES_T = a.Acc.HesT!.Value, // 🚀 اصلاح شد: مقدار واقعی جایگزین شد، بدون ?? 0
                        HES_T2 = a.Acc.HesT2,
                        HES_T3 = a.Acc.HesT3,
                        HES_T4 = a.Acc.HesT4,
                        HES = a.Acc.FullCode,
                        SHARH = a.Sharh,
                        BED = a.Bed,
                        BES = a.Bes,
                        UID = userCod
                    }).ToList();

                    const string insertSql = @"
INSERT INTO DEED_DTL (N_S, RADIF, HES_K, HES_M, HES_T, HES_T2, HES_T3, HES_T4, HES, SHARH, BED, BES, CRT, UID)
VALUES (@N_S, @RADIF, @HES_K, @HES_M, @HES_T, @HES_T2, @HES_T3, @HES_T4, @HES, @SHARH, @BED, @BES, GETDATE(), @UID)";

                    await conn.ExecuteAsync(insertSql, finalDetailsToInsert, tran);

                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN SET STATUS = 3, DEED_ID_SAL = @deedId, DEED_MODE = @mode, DEED_GENERATOR_VERSION = 1 WHERE RUN_ID = @runId;
                        UPDATE PAY2_PERIOD SET STATUS = 4, DEED_N_S_PAY = @targetNs WHERE PER_ID = @perId;",
                        new { runId, deedId = (int)targetNs, targetNs, perId, mode = effectiveMode }, tran);
                });

                return Ok();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در صدور سند: " + ex.Message); }
        }

        [HttpPut("{runId:int}/unfinalize-deed")]
        public async Task<IActionResult> UnfinalizeDeed(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var runInfo = await conn.QuerySingleOrDefaultAsync<RunDeedInfoRow>(
                        @"SELECT R.STATUS, R.PER_ID, R.DEED_ID_SAL, R.DEED_MODE, W.DEFAULT_DEED_MODE 
                          FROM PAY2_RUN R 
                          INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID 
                          INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                          WHERE R.RUN_ID = @runId", new { runId }, tran);

                    if (runInfo == null)
                        throw new InvalidOperationException("محاسبه‌ای با این شناسه یافت نشد.");

                    byte status = runInfo.STATUS;
                    if (status != 3)
                        throw new InvalidOperationException("این عملیات فقط برای اجراهایی که سند صادر کرده‌اند مجاز است.");

                    var periodInfo = await conn.QuerySingleOrDefaultAsync<dynamic>(
                        "SELECT DEED_N_S_PAY FROM PAY2_PERIOD WHERE PER_ID = @perId", new { perId = runInfo.PER_ID }, tran);

                    double? deedNs = (double?)periodInfo.DEED_N_S_PAY;

                    if (deedNs.HasValue && deedNs.Value > 0)
                    {
                        var okfStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                            "SELECT OKF FROM DEED_HED WITH (UPDLOCK) WHERE N_S = @N_S",
                            new { N_S = deedNs.Value }, tran);

                        if (okfStatus.HasValue && okfStatus.Value != 1)
                            throw new InvalidOperationException("این سند در سیستم حسابداری بررسی و قطعی شده است. امکان لغو صدور سند وجود ندارد.");

                        await conn.ExecuteAsync("DELETE FROM DEED_DTL WHERE N_S = @N_S", new { N_S = deedNs.Value }, tran);
                        await conn.ExecuteAsync("DELETE FROM DEED_HED WHERE N_S = @N_S", new { N_S = deedNs.Value }, tran);
                    }

                    int perId = runInfo.PER_ID;

                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN
                        SET STATUS = 2, DEED_ID_SAL = NULL,
                            NOTES = SUBSTRING(ISNULL(NOTES,'') + N' | DeedUnfinalized by ' + CAST(@userCod AS NVARCHAR), 1, 300)
                        WHERE RUN_ID = @runId;

                        UPDATE PAY2_PERIOD
                        SET STATUS = 3, DEED_N_S_PAY = NULL
                        WHERE PER_ID = @perId;",
                        new { runId, perId, userCod }, tran);
                });

                return Ok();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در لغو صدور سند: " + ex.Message); }
        }

        // ===================================================================
        // چاپ لیست بیمه تامین اجتماعی (برای یک ماه یا تمام ماه‌های کارگاه)
        // اگر runId = 0 باشد و wsId ارسال شود، گزارش تجمیعی کل سال/ماه‌ها صادر می‌شود
        // ===================================================================
        [HttpGet("{runId:int}/insurance-report")]
        public async Task<IActionResult> GetInsuranceReportPdf(int runId, [FromQuery] int wsId = 0)
        {
            try
            {
                var reportDto = new InsuranceReportDto();
                List<int> targetRunIds = new List<int>();

                // ۱. پیدا کردن Run ها (یا یک دانه، یا همه ماه‌های تایید شده یک کارگاه)
                if (runId > 0)
                {
                    targetRunIds.Add(runId);
                }
                else if (wsId > 0)
                {
                    // گرفتن تمام Runهای تایید شده (Status >= 2) برای این کارگاه
                    var runsSql = @"
                        SELECT R.RUN_ID 
                        FROM PAY2_RUN R
                        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                        WHERE P.WS_ID = @wsId AND R.IS_LATEST = 1 AND R.STATUS >= 2
                        ORDER BY P.PERIOD_DATE ASC"; // به ترتیب ماه

                    targetRunIds = (await _db.DoGetDataSQLAsync<int>(runsSql, new { wsId })).ToList();

                    if (!targetRunIds.Any())
                        return NotFound("هیچ ماهِ محاسبه و تایید شده‌ای برای این کارگاه یافت نشد.");
                }
                else
                {
                    return BadRequest("پارامترهای ورودی نامعتبر است.");
                }

                // گرفتن اطلاعات هدر از اولین Run موجود
                int firstRunId = targetRunIds.First();
                const string headSql = @"
                    SELECT 
                        P.PERIOD_DATE, W.WS_CODE, W.WS_NAME, W.EMPLOYER_NAME, 
                        W.ADDRESS, W.SSO_BRANCH
                    FROM PAY2_RUN R
                    INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                    INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                    WHERE R.RUN_ID = @firstRunId";

                var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { firstRunId });
                if (head == null) return NotFound("اطلاعات کارگاه/دوره یافت نشد.");

                long periodDate = (long)head.PERIOD_DATE;
                string year = (periodDate / 10000).ToString();
                int month = (int)((periodDate / 100) % 100);
                string[] monthNames = { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

                string monthName = runId > 0
                    ? ((month >= 1 && month <= 12) ? monthNames[month - 1] : month.ToString())
                    : "تجمیعی تمام ماه‌ها";

                reportDto = new InsuranceReportDto
                {
                    WorkshopCode = head.WS_CODE?.ToString() ?? "",
                    WorkshopName = head.WS_NAME?.ToString() ?? "",
                    EmployerName = head.EMPLOYER_NAME?.ToString() ?? "",
                    Address = head.ADDRESS?.ToString() ?? "",
                    BranchName = head.SSO_BRANCH?.ToString() ?? "",
                    PeriodYear = year,
                    PeriodMonthName = monthName
                };

                int rowIndex = 1;

                // پردازش تمام Runها (یک یا چندتا)
                foreach (var currentRunId in targetRunIds)
                {
                    // اگر تجمیعی است، عنوان ماه را به نام پرسنل اضافه می‌کنیم تا مشخص شود
                    string monthLabel = "";
                    if (runId == 0)
                    {
                        var pDateSql = "SELECT P.PERIOD_DATE FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE R.RUN_ID = @currentRunId";
                        var pDate = await _db.DoGetDataSQLAsyncSingle<long>(pDateSql, new { currentRunId });
                        int m = (int)((pDate / 100) % 100);
                        monthLabel = $" [{(m >= 1 && m <= 12 ? monthNames[m - 1] : m.ToString())}]";
                    }

                    var lines = (await _db.DoGetDataSQLAsync<dynamic>(
                        Safir.Server.Services.Pay2PayrollSnapshotQuery.Sql, new { runId = currentRunId })).ToList();
                    if (lines.Any(x => !(bool)x.HAS_NOMINAL_RAIL || !(bool)x.HAS_COMPLETE_NOMINAL_SNAPSHOT))
                        return UnprocessableEntity("خروجی قانونی ممکن نیست: Snapshot کامل ریل اسمی برای حداقل یک پرسنل وجود ندارد.");

                    foreach (var line in lines)
                    {
                        decimal workDays = (decimal)line.INSURANCE_DAYS;
                        long baseMonthly = (long)line.BASE_WAGE_MONTHLY;
                        long seniorityMonthly = (long)line.SENIORITY_MONTHLY;
                        long baseDaily = workDays > 0 ? (long)Math.Round(baseMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                        long seniorityDaily = workDays > 0 ? (long)Math.Round(seniorityMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                        long monthlyWage = baseMonthly + seniorityMonthly;
                        long otherBenefits = (long)line.DISPLAY_OTHER_BENEFITS;

                        reportDto.Rows.Add(new InsuranceEmployeeRowDto
                        {
                            RowIndex = rowIndex++,
                            FullName = (line.FULL_NAME?.ToString() ?? "") + monthLabel,
                            NationalCode = line.NATIONAL_CODE?.ToString() ?? "",
                            InsuranceCode = line.INS_CODE?.ToString() ?? "",
                            FatherName = line.FATHER_NAME?.ToString() ?? "",
                            JobTitle = line.JOB_NAME?.ToString() ?? "",
                            WorkDays = workDays,
                            HireDate = DateInOccurrenceMonth(line.HIRE_DATE, (long)line.PERIOD_DATE),
                            FireDate = DateInOccurrenceMonth(line.FIRE_DATE, (long)line.PERIOD_DATE),
                            BaseDailyWage = baseDaily,
                            SeniorityDailyBase = seniorityDaily,
                            MonthlyWage = monthlyWage,
                            OtherSubjectBenefits = otherBenefits,
                            TotalSubjectToInsurance = (long)line.INS_BASE,
                            TotalGrossPay = (long)line.NOMINAL_GROSS,
                            WorkerPremium = (long)line.INS_WORKER,
                            TaxAmount = (long)line.TAX_AMOUNT,
                            NetPayable = (long)line.NOMINAL_NET_PAYABLE
                        });
                    }
                }

                byte[] pdfBytes = new InsuranceListDocument(reportDto).GeneratePdf();
                return File(pdfBytes, "application/pdf", $"InsuranceList_{reportDto.PeriodYear}_{reportDto.PeriodMonthName}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ===================================================================
        // تولید دیسکت بیمه تامین اجتماعی (فرمت DBF)
        // ===================================================================
        [HttpGet("{runId:int}/insurance-diskette")]
        public async Task<IActionResult> GetInsuranceDiskette([FromServices] Safir.Server.Services.Pay2DisketteService disketteService, int runId)
        {
            try
            {
                var result = await disketteService.GenerateInsuranceDisketteAsync(runId);

                if (result == null)
                    return NotFound("اطلاعات محاسبه مورد نظر یافت نشد.");

                return File(result.Value.ZipBytes, "application/zip", result.Value.FileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در تولید فایل دیسکت: " + ex.Message);
            }
        }

        [HttpGet("{runId:int}/insurance-diskette-preview")]
        public async Task<ActionResult<DiskettePreviewDto>> GetInsuranceDiskettePreview([FromServices] Safir.Server.Services.Pay2DisketteService disketteService, int runId)
        {
            try
            {
                var result = await disketteService.GetInsuranceDiskettePreviewAsync(runId);
                if (result == null)
                    return NotFound("اطلاعات محاسبه مورد نظر یافت نشد.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در بارگذاری پیش‌نمایش دیسکت: " + ex.Message);
            }
        }

        // ===================================================================
        // چاپ لیست مالیات حقوق (برای یک ماه یا تجمیعی)
        // ===================================================================
        [HttpGet("{runId:int}/tax-report")]
        public async Task<IActionResult> GetTaxReportPdf(int runId, [FromQuery] int wsId = 0)
        {
            try
            {
                var reportDto = new Safir.Shared.Models.Salary.Reports.TaxReportDto();
                List<int> targetRunIds = new List<int>();

                if (runId > 0)
                {
                    targetRunIds.Add(runId);
                }
                else if (wsId > 0)
                {
                    var runsSql = @"
                        SELECT R.RUN_ID 
                        FROM PAY2_RUN R
                        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                        WHERE P.WS_ID = @wsId AND R.IS_LATEST = 1 AND R.STATUS >= 2
                        ORDER BY P.PERIOD_DATE ASC";

                    targetRunIds = (await _db.DoGetDataSQLAsync<int>(runsSql, new { wsId })).ToList();

                    if (!targetRunIds.Any())
                        return NotFound("هیچ ماهِ محاسبه و تایید شده‌ای برای این کارگاه یافت نشد.");
                }
                else
                {
                    return BadRequest("پارامترهای ورودی نامعتبر است.");
                }

                int firstRunId = targetRunIds.First();
                const string headSql = @"
                    SELECT 
                        P.PERIOD_DATE, W.WS_CODE, W.WS_NAME, W.EMPLOYER_NAME, W.TAX_CODE
                    FROM PAY2_RUN R
                    INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                    INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                    WHERE R.RUN_ID = @firstRunId";

                var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { firstRunId });
                if (head == null) return NotFound("اطلاعات کارگاه/دوره یافت نشد.");

                long periodDate = (long)head.PERIOD_DATE;
                string year = (periodDate / 10000).ToString();
                int month = (int)((periodDate / 100) % 100);
                string[] monthNames = { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

                string monthName = runId > 0
                    ? ((month >= 1 && month <= 12) ? monthNames[month - 1] : month.ToString())
                    : "تجمیعی تمام ماه‌ها";

                reportDto.WorkshopCode = head.WS_CODE?.ToString() ?? "";
                reportDto.WorkshopName = head.WS_NAME?.ToString() ?? "";
                reportDto.EmployerName = head.EMPLOYER_NAME?.ToString() ?? "";
                reportDto.TaxCode = head.TAX_CODE?.ToString() ?? "";
                reportDto.PeriodYear = year;
                reportDto.PeriodMonthName = monthName;

                int rowIndex = 1;

                foreach (var currentRunId in targetRunIds)
                {
                    string monthLabel = "";
                    if (runId == 0)
                    {
                        var pDate = await _db.DoGetDataSQLAsyncSingle<long>("SELECT P.PERIOD_DATE FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE R.RUN_ID = @currentRunId", new { currentRunId });
                        int m = (int)((pDate / 100) % 100);
                        monthLabel = $" [{(m >= 1 && m <= 12 ? monthNames[m - 1] : m.ToString())}]";
                    }

                    var lines = (await _db.DoGetDataSQLAsync<dynamic>(
                        Safir.Server.Services.Pay2PayrollSnapshotQuery.Sql, new { runId = currentRunId })).ToList();
                    if (lines.Any(x => !(bool)x.HAS_NOMINAL_RAIL || !(bool)x.HAS_COMPLETE_NOMINAL_SNAPSHOT))
                        return UnprocessableEntity("خروجی قانونی ممکن نیست: Snapshot کامل ریل اسمی برای حداقل یک پرسنل وجود ندارد.");

                    foreach (var line in lines)
                    {
                        decimal workDays = (decimal)line.INSURANCE_DAYS;
                        long baseMonthly = (long)line.BASE_WAGE_MONTHLY;
                        long seniorityMonthly = (long)line.SENIORITY_MONTHLY;
                        long monthlyWage = baseMonthly + seniorityMonthly;
                        long otherBenefits = (long)line.OTHER_TAXABLE_ITEMS;
                        reportDto.Rows.Add(new TaxEmployeeRowDto
                        {
                            RowIndex = rowIndex++,
                            FullName = (line.FULL_NAME?.ToString() ?? "") + monthLabel,
                            NationalCode = line.NATIONAL_CODE?.ToString() ?? "",
                            JobTitle = line.JOB_NAME?.ToString() ?? "",
                            WorkDays = workDays,
                            HireDate = DateInOccurrenceMonth(line.HIRE_DATE, (long)line.PERIOD_DATE),
                            FireDate = DateInOccurrenceMonth(line.FIRE_DATE, (long)line.PERIOD_DATE),
                            BaseDailyWage = workDays > 0 ? (long)Math.Round(baseMonthly / workDays, MidpointRounding.AwayFromZero) : 0,
                            SeniorityDailyBase = workDays > 0 ? (long)Math.Round(seniorityMonthly / workDays, MidpointRounding.AwayFromZero) : 0,
                            MonthlyWage = monthlyWage,
                            OtherSubjectBenefits = otherBenefits,
                            TotalSubject = (long)line.TAXABLE_WAGE_MONTHLY + otherBenefits,
                            GrossPay = (long)line.NOMINAL_GROSS,
                            TaxBase = (long)line.TAX_BASE,
                            TaxAmount = (long)line.TAX_AMOUNT,
                            WorkerPremium = (long)line.INS_WORKER,
                            NetPayable = (long)line.NOMINAL_NET_PAYABLE
                        });
                    }
                }

                byte[] pdfBytes = new Safir.Server.Reports.TaxListDocument(reportDto).GeneratePdf();
                return File(pdfBytes, "application/pdf", $"TaxList_{reportDto.PeriodYear}_{reportDto.PeriodMonthName}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ===================================================================
        // گزارش مقایسه ماه به ماه (روند تغییرات حقوق)
        // ===================================================================
        [HttpGet("compare-months")]
        public async Task<ActionResult<Pay2MonthCompareResultDto>> CompareMonths([FromQuery] int wsId, [FromQuery] long period1, [FromQuery] long period2)
        {
            try
            {
                var result = new Pay2MonthCompareResultDto();

                // ۱. پیدا کردن شناسه آخرین محاسبه قطعی برای ماه اول
                var run1Sql = "SELECT TOP 1 R.RUN_ID FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE P.WS_ID = @wsId AND P.PERIOD_DATE = @period1 AND R.IS_LATEST = 1 AND R.STATUS >= 2 ORDER BY R.RUN_ID DESC";
                var run1Id = await _db.DoGetDataSQLAsyncSingle<int?>(run1Sql, new { wsId, period1 });

                // ۲. پیدا کردن شناسه آخرین محاسبه قطعی برای ماه دوم
                var run2Sql = "SELECT TOP 1 R.RUN_ID FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE P.WS_ID = @wsId AND P.PERIOD_DATE = @period2 AND R.IS_LATEST = 1 AND R.STATUS >= 2 ORDER BY R.RUN_ID DESC";
                var run2Id = await _db.DoGetDataSQLAsyncSingle<int?>(run2Sql, new { wsId, period2 });

                if (!run1Id.HasValue && !run2Id.HasValue)
                    return BadRequest("برای هیچ‌کدام از ماه‌های انتخاب شده، محاسبه تایید شده‌ای یافت نشد.");

                result.Period1Title = BuildPeriodTitle(period1);
                result.Period2Title = BuildPeriodTitle(period2);

                // ۳. اجرای FULL OUTER JOIN برای مقایسه دقیق و کشف پرسنل جدید/حذف شده
                string compareSql = @"
                    SELECT 
                        COALESCE(R1.EMP_ID, R2.EMP_ID) AS EMP_ID,
                        E.EMP_CODE,
                        E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
                        ISNULL(R1.GROSS_PAY, 0) AS GROSS_PAY_1,
                        ISNULL(R2.GROSS_PAY, 0) AS GROSS_PAY_2,
                        ISNULL(R1.TOTAL_DED, 0) AS TOTAL_DED_1,
                        ISNULL(R2.TOTAL_DED, 0) AS TOTAL_DED_2,
                        ISNULL(R1.NET_PAY, 0) AS NET_PAY_1,
                        ISNULL(R2.NET_PAY, 0) AS NET_PAY_2
                    FROM (SELECT * FROM PAY2_RUN_LINE WHERE RUN_ID = @r1) R1
                    FULL OUTER JOIN (SELECT * FROM PAY2_RUN_LINE WHERE RUN_ID = @r2) R2 ON R1.EMP_ID = R2.EMP_ID
                    INNER JOIN PAY2_EMPLOYEE E ON COALESCE(R1.EMP_ID, R2.EMP_ID) = E.EMP_ID
                    ORDER BY E.LAST_NAME, E.FIRST_NAME";

                var rows = await _db.DoGetDataSQLAsync<Pay2MonthCompareRowDto>(compareSql, new { r1 = run1Id ?? 0, r2 = run2Id ?? 0 });
                result.Rows = rows.ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در مقایسه ماه‌ها: " + ex.Message);
            }
        }

        // ===================================================================
        // تولید فایل اکسل اظهارنامه سالانه مالیات (خلاصه وضعیت پرسنل در سال)
        // ===================================================================
        [HttpGet("tax-report-excel")]
        public async Task<IActionResult> GetAnnualTaxReportExcel([FromQuery] int wsId, [FromQuery] long periodDate)
        {
            if (wsId <= 0)
                return BadRequest("کارگاه نامعتبر است.");

            try
            {
                // ۱. استخراج سال هدف
                long targetYear;
                if (periodDate > 0)
                {
                    targetYear = periodDate / 10000;
                }
                else
                {
                    // اگر کاربر "تجمیعی کل سال" را انتخاب کرده بود، سال آخرین دوره کارگاه را می‌گیریم
                    var maxDate = await _db.DoGetDataSQLAsyncSingle<long?>(
                        "SELECT MAX(PERIOD_DATE) FROM PAY2_PERIOD WHERE WS_ID = @wsId AND STATUS >= 3", new { wsId });

                    if (maxDate == null)
                        return NotFound("هیچ دوره‌ی محاسبه‌شده‌ای برای این کارگاه یافت نشد.");

                    targetYear = maxDate.Value / 10000;
                }

                // ۲. خواندن نام کارگاه
                var wsName = await _db.DoGetDataSQLAsyncSingle<string>(
                    "SELECT WS_NAME FROM PAY2_WORKSHOP WHERE WS_ID = @wsId", new { wsId });

                var missingAnnualSnapshots = await _db.DoGetDataSQLAsyncSingle<int>(@"
                    SELECT COUNT(*) FROM PAY2_RUN_LINE RL
                    INNER JOIN PAY2_RUN R ON R.RUN_ID=RL.RUN_ID
                    INNER JOIN PAY2_PERIOD P ON P.PER_ID=R.PER_ID
                    WHERE P.WS_ID=@wsId AND R.IS_LATEST=1 AND R.STATUS>=2
                      AND P.PERIOD_DATE/10000=@targetYear
                      AND (RL.NOMINAL_DAYS IS NULL OR RL.NOMINAL_GROSS IS NULL)", new { wsId, targetYear });
                if (missingAnnualSnapshots > 0)
                    return UnprocessableEntity("گزارش سالانه قانونی قابل تولید نیست: Snapshot روزکرد یا ناخالص اسمی در یک یا چند Run موجود نیست.");

                // ۳. کوئری تجمیع اطلاعات پرسنل در سال هدف (فقط اجراهای تأیید شده یا قطعی)
                const string sql = @"
                    SELECT 
                        E.EMP_CODE,
                        E.NATIONAL_CODE,
                        E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
                        SUM(RL.NOMINAL_DAYS) AS TOTAL_WORK_DAYS,
                        SUM(RL.NOMINAL_GROSS) AS TOTAL_GROSS_PAY,
                        SUM(RL.TAX_BASE) AS TOTAL_TAX_BASE,
                        SUM(RL.TAX_AMOUNT) AS TOTAL_TAX_AMOUNT
                    FROM PAY2_RUN_LINE RL
                    INNER JOIN PAY2_RUN R ON RL.RUN_ID = R.RUN_ID
                    INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                    INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
                    WHERE P.WS_ID = @wsId
                      AND R.IS_LATEST = 1
                      AND R.STATUS >= 2
                      AND (P.PERIOD_DATE / 10000) = @targetYear
                    GROUP BY 
                        E.EMP_ID, E.EMP_CODE, E.NATIONAL_CODE, E.LAST_NAME, E.FIRST_NAME
                    HAVING SUM(RL.NOMINAL_GROSS) > 0
                    ORDER BY 
                        E.LAST_NAME, E.FIRST_NAME";

                var reportData = await _db.DoGetDataSQLAsync<dynamic>(sql, new { wsId, targetYear });

                var dataList = reportData.ToList();
                if (!dataList.Any())
                    return NotFound($"هیچ داده مالیاتی برای سال {targetYear} یافت نشد.");

                // ۴. تولید فایل اکسل با ClosedXML
                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add($"مالیات_{targetYear}");
                ws.RightToLeft = true;

                // هدر گزارش
                ws.Cell(1, 1).Value = $"گزارش تجمیعی مالیات حقوق سال {targetYear}";
                ws.Cell(2, 1).Value = $"کارگاه: {wsName}";
                var titleRange = ws.Range(1, 1, 1, 8);
                titleRange.Merge();
                titleRange.Style.Font.Bold = true;
                titleRange.Style.Font.FontSize = 14;
                titleRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4472C4");
                titleRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                titleRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // هدر ستون‌ها
                string[] headers = { "ردیف", "کد پرسنلی", "کد ملی", "نام و نام خانوادگی", "کارکرد (روز)", "جمع حقوق و مزایا", "درآمد مشمول مالیات", "مالیات کسر شده" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(4, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#D9E1F2");
                    cell.Style.Border.SetOutsideBorder(ClosedXML.Excel.XLBorderStyleValues.Thin);
                }

                // تزریق داده‌ها
                int r = 5;
                int rowIdx = 1;
                foreach (var item in dataList)
                {
                    ws.Cell(r, 1).Value = rowIdx++;
                    ws.Cell(r, 2).Value = item.EMP_CODE?.ToString();
                    ws.Cell(r, 3).Value = item.NATIONAL_CODE?.ToString();
                    ws.Cell(r, 4).Value = item.FULL_NAME?.ToString();
                    ws.Cell(r, 5).Value = (decimal)item.TOTAL_WORK_DAYS;
                    ws.Cell(r, 6).Value = (long)item.TOTAL_GROSS_PAY;
                    ws.Cell(r, 7).Value = (long)item.TOTAL_TAX_BASE;
                    ws.Cell(r, 8).Value = (long)item.TOTAL_TAX_AMOUNT;
                    r++;
                }

                // ردیف جمع کل
                ws.Cell(r, 1).Value = "جمع کل";
                ws.Range(r, 1, r, 4).Merge().Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Left;
                ws.Cell(r, 5).FormulaA1 = $"SUM(E5:E{r - 1})";
                ws.Cell(r, 6).FormulaA1 = $"SUM(F5:F{r - 1})";
                ws.Cell(r, 7).FormulaA1 = $"SUM(G5:G{r - 1})";
                ws.Cell(r, 8).FormulaA1 = $"SUM(H5:H{r - 1})";

                var footerRange = ws.Range(r, 1, r, 8);
                footerRange.Style.Font.Bold = true;
                footerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                // تنظیم فرمت اعداد (جداکننده هزارگان)
                ws.Range(5, 6, r, 8).Style.NumberFormat.Format = "#,##0";

                // تنظیم بوردر کل جدول
                ws.Range(4, 1, r, 8).Style.Border.SetOutsideBorder(ClosedXML.Excel.XLBorderStyleValues.Thin);
                ws.Range(4, 1, r, 8).Style.Border.SetInsideBorder(ClosedXML.Excel.XLBorderStyleValues.Thin);

                ws.Columns(1, 8).AdjustToContents();
                ws.Column(3).Width = 15; // کد ملی
                ws.Column(4).Width = 30; // نام

                using var ms = new MemoryStream();
                wb.SaveAs(ms);

                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TaxReport_{targetYear}.xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در تولید گزارش مالیات: " + ex.Message);
            }
        }

        [HttpGet("{runId:int}/tax-diskette")]
        public async Task<IActionResult> GetTaxDiskette([FromServices] Safir.Server.Services.Pay2DisketteService disketteService, int runId)
        {
            try
            {
                var result = await disketteService.GenerateTaxDisketteAsync(runId);

                if (result == null)
                    return NotFound("اطلاعات محاسبه مورد نظر یافت نشد.");

                return File(result.Value.ZipBytes, "application/zip", result.Value.FileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در تولید فایل دیسکت مالیات: " + ex.Message);
            }
        }

        [HttpGet("{runId:int}/tax-diskette-preview")]
        public async Task<ActionResult<TaxDiskettePreviewDto>> GetTaxDiskettePreview([FromServices] Safir.Server.Services.Pay2DisketteService disketteService, int runId)
        {
            try
            {
                var result = await disketteService.GetTaxDiskettePreviewAsync(runId);
                if (result == null)
                    return NotFound("اطلاعات محاسبه مورد نظر یافت نشد.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در بارگذاری پیش‌نمایش دیسکت مالیات: " + ex.Message);
            }
        }
        private static string DateInOccurrenceMonth(object? value, long periodDate)
        {
            if (value is null || value is DBNull) return string.Empty;
            if (!long.TryParse(value.ToString(), out var date) || date <= 0) return string.Empty;
            return date / 100 == periodDate / 100 ? date.ToString() : string.Empty;
        }

    }
}
