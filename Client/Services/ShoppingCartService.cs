using System.Text.Json;
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Visitory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models;
using Safir.Shared.Models.User_Model;

namespace Safir.Client.Services
{
    public class ShoppingCartService
    {
        private readonly UserStateApiService _stateApi;
        private readonly ILogger<ShoppingCartService> _logger;

        public VISITOR_CUSTOMERS? CurrentCustomer { get; private set; }
        public List<CartItem> Items { get; private set; } = new List<CartItem>();
        public int? CurrentAnbarCode { get; private set; } // این را نگه می‌داریم
        public event Action? CartChanged;

        //تنظیمات قیمتی مثل اعلامیه قیمت
        public LookupDto<int?> CustomerType { get; set; }
        public LookupDto<int?>? DepartmentValue { get; set; }
        public PaymentTermDto? PaymentTerm { get; set; }
        public int? AgreedDuration { get; set; }
        public PriceListDto? PriceList { get; set; }
        public DiscountListDto? DiscountList { get; set; }
        public double? SelectedGroupId { get; set; }
        public bool IsHistoricalMode { get; set; }

        private Task? _initializationTask; // <--- فیلد برای نگهداری تسک بارگذاری اولیه
        private bool _isInitialized = false; // <--- فلگ برای جلوگیری از اجرای مجدد منطق اصلی


        // سازنده به‌روز شده
        public ShoppingCartService(UserStateApiService stateApi, ILogger<ShoppingCartService> logger)
        {
            _stateApi = stateApi;
            _logger = logger;
            // InitializeCartFromLocalStorage(); // در OnInitializedAsync کامپوننت اصلی یا MainLayout فراخوانی شود
        }

        // این متد باید یکبار هنگام شروع برنامه فراخوانی شود
        // بهترین جا برای فراخوانی آن، متد OnInitializedAsync در MainLayout.razor یا App.razor است

        // این متد توسط MainLayout و سایر کامپوننت‌ها فراخوانی می‌شود
        public Task InitializeCartFromServerAsync()
        {
            // اگر تسک بارگذاری اولیه قبلاً ایجاد نشده، آن را ایجاد کن
            if (_initializationTask == null)
            {
                _logger.LogInformation("Creating and starting initialization task for ShoppingCartService.");
                _initializationTask = InitializeInternalAsync();
            }
            else
            {
                _logger.LogInformation("Returning existing initialization task for ShoppingCartService.");
            }
            return _initializationTask; // تسک موجود یا جدید را برگردان
        }
        private async Task InitializeInternalAsync()
        {
            // اگر قبلاً مقداردهی اولیه انجام شده، خارج شو
            if (_isInitialized)
            {
                _logger.LogInformation("ShoppingCartService already initialized. Skipping InitializeInternalAsync logic.");
                return;
            }

            _logger.LogInformation("InitializeInternalAsync started for ShoppingCartService.");
            try
            {
                var state = await _stateApi.GetStateAsync();
                if (state != null)
                {
                    CurrentCustomer = state.CurrentCustomer;
                    Items = state.CartItems ?? new List<CartItem>();
                    CustomerType = state.CustomerType ?? CustomerType;
                    DepartmentValue = state.DepartmentValue ?? DepartmentValue;
                    PaymentTerm = state.PaymentTerm ?? PaymentTerm;
                    AgreedDuration = state.AgreedDuration;
                    PriceList = state.PriceList;
                    DiscountList = state.DiscountList;
                    SelectedGroupId = state.SelectedGroupId;
                    CurrentAnbarCode = state.CurrentAnbarCode;
                    IsHistoricalMode = state.IsHistoricalMode;
                }
                _logger.LogInformation("Cart successfully initialized from server state. Items: {ItemCount}", Items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing cart from server.");
                CurrentCustomer = null;
                Items = new List<CartItem>();
            }
            finally
            {
                _isInitialized = true; // علامت‌گذاری به عنوان مقداردهی شده (حتی در صورت خطا برای جلوگیری از تلاش مجدد)
                _logger.LogInformation("InitializeInternalAsync finished for ShoppingCartService. Notifying cart changed.");
                NotifyCartChanged(); // برای به‌روزرسانی UI پس از بارگذاری
            }
        }

        public async Task SetCustomerAsync(VISITOR_CUSTOMERS? customer)
        {
            if (CurrentCustomer?.hes != customer?.hes)
            {
                if (customer != null && CurrentCustomer != null && Items.Any())
                {
                    await ClearCartAsync();
                }
                CurrentCustomer = customer;
                NotifyCartChanged();
                await PersistStateAsync();
            }
        }

        // متدهای دیگر مانند GetCurrentAnbarCode، GetItemQuantity، GetCartItem بدون تغییر باقی می‌مانند

        public async Task AddItemAsync(
            ItemDisplayDto item,
            decimal quantity,
            int unitCode,
            List<UnitInfo>? availableUnitsForThisItem,
            int anbarCode,
            decimal? priceOverride = null,
            double? discountPercent = null)
        {
            if (item == null || quantity <= 0) return;
            // ... (بقیه منطق AddItem مانند قبل با فرض اینکه nesbat را به درستی از availableUnitsForThisItem می‌گیرید) ...
            UnitInfo? selectedUnitInfo = availableUnitsForThisItem?.FirstOrDefault(u => u.VahedCode == unitCode);
            string? selectedUnitName = selectedUnitInfo?.VahedName ?? item.VahedName;
            double nesbat = selectedUnitInfo?.Nesbat ?? 1.0;
            decimal pricePerUnitToUse = priceOverride ?? item.MABL_F;

            var existingItem = Items.FirstOrDefault(i => i.ItemCode == item.CODE && i.SelectedUnitCode == unitCode && i.AnbarCode == anbarCode);

            if (existingItem != null)
            {
                existingItem.Quantity = quantity;
                existingItem.PricePerUnit = pricePerUnitToUse;
                existingItem.PricePerUnitBeforeDiscount = pricePerUnitToUse;
                existingItem.DiscountPercent = discountPercent;
                existingItem.Nesbat = nesbat;
                existingItem.SelectedUnitName = selectedUnitName;
            }
            else
            {
                var newItem = new CartItem
                {
                    SourceItem = item,
                    ItemCode = item.CODE,
                    ItemName = item.NAME,
                    Quantity = quantity,
                    SelectedUnitCode = unitCode,
                    SelectedUnitName = selectedUnitName,
                    PricePerUnit = pricePerUnitToUse,
                    PricePerUnitBeforeDiscount = pricePerUnitToUse,
                    AnbarCode = anbarCode,
                    DiscountPercent = discountPercent,
                    Nesbat = nesbat,
                    VahedCode = unitCode,
                    VahedName = selectedUnitName ?? string.Empty
                };
                Items.Add(newItem);
            }
            await PersistStateAsync();
            NotifyCartChanged();
        }

        public async Task UpdateQuantityAsync(
            string itemCode,
            int unitCode,
            decimal newQuantity,
            decimal? priceOverride = null,
            double? discountPercent = null)
        {
            var itemToUpdate = Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);
            if (itemToUpdate != null)
            {
                if (newQuantity > 0)
                {
                    itemToUpdate.Quantity = newQuantity;
                    if (priceOverride.HasValue)
                    {
                        itemToUpdate.PricePerUnit = priceOverride.Value;
                        itemToUpdate.PricePerUnitBeforeDiscount = priceOverride.Value;
                    }
                    itemToUpdate.DiscountPercent = discountPercent;
                }
                else
                {
                    Items.Remove(itemToUpdate);
                }
                await PersistStateAsync();

                NotifyCartChanged();
            }
        }

