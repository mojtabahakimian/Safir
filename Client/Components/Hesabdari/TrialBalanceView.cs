using System.Globalization;
using Safir.Client.Services;
using Safir.Shared.Models.Hesabdari;

namespace Safir.Client.Components.Hesabdari
{
    /// <summary>
    /// منطقِ نمایشِ تراز آزمایشی که صفحه‌ی /trial-balance و صفحه‌ی چاپش هر دو
    /// لازم دارند — تا آنچه چاپ می‌شود دقیقاً همان باشد که روی صفحه دیده شده.
    /// </summary>
    public static class TrialBalanceView
    {
        public static string LevelName(TrialBalanceLevel l) => l switch
        {
            TrialBalanceLevel.Kol => "کل",
            TrialBalanceLevel.Moin => "معین",
            TrialBalanceLevel.Tafsili => "تفصیلی",
            TrialBalanceLevel.Tafsili2 => "تفصیلی ۲",
            TrialBalanceLevel.Tafsili3 => "تفصیلی ۳",
            _ => "تفصیلی ۴"
        };

        public static int? LevelCode(TrialBalanceLevel level, TrialBalanceRowDto r) => level switch
        {
            TrialBalanceLevel.Kol => r.Kol,
            TrialBalanceLevel.Moin => r.Moin,
            TrialBalanceLevel.Tafsili => r.Tafsili,
            TrialBalanceLevel.Tafsili2 => r.Tafsili2,
            TrialBalanceLevel.Tafsili3 => r.Tafsili3,
            _ => r.Tafsili4
        };

        /// <summary>
        /// شماره‌ای که در ستون «شماره» نشان داده می‌شود. وقتی تراز تفصیلیِ «همه‌ی معین‌ها»
        /// است، تفصیلی‌ها زیر معین‌های مختلف‌اند و شماره‌ی تنها مبهم است؛ آنجا «معین/تفصیلی».
        /// </summary>
        public static string Code(TrialBalanceLevel level, TrialBalanceRowDto r, bool withMoin = false) =>
            withMoin && level == TrialBalanceLevel.Tafsili
                ? $"{r.Moin}/{r.Tafsili}"
                : LevelCode(level, r)?.ToString() ?? "—";

        /// <summary>کدِ حساب برای صورت‌حساب: کل-معین-تفصیلی[-تفصیلی۲…] — همان قالب DEED_DTL.HES.</summary>
        public static string Hes(int?[] parts) => string.Join("-", parts.TakeWhile(x => x is not null));
        public static string Hes(TrialBalanceRowDto r) => Hes(new[] { r.Kol, r.Moin, r.Tafsili, r.Tafsili2, r.Tafsili3, r.Tafsili4 });

        public static string Money(double v) => v == 0 ? "—" : v.ToString("N0", CultureInfo.InvariantCulture);

        // ───────────── فیلتر و مرتب‌سازی (چهارستونی) ─────────────

        public static IEnumerable<TrialBalanceRowDto> Filter(IEnumerable<TrialBalanceRowDto> rows, TrialBalanceLevel level,
            string? q, bool hideZero, string sort, bool desc, bool withMoin = false)
        {
            if (hideZero) rows = rows.Where(r => r.SumBed != 0 || r.SumBes != 0 || r.Bed != 0 || r.Bes != 0);
            var t = (q ?? "").Trim();
            if (t.Length > 0)
                rows = rows.Where(r => (r.Name ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) || Code(level, r, withMoin).Contains(t));
            Func<TrialBalanceRowDto, object> key = sort switch
            {
                "name" => r => r.Name ?? "",
                "sbed" => r => r.SumBed,
                "sbes" => r => r.SumBes,
                "bed" => r => r.Bed,
                "bes" => r => r.Bes,
                _ => r => (withMoin ? (r.Moin ?? 0) * 100_000_000L : 0L) + (LevelCode(level, r) ?? 0)
            };
            return desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
        }

        // ───────────── تراز ماهانه: سطرها حساب، ستون‌ها ماه ─────────────

        public sealed record MonthlyAccount(int? Kol, int? Moin, int? Tafsili, string? Name, Dictionary<int, double> Net)
        {
            public double Total => Net.Values.Sum();
            public int? Code(TrialBalanceLevel level) => level switch
            {
                TrialBalanceLevel.Kol => Kol,
                TrialBalanceLevel.Moin => Moin,
                _ => Tafsili
            };
        }

        public sealed record MonthlyPivot(List<int> Months, List<MonthlyAccount> Accounts)
        {
            public double MonthTotal(int ym) => Accounts.Sum(a => a.Net.GetValueOrDefault(ym));
            public double Total => Accounts.Sum(a => a.Total);
        }

        /// <summary>
        /// سطرهای (حساب، ماه) را به جدولِ حساب × ماه تبدیل می‌کند. مقدارِ هر خانه خالصِ
        /// همان ماه است (بدهکار مثبت، بستانکار منفی) — همان bed/bes پنجره‌ی TARAZ_4_MAH.
        /// </summary>
        public static MonthlyPivot Pivot(IEnumerable<TrialBalanceMonthlyRowDto> rows, TrialBalanceLevel level,
            string? q = null, bool hideZero = false)
        {
            var list = rows.ToList();
            var months = list.Select(r => r.Ym).Distinct().OrderBy(x => x).ToList();
            var accounts = list
                .GroupBy(r => (r.Kol, r.Moin, r.Tafsili))
                .Select(g => new MonthlyAccount(g.Key.Kol, g.Key.Moin, g.Key.Tafsili,
                    g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    g.GroupBy(x => x.Ym).ToDictionary(m => m.Key, m => m.Sum(x => x.Bed - x.Bes))))
                .OrderBy(a => a.Moin ?? 0).ThenBy(a => a.Code(level) ?? 0)
                .ToList();
            if (hideZero) accounts = accounts.Where(a => a.Net.Values.Any(v => v != 0)).ToList();
            var t = (q ?? "").Trim();
            if (t.Length > 0)
                accounts = accounts.Where(a => (a.Name ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)
                                            || (a.Code(level)?.ToString() ?? "").Contains(t)).ToList();
            return new MonthlyPivot(months, accounts);
        }

        private static readonly string[] MonthNames =
            { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

        public static string MonthName(int ym) => ym % 100 is >= 1 and <= 12 ? MonthNames[ym % 100 - 1] : ym.ToString();

        // ───────────── چاپ ─────────────

        /// <summary>آدرسِ صفحه‌ی چاپ برای همان چیزی که الان روی صفحه است.</summary>
        public static string PrintUrl(TrialBalanceQuery query, bool monthly, string? path, string? q, bool hideZero,
            string sort = "code", bool desc = false)
        {
            var s = $"/trial-balance/print?{TrialBalanceApiService.QueryString(query)}";
            if (monthly) s += "&monthly=true";
            if (!string.IsNullOrWhiteSpace(path)) s += "&path=" + Uri.EscapeDataString(path);
            if (!string.IsNullOrWhiteSpace(q)) s += "&q=" + Uri.EscapeDataString(q.Trim());
            if (hideZero) s += "&hideZero=true";
            if (sort != "code" || desc) s += $"&sort={sort}&desc={(desc ? "true" : "false")}";
            return s;
        }
    }
}
