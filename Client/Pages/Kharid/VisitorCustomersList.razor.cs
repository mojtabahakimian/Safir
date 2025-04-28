using Microsoft.AspNetCore.Components;
using MudBlazor;
using Safir.Client.Services;
using Safir.Shared.Models.Visitory;
using System.Globalization;

namespace Safir.Client.Pages.Kharid
{
    public partial class VisitorCustomersList : ComponentBase, IDisposable
    {
        [Inject] private VisitorApiService VisitorService { get; set; } = default!;
        [Inject] private NavigationManager NavManager { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<VisitorCustomersList> Logger { get; set; } = default!;
        [Inject] private ShoppingCartService CartService { get; set; } = default!; // <<< تزریق سرویس سبد خرید

        private List<VISITOR_CUSTOMERS>? _originalCustomers;
        private List<long>? availableDates;
        private bool isLoading = true;
        private string? errorMessage;
        private bool datesLoading = true;
        private string? datesErrorMessage;

        private string _searchTerm = "";
        private System.Timers.Timer? _debounceTimer;
        private string SearchTerm
        {
            get => _searchTerm;
            set
            {
                if (_searchTerm != value)
                {
                    _searchTerm = value ?? "";
                    _debounceTimer?.Stop();
                    _debounceTimer?.Start();
                }
            }
        }

        private int _currentPage = 1;
        private int pageSize = 10;
        private int currentPage
        {
            get => _currentPage;
            set
            {
                var totalPgs = TotalPages;
                var newPage = value < 1 ? 1 : (value > totalPgs && totalPgs > 0 ? totalPgs : value);
                if (_currentPage != newPage)
                {
                    _currentPage = newPage;
                    InvokeAsync(StateHasChanged);
                }
            }
        }
        private int TotalPages => pageSize > 0 ? (int)Math.Ceiling((FilteredCustomers?.Count ?? 0) / (double)pageSize) : 0;

        private long? _selectedVisitDate;
        private long? SelectedVisitDate
        {
            get => _selectedVisitDate;
            set
            {
                if (_selectedVisitDate != value)
                {
                    _selectedVisitDate = value;
                    currentPage = 1;
                    SearchTerm = "";
                    _debounceTimer?.Stop();
                    _ = LoadCustomersForDateAsync(_selectedVisitDate);
                }
            }
        }

        private List<VISITOR_CUSTOMERS>? FilteredCustomers
        {
            get
            {
                if (_originalCustomers == null) return null;
                if (string.IsNullOrWhiteSpace(SearchTerm)) return _originalCustomers;

                var searchTermLower = SearchTerm.Trim().ToLowerInvariant();
                try
                {
                    return _originalCustomers.Where(c =>
                        (c.person?.ToLowerInvariant().Contains(searchTermLower) ?? false) ||
                        (c.hes?.ToLowerInvariant().Contains(searchTermLower) ?? false)
                    ).ToList();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error filtering customers (client-side) for term: {SearchTerm}", SearchTerm);
                    return _originalCustomers;
                }
            }
        }

        private List<VISITOR_CUSTOMERS>? PagedCustomers
        {
            get
            {
                var filtered = FilteredCustomers;
                if (filtered == null) return null;
                return filtered.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
            }
        }

        protected override async Task OnInitializedAsync()
        {
            _debounceTimer = new System.Timers.Timer(500);
            _debounceTimer.Elapsed += async (s, e) => await HandleSearchDebounced();
            _debounceTimer.AutoReset = false;

            await LoadVisitDatesAsync();
            if (availableDates != null && availableDates.Any())
            {
                SelectedVisitDate = availableDates.First();
            }
            else
            {
                isLoading = false;
            }
        }

        public void Dispose()
        {
            _debounceTimer?.Dispose();
        }

        // --- متد مربوط به دکمه "ثبت سفارش" ---
        private void StartOrderForCustomer(VISITOR_CUSTOMERS? customer)
        {
            if (customer == null || string.IsNullOrEmpty(customer.hes))
            {
                Snackbar.Add("اطلاعات مشتری برای شروع سفارش معتبر نیست.", Severity.Warning);
                return;
            }

            try
            {
                Logger.LogInformation("Starting order for customer: {CustomerName} ({CustomerHes})", customer.person, customer.hes);
                // تنظیم مشتری فعلی در سرویس سبد خرید
                CartService.SetCustomer(customer);

                // هدایت به صفحه انتخاب کالا (مثلا /item-groups)
                // می‌توانید customer.hes را هم به عنوان پارامتر بفرستید اگر لازم است
                NavManager.NavigateTo("/item-groups");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error starting order for customer {CustomerHes}", customer.hes);
                Snackbar.Add("خطا در شروع فرآیند سفارش.", Severity.Error);
            }
        }


        private async Task LoadVisitDatesAsync()
        {
            datesLoading = true; datesErrorMessage = null; availableDates = null;
            await InvokeAsync(StateHasChanged);
            try
            {
                availableDates = (await VisitorService.GetMyVisitDatesAsync())?.ToList();
                if (availableDates == null)
                {
                    datesErrorMessage = "خطا در بارگذاری لیست تاریخ‌ها.";
                    Logger.LogWarning("GetMyVisitDatesAsync returned null.");
                    Snackbar.Add(datesErrorMessage, Severity.Warning);
                }
                else if (!availableDates.Any()) { Logger.LogInformation("No visit dates found."); }
                else { Logger.LogInformation("Loaded {DateCount} visit dates.", availableDates.Count); }
            }
            catch (Exception ex)
            {
                datesErrorMessage = "خطای پیش‌بینی نشده در بارگذاری تاریخ‌ها.";
                Logger.LogError(ex, "Exception occurred while loading visit dates.");
                Snackbar.Add(datesErrorMessage, Severity.Error);
            }
            finally { datesLoading = false; await InvokeAsync(StateHasChanged); }
        }

        private async Task LoadCustomersForDateAsync(long? dateToLoad)
        {
            if (!dateToLoad.HasValue)
            {
                _originalCustomers = null; isLoading = false; errorMessage = null;
                await InvokeAsync(StateHasChanged); return;
            }

            isLoading = true; errorMessage = null; _originalCustomers = null;
            await InvokeAsync(StateHasChanged);

            try
            {
                _originalCustomers = (await VisitorService.GetMyCustomersAsync(dateToLoad.Value))?.ToList();
                if (_originalCustomers == null)
                {
                    errorMessage = $"خطا در دریافت اطلاعات مشتریان برای تاریخ {FormatPersianDate(dateToLoad.Value.ToString())}.";
                    Logger.LogWarning("GetMyCustomersAsync returned null for date: {VisitDate}", dateToLoad.Value);
                    Snackbar.Add(errorMessage, Severity.Warning);
                }
                else if (!_originalCustomers.Any()) { Logger.LogInformation("No customers found for date: {VisitDate}", dateToLoad.Value); }
                else { Logger.LogInformation("Successfully loaded {CustomerCount} customers for date: {VisitDate}", _originalCustomers.Count, dateToLoad.Value); }
            }
            catch (Exception ex)
            {
                errorMessage = "خطای پیش بینی نشده در بارگذاری لیست مشتریان.";
                Logger.LogError(ex, "Exception occurred loading customers for date: {VisitDate}", dateToLoad.Value);
                Snackbar.Add($"{errorMessage}", Severity.Error);
                _originalCustomers = null;
            }
            finally
            {
                isLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task HandleSearchDebounced()
        {
            _currentPage = 1;
            await InvokeAsync(StateHasChanged);
        }

        private void NavigateToInvoice(string? customerHes)
        {
            if (!string.IsNullOrEmpty(customerHes))
            {
                var invoiceUrl = $"/customer-invoice/{customerHes}";
                NavManager.NavigateTo(invoiceUrl);
            }
            else { Snackbar.Add("کد مشتری نامعتبر است.", Severity.Warning); }
        }

        private async Task ShowCustomerStatement(string? hesabCode) // اسم متد رو شاید لازم نباشه async کنیم دیگه، اما نگهش می‌داریم
        {
            // 1. بررسی کد حساب
            if (string.IsNullOrWhiteSpace(hesabCode))
            {
                Snackbar.Add("کد حساب مشتری (hes) برای این ردیف مشخص نشده است.", Severity.Warning);
                return;
            }

            // 2. ساخت URL صفحه صورت حساب
            var url = $"/customer-statement/{Uri.EscapeDataString(hesabCode)}";

            // 3. استفاده از NavigationManager برای رفتن به آدرس جدید در همین تب
            NavManager.NavigateTo(url);

            // چون از JSRuntime استفاده نمی کنیم، بلاک try-catch مربوط به اون هم لازم نیست
            // و نیازی به async Task هم نیست، می تواند private void باشد، اما async Task هم مشکلی ندارد
        }

        private static string FormatCurrency(double? value) => value?.ToString("N0", CultureInfo.GetCultureInfo("fa-IR")) ?? "0";
        private static string FormatCurrency(long? value) => value?.ToString("N0", CultureInfo.GetCultureInfo("fa-IR")) ?? "0";
        private static string GetMandehColor(double? mandeh) => (!mandeh.HasValue || Math.Abs(mandeh.Value) < 0.01) ? "grey" : (mandeh > 0 ? "red" : "green");
        private static string FormatPersianDate(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString) || (dateString.Length != 8 && dateString.Length != 10) || !long.TryParse(dateString.Replace("/", ""), out _)) return "-";
            try
            {
                if (dateString.Length == 10 && dateString[4] == '/' && dateString[7] == '/') return dateString;
                if (dateString.Length == 8) return $"{dateString.Substring(0, 4)}/{dateString.Substring(4, 2)}/{dateString.Substring(6, 2)}";
                return "?";
            }
            catch { return "?"; }
        }
    }
}