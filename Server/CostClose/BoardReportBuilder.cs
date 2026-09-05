using ClosedXML.Excel;
using Dapper;
using Safir.Shared.Interfaces;
using System.Data;
using System.Globalization;
using Safir.Shared.Utility;

namespace Safir.Server.CostClose
{
    /// <summary>
    /// سازنده گزارش اکسل هیئت‌مدیره.
    ///
    /// شیت‌های موجود گزارش فعلی، به‌علاوه دو شیت جدید که امروز
    /// وجود ندارند: «خلاصه اجرا» و «بیشترین تغییر نرخ».
    ///
    /// شیت دوم پاسخ سؤالی است که تا امروز قابل جواب نبود:
    /// «چرا قیمت تمام‌شده این کالا نسبت به ماه قبل عوض شد؟»
    /// </summary>
    public interface IBoardReportBuilder
    {
        /// <param name="unitId">
        /// اگر مشخص باشد، شیت «سود کالا به کالا» فقط همان واحد تولید را
        /// می‌آورد — تا خروجی اکسل با چیزی که کاربر روی صفحه انتخاب کرده
        /// یکی باشد. null یعنی گزارش کل، مثل قبل.
        /// </param>
        Task<byte[]> BuildAsync(int runId, int? unitId = null);
    }

    public sealed class BoardReportBuilder : IBoardReportBuilder
    {
        private readonly IDatabaseService _db;
        public BoardReportBuilder(IDatabaseService db) => _db = db;

