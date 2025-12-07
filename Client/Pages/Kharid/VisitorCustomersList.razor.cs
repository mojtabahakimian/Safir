using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Safir.Client.Services;
using Safir.Shared.Constants;
using Safir.Shared.Models.Visitory;
using System;
using System.Globalization;

namespace Safir.Client.Pages.Kharid
{
    public partial class VisitorCustomersList : ComponentBase, IDisposable
    {
        [Inject] private VisitorApiService VisitorService { get; set; } = default!;
        [Inject] private NavigationManager NavManager { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<VisitorCustomersList> Logger { get; set; } = default!;
        [Inject] private ShoppingCartService CartService { get; set; } = default!;

        [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

        [Inject] private CustomerApi CustomerApi { get; set; } = default!;

        private List<VISITOR_CUSTOMERS>? _customers; // Renamed from _originalCustomers
        private List<long>? availableDates;
        private bool isLoading = true;
        private string? errorMessage;
        private bool datesLoading = true;
        private string? datesErrorMessage;

        private bool _userHasVisitPlan = true;
        private string? _userHesForCheck;

        private bool _isCheckingBlock = false;

        private string UserNameDisplay = string.Empty;
        private string UserHES = string.Empty;

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
        private int _totalItems = 0;

        private int currentPage
        {
            get => _currentPage;
            set
            {
                // Allow setting page even if totalItems is 0 (to reset to 1)
                // But generally constraint to TotalPages
                var totalPgs = TotalPages;
                var newPage = value < 1 ? 1 : (value > totalPgs && totalPgs > 0 ? totalPgs : value);
                
                if (_currentPage != newPage)
                {
                    _currentPage = newPage;
                    _ = LoadData(); // Reload data for new page
                    InvokeAsync(StateHasChanged);
                }
            }
        }
        private int TotalPages => pageSize > 0 ? (int)Math.Ceiling(_totalItems / (double)pageSize) : 0;

        private long? _selectedVisitDate;
        private long? SelectedVisitDate
        {
            get => _selectedVisitDate;
            set
            {
                if (_selectedVisitDate != value)
                {
                    _selectedVisitDate = value;
                    _currentPage = 1; // Reset page without triggering LoadData yet
                    _searchTerm = ""; // Reset search without triggering debounce
                    
                    if (_userHasVisitPlan && _selectedVisitDate.HasValue)
                    {
                        _ = LoadCustomersForDateAsync(_selectedVisitDate);
                    }
                }
            }
        }

        // For compatibility with Razor view which uses PagedCustomers and FilteredCustomers
        // Since we do server-side paging, PagedCustomers is just the current list.
        private List<VISITOR_CUSTOMERS>? PagedCustomers => _customers;
        private List<VISITOR_CUSTOMERS>? FilteredCustomers => _customers; // Used for count display

        protected override async Task OnInitializedAsync()
        {
            _debounceTimer = new System.Timers.Timer(500);
            _debounceTimer.Elapsed += async (s, e) => await HandleSearchDebounced();
            _debounceTimer.AutoReset = false;

            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var currentUserPrincipal = authState.User;
            UserNameDisplay = currentUserPrincipal.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? string.Empty;
            UserHES = currentUserPrincipal.FindFirst(BaseknowClaimTypes.USER_HES)?.Value ?? string.Empty;
            _userHesForCheck = UserHES;

            if (string.IsNullOrEmpty(_userHesForCheck))
            {
                _userHasVisitPlan = false;
                Logger.LogInformation("User does not have a HES. Loading general customer list.");
                await LoadGeneralActiveCustomersAsync(searchTerm: SearchTerm, pageNumber: currentPage);
            }
            else
            {
                await LoadVisitDatesAsync();
                if (availableDates != null && availableDates.Any())
                {
                    _userHasVisitPlan = true;
                    SelectedVisitDate = availableDates.First();
                }
                else
                {
                    _userHasVisitPlan = false;
                    Logger.LogInformation("No visit dates found for User HES {UserHes}. Loading general customer list.", _userHesForCheck);
                    await LoadGeneralActiveCustomersAsync(searchTerm: SearchTerm, pageNumber: currentPage);
                }
            }
        }

        public void Dispose()
        {
            _debounceTimer?.Dispose();
        }

        private async Task LoadData()
        {
            if (_userHasVisitPlan && SelectedVisitDate.HasValue)
            {
                await LoadCustomersForDateAsync(SelectedVisitDate);
            }
            else
            {
                await LoadGeneralActiveCustomersAsync(SearchTerm, currentPage, pageSize);
            }
        }

        private async Task StartOrderForCustomer(VISITOR_CUSTOMERS? customer)
        {
            if (customer == null || string.IsNullOrEmpty(customer.hes))
            {
                Snackbar.Add("اطلاعات مشتری نامعتبر است.", Severity.Warning);
                return;
            }

            _isCheckingBlock = true;
            await InvokeAsync(StateHasChanged);

            try
            {
                bool isBlocked = await CustomerApi.CheckCustomerBlockedAsync(customer.hes);

                if (isBlocked)
                {
                    Snackbar.Add($"امکان ثبت سفارش برای مشتری '{customer.person}' وجود ندارد (حساب مسدود است).", Severity.Error);
                    return;
                }

                await CartService.SetCustomerAsync(customer);
                string navigationUrl = "/item-groups";
                if (!_userHasVisitPlan)
                {
                    navigationUrl += "?mode=historical";
                }
                NavManager.NavigateTo(navigationUrl);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during StartOrderForCustomer for HES {CustomerHes}", customer.hes);
                Snackbar.Add("خطا در بررسی وضعیت یا شروع فرآیند سفارش.", Severity.Error);
            }
            finally
            {
                _isCheckingBlock = false;
                await InvokeAsync(StateHasChanged);
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
                    Snackbar.Add(datesErrorMessage, Severity.Warning);
                }
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
                _customers = null; isLoading = false; errorMessage = null; _totalItems = 0;
                await InvokeAsync(StateHasChanged); return;
            }

            isLoading = true; errorMessage = null; _customers = null;
            await InvokeAsync(StateHasChanged);

            try
            {
                var pagedResult = await VisitorService.GetMyCustomersAsync(dateToLoad.Value, currentPage, pageSize, SearchTerm);
                
                if (pagedResult != null)
                {
                    _customers = pagedResult.Items;
                    _totalItems = pagedResult.TotalCount;
                    
                    if (_customers == null || !_customers.Any()) { Logger.LogInformation("No customers found for date: {VisitDate}", dateToLoad.Value); }
                }
                else
                {
                    errorMessage = $"خطا در دریافت اطلاعات مشتریان برای تاریخ {FormatPersianDate(dateToLoad.Value.ToString())}.";
                    Snackbar.Add(errorMessage, Severity.Warning);
                    _customers = new List<VISITOR_CUSTOMERS>();
                    _totalItems = 0;
                }
            }
            catch (Exception ex)
            {
                errorMessage = "خطای پیش بینی نشده در بارگذاری لیست مشتریان.";
                Logger.LogError(ex, "Exception occurred loading customers for date: {VisitDate}", dateToLoad.Value);
                Snackbar.Add($"{errorMessage}", Severity.Error);
                _customers = null;
                _totalItems = 0;
            }
            finally
            {
                isLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task HandleSearchDebounced()
        {
            _currentPage = 1; // Reset to page 1 on search
            await LoadData(); // Trigger server-side search
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

        private async Task ShowCustomerStatement(string? hesabCode)
        {
            if (string.IsNullOrWhiteSpace(hesabCode))
            {
                Snackbar.Add("کد حساب مشتری نامعتبر است.", Severity.Warning);
                return;
            }

            _isCheckingBlock = true;
            await InvokeAsync(StateHasChanged);

            try
            {
                var customer = _customers?.FirstOrDefault(c => c.hes == hesabCode);
                string customerName = customer?.person ?? hesabCode;

                bool isBlocked = await CustomerApi.CheckCustomerBlockedAsync(hesabCode);

                if (isBlocked)
                {
                    Snackbar.Add($"امکان مشاهده صورت حساب برای مشتری '{customerName}' وجود ندارد (حساب مسدود است).", Severity.Error);
                    return;
                }

                var url = $"/customer-statement/{Uri.EscapeDataString(hesabCode)}?name={Uri.EscapeDataString(customerName)}";
                NavManager.NavigateTo(url);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during ShowCustomerStatement for HES {HesCode}", hesabCode);
                Snackbar.Add("خطا در بررسی وضعیت یا نمایش صورت حساب.", Severity.Error);
            }
            finally
            {
                _isCheckingBlock = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task LoadGeneralActiveCustomersAsync(string? searchTerm = null, int pageNumber = 1, int pageSize = 50)
        {
            isLoading = true;
            errorMessage = null;
            _customers = null;
            await InvokeAsync(StateHasChanged);

            try
            {
                Logger.LogInformation("Loading general active customers. Page: {Page}, Size: {Size}, Search: '{Search}'", pageNumber, pageSize, searchTerm);

                var pagedResult = await CustomerApi.GetActiveCustomersForUserAsync(pageNumber, pageSize, searchTerm);

                if (pagedResult != null)
                {
                    _customers = pagedResult.Items;
                    _totalItems = pagedResult.TotalCount;
                    
                    if (_customers == null || !_customers.Any())
                    {
                        Snackbar.Add("مشتری فعالی برای نمایش یافت نشد.", Severity.Info);
                    }
                }
                else
                {
                    errorMessage = "خطا در دریافت لیست مشتریان فعال.";
                    Snackbar.Add(errorMessage, Severity.Warning);
                    _customers = new List<VISITOR_CUSTOMERS>();
                    _totalItems = 0;
                }
            }
            catch (Exception ex)
            {
                errorMessage = "خطای پیش بینی نشده در بارگذاری لیست مشتریان فعال.";
                Logger.LogError(ex, "Exception occurred loading general active customers. Search: '{Search}'", searchTerm);
                Snackbar.Add($"{errorMessage}", Severity.Error);
                _customers = new List<VISITOR_CUSTOMERS>();
                _totalItems = 0;
            }
            finally
            {
                isLoading = false;
                await InvokeAsync(StateHasChanged);
            }
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