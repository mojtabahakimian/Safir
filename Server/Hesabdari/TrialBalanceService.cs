using Safir.Shared.Interfaces;
using Safir.Shared.Models.Hesabdari;

namespace Safir.Server.Hesabdari
{
    /// <summary>
    /// تراز آزمایشی چهارستونی (کل، معین، تفصیلی و تفصیلی ۲ تا ۴) — بازنویسیِ همان
    /// پنجره‌های WPF (TARAZ_4، TARAZ_4_MOIN، TARAZ_TAF_DIRECT … TARAZ_TAF4_DIRECT).
    ///
    /// عدد را خودمان حساب نمی‌کنیم: همان رویه‌های دیتابیس را با همان نام و همان
    /// پارامترها صدا می‌زنیم که WPF می‌زند. رویه‌ها در دیتابیسِ مشتری‌اند (هر دو
    /// برنامه به یک دیتابیس وصل‌اند)، پس عددِ سفیر و ویندوزی به‌ساخت یکی است؛
    /// هر بازنویسیِ دوباره‌ی آن SQL یعنی دو منطقِ تراز که دیر یا زود از هم جدا می‌شوند.
    /// </summary>
    public sealed class TrialBalanceService
    {
        private readonly IDatabaseService _db;
        public TrialBalanceService(IDatabaseService db) => _db = db;

        /// <summary>نام فرم‌ها در TFORMS — همان WANTEDFORM در CL_HESABDARI.</summary>
        public const string FormKol = "TARAZ_4";
        public const string FormMoin = "TARAZ_4_MOIN";
        public const string FormTafsili = "TARAZ_4_TAFZ";

        /// <summary>«همه‌ی اسناد» در WPF همیشه این بازه است.</summary>
        public const double AllSanadFrom = 0;
        public const double AllSanadTo = 929292929;

        public static string FormFor(TrialBalanceLevel level) => level switch
        {
            TrialBalanceLevel.Kol => FormKol,
            TrialBalanceLevel.Moin => FormMoin,
            _ => FormTafsili
        };