        public async Task<byte[]> BuildAsync(int runId, int? unitId = null)
        {
            using var grid = await _db.DoGetDataSQLAsyncMultiple(
                "EXEC dbo.CC_sp_S13_ReportData @RunId=@r", new { r = runId });

            var margins = (await grid.ReadAsync()).ToList();
            var summary = (await grid.ReadAsync()).ToList();
            var conv    = (await grid.ReadAsync()).ToList();
            var changes = (await grid.ReadAsync()).ToList();

            // سرجمع هر واحد — همیشه می‌آید، حتی وقتی فیلتری در کار نیست،
            // چون خودِ همین تفکیک چیزی است که در گزارش کل دیده نمی‌شود.
            var unitSummary = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT  ISNULL(cu.UnitName, N'بدون واحد')             AS [واحد],
                        COUNT(*)                                      AS [تعداد کالا],
                        SUM(CASE WHEN u.Profit < 0 THEN 1 ELSE 0 END) AS [زیان‌ده],
                        SUM(u.SalesAmount)                            AS [فروش],
                        SUM(u.CostAmount)                             AS [بهای تمام‌شده],
                        SUM(u.Profit)                                 AS [سود],
                        -- درصد سود نسبت به فروش. NULLIF جلوی تقسیم بر صفر را
                        -- می‌گیرد؛ واحدی که فروش صفر دارد سلول خالی می‌گیرد،
                        -- نه صفرِ گمراه‌کننده.
                        -- ستون خالی می‌ماند و در اکسل با فرمول پر می‌شود
                        -- (نگاه کنید ProfitPctFormula). دو فایده دارد:
                        -- کاربر می‌تواند عدد فروش/سود را دستی عوض کند و درصد
                        -- خودش به‌روز شود، و سرریزِ عددی هم موضوعیت ندارد —
                        -- همان چیزی که با CAST(... AS DECIMAL(9,1)) کلِ
                        -- خروجی را می‌ترکاند (خرداد ۱۴۰۵، کد ۹۳ «پنیر
                        -- موزارلا ۲۰۰۰ گرمی جم»: ۲ عدد فروش به مبلغ ۲ ریال
                        -- با بهای ۲۴٬۷۲۳٬۸۱۱ → −۱٬۲۳۶٬۱۹۰٬۴۵۰٪).
                        CAST(NULL AS FLOAT)                           AS [درصد سود]
                FROM    dbo.CC_ItemMarginUnit u
                LEFT    JOIN dbo.CC_Unit cu ON cu.UnitId = u.UnitId
                WHERE   u.RunId = @runId
                GROUP BY u.UnitId, cu.UnitName
                ORDER BY SUM(u.Profit) DESC", new { runId })).ToList();

            var marginSheetName = "سود کالا به کالا";

            // ── چیدمان ستون‌های شیت سود ──
            //
            // ترتیب طبق خواسته‌ی صاحب پروژه:
            //   • «بهای فروش» بلافاصله کنار «فروش خالص»
            //   • «بهای تمام‌شده» (واقعی، از کاردکس) درست پیش از
            //     «قیمت تمام‌شده استاندارد» (از فرمول)
            //   • تفکیک مواد/دستمزد/سربارِ همان بهای استاندارد
            //   • «قیمت تمام‌شده استاندارد» آخرین ستون، رنگی
            //   • «درصد سود» فرمولِ اکسل، نه عددِ ازپیش‌محاسبه‌شده
            //
            // عمداً یک چیدمانِ واحد برای هر دو حالت (کل و تفکیک واحد):
            // قبلاً گزارشِ کل از نتیجه‌ی اولِ CC_sp_S13_ReportData می‌آمد و
            // ستون‌هایش با شیتِ واحد فرق داشت، پس مقایسه‌ی آن دو سخت بود.
            // اینجا هر دو از یک الگو ساخته می‌شوند و رویه دست‌نخورده
            // می‌ماند (۱۸-margin-report-approve همیشه باید همراه
            // ۱۹-margin-fix-kalas مستقر شود، پس دست‌نزدن به آن امن‌تر است).
            //
            // CC_ItemCost خروجی مستقیم S11 است و برای هر کد دقیقاً یک سطر
            // دارد (روی اجرای ۸ بررسی شد: ۲۳۷ سطر برای ۲۳۷ کد)، پس LEFT
            // JOIN ساده کافی است و ردیف تکراری نمی‌سازد.
            const string marginCols = @"
                        m.Code                AS [کد],
                        s.NAME                AS [کالا],
                        m.QtySold             AS [مقدار فروش],
                        m.GrossSales          AS [فروش ناخالص],
                        m.Discount            AS [تخفیف],
                        m.ReturnAmount        AS [برگشت],
                        m.SalesAmount         AS [فروش خالص],
                        m.CostAmount          AS [بهای فروش],
                        m.Profit              AS [سود],
                        CAST(NULL AS FLOAT)   AS [درصد سود],
                        m.UnitPrice           AS [قیمت واحد],
                        m.UnitCost            AS [بهای تمام‌شده],
                        -- تفکیک مواد/دستمزد/سربار مستقیماً از خودِ فرمولِ
                        -- همین ماه (HEAD_MANF/DTL_MANF) خوانده می‌شود، نه از
                        -- CC_ItemCost.
                        --
                        -- ⚠️ چرا نه CC_ItemCost: آنجا وقتی بهای کالا از
                        -- میانگین انبار می‌آید (کالای نیمه‌ساخته)، S11 عمداً
                        -- Wage/Oh را صفر می‌گذارد چون همان لحظه‌ی تولید
                        -- داخلِ MABL_K حساب شده‌اند و جدا نوشتنشان
                        -- دوباره‌شماری می‌شد (گام ۵-ج در
                        -- 15-rate-engine-production.sql، مغایرت حساب ۷۷۱).
                        -- برای *محاسبه* درست است، ولی برای *گزارش* یعنی
                        -- ستون دستمزد روی ۱۹۹ کالا از ۳۰۲ صفر می‌شد —
                        -- درحالی‌که فرمولشان دستمزد دارد (کد ۴۵ «روغن گاوی
                        -- فله»، مرداد ۱۴۰۵: فرمول ۲۹۱٬۴۶۴ ریال دستمزد دارد
                        -- ولی CC_ItemCost صفر نشان می‌داد).
                        --
                        -- این سه ستون «استانداردِ فرمول»اند، نه بهای واقعی؛
                        -- بهای واقعی همان ستون «بهای تمام‌شده» است.
                        std.Mat               AS [مواد],
                        std.Wage              AS [دستمزد],
                        std.Oh                AS [سربار],
                        CAST(NULL AS FLOAT)   AS [قیمت تمام‌شده استاندارد]";

            // وزن‌دهی عیناً مثل خودِ S11 (گام ۵-ج): وقتی یک کالا چند فرمول
            // در یک ماه دارد، میانگینِ موزون بر مقدارِ واقعیِ تولیدشده گرفته
            // می‌شود؛ بدون هیچ تولیدی، میانگین ساده. هر فرمول دیگری اینجا
            // عددی متفاوت از موتور می‌داد و گزارش با محاسبه نمی‌خواند.
            const string formulaStdCte = @"
                ;WITH Prod AS (
                    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB, SUM(pl.MEGHK) AS Qty
                    FROM    dbo.HEAD_LST h
                    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @dt1 AND @dt2
                      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
                    GROUP BY TRY_CAST(pl.N_KOL AS INT)
                ),
                Fx AS (
                    SELECT  TRY_CAST(hm.CODE AS BIGINT) AS Code,
                            hm.IMBIBE_MANF AS Wage,
                            hm.IMBIBE_SAR  AS Oh,
                            ISNULL((SELECT SUM(d.MABLK) FROM dbo.DTL_MANF d
                                    WHERE d.FNUMB = hm.FNUMB), 0) AS Mat,
                            ISNULL(p.Qty, 0) AS Qty
                    FROM    dbo.HEAD_MANF hm
                    LEFT    JOIN Prod p ON p.FNUMB = hm.FNUMB
                    WHERE   CAST(hm.GHEYMAT AS INT) = @month
                ),
                Std AS (
                    SELECT  Code,
                            CASE WHEN SUM(Qty) > 0 THEN SUM(Mat  * Qty) / SUM(Qty) ELSE AVG(Mat)  END AS Mat,
                            CASE WHEN SUM(Qty) > 0 THEN SUM(Wage * Qty) / SUM(Qty) ELSE AVG(Wage) END AS Wage,
                            CASE WHEN SUM(Qty) > 0 THEN SUM(Oh   * Qty) / SUM(Qty) ELSE AVG(Oh)   END AS Oh
                    FROM    Fx GROUP BY Code
                )";

            var period = await _db.DoGetDataSQLAsyncSingle<PeriodRow>(
                "SELECT PeriodMonth AS [Month], DateFrom AS Dt1, DateTo AS Dt2 " +
                "FROM dbo.CC_Run WHERE RunId = @runId", new { runId });

            if (unitId is null)
            {
                margins = (await _db.DoGetDataSQLAsync<dynamic>($@"
                    {formulaStdCte}
                    SELECT {marginCols}
                    FROM    dbo.CC_ItemMargin m
                    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = m.Code
                    LEFT    JOIN Std std        ON std.Code = m.Code
                    WHERE   m.RunId = @runId
                    ORDER BY m.Profit",
                    new { runId, month = period.Month, dt1 = period.Dt1, dt2 = period.Dt2 })).ToList();
            }
            else
            {
                margins = (await _db.DoGetDataSQLAsync<dynamic>($@"
                    {formulaStdCte}
                    SELECT {marginCols}
                    FROM    dbo.CC_ItemMarginUnit m
                    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = m.Code
                    LEFT    JOIN Std std        ON std.Code = m.Code
                    WHERE   m.RunId = @runId AND m.UnitId = @unitId
                    ORDER BY m.Profit",
                    new { runId, unitId, month = period.Month, dt1 = period.Dt1, dt2 = period.Dt2 })).ToList();

                var name = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT UnitName FROM dbo.CC_Unit WHERE UnitId = @unitId",
                    new { unitId })).FirstOrDefault();

