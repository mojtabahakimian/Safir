using Safir.Shared.Models.CostClose;

namespace Safir.Server.CostClose
{
    /// <summary>
    /// تجمیعِ صورت‌های مالیِ چند ماه در یک صورتِ واحد.
    ///
    /// ── چرا این کلاس لازم است ──
    /// وسوسه‌ی اول، جمع‌کردنِ ساده‌ی سطرهاست. آن کار عددِ غلط می‌دهد:
    /// «موجودی اول دوره»ی اردیبهشت همان «موجودی پایان دوره»ی فروردین است،
    /// پس جمعِ ساده هر موجودیِ میانی را دو بار می‌شمارد و بهای تمام‌شده را
    /// به‌اندازه‌ی همان موجودی‌ها باد می‌کند.
    ///
    /// قاعده‌ی درست برای یک بازه‌ی چندماهه:
    ///   • سطرهای گردشی (خرید، دستمزد، سربار، فروش، هزینه) جمع می‌شوند
    ///   • موجودیِ اول دوره از <b>نخستین</b> ماه برداشته می‌شود
    ///   • موجودیِ پایان دوره از <b>آخرین</b> ماه
    ///   • سطرهای جمع و درصد دوباره محاسبه می‌شوند، نه جمع
    ///
    /// ── چرا بر اساس شماره‌ی ردیف و نه متن ──
    /// شماره‌ی ردیف در CC_sp_FinancialStatements ثابت و عمدی است (۱۰، ۲۰،
    /// …). تکیه بر متنِ فارسیِ سطر شکننده بود: یک ویرایشِ نگارشی کافی بود
    /// تا تجمیع بی‌صدا غلط شود.
    /// </summary>
    public static class FinancialConsolidator
    {
        // ── شماره‌ی ردیف‌ها، عیناً از 28-financial-statements.sql ──

        private const int CogmOpening  = 10;   // موجودی اول دوره مواد
        private const int CogmAvail    = 30;   // مواد آماده مصرف      (جمع)
        private const int CogmClosing  = 40;   // کسر: موجودی پایان دوره مواد
        private const int CogmUsed     = 50;   // مواد مصرف‌شده        (جمع)
        private const int CogmTotal    = 80;   // بهای کالای ساخته‌شده (جمع)

        private const int CogsOpening  = 10;   // موجودی اول دوره کالای ساخته‌شده
        private const int CogsCogm     = 20;   // بهای کالای ساخته‌شده
        private const int CogsAvail    = 30;   // کالای آماده فروش     (جمع)
        private const int CogsClosing  = 40;   // کسر: موجودی پایان دوره
        private const int CogsTotal    = 50;   // بهای کالای فروش‌رفته (جمع)
        private const int CogsPerS12   = 60;   // تطبیق
        private const int CogsDiff     = 70;   // اختلاف

        private const int IncSales     = 10;
        private const int IncCogs      = 20;
        private const int IncGross     = 30;   // (جمع)
        private const int IncExpTotal  = 80;   // (جمع)
        private const int IncOperating = 90;   // (جمع)
        private const int IncGrossPct  = 100;  // درصد
        private const int IncOperPct   = 110;  // درصد

