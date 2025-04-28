// File: Shared/Models/Kharid/ProformaPrintDto.cs
using System;
using System.Collections.Generic;

namespace Safir.Shared.Models.Kharid
{
    // Main DTO for the entire report data
    public class ProformaPrintDto
    {
        public ProformaPrintHeaderDto Header { get; set; } = new();
        public List<ProformaPrintLineDto> Lines { get; set; } = new();
        // Add any other data needed for the report, like footer summaries calculated on server
        public decimal TotalAmountBeforeDiscount { get; set; }
        public decimal TotalDiscountAmount { get; set; } // N_MOIN Sum
        public decimal TotalVatAmount { get; set; } // MBAA
        public decimal TotalAmountPayable { get; set; }
        public string? AmountInWords { get; set; } // Optional: Requires Toman library or similar
    }

    // DTO for Header Information
    public class ProformaPrintHeaderDto
    {
        public double NUMBER { get; set; } // شماره پیش فاکتور
        public long? DATE_N { get; set; } // تاریخ
        public string? CUST_NO { get; set; } // کد مشتری
        public string? CustomerName { get; set; } // نام خریدار (از CUST_HESAB)
        public string? CustomerAddress { get; set; } // آدرس (از CUST_HESAB)
        public string? CustomerTel { get; set; } // تلفن (از CUST_HESAB)
        public string? MOLAH { get; set; } // ملاحظات سربرگ
        public string? SHARAYET { get; set; } // شرایط
        public decimal? MABL_HAZ { get; set; } // هزینه حمل / خدمات
        public decimal? TAKHFIF { get; set; } // تخفیف کلی سربرگ
                                              // Add other header fields from HEAD_LST or joined tables if needed for display
                                              // e.g., Salesperson Name, etc.
    }

    // DTO for Line Item Information
    public class ProformaPrintLineDto
    {
        public short RADIF { get; set; } // ردیف (Assuming short is sufficient, adjust if needed)
        public string? CODE { get; set; } // کد کالا
        public string? ItemName { get; set; } // شرح کالا (از STUF_DEF)
        public string? UnitName { get; set; } // واحد کالا (از TCOD_VAHEDS)
        public decimal MEGH { get; set; } // مقدار (با واحد انتخابی)
        public decimal MEGHk { get; set; } // مقدار کل (با واحد پایه) - Optional for display
        public decimal MABL { get; set; } // فی (قیمت واحد انتخابی)
        public decimal IMBAA { get; set; }
        public double N_KOL { get; set; } // درصد تخفیف
        public double TKHN { get; set; } // درصد تخفیف نقدی (معمولا صفر)
        public decimal MABL_K { get; set; } // مبلغ کل (قبل از تخفیف) = MEGH * MABL
        public decimal DiscountAmount { get; set; } // مبلغ تخفیف محاسبه شده (N_MOIN)
        public decimal NetAmount => MABL_K - DiscountAmount; // مبلغ پس از تخفیف
                                                             // Add other line fields if needed
    }
}