                // نام شیت اکسل حداکثر ۳۱ کاراکتر و بدون چند نویسه‌ی خاص است
                marginSheetName = Trim31($"سود کالا — {name ?? $"واحد {unitId}"}");
            }

            using var wb = new XLWorkbook();

            // فونت پیش‌فرض کل کارپوشه — شیت‌ها هم جداگانه ست می‌شوند تا
            // سلول‌هایی که استایل صریح می‌گیرند (سرستون‌ها) هم پوشش داشته باشند.
            wb.Style.Font.FontName = ReportFont;

            AddSheet(wb, marginSheetName, margins, freezeTop: true,
                     formulas: new()
                     {
                         ["درصد سود"] = ProfitPctFormula("سود", "فروش خالص"),
                         // جمعِ سه ستونِ استاندارد، نه یک عددِ جدا: این‌طور
                         // چهار ستون همیشه با هم می‌خوانند و اگر کاربر یکی
                         // را دستی عوض کند، جمع هم به‌روز می‌شود. SUM هم
                         // سلولِ خالی را نادیده می‌گیرد.
                         ["قیمت تمام‌شده استاندارد"] =
                             "=SUM([مواد]{r}:[سربار]{r})"
                     },
                     highlight: StandardCostColumns);

            AddSheet(wb, "سود به تفکیک واحد", unitSummary,
                     formulas: new() { ["درصد سود"] = ProfitPctFormula("سود", "فروش") });

