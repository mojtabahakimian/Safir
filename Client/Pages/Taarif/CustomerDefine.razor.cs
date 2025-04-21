using Microsoft.AspNetCore.Components;
using MudBlazor;
using Safir.Client.Services; // For CustomerApi
using Safir.Shared.Models.Taarif; // For CustomerModel
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations; // For ValidationAttribute
using Safir.Shared.Models;
using Microsoft.JSInterop;
using static System.Net.WebRequestMethods;
using System.Net.Http.Json;

namespace Safir.Client.Pages.Taarif
{
    // --- کلاس کمکی برای نتیجه Geolocation ---
    // این کلاس برای دریافت نتیجه از جاوا اسکریپت استفاده می‌شود
    public class GeolocationResult
    {
        public bool Success { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Message { get; set; } // برای پیام موفقیت یا خطا
    }

    // --- Helper class for Dropdown data ---
    // Consider moving this to a shared location if used elsewhere
    public class DropdownItem<T>
    {
        public T Id { get; set; }
        public string Name { get; set; }
        public T ParentId { get; set; } // For Shahr dependency on Ostan
    }

    public partial class CustomerDefine
    {
        [Inject] IJSRuntime JSRuntime { get; set; } // <<< تزریق IJSRuntime

        private MudForm form = default!;
        private CustomerModel customerModel = new();
        private bool success; // Form validation status
        private bool isLoading = false;

        private bool isFetchingLocation = false; // <<< فلگ برای نمایش وضعیت دریافت موقعیت


        // --- Watch for Ostan changes to update Shahr ---
        private int? _selectedOstanId;
        private int? SelectedOstanId
        {
            get => _selectedOstanId;
            set
            {
                if (_selectedOstanId != value)
                {
                    _selectedOstanId = value;
                    // Update the model directly as well
                    customerModel.OSTANID = value;
                    FilterShahrList(value);
                }
            }
        }
        protected override async Task OnInitializedAsync()
        {
            await LoadInitialData();
        }

        private async Task LoadInitialData()
        {
            isLoading = true;
            StateHasChanged();

            var loadDropdownsTask = LoadDropdownDataAsync(); // Load dropdowns first
            await loadDropdownsTask; // Wait for dropdowns

            // <<< دریافت موقعیت مکانی بعد از بارگذاری Dropdown ها

            await PrepareNewCustomer(); // Then prepare the new customer form (fetches next number)

            // isLoading is set to false inside PrepareNewCustomer or HandleValidSubmit
        }
        // --- متد جدید برای دریافت موقعیت مکانی ---
        private async Task FetchAndSetCurrentLocation()
        {
            isFetchingLocation = true;
            StateHasChanged();

            try
            {
                var result = await JSRuntime.InvokeAsync<GeolocationResult>("blazorGeolocation.getCurrentPosition");

                if (result.Success && result.Latitude.HasValue && result.Longitude.HasValue)
                {
                    customerModel.Latitude = result.Latitude.Value;
                    customerModel.Longitude = result.Longitude.Value;
                    // نمایش پیام موفقیت (که ممکن است شامل دقت باشد)
                    Snackbar.Add(result.Message ?? "موقعیت مکانی فعلی دریافت شد.", Severity.Info);
                }
                else
                {
                    // اگر success false باشد (که بعید است با JS فعلی رخ دهد)
                    customerModel.Latitude = null;
                    customerModel.Longitude = null;
                    Snackbar.Add(result.Message ?? "خطا در دریافت موقعیت (نتیجه ناموفق).", Severity.Warning);
                }
            }
            catch (JSException jsEx) // گرفتن خطاهای reject شده از JS
            {
                customerModel.Latitude = null;
                customerModel.Longitude = null;
                Snackbar.Add($"خطا در دریافت موقعیت: {jsEx.Message}", Severity.Error); // نمایش پیام خطای JS
            }
            catch (Exception ex) // گرفتن خطاهای دیگر C#
            {
                customerModel.Latitude = null;
                customerModel.Longitude = null;
                Snackbar.Add($"خطای غیرمنتظره: {ex.Message}", Severity.Error);
            }
            finally
            {
                isFetchingLocation = false;
                StateHasChanged();
            }
        }

