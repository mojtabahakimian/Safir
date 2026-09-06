namespace Safir.Shared.Constants
{
    /// <summary>
    /// نام فرم‌های ماژول «بستن ماه بهای تمام‌شده» برای سیستم دسترسی.
    /// همان الگوی Pay2Forms — هر مورد باید یک رکورد در جدول دسترسی‌ها داشته باشد.
    /// </summary>
    public static class CostForms
    {
        // فرم‌های اصلی
        public const string Dashboard  = "COST_DASHBOARD";
        public const string Run        = "COST_RUN";
        public const string Exceptions = "COST_EXCEPTIONS";
        public const string Variance   = "COST_VARIANCE";
        public const string Conversion = "COST_CONVERSION";
        public const string Margin     = "COST_MARGIN";
        public const string History    = "COST_HISTORY";
        public const string Settings   = "COST_SETTINGS";

        // عملیات حساس — هرکدام مجوز جداگانه دارد
        public const string ActStart      = "COST_ACT_START";       // شروع اجرا
        public const string ActAutoFix    = "COST_ACT_AUTOFIX";     // اصلاح خودکار داده
        public const string ActResolve    = "COST_ACT_RESOLVE";     // بستن استثنا
        public const string ActDecide     = "COST_ACT_DECIDE";      // ثبت تصمیم انحراف
        public const string ActApplyRate  = "COST_ACT_APPLY_RATE";  // اعمال ضریب تعدیل
        public const string ActRollup     = "COST_ACT_ROLLUP";      // اجرای موتور نرخ
        public const string ActRollback   = "COST_ACT_ROLLBACK";    // بازگردانی از اسنپ‌شات
        public const string ActApprove    = "COST_ACT_APPROVE";     // تأیید نهایی و قفل ماه
        public const string ActExport     = "COST_ACT_EXPORT";      // خروجی اکسل
        public const string ActRebuildDocs = "COST_ACT_REBUILD_DOCS"; // بازسازی سند حواله خروج مواد
        public const string ActPostCorrection = "COST_ACT_POST_CORRECTION"; // سند اصلاحی مغایرت CHK-02

        // ⚠️ اصلاح (تأیید کاربر): این دو قبلاً زیرِ Pay2Perm.Upd روی همان
        // فرمِ ActResolve بودند — یعنی «بستن استثنا» که در صفحه‌ی «عملیات
        // حساس» (/salary/manage) فقط یک تیکِ «مجاز است» (=Run) دارد، هیچ‌وقت
        // این دو قابلیت را نشان نمی‌داد چون آن صفحه اصلاً به ستونِ Upd
        // دسترسی نمی‌دهد — کاربر هرچقدر هم «همه‌چیز» را در آن صفحه تیک
        // بزند، باز این دو دکمه پنهان می‌ماندند. طبقِ همان الگویی که بقیه‌ی
        // عملیاتِ حساس دارند (یک فرمِ مستقل، فقط Run)، این دو حالا فرمِ
        // جداگانه‌ی خودشان را دارند — در همان صفحه به‌صورت یک ردیفِ عادیِ
        // دیگر ظاهر می‌شوند، بدون نیاز به هیچ تغییری در خودِ آن صفحه.
        public const string ActResolvePermanent = "COST_ACT_RESOLVE_PERMANENT"; // پذیرش دائمی مغایرت
        public const string ActFixDateMismatch  = "COST_ACT_FIX_DATE";          // اصلاح تاریخ مغایرِ سند
    }
}
