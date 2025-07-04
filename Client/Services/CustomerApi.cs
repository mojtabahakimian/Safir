using Safir.Shared.Models.Taarif;
using Safir.Shared.Models.Visitory;
using System.Net.Http.Json;
using Safir.Shared.Models.Hesabdari;
using System.Web;
using Safir.Shared.Models;

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

    public async Task<CustomerSaveResponseDto?> SaveCustomerAsync(CustomerModel customer)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/customers", customer);

            if (response.IsSuccessStatusCode)
            {
                // خواندن پاسخ به عنوان CustomerSaveResponseDto
                var result = await response.Content.ReadFromJsonAsync<CustomerSaveResponseDto>();
                _logger.LogInformation("Customer saved successfully via API. Message: {Message}, TNUMBER: {Tnumber}, HES: {Hes}",
                    result?.Message, result?.Tnumber, result?.Hes);
                return result; // برگرداندن آبجکت کامل پاسخ
            }
            else
            {
                // خواندن جزئیات خطا از بدنه پاسخ
                var errorContent = await response.Content.ReadAsStringAsync();
                ProblemDetails? problemDetails = null;
                try
                {
                    problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                }
                catch { /* Ignore if not ProblemDetails */ }

                string errorMessage = problemDetails?.Detail ?? problemDetails?.Title ?? $"خطا در ذخیره مشتری: {response.StatusCode}";
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict && problemDetails?.Detail != null)
                {
                    // پیام تکراری بودن از سرور خوانده شده
                    errorMessage = problemDetails.Detail;
                }

                _logger.LogError("Error saving customer. Status: {StatusCode}, Content: {ErrorContent}", response.StatusCode, errorContent);
                // برای سازگاری با ساختار قبلی که انتظار پیام خطا داشت، یک DTO با پیام خطا برمی‌گردانیم
                // یا می‌توانید null برگردانید و در CustomerDefine.razor.cs مدیریت کنید.
                // در اینجا، یک DTO با پیام خطا می‌سازیم:
                return new CustomerSaveResponseDto { Message = errorMessage, Tnumber = 0 }; // Tnumber = 0 نشان دهنده خطا
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during SaveCustomerAsync.");
            return new CustomerSaveResponseDto { Message = $"خطای پیش‌بینی نشده: {ex.Message}", Tnumber = 0 };
        }
    }

    // Helper class still needed
    private class SaveCustomerResponse
    {
        public string Message { get; set; }
        public int Tnumber { get; set; }
        public string? Hes { get; set; }
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
    public async Task<List<QDAFTARTAFZIL2_H>?> GetCustomerStatementAsync(string hesabCode, long? startDate = null, long? endDate = null)
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
            var result = await _httpClient.GetFromJsonAsync<List<QDAFTARTAFZIL2_H>>(requestUri);

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

    public async Task<byte[]?> GetCustomerStatementPdfBytesAsync(string hesabCode, long? startDate = null, long? endDate = null)
    {
        if (string.IsNullOrWhiteSpace(hesabCode))
        {
            _logger.LogWarning("GetCustomerStatementPdfBytesAsync called with empty hesabCode.");
            return null;
        }

        try
        {
            // ساخت Query String
            var queryParams = System.Web.HttpUtility.ParseQueryString(string.Empty);
            if (startDate.HasValue)
                queryParams["startDate"] = startDate.Value.ToString();
            if (endDate.HasValue)
                queryParams["endDate"] = endDate.Value.ToString();
            var queryString = queryParams.ToString();

            // ساخت URI نهایی - دقت کنید که مسیر باید با کنترلر سرور مچ باشد
            var requestUri = $"api/Customers/{Uri.EscapeDataString(hesabCode)}/statement/pdf";
            if (!string.IsNullOrEmpty(queryString))
            {
                requestUri += $"?{queryString}";
            }

            _logger.LogInformation("Calling API to get PDF bytes: {RequestUri}", requestUri);

            // ارسال درخواست GET و دریافت پاسخ
            var response = await _httpClient.GetAsync(requestUri);

            if (response.IsSuccessStatusCode)
            {
                // خواندن محتوای پاسخ به صورت آرایه بایت
                var pdfBytes = await response.Content.ReadAsByteArrayAsync();
                _logger.LogInformation("Received {Size} PDF bytes from API.", pdfBytes.Length);
                return pdfBytes;
            }
            else
            {
                // لاگ کردن خطا بر اساس کد وضعیت
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get PDF bytes from API. Status: {StatusCode}, Reason: {ReasonPhrase}, Content: {ErrorContent}",
                                response.StatusCode, response.ReasonPhrase, errorContent);
                return null; // یا throw کنید
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting PDF bytes for HesabCode: {HesabCode} - StatusCode: {StatusCode}", hesabCode, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generic error getting PDF bytes for HesabCode: {HesabCode}", hesabCode);
            return null;
        }
    }

    // --- START: Code to Add ---
    /// <summary>
    /// وضعیت مسدودی حساب مشتری را از سرور استعلام می‌کند.
    /// </summary>
    /// <param name="hesabCode">کد حساب مشتری (HES)</param>
    /// <returns>True اگر مشتری مسدود باشد، False در غیر این صورت یا در صورت بروز خطا.</returns>
    public async Task<bool> CheckCustomerBlockedAsync(string hesabCode)
    {
        // بررسی اولیه کد حساب در سمت کلاینت
        if (string.IsNullOrWhiteSpace(hesabCode))
        {
            _logger.LogWarning("CheckCustomerBlockedAsync called with empty hesabCode.");
            return false; // پیش‌فرض: مسدود نیست اگر کد نامعتبر است
        }

        // ساخت آدرس API با استفاده از EscapeDataString برای امنیت
        string requestUri = $"api/customers/{Uri.EscapeDataString(hesabCode)}/is-blocked";
        _logger.LogInformation("Client API Call: Checking block status from {RequestUri}", requestUri);

        try
        {
            // فراخوانی API و دریافت مستقیم نتیجه boolean
            // GetFromJsonAsync در صورت موفقیت (کد 200 OK) مقدار boolean را برمی‌گرداند
            bool isBlocked = await _httpClient.GetFromJsonAsync<bool>(requestUri);
            _logger.LogInformation("Client API Response: Block status for {HesabCode}: {IsBlocked}", hesabCode, isBlocked);
            return isBlocked;
        }
        catch (HttpRequestException httpEx)
        {
            // خطاهای HTTP مانند 404 (یافت نشد) یا 500 (خطای سرور)
            _logger.LogError(httpEx, "Client API Error: HTTP error checking block status for {HesabCode}. Status: {StatusCode}", hesabCode, httpEx.StatusCode);
            // در صورت بروز خطا، فرض می‌کنیم مسدود نیست تا جلوی کار کاربر بیهوده گرفته نشود،
            // اما خطا را لاگ می‌کنیم. می‌توان پیام خطا به کاربر نشان داد.
            // Snackbar.Add("خطا در بررسی وضعیت مسدودی حساب.", Severity.Warning);
            return false;
        }
        catch (System.Text.Json.JsonException jsonEx) // خطای احتمالی در تبدیل پاسخ JSON
        {
            _logger.LogError(jsonEx, "Client API Error: JSON parsing error checking block status for {HesabCode}.", hesabCode);
            return false;
        }
        catch (Exception ex) // سایر خطاهای پیش‌بینی نشده
        {
            _logger.LogError(ex, "Client API Error: Generic error checking block status for {HesabCode}", hesabCode);
            return false;
        }
    }

    public async Task<PagedResult<VISITOR_CUSTOMERS>?> GetActiveCustomersForUserAsync(int pageNumber, int pageSize, string? searchTerm)
    {
        try
        {
            // اطمینان از اینکه پارامترهای کوئری به درستی به URL اضافه می‌شوند
            var queryParams = System.Web.HttpUtility.ParseQueryString(string.Empty);
            queryParams["pageNumber"] = pageNumber.ToString();
            queryParams["pageSize"] = pageSize.ToString();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                queryParams["searchTerm"] = searchTerm;
            }
            var requestUri = $"api/customers/list-for-user?{queryParams}";

            _logger.LogInformation("Client API Call: Fetching active customers from {RequestUri}", requestUri);
            var result = await _httpClient.GetFromJsonAsync<PagedResult<VISITOR_CUSTOMERS>>(requestUri);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Client API Error: HTTP error fetching active customers. Status: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client API Error: Generic error fetching active customers.");
            return null;
        }
    }
    // --- END: Code to Add ---
}