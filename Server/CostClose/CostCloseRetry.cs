namespace Safir.Server.CostClose
{
    /// <summary>
    /// بازتلاش روی بن‌بست (Deadlock، خطای ۱۲۰۵) و انتظارِ قفل (۱۲۲۲).
    ///
    /// ── چرا مشترک ──
    /// همین حلقه تا امروز نُه بار در سرویس‌های بازسازی کپی شده بود
    /// (اسناد گروهی، خروج مواد، نرخ میانگین). گام‌های ارکستریتور هیچ‌کدام
    /// نداشتند، چون فرضِ اولیه این بود که گام‌ها پشت‌سرهم اجرا می‌شوند پس
    /// رقیبی ندارند. رقیب از بیرونِ سامانه می‌آید: نرم‌افزار قدیمی روی همان
    /// DTL_MANF و INVO_LST باز است و کاربرها موقع بستنِ ماه از کار
    /// نمی‌ایستند.
    ///
    /// ── ⚠️ شرطِ استفاده ──
    /// فقط برای کاری که اجرای دوباره‌اش همان نتیجه را بدهد. دو حالت
    /// قبول است:
    ///   • خودِ دستور از وضعیتِ فعلیِ سطر مستقل باشد
    ///     (`SET AVRAGE = &lt;عدد&gt;` — حالتِ S07A)، یا
    ///   • همه‌ی نوشتن‌ها داخل یک تراکنش باشند، چون SQL Server تراکنشِ
    ///     قربانی را کامل برمی‌گرداند و تلاش بعدی از صفر شروع می‌کند
    ///     (حالتِ S09).
    ///
    /// برای رویه‌ای که بیرونِ تراکنش، تکه‌تکه و انباشتی می‌نویسد
    /// (`SET X = X + …`) این helper خطرناک است: نیمه‌ی اولِ کار دوباره
    /// اعمال می‌شود. اگر شک دارید، اول تراکنش را درست کنید.
    ///
    /// وقفه تصادفی است تا همان دو نشست بلافاصله دوباره به هم نخورند، و
    /// با هر تلاش بلندتر می‌شود.
    /// </summary>
    public static class CostCloseRetry
    {
        public const int DefaultMaxAttempts = 4;

        /// <param name="onRetry">
        /// شماره‌ی تلاشِ بعدی و خطای دریافتی. برای این‌که بازتلاش بی‌صدا
        /// نباشد — یک اجرای کند که سه بار گیر کرده باید در لاگ دیده شود.
        /// </param>
        public static async Task<T> ExecuteAsync<T>(
            Func<Task<T>>                        action,
            int                                  maxAttempts = DefaultMaxAttempts,
            Func<int, Exception, Task>?          onRetry     = null)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Microsoft.Data.SqlClient.SqlException ex)
                    when (IsTransientLockError(ex) && attempt < maxAttempts)
                {
                    if (onRetry is not null) await onRetry(attempt + 1, ex);
                    await Task.Delay(Random.Shared.Next(150, 450) * attempt);
                }
            }
        }

        public static Task ExecuteAsync(
            Func<Task>                  action,
            int                         maxAttempts = DefaultMaxAttempts,
            Func<int, Exception, Task>? onRetry     = null)
            => ExecuteAsync<bool>(async () => { await action(); return true; },
                                  maxAttempts, onRetry);

        /// <summary>۱۲۰۵ بن‌بست، ۱۲۲۲ پایانِ مهلتِ انتظارِ قفل.</summary>
        public static bool IsTransientLockError(Microsoft.Data.SqlClient.SqlException ex)
            => ex.Number == 1205 || ex.Number == 1222;
    }
}