        public static string ProcedureFor(TrialBalanceLevel level) => level switch
        {
            TrialBalanceLevel.Kol => "dbo.TARAZ_4",
            TrialBalanceLevel.Moin => "dbo.TARAZ4_MOIN",
            TrialBalanceLevel.Tafsili => "dbo.TARAZ4_TAFZ_DIRECT",
            TrialBalanceLevel.Tafsili2 => "dbo.TARAZ4_TAFZ2_DIRECT",
            TrialBalanceLevel.Tafsili3 => "dbo.TARAZ4_TAFZ3_DIRECT",
            TrialBalanceLevel.Tafsili4 => "dbo.TARAZ4_TAFZ4_DIRECT",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

        /// <summary>
        /// پیام خطای فارسی اگر پرس‌وجو ناقص باشد؛ وگرنه null. هر سطح به همه‌ی
        /// شماره‌های بالای خودش نیاز دارد (معین به کل، تفصیلی ۲ به کل و معین و تفصیلی …).
        /// </summary>
        public static string? Validate(TrialBalanceQuery q)
        {
            if (!Enum.IsDefined(q.Level)) return "سطح تراز نامعتبر است.";
            if (q.From <= 0 || q.To <= 0) return "بازه‌ی تاریخ را کامل وارد کنید.";
            if (q.From > q.To) return "«از تاریخ» بعد از «تا تاریخ» است.";
            if (q.SanadFrom is { } sf && q.SanadTo is { } st && sf > st) return "«از سند» بزرگ‌تر از «تا سند» است.";
            if (q.Level >= TrialBalanceLevel.Moin && q.Kol is null) return "حساب کل مشخص نیست.";
            if (q.Level >= TrialBalanceLevel.Tafsili && q.Moin is null) return "حساب معین مشخص نیست.";
            if (q.Level >= TrialBalanceLevel.Tafsili2 && q.Tafsili is null) return "حساب تفصیلی مشخص نیست.";
            if (q.Level >= TrialBalanceLevel.Tafsili3 && q.Tafsili2 is null) return "حساب تفصیلی ۲ مشخص نیست.";
            if (q.Level >= TrialBalanceLevel.Tafsili4 && q.Tafsili3 is null) return "حساب تفصیلی ۳ مشخص نیست.";
            return null;
        }

        /// <summary>
        /// پارامترها دقیقاً با همان نام و نوعی که WPF می‌فرستد: تاریخ‌ها Int64،
        /// بازه‌ی سند double، و شماره‌ی حساب‌ها رشته.
        /// </summary>
        public static Dictionary<string, object?> BuildParameters(TrialBalanceQuery q)
        {
            var p = new Dictionary<string, object?>
            {
                ["Forms___FMENU_TARAZ_4___DT1"] = q.From,
                ["Forms___FMENU_TARAZ_4___DT2"] = q.To,
                ["Forms___FMENU_TARAZ_4___SNDNUM1"] = q.SanadFrom ?? AllSanadFrom,
                ["Forms___FMENU_TARAZ_4___SNDNUM2"] = q.SanadTo ?? AllSanadTo,
            };
            if (q.Level >= TrialBalanceLevel.Moin) p["KOL"] = q.Kol?.ToString();
            if (q.Level >= TrialBalanceLevel.Tafsili) p["MOIN"] = q.Moin?.ToString();
            if (q.Level >= TrialBalanceLevel.Tafsili2) p["TAF"] = q.Tafsili?.ToString();
            if (q.Level >= TrialBalanceLevel.Tafsili3) p["TAF2"] = q.Tafsili2?.ToString();
            if (q.Level >= TrialBalanceLevel.Tafsili4) p["TAF3"] = q.Tafsili3?.ToString();
            return p;
        }

        /// <summary>همه‌ی ستون‌هایی که شش رویه برمی‌گردانند؛ هر رویه فقط بخشی را دارد.</summary>
        public sealed class ProcRow
        {
            public int? N_KOL { get; set; }
            public int? NUMBER { get; set; }
            public int? TNUMBER { get; set; }
            public int? HES_T2 { get; set; }
            public int? TNUMBER3 { get; set; }
            public int? TNUMBER4 { get; set; }
            public string? NAME { get; set; }
            public string? moin { get; set; }
            public string? TAFZIL { get; set; }
            public double? SumOfBED { get; set; }
            public double? SumOfBES { get; set; }
            public double? bed { get; set; }
            public double? bes { get; set; }
        }

        /// <summary>
        /// نگاشت به ردیف سفیر. ستونِ نام همان است که گریدِ هر پنجره‌ی WPF نشان می‌دهد
        /// (کل: NAME، معین: moin، تفصیلی: TAFZIL، تفصیلی ۲ تا ۴: NAME)، و اعداد عیناً
        /// همان اصلاحِ WPF را می‌گیرند: Math.Abs(Math.Truncate(x)).
        /// </summary>
        public static TrialBalanceRowDto Map(TrialBalanceLevel level, ProcRow r)
        {
            static double Fix(double? v) => Math.Abs(Math.Truncate(v ?? 0));
            var row = new TrialBalanceRowDto
            {
                SumBed = Fix(r.SumOfBED),
                SumBes = Fix(r.SumOfBES),
                Bed = Fix(r.bed),
                Bes = Fix(r.bes),
            };
            switch (level)
            {
                case TrialBalanceLevel.Kol:
                    row.Kol = r.NUMBER; row.Name = r.NAME; break;
                case TrialBalanceLevel.Moin:
                    row.Kol = r.N_KOL; row.Moin = r.NUMBER; row.Name = r.moin; break;
                case TrialBalanceLevel.Tafsili:
                    row.Kol = r.N_KOL; row.Moin = r.NUMBER; row.Tafsili = r.TNUMBER; row.Name = r.TAFZIL; break;
                default:
                    row.Kol = r.N_KOL; row.Moin = r.NUMBER; row.Tafsili = r.TNUMBER; row.Tafsili2 = r.HES_T2;
                    if (level >= TrialBalanceLevel.Tafsili3) row.Tafsili3 = r.TNUMBER3;
                    if (level >= TrialBalanceLevel.Tafsili4) row.Tafsili4 = r.TNUMBER4;
                    row.Name = r.NAME;
                    break;
            }
            return row;
        }

        public async Task<List<TrialBalanceRowDto>> LoadAsync(TrialBalanceQuery q)
        {
            var rows = await _db.DoGetStoreProcedureSQLAsync<ProcRow>(
                ProcedureFor(q.Level), new Dapper.DynamicParameters(BuildParameters(q)), commandTimeout: 600);
            return rows.Select(r => Map(q.Level, r)).ToList();
        }

        private sealed class PermRow
        {
            public string FORMNAME { get; set; } = "";
            public bool? SEE { get; set; }
        }

        /// <summary>
        /// مثل SETSECURITY: پنجره فقط وقتی باز می‌شود که SEE روی همان فرم روشن باشد.
        /// ردیفِ نبودِ SAL_CHEK یعنی «اجازه ندارد» (WPF در آن حالت ردیف صفر می‌سازد و
        /// باز هم پنجره را می‌بندد؛ ما چیزی نمی‌نویسیم).
        /// </summary>
        public async Task<(bool Kol, bool Moin, bool Tafsili)> GetAccessAsync(int userCo)
        {
            var rows = (await _db.DoGetDataSQLAsync<PermRow>(@"
                SELECT f.FORMNAME, CAST(sc.SEE AS bit) SEE
                FROM dbo.TFORMS f
                JOIN dbo.SAL_CHEK sc ON sc.OBJECT = f.IDH AND sc.USERCO = @userCo
                WHERE f.FORMNAME IN (@k, @m, @t)",
                new { userCo, k = FormKol, m = FormMoin, t = FormTafsili })).ToList();
            bool See(string f) => rows.Any(r => string.Equals(r.FORMNAME, f, StringComparison.OrdinalIgnoreCase) && r.SEE == true);
            return (See(FormKol), See(FormMoin), See(FormTafsili));
        }

        public static bool Allowed((bool Kol, bool Moin, bool Tafsili) a, TrialBalanceLevel level) => level switch
        {
            TrialBalanceLevel.Kol => a.Kol,
            TrialBalanceLevel.Moin => a.Moin,
            _ => a.Tafsili
        };
    }
}
