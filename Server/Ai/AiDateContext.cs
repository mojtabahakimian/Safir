using System.Globalization;

namespace Safir.Server.Ai
{
    /// <summary>
    /// «امروز» و «این ماه» را سرور حساب می‌کند، نه مدل.
    ///
    /// ── چرا ──
    /// مدل تاریخ امروز را نمی‌داند و تقویم شمسی را هم خوب حساب نمی‌کند.
    /// در آزمون پایه، «سود این ماه چقدره؟» (مهر) با اطمینان سود مرداد را
    /// برگرداند — چون آخرین ماهی بود که داده داشت. با این بلوک، مدل بازه‌ی
    /// دقیق را از پرامپت می‌خواند و لازم نیست حدس بزند.
    ///
    /// ── سال مالی ──
    /// هر سال مالی دیتابیس جدا دارد (SAZMAN.YEA). «ماه قبل» در فروردین در
    /// دیتابیس سال قبل است؛ اینجا صریح گفته می‌شود تا مدل به‌جایش عدد
    /// اسفندِ همین دیتابیس را (که وجود ندارد) یا ماه دیگری را نسازد.
    /// </summary>
    public static class AiDateContext
    {
        private static readonly string[] MonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        public static string Build(DateTime now, int? fiscalYear)
        {
            var pc    = new PersianCalendar();
            int y     = pc.GetYear(now);
            int m     = pc.GetMonth(now);
            int d     = pc.GetDayOfMonth(now);
            long today = Pack(y, m, d);

            int py = m == 1 ? y - 1 : y;
            int pm = m == 1 ? 12 : m - 1;

            var lines = new List<string>
            {
                $"• امروز: {Fmt(today)} ({today}).",
                $"• «این ماه» یعنی {MonthNames[m - 1]} {y}: از {Pack(y, m, 1)} تا امروز {today} — ماه هنوز تمام نشده.",
                $"• «ماه قبل» یعنی {MonthNames[pm - 1]} {py}: از {Pack(py, pm, 1)} تا {Pack(py, pm, pc.GetDaysInMonth(py, pm))}."
            };

            if (fiscalYear is int fy)
            {
                lines.Add($"• این پایگاه داده فقط سال مالی {fy} را دارد: از {Pack(fy, 1, 1)} تا {Pack(fy, 12, pc.GetDaysInMonth(fy, 12))}.");

                if (py != fy)
                    lines.Add($"• ماه قبل ({MonthNames[pm - 1]} {py}) در سال مالی دیگری است و در این پایگاه نیست؛ " +
                              "برایش عدد نساز و بگو باید از دیتابیس آن سال پرسید.");
                if (y != fy)
                    lines.Add($"• امروز خارج از سال مالی {fy} است؛ «این ماه» و «امروز» در این پایگاه داده‌ای ندارند.");
            }

            return string.Join("\n", lines);
        }

        private static long Pack(int y, int m, int d) => (long)y * 10000 + m * 100 + d;

        private static string Fmt(long v) => $"{v / 10000}/{v / 100 % 100:00}/{v % 100:00}";
    }
}
