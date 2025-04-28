using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Automation
{
    public class EventModel
    {
        public long IDNUM { get; set; } // Task ID
        public int IDD { get; set; } // Event ID

        [Required(ErrorMessage = "شرح رویداد الزامی است.")]
        [MaxLength(4000)]
        public string? EVENTS { get; set; } // شرح رویداد

        public DateTime? STDATE { get; set; } // تاریخ ثبت رویداد
        public TimeSpan? STTIME { get; set; } // زمان ثبت رویداد
        public string? USERNAME { get; set; } // کاربر ثبت کننده
        public TimeSpan? SUMTIME { get; set; } // زمان صرف شده

        // فیلدهای سند مرتبط (بدون فایل ضمیمه)
        public int? skid { get; set; }
        public long? num { get; set; }
        // pic و FXTYPE حذف شدند
    }
}