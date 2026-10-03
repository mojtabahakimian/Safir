namespace Safir.Shared.Models.Pulse
{
    /// <summary>
    /// «نبض سازمان» — همان داشبوردهای مدیریتیِ WPF (NABZEFROOSH، NABZEDARY، NABZEMALI) در یک صفحه.
    /// هر سری یک عدد برای هر روزِ <see cref="Days"/> دارد (روزِ بی‌گردش صفر است، نه حذف‌شده)؛
    /// ۱۸۰ روز برمی‌گردد تا صفحه بازه‌ی ۹۰روزه را با ۹۰ روزِ قبلش مقایسه کند.
    /// </summary>
    public class PulseDto
    {
        /// <summary>روزها به شمسی (yyyymmdd)، از قدیم به جدید؛ آخرین آن <see cref="End"/> است.</summary>
        public long[] Days { get; set; } = System.Array.Empty<long>();
        public long End { get; set; }
        /// <summary>روزِ هفته‌ی اولین روز، شنبه = ۰ — صفحه روزِ هفته‌ی بقیه را از روی آن می‌شمارد.</summary>
        public int FirstWeekday { get; set; }
        public int FiscalYear { get; set; }

        /// <summary>فاکتور فروش (HEAD_LST.TAG = 2، TAMIR = -1): جمعِ MABL_K − N_MOIN و تعدادِ فاکتور.</summary>
        public double[] Sales { get; set; } = System.Array.Empty<double>();
        public int[] SalesCount { get; set; } = System.Array.Empty<int>();

        /// <summary>پیش‌فاکتور (TAG = 20).</summary>
        public double[] PreInvoices { get; set; } = System.Array.Empty<double>();
        public int[] PreInvoiceCount { get; set; } = System.Array.Empty<int>();

        /// <summary>فعالیت‌های کل (TASKS) و جزء (EVENTS) در هر روز.</summary>
        public int[] Tasks { get; set; } = System.Array.Empty<int>();
        public int[] Events { get; set; } = System.Array.Empty<int>();

        /// <summary>دریافتِ نقد: بدهکارِ حسابِ صندوق (SAZMAN.hesnaghd) در اسناد.</summary>
        public double[] Cash { get; set; } = System.Array.Empty<double>();
        public string? CashAccount { get; set; }

        /// <summary>چک‌های دریافتی (PAY_GETD) به تاریخِ دریافت.</summary>
        public double[] Cheques { get; set; } = System.Array.Empty<double>();

        /// <summary>آخرین روزِ دارای فاکتور/پیش‌فاکتور — وقتی دیتابیس نسخه‌ی قدیمی است، صفحه می‌گوید چرا انتهای نمودار صفر است.</summary>
        public long? LastSalesDay { get; set; }
    }
}
