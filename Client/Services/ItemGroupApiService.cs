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
using Safir.Shared.Models.Kharid;

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

        public async Task<List<VisitorItemPriceDto>?> GetVisitorPricesAsync(int priceListId)
        {
            if (priceListId <= 0) return null;
            try
            {
                // اطمینان از اینکه HttpClient به درستی BaseAddress و هدرهای لازم (مانند توکن احراز هویت) را دارد
                var response = await _httpClient.GetAsync($"api/items/visitor-prices?priceListId={priceListId}");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<List<VisitorItemPriceDto>>();
                    return result;
                }
                else
                {
                    _logger.LogError("Error fetching visitor prices from API. Status: {StatusCode}, PriceListId: {PriceListId}", response.StatusCode, priceListId);
                    // می‌توانید جزئیات خطا را نیز لاگ کنید: await response.Content.ReadAsStringAsync();
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in GetVisitorPricesAsync for PriceListId: {PriceListId}", priceListId);
                return null;
            }
        }

        public async Task<PriceElamieTfDtlDto?> GetPriceElamieTfDetailsAsync(int? elamiehTakhfifId, int? custTypeCode, int? paymentTermId)
        {
            if (!elamiehTakhfifId.HasValue || !custTypeCode.HasValue || !paymentTermId.HasValue)
                return null;

            // The actual API endpoint might differ
            var apiUrl = $"api/lookup/GetPriceElamieTfDetails?elamiehTakhfifId={elamiehTakhfifId.Value}&custTypeCode={custTypeCode.Value}&paymentTermId={paymentTermId.Value}";
            try
            {
                var result = await _httpClient.GetFromJsonAsync<PriceElamieTfDtlDto>(apiUrl);
                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning(ex, "PriceElamieTfDetails not found for PEID: {PEID}, CustCode: {CustCode}, PPID: {PPID}", elamiehTakhfifId, custTypeCode, paymentTermId);
                return null; // Or return new PriceElamieTfDtlDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching PriceElamieTfDetails for PEID: {PEID}, CustCode: {CustCode}, PPID: {PPID}", elamiehTakhfifId, custTypeCode, paymentTermId);
                // Depending on desired behavior, either return null or re-throw
                return null;
            }
        }
    }
}