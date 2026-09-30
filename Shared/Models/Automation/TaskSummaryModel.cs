namespace Safir.Shared.Models.Automation
{
    /// <summary>
    /// خلاصه‌ی کارتابل یک کاربر — سربرگ و کارت‌های شاخصِ صفحه‌ی اتوماسیون.
    /// شمارش‌ها با همان فیلترِ کاربر و نوع سندِ فهرست وظایف حساب می‌شوند.
    /// </summary>
    public class TaskSummaryModel
    {
        public int Open { get; set; }
        public int Done { get; set; }
        public int Cancelled { get; set; }

        /// <summary>باز و فوری.</summary>
        public int OpenUrgent { get; set; }

        /// <summary>باز و بیش از <see cref="StaleDays"/> روز از ارجاعش گذشته.</summary>
        public int OpenStale { get; set; }
        public int StaleDays { get; set; }

        /// <summary>امروز ارجاع شده (هر وضعیتی).</summary>
        public int NewToday { get; set; }

        /// <summary>انجام‌شده از شنبه‌ی همین هفته.</summary>
        public int DoneThisWeek { get; set; }

        /// <summary>کار پیشنهادی بعدی: فوری‌ترین، و بین هم‌اولویت‌ها قدیمی‌ترینِ کارهای باز.</summary>
        public TaskModel? Focus { get; set; }

        public int All => Open + Done + Cancelled;
    }
}
