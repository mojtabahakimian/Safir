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
                SELECT DISTINCT I.ITEM_ID, I.ITEM_CODE, I.ITEM_NAME, I.ITEM_TYPE, I.SORT_ORDER, I.INS_SUBJECT, I.TAX_SUBJECT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER", new { runId })).ToList();

            var lines = (await _db.DoGetDataSQLAsync<RunAuditLine>(@"
                SELECT RL.*, E.EMP_CODE, E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
                       E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.INS_TYPE,
                       ISNULL(A.OT_NORMAL_H,0) AS OT_NORMAL_H, ISNULL(A.OT_HOLIDAY_H,0) AS OT_HOLIDAY_H,
                       ISNULL(A.OT_ADMIN_H,0) AS OT_ADMIN_H, ISNULL(A.DAYS,0) AS DAYS, ISNULL(A.DAYSB,0) AS DAYSB, ISNULL(A.FRID_COUNT,0) AS FRID_COUNT, ISNULL(A.TDAYS,0) AS TDAYS, ISNULL(A.LEAVE_DAYS,0) AS LEAVE_DAYS,
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

            var configMap = configs.ToDictionary(x => x.CFG_KEY, x => x.CFG_VALUE, StringComparer.OrdinalIgnoreCase);
            int monthDays = ResolveMonthDays(head.PERIOD_DATE, configMap);
            int periodStart = (int)(head.PERIOD_DATE + 1);
            int periodEnd = (int)(head.PERIOD_DATE + monthDays);

            var decreeTraceRows = (await _db.DoGetDataSQLAsync<RunAuditDecreeRow>(@"
                SELECT E.EMP_ID, E.EMP_CODE, E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
                       D.DEC_ID, D.EFF_FROM, ISNULL(D.EFF_TO, 99991231) AS EFF_TO,
                       I.ITEM_CODE, I.ITEM_NAME, I.ITEM_TYPE, ISNULL(DL.AMOUNT,0) AS DEC_AMOUNT,
                       ISNULL(OV.BASIS_OV, ISNULL(DL.BASIS_OV, I.CALC_BASIS)) AS EFFECTIVE_BASIS,
                       ISNULL(OV.INS_OV, ISNULL(DL.INS_OV, I.INS_SUBJECT)) AS EFFECTIVE_INS,
                       ISNULL(OV.TAX_OV, ISNULL(DL.TAX_OV, I.TAX_SUBJECT)) AS EFFECTIVE_TAX,
                       I.PAY_BASE_DAYS, I.INS_BASE_DAYS,
                       ISNULL(A.DAYS,0) AS DAYS, ISNULL(A.DAYSB,0) AS DAYSB, ISNULL(A.FRID_COUNT,0) AS FRID_COUNT,
                       ISNULL(A.TDAYS,0) AS TDAYS, ISNULL(A.LEAVE_DAYS,0) AS LEAVE_DAYS,
                       ISNULL(A.OT_NORMAL_H,0) AS OT_NORMAL_H, ISNULL(A.OT_HOLIDAY_H,0) AS OT_HOLIDAY_H, ISNULL(A.OT_ADMIN_H,0) AS OT_ADMIN_H,
                       COALESCE(NULLIF(DL.SHIFT_MODE_OV, N''), NULLIF(D.SHIFT_MODE, N''), NULLIF(W.SHIFT_MODE, N''), @shiftMode, N'PCT') AS EFFECTIVE_SHIFT_MODE,
                       ISNULL(BaseLine.CURRENT_DEC_DAILY_BASE,0) AS CURRENT_DEC_DAILY_BASE,
                       CASE WHEN OV.ITEM_ID IS NULL THEN 0 ELSE 1 END AS HAS_OVERRIDE
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON E.EMP_ID = RL.EMP_ID
                INNER JOIN PAY2_ATTENDANCE A WITH (NOLOCK) ON A.PER_ID = @perId AND A.EMP_ID = RL.EMP_ID
                INNER JOIN PAY2_DECREE D WITH (NOLOCK) ON D.EMP_ID = RL.EMP_ID AND D.IS_CONFIRMED = 1
                    AND D.EFF_FROM <= @periodEnd AND (D.EFF_TO IS NULL OR D.EFF_TO >= @periodStart)
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON W.WS_ID = @wsId
                INNER JOIN PAY2_DECREE_LINE DL WITH (NOLOCK) ON DL.DEC_ID = D.DEC_ID
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON I.ITEM_ID = DL.ITEM_ID AND I.IS_ACTIVE = 1
                OUTER APPLY (
                    SELECT TOP 1 O.ITEM_ID, O.INS_OV, O.TAX_OV, O.BASIS_OV
                    FROM PAY2_OVERRIDE O WITH (NOLOCK)
                    WHERE O.EMP_ID = E.EMP_ID AND O.ITEM_ID = DL.ITEM_ID
                      AND O.VALID_FROM <= @periodDate AND (O.VALID_TO IS NULL OR O.VALID_TO >= @periodDate)
                    ORDER BY O.VALID_FROM DESC
                ) OV
                OUTER APPLY (
                    SELECT TOP 1 DL2.AMOUNT AS CURRENT_DEC_DAILY_BASE
                    FROM PAY2_DECREE_LINE DL2 WITH (NOLOCK)
                    INNER JOIN PAY2_ITEM_DEF ID2 WITH (NOLOCK) ON ID2.ITEM_ID = DL2.ITEM_ID
                    WHERE DL2.DEC_ID = D.DEC_ID AND ID2.ITEM_CODE IN ('BASE_SAL', 'BASE_SAL_B')
                    ORDER BY CASE WHEN ID2.ITEM_CODE = 'BASE_SAL_B' THEN 1 ELSE 2 END
                ) BaseLine
                WHERE RL.RUN_ID = @runId AND I.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                ORDER BY E.EMP_CODE, D.EFF_FROM, I.SORT_ORDER", new
            {
                runId,
                perId = head.PER_ID,
                wsId = head.WS_ID,
                periodStart,
                periodEnd,
                periodDate = head.PERIOD_DATE,
                shiftMode = GetConfigText(configMap, "SHIFT_MODE", "PCT")
            })).ToList();

            using var wb = new XLWorkbook();
            wb.CalculateMode = XLCalculateMode.Auto;
            var cfg = wb.Worksheets.Add("تنظیمات");
            var raw = wb.Worksheets.Add("داده خام");
            var decreeTrace = wb.Worksheets.Add("ردیابی حکم");
            var trace = wb.Worksheets.Add("ردیابی اقلام");
            var deductionTrace = wb.Worksheets.Add("ردیابی کسورات");
            var pay = wb.Worksheets.Add("فیش حقوقی");
            var ctl = wb.Worksheets.Add("کنترل تطابق");
            cfg.RightToLeft = raw.RightToLeft = decreeTrace.RightToLeft = trace.RightToLeft = deductionTrace.RightToLeft = pay.RightToLeft = ctl.RightToLeft = true;

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

            var rawHeaders = new List<string> { "کد", "نام", "معاف از مالیات", "درصد منطقه محروم", "نوع بیمه", "روز کارکرد", "روز اسمی", "روز رسمی", "جمعه", "روز غذا/تولید", "اضافه‌کاری عادی", "اضافه‌کاری تعطیل", "اضافه‌کاری اداری", "مرخصی", "غیبت", "ماموریت", "مبلغ کارانه", "ایاب ذهاب", "سایر کسورات خام" };
            rawHeaders.AddRange(columns.Select(c => c.ITEM_NAME));
            rawHeaders.AddRange(new[] { "ناخالص موتور", "مبنای بیمه موتور", "بیمه موتور", "مبنای مالیات موتور", "مالیات موتور", "وام", "مساعده", "سایر کسورات", "کل کسورات موتور", "خالص موتور" });
            for (int c = 0; c < rawHeaders.Count; c++) raw.Cell(1, c + 1).Value = rawHeaders[c];
            for (int r = 0; r < lines.Count; r++)
            {
                var l = lines[r]; int rr = r + 2; int c = 1;
                raw.Cell(rr, c++).Value = l.EMP_CODE; raw.Cell(rr, c++).Value = l.FULL_NAME; raw.Cell(rr, c++).Value = l.TAX_EXEMPT ? 1 : 0; raw.Cell(rr, c++).Value = l.REGION_DEPRIVATION; raw.Cell(rr, c++).Value = l.INS_TYPE; raw.Cell(rr, c++).Value = l.WORK_DAYS; raw.Cell(rr, c++).Value = l.DAYS; raw.Cell(rr, c++).Value = l.DAYSB; raw.Cell(rr, c++).Value = l.FRID_COUNT; raw.Cell(rr, c++).Value = l.TDAYS;
                raw.Cell(rr, c++).Value = l.OT_NORMAL_H; raw.Cell(rr, c++).Value = l.OT_HOLIDAY_H; raw.Cell(rr, c++).Value = l.OT_ADMIN_H;
                raw.Cell(rr, c++).Value = l.LEAVE_DAYS; raw.Cell(rr, c++).Value = l.ABSENT_DAYS; raw.Cell(rr, c++).Value = l.MISSION_DAYS;
                raw.Cell(rr, c++).Value = l.PERF_AMOUNT; raw.Cell(rr, c++).Value = l.TRANSP_AMOUNT; raw.Cell(rr, c++).Value = l.KASR_OTHER;
                detailMap.TryGetValue(l.EMP_ID, out var empDetails);
                foreach (var col in columns) raw.Cell(rr, c++).Value = empDetails != null && empDetails.TryGetValue(col.ITEM_CODE, out var amount) ? amount : 0;
                raw.Cell(rr, c++).Value = l.GROSS_PAY; raw.Cell(rr, c++).Value = l.INS_BASE; raw.Cell(rr, c++).Value = l.INS_WORKER; raw.Cell(rr, c++).Value = l.TAX_BASE;
                raw.Cell(rr, c++).Value = l.TAX_AMOUNT; raw.Cell(rr, c++).Value = l.LOAN_DED; raw.Cell(rr, c++).Value = l.ADVANCE_DED; raw.Cell(rr, c++).Value = l.OTHER_DED;
                raw.Cell(rr, c++).Value = l.TOTAL_DED; raw.Cell(rr, c++).Value = l.NET_PAY;
            }

            int itemStartCol = 20;
            int grossRawCol = 19 + columns.Count + 1;
            int loanRawCol = grossRawCol + 5;
            int advRawCol = grossRawCol + 6;
            int otherDedRawCol = grossRawCol + 7;

            string[] decreeHeaders = { "کد", "نام", "شناسه حکم", "از", "تا", "کد آیتم", "نام آیتم", "مبلغ حکم", "مبنا", "روز پرداخت", "روز بیمه", "روز فعال", "روز ماه", "ضریب حکم", "PAY_DAYS", "INS_DAYS", "BASE_DAYS", "INS_DAYS_RAW", "شیفت", "پایه شیفت", "Override", "مبلغ فرمولی", "فرمول", "شرح" };
            for (int c = 0; c < decreeHeaders.Length; c++) decreeTrace.Cell(1, c + 1).Value = decreeHeaders[c];
            for (int i = 0; i < decreeTraceRows.Count; i++)
            {
                var d = decreeTraceRows[i];
                int rr = i + 2;
                int activeStart = Math.Max(d.EFF_FROM, periodStart);
                int activeEnd = Math.Min(d.EFF_TO, periodEnd);
                int activeDays = activeStart <= activeEnd ? (activeEnd % 100) - (activeStart % 100) + 1 : 0;

                decreeTrace.Cell(rr, 1).Value = d.EMP_CODE; decreeTrace.Cell(rr, 2).Value = d.FULL_NAME; decreeTrace.Cell(rr, 3).Value = d.DEC_ID;
                decreeTrace.Cell(rr, 4).Value = d.EFF_FROM; decreeTrace.Cell(rr, 5).Value = d.EFF_TO; decreeTrace.Cell(rr, 6).Value = d.ITEM_CODE;
                decreeTrace.Cell(rr, 7).Value = d.ITEM_NAME; decreeTrace.Cell(rr, 8).Value = d.DEC_AMOUNT; decreeTrace.Cell(rr, 9).Value = d.EFFECTIVE_BASIS;
                decreeTrace.Cell(rr, 10).Value = d.PAY_BASE_DAYS; decreeTrace.Cell(rr, 11).Value = d.INS_BASE_DAYS; decreeTrace.Cell(rr, 12).Value = activeDays;
                decreeTrace.Cell(rr, 13).Value = monthDays; decreeTrace.Cell(rr, 14).FormulaA1 = $"L{rr}/M{rr}";
                decreeTrace.Cell(rr, 15).FormulaA1 = $"IF(J{rr}=1,{X(d.DAYS)},{X(d.DAYSB)})*N{rr}";
                decreeTrace.Cell(rr, 16).FormulaA1 = $"IF(K{rr}=1,{X(d.DAYS)},{X(d.DAYSB)})*N{rr}";
                decreeTrace.Cell(rr, 17).FormulaA1 = $"IF(J{rr}=1,{X(d.DAYS)},{X(d.DAYSB)})";
                decreeTrace.Cell(rr, 18).FormulaA1 = $"IF(K{rr}=1,{X(d.DAYS)},{X(d.DAYSB)})";
                decreeTrace.Cell(rr, 19).Value = d.EFFECTIVE_SHIFT_MODE; decreeTrace.Cell(rr, 20).Value = d.CURRENT_DEC_DAILY_BASE; decreeTrace.Cell(rr, 21).Value = d.HAS_OVERRIDE;
                decreeTrace.Cell(rr, 22).FormulaA1 = BuildDecreeTraceFormula(rr, d);
                decreeTrace.Cell(rr, 23).FormulaA1 = $"FORMULATEXT(V{rr})";
                decreeTrace.Cell(rr, 24).Value = BuildDecreeTraceDescription(d);
            }

            string[] traceHeaders = { "کد", "نام", "کد آیتم", "نام آیتم", "نوع آیتم", "مبلغ موتور", "مبلغ فرمولی", "فرمول قابل مشاهده", "شرح مسیر", "مشمول بیمه", "مشمول مالیات" };
            for (int c = 0; c < traceHeaders.Length; c++) trace.Cell(1, c + 1).Value = traceHeaders[c];
            int traceRow = 2;
            for (int r = 0; r < lines.Count; r++)
            {
                int rawRow = r + 2;
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    int itemRawCol = itemStartCol + i;
                    string rawRef = $"'داده خام'!{ColLetter(itemRawCol)}{rawRow}";
                    trace.Cell(traceRow, 1).FormulaA1 = $"'داده خام'!A{rawRow}";
                    trace.Cell(traceRow, 2).FormulaA1 = $"'داده خام'!B{rawRow}";
                    trace.Cell(traceRow, 3).Value = col.ITEM_CODE;
                    trace.Cell(traceRow, 4).Value = col.ITEM_NAME;
                    trace.Cell(traceRow, 5).Value = col.ITEM_TYPE;
                    trace.Cell(traceRow, 6).FormulaA1 = rawRef;
                    trace.Cell(traceRow, 7).FormulaA1 = BuildItemTraceFormula(col.ITEM_CODE, rawRow, rawRef, itemStartCol, columns);
                    trace.Cell(traceRow, 8).FormulaA1 = $"FORMULATEXT(G{traceRow})";
                    trace.Cell(traceRow, 9).Value = BuildItemTraceDescription(col.ITEM_CODE);
                    trace.Cell(traceRow, 10).Value = col.INS_SUBJECT ? 1 : 0;
                    trace.Cell(traceRow, 11).Value = col.TAX_SUBJECT ? 1 : 0;
                    traceRow++;
                }
            }

            string[] deductionHeaders = { "کد", "نام", "کد کسر", "عنوان کسر", "مبلغ موتور", "مبلغ فرمولی", "فرمول", "شرح" };
            for (int c = 0; c < deductionHeaders.Length; c++) deductionTrace.Cell(1, c + 1).Value = deductionHeaders[c];
            int deductionRow = 2;
            for (int r = 0; r < lines.Count; r++)
            {
                int rawRow = r + 2;
                AddDeductionTraceRow(deductionTrace, deductionRow++, rawRow, "INS_DED", "بیمه کارگر", $"'فیش حقوقی'!F{rawRow}", "مبنای بیمه × INS_WORKER_RATE");
                AddDeductionTraceRow(deductionTrace, deductionRow++, rawRow, "TAX_DED", "مالیات", $"'فیش حقوقی'!I{rawRow}", "مالیات پلکانی سالانه/۱۲ پس از معافیت‌ها");
                AddDeductionTraceRow(deductionTrace, deductionRow++, rawRow, "LOAN_DED", "قسط وام", $"'داده خام'!{ColLetter(loanRawCol)}{rawRow}", "جمع اقساط PAY2_LOAN_SCHED ثبت‌شده برای این Run/دوره");
                AddDeductionTraceRow(deductionTrace, deductionRow++, rawRow, "ADVANCE_DED", "مساعده", $"'داده خام'!{ColLetter(advRawCol)}{rawRow}", "خروجی مساعده هوشمند موتور در لحظه Run");
                AddDeductionTraceRow(deductionTrace, deductionRow++, rawRow, "OTHER_DED", "سایر کسورات", $"'داده خام'!{ColLetter(otherDedRawCol)}{rawRow}", "کسورات دستی/سایر از کارکرد ماه");
            }

            string[] payHeaders = { "کد", "نام", "روز کارکرد", "ناخالص فرمولی", "مبنای بیمه فرمولی", "بیمه کارگر فرمولی", "مشمول مالیات قبل معافیت", "مبنای مالیات فرمولی", "مالیات فرمولی", "کل کسورات فرمولی", "خالص پرداختی فرمولی", "شرح فرمول" };
            for (int c = 0; c < payHeaders.Length; c++) pay.Cell(1, c + 1).Value = payHeaders[c];
            for (int r = 0; r < lines.Count; r++)
            {
                int rr = r + 2;
                pay.Cell(rr, 1).FormulaA1 = $"'داده خام'!A{rr}";
                pay.Cell(rr, 2).FormulaA1 = $"'داده خام'!B{rr}";
                pay.Cell(rr, 3).FormulaA1 = $"'داده خام'!F{rr}";
                pay.Cell(rr, 4).FormulaA1 = columns.Count > 0 ? $"ROUND(SUMIFS('ردیابی اقلام'!G:G,'ردیابی اقلام'!A:A,A{rr},'ردیابی اقلام'!E:E,1)+SUMIFS('ردیابی اقلام'!G:G,'ردیابی اقلام'!A:A,A{rr},'ردیابی اقلام'!E:E,2)-SUMIFS('ردیابی اقلام'!G:G,'ردیابی اقلام'!A:A,A{rr},'ردیابی اقلام'!C:C,\"BASE_SAL_B\"),0)" : "0";
                pay.Cell(rr, 5).FormulaA1 = BuildInsuranceBaseFormula(columns, rr, itemStartCol);
                pay.Cell(rr, 6).FormulaA1 = $"IF('داده خام'!E{rr}=3,0,ROUND(E{rr}*IFERROR(VLOOKUP(\"INS_WORKER_RATE\",'تنظیمات'!A:B,2,FALSE)/100,0.07),0))";
                pay.Cell(rr, 7).FormulaA1 = BuildSubjectSumFormula(columns, rr, itemStartCol, x => x.TAX_SUBJECT);
                pay.Cell(rr, 8).FormulaA1 = $"IF('داده خام'!C{rr}=1,0,ROUND(MAX(0,(G{rr}-IF(IFERROR(VLOOKUP(\"TAX_DEDUCT_INS\",'تنظیمات'!A:B,2,FALSE),1)=1,F{rr},0)-IFERROR(VLOOKUP(\"TAX_EXEMPT_MONTHLY\",'تنظیمات'!A:B,2,FALSE),0))*IF(IFERROR(VLOOKUP(\"TAX_DEPRIVATION_APPLY\",'تنظیمات'!A:B,2,FALSE),1)=1,1-'داده خام'!D{rr}/100,1)),0))";
                pay.Cell(rr, 9).FormulaA1 = GenerateMonthlyTaxFormula($"H{rr}", taxBrackets);
                pay.Cell(rr, 10).FormulaA1 = $"ROUND(SUMIFS('ردیابی کسورات'!F:F,'ردیابی کسورات'!A:A,A{rr}),0)";
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






        private static int ResolveMonthDays(long periodDate, Dictionary<string, string> configMap)
        {
            string mode = GetConfigText(configMap, "MONTH_DAYS_MODE", "30");
            if (mode == "30") return 30;

            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            bool isLeap = ((25 * year + 11) % 33) < 8;
            if (month <= 6) return 31;
            if (month <= 11) return 30;
            return isLeap ? 30 : 29;
        }

        private static string GetConfigText(Dictionary<string, string> configMap, string key, string fallback)
            => configMap.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

        private static string BuildDecreeTraceFormula(int row, RunAuditDecreeRow d)
        {
            string monthlyProrate = "IFERROR(VLOOKUP(\"MONTHLY_ITEM_PRORATE\",'تنظیمات'!A:B,2,FALSE),0)";
            string naharDays = $"({X(d.DAYSB)}-{d.FRID_COUNT}-{X(d.LEAVE_DAYS)}+{X(d.TDAYS)})";
            return d.ITEM_CODE switch
            {
                "BASE_SAL" or "BASE_SAL_B" => $"ROUND(H{row}*O{row},0)",
                "HOME" or "CHILDREN" or "GROCERY" => $"ROUND(IF(Q{row}>=28,H{row},H{row}*(Q{row}/30))*N{row},0)",
                "NAHAR" => $"ROUND(IF({naharDays}*N{row}>0,H{row}*({naharDays}*N{row}),H{row}*O{row}),0)",
                "SHIFT" => $"IF(S{row}=\"FIXED\",ROUND(H{row}*(O{row}/M{row}),0),ROUND(T{row}*O{row}*H{row}/100,0))",
                "OT_NORMAL" when d.EFFECTIVE_BASIS == 3 => $"ROUND(H{row}*{X(d.OT_NORMAL_H)},0)",
                "OT_HOLIDAY" when d.EFFECTIVE_BASIS == 3 => $"ROUND(H{row}*{X(d.OT_HOLIDAY_H)},0)",
                "OT_ADMIN" when d.EFFECTIVE_BASIS == 3 => $"ROUND(H{row}*{X(d.OT_ADMIN_H)},0)",
                _ when d.EFFECTIVE_BASIS == 3 => $"ROUND(H{row}*O{row}*IFERROR(VLOOKUP(\"OT_HOUR_BASE\",'تنظیمات'!A:B,2,FALSE),7.33),0)",
                _ when d.EFFECTIVE_BASIS == 2 => $"IF({monthlyProrate}=1,ROUND(H{row}*(O{row}/M{row}),0),ROUND(H{row}*N{row},0))",
                _ when d.EFFECTIVE_BASIS == 1 => $"ROUND(H{row}*O{row},0)",
                _ => $"ROUND(H{row},0)"
            };
        }

        private static string X(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static string BuildDecreeTraceDescription(RunAuditDecreeRow d)
        {
            string ov = d.HAS_OVERRIDE == 1 ? " با اعمال Override" : " بدون Override";
            return d.ITEM_CODE switch
            {
                "BASE_SAL" or "BASE_SAL_B" => "مبلغ روزانه حکم × PAY_DAYS" + ov,
                "HOME" or "CHILDREN" or "GROCERY" => "مزایای ماهانه: اگر روز مبنا >= ۲۸ مبلغ کامل، وگرنه نسبت ۳۰ روز × ضریب حکم" + ov,
                "NAHAR" => "حق نهار: مبلغ × (DAYSB - جمعه - مرخصی + TDAYS) × ضریب حکم" + ov,
                "SHIFT" => "حق شیفت: حالت FIXED یا درصدی طبق اولویت خط حکم/حکم/کارگاه/تنظیمات" + ov,
                _ => $"محاسبه بر اساس CALC_BASIS={d.EFFECTIVE_BASIS} و PAY_BASE_DAYS/INS_BASE_DAYS" + ov
            };
        }

        private static string BuildItemTraceFormula(string itemCode, int rawRow, string rawRef, int itemStartCol, List<RunAuditColumn> columns)
        {
            string cfg = "'تنظیمات'!A:B";
            string baseSum = BuildItemCodeSumExpression(rawRow, itemStartCol, columns, "BASE_SAL", "BASE_SAL_B");
            string hourly = $"IF('داده خام'!H{rawRow}>0,(({baseSum})/'داده خام'!H{rawRow})/IFERROR(VLOOKUP(\"OT_HOUR_BASE\",{cfg},2,FALSE),7.33),0)";
            return itemCode switch
            {
                "OT_NORMAL" => $"ROUND({hourly}*'داده خام'!K{rawRow}*IFERROR(VLOOKUP(\"OT_NORMAL_MULT\",{cfg},2,FALSE),1.4),0)",
                "OT_HOLIDAY" => $"ROUND({hourly}*'داده خام'!L{rawRow}*IFERROR(VLOOKUP(\"OT_HOLIDAY_MULT\",{cfg},2,FALSE),1.4),0)",
                "OT_ADMIN" => $"ROUND({hourly}*'داده خام'!M{rawRow}*IFERROR(VLOOKUP(\"OT_NORMAL_MULT\",{cfg},2,FALSE),1.4),0)",
                "PERF_BONUS" => $"'داده خام'!Q{rawRow}",
                "TRANSP" => $"'داده خام'!R{rawRow}",
                "INS_DED" => BuildDeductionBackedFormula("INS_DED", rawRow, rawRef),
                "TAX_DED" => BuildDeductionBackedFormula("TAX_DED", rawRow, rawRef),
                "LOAN_DED" => BuildDeductionBackedFormula("LOAN_DED", rawRow, rawRef),
                "ADVANCE_DED" => BuildDeductionBackedFormula("ADVANCE_DED", rawRow, rawRef),
                _ => BuildDecreeBackedItemFormula(itemCode, rawRow, rawRef)
            };
        }

        private static void AddDeductionTraceRow(IXLWorksheet ws, int row, int rawRow, string code, string title, string formula, string description)
        {
            ws.Cell(row, 1).FormulaA1 = $"'داده خام'!A{rawRow}";
            ws.Cell(row, 2).FormulaA1 = $"'داده خام'!B{rawRow}";
            ws.Cell(row, 3).Value = code;
            ws.Cell(row, 4).Value = title;
            ws.Cell(row, 5).FormulaA1 = formula;
            ws.Cell(row, 6).FormulaA1 = formula;
            ws.Cell(row, 7).FormulaA1 = $"FORMULATEXT(F{row})";
            ws.Cell(row, 8).Value = description;
        }

        private static string BuildDeductionBackedFormula(string itemCode, int rawRow, string rawRef)
        {
            string sum = $"SUMIFS('ردیابی کسورات'!F:F,'ردیابی کسورات'!A:A,'داده خام'!A{rawRow},'ردیابی کسورات'!C:C,\"{itemCode}\")";
            return $"IF({sum}<>0,{sum},{rawRef})";
        }

        private static string BuildDecreeBackedItemFormula(string itemCode, int rawRow, string rawRef)
        {
            string code = itemCode.Replace("\"", "\"\"");
            string sum = $"SUMIFS('ردیابی حکم'!V:V,'ردیابی حکم'!A:A,'داده خام'!A{rawRow},'ردیابی حکم'!F:F,\"{code}\")";
            return $"IF({sum}<>0,{sum},{rawRef})";
        }

        private static string BuildItemTraceDescription(string itemCode)
        {
            return itemCode switch
            {
                "OT_NORMAL" => "نرخ ساعتی موثر = جمع پایه / روز رسمی / OT_HOUR_BASE؛ سپس × ساعت اضافه‌کار عادی × OT_NORMAL_MULT",
                "OT_HOLIDAY" => "نرخ ساعتی موثر = جمع پایه / روز رسمی / OT_HOUR_BASE؛ سپس × ساعت تعطیل‌کاری × OT_HOLIDAY_MULT",
                "OT_ADMIN" => "نرخ ساعتی موثر = جمع پایه / روز رسمی / OT_HOUR_BASE؛ سپس × ساعت اضافه‌کار اداری × OT_NORMAL_MULT",
                "PERF_BONUS" => "مبلغ متغیر کارانه از کارکرد ماه",
                "TRANSP" => "مبلغ متغیر ایاب‌وذهاب از کارکرد ماه",
                "INS_DED" => "برابر فرمول بیمه کارگر در شیت فیش حقوقی",
                "TAX_DED" => "برابر فرمول مالیات پلکانی در شیت فیش حقوقی",
                _ => "اگر آیتم حکمی باشد از شیت ردیابی حکم با SUMIFS جمع می‌شود؛ اگر آیتم متغیر/خارج از حکم باشد به مبلغ موتور متصل می‌شود"
            };
        }

        private static string BuildItemCodeSumExpression(int rawRow, int itemStartCol, List<RunAuditColumn> columns, params string[] itemCodes)
        {
            var refs = columns.Select((c, i) => new { c, i }).Where(x => itemCodes.Contains(x.c.ITEM_CODE)).Select(x => $"'داده خام'!{ColLetter(itemStartCol + x.i)}{rawRow}").ToList();
            return refs.Count == 0 ? "0" : string.Join("+", refs);
        }

        private static string BuildInsuranceBaseFormula(List<RunAuditColumn> columns, int row, int firstRawItemCol)
        {
            string subjectSum = $"SUMIFS('ردیابی اقلام'!G:G,'ردیابی اقلام'!A:A,'داده خام'!A{row},'ردیابی اقلام'!J:J,1)";
            string ceiling = $"IF(IFERROR(VLOOKUP(\"INS_CEILING_APPLY\",'تنظیمات'!A:B,2,FALSE),1)=1,IFERROR(VLOOKUP(\"INS_CEILING_MONTHLY\",'تنظیمات'!A:B,2,FALSE),{subjectSum})*IF('داده خام'!H{row}>0,'داده خام'!H{row},'داده خام'!G{row})/30,{subjectSum})";
            return $"IF('داده خام'!E{row}=3,0,ROUND(MIN({subjectSum},{ceiling}),0))";
        }

        private static string BuildSubjectSumExpression(List<RunAuditColumn> columns, int row, int firstRawItemCol, Func<RunAuditColumn, bool> subject)
        {
            var refs = columns.Select((c, i) => new { c, i }).Where(x => subject(x.c)).Select(x => $"'داده خام'!{ColLetter(firstRawItemCol + x.i)}{row}").ToList();
            return refs.Count == 0 ? "0" : $"SUM({string.Join(",", refs)})";
        }

        private static string BuildSubjectSumFormula(List<RunAuditColumn> columns, int row, int firstRawItemCol, Func<RunAuditColumn, bool> subject)
        {
            return $"ROUND(SUMIFS('ردیابی اقلام'!G:G,'ردیابی اقلام'!A:A,'داده خام'!A{row},'ردیابی اقلام'!K:K,1),0)";
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
        private class RunAuditColumn { public int ITEM_ID { get; set; } public string ITEM_CODE { get; set; } = ""; public string ITEM_NAME { get; set; } = ""; public byte ITEM_TYPE { get; set; } public int SORT_ORDER { get; set; } public bool INS_SUBJECT { get; set; } public bool TAX_SUBJECT { get; set; } }
        private class RunAuditDecreeRow
        {
            public int EMP_ID { get; set; } public string EMP_CODE { get; set; } = ""; public string FULL_NAME { get; set; } = "";
            public int DEC_ID { get; set; } public int EFF_FROM { get; set; } public int EFF_TO { get; set; }
            public string ITEM_CODE { get; set; } = ""; public string ITEM_NAME { get; set; } = ""; public byte ITEM_TYPE { get; set; }
            public decimal DEC_AMOUNT { get; set; } public byte EFFECTIVE_BASIS { get; set; } public bool EFFECTIVE_INS { get; set; } public bool EFFECTIVE_TAX { get; set; }
            public byte PAY_BASE_DAYS { get; set; } public byte INS_BASE_DAYS { get; set; }
            public decimal DAYS { get; set; } public decimal DAYSB { get; set; } public byte FRID_COUNT { get; set; } public decimal TDAYS { get; set; } public decimal LEAVE_DAYS { get; set; }
            public decimal OT_NORMAL_H { get; set; } public decimal OT_HOLIDAY_H { get; set; } public decimal OT_ADMIN_H { get; set; }
            public string EFFECTIVE_SHIFT_MODE { get; set; } = "PCT"; public decimal CURRENT_DEC_DAILY_BASE { get; set; } public int HAS_OVERRIDE { get; set; }
        }

        private class RunAuditLine : Pay2RunLineDto
        {
            public decimal OT_NORMAL_H { get; set; } public decimal OT_HOLIDAY_H { get; set; } public decimal OT_ADMIN_H { get; set; }
            public decimal DAYS { get; set; } public decimal DAYSB { get; set; } public byte FRID_COUNT { get; set; } public decimal TDAYS { get; set; }
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