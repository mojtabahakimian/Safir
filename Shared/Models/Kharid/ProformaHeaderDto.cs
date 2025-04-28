using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class ProformaHeaderDto
    {
        [Required(ErrorMessage = "مشتری انتخاب نشده است.")]
        public string CustomerHesCode { get; set; } = string.Empty; // CUST_NO

        public long? Date { get; set; }          // DATE_N (تاریخ شمسی به صورت long)
        public string? Notes { get; set; }         // MOLAH
        public string? Conditions { get; set; }    // SHARAYET
        public int? CustomerKindCode { get; set; } // CUST_KIND
        public int? DepartmentCode { get; set; }   // DEPATMAN
        public int? PaymentTermId { get; set; }    // MODAT_PPID (نحوه پرداخت) - nullable
        public double? AgreedDuration { get; set; } // MAS (مدت توافق) - nullable
        public int? PriceListId { get; set; }      // PEPID (اعلامیه قیمت) - nullable
        public int? DiscountListId { get; set; }   // PEID (اعلامیه تخفیف) - nullable
        public bool ApplyVat { get; set; }        // TICMBAA
        public bool CalculateAward { get; set; }  // JAY
        public decimal? ShippingCost { get; set; } // MABL_HAZ (مبلغ خدمات/هزینه حمل) - nullable
        public decimal? TotalDiscount { get; set; } // TAKHFIF (تخفیف کلی دستی سربرگ) - nullable

        // فیلدهای مربوط به امضا (SGN) در اینجا لازم نیستند، سمت سرور مدیریت می‌شوند
    }
}
