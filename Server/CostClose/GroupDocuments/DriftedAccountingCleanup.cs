using Safir.Shared.Interfaces;

namespace Safir.Server.CostClose.GroupDocuments
{
    /// <summary>
    /// پاک‌سازیِ سطرهای حسابداریِ «جامانده از تاریخِ قبلی».
    ///
    /// ── مسئله ──
    /// وقتی تاریخِ یک برگه (HEAD_LST.DATE_N) بعد از ثبتِ سند حسابداری‌اش
    /// اصلاح می‌شود، سندِ حسابداری با تاریخِ قدیمی سرِ جایش می‌ماند. هیچ
    /// اجرای بعدی هم پاکش نمی‌کند، چون فیلترِ تاریخِ هر پاس فقط برگه‌های
    /// همان بازه را برمی‌دارد و این برگه دیگر در آن بازه نیست — یک سطرِ
    /// یتیم که تا ابد در ماهِ اشتباه می‌ماند و CHK-02 را مغایر نشان می‌دهد.
    ///
    /// ── چرا اینجا و نه داخلِ یک سرویس ──
    /// این منطق اول فقط داخل SaleReturnRebuildService نوشته شد (کشف روی کد
    /// ۳۲۹۴/انبار۸۱۳). بعداً معلوم شد همان اتفاق برای *فروش* هم افتاده و
    /// چون آنجا کپی‌اش وجود نداشت، بازسازیِ فروش هیچ اثری نداشت:
    ///
    ///     کد ۳۱۳۵ / انبار ۸۰۷ / خرداد ۱۴۰۵ — مغایرت ۱۵٬۴۱۷٬۳۸۴ ریال.
    ///     حوالهٔ فروش ۱۰۵۶ به ۱۴۰۵/۰۴/۱۳ منتقل شده بود ولی ۴۰ سطرش هنوز
    ///     داخل سند ۵۷۲۵ با تاریخ ۱۴۰۵/۰۲/۰۳ نشسته بود. کاربر بازسازیِ
    ///     گروهیِ اردیبهشت را زد و هیچ عددی تکان نخورد — چون برگه دیگر
    ///     اردیبهشتی نبود که انتخاب شود.
    ///
    /// پس منطق یک‌جا نشست تا هر سرویسی بتواند صدایش بزند.
    ///
    /// ── چرا حذف امن است ──
    /// سطر حذف‌شده از بین نمی‌رود: وقتی پاسِ ماهِ درست اجرا شود (همان‌جایی
    /// که DATE_N الان واقعاً به آن اشاره می‌کند)، همین برگه را در محدودهٔ
    /// خودش می‌بیند و با تاریخ و نرخِ درست دوباره ثبتش می‌کند.
    /// </summary>
    internal static class DriftedAccountingCleanup
    {
        /// <summary>
        /// سطرهای این TAG را که تاریخِ سند حسابداری‌شان در بازهٔ همین اجراست
        /// ولی تاریخِ فعلیِ برگهٔ مبدأشان دیگر در این بازه نیست، حذف می‌کند.
        /// </summary>
        /// <param name="deedTag">
        /// مقدار DEED_DTL.TAG. برای فروش ۱۳، برگشت فروش ۴ و ۲۵، خرید آزاد ۲۷.
        /// </param>
        /// <param name="headTag">
        /// مقدار HEAD_LST.TAG که تاریخِ مرجع از آن خوانده می‌شود. معمولاً
        /// همان deedTag است (یک برگه می‌تواند چند سطرِ HEAD_LST با TAGهای
        /// مختلف داشته باشد و همه یک DATE_N دارند — روی حوالهٔ ۱۰۵۶ هم
        /// TAG=۲ و هم TAG=۱۳ تاریخ ۱۴۰۵/۰۴/۱۳ داشتند). فقط وقتی صریح داده
        /// می‌شود که سرویسی TAGِ متفاوتی را مرجع بداند.
        /// </param>
        /// <returns>تعداد سطرهای حذف‌شده — صفر یعنی چیزی جا نمانده بود.</returns>
        public static async Task<int> RunAsync(
            IDatabaseService db, double deedTag, long dateFrom, long dateTo, double? headTag = null)
        {
            return await db.DoExecuteSQLAsync(
                "DELETE d FROM dbo.DEED_DTL d " +
                "JOIN dbo.DEED_HED h ON h.N_S = d.N_S " +
                "JOIN dbo.HEAD_LST hl ON hl.NUMBER = d.NUMBER AND hl.TAG = @HeadTag " +
                "WHERE d.TAG = @Tag " +
                "  AND h.DATE_S BETWEEN @DateFrom AND @DateTo " +
                "  AND hl.DATE_N NOT BETWEEN @DateFrom AND @DateTo",
                new
                {
                    Tag      = deedTag,
                    HeadTag  = headTag ?? deedTag,
                    DateFrom = dateFrom,
                    DateTo   = dateTo
                },
                // خرداد ۱۴۰۵: همین دستور با پیش‌فرضِ ۳۰ ثانیه‌ی Dapper
                // «Execution Timeout Expired» داد، درحالی‌که صفر ردیف
                // برمی‌گرداند و شمارشِ همان شرط ۳٫۸ ثانیه طول می‌کشد —
                // یعنی انتظارِ قفل بود، نه کندی.
                commandTimeout: CostCloseTuning.BatchTimeoutSeconds);
        }

        /// <summary>متن استاندارد لاگ، تا همه‌ی سرویس‌ها یک‌جور گزارش بدهند.</summary>
        public static string LogMessage(double deedTag, int rows)
            => $"پاک‌سازی حسابداریِ قدیمی (TAG={deedTag}): {rows} ردیف که تاریخ برگه‌ی مبدأشان دیگر با تاریخ سند حسابداری نمی‌خواند حذف شد — با اجرای بازسازیِ ماهِ درست دوباره ثبت می‌شوند.";
    }
}