            // ── صورت‌های مالی ──
            // سه صورت در یک شیت پشت سر هم، نه سه شیت جدا: کسی که صورت مالی
            // می‌خواند هر سه را با هم می‌بیند و بین شیت‌ها بالا‌پایین نمی‌رود.
            using (var fin = await _db.DoGetDataSQLAsyncMultiple(
                       "EXEC dbo.CC_sp_FinancialStatements @RunId=@r", new { r = runId }))
            {
                var cogm = (await fin.ReadAsync()).ToList();
                var cogs = (await fin.ReadAsync()).ToList();
                var inc  = (await fin.ReadAsync()).ToList();
                var exp  = (await fin.ReadAsync()).ToList();

                AddStatementSheet(wb, "صورت‌های مالی", new[]
                {
                    ("صورت بهای تمام‌شده کالای ساخته‌شده", cogm),
                    ("صورت بهای تمام‌شده کالای فروش‌رفته", cogs),
                    ("صورت سود و زیان",                    inc)
                });

                AddSheet(wb, "سرفصل‌های هزینه", exp);
            }
            AddSheet(wb, "هزینه تبدیل",      conv);
            AddSheet(wb, "بیشترین تغییر نرخ", changes, freezeTop: true);
            AddSheet(wb, "خلاصه اجرا",       summary);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        /// <summary>نام شیت اکسل حداکثر ۳۱ نویسه است؛ بلندتر باشد ClosedXML خطا می‌دهد.</summary>
        private static string Trim31(string s)
            => s.Length <= 31 ? s : s.Substring(0, 31);

        /// <summary>
        /// فونت گزارش‌های اکسل. همان فونتی که کل برنامه استفاده می‌کند
        /// (Client/wwwroot/FNT و Server/Fonts) تا خروجی با محیط یکدست باشد.
        ///
        /// نکته: اکسل فونت را جاسازی نمی‌کند، فقط با نام ارجاع می‌دهد. روی
        /// دستگاهی که IRANYekanFN نصب نباشد، اکسل به فونت پیش‌فرض برمی‌گردد
        /// و فایل همچنان سالم باز می‌شود.
        /// </summary>
        private const string ReportFont = "IRANYekanFN";

        /// <summary>دورهٔ اجرا — برای خواندن فرمول‌های همان ماه و تولیدش.</summary>
        private sealed class PeriodRow
        {
            public byte Month { get; set; }
            public long Dt1   { get; set; }
            public long Dt2   { get; set; }
        }

        /// <summary>
        /// ستون‌های بهای استاندارد (از فرمول) — با پس‌زمینه‌ی متمایز، تا از
        /// ستون‌های واقعیِ کاردکس جدا دیده شوند. آخرین ستون (خودِ بهای
        /// استاندارد) پررنگ‌تر است و سه تفکیکش کم‌رنگ‌تر.
        /// </summary>
        private static readonly string[] StandardCostColumns =
            { "مواد", "دستمزد", "سربار", "قیمت تمام‌شده استاندارد" };

        /// <summary>
        /// فرمول درصد سود برای اکسل. نام ستون‌ها داخل کروشه نوشته می‌شود و
        /// هنگام نوشتن به حرفِ ستون ترجمه می‌شود، تا جابه‌جا شدنِ ستون‌ها
        /// فرمول را نشکند. IFERROR جلوی تقسیم بر صفر را می‌گیرد.
        /// </summary>
        private static string ProfitPctFormula(string profitCol, string salesCol)
            => $"=IFERROR(ROUND([{profitCol}]{{r}}/[{salesCol}]{{r}}*100,1),\"\")";

