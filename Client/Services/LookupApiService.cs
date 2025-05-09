using Safir.Shared.Models; // For DTOs
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json; // For GetFromJsonAsync
using System.Threading.Tasks;
using System; // For Exception
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Taarif;

namespace Safir.Client.Services
{
    public class LookupApiService
    {
        private readonly HttpClient _httpClient;

        private List<TCOD_ANBAR>? _cachedAnbarList; // برای کش کردن لیست انبارها

        public LookupApiService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<List<LookupDto<int?>>?> GetOstansAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<LookupDto<int?>>>("api/lookup/ostans");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Ostans: {ex.Message}"); // Log error
                return null; // Or throw specific exception
            }
        }

        public async Task<List<CityLookupDto>?> GetShahrsAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<CityLookupDto>>("api/lookup/shahrs");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Shahrs: {ex.Message}");
                return null;
            }
        }

        public async Task<List<RouteLookupDto>?> GetRoutesAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<RouteLookupDto>>("api/lookup/routes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Routes: {ex.Message}");
                return null;
            }
        }

        public async Task<List<LookupDto<int>>?> GetPersonalityTypesAsync()
        {
            try
            {
                // Assuming API endpoint exists, otherwise keep it static in Blazor
                return await _httpClient.GetFromJsonAsync<List<LookupDto<int>>>("api/lookup/personalitytypes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Personality Types: {ex.Message}");
                return null;
            }
        }
        public async Task<List<TCOD_VAHEDS>?> GetUnitsAsync()
        {
            try
            {
                // مسیر API که در کنترلر سرور ایجاد خواهیم کرد
                return await _httpClient.GetFromJsonAsync<List<TCOD_VAHEDS>>("api/lookup/units");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Units: {ex.Message}"); // یا استفاده از ILogger
                // می‌توانید خطا را throw کنید یا null برگردانید
                return null;
            }
        }
        public async Task<List<TCOD_ANBAR>?> GetAnbarhaAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedAnbarList != null && _cachedAnbarList.Any())
            {
                return _cachedAnbarList;
            }
            try        

            {
            _cachedAnbarList = await _httpClient.GetFromJsonAsync<List<TCOD_ANBAR>>("api/lookup/anbarha");
                return _cachedAnbarList ?? new List<TCOD_ANBAR>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Anbarha: {ex.Message}"); // یا استفاده از ILogger
                return null;
            }
        }

        #region ELEMIYEH_GHEYMAT
        // متد GetCustomerTypesAsync قبلا برای نوع مشتری استفاده شده، نام آن را حفظ می‌کنیم
        // اما آدرس API را به "customerkinds" تغییر می‌دهیم اگر با Controller هماهنگ باشد
        // public async Task<List<LookupDto<int?>>> GetCustomerTypesAsync()
        // {
        //     return await _httpClient.GetFromJsonAsync<List<LookupDto<int?>>>("api/lookup/customerkinds") ?? new();
        // }
        // با توجه به اینکه در ItemGroups.razor قبلا LookupDto<int?> استفاده شده، بهتر است این نوع را بازگردانیم.
        public async Task<List<LookupDto<int?>>> GetCustomerTypesAsync() // قبلا LookupDto<int> بود
        {
            var result = await _httpClient.GetFromJsonAsync<List<LookupDto<int>>>("api/lookup/customerkinds");
            return result?.Select(r => new LookupDto<int?>(r.Id, r.Name)).ToList() ?? new List<LookupDto<int?>>();
        }


        public async Task<CustomerHesabInfo?> GetCustomerHesabInfoByHesCodeAsync(string customerHesCode)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<CustomerHesabInfo?>($"api/lookup/customerhesabinfo/{customerHesCode}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; } // یا لاگ خطا
        }

        public async Task<List<LookupDto<int?>>> GetDepartmentsAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<LookupDto<int>>>("api/lookup/departments");
            return result?.Select(r => new LookupDto<int?>(r.Id, r.Name)).ToList() ?? new List<LookupDto<int?>>();
        }

        public async Task<List<PaymentTermDto>> GetPaymentTermsAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<PaymentTermDto>>("api/lookup/paymentterms") ?? new();
        }

        public async Task<int?> GetDefaultPaymentTermIdForUserAsync(int userId)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<int?>($"api/lookup/defaultpaymentterm/user/{userId}");
            }
            // اگر سرور null یا NoContent برگرداند GetFromJsonAsync<int?> خطا می‌دهد، مگر اینکه سرور واقعا JSON null برگرداند
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.StatusCode == System.Net.HttpStatusCode.NoContent) { return null; }
            catch (System.Text.Json.JsonException) { return null; } // اگر پاسخ خالی باشد و نتواند به int? تبدیل کند
        }

        public async Task<List<PriceListDto>> GetPriceListsAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<PriceListDto>>("api/lookup/pricelists") ?? new();
        }

        public async Task<int?> GetDefaultPriceListIdAsync(int departmentId)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<int?>($"api/lookup/defaultpricelist/department/{departmentId}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.StatusCode == System.Net.HttpStatusCode.NoContent) { return null; }
            catch (System.Text.Json.JsonException) { return null; }
        }

        public async Task<List<DiscountListDto>> GetDiscountListsAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<DiscountListDto>>("api/lookup/discountlists") ?? new();
        }

        public async Task<int?> GetDefaultDiscountListIdAsync(int departmentId)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<int?>($"api/lookup/defaultdiscountlist/department/{departmentId}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.StatusCode == System.Net.HttpStatusCode.NoContent) { return null; }
            catch (System.Text.Json.JsonException) { return null; }
        }

        public async Task<List<PaymentTermDto>> GetDynamicPaymentTermsAsync(int? departmentId, int? selectedDiscountListId)
        {
            var queryParams = new List<string>();
            if (departmentId.HasValue)
            {
                queryParams.Add($"departmentId={departmentId.Value}");
            }
            if (selectedDiscountListId.HasValue)
            {
                queryParams.Add($"selectedDiscountListId={selectedDiscountListId.Value}");
            }
            var queryString = string.Join("&", queryParams);

            try
            {
                return await _httpClient.GetFromJsonAsync<List<PaymentTermDto>>($"api/lookup/paymentterms/dynamic?{queryString}") ?? new List<PaymentTermDto>();
            }
            catch (Exception ex)
            {
                return new List<PaymentTermDto>(); // برگرداندن لیست خالی در صورت خطا
            }
        }
        #endregion
    }
}