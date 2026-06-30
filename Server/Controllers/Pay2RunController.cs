using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using Safir.Server.Reports;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using Safir.Shared.Models.Salary.Reports;
using Safir.Shared.Utility;
using System.Globalization;
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



        [HttpGet("{runId:int}/excel-audit")]
        public async Task<IActionResult> GetExcelAudit(int runId)
        {
            var head = await _db.DoGetDataSQLAsyncSingle<RunAuditHead>(@"
                SELECT R.RUN_ID, R.PER_ID, P.WS_ID, P.PERIOD_DATE, W.WS_NAME
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId", new { runId });

            if (head == null)
                return NotFound("اجرای حقوق برای خروجی اکسل یافت نشد.");

            var columns = (await _db.DoGetDataSQLAsync<RunAuditColumn>(@"
                SELECT DISTINCT I.ITEM_ID, I.ITEM_CODE, I.ITEM_NAME, I.SORT_ORDER, I.INS_SUBJECT, I.TAX_SUBJECT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER", new { runId })).ToList();

            var lines = (await _db.DoGetDataSQLAsync<RunAuditLine>(@"
                SELECT RL.*, E.EMP_CODE, E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
                       E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.INS_TYPE,
                       ISNULL(A.OT_NORMAL_H,0) AS OT_NORMAL_H, ISNULL(A.OT_HOLIDAY_H,0) AS OT_HOLIDAY_H,
                       ISNULL(A.OT_ADMIN_H,0) AS OT_ADMIN_H, ISNULL(A.LEAVE_DAYS,0) AS LEAVE_DAYS,
                       ISNULL(A.ABSENT_DAYS,0) AS ABSENT_DAYS, ISNULL(A.MISSION_DAYS,0) AS MISSION_DAYS,
                       ISNULL(A.PERF_AMOUNT,0) AS PERF_AMOUNT, ISNULL(A.TRANSP_AMOUNT,0) AS TRANSP_AMOUNT,
                       ISNULL(A.KASR_OTHER,0) AS KASR_OTHER
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                LEFT JOIN PAY2_ATTENDANCE A WITH (NOLOCK) ON A.PER_ID = @perId AND A.EMP_ID = RL.EMP_ID
                WHERE RL.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME", new { runId, perId = head.PER_ID })).ToList();

            var details = await _db.DoGetDataSQLAsync<RunDetailFlat>(@"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId", new { runId });
            var detailMap = details.GroupBy(x => x.EMP_ID).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ITEM_CODE, x => x.AMOUNT));

            var configs = (await _db.DoGetDataSQLAsync<Pay2ConfigDto>(@"
                SELECT CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL
                FROM PAY2_CONFIG WITH (NOLOCK)
                ORDER BY CFG_SECTION, CFG_KEY")).ToList();

            short taxYear = (short)(head.PERIOD_DATE / 10000);
            var taxBrackets = (await _db.DoGetDataSQLAsync<Pay2TaxBracketDto>(@"
                SELECT BRK_ID, TAX_YEAR, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER
                FROM PAY2_TAX_BRACKET WITH (NOLOCK)
                WHERE TAX_YEAR = @taxYear
                ORDER BY SORT_ORDER", new { taxYear })).ToList();

            using var wb = new XLWorkbook();
            wb.CalculateMode = XLCalculateMode.Auto;
            var cfg = wb.Worksheets.Add("تنظیمات");
            var raw = wb.Worksheets.Add("داده خام");
            var pay = wb.Worksheets.Add("فیش حقوقی");
            var ctl = wb.Worksheets.Add("کنترل تطابق");
            cfg.RightToLeft = raw.RightToLeft = pay.RightToLeft = ctl.RightToLeft = true;

            cfg.Cell(1, 1).Value = "کلید"; cfg.Cell(1, 2).Value = "مقدار"; cfg.Cell(1, 3).Value = "عنوان";
            for (int i = 0; i < configs.Count; i++)
            {
                cfg.Cell(i + 2, 1).Value = configs[i].CFG_KEY;
                cfg.Cell(i + 2, 2).Value = configs[i].CFG_VALUE;
                cfg.Cell(i + 2, 3).Value = configs[i].LABEL_FA;
            }
            int taxStart = configs.Count + 4;
            cfg.Cell(taxStart, 1).Value = "پله‌های مالیاتی";
            cfg.Cell(taxStart + 1, 1).Value = "سقف"; cfg.Cell(taxStart + 1, 2).Value = "نرخ"; cfg.Cell(taxStart + 1, 3).Value = "مالیات ثابت";
            for (int i = 0; i < taxBrackets.Count; i++)
            {
                cfg.Cell(taxStart + 2 + i, 1).Value = taxBrackets[i].UPPER_LIMIT;
                cfg.Cell(taxStart + 2 + i, 2).Value = taxBrackets[i].RATE_PCT / 100m;
                cfg.Cell(taxStart + 2 + i, 3).Value = taxBrackets[i].FIXED_TAX;
            }

            var rawHeaders = new List<string> { "کد", "نام", "معاف از مالیات", "درصد منطقه محروم", "نوع بیمه", "روز کارکرد", "اضافه‌کاری عادی", "اضافه‌کاری تعطیل", "اضافه‌کاری اداری", "مرخصی", "غیبت", "ماموریت", "مبلغ کارانه", "ایاب ذهاب", "سایر کسورات خام" };
            rawHeaders.AddRange(columns.Select(c => c.ITEM_NAME));
            rawHeaders.AddRange(new[] { "ناخالص موتور", "مبنای بیمه موتور", "بیمه موتور", "مبنای مالیات موتور", "مالیات موتور", "وام", "مساعده", "سایر کسورات", "کل کسورات موتور", "خالص موتور" });
            for (int c = 0; c < rawHeaders.Count; c++) raw.Cell(1, c + 1).Value = rawHeaders[c];
            for (int r = 0; r < lines.Count; r++)
            {
                var l = lines[r]; int rr = r + 2; int c = 1;
                raw.Cell(rr, c++).Value = l.EMP_CODE; raw.Cell(rr, c++).Value = l.FULL_NAME; raw.Cell(rr, c++).Value = l.TAX_EXEMPT ? 1 : 0; raw.Cell(rr, c++).Value = l.REGION_DEPRIVATION; raw.Cell(rr, c++).Value = l.INS_TYPE; raw.Cell(rr, c++).Value = l.WORK_DAYS;
                raw.Cell(rr, c++).Value = l.OT_NORMAL_H; raw.Cell(rr, c++).Value = l.OT_HOLIDAY_H; raw.Cell(rr, c++).Value = l.OT_ADMIN_H;
                raw.Cell(rr, c++).Value = l.LEAVE_DAYS; raw.Cell(rr, c++).Value = l.ABSENT_DAYS; raw.Cell(rr, c++).Value = l.MISSION_DAYS;
                raw.Cell(rr, c++).Value = l.PERF_AMOUNT; raw.Cell(rr, c++).Value = l.TRANSP_AMOUNT; raw.Cell(rr, c++).Value = l.KASR_OTHER;
                detailMap.TryGetValue(l.EMP_ID, out var empDetails);
                foreach (var col in columns) raw.Cell(rr, c++).Value = empDetails != null && empDetails.TryGetValue(col.ITEM_CODE, out var amount) ? amount : 0;
                raw.Cell(rr, c++).Value = l.GROSS_PAY; raw.Cell(rr, c++).Value = l.INS_BASE; raw.Cell(rr, c++).Value = l.INS_WORKER; raw.Cell(rr, c++).Value = l.TAX_BASE;
                raw.Cell(rr, c++).Value = l.TAX_AMOUNT; raw.Cell(rr, c++).Value = l.LOAN_DED; raw.Cell(rr, c++).Value = l.ADVANCE_DED; raw.Cell(rr, c++).Value = l.OTHER_DED;
                raw.Cell(rr, c++).Value = l.TOTAL_DED; raw.Cell(rr, c++).Value = l.NET_PAY;
            }

            string[] payHeaders = { "کد", "نام", "روز کارکرد", "ناخالص فرمولی", "مبنای بیمه فرمولی", "بیمه کارگر فرمولی", "مشمول مالیات قبل معافیت", "مبنای مالیات فرمولی", "مالیات فرمولی", "کل کسورات فرمولی", "خالص پرداختی فرمولی", "شرح فرمول" };
            for (int c = 0; c < payHeaders.Length; c++) pay.Cell(1, c + 1).Value = payHeaders[c];
            int itemStartCol = 16;
            int grossRawCol = 15 + columns.Count + 1;
            int loanRawCol = grossRawCol + 5;
            int advRawCol = grossRawCol + 6;
            int otherDedRawCol = grossRawCol + 7;
            for (int r = 0; r < lines.Count; r++)
            {
                int rr = r + 2;
                pay.Cell(rr, 1).FormulaA1 = $"'داده خام'!A{rr}";
                pay.Cell(rr, 2).FormulaA1 = $"'داده خام'!B{rr}";
                pay.Cell(rr, 3).FormulaA1 = $"'داده خام'!F{rr}";
                string itemRange = $"'داده خام'!{ColLetter(itemStartCol)}{rr}:{ColLetter(itemStartCol + columns.Count - 1)}{rr}";
                pay.Cell(rr, 4).FormulaA1 = columns.Count > 0 ? $"ROUND(SUM({itemRange}),0)" : "0";
                pay.Cell(rr, 5).FormulaA1 = BuildInsuranceBaseFormula(columns, rr, itemStartCol);
                pay.Cell(rr, 6).FormulaA1 = $"IF('داده خام'!E{rr}=3,0,ROUND(E{rr}*IFERROR(VLOOKUP(\"INS_WORKER_RATE\",'تنظیمات'!A:B,2,FALSE)/100,0.07),0))";
                pay.Cell(rr, 7).FormulaA1 = BuildSubjectSumFormula(columns, rr, itemStartCol, x => x.TAX_SUBJECT);
                pay.Cell(rr, 8).FormulaA1 = $"IF('داده خام'!C{rr}=1,0,ROUND(MAX(0,(G{rr}-IF(IFERROR(VLOOKUP(\"TAX_DEDUCT_INS\",'تنظیمات'!A:B,2,FALSE),1)=1,F{rr},0)-IFERROR(VLOOKUP(\"TAX_EXEMPT_MONTHLY\",'تنظیمات'!A:B,2,FALSE),0))*IF(IFERROR(VLOOKUP(\"TAX_DEPRIVATION_APPLY\",'تنظیمات'!A:B,2,FALSE),1)=1,1-'داده خام'!D{rr}/100,1)),0))";
                pay.Cell(rr, 9).FormulaA1 = GenerateMonthlyTaxFormula($"H{rr}", taxBrackets);
                pay.Cell(rr, 10).FormulaA1 = $"ROUND(F{rr}+I{rr}+'داده خام'!{ColLetter(loanRawCol)}{rr}+'داده خام'!{ColLetter(advRawCol)}{rr}+'داده خام'!{ColLetter(otherDedRawCol)}{rr},0)";
                pay.Cell(rr, 11).FormulaA1 = $"ROUND((D{rr}-J{rr})/IFERROR(VLOOKUP(\"ROUND_MODE\",'تنظیمات'!A:B,2,FALSE),1),0)*IFERROR(VLOOKUP(\"ROUND_MODE\",'تنظیمات'!A:B,2,FALSE),1)";
                pay.Cell(rr, 12).Value = "بیمه: MIN اقلام مشمول با سقف INS_CEILING؛ مالیات: اقلام مشمول - بیمه در صورت TAX_DEDUCT_INS - معافیت - منطقه؛ سپس مالیات سالانه/۱۲";
            }

            string[] ctlHeaders = { "کد", "نام", "اختلاف ناخالص", "اختلاف بیمه", "اختلاف مالیات", "اختلاف خالص", "وضعیت" };
            for (int c = 0; c < ctlHeaders.Length; c++) ctl.Cell(1, c + 1).Value = ctlHeaders[c];
            for (int r = 0; r < lines.Count; r++)
            {
                int rr = r + 2;
                ctl.Cell(rr, 1).FormulaA1 = $"'فیش حقوقی'!A{rr}";
                ctl.Cell(rr, 2).FormulaA1 = $"'فیش حقوقی'!B{rr}";
                ctl.Cell(rr, 3).FormulaA1 = $"'فیش حقوقی'!D{rr}-'داده خام'!{ColLetter(grossRawCol)}{rr}";
                ctl.Cell(rr, 4).FormulaA1 = $"'فیش حقوقی'!F{rr}-'داده خام'!{ColLetter(grossRawCol + 2)}{rr}";
                ctl.Cell(rr, 5).FormulaA1 = $"'فیش حقوقی'!I{rr}-'داده خام'!{ColLetter(grossRawCol + 4)}{rr}";
                ctl.Cell(rr, 6).FormulaA1 = $"'فیش حقوقی'!K{rr}-'داده خام'!{ColLetter(grossRawCol + 9)}{rr}";
                ctl.Cell(rr, 7).FormulaA1 = $"IF(MAX(ABS(C{rr}),ABS(D{rr}),ABS(E{rr}),ABS(F{rr}))<=10,\"OK\",\"CHECK\")";
            }

            foreach (var ws in wb.Worksheets) { ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(1); }
            cfg.Hide(); raw.Hide();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Payroll_Audit_{head.PERIOD_DATE}_{runId}.xlsx");
        }

        // کلاس کمکی برای Dapper
        private class RunDetailFlat
        {
            public int EMP_ID { get; set; }
            public string ITEM_CODE { get; set; } = "";
            public long AMOUNT { get; set; }
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




        private static string BuildInsuranceBaseFormula(List<RunAuditColumn> columns, int row, int firstRawItemCol)
        {
            string subjectSum = BuildSubjectSumExpression(columns, row, firstRawItemCol, x => x.INS_SUBJECT);
            string ceiling = $"IF(IFERROR(VLOOKUP(\"INS_CEILING_APPLY\",'تنظیمات'!A:B,2,FALSE),1)=1,IFERROR(VLOOKUP(\"INS_CEILING_MONTHLY\",'تنظیمات'!A:B,2,FALSE),{subjectSum})*'داده خام'!F{row}/30,{subjectSum})";
            return $"IF('داده خام'!E{row}=3,0,ROUND(MIN({subjectSum},{ceiling}),0))";
        }

        private static string BuildSubjectSumExpression(List<RunAuditColumn> columns, int row, int firstRawItemCol, Func<RunAuditColumn, bool> subject)
        {
            var refs = columns.Select((c, i) => new { c, i }).Where(x => subject(x.c)).Select(x => $"'داده خام'!{ColLetter(firstRawItemCol + x.i)}{row}").ToList();
            return refs.Count == 0 ? "0" : $"SUM({string.Join(",", refs)})";
        }

        private static string BuildSubjectSumFormula(List<RunAuditColumn> columns, int row, int firstRawItemCol, Func<RunAuditColumn, bool> subject)
        {
            return $"ROUND({BuildSubjectSumExpression(columns, row, firstRawItemCol, subject)},0)";
        }

        private static string GenerateMonthlyTaxFormula(string monthlyBaseRef, List<Pay2TaxBracketDto> brackets)
        {
            var ordered = brackets.OrderBy(x => x.SORT_ORDER).ToList();
            if (ordered.Count == 0) return "0";

            string annualBaseRef = $"({monthlyBaseRef}*12)";
            var highest = ordered[^1];
            string expr = TaxBracketFormulaPart(annualBaseRef, brackets, highest);
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                var b = ordered[i];
                string upper = b.UPPER_LIMIT.ToString(CultureInfo.InvariantCulture);
                expr = $"IF({annualBaseRef}<={upper},{TaxBracketFormulaPart(annualBaseRef, brackets, b)},{expr})";
            }

            return $"ROUND(({expr})/12,0)";
        }

        private static string TaxBracketFormulaPart(string baseRef, List<Pay2TaxBracketDto> brackets, Pay2TaxBracketDto bracket)
        {
            string rate = (bracket.RATE_PCT / 100m).ToString(CultureInfo.InvariantCulture);
            string fixedTax = bracket.FIXED_TAX.ToString(CultureInfo.InvariantCulture);
            return $"{fixedTax}+MAX(0,{baseRef}-{PreviousLimit(brackets, bracket)})*{rate}";
        }

        private static string PreviousLimit(List<Pay2TaxBracketDto> brackets, Pay2TaxBracketDto current)
        {
            var prev = brackets.Where(x => x.SORT_ORDER < current.SORT_ORDER).OrderByDescending(x => x.SORT_ORDER).FirstOrDefault();
            return (prev?.UPPER_LIMIT ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string ColLetter(int index)
        {
            var sb = new System.Text.StringBuilder();
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                index = (index - 1) / 26;
            }
            return sb.ToString();
        }

        private class RunAuditHead { public int RUN_ID { get; set; } public int PER_ID { get; set; } public int WS_ID { get; set; } public long PERIOD_DATE { get; set; } public string WS_NAME { get; set; } = ""; }
        private class RunAuditColumn { public int ITEM_ID { get; set; } public string ITEM_CODE { get; set; } = ""; public string ITEM_NAME { get; set; } = ""; public int SORT_ORDER { get; set; } public bool INS_SUBJECT { get; set; } public bool TAX_SUBJECT { get; set; } }
        private class RunAuditLine : Pay2RunLineDto
        {
            public decimal OT_NORMAL_H { get; set; } public decimal OT_HOLIDAY_H { get; set; } public decimal OT_ADMIN_H { get; set; }
            public decimal LEAVE_DAYS { get; set; } public decimal ABSENT_DAYS { get; set; } public decimal MISSION_DAYS { get; set; }
            public bool TAX_EXEMPT { get; set; } public decimal REGION_DEPRIVATION { get; set; } public byte INS_TYPE { get; set; }
            public long PERF_AMOUNT { get; set; } public long TRANSP_AMOUNT { get; set; } public long KASR_OTHER { get; set; }
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