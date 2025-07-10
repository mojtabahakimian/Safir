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
                var response = await _httpClient.GetAsync(requestUri);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<TCODE_MENUITEM>>();
                }
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("API Error fetching groups. Status: {StatusCode}, Content: {ErrorContent}", response.StatusCode, errorContent);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching item groups from {RequestUri}", requestUri);
                return null;
            }
        }

        // --- Get Items By Group (Corrected Signature & Implementation) ---
        // <<< متد به‌روز شده برای دریافت کالاها >>>
        public async Task<PagedResult<ItemDisplayDto>?> GetItemsByGroupAsync(double groupCode, int pageNumber = 1, int pageSize = 10, string? searchTerm = null)
        {
            var queryParams = new List<string>
            {
                $"pageNumber={pageNumber}",
                $"pageSize={pageSize}"
            };
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                queryParams.Add($"searchTerm={HttpUtility.UrlEncode(searchTerm)}");
            }

            string requestUri = $"api/items/bygroup/{groupCode}?" + string.Join("&", queryParams);

            try
            {
                var pagedResult = await _httpClient.GetFromJsonAsync<PagedResult<ItemDisplayDto>>(requestUri);
                return pagedResult;
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
                //var requestUri = $"api/items/visitor-prices?{queryParams.ToString()}";

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

        public async Task<PagedResult<ItemDisplayDto>?> GetHistoricalOrderItemsAsync(
            int anbarCode,
            int? priceListId,
            int? customerTypeCode,
            int? paymentTermId,
            int? discountListId,
            string? searchTerm,
            int pageNumber,
            int pageSize)
        {
            // ایجاد یک آبجکت از DTO جدید
            var requestDto = new HistoricalSearchRequestDto
            {
                AnbarCode = anbarCode,
                SearchTerm = searchTerm,
                PageNumber = pageNumber,
                PageSize = pageSize,
                PriceListId = priceListId,
                CustomerTypeCode = customerTypeCode,
                PaymentTermId = paymentTermId,
                DiscountListId = discountListId
            };

            string requestUri = "api/items/search-historical-items"; // << آدرس جدید

            try
            {
                _logger.LogInformation("Client Service: Posting to {RequestUri} for historical items.", requestUri);

                // ارسال درخواست به صورت POST
                var response = await _httpClient.PostAsJsonAsync(requestUri, requestDto);

                // بررسی پاسخ
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PagedResult<ItemDisplayDto>>();
                    _logger.LogInformation("Client Service: Successfully received paged result for historical items.");
                    return result;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get historical items. Status: {StatusCode}, Content: {ErrorContent}", response.StatusCode, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Client Service: Exception searching historical items from {RequestUri}", requestUri);
                return null;
            }
        }
    }
}