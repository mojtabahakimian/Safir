using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Automation
{
    public class MessageModel
    {
        public long IDNUM { get; set; }
        public int PERSONEL { get; set; } // گیرنده
        [Required(ErrorMessage = "متن پیام الزامی است.")]
        [MaxLength(2047)]
        public string? PAYAM { get; set; }
        public int STATUS { get; set; } // 1: مشاهده نشده, 2: مشاهده شده
        public DateTime? STDATE { get; set; }
        public TimeSpan? STTIME { get; set; }
        public string? USERNAME { get; set; } // فرستنده
        public string? COMP_COD { get; set; } // کد حساب مرتبط
        public string? NAME { get; set; } // نام مرتبط (از Join)
        public DateTime? CRT { get; set; } // زمان ایجاد رکورد
        public int? UID { get; set; } // کد کاربر ایجاد کننده
    }

    // مدل کمکی برای درخواست ارسال پیام
    public class MessageSendRequest
    {
        [Required(ErrorMessage = "حداقل یک گیرنده باید انتخاب شود.")]
        [MinLength(1, ErrorMessage = "حداقل یک گیرنده باید انتخاب شود.")]
        public List<int> RecipientUserIds { get; set; } = new List<int>();

        [Required(ErrorMessage = "متن پیام الزامی است.")]
        [MaxLength(2047, ErrorMessage = "متن پیام بیش از حد طولانی است.")]
        public string? MessageText { get; set; }
        public string? CompCod { get; set; } // کد حساب مرتبط (اختیاری)
    }
}