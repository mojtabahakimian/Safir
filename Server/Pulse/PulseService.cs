using System.Globalization;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Pulse;

namespace Safir.Server.Pulse
{
    /// <summary>
    /// «نبض سازمان» — داده‌ی داشبوردهای مدیریتیِ WPF با همان تعریف‌ها:
    ///   NABZEFROOSH: فاکتور فروش (TAG 2، TAMIR −1) و پیش‌فاکتور (TAG 20)، جمعِ MABL_K − N_MOIN در هر DATE_N
    ///   NABZEDARY:  COUNT(COMP_COD) از TASKS و COUNT(IDD) از EVENTS در هر STDATE
    ///   NABZEMALI:  جمعِ BED ِ حسابِ صندوق (SAZMAN.hesnaghd) در DEED_DTL و جمعِ MABL ِ PAY_GETD در هر DATE
    ///
    /// تفاوت‌های عمدی با WPF (همه ایرادِ آنجا بودند):
    ///   • WPF «TOP(90) روزِ دارای داده» می‌گرفت؛ اینجا ۹۰ روزِ تقویمیِ آخر، با روزِ بی‌گردش = صفر.
    ///   • پرس‌وجوی فاکتورِ فروش در WPF صعودی مرتب می‌شد و «۹۰ روزه» در عمل ۹۰ روزِ اولِ سال بود.
    ///   • سری‌ی ۷روزه‌ی فعالیت‌ها از داده‌ی ۳۰روزه پر می‌شد و EVENTS اصلاً ORDER BY نداشت.
    ///   • WPF هر بار view های nabz_mali را DROP/CREATE می‌کرد؛ اینجا چیزی در دیتابیس ساخته نمی‌شود.
    /// </summary>
    public sealed class PulseService
    {
        /// <summary>۹۰ روزِ نمایش + ۹۰ روزِ قبلش برای مقایسه — حداقل؛ اگر ماه‌های سال بیشتر بخواهند، بیشتر.</summary>
        public const int DayCount = 180;

        private static readonly PersianCalendar Pc = new();
        private readonly IDatabaseService _db;

        public PulseService(IDatabaseService db) => _db = db;

        private sealed class DayValue
        {
            public long D { get; set; }
            public double V { get; set; }
            public int N { get; set; }
        }

        private sealed class SazmanRow
        {
            public string? Hesnaghd { get; set; }
            public int? Yea { get; set; }
        }

