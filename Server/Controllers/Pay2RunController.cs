using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using Safir.Server.Reports;
using Safir.Server.Services;
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
        private readonly Pay2ExcelAuditService _excelAudit;
        public Pay2RunController(IDatabaseService db, Pay2ExcelAuditService excelAudit)
        { _db = db; _excelAudit = excelAudit; }

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
        // 🚀 خروجی اکسل حسابرسی با فرمول‌های واقعی (Excel Audit)
        // شامل ۴ شیت: Settings, RawData, Payslip, Control
        // هر سلول عددی در Payslip حاوی فرمول Excel است.
        // ===================================================================
        [HttpGet("{runId:int}/excel-audit")]
        public async Task<IActionResult> GetExcelAudit(int runId)
        {
            try
            {
                var data = await BuildExcelAuditData(runId);
                var bytes = _excelAudit.Generate(data);
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Payroll_Audit_Run{runId}.xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطا در تهیه خروجی اکسل حسابرسی: " + ex.Message);
            }
        }

        /// <summary>
        /// جمع‌آوری تمام داده‌های لازم برای اکسل حسابرسی از دیتابیس
        /// </summary>
        private async Task<Pay2ExcelAuditDataDto> BuildExcelAuditData(int runId)
        {
            var data = new Pay2ExcelAuditDataDto { RUN_ID = runId };

            // ─── ۱. اطلاعات اجرا و دوره ───
            var runInfo = await _db.DoGetDataSQLAsyncSingle<RunInfoRow>(@"
                SELECT R.RUN_ID, R.PER_ID, P.PERIOD_DATE, W.WS_NAME
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId", new { runId });

            if (runInfo != null)
            {
                data.PERIOD_DATE = runInfo.PERIOD_DATE;
                data.WS_NAME = runInfo.WS_NAME;
            }

            // ─── ۲. تنظیمات PAY2_CONFIG ───
            var configs = await _db.DoGetDataSQLAsync<Pay2ConfigDto>(
                "SELECT CFG_KEY, CFG_VALUE, DATA_TYPE FROM PAY2_CONFIG ORDER BY CFG_KEY");
            data.Config = configs?.ToDictionary(c => c.CFG_KEY, c => c.CFG_VALUE ?? "") ?? new();

            // ─── ۳. پل‌های مالیات ───
            int taxYear = data.Config.TryGetValue("TAX_YEAR", out var tyStr) && int.TryParse(tyStr, out var ty) ? ty : 1403;
            var brackets = await _db.DoGetDataSQLAsync<Pay2TaxBracketDto>(
                "SELECT BRK_ID, TAX_YEAR, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = @taxYear ORDER BY SORT_ORDER",
                new { taxYear });
            data.TaxBrackets = brackets?.ToList() ?? new();

            // ─── ۴. تعاریف آیتم‌ها ───
            var itemDefs = await _db.DoGetDataSQLAsync<Pay2ExcelItemDefDto>(@"
                SELECT ITEM_CODE, ITEM_NAME, ITEM_TYPE, CALC_BASIS,
                       CASE WHEN INS_SUBJECT = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS INS_SUBJECT,
                       CASE WHEN TAX_SUBJECT = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS TAX_SUBJECT
                FROM PAY2_ITEM_DEF
                ORDER BY SORT_ORDER");
            data.ItemDefs = itemDefs?.ToList() ?? new();

            // ─── ۵. ستون‌های پویا ───
            string colSql = @"
                SELECT DISTINCT D.ITEM_ID, I.ITEM_CODE, I.ITEM_NAME, I.SORT_ORDER
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER";
            data.Columns = (await _db.DoGetDataSQLAsync<Pay2RunColumnDto>(colSql, new { runId })).ToList();

            // ─── ۶. ردیف‌های نتیجه محاسبه (PAY2_RUN_LINE) ───
            string lineSql = @"
                SELECT L.*, E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME
                FROM PAY2_RUN_LINE L WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON L.EMP_ID = E.EMP_ID
                WHERE L.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME";
            var runLines = (await _db.DoGetDataSQLAsync<Pay2RunLineDto>(lineSql, new { runId })).ToList();

            // ─── ۷. جزئیات آیتم‌ها (PAY2_RUN_DETAIL) ───
            string detSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId";
            var details = await _db.DoGetDataSQLAsync<RunDetailFlat>(detSql, new { runId });
            var groupedDetails = details
                .GroupBy(x => x.EMP_ID)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ITEM_CODE, x => x.AMOUNT));

            // ─── ۸. داده‌های خام کارکرد (PAY2_ATTENDANCE) ───
            string attSql = @"
                SELECT A.EMP_ID, A.WORK_DAYS, A.DAYS, A.DAYSB,
                       A.OT_NORMAL_H, A.OT_HOLIDAY_H, A.OT_ADMIN_H, A.LEAVE_DAYS,
                       A.PERF_AMOUNT, A.TRANSP_AMOUNT, A.KASR_OTHER,
                       E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME
                FROM PAY2_ATTENDANCE A WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON A.EMP_ID = E.EMP_ID
                INNER JOIN PAY2_RUN R WITH (NOLOCK) ON R.PER_ID = A.PER_ID
                WHERE R.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME";
            var attLines = (await _db.DoGetDataSQLAsync<AttRawRow>(attSql, new { runId })).ToList();

            // ─── ۹. سندهای استخدامی (Decree Lines) ───
            string decSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, I.ITEM_NAME,
                       I.ITEM_TYPE, I.CALC_BASIS,
                       CASE WHEN I.INS_SUBJECT = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS INS_SUBJECT,
                       CASE WHEN I.TAX_SUBJECT = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS TAX_SUBJECT,
                       DL.AMOUNT, DL.INS_OV, DL.TAX_OV, DL.BASIS_OV
                FROM PAY2_DECREE_LINE DL WITH (NOLOCK)
                INNER JOIN PAY2_DECREE D WITH (NOLOCK) ON DL.DEC_ID = D.DEC_ID
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON DL.ITEM_ID = I.ITEM_ID
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON D.EMP_ID = E.EMP_ID
                INNER JOIN PAY2_RUN R WITH (NOLOCK) ON R.RUN_ID = @runId
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                WHERE E.WS_ID = P.WS_ID
                  AND E.IS_ACTIVE = 1
                  AND D.IS_CONFIRMED = 1
                  AND D.EFF_FROM <= P.PERIOD_DATE
                  AND (D.EFF_TO IS NULL OR D.EFF_TO >= P.PERIOD_DATE)
                ORDER BY DL.EMP_ID, I.SORT_ORDER";
            var decLines = (await _db.DoGetDataSQLAsync<DecLineRow>(decSql, new { runId })).ToList();
            var groupedDec = decLines.GroupBy(d => d.EMP_ID)
                .ToDictionary(g => g.Key, g => g.ToList());

            // ─── ۱۰. مقادیر ATT_VALUE (آیتم‌های متغیر) ───
            string attValSql = @"
                SELECT V.EMP_ID, I.ITEM_CODE, V.VALUE
                FROM PAY2_ATT_VALUE V WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON V.ITEM_ID = I.ITEM_ID
                INNER JOIN PAY2_RUN R WITH (NOLOCK) ON R.PER_ID = V.PER_ID
                WHERE R.RUN_ID = @runId";
            var attValues = await _db.DoGetDataSQLAsync<AttValueRow>(attValSql, new { runId });
            var groupedAtt = attValues
                .GroupBy(a => a.EMP_ID)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ITEM_CODE, x => x.VALUE));

            // ─── ۱۱. سرهم‌بندی Pay2ExcelRawLineDto ───
            foreach (var att in attLines)
            {
                var rawLine = new Pay2ExcelRawLineDto
                {
                    EMP_ID = att.EMP_ID,
                    EMP_CODE = att.EMP_CODE,
                    FULL_NAME = att.FULL_NAME,
                    WORK_DAYS = att.WORK_DAYS,
                    DAYS = att.DAYS,
                    DAYSB = att.DAYSB,
                    OT_NORMAL_H = att.OT_NORMAL_H,
                    OT_HOLIDAY_H = att.OT_HOLIDAY_H,
                    OT_ADMIN_H = att.OT_ADMIN_H,
                    LEAVE_DAYS = att.LEAVE_DAYS,
                    PERF_AMOUNT = att.PERF_AMOUNT,
                    TRANSP_AMOUNT = att.TRANSP_AMOUNT,
                    KASR_OTHER = att.KASR_OTHER,
                };

                // سندها
                if (groupedDec.TryGetValue(att.EMP_ID, out var decs))
                {
                    rawLine.DecreeLines = decs.Select(d => new Pay2ExcelDecreeLineDto
                    {
                        ITEM_CODE = d.ITEM_CODE,
                        ITEM_NAME = d.ITEM_NAME,
                        ITEM_TYPE = d.ITEM_TYPE,
                        CALC_BASIS = d.CALC_BASIS,
                        INS_SUBJECT = d.INS_SUBJECT,
                        TAX_SUBJECT = d.TAX_SUBJECT,
                        AMOUNT = d.AMOUNT,
                        INS_OV = d.INS_OV,
                        TAX_OV = d.TAX_OV,
                        BASIS_OV = d.BASIS_OV,
                    }).ToList();
                }

                // ATT_VALUE
                if (groupedAtt.TryGetValue(att.EMP_ID, out var attVals))
                    rawLine.AttValues = attVals;

                // مقادیر SP
                var runLine = runLines.FirstOrDefault(l => l.EMP_ID == att.EMP_ID);
                if (runLine != null)
                {
                    rawLine.SP_GROSS_PAY  = runLine.GROSS_PAY;
                    rawLine.SP_INS_BASE   = runLine.INS_BASE;
                    rawLine.SP_INS_WORKER = runLine.INS_WORKER;
                    rawLine.SP_TAX_BASE   = runLine.TAX_BASE;
                    rawLine.SP_TAX_AMOUNT = runLine.TAX_AMOUNT;
                    rawLine.SP_LOAN_DED   = runLine.LOAN_DED;
                    rawLine.SP_ADVANCE_DED= runLine.ADVANCE_DED;
                    rawLine.SP_OTHER_DED  = runLine.OTHER_DED;
                    rawLine.SP_TOTAL_DED  = runLine.TOTAL_DED;
                    rawLine.SP_NET_PAY    = runLine.NET_PAY;
                    rawLine.Details = groupedDetails.TryGetValue(att.EMP_ID, out var d) ? d : new();
                }

                data.RawLines.Add(rawLine);
            }

            return data;
        }

        // کلاس‌های کمکی Dapper برای BuildExcelAuditData
        private class RunInfoRow
        {
            public int RUN_ID { get; set; }
            public int PER_ID { get; set; }
            public long PERIOD_DATE { get; set; }
            public string? WS_NAME { get; set; }
        }

        private class AttRawRow
        {
            public int EMP_ID { get; set; }
            public string? EMP_CODE { get; set; }
            public string? FULL_NAME { get; set; }
            public decimal WORK_DAYS { get; set; }
            public decimal DAYS { get; set; }
            public decimal DAYSB { get; set; }
            public decimal OT_NORMAL_H { get; set; }
            public decimal OT_HOLIDAY_H { get; set; }
            public decimal OT_ADMIN_H { get; set; }
            public decimal LEAVE_DAYS { get; set; }
            public long PERF_AMOUNT { get; set; }
            public long TRANSP_AMOUNT { get; set; }
            public long KASR_OTHER { get; set; }
        }

        private class DecLineRow
        {
            public int EMP_ID { get; set; }
            public string ITEM_CODE { get; set; } = "";
            public string ITEM_NAME { get; set; } = "";
            public int ITEM_TYPE { get; set; }
            public int CALC_BASIS { get; set; }
            public bool INS_SUBJECT { get; set; }
            public bool TAX_SUBJECT { get; set; }
            public long AMOUNT { get; set; }
            public long? INS_OV { get; set; }
            public long? TAX_OV { get; set; }
            public long? BASIS_OV { get; set; }
        }

        private class AttValueRow
        {
            public int EMP_ID { get; set; }
            public string ITEM_CODE { get; set; } = "";
            public long VALUE { get; set; }
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
                    // ۱. 🚀 استفاده از UPDLOCK برای جلوگیری قطعی از Race Condition در کلیک‌های همزمان
                    var periodStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @PER_ID",
                        new { request.PER_ID }, tran);

                    if (periodStatus == null || periodStatus == 1)
                        throw new InvalidOperationException("دوره کارکرد هنوز باز است. لطفاً ابتدا در تب کارکرد، دکمه 'بستن کارکرد' را بزنید.");

                    // ۲. بررسی وضعیت آخرین فیش با UPDLOCK
                    var latestRunStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_RUN WITH (UPDLOCK) WHERE PER_ID = @PER_ID AND IS_LATEST = 1",
                        new { request.PER_ID }, tran);

                    if (latestRunStatus >= 2)
                        throw new InvalidOperationException("برای این دوره فیش حقوقیِ تأیید شده وجود دارد. امکان بازمحاسبه نیست.");

                    // ۳. فراخوانی SP محاسبات (با افزایش Timeout به ۱۸۰ ثانیه برای کارگاه‌های بزرگ)
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

                    // 🚀 اعمال Command Timeout
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
                    // ۱. کنترل وضعیت با قفل انحصاری
                    var runInfo = await conn.QuerySingleAsync(
                        "SELECT STATUS, PER_ID FROM PAY2_RUN WITH (UPDLOCK) WHERE RUN_ID = @runId",
                        new { runId }, tran);

                    if ((byte)runInfo.STATUS != 2)
                        throw new InvalidOperationException("اجرا باید در وضعیت 'تأیید نهایی' باشد تا سند صادر شود.");

                    int perId = (int)runInfo.PER_ID;

                    // ۲. گرفتن تاریخ دوره برای ثبت در هدر سند
                    long periodDate = await conn.QuerySingleAsync<long>(
                        "SELECT PERIOD_DATE FROM PAY2_PERIOD WHERE PER_ID = @perId",
                        new { perId }, tran);

                    // ۳. گرفتن شماره سند جدید از سیستم حسابداری (DEED_HED)
                    double nextNs = (await conn.QuerySingleOrDefaultAsync<double?>(
                        "SELECT MAX(N_S) FROM DEED_HED WITH (UPDLOCK)", null, tran) ?? 0) + 1;

                    // ۴. ایجاد هدر سند حقوق در حسابداری
                    string hedSharh = $"سند حقوق و دستمزد دوره {periodDate}";
                    await conn.ExecuteAsync(@"
                        INSERT INTO DEED_HED (N_S, DATE_S, SHARH_S, NO_S, USER_NAME, OKF, CRT, UID)
                        VALUES (@N_S, @DATE_S, @SHARH, 2, @USER, 1, GETDATE(), @UID)",
                        new { N_S = nextNs, DATE_S = periodDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);

                    // ۵. فراخوانی SP تولید آرتیکل‌های سند
                    var articles = await conn.QueryAsync(
                        "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @CALC_BY = @userCod",
                        new { runId, userCod }, tran, commandTimeout: 120);

                    // ۶. درج آرتیکل‌ها در ریز سند (DEED_DTL)
                    int radif = 1;
                    foreach (var art in articles)
                    {
                        string hesCode = (string)art.HES_CODE;
                        var parts = hesCode.Split('-');

                        int hesK = int.Parse(parts[0]);
                        int hesM = int.Parse(parts[1]);
                        int? hesT = null;

                        // اگر SP پرسنل را مشخص کرده (مثلاً برای مساعده)، کد تفصیلی او را از جدول پرسنل استخراج می‌کنیم
                        if (art.EMP_ID != null)
                        {
                            string? accT = await conn.QuerySingleOrDefaultAsync<string>(
                                "SELECT ACC_T FROM PAY2_EMPLOYEE WHERE EMP_ID = @empId",
                                new { empId = (int)art.EMP_ID }, tran);

                            if (!string.IsNullOrWhiteSpace(accT) && int.TryParse(accT, out int tValue))
                                hesT = tValue;
                        }

                        await conn.ExecuteAsync(@"
                            INSERT INTO DEED_DTL (N_S, RADIF, HES_K, HES_M, HES_T, SHARH, BED, BES, CRT, UID)
                            VALUES (@N_S, @RADIF, @HES_K, @HES_M, @HES_T, @SHARH, @BED, @BES, GETDATE(), @UID)",
                            new
                            {
                                N_S = nextNs,
                                RADIF = radif++,
                                HES_K = hesK,
                                HES_M = hesM,
                                HES_T = hesT,
                                SHARH = (string)art.SHARH,
                                BED = (double)art.BED,
                                BES = (double)art.BES,
                                UID = userCod
                            }, tran);
                    }

                    // ۷. آپدیت وضعیت‌های سیستم حقوق
                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN SET STATUS = 3, DEED_ID_SAL = @deedId WHERE RUN_ID = @runId;
                        UPDATE PAY2_PERIOD SET STATUS = 4, DEED_N_S_PAY = @nextNs WHERE PER_ID = @perId;",
                        new { runId, deedId = (int)nextNs, nextNs, perId }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در صدور سند: " + ex.Message); }
        }
    }
}