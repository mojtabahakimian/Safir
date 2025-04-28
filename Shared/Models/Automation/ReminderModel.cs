using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Automation
{
    public class ReminderModel
    {
        public long IDNUM { get; set; }
        public int PERSONEL { get; set; } // گیرنده
        public string? COMP_COD { get; set; } // کد حساب مرتبط
        public string? NAME { get; set; } // نام مرتبط (از Join)
        [Required(ErrorMessage = "متن یادآوری الزامی است.")]
        [MaxLength(2047)]
        public string? PAYAM { get; set; }
        public int STATUS { get; set; } // 1: در جریان, 2: تمام شده, 3: لغو شده
        [Required(ErrorMessage = "تاریخ یادآوری الزامی است.")]
        public DateTime? STDATE { get; set; }
        [Required(ErrorMessage = "زمان یادآوری الزامی است.")]
        public TimeSpan? STTIME { get; set; }
        // SMSOK حذف شد
        public string? USERNAME { get; set; } // ثبت کننده
        public DateTime? CTDATE { get; set; } // تاریخ ثبت سیستم
        public DateTime? CTTIME { get; set; } // زمان ثبت سیستم
        public DateTime? CRT { get; set; } // زمان ایجاد رکورد
        public int? UID { get; set; } // کد کاربر ایجاد کننده
    }

    // مدل کمکی برای درخواست ایجاد یادآوری
    public class ReminderCreateRequest
    {
        [Required(ErrorMessage = "حداقل یک گیرنده باید انتخاب شود.")]
        [MinLength(1, ErrorMessage = "حداقل یک گیرنده باید انتخاب شود.")]
        public List<int> RecipientUserIds { get; set; } = new List<int>();

        [Required(ErrorMessage = "متن یادآوری الزامی است.")]
        [MaxLength(2047, ErrorMessage = "متن یادآوری بیش از حد طولانی است.")]
        public string? ReminderText { get; set; }
        public string? CompCod { get; set; } // کد حساب مرتبط (اختیاری)

        [Required(ErrorMessage = "تاریخ یادآوری الزامی است.")]
        public DateTime? ReminderDate { get; set; }

        [Required(ErrorMessage = "زمان یادآوری الزامی است.")]
        public TimeSpan? ReminderTime { get; set; }
        // SendSms حذف شد
    }
}