        public async Task RemoveItemAsync(string itemCode, int unitCode)
        {
            var itemsToRemove = Items.Where(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode).ToList();
            if (itemsToRemove.Any())
            {
                foreach (var item in itemsToRemove)
                {
                    Items.Remove(item);
                }
                await PersistStateAsync();

                NotifyCartChanged();
            }
        }

        public async Task ClearCartAsync() // تبدیل به متد آسنکرون
        {
            Items.Clear();
            // CurrentCustomer را اینجا null نکنید، SetCustomerAsync مسئول آن است
            // CurrentCustomer = null; 
            _logger.LogInformation("Cart items cleared.");
            NotifyCartChanged();
            await PersistStateAsync();
            NotifyCartChanged();
        }

        // متد خصوصی برای ذخیره آیتم‌های سبد
        public async Task PersistStateAsync()
        {
            try
            {
                var state = new UserStateDto
                {
                    CurrentCustomer = this.CurrentCustomer,
                    CartItems = this.Items,
                    CustomerType = this.CustomerType,
                    DepartmentValue = this.DepartmentValue,
                    PaymentTerm = this.PaymentTerm,
                    AgreedDuration = this.AgreedDuration,
                    PriceList = this.PriceList,
                    DiscountList = this.DiscountList,
                    SelectedGroupId = this.SelectedGroupId,
                    CurrentAnbarCode = this.CurrentAnbarCode,
                    IsHistoricalMode = this.IsHistoricalMode
                };
                await _stateApi.SaveStateAsync(state);
                _logger.LogInformation("{ItemCount} cart items saved to local storage.", Items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving cart items to local storage.");
            }
        }

        // سایر متدها (GetTotal و ...) بدون تغییر باقی می‌مانند یا در صورت نیاز آسنکرون می‌شوند
        // ... (متدهای GetTotalAmountBeforeDiscountConsideringNesbat, GetTotalLineDiscountAmount, GetFinalTotal) ...
        public decimal GetTotalAmountBeforeDiscountConsideringNesbat() => Items.Sum(item => item.CalculatedRowTotalPriceBeforeLineDiscount);
        public decimal GetTotalLineDiscountAmount() => Items.Sum(item => item.LineDiscountAmountCalculatedOnFullPrice);
        public decimal GetFinalTotal() => Items.Sum(item => item.FinalRowPriceAfterLineDiscount);
        public decimal GetTotal() => GetFinalTotal();


        // SetCurrentAnbarCode, GetCurrentAnbarCode, GetItemQuantity, GetCartItem
        // این متدها اگر وضعیت داخلی سرویس را تغییر نمی‌دهند که نیاز به ذخیره‌سازی داشته باشد، می‌توانند سنکرون باقی بمانند.
        public void SetCurrentAnbarCode(int anbarCode)
        {
            if (CurrentAnbarCode != anbarCode && Items.Any())
            {
                Console.WriteLine($"Warning: AnbarCode changed from {CurrentAnbarCode} to {anbarCode} while cart has items.");
                // در این حالت معمولاً باید سبد خرید پاک شود یا به کاربر هشدار داده شود
                // فعلا فقط لاگ می‌کنیم
            }
            CurrentAnbarCode = anbarCode;
            // ذخیره CurrentAnbarCode در localStorage اگر لازم است (معمولا لازم نیست چون با انتخاب گروه کالا مجدد تنظیم می‌شود)
        }
        public int? GetCurrentAnbarCode() => CurrentAnbarCode;
        public decimal GetItemQuantity(string itemCode, int unitCode) => Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode)?.Quantity ?? 0;
        public CartItem? GetCartItem(string itemCode, int unitCode) => Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);


        private void NotifyCartChanged() => CartChanged?.Invoke();
    }
}