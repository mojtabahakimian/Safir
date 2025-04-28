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
        // <<< متد به‌روز شده برای دریافت کالاها >>>
        public async Task<PagedResult<ItemDisplayDto>?> GetItemsByGroupAsync( // <<< نوع خروجی تغییر کرد
            double groupCode,
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["pageNumber"] = pageNumber.ToString();
            query["pageSize"] = pageSize.ToString();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query["searchTerm"] = searchTerm;
            }

            string requestUri = $"api/items/bygroup/{groupCode}?{query}";
            try
            {
                _logger.LogInformation("Calling API: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    // <<< نوع ReadFromJsonAsync تغییر کرد >>>
                    var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<ItemDisplayDto>>();
                    _logger.LogInformation("Received paged items DTOs for group {GroupCode}, Search: '{SearchTerm}'", groupCode, searchTerm);
                    return pagedResult;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching items. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching items from {RequestUri}", requestUri);
                return null;
            }
        }

        // <<< متد جدید برای دریافت موجودی >>>
        public async Task<decimal?> GetItemInventoryAsync(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                _logger.LogWarning("GetItemInventoryAsync called with empty itemCode.");
                return null;
            }

            //string requestUri = $"api/items/inventory/{Uri.EscapeDataString(itemCode)}";
            string requestUri = $"api/inventory/{Uri.EscapeDataString(itemCode)}";
            try
            {
                _logger.LogInformation("Calling API for inventory: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var inventory = await response.Content.ReadFromJsonAsync<decimal?>();
                    _logger.LogInformation("Received inventory for {ItemCode}: {Inventory}", itemCode, inventory);
                    return inventory;
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Inventory API returned NotFound for {ItemCode}", itemCode);
                    return null; // Or handle as 0?
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching inventory. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null; // Indicate error
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching inventory from {RequestUri}", requestUri);
                return null;
            }
        }

        // --- New Method to Fetch Inventory Details ---
        public async Task<InventoryDetailsDto?> GetItemInventoryDetailsAsync(string itemCode, int anbarCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                _logger.LogWarning("GetItemInventoryDetailsAsync called with empty itemCode.");
                return null;
            }

            // Construct the URI with the anbarCode query parameter
            string requestUri = $"api/inventory/{Uri.EscapeDataString(itemCode)}/details?anbarCode={anbarCode}";
            try
            {
                _logger.LogInformation("Calling API for inventory details: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var details = await response.Content.ReadFromJsonAsync<InventoryDetailsDto>();
                    _logger.LogInformation("Received inventory details for {ItemCode} in Anbar {AnbarCode}", itemCode, anbarCode);
                    return details;
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Inventory details API returned NotFound for Item: {ItemCode}, Anbar: {AnbarCode}", itemCode, anbarCode);
                    // Return a default DTO or null depending on how the client should handle 'not found'
                    return new InventoryDetailsDto { CurrentInventory = 0, MinimumInventory = 0 }; // Default to 0
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching inventory details. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null; // Indicate error
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching inventory details from {RequestUri}", requestUri);
                return null;
            }
        }
    }
}