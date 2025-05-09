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

        public int VahedCode { get; set; }
        public string VahedName { get; set; }
        public double Nesbat { get; set; }
        public decimal PricePerUnitBeforeDiscount { get; set; } // قیمت واحد انتخابی، *قبل* از تخفیف
        public decimal PricePerUnitAfterDiscount => PricePerUnitBeforeDiscount * (1 - (decimal)(DiscountPercent / 100.0));
        public decimal TotalRowPrice => PricePerUnitAfterDiscount * Quantity;
        public decimal QuantityInBaseUnit => Quantity * (decimal)Nesbat;

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


        // این قبلا TotalRowPrice نام داشت، برای وضوح بیشتر تغییر نام داده شد
        public decimal TotalPriceAfterLineDiscountAndNesbat => PricePerUnitAfterDiscount * Quantity * (decimal)Nesbat;

        // قیمت کل ردیف قبل از هرگونه تخفیف (محاسبه قبلی)
        // این همان مقداری است که قبلاً به عنوان مبلغ کل ردیف در نظر گرفته می‌شد (تعداد کارتن * قیمت کارتن)
        public decimal TotalPriceForSelectedUnitQuantity => Quantity * PricePerUnit;

        // ********************************************************************
        // ******** پراپرتی جدید برای نمایش مبلغ کل ردیف با احتساب نسبت ********
        // ********************************************************************
        /// <summary>
        /// مبلغ کل واقعی ردیف ( قیمت واحد انتخابی * تعداد واحد انتخابی * نسبت تبدیل ) قبل از تخفیف خطی
        /// یا ( قیمت واحد انتخابی * مقدار کل با واحد پایه )
        /// </summary>
        public decimal CalculatedRowTotalPriceBeforeLineDiscount => PricePerUnit * Quantity * (decimal)Nesbat;
        // ********************************************************************

        // مبلغ تخفیف خطی محاسبه شده بر اساس CalculatedRowTotalPriceBeforeLineDiscount
        public decimal LineDiscountAmountCalculatedOnFullPrice => Math.Round(((decimal)(DiscountPercent ?? 0) * CalculatedRowTotalPriceBeforeLineDiscount) / 100m);

        // قیمت کل نهایی ردیف *بعد* از اعمال تخفیف خطی روی قیمت کل واقعی
        public decimal FinalRowPriceAfterLineDiscount => CalculatedRowTotalPriceBeforeLineDiscount - LineDiscountAmountCalculatedOnFullPrice;


        // سازنده پیش‌فرض
        public CartItem()
        {
            // مقادیر پیش‌فرض اگر لازم است
            VahedName = string.Empty; // برای جلوگیری از نال بودن در نمایش اولیه
        }


        // سازنده کمکی (اختیاری) - مطمئن شوید Nesbat هم مقداردهی می‌شود اگر از این سازنده استفاده می‌کنید
        public CartItem(ItemDisplayDto item, decimal quantity, TCOD_VAHEDS? selectedUnit, int anbarCode, decimal pricePerUnitOverride, double? discountPercent, double nesbat)
        {
            SourceItem = item;
            ItemCode = item.CODE;
            ItemName = item.NAME;
            Quantity = quantity;
            SelectedUnitCode = selectedUnit?.CODE ?? item.VahedCode;
            SelectedUnitName = selectedUnit?.NAMES ?? item.VahedName ?? string.Empty;
            PricePerUnit = pricePerUnitOverride; // قیمت واحد انتخابی
            PricePerUnitBeforeDiscount = pricePerUnitOverride; // قیمت واحد انتخابی قبل از تخفیف خطی
            AnbarCode = anbarCode;
            DiscountPercent = discountPercent;
            Nesbat = nesbat; // مقداردهی نسبت واحد

            // مقادیر پیش‌فرض برای فیلدهای دیگر اگر لازم است
            VahedCode = SelectedUnitCode;
            VahedName = SelectedUnitName;
        }
    }
}
