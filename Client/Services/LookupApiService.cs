using Safir.Shared.Models; // For DTOs
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json; // For GetFromJsonAsync
using System.Threading.Tasks;
using System; // For Exception
using Safir.Shared.Models.Kala;

namespace Safir.Client.Services
{
    public class LookupApiService
    {
        private readonly HttpClient _httpClient;
        // Optional: Inject ILogger if needed

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

        public async Task<List<LookupDto<int?>>?> GetCustomerTypesAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<LookupDto<int?>>>("api/lookup/customertypes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Customer Types: {ex.Message}");
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
    }
}