        /// <summary>
        /// خواندن یک ستون از سطرِ dynamic، با تحملِ «ی»/«ک»ِ عربی در برابر
        /// فارسی. رویه‌های این ماژول ستون‌های فارسی برمی‌گردانند و کاراکترشان
        /// همیشه یکسان نیست (SQL روی نصب‌های قدیمی «ي» عربی می‌نویسد)، پس
        /// جستجوی خامِ کلید گاهی null می‌دهد بدون اینکه خطایی رخ دهد.
        /// </summary>
        private static object? Col(IDictionary<string, object> row, string name)
        {
            if (row.TryGetValue(name, out var direct)) return direct;

            var target = name.FixPersianChars();
            foreach (var kv in row)
                if (kv.Key.FixPersianChars() == target) return kv.Value;

            return null;
        }

        /// <summary>
        /// چند صورت مالی پشت سر هم در یک شیت، با قرارداد بصریِ خودشان:
        /// جمع جزء خط بالا، جمع نهایی خط دوتایی، و سطرِ اطلاعی/تطبیقی
        /// کم‌رنگ و مورب چون جزو جمع نیست.
        ///
        /// ستون «نوع» که رویه برمی‌گرداند فقط شکلِ نمایش را تعیین می‌کند و
        /// خودش در خروجی نوشته نمی‌شود — عددی است برای ماشین، نه برای خواننده.
        /// </summary>
        private static void AddStatementSheet(
            XLWorkbook wb, string name,
            IEnumerable<(string Title, List<dynamic> Lines)> statements)
        {
            var ws = wb.Worksheets.Add(name);
            ws.RightToLeft = true;
            ws.Style.Font.FontName = ReportFont;

            var r = 1;
            foreach (var (title, lines) in statements)
            {
                var head = ws.Cell(r, 1);
                head.Value = title;
                head.Style.Font.Bold = true;
                head.Style.Font.FontSize = 12;
                head.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E7FF");
                ws.Range(r, 1, r, 2).Merge();
                r++;

                foreach (var raw in lines)
                {
                    var d = (IDictionary<string, object>)raw;
                    var kind = Convert.ToByte(Col(d, "نوع") ?? (byte)0);

                    var cText = ws.Cell(r, 1);
                    var cVal  = ws.Cell(r, 2);

                    cText.Value = Col(d, "شرح")?.ToString() ?? "";

                    if (Col(d, "مبلغ") is { } amount)
                    {
                        cVal.Value = Convert.ToDecimal(amount);
                        cVal.Style.NumberFormat.Format = "#,##0;#,##0-";
                        if (Convert.ToDecimal(amount) < 0)
                            cVal.Style.Font.FontColor = XLColor.FromHtml("#B4342F");
                    }

                    switch (kind)
                    {
                        case 1:                       // جمع جزء
                            cText.Style.Font.Bold = true;
                            cVal.Style.Font.Bold = true;
                            ws.Range(r, 1, r, 2).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                            break;
                        case 2:                       // جمع نهایی
                            cText.Style.Font.Bold = true;
                            cVal.Style.Font.Bold = true;
                            ws.Range(r, 1, r, 2).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                            ws.Range(r, 1, r, 2).Style.Border.BottomBorder = XLBorderStyleValues.Double;
                            break;
                        case 3:                       // اطلاعی / تطبیق
                            cText.Style.Font.Italic = true;
                            cVal.Style.Font.Italic = true;
                            cText.Style.Font.FontColor = XLColor.FromHtml("#64748B");
                            cVal.Style.Font.FontColor = XLColor.FromHtml("#64748B");
                            break;
                    }
                    r++;
                }
                r += 2;                                // فاصله تا صورت بعدی
            }

            ws.Column(1).Width = 45;
            ws.Column(2).Width = 22;
        }