        private async Task<IEnumerable<string>> SearchRoutes(string value)
        {
            // اگر لیست مسیرها خالی است یا هنوز لود نشده، لیست خالی برگردان
            if (routeList == null || !routeList.Any())
            {
                return Enumerable.Empty<string>();
            }

            // اگر ورودی کاربر خالی است، کل لیست RouteName ها را برگردان (یا می‌توانید لیست خالی برگردانید تا فقط با تایپ کردن نتایج نشان داده شوند)
            if (string.IsNullOrEmpty(value))
            {
                // return routeList.Select(r => r.DisplayName); // یا DisplayName اگر میخواهید آن نمایش داده شود
                return routeList.Select(r => r.RouteName);
            }

            // فیلتر کردن لیست بر اساس متن ورودی کاربر (value)
            // جستجو در DisplayName (که ترکیبی از نام مسیر و نام مشتری است) انجام می‌شود
            // می‌توانید RouteName را هم به شرط جستجو اضافه کنید
            return routeList
                .Where(r => r.DisplayName.Contains(value, StringComparison.InvariantCultureIgnoreCase)
                         /* || r.RouteName.Contains(value, StringComparison.InvariantCultureIgnoreCase) */ ) // جستجو در نام نمایشی (یا نام مسیر)
                .Select(r => r.RouteName); // مقدار RouteName را برگردان چون به آن Bind شده‌ایم
                                           // .Take(10); // اختیاری: محدود کردن تعداد نتایج برای کارایی بهتر
        }

        // --- Dropdown Data Lists (Adjust types if needed based on DTOs) ---
        private List<LookupDto<int?>> ostanList = new();
        private List<CityLookupDto> shahrList = new(); // Use CityLookupDto
        private List<DropdownItem<int?>> filteredShahrList = new(); // <<< ADD THIS LINE
        private List<LookupDto<int?>> customerTypeList = new();
        private List<LookupDto<int>> personalityTypeList = new(); // Use int based on API/WPF
        private List<RouteLookupDto> routeList = new(); // Use RouteLookupDto

        // --- Load Data for MudSelect Components ---
        private async Task LoadDropdownDataAsync()
        {
            try
            {
                // Fetch all lookup data concurrently
                var ostanTask = LookupService.GetOstansAsync();
                var shahrTask = LookupService.GetShahrsAsync();
                var custTypeTask = LookupService.GetCustomerTypesAsync();
                var routeTask = LookupService.GetRoutesAsync();
                var personalityTypeTask = LookupService.GetPersonalityTypesAsync(); // Fetching from API

                // Wait for all tasks to complete
                await Task.WhenAll(ostanTask, shahrTask, custTypeTask, routeTask, personalityTypeTask);

                // Assign results or handle nulls/errors
                ostanList = ostanTask.Result ?? new List<LookupDto<int?>>();
                shahrList = shahrTask.Result ?? new List<CityLookupDto>(); // Assign full list
                customerTypeList = custTypeTask.Result ?? new List<LookupDto<int?>>();
                routeList = routeTask.Result ?? new List<RouteLookupDto>();

                // Personality types from API
                //personalityTypeList = personalityTypeTask.Result ?? new List<LookupDto<int>>();

                // --- OR: Keep Personality Types Static if preferred ---
                personalityTypeList = new List<LookupDto<int>>
                 {
                     new() { Id = 1, Name = "حقیقی" },
                     new() { Id = 2, Name = "حقوقی" },
                     //new() { Id = 3, Name = "مشارکت مدنی"},
                     //new() { Id = 4, Name = "اتباع غیر ایرانی"}
                 };
                // ------------------------------------------------------

                // Check for specific errors if needed (e.g., if ostanTask.IsFaulted)
                if (ostanTask.IsFaulted || shahrTask.IsFaulted || custTypeTask.IsFaulted || routeTask.IsFaulted || personalityTypeTask.IsFaulted)
                {
                    Snackbar.Add("خطا در دریافت بخشی از اطلاعات پایه.", Severity.Warning);
                    // Log specific errors from Task exceptions if desired
                }
            }
            catch (Exception ex) // Catch potential general exceptions
            {
                Snackbar.Add($"خطای کلی در بارگذاری لیست‌ها: {ex.Message}", Severity.Error);
                // Clear lists to avoid partial data display
                ostanList.Clear();
                shahrList.Clear();
                customerTypeList.Clear();
                routeList.Clear();
                personalityTypeList.Clear();
            }
            // StateHasChanged is called by the parent method (LoadInitialData) or OnInitializedAsync
        }

        // Update FilterShahrList to work with CityLookupDto
        private void FilterShahrList(int? selectedOstanId)
        {
            customerModel.SHAHRID = null;
            if (selectedOstanId.HasValue)
            {
                // Filter based on ParentId from CityLookupDto
                filteredShahrList = shahrList
                    .Where(s => s.ParentId == selectedOstanId.Value)
                    .Select(s => new DropdownItem<int?> { Id = s.Id, Name = s.Name, ParentId = s.ParentId }) // Convert back if needed by MudSelect binding
                    .ToList();
                // OR: Adjust MudSelect for Shahr in .razor to directly use CityLookupDto if simpler
            }
            else
            {
                filteredShahrList.Clear();
            }
            StateHasChanged();
        }


