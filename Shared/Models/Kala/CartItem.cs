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
        public decimal PricePerUnit { get; set; } // قیمت واحد بر اساس واحد انتخابی (فعلا قیمت واحد پیش‌فرض را در نظر می‌گیریم)
        public decimal TotalPrice => Quantity * PricePerUnit;

        // نگهداری کل آبجکت DTO برای دسترسی به سایر اطلاعات در صورت نیاز
        public ItemDisplayDto? SourceItem { get; set; }

        // سازنده برای سهولت ایجاد آیتم
        public CartItem(ItemDisplayDto item, int quantity, TCOD_VAHEDS? selectedUnit)
        {
            SourceItem = item;
            ItemCode = item.CODE;
            ItemName = item.NAME;
            Quantity = quantity;
            SelectedUnitCode = selectedUnit?.CODE ?? item.VahedCode; // اگر واحد انتخابی null بود، از واحد پیش‌فرض استفاده کن
            SelectedUnitName = selectedUnit?.NAMES ?? item.VahedName;

            // TODO: منطق تعیین قیمت بر اساس واحد انتخابی
            // فعلا فرض می‌کنیم قیمت همیشه بر اساس واحد پیش‌فرض (MABL_F) است
            // در آینده باید بررسی شود اگر واحد انتخابی متفاوت است، قیمت متناسب تغییر کند یا خیر
            PricePerUnit = item.MABL_F;
        }

        // سازنده پیش‌فرض برای سریال‌سازی احتمالی
        public CartItem() { }
    }
}
