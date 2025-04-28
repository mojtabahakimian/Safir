using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Safir.Shared.Utility
{
    public static class CL_Tarikh
    {
        // --- توابع موجود ---
        public static long GetCurrentPersianDateAsLong()
        {
            PersianCalendar pc = new PersianCalendar();
            DateTime now = DateTime.Now; // یا GetdateFromServer() اگر دارید
            return (long)(pc.GetYear(now) * 10000) + (pc.GetMonth(now) * 100) + pc.GetDayOfMonth(now);
        }

        public static long? ConvertToPersianDateLong(DateTime? dt)
        {
            if (!dt.HasValue) return null;
            try
            {
                PersianCalendar pc = new PersianCalendar();
                int year = pc.GetYear(dt.Value);
                int month = pc.GetMonth(dt.Value);
                int day = pc.GetDayOfMonth(dt.Value);
                return (long)(year * 10000) + (month * 100) + day;
            }
            catch { return null; }
        }

        public static long? ConvertToPersianDateLong(string? gregorianDateString)
        {
            if (!IsValidPersianDate(gregorianDateString)) return null;
            string cleanedDate = gregorianDateString.Replace("/", "");
            if (long.TryParse(cleanedDate, out long result))
            {
                return result;
            }
            return null;
        }

        public static string FormatShamsiDateFromLong(long? dateLong)
        {
            if (!dateLong.HasValue || dateLong.Value <= 0) return string.Empty;
            try
            {
                string d = dateLong.Value.ToString();
                if (d.Length == 8)
                {
                    int year = int.Parse(d.Substring(0, 4));
                    int month = int.Parse(d.Substring(4, 2));
                    int day = int.Parse(d.Substring(6, 2));
                    return $"{year:D4}/{month:D2}/{day:D2}";
                }
                return d; // Return as is if not 8 digits
            }
            catch { return dateLong.Value.ToString(); }
        }

        public static bool IsValidPersianDate(string? persianDate)
        {
            if (string.IsNullOrWhiteSpace(persianDate)) return false;
            string cleanedDate = persianDate.Replace("/", "");
            if (cleanedDate.Length != 8 || !long.TryParse(cleanedDate, out _)) return false;
            try
            {
                int year = int.Parse(cleanedDate.Substring(0, 4));
                int month = int.Parse(cleanedDate.Substring(4, 2));
                int day = int.Parse(cleanedDate.Substring(6, 2));
                PersianCalendar pc = new PersianCalendar();
                pc.ToDateTime(year, month, day, 0, 0, 0, 0); // Throws exception if invalid
                return true;
            }
            catch { return false; }
        }

        // --- توابع جدید برای تبدیل از دیتابیس به مدل ---

        public static DateTime? ConvertToDateTimeFromPersianLong(long? persianDateLong)
        {
            if (!persianDateLong.HasValue || persianDateLong.Value <= 0) return null;
            string dateStr = persianDateLong.Value.ToString();
            if (dateStr.Length != 8) return null;

            try
            {
                int year = int.Parse(dateStr.Substring(0, 4));
                int month = int.Parse(dateStr.Substring(4, 2));
                int day = int.Parse(dateStr.Substring(6, 2));
                PersianCalendar pc = new PersianCalendar();
                // Ensure the date is valid within the Persian calendar context before converting
                if (month >= 1 && month <= 12 && day >= 1 && day <= pc.GetDaysInMonth(year, month))
                {
                    return pc.ToDateTime(year, month, day, 0, 0, 0, 0);
                }
                // Log warning for invalid date parts?
                Console.WriteLine($"Warning: Invalid Persian date components derived from long: {persianDateLong}");
                return null;
            }
            catch (ArgumentOutOfRangeException ex) // Catch specific exception from ToDateTime
            {
                Console.WriteLine($"Error converting Persian long {persianDateLong} to DateTime: {ex.Message}");
                return null;
            }
            catch (Exception ex) // Catch other potential parsing errors
            {
                Console.WriteLine($"General error converting Persian long {persianDateLong} to DateTime: {ex.Message}");
                return null;
            }
        }

        public static TimeSpan? ConvertToTimeSpanFromTimeInt(int? timeInt)
        {
            if (!timeInt.HasValue || timeInt < 0 || timeInt > 2359) return null;
            // Ensure 4 digits (e.g., 930 -> "0930", 0 -> "0000")
            string timeStr = timeInt.Value.ToString("D4");

            try
            {
                int hour = int.Parse(timeStr.Substring(0, 2));
                int minute = int.Parse(timeStr.Substring(2, 2));
                // Validate hour and minute ranges
                if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                {
                    return new TimeSpan(hour, minute, 0);
                }
                Console.WriteLine($"Warning: Invalid time components derived from int: {timeInt}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting time int {timeInt} to TimeSpan: {ex.Message}");
                return null; // Error during parsing
            }
        }

        // --- تابع کمکی برای تبدیل DateTime/TimeSpan به int زمان (HHmm) ---
        public static int? ConvertTimeToInt(TimeSpan? time)
        {
            if (!time.HasValue) return null;
            // Ensures HHmm format (e.g., 9:05 AM -> 905, 11:30 PM -> 2330)
            return time.Value.Hours * 100 + time.Value.Minutes;
        }
        public static int? ConvertTimeToInt(DateTime? dateTime)
        {
            if (!dateTime.HasValue) return null;
            return dateTime.Value.Hour * 100 + dateTime.Value.Minute;
        }
    }
}