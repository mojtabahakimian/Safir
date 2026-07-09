using ClosedXML.Excel;
using Safir.Shared.Models.Salary;
using System.Globalization;

namespace Safir.Server.Reports
{
    /// <summary>
    /// می‌سازد یک Workbook که در آن «هر عددِ فیش، یک فرمولِ واقعی اکسل» است.
    /// فرمول‌ها به شیت‌های «تنظیمات»، «پله‌های مالیات» و «کارکرد خام» ارجاع می‌دهند
    /// و زنجیرهٔ محاسبهٔ موتور C# (SP_PAY2_CALC_RUN) را مو‌به‌مو بازسازی می‌کنند.
    /// نکته‌های تطبیقِ گردکردن:
    ///   • CAST(... AS BIGINT) در T-SQL ⇒ TRUNC(...) در اکسل (بریدن به سمت صفر)
    ///   • ROUND(...,0) صریح در T-SQL ⇒ ROUND(...,0) در اکسل
    ///   • تقسیم صحیحِ BIGINT در مالیات ⇒ TRUNC(.../12)
    /// </summary>
    public static class Pay2ExcelAuditWorkbook
    {
        // نام شیت‌ها
        private const string SH_HELP = "راهنما";
        private const string SH_CONFIG = "تنظیمات";
        private const string SH_TAX = "پله‌های مالیات";
        private const string SH_RAW = "کارکرد خام";
        private const string SH_BD = "محاسبات ریز";
        private const string SH_SLIP = "فیش حقوقی";
        private const string SH_DETAIL = "فیش تفصیلی";
        private const string SH_REC = "کنترل تطابق";

        private const string NUM_FMT = "#,##0";
        private static readonly XLColor HeaderFill = XLColor.FromHtml("#D9E1F2");
        private static readonly XLColor TitleFill = XLColor.FromHtml("#4472C4");

        // ── ستون‌های شیت «محاسبات ریز» (ثابت) ──
        private const int BD_EMP = 1;   // A
        private const int BD_CODE = 2;  // B  کد پرسنلی
        private const int BD_NAME = 3;  // C
        private const int BD_ICODE = 4; // D  کد آیتم
        private const int BD_INAME = 5; // E
        private const int BD_SRC = 6;   // F  منبع
        private const int BD_TYPE = 7;  // G  نوع آیتم
        private const int BD_BASIS = 8; // H  basis مؤثر
        private const int BD_RATE = 9;  // I  نرخ/مبلغ خام
        private const int BD_PDAYS = 10;// J  روز پرداخت خام
        private const int BD_IDAYS = 11;// K  روز بیمه خام
        private const int BD_ADAYS = 12;// L  روزهای فعال حکم
        private const int BD_MDAYS = 13;// M  طول ماه
        private const int BD_PRORATE = 14;// N ضریب تناسب (=L/M)
        private const int BD_HOURS = 15;// O  ساعت (اضافه‌کار)
        private const int BD_MULT = 16; // P  ضریب اضافه‌کار
        private const int BD_HOURLY = 17;//Q نرخ ساعتی مؤثر
        private const int BD_DBASE = 18;// R  پایه روزانه حکم (شیفت درصدی)
        private const int BD_FRID = 19; // S  جمعه‌ها
        private const int BD_LEAVE = 20;// T  مرخصی
        private const int BD_TDAYS = 21;// U  تعطیل جبرانی
        private const int BD_DAYSB = 22;// V  DAYSB
        private const int BD_EARN = 23; // W  مشمول ناخالص؟
        private const int BD_INSF = 24; // X  مشمول بیمه؟
        private const int BD_TAXF = 25; // Y  مشمول مالیات؟
        private const int BD_AMT = 26;  // Z  مبلغ پرداخت (فرمول)
        private const int BD_INSAMT = 27;//AA مبلغ مشمول بیمه (فرمول)
        private const int BD_DNOM = 28; // AB  پایه روزانه اسمی حکم (ریلِ پرداختِ شیفتِ درصدی)
        private const int BD_DAYS = 29; // AC  کارکرد اسمی DAYS (ریلِ بیمهٔ اضافه‌کارِ خودکار)
        private const int BD_HOURLY_OFF = 30;//AD نرخ ساعتی رسمی (بیمهٔ اضافه‌کارِ خودکار)
        private const int BD_DESC = 31; // AE  شرح فرمول (متن پویا)

        public static byte[] Build(Pay2ExcelAuditData d)
        {
            using var wb = new XLWorkbook();

            BuildHelpSheet(wb, d);
            BuildConfigSheet(wb, d);
            BuildTaxSheet(wb, d);
            BuildRawSheet(wb, d);
            var bdRows = BuildBreakdownSheet(wb, d);
            BuildSlipSheet(wb, d);
            BuildDetailedSlipSheet(wb, d, bdRows);
            BuildReconciliationSheet(wb, d);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ════════════════════════════ شیت تنظیمات ════════════════════════════
        private static void BuildConfigSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_CONFIG);
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = "کلید تنظیم";
            ws.Cell(1, 2).Value = "مقدار";
            ws.Cell(1, 3).Value = "توضیح";
            ws.Range(1, 1, 1, 3).Style.Font.Bold = true;

            int r = 2;
            void Cfg(string name, string label, XLCellValue value, string desc, string? fmt = null)
            {
                ws.Cell(r, 1).Value = label;
                ws.Cell(r, 2).Value = value;
                if (fmt != null) ws.Cell(r, 2).Style.NumberFormat.Format = fmt;
                ws.Cell(r, 3).Value = desc;
                ws.Cell(r, 2).AddToNamed(name, XLScope.Workbook);
                r++;
            }

            Cfg("CFG_MONTH_DAYS", "طول ماه (روز)", d.MONTH_DAYS, "تعداد روزهای این ماه شمسی (مبنای prorate)");
            Cfg("CFG_OT_NORMAL_MULT", "ضریب اضافه‌کار عادی", CfgDec(d, "OT_NORMAL_MULT", 1.40m), "ضربِ نرخ ساعتی برای اضافه‌کار عادی/اداری");
            Cfg("CFG_OT_HOLIDAY_MULT", "ضریب اضافه‌کار تعطیل", CfgDec(d, "OT_HOLIDAY_MULT", 1.40m), "ضربِ نرخ ساعتی برای اضافه‌کار تعطیل");
            Cfg("CFG_OT_HOUR_BASE", "مبنای ساعت روزانه", CfgDec(d, "OT_HOUR_BASE", 7.33m), "ساعت کار روزانه برای تبدیل حقوق روز به نرخ ساعتی");
            Cfg("CFG_INS_WORKER_PCT", "درصد بیمه سهم کارگر", CfgDec(d, "INS_WORKER_RATE", 7m), "درصد کسر بیمهٔ سهم کارگر (÷۱۰۰ در فرمول)");
            Cfg("CFG_INS_CEILING", "سقف ماهانه بیمه", CfgLong(d, "INS_CEILING_MONTHLY", 999999999), "سقف دستمزد مشمول بیمه (ماهانه)", NUM_FMT);
            Cfg("CFG_INS_CEILING_APPLY", "اعمال سقف بیمه؟", CfgInt(d, "INS_CEILING_APPLY", 1), "۱=سقف بیمه اعمال شود، ۰=خیر");
            Cfg("CFG_TAX_EXEMPT", "معافیت ماهانه مالیات", CfgLong(d, "TAX_EXEMPT_MONTHLY", 0), "سقف معافیت مالیاتی ماهانه", NUM_FMT);
            Cfg("CFG_TAX_DEDUCT_INS", "کسر بیمه از مبنای مالیات؟", CfgInt(d, "TAX_DEDUCT_INS", 1), "۱=بیمهٔ کارگر از مبنای مالیات کم شود");
            Cfg("CFG_TAX_DEP_APPLY", "اعمال معافیت منطقه محروم؟", CfgInt(d, "TAX_DEPRIVATION_APPLY", 0), "۱=درصد منطقه محروم روی مبنای مالیات اعمال شود");
            Cfg("CFG_ROUND_MODE", "واحد گرد کردن خالص", CfgInt(d, "ROUND_MODE", 1), ">۱ ⇒ خالص به این واحد گرد می‌شود");
            Cfg("CFG_MONTHLY_PRORATE", "prorate آیتم‌های ماهانه؟", CfgInt(d, "MONTHLY_ITEM_PRORATE", 0), "۱=آیتم‌های ماهانه بر اساس روزِ کارکرد نسبت‌گیری شوند");

