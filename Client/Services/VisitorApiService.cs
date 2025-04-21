// Safir.Client/Services/VisitorApiService.cs
using Safir.Shared.Models.Visitory;
// using Safir.Shared.Models; // PagedResult دیگر لازم نیست
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net.Http.Headers;
using System.Web;

namespace Safir.Client.Services
{
    public class VisitorApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VisitorApiService> _logger;

        public VisitorApiService(HttpClient httpClient, ILogger<VisitorApiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }

        // متد دریافت تاریخ ها (بدون تغییر)
        public async Task<IEnumerable<long>?> GetMyVisitDatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/visitors/my-visit-dates");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<long>>();
                }
                // ... Error Handling ...
                _logger.LogError("Error fetching visit dates..."); return null;
            }
            catch (Exception ex) { /* ... Log ... */ return null; }
        }


        // --- متد بازگردانی شده برای دریافت *همه* مشتریان ---
        public async Task<IEnumerable<VISITOR_CUSTOMERS>?> GetMyCustomersAsync(long? visitDate = null)
        {
            string requestUri = "api/visitors/my-customers";
            if (visitDate.HasValue)
            {
                requestUri += $"?visitDate={visitDate.Value}";
            }

            try
            {
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var customers = await response.Content.ReadFromJsonAsync<List<VISITOR_CUSTOMERS>>();
                    return customers;
                }
                else
                {
                    // ... Error Handling ...
                    _logger.LogError("Error fetching customers for date {VisitDate}...", visitDate);
                    return null;
                }
            }
            catch (Exception ex)
            {
                // ... Log ...
                _logger.LogError(ex, "Exception fetching customers for date {VisitDate}...", visitDate);
                return null;
            }
        }
    }
}