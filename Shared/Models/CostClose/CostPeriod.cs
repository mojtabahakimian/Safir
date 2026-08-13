namespace Safir.Shared.Models.CostClose
{
    /// <summary>
    /// کمکی دوره — تا سال مالی در صفحات مختلف تکرار و ناهماهنگ نشود.
    ///
    /// بازه‌ها با BETWEEN کار می‌کنند و روز ۳۱ همیشه امن است، حتی برای
    /// ماه‌های ۳۰ روزه و اسفند. مهم این است که سال از یک جا بیاید.
    /// </summary>
    public static class CostPeriod
    {
        /// <summary>سال مالی جاری. از تنظیمات یا وضعیت کاربر مقدار می‌گیرد.</summary>
        public static short CurrentYear { get; set; } = 1405;

        public static long From(short year, byte month) => year * 10000L + month * 100L + 1;
        public static long To  (short year, byte month) => year * 10000L + month * 100L + 31;

        public static long From(byte month) => From(CurrentYear, month);
        public static long To  (byte month) => To  (CurrentYear, month);

        public static string MonthName(byte m) => m switch
        {
            1  => "فروردین", 2  => "اردیبهشت", 3  => "خرداد",
            4  => "تیر",     5  => "مرداد",    6  => "شهریور",
            7  => "مهر",     8  => "آبان",     9  => "آذر",
            10 => "دی",      11 => "بهمن",     12 => "اسفند",
            _  => m.ToString()
        };

        /// <summary>14050401 → 1405/04/01</summary>
        public static string FormatDate(long d)
            => $"{d / 10000}/{d / 100 % 100:00}/{d % 100:00}";

        /// <summary>ماه جاری بر اساس تاریخ روز شمسی که از سرور می‌آید</summary>
        public static byte MonthOf(long shamsiDate) => (byte)(shamsiDate / 100 % 100);
    }
}
