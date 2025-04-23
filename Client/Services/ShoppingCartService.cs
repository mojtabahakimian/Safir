// مسیر: Safir.Client/Services/ShoppingCartService.cs
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Visitory; // برای VISITOR_CUSTOMERS
using System;
using System.Collections.Generic;
using System.Linq;

namespace Safir.Client.Services
{
    public class ShoppingCartService
    {
        public VISITOR_CUSTOMERS? CurrentCustomer { get; private set; }
        public List<CartItem> Items { get; private set; } = new List<CartItem>();

        // رویداد برای اطلاع‌رسانی تغییرات در سبد
        public event Action? CartChanged;

        private readonly List<TCOD_VAHEDS>? _availableUnits; // نگهداری لیست واحدها

        // دریافت لیست واحدها از طریق سازنده (از کامپوننت والد یا سرویس دیگر)
        // یا بارگذاری آن در اینجا با تزریق LookupApiService
        public ShoppingCartService(LookupApiService lookupService)
        {
            // در اینجا می‌توانیم واحدها را بارگذاری کنیم، اما بهتر است
            // واحدها یکبار در کامپوننت اصلی بارگذاری و به این سرویس پاس داده شوند
            // یا به عنوان یک وابستگی جداگانه تزریق شوند.
            // فعلا فرض می‌کنیم در متد AddItem واحد مربوطه را می‌گیریم.
        }

        // یا سازنده بدون وابستگی اگر واحدها جای دیگری مدیریت می‌شوند
        // public ShoppingCartService() { }

        public decimal GetItemQuantity(string itemCode, int unitCode)
        {
            var item = Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);
            return item?.Quantity ?? 0; // اگر آیتم پیدا نشد، تعداد صفر است
        }
        public void SetCustomer(VISITOR_CUSTOMERS? customer)
        {
            if (CurrentCustomer != customer) // فقط اگر مشتری تغییر کرد یا اولین بار است
            {
                // اگر مشتری جدیدی انتخاب می‌شود، سبد قبلی را پاک کن
                if (customer != null && CurrentCustomer != null && Items.Any())
                {
                    ClearCartInternal(); // پاک کردن سبد بدون مشتری
                }
                CurrentCustomer = customer;
                NotifyCartChanged();
            }
        }

        public void AddItem(ItemDisplayDto item, int quantity, int unitCode, List<TCOD_VAHEDS> availableUnits)
        {
            if (item == null || quantity <= 0) return;

            var selectedUnit = availableUnits?.FirstOrDefault(u => u.CODE == unitCode);
            string? selectedUnitName = selectedUnit?.NAMES ?? item.VahedName; // نام واحد

            // TODO: در آینده، بررسی کنید آیا قیمت باید بر اساس واحد انتخابی تعدیل شود؟
            // فعلا از MABL_F (فی عمده) به عنوان قیمت واحد استفاده می‌کنیم.
            decimal pricePerUnit = item.MABL_F;

            // بررسی اینکه آیا این کالا با همین واحد قبلا اضافه شده؟
            var existingItem = Items.FirstOrDefault(i => i.ItemCode == item.CODE && i.SelectedUnitCode == unitCode);

            if (existingItem != null)
            {
                // فقط تعداد را افزایش بده
                existingItem.Quantity += quantity;
            }
            else
            {
                // آیتم جدید به لیست اضافه کن
                // استفاده از سازنده CartItem برای مقداردهی اولیه
                var newItem = new CartItem
                {
                    SourceItem = item,
                    ItemCode = item.CODE,
                    ItemName = item.NAME,
                    Quantity = quantity,
                    SelectedUnitCode = unitCode,
                    SelectedUnitName = selectedUnitName,
                    PricePerUnit = pricePerUnit
                };
                Items.Add(newItem);
            }
            NotifyCartChanged();
        }

        public void RemoveItem(string itemCode, int unitCode)
        {
            var itemToRemove = Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);
            if (itemToRemove != null)
            {
                Items.Remove(itemToRemove);
                NotifyCartChanged();
            }
        }

        public void UpdateQuantity(string itemCode, int unitCode, decimal newQuantity)
        {
            var itemToUpdate = Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);
            if (itemToUpdate != null)
            {
                if (newQuantity > 0)
                {
                    itemToUpdate.Quantity = newQuantity;
                }
                else
                {
                    // اگر تعداد صفر یا کمتر شد، آیتم را حذف کن
                    Items.Remove(itemToUpdate);
                }
                NotifyCartChanged();
            }
        }

        public decimal GetTotal()
        {
            return Items.Sum(item => item.TotalPrice);
        }

        public void ClearCart()
        {
            ClearCartInternal();
            CurrentCustomer = null; // مشتری را هم پاک کن
            NotifyCartChanged();
        }

        private void ClearCartInternal()
        {
            Items.Clear();
            // NotifyCartChanged(); // در ClearCart اصلی انجام می‌شود
        }


        private void NotifyCartChanged()
        {
            CartChanged?.Invoke();
        }
    }
}