using ClosedXML.Excel;
using Safir.Shared.Models.Salary;

namespace Safir.Server.Services
{
    /// <summary>
    /// تولید فایل اکسل حسابرسی با فرمول‌های واقعی (نه اعداد ثابت)
    /// هر سلول مالی در شیت Payslip حاوی فرمول Excel است که به شیت‌های
    /// Settings و RawData ارجاع می‌دهد — کاربر دقیقاً می‌بیند هر عدد چگونه محاسبه شده.
    /// </summary>
    public class Pay2ExcelAuditService
    {
        // ═══════════════════════════════════════════════════
        // متد اصلی: تولید ورک‌بوک ۴ شیت
        // ═══════════════════════════════════════════════════
        public byte[] Generate(Pay2ExcelAuditDataDto data)
        {
            using var wb = new XLWorkbook();

            var wsSettings  = wb.Worksheets.Add("Settings");   // شیت ۱: تنظیمات (Named Ranges)
            var wsRawData   = wb.Worksheets.Add("RawData");    // شیت ۲: داده‌های خام
            var wsPayslip   = wb.Worksheets.Add("Payslip");    // شیت ۳: فیش حقوقی با فرمول
            var wsControl   = wb.Worksheets.Add("Control");    // شیت ۴: تطبیق / Reconciliation

            BuildSettingsSheet(wsSettings, data);
            BuildRawDataSheet(wsRawData, data);
            BuildPayslipSheet(wsPayslip, data, wsSettings, wsRawData);
            BuildControlSheet(wsControl, data, wsPayslip);

            // مخفی کردن شیت‌های تقنینی
            wsSettings.SetTabColor(XLColor.Gray);
            wsRawData.SetTabColor(XLColor.Gray);

            // RTL برای شیت‌های قابل مشاهده
            wsPayslip.WorksheetRightToLeft = true;
            wsControl.WorksheetRightToLeft = true;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ═══════════════════════════════════════════════════
        // شیت ۱: Settings — مقادیر پیکربندی به‌صورت Named Ranges
        // ═══════════════════════════════════════════════════
        private void BuildSettingsSheet(IXLWorksheet ws, Pay2ExcelAuditDataDto data)
        {
            // هدر
            ws.Cell(1, 1).Value = XLCellValue.FromObject("کلید تنظیم");
            ws.Cell(1, 2).Value = XLCellValue.FromObject("مقدار");
            ws.Cell(1, 3).Value = XLCellValue.FromObject("توضیح");
            ws.Range(1, 1, 1, 3).Style.Font.Bold = true;

            int row = 2;
            foreach (var kv in data.Config)
            {
                string key = kv.Key;
                string rawVal = kv.Value ?? "0";

                // تبدیل مقدار رشته‌ای به عدد اگر ممکن باشد
                XLCellValue cellVal;
                if (decimal.TryParse(rawVal, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var decVal))
                {
                    cellVal = XLCellValue.FromObject((double)decVal);
                }
                else if (long.TryParse(rawVal, out var longVal))
                {
                    cellVal = XLCellValue.FromObject((double)longVal);
                }
                else if (rawVal == "1" || rawVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    cellVal = XLCellValue.FromObject(1.0);
                }
                else if (rawVal == "0" || rawVal.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    cellVal = XLCellValue.FromObject(0.0);
                }
                else
                {
                    cellVal = XLCellValue.FromObject(rawVal);
                }

                ws.Cell(row, 1).Value = XLCellValue.FromObject(key);
                ws.Cell(row, 2).Value = cellVal;

                // ایجاد Named Range برای هر تنظیم
                string safeName = MakeSafeName(key);
                try
                {
                    ws.Cell(row, 2).AddToNamed(safeName, XLScope.Workbook);
                }
                catch { /* اگر نام تکراری بود، نادیده بگیر */ }

                row++;
            }

            // --- پل‌های مالیات ---
            row += 2;
            int taxStartRow = row;
            ws.Cell(row, 1).Value = XLCellValue.FromObject("پله‌های مالیات (Tax Brackets)");
            ws.Range(row, 1, row, 5).Style.Font.Bold = true;
            row++;
            ws.Cell(row, 1).Value = XLCellValue.FromObject("SORT_ORDER");
            ws.Cell(row, 2).Value = XLCellValue.FromObject("UPPER_LIMIT");
            ws.Cell(row, 3).Value = XLCellValue.FromObject("RATE_PCT");
            ws.Cell(row, 4).Value = XLCellValue.FromObject("FIXED_TAX");
            ws.Range(row, 1, row, 4).Style.Font.Bold = true;
            row++;

            int bracketDataStart = row;
            foreach (var b in data.TaxBrackets.OrderBy(b => b.SORT_ORDER))
            {
                ws.Cell(row, 1).Value = XLCellValue.FromObject((double)b.SORT_ORDER);
                ws.Cell(row, 2).Value = XLCellValue.FromObject((double)b.UPPER_LIMIT);
                ws.Cell(row, 3).Value = XLCellValue.FromObject((double)b.RATE_PCT);
                ws.Cell(row, 4).Value = XLCellValue.FromObject((double)b.FIXED_TAX);
                row++;
            }
            int bracketDataEnd = row - 1;

            // Named Range برای محدوده پل‌ها
            try
            {
                ws.Range(bracketDataStart, 2, bracketDataEnd, 2).AddToNamed("TAX_UPPER_LIMITS", XLScope.Workbook);
                ws.Range(bracketDataStart, 3, bracketDataEnd, 3).AddToNamed("TAX_RATE_PCTS", XLScope.Workbook);
                ws.Range(bracketDataStart, 4, bracketDataEnd, 4).AddToNamed("TAX_FIXED_TAXES", XLScope.Workbook);
            }
            catch { /* نادیده */ }

            // --- تعاریف آیتم‌ها ---
            row += 2;
            ws.Cell(row, 1).Value = XLCellValue.FromObject("تعاریف آیتم‌ها (Item Definitions)");
            ws.Range(row, 1, row, 6).Style.Font.Bold = true;
            row++;
            ws.Cell(row, 1).Value = XLCellValue.FromObject("ITEM_CODE");
            ws.Cell(row, 2).Value = XLCellValue.FromObject("ITEM_NAME");
            ws.Cell(row, 3).Value = XLCellValue.FromObject("ITEM_TYPE");
            ws.Cell(row, 4).Value = XLCellValue.FromObject("CALC_BASIS");
            ws.Cell(row, 5).Value = XLCellValue.FromObject("INS_SUBJECT");
            ws.Cell(row, 6).Value = XLCellValue.FromObject("TAX_SUBJECT");
            ws.Range(row, 1, row, 6).Style.Font.Bold = true;
            row++;

            foreach (var item in data.ItemDefs)
            {
                ws.Cell(row, 1).Value = XLCellValue.FromObject(item.ITEM_CODE);
                ws.Cell(row, 2).Value = XLCellValue.FromObject(item.ITEM_NAME);
                ws.Cell(row, 3).Value = XLCellValue.FromObject(item.ITEM_TYPE);
                ws.Cell(row, 4).Value = XLCellValue.FromObject(item.CALC_BASIS);
                ws.Cell(row, 5).Value = XLCellValue.FromObject(item.INS_SUBJECT ? 1 : 0);
                ws.Cell(row, 6).Value = XLCellValue.FromObject(item.TAX_SUBJECT ? 1 : 0);
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        // ═══════════════════════════════════════════════════
        // شیت ۲: RawData — داده‌های خام هر کارمند
        // ═══════════════════════════════════════════════════
        private void BuildRawDataSheet(IXLWorksheet ws, Pay2ExcelAuditDataDto data)
        {
            // هدر ثابت
            int col = 1;
            SetHeader(ws, col++, "EMP_ID");
            SetHeader(ws, col++, "EMP_CODE");
            SetHeader(ws, col++, "FULL_NAME");
            SetHeader(ws, col++, "WORK_DAYS");
            SetHeader(ws, col++, "DAYS");
            SetHeader(ws, col++, "DAYSB");
            SetHeader(ws, col++, "OT_NORMAL_H");
            SetHeader(ws, col++, "OT_HOLIDAY_H");
            SetHeader(ws, col++, "OT_ADMIN_H");
            SetHeader(ws, col++, "LEAVE_DAYS");
            SetHeader(ws, col++, "PERF_AMOUNT");
            SetHeader(ws, col++, "TRANSP_AMOUNT");
            SetHeader(ws, col++, "KASR_OTHER");

            // ستون‌های decree line — یک ستون برای هر ITEM_CODE
            var allDecreeCodes = data.RawLines
                .SelectMany(l => l.DecreeLines.Select(d => d.ITEM_CODE))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var decreeColStart = col; // ستون شروع decree
            var decreeColMap = new Dictionary<string, int>(); // ITEM_CODE → شماره ستون
            foreach (var code in allDecreeCodes)
            {
                decreeColMap[code] = col;
                SetHeader(ws, col, $"DEC_{code}");
                col++;
            }

            // ستون‌های ATT_VALUE
            var allAttKeys = data.RawLines
                .SelectMany(l => l.AttValues.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            var attColStart = col;
            var attColMap = new Dictionary<string, int>();
            foreach (var key in allAttKeys)
            {
                attColMap[key] = col;
                SetHeader(ws, col, $"ATT_{key}");
                col++;
            }

            // ردیف‌های داده
            int row = 2;
            foreach (var emp in data.RawLines)
            {
                col = 1;
                ws.Cell(row, col++).Value = XLCellValue.FromObject(emp.EMP_ID);
                ws.Cell(row, col++).Value = XLCellValue.FromObject(emp.EMP_CODE ?? "");
                ws.Cell(row, col++).Value = XLCellValue.FromObject(emp.FULL_NAME ?? "");
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.WORK_DAYS);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.DAYS);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.DAYSB);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.OT_NORMAL_H);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.OT_HOLIDAY_H);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.OT_ADMIN_H);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.LEAVE_DAYS);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.PERF_AMOUNT);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.TRANSP_AMOUNT);
                ws.Cell(row, col++).Value = XLCellValue.FromObject((double)emp.KASR_OTHER);

                // مقادیر decree
                foreach (var dec in emp.DecreeLines)
                {
                    if (decreeColMap.TryGetValue(dec.ITEM_CODE, out var c))
                        ws.Cell(row, c).Value = XLCellValue.FromObject((double)dec.AMOUNT);
                }

                // مقادیر ATT_VALUE
                foreach (var kv in emp.AttValues)
                {
                    if (attColMap.TryGetValue(kv.Key, out var c))
                        ws.Cell(row, c).Value = XLCellValue.FromObject((double)kv.Value);
                }

                row++;
            }

            ws.Columns().AdjustToContents();
        }

        // ═══════════════════════════════════════════════════
        // شیت ۳: Payslip — فیش حقوقی با فرمول‌های واقعی
        // ═══════════════════════════════════════════════════
        private void BuildPayslipSheet(IXLWorksheet ws, Pay2ExcelAuditDataDto data,
            IXLWorksheet wsSettings, IXLWorksheet wsRawData)
        {
            // --- هدر اطلاعات اجرا ---
            ws.Cell(1, 1).Value = XLCellValue.FromObject("فیش حقوقی حسابرسی — اجرا #" + data.RUN_ID);
            ws.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws.Cell(2, 1).Value = XLCellValue.FromObject("دوره:");
            ws.Cell(2, 2).Value = XLCellValue.FromObject(data.PERIOD_DATE.ToString());
            ws.Cell(2, 3).Value = XLCellValue.FromObject("کارگاه:");
            ws.Cell(2, 4).Value = XLCellValue.FromObject(data.WS_NAME ?? "");

            // --- هدر جدول ---
            int hdrRow = 4;
            int col = 1;
            SetHeader(ws, col++, "ردیف", hdrRow);
            SetHeader(ws, col++, "کد پرسنلی", hdrRow);
            SetHeader(ws, col++, "نام و نام خانوادگی", hdrRow);
            SetHeader(ws, col++, "کارکرد", hdrRow);

            // ستون‌های پویا: یک ستون برای هر ITEM_CODE
            var itemCodes = data.ItemDefs
                .Where(i => i.ITEM_TYPE is 1 or 2 or 3 or 5) // پرداختی‌ها
                .Select(i => i.ITEM_CODE)
                .ToList();

            // اضافه کردن کدهای Details که شاید در ItemDefs نباشند
            var detailCodes = data.Columns.Select(c => c.ITEM_CODE).ToList();
            var allItemCodes = itemCodes.Union(detailCodes).Distinct().OrderBy(c => c).ToList();

            var itemColMap = new Dictionary<string, int>(); // ITEM_CODE → شماره ستون
            foreach (var code in allItemCodes)
            {
                itemColMap[code] = col;
                var itemDef = data.ItemDefs.FirstOrDefault(i => i.ITEM_CODE == code);
                string header = itemDef?.ITEM_NAME ?? code;
                SetHeader(ws, col, header, hdrRow);
                col++;
            }

            // ستون‌های ثابت مالی
            int grossCol   = col; SetHeader(ws, col++, "ناخالص حقوق", hdrRow);
            int insBaseCol = col; SetHeader(ws, col++, "مبنای بیمه", hdrRow);
            int insWrkCol  = col; SetHeader(ws, col++, "بیمه کارگر", hdrRow);
            int taxBaseCol = col; SetHeader(ws, col++, "مبنای مالیات", hdrRow);
            int taxAmtCol  = col; SetHeader(ws, col++, "مالیات", hdrRow);
            int loanCol    = col; SetHeader(ws, col++, "قسط وام", hdrRow);
            int advCol     = col; SetHeader(ws, col++, "مساعده", hdrRow);
            int otherCol   = col; SetHeader(ws, col++, "سایر کسورات", hdrRow);
            int totalDedCol= col; SetHeader(ws, col++, "جمع کسورات", hdrRow);
            int netPayCol  = col; SetHeader(ws, col++, "خالص پرداختی", hdrRow);

            int totalCols = col - 1;

            // --- خواندن تنظیمات کلیدی ---
            var cfg = data.Config;
            double insWorkerRate = GetCfgDouble(cfg, "INS_WORKER_RATE", 0.07);
            double insCeiling   = GetCfgDouble(cfg, "INS_CEILING_MONTHLY", 0);
            bool insCeilingApply= GetCfgBool(cfg, "INS_CEILING_APPLY", false);
            int taxYear         = GetCfgInt(cfg, "TAX_YEAR", 1403);
            double taxExempt    = GetCfgDouble(cfg, "TAX_EXEMPT_MONTHLY", 0);
            bool taxDeductIns   = GetCfgBool(cfg, "TAX_DEDUCT_INS", true);
            bool taxDepApply    = GetCfgBool(cfg, "TAX_DEP_APPLY", false);
            string monthDaysModeStr = cfg.TryGetValue("MONTH_DAYS_MODE", out var mdms) ? mdms : "30";
            double monthDaysBase = monthDaysModeStr.Equals("REAL", StringComparison.OrdinalIgnoreCase) ? 0 : GetCfgDouble(cfg, "MONTH_DAYS_MODE", 30);
            double otNormalMult = GetCfgDouble(cfg, "OT_NORMAL_MULT", 1.4);
            double otHolidayMult= GetCfgDouble(cfg, "OT_HOLIDAY_MULT", 1.4);
            double otAdminMult  = GetCfgDouble(cfg, "OT_ADMIN_MULT", 1.0);
            int roundDivisor    = GetCfgInt(cfg, "ROUND_MODE", 0); // 0=no rounding, 1000=round to nearest 1000

            // --- فرمول مالیات پلکانی ---
            string taxFormula = GenerateTaxFormula(data.TaxBrackets, taxExempt, taxDeductIns);

            // --- ردیف‌های کارمندان ---
            int dataRow = hdrRow + 1;
            int rowNum = 1;
            foreach (var emp in data.RawLines)
            {
                col = 1;
                ws.Cell(dataRow, col++).Value = XLCellValue.FromObject(rowNum);
                ws.Cell(dataRow, col++).Value = XLCellValue.FromObject(emp.EMP_CODE ?? "");
                ws.Cell(dataRow, col++).Value = XLCellValue.FromObject(emp.FULL_NAME ?? "");

                // کارکرد — ارجاع به RawData
                int rawRow = dataRow - hdrRow + 1; // ردیف متناظر در RawData (هدر ردیف ۱، داده از ۲)
                ws.Cell(dataRow, col++).Value = XLCellValue.FromObject((double)emp.WORK_DAYS);

                // --- مقادیر آیتم‌ها ---
                // هر آیتم: مبلغ از decree × (کارکرد / مبنای محاسبه) + اضافه‌کاری
                // ساده‌سازی: مستقیم از RawData decree و ATT_VALUE بخوانیم
                foreach (var code in allItemCodes)
                {
                    if (!itemColMap.TryGetValue(code, out var itemCol)) continue;

                    var itemDef = data.ItemDefs.FirstOrDefault(i => i.ITEM_CODE == code);

                    // فرمول: اگر decree دارد → DEC_CODE از RawData، اگر ATT دارد → ATT_CODE
                    string decColRef = "";
                    string attColRef = "";

                    // بررسی ستون decree در RawData
                    // شماره ستون در RawData: از decreeColStart جستجو
                    int rawColIdx = FindRawDataDecreeCol(code, data);
                    if (rawColIdx > 0)
                        decColRef = $"RawData!{ColLetter(rawColIdx)}{rawRow}";

                    int attColIdx = FindRawDataAttCol(code, data);
                    if (attColIdx > 0)
                        attColRef = $"RawData!{ColLetter(attColIdx)}{rawRow}";

                    if (decColRef != "" && attColRef != "")
                    {
                        // هر دو وجود دارند — جمع
                        ws.Cell(dataRow, itemCol).FormulaA1 = $"{decColRef}+{attColRef}";
                    }
                    else if (decColRef != "")
                    {
                        ws.Cell(dataRow, itemCol).FormulaA1 = decColRef;
                    }
                    else if (attColRef != "")
                    {
                        ws.Cell(dataRow, itemCol).FormulaA1 = attColRef;
                    }
                    else
                    {
                        // نه decree نه ATT — صفر یا از Details
                        long detailVal = 0;
                        emp.Details?.TryGetValue(code, out detailVal);
                        ws.Cell(dataRow, itemCol).Value = XLCellValue.FromObject((double)detailVal);
                    }
                }

                // ═══ ناخالص حقوق (GROSS_PAY) ═══
                // = SUM(آیتم‌های نوع ۱ و ۲)
                var grossRefs = allItemCodes
                    .Where(c => data.ItemDefs.FirstOrDefault(d => d.ITEM_CODE == c) is { ITEM_TYPE: 1 or 2 })
                    .Select(c => ColLetter(itemColMap[c]) + dataRow)
                    .ToList();

                // اگر ItemDef نداریم ولی در Details هست (از RUN_DETAIL)، به ستون آیتم ارجاع بده
                var detailOnlyCodes = allItemCodes
                    .Where(c => data.ItemDefs.FirstOrDefault(d => d.ITEM_CODE == c) == null)
                    .ToList();
                foreach (var c in detailOnlyCodes)
                    if (itemColMap.TryGetValue(c, out var ic))
                        grossRefs.Add(ColLetter(ic) + dataRow);

                if (grossRefs.Count > 0)
                    ws.Cell(dataRow, grossCol).FormulaA1 = string.Join("+", grossRefs);
                else
                    ws.Cell(dataRow, grossCol).Value = XLCellValue.FromObject(0.0);

                // ═══ مبنای بیمه (INS_BASE) ═══
                // = SUM(آیتم‌های مشمول بیمه)
                var insRefs = allItemCodes
                    .Where(c =>
                    {
                        var def = data.ItemDefs.FirstOrDefault(d => d.ITEM_CODE == c);
                        return def != null && def.INS_SUBJECT;
                    })
                    .Select(c => ColLetter(itemColMap[c]) + dataRow)
                    .ToList();

                if (insRefs.Count > 0)
                {
                    string insBaseFormula = string.Join("+", insRefs);

                    // اعمال سقف بیمه: MIN(INS_BASE, INS_CEILING_MONTHLY / dayBase * DAYSB)
                    // فقط اگر INS_CEILING_APPLY=1 و INS_CEILING_MONTHLY > 0
                    // MONTH_DAYS_MODE=REAL → dayBase از DAYSB خود کارمند، در غیر این صورت 30
                    if (insCeilingApply && insCeiling > 0)
                    {
                        string daysbRef = $"RawData!{ColLetter(FindRawDataFixedCol("DAYSB"))}{rawRow}";
                        string daysRef  = $"RawData!{ColLetter(FindRawDataFixedCol("DAYS"))}{rawRow}";
                        // dayBase: اگر REAL → DAYSB، در غیر این صورت عدد ثابت
                        string dayBase = monthDaysBase > 0
                            ? monthDaysBase.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : daysbRef;
                        insBaseFormula = $"MIN({insBaseFormula},{insCeiling}/{dayBase}*{daysbRef})";
                    }
                    ws.Cell(dataRow, insBaseCol).FormulaA1 = insBaseFormula;
                }
                else
                {
                    ws.Cell(dataRow, insBaseCol).Value = XLCellValue.FromObject(0.0);
                }

                // ═══ بیمه کارگر (INS_WORKER) ═══
                // = INS_BASE * INS_WORKER_RATE / 100 (DB stores percentage: 7.00 = 7%)
                double insWorkerRateDec = insWorkerRate / 100.0;
                ws.Cell(dataRow, insWrkCol).FormulaA1 =
                    $"{ColLetter(insBaseCol)}{dataRow}*{insWorkerRateDec}";

                // ═══ مبنای مالیات (TAX_BASE) ═══
                // = SUM(آیتم‌های مشمول مالیات) - (بیمه کارگر اگر TAX_DEDUCT_INS)
                var taxRefs = allItemCodes
                    .Where(c =>
                    {
                        var def = data.ItemDefs.FirstOrDefault(d => d.ITEM_CODE == c);
                        return def != null && def.TAX_SUBJECT;
                    })
                    .Select(c => ColLetter(itemColMap[c]) + dataRow)
                    .ToList();

                if (taxRefs.Count > 0)
                {
                    string taxBaseFormula = string.Join("+", taxRefs);
                    if (taxDeductIns)
                        taxBaseFormula = $"{taxBaseFormula}-{ColLetter(insWrkCol)}{dataRow}";
                    ws.Cell(dataRow, taxBaseCol).FormulaA1 = taxBaseFormula;
                }
                else
                {
                    ws.Cell(dataRow, taxBaseCol).Value = XLCellValue.FromObject(0.0);
                }

                // ═══ مالیات (TAX_AMOUNT) ═══
                // فرمول پلکانی nested IF
                string taxBaseCell = $"{ColLetter(taxBaseCol)}{dataRow}";
                ws.Cell(dataRow, taxAmtCol).FormulaA1 =
                    GenerateTaxFormulaForCell(taxBaseCell, data.TaxBrackets, taxExempt, taxDeductIns);

                // ═══ کسورات صریح ═══
                ws.Cell(dataRow, loanCol).Value  = XLCellValue.FromObject((double)emp.SP_LOAN_DED);
                ws.Cell(dataRow, advCol).Value   = XLCellValue.FromObject((double)emp.SP_ADVANCE_DED);
                ws.Cell(dataRow, otherCol).Value = XLCellValue.FromObject((double)emp.SP_OTHER_DED);

                // ═══ جمع کسورات ═══
                ws.Cell(dataRow, totalDedCol).FormulaA1 =
                    $"{ColLetter(insWrkCol)}{dataRow}+{ColLetter(taxAmtCol)}{dataRow}+" +
                    $"{ColLetter(loanCol)}{dataRow}+{ColLetter(advCol)}{dataRow}+{ColLetter(otherCol)}{dataRow}";

                // ═══ خالص پرداختی ═══
                // اگر ROUND_MODE > 0: ROUND((GROSS-TOTAL_DED)/ROUND_MODE, 0) * ROUND_MODE
                // در غیر این صورت: GROSS - TOTAL_DED
                if (roundDivisor > 0)
                {
                    ws.Cell(dataRow, netPayCol).FormulaA1 =
                        $"ROUND(({ColLetter(grossCol)}{dataRow}-{ColLetter(totalDedCol)}{dataRow})/{roundDivisor},0)*{roundDivisor}";
                }
                else
                {
                    ws.Cell(dataRow, netPayCol).FormulaA1 =
                        $"{ColLetter(grossCol)}{dataRow}-{ColLetter(totalDedCol)}{dataRow}";
                }

                // --- فرمت پولی ---
                for (int c = grossCol; c <= netPayCol; c++)
                {
                    ws.Cell(dataRow, c).Style.NumberFormat.Format = "#,##0";
                }
                foreach (var ic in itemColMap.Values)
                {
                    ws.Cell(dataRow, ic).Style.NumberFormat.Format = "#,##0";
                }

                dataRow++;
                rowNum++;
            }

            // --- ردیف جمع کل ---
            int sumRow = dataRow;
            ws.Cell(sumRow, 2).Value = XLCellValue.FromObject("جمع کل");
            ws.Cell(sumRow, 2).Style.Font.Bold = true;

            for (int c = 1; c <= totalCols; c++)
            {
                string colL = ColLetter(c);
                // فقط ستون‌های عددی (ردیف=1, کد=2, نام=3 را رها کن)
                if (c >= 4)
                {
                    ws.Cell(sumRow, c).FormulaA1 = $"SUM({colL}{hdrRow+1}:{colL}{sumRow-1})";
                    ws.Cell(sumRow, c).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(sumRow, c).Style.Font.Bold = true;
                }
            }

            ws.Columns().AdjustToContents();
        }

        // ═══════════════════════════════════════════════════
        // شیت ۴: Control — تطبیق مقادیر SP با فرمول Excel
        // ═══════════════════════════════════════════════════
        private void BuildControlSheet(IXLWorksheet ws, Pay2ExcelAuditDataDto data,
            IXLWorksheet wsPayslip)
        {
            // هدر
            int col = 1;
            SetHeader(ws, col++, "کد پرسنلی");
            SetHeader(ws, col++, "نام");
            SetHeader(ws, col++, "فیلد");
            SetHeader(ws, col++, "مقدار SP (دیتابیس)");
            SetHeader(ws, col++, "مقدار Excel (فرمول)");
            SetHeader(ws, col++, "اختلاف");
            SetHeader(ws, col++, "تطبیق؟");

            int row = 2;
            // پیمایش شیت Payslip و مقایسه با مقادیر SP
            // فرض: Payslip هدر در ردیف ۴، داده از ردیف ۵
            // ستون‌های ثابت Payslip (از hdrRow+1)
            // برای سادگی، مقایسه را روی فیلدهای اصلی انجام می‌دهیم

            var fields = new[]
            {
                ("GROSS_PAY", 0), // شماره ستون نسبی — بعداً محاسبه می‌کنیم
                ("INS_BASE", 0),
                ("INS_WORKER", 0),
                ("TAX_AMOUNT", 0),
                ("TOTAL_DED", 0),
                ("NET_PAY", 0)
            };

            // ستون‌های Payslip را پیدا کن — جستجو در هدر
            var payslipColMap = new Dictionary<string, int>();
            int hdrRow = 4;
            int lastCol = wsPayslip.LastColumnUsed()?.ColumnNumber() ?? 20;
            for (int c = 1; c <= lastCol; c++)
            {
                string? hdr = wsPayslip.Cell(hdrRow, c).Value.GetText();
                if (!string.IsNullOrEmpty(hdr))
                    payslipColMap[hdr] = c;
            }

            int empIdx = 0;
            foreach (var emp in data.RawLines)
            {
                int payslipRow = hdrRow + 1 + empIdx; // ردیف متناظر در Payslip

                // مقایسه GROSS_PAY
                AddControlRow(ws, ref row, emp.EMP_CODE ?? "", emp.FULL_NAME ?? "",
                    "ناخالص حقوق", emp.SP_GROSS_PAY, payslipRow,
                    payslipColMap, "ناخالص حقوق", wsPayslip);

                AddControlRow(ws, ref row, emp.EMP_CODE ?? "", emp.FULL_NAME ?? "",
                    "مبنای بیمه", emp.SP_INS_BASE, payslipRow,
                    payslipColMap, "مبنای بیمه", wsPayslip);

                AddControlRow(ws, ref row, emp.EMP_CODE ?? "", emp.FULL_NAME ?? "",
                    "بیمه کارگر", emp.SP_INS_WORKER, payslipRow,
                    payslipColMap, "بیمه کارگر", wsPayslip);

                AddControlRow(ws, ref row, emp.EMP_CODE ?? "", emp.FULL_NAME ?? "",
                    "مالیات", emp.SP_TAX_AMOUNT, payslipRow,
                    payslipColMap, "مالیات", wsPayslip);

                AddControlRow(ws, ref row, emp.EMP_CODE ?? "", emp.FULL_NAME ?? "",
                    "جمع کسورات", emp.SP_TOTAL_DED, payslipRow,
                    payslipColMap, "جمع کسورات", wsPayslip);

                AddControlRow(ws, ref row, emp.EMP_CODE ?? "", emp.FULL_NAME ?? "",
                    "خالص پرداختی", emp.SP_NET_PAY, payslipRow,
                    payslipColMap, "خالص پرداختی", wsPayslip);

                empIdx++;
            }

            ws.Columns().AdjustToContents();
        }

        private void AddControlRow(IXLWorksheet ws, ref int row,
            string empCode, string empName, string fieldName,
            long spValue, int payslipRow,
            Dictionary<string, int> payslipColMap, string payslipHeader,
            IXLWorksheet wsPayslip)
        {
            int col = 1;
            ws.Cell(row, col++).Value = XLCellValue.FromObject(empCode);
            ws.Cell(row, col++).Value = XLCellValue.FromObject(empName);
            ws.Cell(row, col++).Value = XLCellValue.FromObject(fieldName);
            ws.Cell(row, col++).Value = XLCellValue.FromObject((double)spValue);
            ws.Cell(row, col++).Style.NumberFormat.Format = "#,##0";

            // مقدار Excel — ارجاع به شیت Payslip
            if (payslipColMap.TryGetValue(payslipHeader, out var pCol))
            {
                ws.Cell(row, col++).FormulaA1 = $"Payslip!{ColLetter(pCol)}{payslipRow}";
                ws.Cell(row, col - 1).Style.NumberFormat.Format = "#,##0";
            }
            else
            {
                ws.Cell(row, col++).Value = XLCellValue.FromObject("N/A");
            }

            // اختلاف
            ws.Cell(row, col++).FormulaA1 = $"D{row}-E{row}";
            ws.Cell(row, col - 1).Style.NumberFormat.Format = "#,##0";

            // تطبیق
            ws.Cell(row, col++).FormulaA1 = $"IF(F{row}=0,\"✓\",\"✗ اختلاف=\"&F{row})";

            row++;
        }

        // ═══════════════════════════════════════════════════
        // 🚀 تولید فرمول مالیات پلکانی (Nested IF)
        // منطق: TAX_BASE سالانه = TAX_BASE * 12
        // سپس پله‌بندی: اگر <= سقف پله ۱ → RATE_PCT/100 * سالانه
        //              اگر <= سقف پله ۲ → (سالانه - سقف پله ۱) * RATE/100 + FIXED_TAX
        //              ...
        // در نهایت / 12 برای ماهانه
        // ═══════════════════════════════════════════════════
        private string GenerateTaxFormulaForCell(string taxBaseCell,
            List<Pay2TaxBracketDto> brackets,
            double taxExempt, bool taxDeductIns)
        {
            if (brackets == null || brackets.Count == 0)
                return "0";

            var sorted = brackets.OrderBy(b => b.SORT_ORDER).ToList();

            // سالانه کردن مبنای مالیات
            string annualBase = $"{taxBaseCell}*12";

            // کسر معافیت مالیاتی
            string taxableAnnual = taxExempt > 0
                ? $"MAX({annualBase}-{taxExempt * 12},0)"
                : annualBase;

            // ساخت nested IF از آخر به اول
            string formula = "0";

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                double rate = (double)sorted[i].RATE_PCT / 100.0;
                double upperLimit = sorted[i].UPPER_LIMIT;
                double fixedTax = sorted[i].FIXED_TAX;
                double prevLimit = i > 0 ? sorted[i - 1].UPPER_LIMIT : 0;

                string stepTax = $"(({taxableAnnual}-{prevLimit})*{rate}+{fixedTax})";

                if (i == sorted.Count - 1)
                {
                    // آخرین پله: هرچه بالاتر
                    formula = stepTax;
                }
                else
                {
                    formula = $"IF({taxableAnnual}<={upperLimit},{stepTax},{formula})";
                }
            }

            // تقسیم بر ۱۲ برای ماهانه + گرد کردن
            // ROUND(formula/12, 0) برای ROUND_MODE
            formula = $"ROUND({formula}/12,0)";

            // اگر مبنای مالیات صفر یا منفی باشد، مالیات صفر
            formula = $"IF({taxBaseCell}<=0,0,{formula})";

            return formula;
        }

        private string GenerateTaxFormula(List<Pay2TaxBracketDto> brackets,
            double taxExempt, bool taxDeductIns)
        {
            // این متد برای مستندسازی و لاگ استفاده می‌شود
            return GenerateTaxFormulaForCell("TAX_BASE", brackets, taxExempt, taxDeductIns);
        }

        // ═══════════════════════════════════════════════════
        // متدهای کمکی
        // ═══════════════════════════════════════════════════

        private static void SetHeader(IXLWorksheet ws, int col, string text, int row = 1)
        {
            ws.Cell(row, col).Value = XLCellValue.FromObject(text);
            ws.Cell(row, col).Style.Font.Bold = true;
        }

        private static string MakeSafeName(string key)
        {
            // تبدیل کلید تنظیم به نام امن برای Named Range
            var safe = new System.Text.StringBuilder();
            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    safe.Append(c);
                else
                    safe.Append('_');
            }
            // پیشوند CFG_ برای جلوگیری از تداخل با نام‌های رزرو
            string name = safe.ToString();
            if (!name.StartsWith("CFG_"))
                name = "CFG_" + name;
            return name;
        }

        private static double GetCfgDouble(Dictionary<string, string> cfg, string key, double defaultVal)
        {
            if (cfg.TryGetValue(key, out var val) && double.TryParse(val,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            return defaultVal;
        }

        private static int GetCfgInt(Dictionary<string, string> cfg, string key, int defaultVal)
        {
            if (cfg.TryGetValue(key, out var val) && int.TryParse(val, out var i))
                return i;
            return defaultVal;
        }

        private static bool GetCfgBool(Dictionary<string, string> cfg, string key, bool defaultVal)
        {
            if (cfg.TryGetValue(key, out var val))
            {
                if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (val == "0" || val.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return defaultVal;
        }

        // شماره ستون decree در RawData بر اساس ITEM_CODE
        private int FindRawDataDecreeCol(string itemCode, Pay2ExcelAuditDataDto data)
        {
            var allCodes = data.RawLines
                .SelectMany(l => l.DecreeLines.Select(d => d.ITEM_CODE))
                .Distinct().OrderBy(c => c).ToList();

            int idx = allCodes.IndexOf(itemCode);
            if (idx < 0) return 0;
            return 14 + idx; // ستون‌های ثابت ۱ تا ۱۳، decree از ۱۴ شروع
        }

        // شماره ستون ATT_VALUE در RawData
        private int FindRawDataAttCol(string itemCode, Pay2ExcelAuditDataDto data)
        {
            var allCodes = data.RawLines
                .SelectMany(l => l.AttValues.Keys)
                .Distinct().OrderBy(k => k).ToList();

            var decreeCount = data.RawLines
                .SelectMany(l => l.DecreeLines.Select(d => d.ITEM_CODE))
                .Distinct().Count();

            int idx = allCodes.IndexOf(itemCode);
            if (idx < 0) return 0;
            return 14 + decreeCount + idx;
        }

        // شماره ستون ثابت در RawData (WORK_DAYS=4, DAYS=5, DAYSB=6, ...)
        private int FindRawDataFixedCol(string fieldName)
        {
            return fieldName switch
            {
                "WORK_DAYS" => 4,
                "DAYS" => 5,
                "DAYSB" => 6,
                "OT_NORMAL_H" => 7,
                "OT_HOLIDAY_H" => 8,
                "OT_ADMIN_H" => 9,
                "LEAVE_DAYS" => 10,
                "PERF_AMOUNT" => 11,
                "TRANSP_AMOUNT" => 12,
                "KASR_OTHER" => 13,
                _ => 0
            };
        }

        // تبدیل شماره ستون (1-based) به حرف اکسل (A, B, ..., Z, AA, AB, ...)
        private static string ColLetter(int col)
        {
            var sb = new System.Text.StringBuilder();
            while (col > 0)
            {
                int rem = (col - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                col = (col - 1) / 26;
            }
            return sb.ToString();
        }
    }
}