            ws.Columns(1, 3).AdjustToContents();
            ws.Visibility = XLWorksheetVisibility.Hidden;
        }

        // ════════════════════════════ شیت پله‌های مالیات ════════════════════════════
        private static void BuildTaxSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_TAX);
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = "ردیف";
            ws.Cell(1, 2).Value = "سقف سالانه";
            ws.Cell(1, 3).Value = "نرخ٪";
            ws.Cell(1, 4).Value = "مالیات ثابت پله";
            ws.Range(1, 1, 1, 4).Style.Font.Bold = true;

            int r = 2;
            foreach (var b in d.TaxBrackets.OrderBy(x => x.SORT_ORDER))
            {
                ws.Cell(r, 1).Value = b.SORT_ORDER;
                ws.Cell(r, 2).Value = b.UPPER_LIMIT;
                ws.Cell(r, 3).Value = b.RATE_PCT;
                ws.Cell(r, 4).Value = b.FIXED_TAX;
                ws.Cell(r, 2).Style.NumberFormat.Format = NUM_FMT;
                ws.Cell(r, 4).Style.NumberFormat.Format = NUM_FMT;
                r++;
            }

            ws.Cell(r + 1, 1).Value = $"سال مالیاتی: {d.TAX_YEAR}";
            ws.Columns(1, 4).AdjustToContents();
            ws.Visibility = XLWorksheetVisibility.Hidden;
        }

        // ════════════════════════════ شیت کارکرد خام ════════════════════════════
        // یک ردیف به‌ازای هر پرسنل (هم‌ترتیب با شیت فیش). ستون‌ها:
        // A EMP_ID | B کد | C نام | D WORK_DAYS | E DAYS | F DAYSB | G FRID | H TDAYS
        // I OT_N_H | J OT_H_H | K OT_A_H | L LEAVE | M PERF | N TRANSP | O KASR_OTHER
        // P INS_TYPE | Q TAX_EXEMPT | R IS_MANAGER | S IS_JANBAZ | T REGION_DEP
        // U LOAN_DED | V ADVANCE_DED
        private static void BuildRawSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_RAW);
            ws.RightToLeft = true;

            string[] headers =
            {
                "EMP_ID","کد پرسنلی","نام","کل کارکرد","کارکرد اسمی (DAYS)","کارکرد رسمی (DAYSB)",
                "جمعه‌ها","تعطیل جبرانی","ساعت اضافه‌کار عادی","ساعت اضافه‌کار تعطیل","ساعت اضافه‌کار اداری",
                "روز مرخصی","پاداش (خام)","ناقل (خام)","سایر کسورات","نوع بیمه","معاف مالیات",
                "مدیر","جانباز","درصد منطقه محروم","قسط وام (ورودی)","مساعده (ورودی)"
            };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

            var loanByEmp = d.Results.ToDictionary(x => x.EMP_ID, x => x);

            int r = 2;
            foreach (var e in d.Employees)
            {
                loanByEmp.TryGetValue(e.EMP_ID, out var res);
                ws.Cell(r, 1).Value = e.EMP_ID;
                ws.Cell(r, 2).Value = e.EMP_CODE;
                ws.Cell(r, 3).Value = e.FULL_NAME;
                ws.Cell(r, 4).Value = e.WORK_DAYS;
                ws.Cell(r, 5).Value = e.DAYS;
                ws.Cell(r, 6).Value = e.DAYSB;
                ws.Cell(r, 7).Value = e.FRID_COUNT;
                ws.Cell(r, 8).Value = e.TDAYS;
                ws.Cell(r, 9).Value = e.OT_NORMAL_H;
                ws.Cell(r, 10).Value = e.OT_HOLIDAY_H;
                ws.Cell(r, 11).Value = e.OT_ADMIN_H;
                ws.Cell(r, 12).Value = e.LEAVE_DAYS;
                ws.Cell(r, 13).Value = e.PERF_AMOUNT;
                ws.Cell(r, 14).Value = e.TRANSP_AMOUNT;
                ws.Cell(r, 15).Value = e.KASR_OTHER;
                ws.Cell(r, 16).Value = e.INS_TYPE;
                ws.Cell(r, 17).Value = e.TAX_EXEMPT ? 1 : 0;
                ws.Cell(r, 18).Value = e.IS_MANAGER ? 1 : 0;
                ws.Cell(r, 19).Value = e.IS_JANBAZ ? 1 : 0;
                ws.Cell(r, 20).Value = e.REGION_DEPRIVATION;
                ws.Cell(r, 21).Value = res?.LOAN_DED ?? 0;
                ws.Cell(r, 22).Value = res?.ADVANCE_DED ?? 0;
                r++;
            }

            ws.Columns(1, headers.Length).AdjustToContents();
            ws.Visibility = XLWorksheetVisibility.Hidden;
        }

        // ════════════════════════════ شیت محاسبات ریز ════════════════════════════
        // خروجی: نگاشتِ EMP_ID → شمارهٔ ردیف‌های این پرسنل (برای ساختِ فیش تفصیلی)
        private static Dictionary<int, List<int>> BuildBreakdownSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_BD);
            ws.RightToLeft = true;

            string[] headers =
            {
                "EMP_ID","کد پرسنلی","نام","کد آیتم","نام آیتم","منبع","نوع","basis",
                "نرخ/مبلغ خام","روز پرداخت (خام)","روز بیمه (خام)","روزهای فعال حکم","طول ماه","ضریب تناسب",
                "ساعت","ضریب اضافه‌کار","نرخ ساعتی مؤثر","پایه روزانه حکم","جمعه‌ها","مرخصی","تعطیل جبرانی","DAYSB",
                "مشمول ناخالص","مشمول بیمه","مشمول مالیات","مبلغ پرداخت (فرمول)","مبلغ مشمول بیمه (فرمول)",
                "پایه روزانه اسمی","کارکرد اسمی (DAYS)","نرخ ساعتی رسمی","شرح فرمول"
            };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            var head = ws.Range(1, 1, 1, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = HeaderFill;

            var defByCode = d.ItemDefs.ToDictionary(x => x.ITEM_CODE, x => x, StringComparer.OrdinalIgnoreCase);
            var linesByEmp = d.DecreeLines.Where(l => l.ACTIVE_DAYS > 0)
                                          .GroupBy(l => l.EMP_ID)
                                          .ToDictionary(g => g.Key, g => g.ToList());
            var attByEmp = d.AttValues.GroupBy(a => a.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            var bdRows = new Dictionary<int, List<int>>();
            int r = 2;
            foreach (var e in d.Employees)
            {
                var addedItemIds = new HashSet<int>();
                var codesPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var myRows = new List<int>();

                // آیا هر دو حقوقِ اسمی (BASE_SAL) و رسمی (BASE_SAL_B) در احکام هست؟ (سیستمِ دو-ریلی)
                linesByEmp.TryGetValue(e.EMP_ID, out var lines);
                bool hasBothSal = lines != null
                    && lines.Any(l => l.ITEM_CODE.Equals("BASE_SAL", StringComparison.OrdinalIgnoreCase))
                    && lines.Any(l => l.ITEM_CODE.Equals("BASE_SAL_B", StringComparison.OrdinalIgnoreCase));

                // آیا ریلِ حقوقِ رسمی (BASE_SAL_B) واقعاً مشمولِ بیمه/مالیات و غیرصفر است؟ (منطبق با SP)
                // اگر رسمی معاف/صفر باشد، پایهٔ بیمه/مالیات به حقوقِ اسمی (BASE_SAL) fallback می‌شود.
                bool insOfficialValid = hasBothSal && lines!.Any(l =>
                    l.ITEM_CODE.Equals("BASE_SAL_B", StringComparison.OrdinalIgnoreCase) && l.EFF_INS && l.RAW_AMOUNT != 0);
                bool taxOfficialValid = hasBothSal && lines!.Any(l =>
                    l.ITEM_CODE.Equals("BASE_SAL_B", StringComparison.OrdinalIgnoreCase) && l.EFF_TAX && l.RAW_AMOUNT != 0);

                // (۱) خطوط احکام
                if (lines != null)
                {
                    foreach (var l in lines)
                    {
                        WriteDecreeRow(ws, r, e, l, hasBothSal, insOfficialValid, taxOfficialValid);
                        addedItemIds.Add(l.ITEM_ID);
                        codesPresent.Add(l.ITEM_CODE);
                        myRows.Add(r);
                        r++;
                    }
                }

                // (۲) اضافه‌کارِ خودکار (گام ۶ موتور)
                int b;
                b = r; r = TryWriteAutoOt(ws, r, e, "OT_NORMAL", e.OT_NORMAL_H, "CFG_OT_NORMAL_MULT", defByCode, codesPresent, addedItemIds, hasBothSal); if (r > b) myRows.Add(b);
                b = r; r = TryWriteAutoOt(ws, r, e, "OT_HOLIDAY", e.OT_HOLIDAY_H, "CFG_OT_HOLIDAY_MULT", defByCode, codesPresent, addedItemIds, hasBothSal); if (r > b) myRows.Add(b);
                b = r; r = TryWriteAutoOt(ws, r, e, "OT_ADMIN", e.OT_ADMIN_H, "CFG_OT_NORMAL_MULT", defByCode, codesPresent, addedItemIds, hasBothSal); if (r > b) myRows.Add(b);

                // (۳) پاداش/ناقل از کارکرد
                if (e.PERF_AMOUNT > 0 && defByCode.TryGetValue("PERF_BONUS", out var perf))
                {
                    WriteLiteralRow(ws, r, e, perf.ITEM_ID, "PERF_BONUS", perf.ITEM_NAME, perf.ITEM_TYPE,
                        e.PERF_AMOUNT, perf.INS_SUBJECT, perf.TAX_SUBJECT, "کارکرد");
                    addedItemIds.Add(perf.ITEM_ID); myRows.Add(r); r++;
                }
                if (e.TRANSP_AMOUNT > 0 && defByCode.TryGetValue("TRANSP", out var tr))
                {
                    WriteLiteralRow(ws, r, e, tr.ITEM_ID, "TRANSP", tr.ITEM_NAME, tr.ITEM_TYPE,
                        e.TRANSP_AMOUNT, tr.INS_SUBJECT, tr.TAX_SUBJECT, "کارکرد");
                    addedItemIds.Add(tr.ITEM_ID); myRows.Add(r); r++;
                }

                // (۴) مقادیر دستیِ آیتم‌ها (بدون دوبار‌شماری)
                if (attByEmp.TryGetValue(e.EMP_ID, out var atts))
                {
                    foreach (var a in atts)
                    {
                        if (addedItemIds.Contains(a.ITEM_ID)) continue;
                        WriteLiteralRow(ws, r, e, a.ITEM_ID, a.ITEM_CODE, a.ITEM_NAME, a.ITEM_TYPE,
                            a.VALUE, a.INS_SUBJECT, a.TAX_SUBJECT, "دستی");
                        addedItemIds.Add(a.ITEM_ID); myRows.Add(r); r++;
                    }
                }

                bdRows[e.EMP_ID] = myRows;
            }

            int bdLast = Math.Max(2, r - 1);
            ws.Range(2, BD_AMT, bdLast, BD_INSAMT).Style.NumberFormat.Format = NUM_FMT;
            ws.Range(2, BD_DNOM, bdLast, BD_HOURLY_OFF).Style.NumberFormat.Format = NUM_FMT;
            ws.Columns(1, BD_HOURLY_OFF).AdjustToContents();
            ws.Column(BD_DESC).Width = 55;

            AddComment(ws, 1, BD_RATE, "نرخ روزانه یا مبلغ ماهانهٔ آیتم طبق حکم/تنظیم.");
            AddComment(ws, 1, BD_PDAYS, "روزهای مبنای پرداخت (اسمی یا رسمی بسته به آیتم).");
            AddComment(ws, 1, BD_IDAYS, "روزهای مبنای بیمه (ممکن است با روزِ پرداخت فرق کند).");
            AddComment(ws, 1, BD_PRORATE, "ضریبِ تناسب = روزهای فعال حکم ÷ طول ماه.");
            AddComment(ws, 1, BD_AMT, "مبلغِ نهاییِ این خط (فرمولِ واقعی؛ روی سلول کلیک کنید).");
            AddComment(ws, 1, BD_DESC, "توضیحِ خواناى همان فرمول با مقادیرِ واقعی.");

            head.SetAutoFilter();
            ws.SheetView.FreezeRows(1);
            ws.SheetView.FreezeColumns(3);
            ws.Visibility = XLWorksheetVisibility.Visible;
            return bdRows;
        }

        private static void WriteDecreeRow(IXLWorksheet ws, int r, Pay2AuditEmpRow e, Pay2AuditDecreeLineRow l, bool hasBothSal, bool insOfficialValid, bool taxOfficialValid)
        {
            decimal payDaysRaw = l.PAY_BASE_DAYS == 1 ? e.DAYS : e.DAYSB;
            decimal insDaysRaw = l.INS_BASE_DAYS == 1 ? e.DAYS : e.DAYSB;
            string code = l.ITEM_CODE.ToUpperInvariant();
            bool isNominalSal = code == "BASE_SAL";
            bool isOfficialSal = code == "BASE_SAL_B";

            ws.Cell(r, BD_EMP).Value = e.EMP_ID;
            ws.Cell(r, BD_CODE).Value = e.EMP_CODE;
            ws.Cell(r, BD_NAME).Value = e.FULL_NAME;
            ws.Cell(r, BD_ICODE).Value = l.ITEM_CODE;
            ws.Cell(r, BD_INAME).Value = l.ITEM_NAME;
            ws.Cell(r, BD_SRC).Value = "حکم";
            ws.Cell(r, BD_TYPE).Value = l.ITEM_TYPE;
            ws.Cell(r, BD_BASIS).Value = l.EFF_BASIS;
            ws.Cell(r, BD_RATE).Value = l.RAW_AMOUNT;
            ws.Cell(r, BD_PDAYS).Value = payDaysRaw;
            ws.Cell(r, BD_IDAYS).Value = insDaysRaw;
            ws.Cell(r, BD_ADAYS).Value = l.ACTIVE_DAYS;
            ws.Cell(r, BD_MDAYS).FormulaA1 = "CFG_MONTH_DAYS";
            ws.Cell(r, BD_PRORATE).FormulaA1 = $"{A(BD_ADAYS, r)}/{A(BD_MDAYS, r)}";
            ws.Cell(r, BD_DBASE).Value = l.DEC_DAILY_BASE;
            ws.Cell(r, BD_DNOM).Value = l.DEC_DAILY_NOM;
            ws.Cell(r, BD_DAYS).Value = e.DAYS;
            ws.Cell(r, BD_FRID).Value = e.FRID_COUNT;
            ws.Cell(r, BD_LEAVE).Value = e.LEAVE_DAYS;
            ws.Cell(r, BD_TDAYS).Value = e.TDAYS;
            ws.Cell(r, BD_DAYSB).Value = e.DAYSB;

            // ساعت (فقط برای اضافه‌کارِ basis=3 داخل حکم)
            decimal hours = code switch
            {
                "OT_NORMAL" => e.OT_NORMAL_H,
                "OT_HOLIDAY" => e.OT_HOLIDAY_H,
                "OT_ADMIN" => e.OT_ADMIN_H,
                _ => 0m
            };
            ws.Cell(r, BD_HOURS).Value = hours;

            // ── پرچم‌های دو-ریلی (منطبق با SP_PAY2_CALC_RUN) ──
            // ناخالص از AMOUNT: وقتی هر دو حقوق هست BASE_SAL_B کنار می‌رود؛ اگر فقط رسمی باشد، خودش لحاظ می‌شود.
            bool isEarnItem = l.ITEM_TYPE == 1 || l.ITEM_TYPE == 2;
            bool earn = isEarnItem && !(hasBothSal && isOfficialSal);
            // بیمه و مالیات از INS_AMOUNT: وقتی هر دو حقوق هست، دقیقاً یک ریلِ پایه لحاظ می‌شود:
            // رسمی (BASE_SAL_B) اگر مشمول و غیرصفر باشد، وگرنه fallback به اسمی (BASE_SAL).
            bool insDropNominal  = hasBothSal &&  insOfficialValid;
            bool insDropOfficial = hasBothSal && !insOfficialValid;
            bool taxDropNominal  = hasBothSal &&  taxOfficialValid;
            bool taxDropOfficial = hasBothSal && !taxOfficialValid;
            bool insf = isEarnItem && l.EFF_INS && !(isNominalSal && insDropNominal) && !(isOfficialSal && insDropOfficial);
            bool taxf = isEarnItem && l.EFF_TAX && !(isNominalSal && taxDropNominal) && !(isOfficialSal && taxDropOfficial);
            ws.Cell(r, BD_EARN).Value = earn ? 1 : 0;
            ws.Cell(r, BD_INSF).Value = insf ? 1 : 0;
            ws.Cell(r, BD_TAXF).Value = taxf ? 1 : 0;

            var (amt, ins) = DecreeFormulas(l, r);
            ws.Cell(r, BD_AMT).FormulaA1 = amt;
            ws.Cell(r, BD_INSAMT).FormulaA1 = ins;
            ws.Cell(r, BD_DESC).FormulaA1 = DecreeDescFormula(l, r);
        }

        // بازسازیِ شاخه‌به‌شاخهٔ منطق SP برای مبلغِ پرداخت و مبلغِ مشمولِ بیمه
        private static (string amount, string ins) DecreeFormulas(Pay2AuditDecreeLineRow l, int r)
        {
            string I = A(BD_RATE, r), J = A(BD_PDAYS, r), K = A(BD_IDAYS, r),
                   M = A(BD_MDAYS, r), N = A(BD_PRORATE, r), O = A(BD_HOURS, r),
                   R = A(BD_DBASE, r), S = A(BD_FRID, r), T = A(BD_LEAVE, r),
                   U = A(BD_TDAYS, r), V = A(BD_DAYSB, r), Rn = A(BD_DNOM, r);

            string code = l.ITEM_CODE.ToUpperInvariant();

            if (code == "BASE_SAL" || code == "BASE_SAL_B")
                return ($"TRUNC({I}*({J}*{N}))", $"TRUNC({I}*({K}*{N}))");

            if (code == "HOME" || code == "CHILDREN" || code == "GROCERY")
                return ($"TRUNC(IF({J}>=28,{I},TRUNC({I}*{J}/30))*{N})",
                        $"TRUNC(IF({K}>=28,{I},TRUNC({I}*{K}/30))*{N})");

            if (code == "NAHAR")
            {
                string nd = $"(({V}-{S}-{T}+{U})*{N})";
                string f = $"IF({nd}>0,TRUNC({I}*{nd}),TRUNC({I}*({J}*{N})))";
                return (f, f);
            }

            if (code == "SHIFT")
            {
                bool fixedMode = string.Equals(l.EFF_SHIFT_MODE, "FIXED", StringComparison.OrdinalIgnoreCase);
                // پرداخت روی ریلِ اسمی (روزِ پرداخت + پایه روزانه اسمی)، بیمه روی ریلِ رسمی (روزِ بیمه + پایه روزانه رسمی)
                string amt = fixedMode
                    ? $"TRUNC({I}*(({J}*{N})/{M}))"
                    : $"ROUND({Rn}*({J}*{N})*{I}/100,0)";
                string ins = fixedMode
                    ? $"TRUNC({I}*(({K}*{N})/{M}))"
                    : $"ROUND({R}*({K}*{N})*{I}/100,0)";
                return (amt, ins);
            }

            if (l.EFF_BASIS == 3)
            {
                string f = code switch
                {
                    "OT_NORMAL" or "OT_HOLIDAY" or "OT_ADMIN" => $"TRUNC({I}*{O})",
                    _ => $"TRUNC({I}*({J}*{N})*CFG_OT_HOUR_BASE)"
                };
                return (f, f);
            }

            if (l.EFF_BASIS == 2)
            {
                string f = $"IF(CFG_MONTHLY_PRORATE=1,TRUNC({I}*(({J}*{N})/{M})),TRUNC({I}*{N}))";
                return (f, f);
            }

            if (l.EFF_BASIS == 1)
                return ($"TRUNC({I}*({J}*{N}))", $"TRUNC({I}*({K}*{N}))");

            return (I, I);
        }

        // متنِ خواناى فرمول (به‌صورت فرمولِ اکسل با TEXT/& تا با مقادیر واقعی زنده بماند)
        private static string DecreeDescFormula(Pay2AuditDecreeLineRow l, int r)
        {
            string I = A(BD_RATE, r), J = A(BD_PDAYS, r), M = A(BD_MDAYS, r), N = A(BD_PRORATE, r),
                   O = A(BD_HOURS, r), Rb = A(BD_DNOM, r), Z = A(BD_AMT, r);
            string Num(string cell) => $"TEXT({cell},\"#,##0\")";
            string Fac(string cell) => $"TEXT({cell},\"0.00\")";
            string code = l.ITEM_CODE.ToUpperInvariant();

            if (code == "BASE_SAL" || code == "BASE_SAL_B" || l.EFF_BASIS == 1)
                return $"\"= نرخ روزانه \"&{Num(I)}&\" × (\"&{Num(J)}&\" روز × ضریب \"&{Fac(N)}&\") = \"&{Num(Z)}";

            if (code == "HOME" || code == "CHILDREN" || code == "GROCERY")
                return $"\"= \"&IF({J}>=28,\"کل مبلغ \"&{Num(I)},{Num(I)}&\"×\"&{Num(J)}&\"÷۳۰\")&\" × ضریب \"&{Fac(N)}&\" = \"&{Num(Z)}";

            if (code == "NAHAR")
                return $"\"= نرخ نهار \"&{Num(I)}&\" × روزهای مشمولِ نهار = \"&{Num(Z)}";

            if (code == "SHIFT")
            {
                bool fixedMode = string.Equals(l.EFF_SHIFT_MODE, "FIXED", StringComparison.OrdinalIgnoreCase);
                return fixedMode
                    ? $"\"= مبلغ شیفت \"&{Num(I)}&\" × (\"&{Num(J)}&\" روز × ضریب \"&{Fac(N)}&\") ÷ طول ماه \"&{Num(M)}&\" = \"&{Num(Z)}"
                    : $"\"= پایه روزانه حکم \"&{Num(Rb)}&\" × (روز×ضریب) × \"&{Num(I)}&\"٪ ÷ ۱۰۰ = \"&{Num(Z)}";
            }

            if (l.EFF_BASIS == 3)
                return (code == "OT_NORMAL" || code == "OT_HOLIDAY" || code == "OT_ADMIN")
                    ? $"\"= نرخ \"&{Num(I)}&\" × ساعت \"&{Num(O)}&\" = \"&{Num(Z)}"
                    : $"\"= نرخ ساعتی \"&{Num(I)}&\" × (روز×ضریب) × مبنای ساعت = \"&{Num(Z)}";

            if (l.EFF_BASIS == 2)
                return $"\"= مبلغ ماهانه \"&{Num(I)}&IF(CFG_MONTHLY_PRORATE=1,\" × ضریب \"&{Fac(N)},\"\")&\" = \"&{Num(Z)}";

            return $"\"مقدار = \"&{Num(Z)}";
        }

        private static int TryWriteAutoOt(IXLWorksheet ws, int r, Pay2AuditEmpRow e, string code,
            decimal hours, string multName, Dictionary<string, Pay2AuditItemDefRow> defByCode,
            HashSet<string> codesPresent, HashSet<int> addedItemIds, bool hasBothSal)
        {
            if (hours <= 0 || codesPresent.Contains(code)) return r;
            if (!defByCode.TryGetValue(code, out var def)) return r;

            ws.Cell(r, BD_EMP).Value = e.EMP_ID;
            ws.Cell(r, BD_CODE).Value = e.EMP_CODE;
            ws.Cell(r, BD_NAME).Value = e.FULL_NAME;
            ws.Cell(r, BD_ICODE).Value = code;
            ws.Cell(r, BD_INAME).Value = def.ITEM_NAME;
            ws.Cell(r, BD_SRC).Value = "اضافه‌کار خودکار";
            ws.Cell(r, BD_TYPE).Value = def.ITEM_TYPE;
            ws.Cell(r, BD_BASIS).Value = 3;
            ws.Cell(r, BD_HOURS).Value = hours;
            ws.Cell(r, BD_MULT).FormulaA1 = multName;
            ws.Cell(r, BD_DAYSB).Value = e.DAYSB;
            ws.Cell(r, BD_DAYS).Value = e.DAYS;

            string aEmp = A(BD_EMP, r), V = A(BD_DAYSB, r), Vd = A(BD_DAYS, r);
            string bsCol = $"{Col(BD_AMT)}:{Col(BD_AMT)}";
            string empCol = $"{Col(BD_EMP)}:{Col(BD_EMP)}";
            string codeCol = $"{Col(BD_ICODE)}:{Col(BD_ICODE)}";
            string sumBs(string salCode) => $"SUMIFS({bsCol},{empCol},{aEmp},{codeCol},\"{salCode}\")";

            // ── ریلِ پرداخت: TOTAL_NOMINAL_BASE / DAYSB / OT_HOUR_BASE ──
            // (اسمی → fallback رسمی)
            string nomBase = hasBothSal ? sumBs("BASE_SAL") : $"({sumBs("BASE_SAL")}+{sumBs("BASE_SAL_B")})";
            ws.Cell(r, BD_HOURLY).FormulaA1 = $"IF({V}=0,0,ROUND({nomBase}/{V}/CFG_OT_HOUR_BASE,2))";

            // ── ریلِ بیمه: TOTAL_OFFICIAL_BASE / DAYS / OT_HOUR_BASE ──
            // (رسمی → fallback اسمی)
            string offBase = hasBothSal ? sumBs("BASE_SAL_B") : $"({sumBs("BASE_SAL")}+{sumBs("BASE_SAL_B")})";
            ws.Cell(r, BD_HOURLY_OFF).FormulaA1 = $"IF({Vd}=0,0,ROUND({offBase}/{Vd}/CFG_OT_HOUR_BASE,2))";

            ws.Cell(r, BD_EARN).Value = 1;
            ws.Cell(r, BD_INSF).Value = def.INS_SUBJECT ? 1 : 0;
            ws.Cell(r, BD_TAXF).Value = def.TAX_SUBJECT ? 1 : 0;

            string Qn = A(BD_HOURLY, r), Qo = A(BD_HOURLY_OFF, r);
            string H = A(BD_HOURS, r), Ml = A(BD_MULT, r);
            ws.Cell(r, BD_AMT).FormulaA1 = $"TRUNC({Qn}*{H}*{Ml})";
            ws.Cell(r, BD_INSAMT).FormulaA1 = $"TRUNC({Qo}*{H}*{Ml})";
            ws.Cell(r, BD_DESC).FormulaA1 =
                $"\"= نرخ ساعتیِ مؤثر \"&TEXT({Qn},\"#,##0\")&\" × ساعت \"&TEXT({H},\"#,##0\")&\" × ضریب \"&TEXT({Ml},\"0.00\")&\" = \"&TEXT({A(BD_AMT, r)},\"#,##0\")";

            addedItemIds.Add(def.ITEM_ID);
            codesPresent.Add(code);
            return r + 1;
        }

        private static void WriteLiteralRow(IXLWorksheet ws, int r, Pay2AuditEmpRow e, int itemId,
            string code, string name, byte type, long value, bool ins, bool tax, string source)
        {
            ws.Cell(r, BD_EMP).Value = e.EMP_ID;
            ws.Cell(r, BD_CODE).Value = e.EMP_CODE;
            ws.Cell(r, BD_NAME).Value = e.FULL_NAME;
            ws.Cell(r, BD_ICODE).Value = code;
            ws.Cell(r, BD_INAME).Value = name;
            ws.Cell(r, BD_SRC).Value = source;
            ws.Cell(r, BD_TYPE).Value = type;
            ws.Cell(r, BD_RATE).Value = value;

            bool earn = (type == 1 || type == 2) && !code.Equals("BASE_SAL_B", StringComparison.OrdinalIgnoreCase);
            ws.Cell(r, BD_EARN).Value = earn ? 1 : 0;
            ws.Cell(r, BD_INSF).Value = (ins && (type == 1 || type == 2)) ? 1 : 0;
            ws.Cell(r, BD_TAXF).Value = (tax && (type == 1 || type == 2)) ? 1 : 0;

            ws.Cell(r, BD_AMT).FormulaA1 = A(BD_RATE, r);
            ws.Cell(r, BD_INSAMT).FormulaA1 = A(BD_RATE, r);
            ws.Cell(r, BD_DESC).FormulaA1 =
                $"\"مقدار ({source}) = \"&TEXT({A(BD_AMT, r)},\"#,##0\")";
        }

        private static void AddComment(IXLWorksheet ws, int row, int col, string text)
        {
            var cmt = ws.Cell(row, col).GetComment();
            cmt.AddText(text);
            cmt.Style.Size.SetWidth(28);
            cmt.Style.Size.SetHeight(2);
        }

        // ════════════════════════════ شیت فیش حقوقی ════════════════════════════
        private static void BuildSlipSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_SLIP);
            ws.RightToLeft = true;

            int nItems = d.Columns.Count;
            int baseCol = 4 + nItems;
            int cGross = baseCol + 1;
            int cInsBaseRaw = baseCol + 2;
            int cEffCeil = baseCol + 3;
            int cInsBase = baseCol + 4;
            int cInsWorker = baseCol + 5;
            int cTaxBaseRaw = baseCol + 6;
            int cTaxBase = baseCol + 7;
            int cTax = baseCol + 8;
            int cLoan = baseCol + 9;
            int cAdv = baseCol + 10;
            int cKasr = baseCol + 11;
            int cTotDed = baseCol + 12;
            int cNet = baseCol + 13;

            // سرستون‌ها
            ws.Cell(1, 1).Value = "EMP_ID";
            ws.Cell(1, 2).Value = "کد پرسنلی";
            ws.Cell(1, 3).Value = "نام";
            ws.Cell(1, 4).Value = "کارکرد رسمی";
            for (int i = 0; i < nItems; i++) ws.Cell(1, 5 + i).Value = d.Columns[i].ITEM_NAME;
            ws.Cell(1, cGross).Value = "ناخالص";
            ws.Cell(1, cInsBaseRaw).Value = "مبنای بیمه (خام)";
            ws.Cell(1, cEffCeil).Value = "سقف بیمه مؤثر";
            ws.Cell(1, cInsBase).Value = "مبنای بیمه";
            ws.Cell(1, cInsWorker).Value = "بیمه کارگر";
            ws.Cell(1, cTaxBaseRaw).Value = "مبنای مالیات (خام)";
            ws.Cell(1, cTaxBase).Value = "مبنای مالیات";
            ws.Cell(1, cTax).Value = "مالیات";
            ws.Cell(1, cLoan).Value = "قسط وام";
            ws.Cell(1, cAdv).Value = "مساعده";
            ws.Cell(1, cKasr).Value = "سایر کسورات";
            ws.Cell(1, cTotDed).Value = "جمع کسورات";
            ws.Cell(1, cNet).Value = "خالص پرداختی";
            var slipHead = ws.Range(1, 1, 1, cNet);
            slipHead.Style.Font.Bold = true;
            slipHead.Style.Fill.BackgroundColor = HeaderFill;

            string BD = SH_BD;
            string bdAmt = $"'{BD}'!{Col(BD_AMT)}:{Col(BD_AMT)}";
            string bdInsAmt = $"'{BD}'!{Col(BD_INSAMT)}:{Col(BD_INSAMT)}";
            string bdEmp = $"'{BD}'!{Col(BD_EMP)}:{Col(BD_EMP)}";
            string bdCode = $"'{BD}'!{Col(BD_ICODE)}:{Col(BD_ICODE)}";
            string bdEarn = $"'{BD}'!{Col(BD_EARN)}:{Col(BD_EARN)}";
            string bdInsF = $"'{BD}'!{Col(BD_INSF)}:{Col(BD_INSF)}";
            string bdTaxF = $"'{BD}'!{Col(BD_TAXF)}:{Col(BD_TAXF)}";

            int r = 2;
            foreach (var e in d.Employees)
            {
                int rr = r; // ردیف متناظر در شیت کارکرد خام (هم‌ترتیب)
                string emp = A(1, r);
                string rawDaysNominal = $"'{SH_RAW}'!{Col(5)}{rr}";
                string rawDaysb = $"'{SH_RAW}'!{Col(6)}{rr}";
                string insType = $"'{SH_RAW}'!{Col(16)}{rr}";
                string taxExempt = $"'{SH_RAW}'!{Col(17)}{rr}";
                string regionDep = $"'{SH_RAW}'!{Col(20)}{rr}";
                string loanRaw = $"'{SH_RAW}'!{Col(21)}{rr}";
                string advRaw = $"'{SH_RAW}'!{Col(22)}{rr}";
                string kasrRaw = $"'{SH_RAW}'!{Col(15)}{rr}";

                ws.Cell(r, 1).Value = e.EMP_ID;
                ws.Cell(r, 2).Value = e.EMP_CODE;
                ws.Cell(r, 3).Value = e.FULL_NAME;
                ws.Cell(r, 4).FormulaA1 = rawDaysb;

                // ستون‌های پویای آیتم
                for (int i = 0; i < nItems; i++)
                {
                    string code = d.Columns[i].ITEM_CODE;
                    int col = 5 + i;
                    ws.Cell(r, col).FormulaA1 = code.ToUpperInvariant() switch
                    {
                        "INS_DED" => A(cInsWorker, r),
                        "TAX_DED" => A(cTax, r),
                        "LOAN_DED" => A(cLoan, r),
                        "ADVANCE_DED" => A(cAdv, r),
                        _ => $"SUMIFS({bdAmt},{bdEmp},{emp},{bdCode},\"{code}\")"
                    };
                }

                // مبنای بیمه خام / سقف مؤثر / مبنای بیمه / بیمه کارگر
                ws.Cell(r, cInsBaseRaw).FormulaA1 = $"SUMIFS({bdInsAmt},{bdEmp},{emp},{bdInsF},1)";
                ws.Cell(r, cEffCeil).FormulaA1 = $"TRUNC(CFG_INS_CEILING/30*IF({rawDaysb}>0,{rawDaysb},{rawDaysNominal}))";
                ws.Cell(r, cInsBase).FormulaA1 =
                    $"IF({insType}=3,0,IF(CFG_INS_CEILING_APPLY=1,MIN({A(cInsBaseRaw, r)},{A(cEffCeil, r)}),{A(cInsBaseRaw, r)}))";
                ws.Cell(r, cInsWorker).FormulaA1 = $"IF({insType}=3,0,TRUNC({A(cInsBase, r)}*CFG_INS_WORKER_PCT/100))";

                // مبنای مالیات — از INS_AMOUNT (ریلِ رسمی)
                ws.Cell(r, cTaxBaseRaw).FormulaA1 = $"SUMIFS({bdInsAmt},{bdEmp},{emp},{bdTaxF},1)";
                string tbRaw = A(cTaxBaseRaw, r), insW = A(cInsWorker, r);
                string sAfterIns = $"IF(CFG_TAX_DEDUCT_INS=1,{tbRaw}-{insW},{tbRaw})";
                string sExempt = $"IF(({sAfterIns})>CFG_TAX_EXEMPT,({sAfterIns})-CFG_TAX_EXEMPT,0)";
                string sDep = $"IF(AND(CFG_TAX_DEP_APPLY=1,{regionDep}>0),TRUNC(({sExempt})*(1-{regionDep}/100)),({sExempt}))";
                ws.Cell(r, cTaxBase).FormulaA1 = $"IF({taxExempt}=1,0,{sDep})";

                // مالیات پلکانی داینامیک
                string annual = GenerateTaxAnnualFormula($"({A(cTaxBase, r)}*12)", d);
                ws.Cell(r, cTax).FormulaA1 = $"IF({taxExempt}=1,0,MAX(TRUNC(({annual})/12),0))";

                // کسورات
                ws.Cell(r, cLoan).FormulaA1 = loanRaw;
                ws.Cell(r, cAdv).FormulaA1 = advRaw;
                ws.Cell(r, cKasr).FormulaA1 = kasrRaw;
                ws.Cell(r, cTotDed).FormulaA1 =
                    $"{A(cInsWorker, r)}+{A(cTax, r)}+{A(cLoan, r)}+{A(cAdv, r)}+{A(cKasr, r)}";

                // ── خالص و ناخالصِ تعدیل‌شده (منطبق با SP: GROSS_PAY += ROUNDING_DIFF) ──
                // خالص = گرد( (ناخالصِ خام − جمعِ کسورات) ÷ واحد گرد) × واحد گرد
                string rawGrossExpr = $"SUMIFS({bdAmt},{bdEmp},{emp},{bdEarn},1)";
                string rawNet = $"({rawGrossExpr})-{A(cTotDed, r)}";
                ws.Cell(r, cNet).FormulaA1 =
                    $"IF(CFG_ROUND_MODE>1,ROUND(({rawNet})/CFG_ROUND_MODE,0)*CFG_ROUND_MODE,{rawNet})";
                // ناخالصِ نهایی = خالص + جمعِ کسورات (= ناخالصِ خام + اختلافِ گردکردن)
                ws.Cell(r, cGross).FormulaA1 = $"{A(cNet, r)}+{A(cTotDed, r)}";

                r++;
            }

            int lastRow = Math.Max(2, r - 1);
            ws.Range(2, 5, lastRow, cNet).Style.NumberFormat.Format = NUM_FMT;
            ws.Columns(1, cNet).AdjustToContents();

            AddComment(ws, 1, cGross, "ناخالصِ تعدیل‌شده = خالص + جمعِ کسورات (شاملِ اختلافِ گردکردن، منطبق با SP).");
            AddComment(ws, 1, cInsBase, "مبنای بیمه پس از اعمالِ سقف (در صورت فعال بودن).");
            AddComment(ws, 1, cInsWorker, "بیمهٔ سهم کارگر = مبنای بیمه × درصدِ سهم کارگر.");
            AddComment(ws, 1, cTaxBase, "مبنای مالیات = (مشمولِ مالیات − بیمهٔ کارگر − معافیت) با اعمالِ منطقهٔ محروم.");
            AddComment(ws, 1, cTax, "مالیاتِ تصاعدی طبق شیت «پله‌های مالیات» (سالانه ÷ ۱۲).");
            AddComment(ws, 1, cNet, "خالص = گرد((ناخالصِ خام − جمعِ کسورات) ÷ واحد) × واحد.");

            slipHead.SetAutoFilter();
            ws.SheetView.FreezeRows(1);
            ws.SheetView.FreezeColumns(3);
        }

        // مالیاتِ سالانه به‌صورت IF تودرتوی داینامیک از روی جدول پله‌ها (بدون LAMBDA)
        private static string GenerateTaxAnnualFormula(string aExpr, Pay2ExcelAuditData d)
        {
            var brackets = d.TaxBrackets.OrderBy(x => x.SORT_ORDER).ToList();
            int n = brackets.Count;
            if (n == 0) return "0";

            string BracketTax(int i)
            {
                int row = i + 2;
                string upper = $"'{SH_TAX}'!$B${row}";
                string rate = $"'{SH_TAX}'!$C${row}";
                string fixedTax = $"'{SH_TAX}'!$D${row}";
                string prev = i == 0 ? "0" : $"'{SH_TAX}'!$B${row - 1}";
                return $"({fixedTax}+TRUNC(({aExpr}-{prev})*{rate}/100))";
            }

            string expr = BracketTax(n - 1);
            for (int i = n - 2; i >= 0; i--)
            {
                string upper = $"'{SH_TAX}'!$B${i + 2}";
                expr = $"IF({aExpr}<={upper},{BracketTax(i)},{expr})";
            }
            return expr;
        }

        // ════════════════════════════ شیت کنترل تطابق ════════════════════════════
        private static void BuildReconciliationSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_REC);
            ws.RightToLeft = true;

            int nItems = d.Columns.Count;
            int baseCol = 4 + nItems;
            int slipGross = baseCol + 1;
            int slipInsWorker = baseCol + 5;
            int slipTax = baseCol + 8;
            int slipTotDed = baseCol + 12;
            int slipNet = baseCol + 13;

            string[] metricTitles = { "ناخالص", "بیمه کارگر", "مالیات", "جمع کسورات", "خالص" };
            int[] slipCols = { slipGross, slipInsWorker, slipTax, slipTotDed, slipNet };

            ws.Cell(1, 1).Value = "EMP_ID";
            ws.Cell(1, 2).Value = "نام";
            int c = 3;
            foreach (var m in metricTitles)
            {
                ws.Cell(1, c).Value = $"{m} (فرمول)";
                ws.Cell(1, c + 1).Value = $"{m} (موتور)";
                ws.Cell(1, c + 2).Value = $"{m} (اختلاف)";
                ws.Cell(1, c + 3).Value = $"{m} (وضعیت)";
                c += 4;
            }
            ws.Range(1, 1, 1, c - 1).Style.Font.Bold = true;

            var resByEmp = d.Results.ToDictionary(x => x.EMP_ID, x => x);

            int r = 2;
            foreach (var e in d.Employees)
            {
                int slipRow = r; // هم‌ترتیب با شیت فیش
                resByEmp.TryGetValue(e.EMP_ID, out var res);

                ws.Cell(r, 1).Value = e.EMP_ID;
                ws.Cell(r, 2).Value = e.FULL_NAME;

                long[] engine =
                {
                    res?.GROSS_PAY ?? 0, res?.INS_WORKER ?? 0, res?.TAX_AMOUNT ?? 0,
                    res?.TOTAL_DED ?? 0, res?.NET_PAY ?? 0
                };

                int col = 3;
                for (int m = 0; m < metricTitles.Length; m++)
                {
                    string slipCell = $"'{SH_SLIP}'!{Col(slipCols[m])}{slipRow}";
                    ws.Cell(r, col).FormulaA1 = slipCell;
                    ws.Cell(r, col + 1).Value = engine[m];
                    ws.Cell(r, col + 2).FormulaA1 = $"{A(col, r)}-{A(col + 1, r)}";
                    ws.Cell(r, col + 3).FormulaA1 = $"IF(ABS({A(col + 2, r)})<10,\"OK\",\"اختلاف\")";
                    ws.Cell(r, col).Style.NumberFormat.Format = NUM_FMT;
                    ws.Cell(r, col + 1).Style.NumberFormat.Format = NUM_FMT;
                    ws.Cell(r, col + 2).Style.NumberFormat.Format = NUM_FMT;
                    col += 4;
                }
                r++;
            }

            int lastRow = Math.Max(2, r - 1);
            // رنگ‌آمیزی شرطی وضعیت‌ها
            for (int m = 0; m < metricTitles.Length; m++)
            {
                int statusCol = 3 + m * 4 + 3;
                var rng = ws.Range(2, statusCol, lastRow, statusCol);
                rng.AddConditionalFormat().WhenEquals("OK").Fill.SetBackgroundColor(XLColor.LightGreen);
                rng.AddConditionalFormat().WhenEquals("اختلاف").Fill.SetBackgroundColor(XLColor.LightSalmon);
            }

            ws.Columns(1, c - 1).AdjustToContents();
            ws.SheetView.FreezeRows(1);
        }

        // ════════════════════════════ شیت راهنما ════════════════════════════
        private static void BuildHelpSheet(XLWorkbook wb, Pay2ExcelAuditData d)
        {
            var ws = wb.Worksheets.Add(SH_HELP);
            ws.RightToLeft = true;
            int r = 1;

            void Title(string t)
            {
                ws.Cell(r, 1).Value = t;
                var rng = ws.Range(r, 1, r, 4);
                rng.Merge();
                rng.Style.Font.Bold = true;
                rng.Style.Font.FontSize = 12;
                rng.Style.Fill.BackgroundColor = HeaderFill;
                r++;
            }
            void Line(string t)
            {
                ws.Cell(r, 1).Value = t;
                ws.Range(r, 1, r, 4).Merge();
                r++;
            }
            void Kv(string k, string v)
            {
                ws.Cell(r, 1).Value = k;
                ws.Cell(r, 2).Value = v;
                r++;
            }

            Title("راهنمای فیش حقوقیِ فرمول‌دار");
            Line("در این فایل «چگونگیِ محاسبه» شفاف است: هر عددِ فیش یک فرمولِ واقعیِ اکسل است؛ روی هر سلول کلیک کنید تا فرمول را ببینید.");
            r++;

            Title("ترتیب و کاربردِ شیت‌ها");
            Line("• فیش تفصیلی: برای هر پرسنل، آیتم‌به‌آیتم «نرخ × روز × ضریب = مبلغ» و سپس گام‌های کسورات تا خالص.");
            Line("• فیش حقوقی: جدولِ همهٔ پرسنل؛ هر سلولِ عددی یک فرمولِ زنده است.");
            Line("• محاسبات ریز: ریزِ هر خطِ حکم/آیتم به‌همراه ستونِ «شرح فرمول» (توضیحِ خواناى فرمول).");
            Line("• کارکرد خام / تنظیمات / پله‌های مالیات: ورودی‌های محاسبه (نرخ‌ها، روزها، ضرایب، پله‌ها).");
            Line("• کنترل تطابق: اختلافِ خروجیِ فرمولِ اکسل با موتورِ حقوق؛ باید ۰ و سبز باشد.");
            r++;

            Title("زنجیرهٔ محاسبه (به‌ترتیب)");
            Line("۱) مبلغِ هر آیتم = نرخ × روز × ضریبِ تناسب (ضریب = روزهای فعالِ حکم ÷ طول ماه).");
            Line("۲) ناخالص = جمعِ همهٔ مزایا.");
            Line("۳) مبنای بیمه = جمعِ آیتم‌های مشمولِ بیمه (در صورت فعال بودن، محدود به سقف).");
            Line("۴) بیمهٔ کارگر = مبنای بیمه × درصدِ سهم کارگر.");
            Line("۵) مبنای مالیات = (جمعِ مشمولِ مالیات − بیمهٔ کارگر − معافیتِ ماهانه) با اعمالِ منطقهٔ محروم.");
            Line("۶) مالیات = طبق پله‌های مالیاتِ سالانه (تصاعدی) و سپس ÷ ۱۲.");
            Line("۷) خالص = (ناخالص − جمعِ کسورات) با گردکردن طبق واحدِ تنظیم.");
            r++;

            Title($"ضرایب و تنظیمات (لحظهٔ این اجرا — دورهٔ {d.PERIOD_TITLE})");
            Kv("طول ماه (روز)", d.MONTH_DAYS.ToString(CultureInfo.InvariantCulture));
            Kv("ضریب اضافه‌کار عادی", CfgDec(d, "OT_NORMAL_MULT", 1.40m).ToString(CultureInfo.InvariantCulture));
            Kv("ضریب اضافه‌کار تعطیل", CfgDec(d, "OT_HOLIDAY_MULT", 1.40m).ToString(CultureInfo.InvariantCulture));
            Kv("مبنای ساعتِ روزانه", CfgDec(d, "OT_HOUR_BASE", 7.33m).ToString(CultureInfo.InvariantCulture));
            Kv("درصد بیمهٔ سهم کارگر", CfgDec(d, "INS_WORKER_RATE", 7m).ToString(CultureInfo.InvariantCulture) + "٪");
            Kv("اعمالِ سقفِ بیمه؟", CfgInt(d, "INS_CEILING_APPLY", 1) == 1 ? "بله" : "خیر");
            Kv("سقفِ ماهانهٔ بیمه", CfgLong(d, "INS_CEILING_MONTHLY", 999999999).ToString("#,##0", CultureInfo.InvariantCulture));
            Kv("معافیتِ ماهانهٔ مالیات", CfgLong(d, "TAX_EXEMPT_MONTHLY", 0).ToString("#,##0", CultureInfo.InvariantCulture));
            Kv("کسرِ بیمه از مبنای مالیات؟", CfgInt(d, "TAX_DEDUCT_INS", 1) == 1 ? "بله" : "خیر");
            Kv("واحدِ گردکردنِ خالص", CfgInt(d, "ROUND_MODE", 1).ToString(CultureInfo.InvariantCulture));
            Kv("سال مالیاتی", d.TAX_YEAR.ToString(CultureInfo.InvariantCulture));
            r++;

            Line("نکته: اختلافِ چند ریالیِ ناشی از تفاوتِ گردکردنِ اکسل و موتور طبیعی است و در شیتِ «کنترل تطابق» شفاف نمایش داده می‌شود.");

            ws.Column(1).Width = 40;
            ws.Column(2).Width = 28;
            ws.Columns(3, 4).Width = 20;
        }

        // ════════════════════════════ شیت فیش تفصیلی ════════════════════════════
        private static void BuildDetailedSlipSheet(XLWorkbook wb, Pay2ExcelAuditData d, Dictionary<int, List<int>> bdRows)
        {
            var ws = wb.Worksheets.Add(SH_DETAIL);
            ws.RightToLeft = true;

            int nItems = d.Columns.Count;
            int baseCol = 4 + nItems;
            int cGross = baseCol + 1;
            int cInsWorker = baseCol + 5;
            int cTax = baseCol + 8;
            int cLoan = baseCol + 9;
            int cAdv = baseCol + 10;
            int cKasr = baseCol + 11;
            int cTotDed = baseCol + 12;
            int cNet = baseCol + 13;

            int r = 1;
            int idx = 0;
            foreach (var e in d.Employees)
            {
                int slipRow = 2 + idx; // هم‌ترتیب با شیت فیش
                idx++;

                // عنوانِ پرسنل
                ws.Cell(r, 1).Value = $"فیش حقوقی: {e.FULL_NAME}  (کد {e.EMP_CODE})";
                var trng = ws.Range(r, 1, r, 4);
                trng.Merge();
                trng.Style.Font.Bold = true;
                trng.Style.Font.FontColor = XLColor.White;
                trng.Style.Fill.BackgroundColor = TitleFill;
                r++;

                // سرستونِ آیتم‌ها
                ws.Cell(r, 1).Value = "آیتم";
                ws.Cell(r, 2).Value = "منبع";
                ws.Cell(r, 3).Value = "شرح محاسبه";
                ws.Cell(r, 4).Value = "مبلغ";
                var hrng = ws.Range(r, 1, r, 4);
                hrng.Style.Font.Bold = true;
                hrng.Style.Fill.BackgroundColor = HeaderFill;
                r++;

                if (bdRows.TryGetValue(e.EMP_ID, out var rows))
                {
                    foreach (var br in rows)
                    {
                        ws.Cell(r, 1).FormulaA1 = $"'{SH_BD}'!{A(BD_INAME, br)}";
                        ws.Cell(r, 2).FormulaA1 = $"'{SH_BD}'!{A(BD_SRC, br)}";
                        ws.Cell(r, 3).FormulaA1 = $"'{SH_BD}'!{A(BD_DESC, br)}";
                        ws.Cell(r, 4).FormulaA1 = $"'{SH_BD}'!{A(BD_AMT, br)}";
                        ws.Cell(r, 4).Style.NumberFormat.Format = NUM_FMT;
                        r++;
                    }
                }

                void Sub(string label, int slipCol, bool bold)
                {
                    ws.Cell(r, 1).Value = label;
                    ws.Range(r, 1, r, 3).Merge();
                    ws.Cell(r, 4).FormulaA1 = $"'{SH_SLIP}'!{A(slipCol, slipRow)}";
                    ws.Cell(r, 4).Style.NumberFormat.Format = NUM_FMT;
                    if (bold)
                    {
                        ws.Cell(r, 1).Style.Font.Bold = true;
                        ws.Cell(r, 4).Style.Font.Bold = true;
                    }
                    r++;
                }

                Sub("جمعِ ناخالص", cGross, true);

                ws.Cell(r, 1).Value = "—— کسورات ——";
                var drng = ws.Range(r, 1, r, 4);
                drng.Merge();
                drng.Style.Font.Italic = true;
                r++;

                Sub("بیمهٔ کارگر (سهم کارگر)", cInsWorker, false);
                Sub("مالیات (طبق پله‌ها)", cTax, false);
                Sub("قسط وام", cLoan, false);
                Sub("مساعده", cAdv, false);
                Sub("سایر کسورات", cKasr, false);
                Sub("جمعِ کسورات", cTotDed, true);
                Sub("خالصِ پرداختی", cNet, true);

                r += 2; // فاصله بین پرسنل‌ها
            }

            ws.Column(1).Width = 26;
            ws.Column(2).Width = 16;
            ws.Column(3).Width = 62;
            ws.Column(4).Width = 18;
        }

        // ════════════════════════════ کمکی‌ها ════════════════════════════
        private static string Col(int col) => XLHelper.GetColumnLetterFromNumber(col);
        private static string A(int col, int row) => $"{Col(col)}{row}";

        private static decimal CfgDec(Pay2ExcelAuditData d, string key, decimal def)
        {
            var v = d.Config.FirstOrDefault(x => string.Equals(x.CFG_KEY, key, StringComparison.OrdinalIgnoreCase))?.CFG_VALUE;
            return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : def;
        }

        private static long CfgLong(Pay2ExcelAuditData d, string key, long def)
        {
            var v = d.Config.FirstOrDefault(x => string.Equals(x.CFG_KEY, key, StringComparison.OrdinalIgnoreCase))?.CFG_VALUE;
            return long.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : def;
        }

        private static int CfgInt(Pay2ExcelAuditData d, string key, int def)
        {
            var v = d.Config.FirstOrDefault(x => string.Equals(x.CFG_KEY, key, StringComparison.OrdinalIgnoreCase))?.CFG_VALUE;
            return int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : def;
        }
    }
}