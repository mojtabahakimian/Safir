using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Automation
{
    public class TaskModel
    {
        public long IDNUM { get; set; }
        public int? GR { get; set; }
        [Required(ErrorMessage = "انتخاب مجری الزامی است.")]
        public int PERSONEL { get; set; } // کد کاربر مجری
        [Required(ErrorMessage = "شرح وظیفه الزامی است.")]
        [MaxLength(4000, ErrorMessage = "شرح وظیفه بیش از حد طولانی است.")]
        public string? TASK { get; set; } // متن اصلی وظیفه

        [Required(ErrorMessage = "انتخاب اولویت الزامی است.")]
        public int PERIORITY { get; set; } // 1: فوری, 2: معمولی
        [Required(ErrorMessage = "انتخاب وضعیت الزامی است.")]
        public int STATUS { get; set; } // 1: انجام نشده, 2: انجام شده, 3: لغو شده

        // تاریخ و زمان‌ها (Nullable برای سازگاری بهتر)
        public DateTime? STDATE { get; set; } // تاریخ ارجاع
        public TimeSpan? STTIME { get; set; } // زمان ارجاع
        public DateTime? ENDATE { get; set; } // تاریخ انجام
        public TimeSpan? ENTIME { get; set; } // زمان انجام
        public DateTime? CTIM { get; set; }   // زمان ایجاد رکورد در دیتابیس

        // اطلاعات مرتبط
        [Required(ErrorMessage = "انتخاب مشتری مرتبط الزامی است.")] // <<<=== اضافه شد
        public string? COMP_COD { get; set; } // کد حساب مرتبط (Hes)
        public string? NAME { get; set; } // نام مرتبط (از Join با CUST_HESAB خوانده می‌شود)

        // اطلاعات کاربر ثبت کننده
        public string? USERNAME { get; set; }
        public int? USERCO { get; set; }

        // اطلاعات سند مرتبط (بدون نیاز به باز کردن سند)
        public int? skid { get; set; }   // نوع سند
        public long? num { get; set; }    // شماره سند
        public int? tg { get; set; }

        // سایر فیلدها
        public TimeSpan? SUMTIME { get; set; } // زمان صرف شده
        public bool? SEE { get; set; }     // آیا توسط مجری دیده شده؟
        public DateTime? SEET { get; set; } // زمان مشاهده توسط مجری

        // فیلد کمکی برای Blazor UI
        [System.Text.Json.Serialization.JsonIgnore] // این فیلد سمت کلاینت استفاده میشه و نیازی به ارسال به/از سرور نداره
        public bool IsSelected { get; set; } = false;
    }
}