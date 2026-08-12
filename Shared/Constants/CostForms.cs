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
    }
}