        public async Task<PulseDto> LoadAsync(DateTime now)
        {
            var sazman = (await _db.DoGetDataSQLAsync<SazmanRow>(
                "SELECT TOP 1 CAST(hesnaghd AS nvarchar(50)) Hesnaghd, CAST(YEA AS int) Yea FROM dbo.SAZMAN")).FirstOrDefault();
            var today = ToPersian(now);
            var year = sazman?.Yea is > 0 ? sazman.Yea.Value : (int)(today / 10000);

            var lastData = await _db.DoGetDataSQLAsyncSingle<long?>(@"
                SELECT MAX(d) FROM (
                    SELECT MAX(CAST(DATE_N AS bigint)) d FROM dbo.HEAD_LST WHERE TAG IN (2, 20) AND DATE_N BETWEEN @y1 AND @y2
                    UNION ALL SELECT MAX(CAST(STDATE AS bigint)) FROM dbo.TASKS WHERE STDATE BETWEEN @y1 AND @y2
                    UNION ALL SELECT MAX(DATE_S) FROM dbo.DEED_HED WHERE DATE_S BETWEEN @y1 AND @y2) x",
                new { y1 = year * 10000L + 101, y2 = year * 10000L + 1230 });

            var end = EndDay(today, year, lastData);
            var days = PersianDays(end, DayCountFor(end, year));
            var p = new { from = days[0], to = end };

            var sales = await _db.DoGetDataSQLAsync<DayValue>(@"
                SELECT CAST(h.DATE_N AS bigint) D, SUM(ISNULL(i.MABL_K, 0) - ISNULL(i.N_MOIN, 0)) V, COUNT(DISTINCT h.NUMBER) N
                FROM dbo.HEAD_LST h JOIN dbo.INVO_LST i ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                WHERE h.TAG = 2 AND h.TAMIR = -1 AND h.DATE_N BETWEEN @from AND @to
                GROUP BY h.DATE_N", p);
            var pre = await _db.DoGetDataSQLAsync<DayValue>(@"
                SELECT CAST(h.DATE_N AS bigint) D, SUM(ISNULL(i.MABL_K, 0) - ISNULL(i.N_MOIN, 0)) V, COUNT(DISTINCT h.NUMBER) N
                FROM dbo.HEAD_LST h JOIN dbo.INVO_LST i ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                WHERE h.TAG = 20 AND h.DATE_N BETWEEN @from AND @to
                GROUP BY h.DATE_N", p);
            var tasks = await _db.DoGetDataSQLAsync<DayValue>(@"
                SELECT CAST(STDATE AS bigint) D, COUNT(COMP_COD) N FROM dbo.TASKS
                WHERE STDATE BETWEEN @from AND @to GROUP BY STDATE", p);
            var events = await _db.DoGetDataSQLAsync<DayValue>(@"
                SELECT CAST(STDATE AS bigint) D, COUNT(IDD) N FROM dbo.EVENTS
                WHERE STDATE BETWEEN @from AND @to GROUP BY STDATE", p);
            var cheques = await _db.DoGetDataSQLAsync<DayValue>(@"
                SELECT CAST(DATE AS bigint) D, SUM(ISNULL(MABL, 0)) V FROM dbo.PAY_GETD
                WHERE DATE BETWEEN @from AND @to GROUP BY DATE", p);

            var cashHes = string.IsNullOrWhiteSpace(sazman?.Hesnaghd) ? null : sazman.Hesnaghd.Trim();
            var cash = cashHes is null ? Enumerable.Empty<DayValue>() : await _db.DoGetDataSQLAsync<DayValue>(@"
                SELECT h.DATE_S D, SUM(ISNULL(d.BED, 0)) V
                FROM dbo.DEED_DTL d JOIN dbo.DEED_HED h ON d.N_S = h.N_S
                WHERE d.HES = @hes AND h.DATE_S BETWEEN @from AND @to
                GROUP BY h.DATE_S", new { p.from, p.to, hes = cashHes });

            var salesList = sales.ToList();
            var preList = pre.ToList();
            return new PulseDto
            {
                Days = days,
                End = end,
                FirstWeekday = PersianWeekday(FromPersian(days[0])),
                FiscalYear = year,
                Sales = Fill(days, salesList, x => x.V),
                SalesCount = Fill(days, salesList, x => x.N),
                PreInvoices = Fill(days, preList, x => x.V),
                PreInvoiceCount = Fill(days, preList, x => x.N),
                Tasks = Fill(days, tasks, x => x.N),
                Events = Fill(days, events, x => x.N),
                Cash = Fill(days, cash, x => x.V),
                CashAccount = cashHes,
                Cheques = Fill(days, cheques, x => x.V),
                LastSalesDay = salesList.Concat(preList).Where(x => x.V != 0).Select(x => (long?)x.D).Max()
            };
        }

        // ─────────────── تقویم ───────────────

        public static long ToPersian(DateTime d) => Pc.GetYear(d) * 10000L + Pc.GetMonth(d) * 100 + Pc.GetDayOfMonth(d);

        public static DateTime FromPersian(long d) => Pc.ToDateTime((int)(d / 10000), (int)(d / 100 % 100), (int)(d % 100), 0, 0, 0, 0);

        /// <summary>
        /// روزِ آخرِ نمودار: امروز وقتی دیتابیسِ سالِ جاری است؛ برای دیتابیسِ سالِ گذشته آخرین روزِ دارای داده
        /// (یا آخرِ همان سال) — وگرنه نمودارِ یک سالِ بسته‌شده تماماً صفر می‌شد.
        /// </summary>
        public static long EndDay(long today, int fiscalYear, long? lastData)
        {
            var todayYear = (int)(today / 10000);
            if (fiscalYear == todayYear) return today;
            if (lastData is > 0) return lastData.Value;
            return LastDayOfYear(fiscalYear);
        }

        /// <summary>
        /// تعدادِ روزها: دست‌کم <see cref="DayCount"/>، و آن‌قدر که از ۱ اسفندِ سالِ قبل شروع شود تا تبِ هر ماهِ
        /// سالِ مالی (فروردین تا ماهِ جاری) کامل باشد و فروردین هم ماهِ قبلی برای مقایسه داشته باشد.
        /// </summary>
        public static int DayCountFor(long end, int fiscalYear)
        {
            var from = FromPersian((fiscalYear - 1) * 10000L + 1201);
            var span = (int)(FromPersian(end) - from).TotalDays + 1;
            return Math.Max(DayCount, span);
        }

        /// <summary>شنبه = ۰ … جمعه = ۶.</summary>
        public static int PersianWeekday(DateTime d) => ((int)d.DayOfWeek + 1) % 7;

        public static long LastDayOfYear(int year) => year * 10000L + 1200 + Pc.GetDaysInMonth(year, 12);

        /// <summary><paramref name="count"/> روزِ پشتِ سرِ هم که به <paramref name="end"/> ختم می‌شود، از قدیم به جدید.</summary>
        public static long[] PersianDays(long end, int count)
        {
            var last = FromPersian(end);
            var days = new long[count];
            for (int i = 0; i < count; i++) days[i] = ToPersian(last.AddDays(i - count + 1));
            return days;
        }

        private static T[] Fill<T>(long[] days, IEnumerable<DayValue> rows, Func<DayValue, T> pick)
        {
            var map = rows.GroupBy(r => r.D).ToDictionary(g => g.Key, g => g.First());
            return days.Select(d => map.TryGetValue(d, out var r) ? pick(r) : default!).ToArray();
        }
    }
}
