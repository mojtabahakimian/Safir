// Safir.Client/Services/ItemGroupApiService.cs
using Safir.Shared.Models.Kala;
using Safir.Shared.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net;
using System.Web;
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

        public async Task<PagedResult<ItemDisplayDto>?> GetItemsByGroupAsync(
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

        public async Task<decimal?> GetItemInventoryAsync(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                _logger.LogWarning("GetItemInventoryAsync called with empty itemCode.");
                return null;
            }

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
                    return null;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching inventory. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching inventory from {RequestUri}", requestUri);
                return null;
            }
        }

        public async Task<InventoryDetailsDto?> GetItemInventoryDetailsAsync(string itemCode, int anbarCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                _logger.LogWarning("GetItemInventoryDetailsAsync called with empty itemCode.");
                return null;
            }

            string requestUri = $"api/inventory/{Uri.EscapeDataString(itemCode)}/details?anbarCode={anbarCode}";
            try
            {
                _logger.LogInformation("Calling API for inventory details: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var details = await response.Content.ReadFromJsonAsync<InventoryDetailsDto>();
                    _logger.LogInformation("Received inventory details for {ItemCode} in Anbar {AnbarCode}. Current: {CurrentInv}, Min: {MinInv}",
                       itemCode, anbarCode, details?.CurrentInventory ?? -1, details?.MinimumInventory ?? -1);
                    return details;
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Inventory details API returned NotFound for Item: {ItemCode}, Anbar: {AnbarCode}", itemCode, anbarCode);
                    return new InventoryDetailsDto { CurrentInventory = 0, MinimumInventory = 0 };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API Error fetching inventory details. Status: {StatusCode}, URI: {RequestUri}, Content: {ErrorContent}", response.StatusCode, requestUri, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching inventory details from {RequestUri}", requestUri);
                return null;
            }
        }

        public async Task<List<VisitorItemPriceDto>?> GetVisitorPricesAsync(int priceListId, List<string>? itemCodes = null) // Modified signature
        {
            if (priceListId <= 0) return null;
            try
            {
                var queryParams = HttpUtility.ParseQueryString(string.Empty);
                queryParams["priceListId"] = priceListId.ToString();
                if (itemCodes != null && itemCodes.Any())
                {
                    foreach (var code in itemCodes)
                    {
                        queryParams.Add("itemCodes", code); // Add each item code
                    }
                }

                var requestUri = $"api/items/visitor-prices?{queryParams}";

                _logger.LogInformation("Calling API for visitor prices: {RequestUri}", requestUri);
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<List<VisitorItemPriceDto>>();
                    _logger.LogInformation("Successfully received {Count} visitor prices from API.", result?.Count ?? 0);
                    return result;
                }
                else
                {
                    _logger.LogError("Error fetching visitor prices from API. Status: {StatusCode}, PriceListId: {PriceListId}", response.StatusCode, priceListId);
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

            var apiUrl = $"api/lookup/GetPriceElamieTfDetails?elamiehTakhfifId={elamiehTakhfifId.Value}&custTypeCode={custTypeCode.Value}&paymentTermId={paymentTermId.Value}";
            try
            {
                var result = await _httpClient.GetFromJsonAsync<PriceElamieTfDtlDto>(apiUrl);
                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning(ex, "PriceElamieTfDetails not found for PEID: {PEID}, CustCode: {CustCode}, PPID: {PPID}", elamiehTakhfifId, custTypeCode, paymentTermId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching PriceElamieTfDetails for PEID: {PEID}, CustCode: {CustCode}, PPID: {PPID}", elamiehTakhfifId, custTypeCode, paymentTermId);
                return null;
            }
        }
    }
}