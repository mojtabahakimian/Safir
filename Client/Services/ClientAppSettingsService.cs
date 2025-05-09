// File: Client/Services/ClientAppSettingsService.cs
using Safir.Shared.Models;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; // اختیاری برای لاگ

namespace Safir.Client.Services
{
    public class ClientAppSettingsService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ClientAppSettingsService> _logger; // اختیاری
        private SAZMAN? _cachedSettings = null;
        private bool _isLoading = false;
        private bool _isLoaded = false;
        private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1); // برای جلوگیری از فراخوانی همزمان

        public ClientAppSettingsService(HttpClient httpClient, ILogger<ClientAppSettingsService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        // متد اصلی برای دریافت و کش کردن تنظیمات
        public async Task EnsureSettingsLoadedAsync()
        {
            if (_isLoaded) return; // اگر قبلا لود شده، خارج شو

            await _initLock.WaitAsync(); // منتظر ماندن برای دسترسی انحصاری
            try
            {
                // دوباره چک کن چون ممکن است ترد دیگری در زمان انتظار آن را لود کرده باشد
                if (_isLoaded) return;

                _isLoading = true;
                _logger?.LogInformation("Client: Attempting to load application settings from API...");
                try
                {
                    // فراخوانی API
                    _cachedSettings = await _httpClient.GetFromJsonAsync<SAZMAN>("api/appsettings");
                    _isLoaded = true;
                    _logger?.LogInformation("Client: Application settings loaded successfully.");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Client: Failed to load application settings from API.");
                    // در صورت خطا، _cachedSettings null باقی می‌ماند و _isLoaded false
                    // می‌توانید اینجا مکانیزم retry یا پیام خطا به کاربر را پیاده‌سازی کنید
                }
                finally
                {
                    _isLoading = false;
                }
            }
            finally
            {
                _initLock.Release(); // آزاد کردن قفل
            }
        }

        // متد برای دسترسی به تنظیمات (ابتدا از لود شدن مطمئن می‌شود)
        public async Task<SAZMAN?> GetSettingsAsync()
        {
            await EnsureSettingsLoadedAsync();
            return _cachedSettings;
        }


        public async Task<int?> Get_BEDEHKAR_Async()
        {
            var settings = await GetSettingsAsync();
            return settings?.BEDEHKAR; // نام فیلد را مطابق مدل SAZMAN تنظیم کنید
        }

        // سایر متدهای Get... برای فیلدهای دیگر
    }
}