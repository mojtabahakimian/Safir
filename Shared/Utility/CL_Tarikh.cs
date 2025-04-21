using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Utility
{
    public static class CL_Tarikh
    {
        public static class PersianCalendarHelper
        {
            public static long GetCurrentPersianDateAsLong()
            {
                PersianCalendar pc = new PersianCalendar();
                // استفاده از DateTime.Now برای سازگاری بیشتر با GetDate() در SQL Server
                // اگر سرور در منطقه زمانی متفاوتی است، DateTime.UtcNow و تبدیل مناسب ممکن است لازم باشد.
                DateTime now = DateTime.Now;
                int year = pc.GetYear(now);
                int month = pc.GetMonth(now);
                int day = pc.GetDayOfMonth(now);
                // فرمت YYYYMMDD
                return (long)(year * 10000) + (month * 100) + day;
            }

            // تابع کمکی برای تبدیل تاریخ میلادی به عدد Long شمسی YYYYMMDD
            public static long ConvertToPersianDateLong(DateTime dt)
            {
                PersianCalendar pc = new PersianCalendar();
                int year = pc.GetYear(dt);
                int month = pc.GetMonth(dt);
                int day = pc.GetDayOfMonth(dt);
                return (long)(year * 10000) + (month * 100) + day;
            }
        }
    }
}
