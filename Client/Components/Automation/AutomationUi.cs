using System.Globalization;
using Safir.Shared.Models.Automation;

namespace Safir.Client.Components.Automation
{
    /// <summary>
    /// قالب‌بندیِ نمایشیِ کارتابل اتوماسیون: تاریخ شمسی، «چند روز پیش»، حروف
    /// اول نام و رنگ آواتار. هیچ منطق کسب‌وکاری اینجا نیست.
    /// </summary>
    public static class AutomationUi
    {
        private static readonly PersianCalendar Pc = new();

        private static readonly string[] Months =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        public static string WeekDay(DayOfWeek d) => d switch
        {
            DayOfWeek.Saturday  => "شنبه",
            DayOfWeek.Sunday    => "یکشنبه",
            DayOfWeek.Monday    => "دوشنبه",
            DayOfWeek.Tuesday   => "سه‌شنبه",
            DayOfWeek.Wednesday => "چهارشنبه",
            DayOfWeek.Thursday  => "پنجشنبه",
            _                   => "جمعه"
        };

        /// <summary>«سه‌شنبه، ۸ مهر ۱۴۰۵»</summary>
        public static string LongDate(DateTime d)
            => $"{WeekDay(d.DayOfWeek)}، {Pc.GetDayOfMonth(d)} {Months[Pc.GetMonth(d) - 1]} {Pc.GetYear(d)}";

        /// <summary>«۱۴۰۵/۰۷/۰۸»</summary>
        public static string ShortDate(DateTime? d)
        {
            if (!d.HasValue) return "";
            try { return $"{Pc.GetYear(d.Value):D4}/{Pc.GetMonth(d.Value):D2}/{Pc.GetDayOfMonth(d.Value):D2}"; }
            catch { return d.Value.ToString("yyyy/MM/dd"); }
        }

        public static string Time(TimeSpan? t) => t.HasValue ? t.Value.ToString("hh\\:mm") : "";

        public static string Greeting(DateTime now) => now.Hour switch
        {
            < 5  => "شب بخیر",
            < 12 => "صبح بخیر",
            < 16 => "ظهر بخیر",
            < 20 => "عصر بخیر",
            _    => "شب بخیر"
        };

        /// <summary>تاریخ و ساعتِ یک رکورد به‌صورت یک DateTime (برای مقایسه و «چند وقت پیش»).</summary>
        public static DateTime? At(DateTime? date, TimeSpan? time)
            => date.HasValue ? date.Value.Date + (time ?? TimeSpan.Zero) : null;

        /// <summary>«همین حالا»، «۳ ساعت پیش»، «دیروز»، «۱۲ روز پیش»، «۲ ماه پیش».</summary>
        public static string Ago(DateTime? date, TimeSpan? time, DateTime now)
        {
            var at = At(date, time);
            if (!at.HasValue) return "";
            var days = (now.Date - at.Value.Date).Days;
            if (days <= 0)
            {
                if (!time.HasValue) return "امروز";
                var mins = (int)(now - at.Value).TotalMinutes;
                if (mins < 1)  return "همین حالا";
                if (mins < 60) return $"{mins} دقیقه پیش";
                return $"{mins / 60} ساعت پیش";
            }
            if (days == 1)   return "دیروز";
            if (days < 30)   return $"{days} روز پیش";
            if (days < 365)  return $"{days / 30} ماه پیش";
            return $"{days / 365} سال پیش";
        }

        /// <summary>«۲ ساعت دیگر»، «فردا ۰۹:۰۰»، «گذشته»</summary>
        public static string Until(DateTime? date, TimeSpan? time, DateTime now)
        {
            var at = At(date, time);
            if (!at.HasValue) return "";
            var diff = at.Value - now;
            if (diff.TotalMinutes < 0)
            {
                var ago = Ago(date, time, now);
                return ago == "همین حالا" ? "همین حالا" : $"گذشته — {ago}";
            }
            if (diff.TotalMinutes < 60) return $"{Math.Max(1, (int)diff.TotalMinutes)} دقیقه دیگر";
            var days = (at.Value.Date - now.Date).Days;
            if (days == 0) return $"امروز {Time(time)}";
            if (days == 1) return $"فردا {Time(time)}";
            if (days < 7)  return $"{WeekDay(at.Value.DayOfWeek)} {Time(time)}";
            return $"{ShortDate(at)} {Time(time)}";
        }

        /// <summary>چند روز است کار باز مانده؛ برای رنگِ «کهنگی».</summary>
        public static int AgeDays(TaskModel t, DateTime now)
            => t.STDATE.HasValue ? Math.Max(0, (now.Date - t.STDATE.Value.Date).Days) : 0;

        /// <summary>fresh / aging / stale — فقط برای کارهای باز معنا دارد.</summary>
        public static string AgeClass(TaskModel t, DateTime now, int staleDays)
        {
            if (t.STATUS != 1) return "";
            var d = AgeDays(t, now);
            return d >= staleDays ? "stale" : d >= 7 ? "aging" : "fresh";
        }

        /// <summary>دو حرفِ اولِ دو کلمه‌ی اولِ نام، بدون عنوان‌هایی مثل «آقای».</summary>
        public static string Initials(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "؟";
            var skip = new HashSet<string> { "آقای", "آقا", "خانم", "دکتر", "مهندس", "شرکت", "سرکار", "جناب" };
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => !skip.Contains(w))
                            .ToList();
            if (words.Count == 0) words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            return words.Count == 1
                ? words[0][..1]
                : $"{words[0][..1]}‌{words[1][..1]}";
        }

        /// <summary>
        /// رنگِ ثابت برای هر نام. string.GetHashCode در .NET در هر اجرا فرق
        /// می‌کند، پس از یک هشِ ساده‌ی قطعی استفاده می‌شود تا رنگِ هر نفر عوض نشود.
        /// </summary>
        public static int Hue(string? name)
        {
            unchecked
            {
                int h = 17;
                foreach (var ch in name ?? "") h = h * 31 + ch;
                return Math.Abs(h % 360);
            }
        }

        public static string AvatarStyle(string? name)
        {
            var h = Hue(name);
            return $"--atm-av-h:{h};";
        }

        public static string StatusName(int s) => s switch
        {
            1 => "انجام نشده",
            2 => "انجام شده",
            3 => "لغو شده",
            _ => "نامشخص"
        };

        /// <summary>نام نوع سند؛ همان فهرستِ ثابتِ AutomationApiService.</summary>
        public static string DocTypeName(int? skid, IEnumerable<DocumentTypeLookupModel>? types)
        {
            if (!skid.HasValue) return "";
            return types?.FirstOrDefault(x => x.ID == skid.Value)?.NAME ?? $"سند نوع {skid}";
        }
    }
}
