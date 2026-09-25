using Dapper;
using Microsoft.Data.SqlClient;
using Safir.Server.Security;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using System.Globalization;
using System.Text.Json;

namespace Safir.Server.Ai
{
    // ═══════════════════ ابزارهای ثابتِ مالی ═══════════════════
    //
    // ── چرا ──
    // در آزمون پایه، هر جا مدل خودش SQL نوشت عدد عوض می‌شد: مانده‌ی بانک در
    // سه اجرا سه عدد شد (۵٬۶۴۷ / ۳٬۹۶۳ / ۳۲۱٬۷۴۷ میلیون) چون هر بار یک فیلترِ
    // OKF دیگر گذاشت. اینجا تعریف یک بار نوشته شده و مدل فقط ابزار و بازه را
    // انتخاب می‌کند؛ جمع، رتبه و درصد را سرور حساب می‌کند.
    //
    // ── منطق از کجاست ──
    // هر ابزار عیناً منطقِ خودِ Safir را تکرار می‌کند، نه تعریفِ تازه:
    //   فروش کالا  = گام S12 بستن ماه (KALAS، TAGCODE 2 منهای 4، ستون KHFR)
    //   سود ماه    = CC_sp_FinancialStatements روی آخرین اجرای کامل‌شده
    //   مانده‌ها    = جمع BED−BES بدون فیلتر OKF، مثل گزارش بدهکاران Safir/WPF
    // هر خروجی «تعریف» و «منبع» خودش را همراه دارد تا مدل آن را به کاربر بگوید.

    public static class FinArgs
    {
        /// <summary>
        /// تاریخ شمسیِ yyyymmdd. مدل گاهی عدد می‌فرستد، گاهی رشته، گاهی با
        /// ارقام فارسی یا «/»؛ همه به یک شکل درمی‌آیند.
        /// </summary>
        public static long? Date(AiToolCall call, string name)
        {
            if (call.Args.ValueKind != JsonValueKind.Object ||
                !call.Args.TryGetProperty(name, out var v)) return null;

            var raw = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var digits = new string(raw.Select(c =>
                c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) :
                c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c)
                .Where(char.IsDigit).ToArray());

            if (digits.Length != 8 || !long.TryParse(digits, out var d)) return null;

            int y = (int)(d / 10000), m = (int)(d / 100 % 100), day = (int)(d % 100);
            if (m is < 1 or > 12 || day < 1) return null;
            try { if (day > new PersianCalendar().GetDaysInMonth(y, m)) return null; }
            catch { return null; }
            return d;
        }

        public static long Today()
        {
            var pc = new PersianCalendar();
            var n  = DateTime.Now;
            return pc.GetYear(n) * 10000L + pc.GetMonth(n) * 100 + pc.GetDayOfMonth(n);
        }

