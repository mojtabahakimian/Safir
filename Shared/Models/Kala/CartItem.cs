using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class CartItem
    {
        public string ItemCode { get; set; } = string.Empty;
        public string? ItemName { get; set; }
        public int SelectedUnitCode { get; set; }
        public string? SelectedUnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal PricePerUnit { get; set; } // قیمت واحد پس از ویرایش احتمالی

        // قیمت کل ردیف قبل از هرگونه تخفیف
        public decimal TotalPriceBeforeDiscount => Quantity * PricePerUnit;

        // درصد تخفیف اصلی (N_KOL)
        public double? DiscountPercent { get; set; }

        // مبلغ تخفیف محاسبه شده فقط بر اساس DiscountPercent (برای نمایش در گرید کلاینت)
        public decimal LineDiscountAmount => Math.Round(((decimal)(DiscountPercent ?? 0) * TotalPriceBeforeDiscount) / 100m);

        // قیمت کل نهایی ردیف *بعد* از اعمال فقط DiscountPercent (برای نمایش در گرید کلاینت)
        // توجه: این با MABL_K در INVO_LST که قبل از تخفیف است، فرق دارد.
        public decimal TotalPriceAfterDiscount => TotalPriceBeforeDiscount - LineDiscountAmount;

        public int AnbarCode { get; set; }

        // نگهداری کل آبجکت DTO برای دسترسی به سایر اطلاعات در صورت نیاز
        public ItemDisplayDto? SourceItem { get; set; }

        // سازنده پیش‌فرض
        public CartItem() { }

        // سازنده کمکی (اختیاری)
        public CartItem(ItemDisplayDto item, decimal quantity, TCOD_VAHEDS? selectedUnit, int anbarCode, decimal pricePerUnitOverride, double? discountPercent)
        {
            SourceItem = item;
            ItemCode = item.CODE;
            ItemName = item.NAME;
            Quantity = quantity;
            SelectedUnitCode = selectedUnit?.CODE ?? item.VahedCode;
            SelectedUnitName = selectedUnit?.NAMES ?? item.VahedName;
            PricePerUnit = pricePerUnitOverride;
            AnbarCode = anbarCode;
            DiscountPercent = discountPercent;
        }
    }
}
