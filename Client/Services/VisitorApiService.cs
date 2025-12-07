using Safir.Shared.Models.Visitory;
using Safir.Shared.Models; // For PagedResult
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

        public async Task<IEnumerable<long>?> GetMyVisitDatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/visitors/my-visit-dates");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<long>>();
                }
                _logger.LogError("Error fetching visit dates..."); return null;
            }
            catch (Exception ex) { /* ... Log ... */ return null; }
        }

        public async Task<PagedResult<VISITOR_CUSTOMERS>?> GetMyCustomersAsync(long? visitDate = null, int pageNumber = 1, int pageSize = 50, string? searchTerm = null)
        {
            string requestUri = $"api/visitors/my-customers?pageNumber={pageNumber}&pageSize={pageSize}";
            if (visitDate.HasValue)
            {
                requestUri += $"&visitDate={visitDate.Value}";
            }
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                requestUri += $"&searchTerm={Uri.EscapeDataString(searchTerm)}";
            }

            try
            {
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PagedResult<VISITOR_CUSTOMERS>>();
                    return result;
                }
                else
                {
                    _logger.LogError("Error fetching customers for date {VisitDate}...", visitDate);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching customers for date {VisitDate}...", visitDate);
                return null;
            }
        }
    }
}