        /// <summary>
        /// دوره‌ها باید از قبل به ترتیبِ زمانی مرتب شده باشند؛ «نخستین» و
        /// «آخرین» بر همین اساس تعیین می‌شوند.
        /// </summary>
        public static FinancialStatementsDto Consolidate(
            IReadOnlyList<PeriodStatementsDto> periods, List<string> notes)
        {
            if (periods.Count == 0) return new FinancialStatementsDto();

            var first = periods[0].Statements;
            var last  = periods[^1].Statements;

            var result = new FinancialStatementsDto
            {
                Cogm   = CloneShape(first.Cogm),
                Cogs   = CloneShape(first.Cogs),
                Income = CloneShape(first.Income)
            };

            // ── ۱) گردشی‌ها: جمعِ همه‌ی دوره‌ها ──
            foreach (var p in periods)
            {
                Add(result.Cogm,   p.Statements.Cogm);
                Add(result.Cogs,   p.Statements.Cogs);
                Add(result.Income, p.Statements.Income);
            }

            // ── ۲) موجودی‌ها: اول از نخستین ماه، پایان از آخرین ──
            Set(result.Cogm, CogmOpening, Get(first.Cogm, CogmOpening));
            Set(result.Cogm, CogmClosing, Get(last.Cogm,  CogmClosing));
            Set(result.Cogs, CogsOpening, Get(first.Cogs, CogsOpening));
            Set(result.Cogs, CogsClosing, Get(last.Cogs,  CogsClosing));

            // ── ۳) جمع‌ها: دوباره محاسبه ──
            var matOpen  = Get(result.Cogm, CogmOpening) ?? 0;
            var purchase = Get(result.Cogm, 20)          ?? 0;
            var matClose = Get(result.Cogm, CogmClosing) ?? 0;   // از قبل منفی است
            var wage     = Get(result.Cogm, 60)          ?? 0;
            var oh       = Get(result.Cogm, 70)          ?? 0;

            var matAvail = matOpen + purchase;
            var matUsed  = matAvail + matClose;
            var cogm     = matUsed + wage + oh;

            Set(result.Cogm, CogmAvail, matAvail);
            Set(result.Cogm, CogmUsed,  matUsed);
            Set(result.Cogm, CogmTotal, cogm);

            var fgOpen  = Get(result.Cogs, CogsOpening) ?? 0;
            var fgClose = Get(result.Cogs, CogsClosing) ?? 0;     // از قبل منفی است

            Set(result.Cogs, CogsCogm,  cogm);                   // همان COGM تجمیعی
            Set(result.Cogs, CogsAvail, fgOpen + cogm);
            var cogs = fgOpen + cogm + fgClose;
            Set(result.Cogs, CogsTotal, cogs);

            var s12 = Get(result.Cogs, CogsPerS12) ?? 0;          // این یکی گردشی است
            Set(result.Cogs, CogsDiff, cogs - s12);

            var sales   = Get(result.Income, IncSales)    ?? 0;
            var incCogs = Get(result.Income, IncCogs)     ?? 0;   // منفی
            var expAll  = Get(result.Income, IncExpTotal) ?? 0;   // منفی
            var gross   = sales + incCogs;

            Set(result.Income, IncGross,     gross);
            Set(result.Income, IncOperating, gross + expAll);

            // ── ۴) درصدها: نسبت است، جمع نمی‌شود ──
            Set(result.Income, IncGrossPct,
                sales != 0 ? Math.Round(gross / sales * 100, 1) : (double?)null);
            Set(result.Income, IncOperPct,
                sales != 0 ? Math.Round((gross + expAll) / sales * 100, 1) : (double?)null);

            // ── ۵) سرفصل‌های هزینه: جمعِ مانده و سهم، به تفکیک حساب ──
            result.Expenses = periods
                .SelectMany(p => p.Statements.Expenses)
                .GroupBy(e => (e.Category, e.Kol, e.Moin, e.Tafsili))
                .Select(g => new FinExpenseDto
                {
                    Category = g.Key.Category,
                    Kol      = g.Key.Kol,
                    Moin     = g.Key.Moin,
                    Tafsili  = g.Key.Tafsili,
                    // ضریب یک تنظیم است نه رقم؛ اگر بین ماه‌ها فرق کرده
                    // باشد، آخری را نشان می‌دهیم و پایین هشدار می‌دهیم.
                    Ratio    = g.Last().Ratio,
                    Balance  = g.Sum(x => x.Balance),
                    Share    = g.Sum(x => x.Share),
                    Note     = g.Last().Note
                })
                .OrderBy(e => e.Category).ThenBy(e => e.Kol).ToList();

            // ── هشدارها ──
            if (periods.Count > 1)
            {
                notes.Add("موجودی اول دوره از نخستین ماه و موجودی پایان دوره از آخرین ماه " +
                          "برداشته شده؛ موجودی‌های میانی جمع نشده‌اند چون هر کدام هم‌زمان " +
                          "پایانِ یک ماه و آغازِ ماه بعدند.");

                var gaps = FindGaps(periods);
                if (gaps.Count > 0)
                    notes.Add("ماه‌های انتخاب‌شده پیوسته نیستند و " + string.Join("، ", gaps) +
                              " جا افتاده‌اند. صورتِ تجمیعی موجودیِ ابتدا و انتها را از دو سرِ " +
                              "همین انتخاب می‌گیرد، پس گردشِ ماه‌های نیامده در آن دیده نمی‌شود.");

                var ratioChanged = periods
                    .SelectMany(p => p.Statements.Expenses)
                    .GroupBy(e => (e.Category, e.Kol, e.Moin, e.Tafsili))
                    .Any(g => g.Select(x => x.Ratio).Distinct().Count() > 1);

                if (ratioChanged)
                    notes.Add("ضریبِ دست‌کم یکی از سرفصل‌های هزینه بین ماه‌های انتخاب‌شده " +
                              "تغییر کرده. مبالغ درست جمع شده‌اند، ولی ستون «ضریب» ضریبِ " +
                              "آخرین ماه را نشان می‌دهد.");
            }

            return result;
        }

        /// <summary>ماه‌هایی که بین نخستین و آخرینِ انتخاب هستند ولی انتخاب نشده‌اند.</summary>
        private static List<string> FindGaps(IReadOnlyList<PeriodStatementsDto> periods)
        {
            var have = periods.Select(p => p.Year * 12 + p.Month).ToHashSet();
            var min  = have.Min();
            var max  = have.Max();

            var gaps = new List<string>();
            for (var k = min + 1; k < max; k++)
                if (!have.Contains(k))
                    gaps.Add(MonthName((byte)(k % 12 == 0 ? 12 : k % 12)));

            return gaps;
        }

        public static string MonthName(byte m) => m switch
        {
            1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد",  4 => "تیر",
            5 => "مرداد",   6 => "شهریور",   7 => "مهر",    8 => "آبان",
            9 => "آذر",     10 => "دی",      11 => "بهمن",  12 => "اسفند",
            _ => m.ToString()
        };

        // ── کمکی‌ها ──

        /// <summary>ساختار سطرها را نگه می‌دارد و مبالغ را صفر می‌کند.</summary>
        private static List<FinLineDto> CloneShape(List<FinLineDto> src) =>
            src.Select(l => new FinLineDto
            {
                Row = l.Row, Text = l.Text, Kind = l.Kind, Amount = 0
            }).ToList();

        private static void Add(List<FinLineDto> target, List<FinLineDto> src)
        {
            foreach (var s in src)
            {
                var t = target.FirstOrDefault(x => x.Row == s.Row);
                if (t is null)
                {
                    target.Add(new FinLineDto
                    {
                        Row = s.Row, Text = s.Text, Kind = s.Kind, Amount = s.Amount
                    });
                    continue;
                }
                t.Amount = (t.Amount ?? 0) + (s.Amount ?? 0);
            }
        }

        private static double? Get(List<FinLineDto> lines, int row) =>
            lines.FirstOrDefault(l => l.Row == row)?.Amount;

        private static void Set(List<FinLineDto> lines, int row, double? value)
        {
            var l = lines.FirstOrDefault(x => x.Row == row);
            if (l is not null) l.Amount = value;
        }
    }
}
