// Safir.Client/Services/ItemGroupApiService.cs (Corrected)
using Safir.Shared.Models.Kala;
using Safir.Shared.Models; // For PagedResult
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net;
using System.Web; // For HttpUtility

namespace Safir.Client.Services
{
    public class ItemGroupApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ItemGroupApiService> _logger;

        public ItemGroupApiService(HttpClient httpClient, ILogger<ItemGroupApiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }

        // --- Get Item Groups ---
        public async Task<List<TCODE_MENUITEM>?> GetItemGroupsAsync()
        {
            string requestUri = "api/itemgroups";
            try
            {
                _logger.LogInformation("Calling API to get item groups: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var groups = await response.Content.ReadFromJsonAsync<List<TCODE_MENUITEM>>();
                    _logger.LogInformation("Successfully received {Count} item groups from API.", groups?.Count ?? 0);
                    return groups ?? new List<TCODE_MENUITEM>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching groups. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching item groups from {RequestUri}", requestUri);
                return null;
            }
        }

        // --- Get Items By Group (Corrected Signature & Implementation) ---
        public async Task<PagedResult<STUF_DEF>?> GetItemsByGroupAsync(
            double groupCode,
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null) // <<< Added searchTerm parameter HERE
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["pageNumber"] = pageNumber.ToString();
            query["pageSize"] = pageSize.ToString();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query["searchTerm"] = searchTerm; // Pass searchTerm if provided
            }

            // Corrected route based on ItemsController
            string requestUri = $"api/items/bygroup/{groupCode}?{query}";
            try
            {
                _logger.LogInformation("Calling API: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<STUF_DEF>>();
                    _logger.LogInformation("Received paged items for group {GroupCode}, Search: '{SearchTerm}'", groupCode, searchTerm);
                    return pagedResult; // Can be null if JSON parsing fails
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching items. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null; // Indicate failure
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching items from {RequestUri}", requestUri);
                return null;
            }
        }
    }
}