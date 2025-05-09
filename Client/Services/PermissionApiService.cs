using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class PermissionApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PermissionApiService> _logger; // برای لاگ خطا

        public PermissionApiService(HttpClient httpClient, ILogger<PermissionApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        // این متد true یا false را بر اساس پاسخ API برمی‌گرداند
        public async Task<bool> CanRunFormAsync(string formCode)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/permissions/check/{formCode}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                else
                {
                    // لاگ کردن وضعیت خطا (مانند 401, 403, 404, 500)
                    _logger.LogWarning("Permission check failed for {FormCode}. Status: {StatusCode}", formCode, response.StatusCode);
                    return false; // اگر دسترسی نبود یا خطا داد، false در نظر بگیر
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling permission check API for {FormCode}", formCode);
                return false; // در صورت بروز خطا، دسترسی را false در نظر بگیر
            }
        }
    }
}
