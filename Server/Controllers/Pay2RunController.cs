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
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER";

            result.Columns = (await _db.DoGetDataSQLAsync<Pay2RunColumnDto>(colSql, new { runId })).ToList();

            // 2. استخراج ردیف‌های اصلی فیش حقوقی
            string lineSql = @"
                SELECT L.*, E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME
                FROM PAY2_RUN_LINE L WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON L.EMP_ID = E.EMP_ID
                WHERE L.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            result.Lines = (await _db.DoGetDataSQLAsync<Pay2RunLineDto>(lineSql, new { runId })).ToList();

            // 3. استخراج مبالغ ریز (Details) و اتصال آن‌ها به ردیف‌ها
            string detSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
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

        // کلاس کمکی برای Dapper
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
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_RUN      R WITH (NOLOCK) ON RL.RUN_ID = R.RUN_ID
                INNER JOIN PAY2_PERIOD   P WITH (NOLOCK) ON R.PER_ID  = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID   = W.WS_ID
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                LEFT  JOIN PAY2_JOB      J WITH (NOLOCK) ON E.JOB_ID  = J.JOB_ID
                WHERE RL.RUN_ID = @runId AND RL.EMP_ID = @empId";

            var head = await _db.DoGetDataSQLAsyncSingle<PayslipHeadRow>(headSql, new { runId, empId });
            if (head == null)
                return NotFound("فیش حقوقی برای این پرسنل در این اجرا یافت نشد.");

            // ۲. مزایا = فقط آیتم‌های نوع پرداختی (ITEM_TYPE 1,2)؛ مجموع آن‌ها = GROSS_PAY
            string earnSql;
            if (isOfficial)
            {
                // در فیش رسمی: حقوق رسمی نمایش داده می‌شود و حقوق اسمی (BASE_SAL) فیلتر می‌شود
                earnSql = @"
                    SELECT I.ITEM_NAME AS Title, D.AMOUNT AS Amount
                    FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                    INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                    WHERE D.RUN_ID = @runId AND D.EMP_ID = @empId
                      AND I.ITEM_TYPE IN (1, 2)
                      AND D.AMOUNT <> 0
                      AND I.ITEM_CODE <> 'BASE_SAL'
                    ORDER BY I.SORT_ORDER";
            }
            else
            {
                // در فیش اسمی (واقعی): حقوق اسمی و مزایا نمایش داده می‌شود و حقوق رسمی (BASE_SAL_B) فیلتر می‌شود
                earnSql = @"
                    SELECT I.ITEM_NAME AS Title, D.AMOUNT AS Amount
                    FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                    INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                    WHERE D.RUN_ID = @runId AND D.EMP_ID = @empId
                      AND I.ITEM_TYPE IN (1, 2)
                      AND D.AMOUNT <> 0
                      AND I.ITEM_CODE <> 'BASE_SAL_B'
                    ORDER BY I.SORT_ORDER";
            }

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

            // در فیش رسمی نیازی به تعدیل گرد کردن به شیوه اسمی نیست زیرا پایه حقوق تغییر کرده است
            // اما برای حفظ ظاهر، تعدیل روی فیش اسمی اعمال می‌شود
            if (!isOfficial)
            {
                // تعدیلِ گرد کردنِ خالص (اعمال ROUND_MODE در موتور محاسبه) تا اتحاد
                // «جمع مزایا − جمع کسورات = خالص پرداختیِ رسمی» همیشه دقیق بماند.
                long rounding = head.NET_PAY - (head.GROSS_PAY - head.TOTAL_DED);
                if (rounding > 0)
                    dto.Earnings.Add(new PayslipLineDto { Title = "تعدیل (گرد کردن)", Amount = rounding });
                else if (rounding < 0)
                    dto.Deductions.Add(new PayslipLineDto { Title = "تعدیل (گرد کردن)", Amount = -rounding });
            }
            else
            {
                // در فیش رسمی مبلغ خالص جدید را بر اساس اقلام موجود (رسمی) دوباره حساب میکنیم
                // تا تراز فیش رسمی درست بماند
                long officialTotalEarn = dto.Earnings.Sum(x => x.Amount);
                long officialTotalDed = dto.Deductions.Sum(x => x.Amount);
                dto.NetPay = officialTotalEarn - officialTotalDed;
            }

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

        // ===================================================================
        // لغو صدور سند (STATUS 3 → 2) — قفل‌گشایی برای صدور مجدد
        // شرط: سند حسابداری در سیستم حسابداری قطعی (OKF≠1) نشده باشد.
        // عملیات: حذف DEED_DTL + DEED_HED، صفر کردن DEED_ID_SAL، برگشت
        //         وضعیت PAY2_RUN به ۲ (تأیید نهایی) و PAY2_PERIOD به ۳.
        // ===================================================================
        [HttpPut("{runId:int}/unfinalize-deed")]
        public async Task<IActionResult> UnfinalizeDeed(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var runInfo = await conn.QuerySingleOrDefaultAsync(
                        @"SELECT R.STATUS, R.PER_ID, R.DEED_ID_SAL, P.DEED_N_S_PAY
                          FROM PAY2_RUN R
                          INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                          WHERE R.RUN_ID = @runId",
                        new { runId }, tran);

                    if (runInfo == null)
                        throw new InvalidOperationException("محاسبه‌ای با این شناسه یافت نشد.");

                    byte status = (byte)runInfo.STATUS;
                    if (status != 3)
                        throw new InvalidOperationException("این عملیات فقط برای اجراهایی که سند صادر کرده‌اند (وضعیت ۳) مجاز است.");

                    double? deedNs = (double?)runInfo.DEED_N_S_PAY;

                    if (deedNs.HasValue && deedNs.Value > 0)
                    {
                        // 🚀 رفع باگ Critical: افزودن WITH (UPDLOCK) برای مسدود کردن Race Condition با حسابداران
                        var okfStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                            "SELECT OKF FROM DEED_HED WITH (UPDLOCK) WHERE N_S = @N_S",
                            new { N_S = deedNs.Value }, tran);

                        if (okfStatus.HasValue && okfStatus.Value != 1)
                            throw new InvalidOperationException("این سند در سیستم حسابداری بررسی و قطعی شده است. امکان لغو صدور سند وجود ندارد.");

                        await conn.ExecuteAsync("DELETE FROM DEED_DTL WHERE N_S = @N_S", new { N_S = deedNs.Value }, tran);
                        await conn.ExecuteAsync("DELETE FROM DEED_HED WHERE N_S = @N_S", new { N_S = deedNs.Value }, tran);
                    }

                    int perId = (int)runInfo.PER_ID;

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
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در لغو صدور سند: " + ex.Message); }
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

        [HttpGet("{runId:int}/preview-deed")]
        public async Task<ActionResult<Pay2DeedPreviewDto>> PreviewDeed(int runId, [FromQuery] byte? overrideMode = null)
        {
            var result = new Pay2DeedPreviewDto();
            try
            {
                var runInfo = await _db.DoGetDataSQLAsyncSingle<dynamic>(
                    @"SELECT R.DEED_MODE, W.DEFAULT_DEED_MODE 
                      FROM PAY2_RUN R 
                      INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID 
                      INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID 
                      WHERE R.RUN_ID = @runId", new { runId });

                if (runInfo == null)
                    return NotFound("محاسبه یافت نشد.");

                byte effectiveMode = overrideMode ?? (byte)(runInfo.DEED_MODE ?? runInfo.DEFAULT_DEED_MODE);

                result.ModeUsed = (Pay2DeedMode)effectiveMode;
                result.ModeTitle = effectiveMode == 1 ? "سند کلی ـ روش فعلی" : "سند نیمه‌تفصیلی اشخاص";

                var articles = await _db.DoGetDataSQLAsync<Pay2DeedArticleDto>(
                    "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @DEED_MODE = @mode",
                    new { runId, mode = effectiveMode });

                result.Articles = articles.ToList();

                foreach (var art in result.Articles)
                {
                    // 🚀 گارد امنیتی: جلوگیری از نشت مقادیر منفی (حتی در پیش‌نمایش)
                    if (art.BED < 0 || art.BES < 0)
                        result.ValidationErrors.Add($"خطای ساختاری: مبلغ منفی در آرتیکل (حساب: {art.HES_CODE}, بدهکار: {art.BED}, بستانکار: {art.BES}). حقوق این شخص نیازمند بررسی است.");

                    var parsed = Pay2AccountHelper.Parse(art.HES_CODE, art.ACC_KEY);
                    if (!parsed.IsValid)
                    {
                        result.ValidationErrors.Add($"خطا در حساب {art.ACC_KEY} ({(art.EmployeeName ?? "تجمیعی")}): {parsed.ErrorMessage}");
                    }
                }

                if (!result.IsBalanced)
                    result.ValidationErrors.Add($"سند تراز نیست. جمع بدهکار: {result.TotalDebit:N0}، جمع بستانکار: {result.TotalCredit:N0}، اختلاف: {result.Difference:N0}");

                return Ok(result);
            }
            catch (System.Data.SqlClient.SqlException ex)
            {
                result.ValidationErrors.Add(ex.Message);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطای سرور: " + ex.Message);
            }
        }

        // ===================================================================
        // صدور قطعی سند حسابداری (Transactional & Locked)
        // ===================================================================
        [HttpPost("{runId:int}/generate-deed")]
        public async Task<IActionResult> GenerateDeed(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();
            var userName = User.Identity?.Name ?? "System";

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var runInfo = await conn.QuerySingleAsync<dynamic>(
                        @"SELECT R.STATUS, R.PER_ID, R.DEED_ID_SAL, R.DEED_MODE, W.DEFAULT_DEED_MODE 
                          FROM PAY2_RUN R WITH (UPDLOCK) 
                          INNER JOIN PAY2_PERIOD P WITH (UPDLOCK) ON R.PER_ID = P.PER_ID 
                          INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                          WHERE R.RUN_ID = @runId",
                        new { runId }, tran);

                    byte status = (byte)runInfo.STATUS;
                    if (status != 2 && status != 3)
                        throw new InvalidOperationException("اجرا باید در وضعیت 'تأیید نهایی' یا 'سند صادر شده' باشد.");

                    int perId = (int)runInfo.PER_ID;
                    byte effectiveMode = (byte)(runInfo.DEED_MODE ?? runInfo.DEFAULT_DEED_MODE);

                    var periodInfo = await conn.QuerySingleAsync(
                        "SELECT PERIOD_DATE, DEED_N_S_PAY FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @perId",
                        new { perId }, tran);

                    long periodDate = (long)periodInfo.PERIOD_DATE;
                    double? existingNs = (double?)periodInfo.DEED_N_S_PAY;

                    var articles = (await conn.QueryAsync<Pay2DeedArticleDto>(
                        "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @CALC_BY = @userCod, @DEED_MODE = @mode",
                        new { runId, userCod, mode = effectiveMode }, tran, commandTimeout: 120)).ToList();

                    long sumBed = articles.Sum(x => x.BED);
                    long sumBes = articles.Sum(x => x.BES);
                    if (sumBed != sumBes)
                        throw new InvalidOperationException($"سند تراز نیست! بدهکار: {sumBed:N0}، بستانکار: {sumBes:N0}");

                    long deedDate = Safir.Shared.Utility.CL_Tarikh.GetPersianMonthEndAsLong(periodDate);
                    string hedSharh = $"سند حقوق و دستمزد دوره {periodDate}";
                    double targetNs;

                    if (status == 3 && existingNs.HasValue && existingNs.Value > 0)
                    {
                        targetNs = existingNs.Value;

                        var okfStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                            "SELECT OKF FROM DEED_HED WITH (UPDLOCK) WHERE N_S = @N_S",
                            new { N_S = targetNs }, tran);

                        if (okfStatus.HasValue && okfStatus.Value != 1)
                            throw new InvalidOperationException("این سند در سیستم حسابداری بررسی و قطعی شده است. امکان بازصدور وجود ندارد.");

                        await conn.ExecuteAsync("DELETE FROM DEED_DTL WHERE N_S = @N_S", new { N_S = targetNs }, tran);
                        await conn.ExecuteAsync(@"
                            UPDATE DEED_HED SET DATE_S = @DATE_S, SHARH_S = @SHARH, NO_S = 11, USER_NAME = @USER, UID = @UID WHERE N_S = @N_S",
                            new { N_S = targetNs, DATE_S = deedDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);
                    }
                    else
                    {
                        // 🟢 اصلاح معماری و پرفورمنس: استفاده از Application Lock به جای TABLOCKِ مخرب
                        // این کار تداخل همزمان (Race Condition) را مسدود می‌کند بدون اینکه کل سیستم حسابداری قفل شود.
                        await conn.ExecuteAsync("EXEC sp_getapplock @Resource = 'DeedNumberAllocation', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", null, tran);

                        targetNs = (await conn.QuerySingleOrDefaultAsync<double?>("SELECT MAX(N_S) FROM DEED_HED WITH (UPDLOCK)", null, tran) ?? 0) + 1;

                        await conn.ExecuteAsync(@"
                            INSERT INTO DEED_HED (N_S, DATE_S, SHARH_S, NO_S, USER_NAME, OKF, CRT, UID)
                            VALUES (@N_S, @DATE_S, @SHARH, 11, @USER, 1, GETDATE(), @UID)",
                            new { N_S = targetNs, DATE_S = deedDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);
                    }

                    int radif = 1;
                    foreach (var art in articles)
                    {
                        // 🚀 گارد امنیتی جلوگیری از نشت مبالغ منفی
                        if (art.BED < 0 || art.BES < 0)
                            throw new InvalidOperationException($"آرتیکل با مبلغ منفی مجاز نیست (حساب: {art.HES_CODE}). لطفاً مقادیر منفی در حقوق پرسنل بررسی شود.");

                        var parsedAcc = Pay2AccountHelper.Parse(art.HES_CODE, art.ACC_KEY);
                        if (!parsedAcc.IsValid)
                            throw new InvalidOperationException($"خطا در حساب {art.ACC_KEY}: {parsedAcc.ErrorMessage}");

                        var p = new DynamicParameters();
                        p.Add("N_S", targetNs);
                        p.Add("RADIF", radif++);
                        p.Add("HES_K", parsedAcc.Account.HesK);
                        p.Add("HES_M", parsedAcc.Account.HesM);
                        p.Add("HES_T", parsedAcc.Account.HesT ?? 0);
                        p.Add("HES", art.HES_CODE);
                        p.Add("SHARH", art.SHARH);
                        p.Add("BED", (double)art.BED);
                        p.Add("BES", (double)art.BES);
                        p.Add("UID", userCod);

                        string cols = "N_S, RADIF, HES_K, HES_M, HES_T, HES, SHARH, BED, BES, CRT, UID";
                        string vals = "@N_S, @RADIF, @HES_K, @HES_M, @HES_T, @HES, @SHARH, @BED, @BES, GETDATE(), @UID";

                        if (parsedAcc.Account.HesT2.HasValue) { cols += ", HES_T2"; vals += ", @HES_T2"; p.Add("HES_T2", parsedAcc.Account.HesT2.Value); }
                        if (parsedAcc.Account.HesT3.HasValue) { cols += ", HES_T3"; vals += ", @HES_T3"; p.Add("HES_T3", parsedAcc.Account.HesT3.Value); }
                        if (parsedAcc.Account.HesT4.HasValue) { cols += ", HES_T4"; vals += ", @HES_T4"; p.Add("HES_T4", parsedAcc.Account.HesT4.Value); }

                        await conn.ExecuteAsync($@"INSERT INTO DEED_DTL ({cols}) VALUES ({vals})", p, tran);
                    }

                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN SET STATUS = 3, DEED_ID_SAL = @deedId, DEED_MODE = @mode, DEED_GENERATOR_VERSION = 1 WHERE RUN_ID = @runId;
                        UPDATE PAY2_PERIOD SET STATUS = 4, DEED_N_S_PAY = @targetNs WHERE PER_ID = @perId;",
                        new { runId, deedId = (int)targetNs, targetNs, perId, mode = effectiveMode }, tran);
                });

                return Ok();
            }
            catch (System.Data.SqlClient.SqlException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در صدور سند: " + ex.Message); }
        }
    }
}