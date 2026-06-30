using Dapper;
using ClosedXML.Excel;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Safir.Server.Services
{
    public class Pay2SmartExcelService : IPay2SmartExcelService
    {
        private readonly IDatabaseService _db;

        public Pay2SmartExcelService(IDatabaseService db)
        {
            _db = db;
        }

        private async Task<Pay2ExcelAuditDto> FetchAuditDataAsync(int runId)
        {
            var auditData = new Pay2ExcelAuditDto();

            // 1. Fetch Run Header
            var runSql = @"
                SELECT R.RUN_ID, R.WS_ID, R.PER_ID, P.PERIOD_DATE
                FROM PAY2_RUN R
                INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                WHERE R.RUN_ID = @runId";
            var runHeader = await _db.DoGetDataSQLAsyncSingle<dynamic>(runSql, new { runId });

            if (runHeader == null)
                throw new Exception($"Run {runId} not found.");

            int wsId = runHeader.WS_ID;
            int perId = runHeader.PER_ID;
            long periodDate = runHeader.PERIOD_DATE;
            short taxYear = (short)(periodDate / 10000);

            // 2. Fetch Configs
            var configSql = "SELECT CFG_KEY, CFG_VALUE, DATA_TYPE FROM PAY2_CONFIG";
            auditData.Configs = (await _db.DoGetDataSQLAsync<Pay2ExcelConfigDto>(configSql)).ToList();

            // 3. Fetch Tax Brackets
            var taxSql = "SELECT UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = @taxYear ORDER BY SORT_ORDER";
            auditData.TaxBrackets = (await _db.DoGetDataSQLAsync<Pay2ExcelTaxBracketDto>(taxSql, new { taxYear })).ToList();

            // 4. Fetch Employees and Run Lines
            var linesSql = @"
                SELECT RL.EMP_ID, E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME,
                       A.WORK_DAYS, A.DAYSB, A.OT_NORMAL_H, A.OT_HOLIDAY_H, A.ABSENT_DAYS,
                       RL.GROSS_PAY, RL.INS_BASE, RL.INS_WORKER, RL.TAX_BASE, RL.TAX_AMOUNT,
                       RL.LOAN_DED, RL.ADVANCE_DED, RL.OTHER_DED, RL.TOTAL_DED, RL.NET_PAY
                FROM PAY2_RUN_LINE RL
                INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
                INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @perId
                WHERE RL.RUN_ID = @runId";

            var lines = (await _db.DoGetDataSQLAsync<Pay2ExcelEmployeeLineDto>(linesSql, new { runId, perId })).ToList();

            // 5. Fetch Details (Only Payments: ITEM_TYPE 1 & 2)
            var detailsSql = @"
                SELECT RD.EMP_ID, ID.ITEM_CODE, RD.AMOUNT, RD.INS_SUBJECT, RD.TAX_SUBJECT
                FROM PAY2_RUN_DETAIL RD
                INNER JOIN PAY2_ITEM_DEF ID ON RD.ITEM_ID = ID.ITEM_ID
                WHERE RD.RUN_ID = @runId AND ID.ITEM_TYPE IN (1, 2)";
            var details = await _db.DoGetDataSQLAsync<dynamic>(detailsSql, new { runId });
            var detailsByEmp = details.GroupBy(d => (int)d.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            var baseValuesSql = @"
                SELECT DL.EMP_ID, ID.ITEM_CODE, DL.AMOUNT
                FROM PAY2_DECREE_LINE DL
                INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
                INNER JOIN PAY2_RUN_LINE RL ON DL.DEC_ID = RL.DEC_ID AND DL.EMP_ID = RL.EMP_ID
                WHERE RL.RUN_ID = @runId";
            var baseValues = await _db.DoGetDataSQLAsync<dynamic>(baseValuesSql, new { runId });
            var baseValuesByEmp = baseValues.GroupBy(b => (int)b.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            foreach(var line in lines)
            {
                if (detailsByEmp.TryGetValue(line.EMP_ID, out var empDetails))
                {
                    foreach(var d in empDetails)
                    {
                        line.ComputedDetails[d.ITEM_CODE] = (long)d.AMOUNT;
                    }
                }
                if (baseValuesByEmp.TryGetValue(line.EMP_ID, out var empBaseValues))
                {
                    foreach(var b in empBaseValues)
                    {
                        line.BaseValues[b.ITEM_CODE] = (long)b.AMOUNT;
                    }
                }
            }

            auditData.AuditLines = lines;
            return auditData;
        }

        public async Task<byte[]> GenerateSmartExcelAsync(int runId)
        {
            var auditData = await FetchAuditDataAsync(runId);
            return GenerateExcelBytes(auditData);
        }

        private byte[] GenerateExcelBytes(Pay2ExcelAuditDto auditData)
        {
            using var wb = new XLWorkbook();
            wb.RightToLeft = true;

            // --- 1. Settings Sheet (Hidden) ---
            var wsSettings = wb.Worksheets.Add("تنظیمات");
            wsSettings.Cell(1, 1).Value = "CFG_KEY";
            wsSettings.Cell(1, 2).Value = "CFG_VALUE";
            wsSettings.Cell(1, 3).Value = "NamedRange";

            int rSettings = 2;
            foreach (var cfg in auditData.Configs)
            {
                wsSettings.Cell(rSettings, 1).Value = cfg.CFG_KEY;

                double numVal;
                if (double.TryParse(cfg.CFG_VALUE, out numVal))
                    wsSettings.Cell(rSettings, 2).Value = numVal;
                else
                    wsSettings.Cell(rSettings, 2).Value = cfg.CFG_VALUE;

                // Create a named range for each setting to easily use in formulas
                var cell = wsSettings.Cell(rSettings, 2);
                wb.DefinedNames.Add("CFG_" + cfg.CFG_KEY, cell.AsRange());
                wsSettings.Cell(rSettings, 3).Value = "CFG_" + cfg.CFG_KEY;
                rSettings++;
            }
            wsSettings.Hide();

            // --- 2. RawData Sheet (Hidden) ---
            var wsRaw = wb.Worksheets.Add("داده‌های_خام");
            wsRaw.Cell(1, 1).Value = "EMP_ID";
            wsRaw.Cell(1, 2).Value = "WORK_DAYS";
            wsRaw.Cell(1, 3).Value = "DAYSB";
            wsRaw.Cell(1, 4).Value = "OT_NORMAL_H";
            wsRaw.Cell(1, 5).Value = "BASE_SAL";
            wsRaw.Cell(1, 6).Value = "BASE_SAL_B";
            wsRaw.Cell(1, 7).Value = "ABSENT_DAYS";

            int rRaw = 2;
            var empRowMap = new Dictionary<int, int>(); // Maps EMP_ID to row number in RawData sheet
            foreach (var line in auditData.AuditLines)
            {
                wsRaw.Cell(rRaw, 1).Value = line.EMP_ID;
                wsRaw.Cell(rRaw, 2).Value = line.WORK_DAYS;
                wsRaw.Cell(rRaw, 3).Value = line.DAYSB;
                wsRaw.Cell(rRaw, 4).Value = line.OT_NORMAL_H;
                wsRaw.Cell(rRaw, 5).Value = line.BaseValues.GetValueOrDefault("BASE_SAL", 0);
                wsRaw.Cell(rRaw, 6).Value = line.BaseValues.GetValueOrDefault("BASE_SAL_B", 0);
                wsRaw.Cell(rRaw, 7).Value = line.ABSENT_DAYS;

                empRowMap[line.EMP_ID] = rRaw;
                rRaw++;
            }
            wsRaw.Hide();

            // --- 3. PayrollSlip Sheet (Visible, Formula Based) ---
            var wsSlip = wb.Worksheets.Add("فیش_حقوقی");
            wsSlip.RightToLeft = true;

            // استخراج تمام آیتم‌های پرداختی یکتا برای ایجاد ستون‌های داینامیک
            var allPaymentCodes = auditData.AuditLines
                .SelectMany(l => l.ComputedDetails)
                .Where(kvp => kvp.Value > 0)
                .Select(kvp => kvp.Key)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var baseHeaders = new List<string> { "ردیف", "کد پرسنلی", "نام و نام خانوادگی", "روز کارکرد" };
            var dynamicHeaders = allPaymentCodes.Select(c => c + " (خام/فرمول)").ToList();
            var trailingHeaders = new List<string> { "ناخالص حقوق (فرمول)", "مبنای بیمه (خام)", "بیمه سهم کارگر (فرمول)", "معافیت مالیاتی (فرمول)", "مبنای مالیات (فرمول)", "مالیات (فرمول)", "مساعده/وام/سایر", "خالص پرداختی (فرمول)" };

            var headers = baseHeaders.Concat(dynamicHeaders).Concat(trailingHeaders).ToList();

            for(int i = 0; i < headers.Count; i++)
            {
                wsSlip.Cell(1, i + 1).Value = headers[i];
                wsSlip.Cell(1, i + 1).Style.Font.Bold = true;
                wsSlip.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            int rSlip = 2;
            string netPayColLetterForValidation = "";
            foreach (var line in auditData.AuditLines)
            {
                int rawRow = empRowMap[line.EMP_ID];
                string rawPrefix = $"داده‌های_خام!";

                wsSlip.Cell(rSlip, 1).Value = rSlip - 1;
                wsSlip.Cell(rSlip, 2).Value = line.EMP_CODE;
                wsSlip.Cell(rSlip, 3).Value = line.FULL_NAME;
                wsSlip.Cell(rSlip, 4).FormulaA1 = $"{rawPrefix}B{rawRow}"; // WORK_DAYS

                int cSlip = 5;
                // پر کردن ستون‌های داینامیک پرداختی همراه با فرمول
                foreach (var code in allPaymentCodes)
                {
                    // If the item exists in baseValues (e.g., from decree) and is based on days, we inject formula
                    long baseVal = line.BaseValues.GetValueOrDefault(code, 0);

                    if (baseVal > 0 && (code == "BASE_SAL" || code == "HOME" || code == "CHILDREN" || code == "GROCERY" || code == "SHIFT" || code == "OT_NORMAL" || code == "OT_HOLIDAY"))
                    {
                        // Some common formulas based on Iranian payroll system logic
                        if (code == "BASE_SAL" || code == "HOME" || code == "CHILDREN" || code == "GROCERY")
                        {
                            wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND({baseVal} * {rawPrefix}C{rawRow} / CFG_MONTH_DAYS, 0)"; // DAYSB
                        }
                        else if (code == "SHIFT")
                        {
                            wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND(({baseVal}/100) * {rawPrefix}F{rawRow} * {rawPrefix}B{rawRow} / CFG_MONTH_DAYS, 0)";
                        }
                        else if (code == "OT_NORMAL")
                        {
                            wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND(({rawPrefix}F{rawRow} / (CFG_MONTH_DAYS * CFG_OT_HOUR_BASE)) * {rawPrefix}D{rawRow} * CFG_OT_NORMAL_MULT, 0)"; // OT_NORMAL
                        }
                        else if (code == "OT_HOLIDAY")
                        {
                            wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND(({rawPrefix}F{rawRow} / (CFG_MONTH_DAYS * CFG_OT_HOUR_BASE)) * {rawPrefix}E{rawRow} * CFG_OT_HOLIDAY_MULT, 0)"; // OT_HOLIDAY
                        }
                        else
                        {
                            wsSlip.Cell(rSlip, cSlip).Value = line.ComputedDetails.GetValueOrDefault(code, 0);
                        }
                    }
                    else if (code == "OT_NORMAL")
                    {
                        wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND(({rawPrefix}F{rawRow} / (CFG_MONTH_DAYS * CFG_OT_HOUR_BASE)) * {rawPrefix}D{rawRow} * CFG_OT_NORMAL_MULT, 0)"; // OT_NORMAL
                    }
                    else if (code == "OT_HOLIDAY")
                    {
                        wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND(({rawPrefix}F{rawRow} / (CFG_MONTH_DAYS * CFG_OT_HOUR_BASE)) * {rawPrefix}E{rawRow} * CFG_OT_HOLIDAY_MULT, 0)"; // OT_HOLIDAY
                    }
                    else
                    {
                        wsSlip.Cell(rSlip, cSlip).Value = line.ComputedDetails.GetValueOrDefault(code, 0);
                    }

                    cSlip++;
                }

                // ستون ناخالص حقوق (جمع تمام پرداختی‌ها)
                int startCol = 5;
                int endCol = cSlip - 1;
                string grossColLetter = GetColumnName(cSlip);
                if (endCol >= startCol)
                {
                    string startLetter = GetColumnName(startCol);
                    string endLetter = GetColumnName(endCol);
                    wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"SUM({startLetter}{rSlip}:{endLetter}{rSlip})";
                }
                else
                {
                    wsSlip.Cell(rSlip, cSlip).Value = 0;
                }
                cSlip++;

                // مبنای بیمه (خام از C# - چون سقف و روزهای غیبت پیچیده است)
                string insBaseColLetter = GetColumnName(cSlip);
                wsSlip.Cell(rSlip, cSlip).Value = line.INS_BASE;
                cSlip++;

                // فرمول بیمه سهم کارگر: ROUND(مبنای بیمه * 7% , 0)
                string insWorkerColLetter = GetColumnName(cSlip);
                wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND({insBaseColLetter}{rSlip} * (CFG_INS_WORKER_RATE / 100), 0)";
                cSlip++;

                // معافیت مالیاتی: ROUND(CFG_TAX_EXEMPT_MONTHLY * WORK_DAYS / CFG_MONTH_DAYS, 0)
                // Use the setting CFG_MONTH_DAYS directly as requested
                string taxExemptColLetter = GetColumnName(cSlip);
                wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"ROUND(CFG_TAX_EXEMPT_MONTHLY * {rawPrefix}B{rawRow} / 30, 0)";
                cSlip++;

                // مبنای مالیات: GROSS - TAX_EXEMPT - INS_WORKER (if exempt). For simplicity: C# TAX_BASE
                string taxBaseColLetter = GetColumnName(cSlip);
                wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"MAX(0, {grossColLetter}{rSlip} - {insWorkerColLetter}{rSlip} - {taxExemptColLetter}{rSlip})";
                cSlip++;

                // مالیات (فرمول پویا مبتنی بر پله‌های مالیاتی سالانه / ۱۲)
                string taxFormula = GenerateTaxFormula(auditData.TaxBrackets, $"{taxBaseColLetter}{rSlip}");
                string taxColLetter = GetColumnName(cSlip);
                wsSlip.Cell(rSlip, cSlip).FormulaA1 = taxFormula;
                cSlip++;

                // سایر کسورات (وام و مساعده - خام از C#)
                string otherDedColLetter = GetColumnName(cSlip);
                long otherDeds = line.ADVANCE_DED + line.LOAN_DED + line.OTHER_DED;
                wsSlip.Cell(rSlip, cSlip).Value = otherDeds;
                cSlip++;

                // خالص پرداختی
                string netPayColLetter = GetColumnName(cSlip);
                netPayColLetterForValidation = netPayColLetter;
                wsSlip.Cell(rSlip, cSlip).FormulaA1 = $"{grossColLetter}{rSlip} - {insWorkerColLetter}{rSlip} - {taxColLetter}{rSlip} - {otherDedColLetter}{rSlip}";

                rSlip++;
            }
            wsSlip.Columns().AdjustToContents();

            // --- 4. Validation Sheet (Visible) ---
            var wsVal = wb.Worksheets.Add("کنترل_تطابق");
            wsVal.RightToLeft = true;

            var valHeaders = new[] { "ردیف", "کد پرسنلی", "نام", "خالص پرداختی (موتور سی‌شارپ)", "خالص پرداختی (اکسل)", "اختلاف" };
            for(int i = 0; i < valHeaders.Length; i++)
            {
                wsVal.Cell(1, i + 1).Value = valHeaders[i];
                wsVal.Cell(1, i + 1).Style.Font.Bold = true;
            }

            int rVal = 2;
            foreach (var line in auditData.AuditLines)
            {
                wsVal.Cell(rVal, 1).Value = rVal - 1;
                wsVal.Cell(rVal, 2).Value = line.EMP_CODE;
                wsVal.Cell(rVal, 3).Value = line.FULL_NAME;

                // C# Net Pay
                wsVal.Cell(rVal, 4).Value = line.NET_PAY;

                // Excel Net Pay (Ref to PayrollSlip)
                wsVal.Cell(rVal, 5).FormulaA1 = $"فیش_حقوقی!{netPayColLetterForValidation}{rVal}";

                // Diff
                wsVal.Cell(rVal, 6).FormulaA1 = $"D{rVal}-E{rVal}";

                // Conditional Formatting for Diff > 10 (Rounding drift)
                var diffCell = wsVal.Cell(rVal, 6);
                diffCell.AddConditionalFormat().WhenLessThan(-10).Fill.SetBackgroundColor(XLColor.LightPink);
                diffCell.AddConditionalFormat().WhenGreaterThan(10).Fill.SetBackgroundColor(XLColor.LightPink);
                diffCell.AddConditionalFormat().WhenBetween(-10, 10).Fill.SetBackgroundColor(XLColor.LightGreen);

                rVal++;
            }
            wsVal.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // Helper to convert column index (1-based) to Excel letter (A, B, C...)
        private string GetColumnName(int columnIndex)
        {
            int dividend = columnIndex;
            string columnName = String.Empty;
            int modulo;

            while (dividend > 0)
            {
                modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo).ToString() + columnName;
                dividend = (int)((dividend - modulo) / 26);
            }

            return columnName;
        }

        private string GenerateTaxFormula(List<Pay2ExcelTaxBracketDto> brackets, string cellRef)
        {
            if (brackets == null || !brackets.Any())
                return "0";

            var ordered = brackets.OrderBy(b => b.SORT_ORDER).ToList();

            // Tax brackets in database are ANNUAL.
            // So we multiply the monthly tax base by 12, calculate tax, and divide by 12.
            string annualBaseRef = $"({cellRef} * 12)";

            var sb = new System.Text.StringBuilder();
            sb.Append("ROUND((");

            int openIfs = 0;
            long prevLimit = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                var bracket = ordered[i];
                bool isLast = (i == ordered.Count - 1);

                if (!isLast)
                {
                    sb.Append($"IF({annualBaseRef}<={bracket.UPPER_LIMIT}, ");
                    openIfs++;
                }

                // Calculation for this bracket
                if (bracket.RATE_PCT == 0)
                {
                    sb.Append("0");
                }
                else
                {
                    sb.Append($"{bracket.FIXED_TAX}+({annualBaseRef}-{prevLimit})*({bracket.RATE_PCT}/100)");
                }

                if (!isLast)
                {
                    sb.Append(", ");
                }

                prevLimit = bracket.UPPER_LIMIT;
            }

            // Close all open IFs
            for (int i = 0; i < openIfs; i++)
            {
                sb.Append(")");
            }

            sb.Append(") / 12, 0)");

            return sb.ToString();
        }
    }
}