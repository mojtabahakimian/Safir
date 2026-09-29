using System.Data;
using System.Globalization;
using Dapper;

namespace Safir.Server.Services
{
    /// <summary>
    /// پیوند «دوره‌ی حقوق ↔ سند حسابداری آن».
    ///
    /// چرا با <c>DEED_HED.base</c> و نه شماره‌ی سند (<c>N_S</c>): نرم‌افزار WPF در
    /// بازشماره‌گذاریِ اسناد (CC_sp_S04_SortDeeds) شماره‌ی سندها را عوض می‌کند و فقط ۹
    /// جدول فرزندِ خودش را با ON UPDATE CASCADE دنبال می‌کند — PAY2_PERIOD جزو آن‌ها
    /// نیست. شماره‌ی ذخیره‌شده کهنه می‌شد و «لغو صدور» یا چیزی را پاک نمی‌کرد (سند
    /// می‌ماند و بازصدور سند دوم می‌ساخت) یا سندِ دیگری را. <c>base</c> ستون Identity
    /// است و در بازشماره‌گذاری و Rollback بستن ماه (IDENTITY_INSERT) ثابت می‌ماند.
    ///
    /// <c>PAY2_PERIOD.DEED_N_S_PAY</c> فقط برای خواندنِ انسانی نوشته می‌شود و ممکن
    /// است کهنه باشد؛ منطق هرگز نباید آن را بخواند.
    /// </summary>
    public static class Pay2DeedLink
    {
        /// <summary>عنوانِ سندِ حقوق؛ همان که صدور روی DEED_HED.SHARH_S می‌نویسد.</summary>
        public static string Title(long periodDate) =>
            "سند حقوق و دستمزد دوره " + periodDate.ToString(CultureInfo.InvariantCulture);

        /// <summary>شماره‌ی فعلیِ سندِ پیوندشده به دوره (@perId)؛ بدون پیوند = ردیفی برنمی‌گردد.</summary>
        public const string LinkedNsSql =
            "SELECT h.N_S FROM PAY2_PERIOD P INNER JOIN DEED_HED h ON h.base = P.DEED_BASE WHERE P.PER_ID = @perId";

        /// <summary>
        /// سندِ حقوقِ این دوره را پیدا می‌کند: اول با پیوند (base)؛ اگر پیوندی نبود
        /// (سندِ یتیمِ باقی‌مانده از پیش از این اصلاح) با عنوانِ دقیق، به‌شرط آنکه
        /// یکتا باشد و به دوره‌ی دیگری وصل نباشد. نبودنِ سند = <c>null</c>.
        /// </summary>
        public static async Task<double?> FindAsync(IDbConnection conn, IDbTransaction tran, int perId, long periodDate)
        {
            var linked = await conn.QuerySingleOrDefaultAsync<double?>(LinkedNsSql, new { perId }, tran);
            if (linked.HasValue) return linked;

            string title = Title(periodDate);
            var orphans = (await conn.QueryAsync<double>(@"
                SELECT h.N_S FROM DEED_HED h
                WHERE h.SHARH_S = @title
                  AND NOT EXISTS (SELECT 1 FROM PAY2_PERIOD P WHERE P.DEED_BASE = h.base AND P.PER_ID <> @perId)
                ORDER BY h.N_S", new { title, perId }, tran)).ToList();

            if (orphans.Count == 0) return null;

            // عنوان شامل کارگاه نیست؛ اگر چند کارگاه در همین ماه دوره دارند، سندِ یتیم
            // را نمی‌شود با اطمینان به این دوره نسبت داد.
            int sameMonthPeriods = await conn.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM PAY2_PERIOD WHERE PERIOD_DATE = @periodDate", new { periodDate }, tran);

            if (orphans.Count > 1 || sameMonthPeriods > 1)
                throw new InvalidOperationException(
                    $"در دفتر حسابداری سند(های) «{title}» به شماره‌ی {string.Join("، ", orphans.Select(x => x.ToString("0", CultureInfo.InvariantCulture)))} " +
                    "هست و مشخص نیست کدام مربوط به این دوره است. سندِ اضافه را در نرم‌افزار حسابداری حذف کنید و دوباره تلاش کنید.");

            return orphans[0];
        }
    }
}
