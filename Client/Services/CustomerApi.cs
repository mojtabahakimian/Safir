using Safir.Shared.Interfaces;
using Safir.Shared.Models.Taarif;
using Safir.Shared.Models.Visitory;
using System.Net.Http.Json; // Ensure this is included
using System.Collections.Generic;
using System.Threading.Tasks;
using System; // For ArgumentNullException
using Safir.Shared.Models.Hesabdari;
using System.Web;

namespace Safir.Client.Services;

public class CustomerApi
{
    private readonly HttpClient _httpClient; // Inject HttpClient directly
    private readonly ILogger<CustomerApi> _logger; // Optional: for logging

    // Inject HttpClient instead of IClientDatabaseService if ApiService was just a wrapper
    public CustomerApi(HttpClient httpClient, ILogger<CustomerApi> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    public async Task<(int GeneratedTnumber, string? ErrorMessage)> SaveCustomerAsync(CustomerModel model)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/customers", model);

            if (response.IsSuccessStatusCode) // HTTP 2xx
            {
                var result = await response.Content.ReadFromJsonAsync<SaveCustomerResponse>();
                if (result != null && result.Tnumber > 0)
                {
                    _logger?.LogInformation("Customer saved via API. New TNUMBER: {TNUMBER}", result.Tnumber);
                    return (result.Tnumber, null); // Success, return TNUMBER, no error message
                }
                else
                {
                    _logger?.LogWarning("Customer save API call succeeded but response format was unexpected.");
                    return (-1, "پاسخ موفقیت آمیز بود، اما فرمت پاسخ سرور نامعتبر است."); // Indicate success but weird response
                }
            }
            else // HTTP 4xx or 5xx
            {
                string errorContent = "خطای نامشخص از سرور.";
                try
                {
                    // Attempt to read standard ProblemDetails structure
                    var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                    if (!string.IsNullOrEmpty(problemDetails?.Detail))
                    {
                        errorContent = problemDetails.Detail; // Use the detail from server
                    }
                    else
                    {
                        // Fallback to reading raw content if ProblemDetails fails
                        errorContent = await response.Content.ReadAsStringAsync();
                    }
                }
                catch { /* Ignore potential exceptions during error content reading */ }

                _logger?.LogError("Failed to save customer via API. Status: {StatusCode}, Content: {ErrorContent}", response.StatusCode, errorContent);
                // Return 0 for failure and the specific error message
                return (0, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception occurred while saving customer via API.");
            // Return 0 for failure and a generic exception message
            return (0, $"خطای کلاینت هنگام ذخیره: {ex.Message}");
        }
    }

    // Helper class still needed
    private class SaveCustomerResponse
    {
        public string Message { get; set; }
        public int Tnumber { get; set; }
    }
    // Helper class to deserialize ProblemDetails from server error responses
    private class ProblemDetails
    {
        public string? Detail { get; set; }
        public string? Title { get; set; }
        public int? Status { get; set; }
        // Add other ProblemDetails fields if needed
    }


    // --- اصلاح شده: فراخوانی Endpoint اختصاصی ---
    // DTO برای ارسال داده‌های مورد نیاز به سرور
    public class RouteMappingRequest
    {
        public string? RouteName { get; set; }
        public int Tnumber { get; set; }
        public double Kol { get; set; } // یا نوع داده مناسب دیگر
        public int Moin { get; set; }
    }
    public async Task<List<ThePart1>?> GetCustomerStatementAsync(string hesabCode, long? startDate = null, long? endDate = null)
    {
        if (string.IsNullOrWhiteSpace(hesabCode))
        {
            _logger.LogWarning("GetCustomerStatementAsync called with empty hesabCode.");
            return null; // یا خطا throw کنید
        }

        try
        {
            // ساخت Query String برای تاریخ ها در صورت وجود مقدار
            var queryParams = HttpUtility.ParseQueryString(string.Empty);

            if (startDate.HasValue)
                queryParams["startDate"] = startDate.Value.ToString();
            if (endDate.HasValue)
                queryParams["endDate"] = endDate.Value.ToString();

            var queryString = queryParams.ToString();

            // ساخت URI نهایی درخواست
            // از Uri.EscapeDataString برای امن کردن hesabCode در URL استفاده می کنیم
            var requestUri = $"api/customers/{Uri.EscapeDataString(hesabCode)}/statement";
            System.Diagnostics.Debug.WriteLine($"[CustomerApi] Attempting to call HttpClient for: {requestUri}");
            if (!string.IsNullOrEmpty(queryString))
            {
                requestUri += $"?{queryString}"; // اضافه کردن Query String در صورت وجود
                System.Diagnostics.Debug.WriteLine($"[CustomerApi] Attempting to call HttpClient for: {requestUri}");

            }

            _logger.LogInformation("Calling API: {RequestUri}", requestUri);

            // ارسال درخواست GET و دریافت پاسخ به صورت List<ThePart1>
            var result = await _httpClient.GetFromJsonAsync<List<ThePart1>>(requestUri);

            _logger.LogInformation("Received {Count} statement items from API.", result?.Count ?? 0);
            return result;
        }
        catch (HttpRequestException ex) // خطاهای مربوط به درخواست HTTP
        {
            _logger.LogError(ex, "HTTP error fetching statement for HesabCode: {HesabCode} - StatusCode: {StatusCode}", hesabCode, ex.StatusCode);
            System.Diagnostics.Debug.WriteLine($"[CustomerApi] HttpRequestException for : Status Code: {ex.StatusCode}, Message: {ex.Message}");
            // می توانید null برگردانید یا خطا را برای مدیریت در UI دوباره throw کنید
            return null;
        }
        catch (Exception ex) // سایر خطاهای احتمالی (مثل خطای JSON Deserialization)
        {
            _logger.LogError(ex, "Generic error fetching statement for HesabCode: {HesabCode}", hesabCode);
            return null;
        }
    }

}