        #region MyRegion
        // Property کمکی برای بایند کردن با MudAutocomplete<RouteLookupDto>
        private RouteLookupDto? SelectedRoute { get; set; }

        // متد رویداد ValueChanged برای به‌روزرسانی customerModel
        private void OnSelectedRouteChanged(RouteLookupDto? selectedDto)
        {
            SelectedRoute = selectedDto; // Property کمکی را آپدیت کن
            customerModel.ROUTE_NAME = selectedDto?.RouteName; // مدل اصلی را آپدیت کن
            InitializeSelectedRoute();
            StateHasChanged(); // برای اطمینان از به‌روز شدن UI اگر لازم باشد
        }
        private void InitializeSelectedRoute()
        {
            if (routeList != null && !string.IsNullOrEmpty(customerModel.ROUTE_NAME))
            {
                SelectedRoute = routeList.FirstOrDefault(r => r.RouteName == customerModel.ROUTE_NAME);
            }
            else
            {
                SelectedRoute = null;
            }
            // StateHasChanged(); // اگر لازم است UI آپدیت شود
        }
        // تابع جستجوی جدید که IEnumerable<RouteLookupDto> برمی‌گرداند
        private async Task<IEnumerable<RouteLookupDto>> SearchRoutesDto(string value)
        {
            // اگر لیست مسیرها خالی است یا هنوز لود نشده، لیست خالی برگردان
            if (routeList == null || !routeList.Any())
            {
                return Enumerable.Empty<RouteLookupDto>();
            }

            // اگر ورودی کاربر خالی است، کل لیست را برگردان
            if (string.IsNullOrEmpty(value))
            {
                return routeList;
            }

            // فیلتر کردن لیست بر اساس متن ورودی کاربر (value)
            return routeList
                .Where(r => r.DisplayName != null &&
                            r.DisplayName.Contains(value, StringComparison.InvariantCultureIgnoreCase))
                // .Take(15); // اختیاری: محدود کردن نتایج
                .ToList(); // خود آبجکت‌های RouteLookupDto را برگردان
        }
        #endregion

        private async Task PrepareNewCustomer()
        {
            isLoading = true;
            customerModel = new CustomerModel(); // Create new empty model, TNUMBER will be null
            filteredShahrList.Clear();
            SelectedOstanId = null;
            SelectedRoute = null;

            await Task.Delay(1);
            // Reset the form, clearing existing values (including any previous TNUMBER shown)
            await form.ResetAsync();
            form.ResetValidation();

            isLoading = false;
            StateHasChanged(); // Update UI to show empty form
        }

        private async Task HandleValidSubmit()
        {
            await form.Validate();
            if (!success)
            {
                Snackbar.Add("لطفاً اطلاعات فرم را به درستی تکمیل کنید.", Severity.Warning);
                return;
            }

            isLoading = true;
            StateHasChanged();

            try
            {
                // Call the API service and get the result tuple
                var (generatedTnumber, errorMessage) = await CustomerApiService.SaveCustomerAsync(customerModel);

                if (generatedTnumber > 0) // Success case (TNUMBER generated)
                {
                    Snackbar.Add($"مشتری با شماره {generatedTnumber} با موفقیت ذخیره شد.", Severity.Success);
                    await PrepareNewCustomer(); // Prepare for the next entry
                                                // isLoading is set to false inside PrepareNewCustomer
                }
                else if (generatedTnumber == -1) // Success but unexpected response format
                {
                    Snackbar.Add(errorMessage ?? "مشتری ذخیره شد، اما پاسخ سرور نامشخص بود.", Severity.Warning);
                    await PrepareNewCustomer();
                }
                else // Failure case (generatedTnumber is 0)
                {
                    // Display the specific error message returned from the API
                    Snackbar.Add(errorMessage ?? "خطا در ذخیره مشتری در سرور.", Severity.Error);
                    isLoading = false; // Keep loading indicator until user corrects
                    StateHasChanged();
                }
            }
            catch (Exception ex) // Catch exceptions from the service call itself (less likely now)
            {
                Snackbar.Add($"خطای غیرمنتظره هنگام ذخیره: {ex.Message}", Severity.Error);
                isLoading = false;
                StateHasChanged();
            }
        }
    }
}