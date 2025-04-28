using Safir.Shared.Models.Kala;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class ProformaLineDto
    {
        // اطلاعات ضروری از CartItem یا ItemDisplayDto
        public int AnbarCode { get; set; }         // کد انبار
        public string ItemCode { get; set; } = string.Empty;       // کد کالا
        public int SelectedUnitCode { get; set; }  // کد واحد انتخابی
        public decimal Quantity { get; set; }      // مقدار سفارش داده شده
        public decimal PricePerUnit { get; set; }  // قیمت واحد (مبلغ)
        public double? DiscountPercent { get; set; } // درصد تخفیف (N_KOL) - nullable
        public double? CashDiscountPercent { get; set; } // درصد تخفیف نقدی (TKHN) - nullable
        public string? Notes { get; set; }         // ملاحظات سطر (MANDAH) - nullable


        // سازنده برای تبدیل آسان از CartItem (اختیاری ولی مفید)
        public static ProformaLineDto FromCartItem(CartItem cartItem, int anbarCode)
        {
            return new ProformaLineDto
            {
                AnbarCode = anbarCode, // باید از جای دیگری بیاید
                ItemCode = cartItem.ItemCode,
                SelectedUnitCode = cartItem.SelectedUnitCode,
                Quantity = cartItem.Quantity,
                PricePerUnit = cartItem.PricePerUnit,
                CashDiscountPercent = 0, // مقدار پیش فرض یا از جای دیگر
                Notes = null // یا مقدار پیش فرض
            };
        }

        // Default constructor needed for model binding/deserialization
        public ProformaLineDto() { }
    }
}
