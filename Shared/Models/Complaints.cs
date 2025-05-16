using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Complaints
{
    public class ComplaintFormDto
    {
        [Required(ErrorMessage = "نام الزامی است.")]
        [MaxLength(100, ErrorMessage = "نام نمی‌تواند بیش از 100 کاراکتر باشد.")]
        public string CustomerFirstName { get; set; }

        [Required(ErrorMessage = "نام خانوادگی الزامی است.")]
        [MaxLength(100, ErrorMessage = "نام خانوادگی نمی‌تواند بیش از 100 کاراکتر باشد.")]
        public string CustomerLastName { get; set; }

        [Required(ErrorMessage = "تلفن همراه الزامی است.")]
        [MaxLength(20, ErrorMessage = "تلفن همراه نمی‌تواند بیش از 20 کاراکتر باشد.")]
        [Phone(ErrorMessage = "فرمت تلفن همراه صحیح نیست.")]
        public string CustomerMobile { get; set; }

        [MaxLength(100, ErrorMessage = "ایمیل نمی‌تواند بیش از 100 کاراکتر باشد.")]
        [EmailAddress(ErrorMessage = "فرمت ایمیل صحیح نیست.")]
        public string? CustomerEmail { get; set; }

        [MaxLength(500, ErrorMessage = "آدرس نمی‌تواند بیش از 500 کاراکتر باشد.")]
        public string? CustomerAddress { get; set; }

        [MaxLength(100)]
        public string? ProductTypeComplaint { get; set; } // e.g., پنیر پیتزا

        [MaxLength(100)]
        public string? PizzaType { get; set; } // e.g., موزارلا، پروسس

        [MaxLength(50)]
        public string? ProductWeight { get; set; }

        public DateTime? ProductionDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        [MaxLength(50)]
        public string? ProductCode { get; set; }

        [MaxLength(100)]
        public string? OtherDairyProductName { get; set; }

        [MaxLength(200)]
        public string? PurchaseLocation { get; set; }
        public DateTime? PurchaseDate { get; set; }

        [MaxLength(100)]
        public string? BatchNumber { get; set; }
        public DateTime? ComplaintRegisteredDate { get; set; }

        // Complaint Types
        public bool IsComplaintType_TasteSmell { get; set; }
        public bool IsComplaintType_Packaging { get; set; }
        public bool IsComplaintType_WrongExpiryDate { get; set; }
        public bool IsComplaintType_NonConformity { get; set; }
        public bool IsComplaintType_ForeignObject { get; set; }
        public bool IsComplaintType_AbnormalTexture { get; set; }
        public bool IsComplaintType_Mold { get; set; }
        public bool IsComplaintType_Other { get; set; }

        [MaxLength(500)]
        public string? ComplaintType_OtherDescription { get; set; }

        [Required(ErrorMessage = "توضیحات شکایت الزامی است.")]
        public string ComplaintDescription { get; set; }

        public bool CustomerActionTaken { get; set; }
        public string? CustomerActionDescription { get; set; }

        // Requested Resolution
        public bool RequestedResolution_Refund { get; set; }
        public bool RequestedResolution_Replacement { get; set; }
        public bool RequestedResolution_FurtherInvestigation { get; set; }
        public string? RequestedResolution_Explanation { get; set; }

        [Required(ErrorMessage = "تأیید صحت اطلاعات الزامی است.")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "لطفاً صحت اطلاعات را تأیید کنید.")]
        public bool InformationConfirmed { get; set; }
    }
}