        private static void AddSheet(
            XLWorkbook wb, string name, List<dynamic> rows, bool freezeTop = false,
            Dictionary<string, string>? formulas = null,
            IReadOnlyCollection<string>? highlight = null)
        {
            var ws = wb.Worksheets.Add(name);
            ws.RightToLeft = true;
            ws.Style.Font.FontName = ReportFont;

            if (rows.Count == 0)
            {
                ws.Cell(1, 1).Value = "داده‌ای وجود ندارد";
                ws.Cell(1, 1).Style.Font.FontName = ReportFont;
                return;
            }

            var cols = ((IDictionary<string, object>)rows[0]).Keys.ToList();

            // ── ترجمه‌ی «[نام ستون]» به حرفِ ستون اکسل ──
            // بلندترین نام‌ها اول جایگزین می‌شوند، وگرنه «[سود]» داخل
            // «[درصد سود]» هم می‌افتد و فرمول خراب می‌شود.
            var byLength = cols.OrderByDescending(x => x.Length).ToList();
            string Resolve(string template, int excelRow)
            {
                var s = template;
                foreach (var col in byLength)
                    s = s.Replace($"[{col}]", XLHelper.GetColumnLetterFromNumber(cols.IndexOf(col) + 1));
                return s.Replace("{r}", excelRow.ToString(CultureInfo.InvariantCulture));
            }

            var highlightSet = highlight is null
                ? new HashSet<string>()
                : new HashSet<string>(highlight);

            // آخرین ستونِ فهرستِ تأکید، ستونِ اصلی است و پررنگ‌تر رنگ می‌گیرد
            var accentCol = highlight?.LastOrDefault();

            // سرستون‌ها
            for (int c = 0; c < cols.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = cols[c].Replace('_', ' ');
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = highlightSet.Contains(cols[c])
                    ? XLColor.FromHtml(cols[c] == accentCol ? "#FDE68A" : "#FEF3C7")
                    : XLColor.FromHtml("#F1F5F9");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // داده
            for (int r = 0; r < rows.Count; r++)
            {
                var dict = (IDictionary<string, object>)rows[r];

                for (int c = 0; c < cols.Count; c++)
                {
                    var v = dict[cols[c]];
                    var cell = ws.Cell(r + 2, c + 1);

                    // ستون‌های تأکیدشده پس‌زمینه‌ی ملایمِ خودشان را در همه‌ی
                    // سطرها نگه می‌دارند تا به‌عنوان یک بلوک خوانده شوند.
                    if (highlightSet.Contains(cols[c]))
                        cell.Style.Fill.BackgroundColor =
                            XLColor.FromHtml(cols[c] == accentCol ? "#FEF9C3" : "#FEFCE8");

                    // فرمول به‌جای مقدار: عدد در فایل ثابت نمی‌شود و اگر
                    // کاربر سلولی را دستی عوض کند، خودش به‌روز می‌شود.
                    if (formulas is not null && formulas.TryGetValue(cols[c], out var tpl))
                    {
                        cell.FormulaA1 = Resolve(tpl, r + 2);
                        cell.Style.NumberFormat.Format = "#,##0.0;#,##0.0-";
                        continue;
                    }

                    if (v is null) { cell.Value = ""; continue; }

                    switch (v)
                    {
                        case double or decimal or float:
                            var d = Convert.ToDecimal(v);
                            cell.Value = d;
                            cell.Style.NumberFormat.Format = "#,##0;#,##0-";
                            if (d < 0) cell.Style.Font.FontColor = XLColor.FromHtml("#B4342F");
                            break;

                        case int or long or short:
                            cell.Value = Convert.ToInt64(v);
                            cell.Style.NumberFormat.Format = "#,##0;#,##0-";
                            break;

                        case DateTime dt:
                            cell.Value = dt;
                            break;

                        default:
                            cell.Value = v.ToString();
                            break;
                    }
                }
            }

            ws.Columns().AdjustToContents(10.0, 45.0);
            ws.SheetView.FreezeRows(freezeTop ? 1 : 0);

            if (rows.Count > 1)
                ws.Range(1, 1, rows.Count + 1, cols.Count).SetAutoFilter();
        }
    }
}