        public static decimal? Pct(decimal now, decimal before)
            => before == 0 ? null : Math.Round((now - before) / Math.Abs(before) * 100m, 2);
    }

    /// <summary>فروش کالا — عیناً فرمول S12.</summary>
    public sealed class SalesByProductTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public SalesByProductTool(IDatabaseService db) => _db = db;

        public string Name        => "sales_by_product";
        public string Title       => "فروش کالاها";
        public string Description =>
            "فروش خالص یک بازه (همان فرمول بستن ماه در Safir: فاکتور فروش منهای برگشت از فروش، " +
            "مبلغ پس از تخفیف ردیف) با جمع کل و پرفروش‌ترین کالاها. برای «فروش ماه»، " +
            "«پرفروش‌ترین کالا» و «فروش کالای X» همین را صدا بزن، نه run_sql. " +
            "برای کالایی که کاربر با نام گفته name بده، نه code: چند کالا ممکن است نام مشابه داشته باشند " +
            "(مثلاً ماده‌ی اولیه و محصولِ هم‌نام) و این ابزار همه را با فروش هرکدام برمی‌گرداند؛ " +
            "اگر بیش از یکی بود، همه را به کاربر بگو.";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  =>
            "from، to: تاریخ yyyymmdd (الزامی). top: تعداد کالا (پیش‌فرض ۱۰). " +
            "by: amount (مبلغ، پیش‌فرض) یا qty (مقدار). name: بخشی از نام کالا (اختیاری). code: فقط یک کد (اختیاری).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var from = FinArgs.Date(call, "from");
            var to   = FinArgs.Date(call, "to");
            if (from is null || to is null || from > to)
                return AiToolResult.Fail("from و to باید تاریخ شمسی yyyymmdd باشند و from ≤ to.");

            int top   = Math.Clamp(call.Int("top", 10), 1, Math.Min(call.MaxRows, 100));
            bool byQty = string.Equals(call.Str("by"), "qty", StringComparison.OrdinalIgnoreCase);
            var code  = call.Str("code");
            var name  = call.Str("name")?.Trim();
            if (string.IsNullOrEmpty(name)) name = null;

            // نام‌ها گاهی دو فاصله دارند («Uپودر  شیر خشک»)؛ هر دو طرف یک‌فاصله می‌شوند.
            // در آزمون طلایی، مدل کالای هم‌نامِ مواد اولیه (۱۷۳۲) را برداشت و با اطمینان
            // گفت «فروش صفر»، در حالی که محصولِ فروخته‌شده کد ۳۳۶۵ بود.
            string? like = name is null ? null : "%" + AiText.NormalizeFa(name) + "%";

            string baseCte = @"
                WITH s AS (
                    SELECT  k.CODE,
                            SUM(CASE WHEN k.TAGCODE = 2 THEN k.KHFR  ELSE -k.KHFR  END) AS NetSales,
                            SUM(CASE WHEN k.TAGCODE = 2 THEN k.MEGHk ELSE -k.MEGHk END) AS NetQty
                    FROM    dbo.KALAS k
                    WHERE   k.TAGCODE IN (2, 4)
                      AND   k.DATE_N BETWEEN @from AND @to
                      AND   (@code IS NULL OR k.CODE = TRY_CAST(@code AS BIGINT))
                      AND   (@like IS NULL OR EXISTS (
                               SELECT 1 FROM dbo.STUF_DEF n
                               WHERE  TRY_CAST(n.CODE AS BIGINT) = k.CODE
                                 AND  " + AiText.SqlFa("n.NAME") + @" LIKE @like))
                    GROUP BY k.CODE)";

            var total = (await _db.DoGetDataSQLAsync<dynamic>(baseCte + @"
                SELECT COUNT(*) AS Products, SUM(NetSales) AS NetSales, SUM(NetQty) AS NetQty FROM s",
                new { from, to, code, like })).FirstOrDefault();

            var items = (await _db.DoGetDataSQLAsync<dynamic>(baseCte + $@"
                SELECT TOP (@top) s.CODE AS Code, d.NAME AS Name, s.NetSales, s.NetQty
                FROM   s LEFT JOIN dbo.STUF_DEF d ON TRY_CAST(d.CODE AS BIGINT) = s.CODE  -- KALAS.code عددی است، STUF_DEF.CODE متنی
                ORDER BY {(byQty ? "s.NetQty" : "s.NetSales")} DESC",
                new { from, to, code, like, top })).ToList();

            // کالاهای هم‌نامی که در این بازه اصلاً فروش نداشته‌اند هم دیده شوند، وگرنه
            // «فروش صفر» و «کالای دیگر» از هم تشخیص داده نمی‌شوند.
            var noSales = like is null ? new List<dynamic>() : (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (20) TRY_CAST(n.CODE AS BIGINT) AS Code, n.NAME AS Name
                FROM   dbo.STUF_DEF n
                WHERE  " + AiText.SqlFa("n.NAME") + @" LIKE @like
                  AND  NOT EXISTS (SELECT 1 FROM dbo.KALAS k
                                   WHERE k.CODE = TRY_CAST(n.CODE AS BIGINT) AND k.TAGCODE IN (2, 4)
                                     AND k.DATE_N BETWEEN @from AND @to)
                ORDER BY n.NAME", new { from, to, like })).ToList();

            return new AiToolResult
            {
                Rows = items.Count,
                Data = new
                {
                    Metric     = "فروش خالص کالا",
                    Period     = new { From = from, To = to },
                    Definition = "فاکتور فروش منهای برگشت از فروش؛ مبلغ پس از تخفیف ردیف (KHFR)، به ریال، بدون مالیات بر ارزش افزوده",
                    Source     = "همان فرمول گام S12 بستن ماه در Safir",
                    RankedBy   = byQty ? "مقدار" : "مبلغ",
                    Total      = total,
                    Top        = items,
                    MatchedWithoutSales = noSales
                }
            };
        }
    }

    /// <summary>مقایسه‌ی فروش دو بازه؛ اختلاف و درصد را سرور حساب می‌کند.</summary>
    public sealed class CompareSalesTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public CompareSalesTool(IDatabaseService db) => _db = db;

        public string Name        => "compare_sales";
        public string Title       => "مقایسه‌ی فروش دو دوره";
        public string Description =>
            "فروش خالص دو بازه را با همان فرمول sales_by_product مقایسه می‌کند و اختلاف ریالی و " +
            "درصد تغییر را برمی‌گرداند. درصد را خودت حساب نکن.";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "fromA، toA: دوره‌ی اول (قبلی). fromB، toB: دوره‌ی دوم (جدید). همه yyyymmdd.";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var a1 = FinArgs.Date(call, "fromA"); var a2 = FinArgs.Date(call, "toA");
            var b1 = FinArgs.Date(call, "fromB"); var b2 = FinArgs.Date(call, "toB");
            if (a1 is null || a2 is null || b1 is null || b2 is null || a1 > a2 || b1 > b2)
                return AiToolResult.Fail("هر چهار تاریخ لازم است (yyyymmdd) و ابتدای هر دوره ≤ انتهای آن.");

            const string sql = @"
                SELECT ISNULL(SUM(CASE WHEN TAGCODE = 2 THEN KHFR ELSE -KHFR END), 0)
                FROM   dbo.KALAS
                WHERE  TAGCODE IN (2, 4) AND DATE_N BETWEEN @f AND @t";

            var a = Convert.ToDecimal((await _db.DoGetDataSQLAsync<double>(sql, new { f = a1, t = a2 })).First());
            var b = Convert.ToDecimal((await _db.DoGetDataSQLAsync<double>(sql, new { f = b1, t = b2 })).First());

            return new AiToolResult
            {
                Rows = 2,
                Data = new
                {
                    Metric      = "فروش خالص",
                    Definition  = "فاکتور فروش منهای برگشت از فروش، مبلغ پس از تخفیف ردیف (فرمول S12 Safir)، به ریال",
                    PeriodA     = new { From = a1, To = a2, NetSales = Math.Round(a) },
                    PeriodB     = new { From = b1, To = b2, NetSales = Math.Round(b) },
                    Change      = Math.Round(b - a),
                    ChangePct   = FinArgs.Pct(b, a)
                }
            };
        }
    }

    /// <summary>
    /// سود و زیان یک ماه — فقط از اجرای کامل‌شده‌ی بستن ماه.
    /// ماهی که بسته نشده عدد سود ندارد؛ این ابزار به‌جای عدد، وضعیت را می‌گوید.
    /// </summary>
    public sealed class ProfitAndLossTool : IAiTool
    {
        private readonly IDatabaseService _db;
        private readonly string _cs;
        public ProfitAndLossTool(IDatabaseService db, IConnectionStringProvider cs)
        {
            _db = db;
            _cs = cs.GetConnectionString();
        }

        public string Name        => "profit_and_loss";
        public string Title       => "سود و زیان ماه";
        public string Description =>
            "صورت سود و زیان Safir برای یک ماه (فروش خالص، بهای تمام‌شده، سود ناخالص، هزینه‌های " +
            "دوره، سود عملیاتی). فقط اگر بستن بهای تمام‌شده‌ی آن ماه کامل شده باشد عدد می‌دهد؛ " +
            "وگرنه Available=false و دلیلش را برمی‌گرداند — آن‌وقت عدد سود نگو. " +
            "برای «سود ماه» همین را صدا بزن و در جواب بگو کدام سود (ناخالص/عملیاتی) است.";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "month: ۱ تا ۱۲ (الزامی). year: سال مالی (اختیاری؛ پیش‌فرض سال این پایگاه).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            int month = call.Int("month");
            if (month is < 1 or > 12) return AiToolResult.Fail("month باید ۱ تا ۱۲ باشد.");
            int year = call.Int("year");
            if (year == 0)
                year = (await _db.DoGetDataSQLAsync<int?>("SELECT TOP (1) CAST(YEA AS INT) FROM dbo.SAZMAN")).FirstOrDefault() ?? 0;

            var run = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (1) r.RunId, r.Status, r.RunKind, r.DateFrom, r.DateTo,
                       (SELECT COUNT(*) FROM dbo.CC_ItemMargin m WHERE m.RunId = r.RunId) AS MarginRows
                FROM   dbo.CC_Run r
                WHERE  r.FiscalYear = @year AND r.PeriodMonth = @month AND r.IsLatest = 1
                ORDER BY r.RunId DESC", new { year, month })).FirstOrDefault();

            string? why =
                run is null                   ? "برای این ماه هیچ اجرای بستن بهای تمام‌شده‌ای ثبت نشده است." :
                (int)run.Status == 1          ? "بستن بهای تمام‌شده‌ی این ماه در حال اجراست." :
                (int)run.Status == 2          ? "بستن بهای تمام‌شده‌ی این ماه متوقف و ناتمام مانده است." :
                (int)run.Status == 4          ? "بستن بهای تمام‌شده‌ی این ماه با خطا متوقف شده است." :
                (int)run.Status != 3          ? "اجرای این ماه کامل‌شده نیست." :
                (int)run.MarginRows == 0      ? "اجرای این ماه کامل شده ولی محاسبه‌ی سود کالاها (گام S12) خروجی ندارد." :
                null;

            if (why is not null)
                return new AiToolResult
                {
                    Rows = 0,
                    Data = new { Available = false, Year = year, Month = month, Reason = why,
                                 Hint = "تا وقتی ماه بسته نشده، سود قابل اتکایی وجود ندارد؛ عدد نساز." }
                };

            int runId = (int)run!.RunId;
            // اتصالِ خودش: DoGetDataSQLAsyncMultiple اتصال را باز رها می‌کند
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);
            using var grid = await conn.QueryMultipleAsync(
                "EXEC dbo.CC_sp_FinancialStatements @RunId = @runId", new { runId }, commandTimeout: 60);

            await grid.ReadAsync();                                 // ۱) بهای تمام‌شده‌ی ساخت
            await grid.ReadAsync();                                 // ۲) بهای فروش‌رفته
            var pl       = (await grid.ReadAsync()).ToList();       // ۳) سود و زیان
            var expenses = (await grid.ReadAsync()).ToList();       // ۴) سرفصل‌های هزینه

            return new AiToolResult
            {
                Rows = pl.Count,
                Data = new
                {
                    Available   = true,
                    Year        = year,
                    Month       = month,
                    Period      = new { From = (long)run.DateFrom, To = (long)run.DateTo },
                    RunId       = runId,
                    RunKind     = (int)run.RunKind == 2 ? "قطعی" : "آزمایشی",
                    Source      = "صورت سود و زیان Safir (CC_sp_FinancialStatements)",
                    Note        = "«سود عملیاتی» = سود ناخالص منهای هزینه‌های دوره‌ای که در تنظیمات صورت‌های مالی تعریف شده‌اند. " +
                                  "اگر اجرا آزمایشی است، این را به کاربر بگو.",
                    Statement   = pl,
                    ExpenseAccounts = expenses
                }
            };
        }
    }

    /// <summary>مانده‌ی بانک‌ها — مانده‌ی دفتری حساب کل ۱۱۲.</summary>
    public sealed class BankBalancesTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public BankBalancesTool(IDatabaseService db) => _db = db;

        public string Name        => "bank_balances";
        public string Title       => "مانده‌ی بانک‌ها";
        public string Description =>
            "مانده‌ی دفتری هر حساب بانکی (حساب کل ۱۱۲) و جمع کل تا یک تاریخ. این «مانده‌ی دفتری» " +
            "است، نه صورت‌حساب بانک — همین را به کاربر بگو. برای مانده‌ی بانک run_sql نزن.";
        public string RequiredForm => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "asOf: تاریخ yyyymmdd (اختیاری؛ پیش‌فرض امروز).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var asOf = FinArgs.Date(call, "asOf") ?? FinArgs.Today();

            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT  d.HES_M AS Moin, d.HES_T AS Tafsili, t.NAME AS Name,
                        SUM(ISNULL(d.BED, 0) - ISNULL(d.BES, 0)) AS Balance
                FROM    dbo.DEED_DTL d
                JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
                LEFT JOIN dbo.TDETA_HES t ON t.N_KOL = d.HES_K AND t.NUMBER = d.HES_M AND t.TNUMBER = d.HES_T
                WHERE   d.HES_K = 112 AND h.DATE_S <= @asOf
                GROUP BY d.HES_M, d.HES_T, t.NAME
                HAVING  SUM(ISNULL(d.BED, 0) - ISNULL(d.BES, 0)) <> 0
                ORDER BY SUM(ISNULL(d.BED, 0) - ISNULL(d.BES, 0)) DESC", new { asOf })).ToList();

            decimal total = rows.Sum(r => Convert.ToDecimal((double)r.Balance));

            return new AiToolResult
            {
                Rows = rows.Count,
                Data = new
                {
                    Metric     = "مانده‌ی دفتری بانک‌ها",
                    AsOf       = asOf,
                    Definition = "جمع بدهکار منهای بستانکار حساب کل ۱۱۲ تا این تاریخ، شامل سند افتتاحیه و همه‌ی اسناد " +
                                 "(بدون فیلتر وضعیت تأیید OKF، مثل گزارش‌های Safir و WPF). مثبت = موجودی، منفی = بستانکار.",
                    Total      = Math.Round(total),
                    Accounts   = rows
                }
            };
        }
    }

    /// <summary>
    /// بدهکاران — مانده‌ی بدهکار هر حسابِ شخص زیر کل ۱۱۵.
    ///
    /// «یک مشتری» = کد کامل حساب (DEED_DTL.HES، مثلاً 115-1-25-3-4-6)، عیناً مثل
    /// صورت‌حساب مشتری در Safir (QDAFTARTAFZIL2_H: DEED_DTL.HES = @HES) و نام از CUST_HESAB.
    /// نسخه‌ی قبلی در سطح تفصیلی ۱ جمع می‌زد و حساب‌های گروهی مثل «مشتریان متفرقه‌ی
    /// دفتر یزد» را یک مشتری نشان می‌داد، در حالی که چند شخص واقعی زیرش بودند.
    /// </summary>
    public sealed class TopDebtorsTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public TopDebtorsTool(IDatabaseService db) => _db = db;

        public string Name        => "top_debtors";
        public string Title       => "بدهکاران";
        public string Description =>
            "مشتریانی که بیشترین مانده‌ی بدهکار را دارند (بدهکاران تجاری، حساب کل ۱۱۵، هر کد حساب کامل یک " +
            "مشتری — همان صورت‌حساب مشتری در Safir) با جمع کل. مانده‌ی دفتری است؛ چک‌های وصول‌نشده جدا حساب نمی‌شوند.";
        public string RequiredForm => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  =>
            "asOf: تاریخ yyyymmdd (اختیاری؛ پیش‌فرض امروز). top: تعداد (پیش‌فرض ۱۰). " +
            "name: بخشی از نام مشتری (اختیاری) — برای «بدهی فلانی چقدر است»؛ همه‌ی حساب‌های هم‌نام برمی‌گردد.";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var asOf = FinArgs.Date(call, "asOf") ?? FinArgs.Today();
            int top  = Math.Clamp(call.Int("top", 10), 1, Math.Min(call.MaxRows, 100));
            var name = call.Str("name")?.Trim();
            // نام حساب‌ها ۳۰٪ «ي/ك» عربی دارند؛ هر دو طرف یکسان می‌شوند
            string? like = string.IsNullOrEmpty(name) ? null : "%" + AiText.NormalizeFa(name) + "%";

            // ── دو مرحله‌ی جدا، عمداً ──
            // اگر جستجوی نام داخل همان کوئریِ دفتر بنشیند، SQL Server آن را برای هر
            // ردیفِ دفتر دوباره اجرا می‌کند: روی کپی مشتری ۸۰ ثانیه طول کشید و
            // دستیار با timeout گفت «پیدا نشد». جدا: ۱۶ms برای کدها + ۰ms برای مانده.
            List<string>? codes = null;
            if (like is not null)
            {
                codes = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT TOP (50) n.hes FROM (" + AccountNamesSql + ") n WHERE " +
                    AiText.SqlFa("n.NAME") + " LIKE @like", new { like })).ToList();

                if (codes.Count == 0)
                    return new AiToolResult
                    {
                        Rows = 0,
                        Data = new { Metric = "بدهکاران تجاری", Found = false, Name = name,
                                     Message = "حسابی با این نام زیر حساب کل ۱۱۵ پیدا نشد." }
                    };
            }

            const string cte = @"
                WITH b AS (
                    SELECT  d.HES, SUM(ISNULL(d.BED, 0) - ISNULL(d.BES, 0)) AS Balance
                    FROM    dbo.DEED_DTL d
                    JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
                    WHERE   d.HES_K = 115 AND h.DATE_S <= @asOf {0}
                    GROUP BY d.HES
                    HAVING  {1})";
            var sql = codes is null
                ? string.Format(cte, "", "SUM(ISNULL(d.BED, 0) - ISNULL(d.BES, 0)) > 0")
                : string.Format(cte, "AND d.HES IN @codes", "1 = 1");

            var total = (await _db.DoGetDataSQLAsync<dynamic>(sql +
                " SELECT COUNT(*) AS Debtors, SUM(Balance) AS TotalDebt FROM b", new { asOf, codes })).FirstOrDefault();

            var rows = (await _db.DoGetDataSQLAsync<dynamic>(sql + @"
                SELECT TOP (@top) b.HES AS Account, b.Balance FROM b ORDER BY b.Balance DESC",
                new { asOf, top, codes })).ToList();

            // نام‌ها جدا و فقط برای همین چند کد (view CUST_HESAB روی همه‌ی ستون‌ها UNION
            // یکتاساز دارد و کند است؛ همان کدِ ترکیبی را مستقیم از جدول‌ها می‌سازیم)
            var accounts = rows.Select(r => (string)r.Account).ToList();
            var names = accounts.Count == 0 ? new Dictionary<string, string>() :
                (await _db.DoGetDataSQLAsync<(string hes, string NAME)>(
                    "SELECT n.hes, n.NAME FROM (" + AccountNamesSql + ") n WHERE n.hes IN @accounts",
                    new { accounts }))
                .GroupBy(x => x.hes).ToDictionary(g => g.Key, g => g.First().NAME);

            var top10 = rows.Select(r => new
            {
                Account = (string)r.Account,
                Name    = names.TryGetValue((string)r.Account, out var n) ? n : null,
                Balance = (double)r.Balance
            }).ToList();

            return new AiToolResult
            {
                Rows = top10.Count,
                Data = new
                {
                    Metric     = "بدهکاران تجاری",
                    AsOf       = asOf,
                    Definition = "مانده‌ی بدهکار (بدهکار منهای بستانکار) هر حساب شخص (کد کامل) زیر حساب کل ۱۱۵، " +
                                 "همه‌ی اسناد بدون فیلتر تأیید. بدون name فقط مانده‌های مثبت؛ با name همه‌ی حساب‌های هم‌نام " +
                                 "(مانده‌ی منفی یعنی بستانکار). چک‌های وصول‌نشده کم نشده‌اند.",
                    Total      = total,
                    Top        = top10
                }
            };
        }

        /// <summary>
        /// کد ترکیبی و نامِ حساب‌های شخصِ زیر کل ۱۱۵ در هر چهار سطح تفصیلی — همان
        /// شکلِ کدِ view CUST_HESAB («115-19-1-3»)، بدون ستون‌های دیگرش.
        /// </summary>
        private const string AccountNamesSql = @"
            SELECT CONCAT(N_KOL,'-',NUMBER,'-',TNUMBER) AS hes, NAME FROM dbo.TDETA_HES WHERE N_KOL = 115
            UNION ALL
            SELECT CONCAT(N_KOL,'-',NUMBER,'-',TNUMBER,'-',TNUMBER2), NAME FROM dbo.TDETA_HES2 WHERE N_KOL = 115
            UNION ALL
            SELECT CONCAT(N_KOL,'-',NUMBER,'-',TNUMBER,'-',TNUMBER2,'-',TNUMBER3), NAME FROM dbo.TDETA_HES3 WHERE N_KOL = 115
            UNION ALL
            SELECT CONCAT(N_KOL,'-',NUMBER,'-',TNUMBER,'-',TNUMBER2,'-',TNUMBER3,'-',TNUMBER4), NAME FROM dbo.TDETA_HES4 WHERE N_KOL = 115";
    }

    /// <summary>عبور از سقف اعتبار — AZAE.TOPETEB در برابر مانده‌ی دفتری همان حساب.</summary>
    public sealed class CreditLimitTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public CreditLimitTool(IDatabaseService db) => _db = db;

        public string Name        => "credit_limit_breaches";
        public string Title       => "عبور از سقف اعتبار";
        public string Description =>
            "مشتریانی که مانده‌ی بدهکارشان از سقف اعتبار تعریف‌شده بیشتر است، به‌علاوه‌ی تعداد مشتریانِ " +
            "دارای سقف و تعدادی که اصلاً گردش ندارند.";
        public string RequiredForm => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "asOf: تاریخ yyyymmdd (اختیاری؛ پیش‌فرض امروز).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var asOf = FinArgs.Date(call, "asOf") ?? FinArgs.Today();

            // سقف روی کد ترکیبی حساب است (مثلاً 115-1-802)؛ مانده‌ی دقیقاً همان کد،
            // مثل صورت‌حساب مشتری در Safir (DEED_DTL.HES = کد).
            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT  RTRIM(a.HES) AS Account, c.NAME AS Name, a.TOPETEB AS CreditLimit, x.Balance
                FROM    dbo.AZAE a
                LEFT JOIN dbo.CUST_HESAB c ON c.hes = RTRIM(a.HES)
                OUTER APPLY (
                    SELECT SUM(ISNULL(d.BED, 0) - ISNULL(d.BES, 0)) AS Balance
                    FROM   dbo.DEED_DTL d
                    JOIN   dbo.DEED_HED h ON h.N_S = d.N_S
                    WHERE  h.DATE_S <= @asOf
                      AND  d.HES = RTRIM(a.HES)) x
                WHERE   a.TOPETEB > 0", new { asOf })).ToList();

            var over = rows.Where(r => r.Balance is not null && (double)r.Balance > (long)r.CreditLimit)
                           .OrderByDescending(r => (double)r.Balance - (long)r.CreditLimit)
                           .Select(r => new
                           {
                               Account = (string)r.Account, Name = (string?)r.Name,
                               CreditLimit = (long)r.CreditLimit, Balance = (double)r.Balance,
                               Excess = (double)r.Balance - (long)r.CreditLimit
                           }).ToList();

            return new AiToolResult
            {
                Rows = over.Count,
                Data = new
                {
                    Metric          = "عبور از سقف اعتبار",
                    AsOf            = asOf,
                    Definition      = "مانده‌ی دفتری حساب مشتری (همه‌ی اسناد) در برابر سقف اعتبار ثبت‌شده برای همان حساب.",
                    CustomersWithLimit = rows.Count,
                    WithoutAnyActivity = rows.Count(r => r.Balance is null),
                    OverLimitCount  = over.Count,
                    OverLimit       = over
                }
            };
        }
    }

    /// <summary>کالاهایی که در یک بازه فروش نداشته‌اند.</summary>
    public sealed class UnsoldItemsTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public UnsoldItemsTool(IDatabaseService db) => _db = db;

        public string Name        => "unsold_items";
        public string Title       => "کالاهای بدون فروش";
        public string Description =>
            "تعداد و فهرست کالاهای تعریف‌شده‌ای که در بازه‌ی داده‌شده هیچ فاکتور فروشی نداشته‌اند. " +
            "مواد اولیه و نیمه‌ساخته هم جزو کالاهای تعریف‌شده‌اند؛ این را به کاربر بگو.";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "from، to: تاریخ yyyymmdd (الزامی). top: تعداد نمونه در فهرست (پیش‌فرض ۲۰).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var from = FinArgs.Date(call, "from");
            var to   = FinArgs.Date(call, "to");
            if (from is null || to is null || from > to)
                return AiToolResult.Fail("from و to باید تاریخ شمسی yyyymmdd باشند و from ≤ to.");
            int top = Math.Clamp(call.Int("top", 20), 1, Math.Min(call.MaxRows, 200));

            const string cte = @"
                WITH sold AS (SELECT DISTINCT CODE FROM dbo.KALAS
                              WHERE TAGCODE = 2 AND DATE_N BETWEEN @from AND @to),
                     u AS (SELECT d.CODE, d.NAME FROM dbo.STUF_DEF d
                           WHERE NOT EXISTS (SELECT 1 FROM sold s WHERE s.CODE = TRY_CAST(d.CODE AS BIGINT)))";

            var counts = (await _db.DoGetDataSQLAsync<dynamic>(cte + @"
                SELECT (SELECT COUNT(*) FROM dbo.STUF_DEF) AS AllItems, (SELECT COUNT(*) FROM u) AS Unsold",
                new { from, to })).FirstOrDefault();

            var list = (await _db.DoGetDataSQLAsync<dynamic>(cte +
                " SELECT TOP (@top) CODE AS Code, NAME AS Name FROM u ORDER BY NAME",
                new { from, to, top })).ToList();

            return new AiToolResult
            {
                Rows      = list.Count,
                Truncated = counts is not null && (int)counts.Unsold > list.Count,
                Data      = new
                {
                    Metric     = "کالاهای بدون فروش",
                    Period     = new { From = from, To = to },
                    Definition = "کالای تعریف‌شده (STUF_DEF) که در این بازه هیچ ردیف فاکتور فروش نداشته است.",
                    Counts     = counts,
                    Sample     = list
                }
            };
        }
    }
}
