using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Blazored.LocalStorage;
using Safir.Shared.Models;

namespace Safir.Client.Services
{
    public class ConnectionManagerService
    {
        const string ConnectionSettingsKey = "dbConnectionSettings";
        readonly ILocalStorageService _localStorage;
        readonly HttpClient _httpClient;
        // دیتابیس هر تب جداست (TabSession)؛ تغییر آن در یک تب، تب دیگر را جابه‌جا نمی‌کند
        readonly TabSession _tab;

        public ConnectionManagerService(ILocalStorageService localStorage, HttpClient httpClient, TabSession tab)
        {
            _localStorage = localStorage;
            _httpClient = httpClient;
            _tab = tab;
        }

        public async Task<DbConnectionSettings?> GetSettingsAsync()
        {
            return await _tab.GetAsync<DbConnectionSettings>(ConnectionSettingsKey);
        }

        // 🚀 متد جدید: دریافت تنظیمات موثر (یا از لوکال استوریج یا از پیش‌فرض سرور)
        public async Task<DbConnectionSettings> GetEffectiveDbSettingsAsync()
        {
            // 1. ابتدا چک می‌کنیم آیا کاربر تنظیمات دستی وارد کرده است؟
            var localSettings = await GetSettingsAsync();
            if (localSettings != null && !string.IsNullOrWhiteSpace(localSettings.Server))
            {
                return localSettings;
            }

            // 2. اگر تنظیمات دستی نبود، از سرور می‌پرسیم که به کجا وصل است
            try
            {
                var debugInfo = await _httpClient.GetFromJsonAsync<DebugInfoDto>("api/settings/debug-info");
                if (debugInfo != null)
                {
                    return new DbConnectionSettings
                    {
                        Server = debugInfo.DatabaseServer ?? "سرور پیش‌فرض",
                        Database = debugInfo.DatabaseName ?? "دیتابیس پیش‌فرض"
                    };
                }
            }
            catch
            {
                // نادیده گرفتن خطا در صورت عدم دسترسی به سرور
            }

            return new DbConnectionSettings { Server = "نامشخص", Database = "نامشخص" };
        }

        public async Task SaveSettingsAsync(DbConnectionSettings settings)
        {
            await _tab.SetAsync(ConnectionSettingsKey, settings);
            ApplySettingsToHttpClient(settings);
        }

        public async Task ClearSettingsAsync()
        {
            await _tab.RemoveAsync<DbConnectionSettings>(ConnectionSettingsKey);
            _httpClient.DefaultRequestHeaders.Remove("X-DB-Connection");
        }

        /// <summary>
        /// «تست اتصال»: هدر را فقط برای همین آزمون عوض می‌کند و بعد به تنظیم ذخیره‌شده‌ی تب
        /// برمی‌گرداند. قبلاً تست تنظیم را ذخیره می‌کرد و آزمودنِ دیتابیس دیگر در تبی که وارد
        /// شده بود، آن تب را به شرکت دیگر می‌برد (و حالا که توکن به دیتابیس گره خورده، بیرونش می‌انداخت).
        /// </summary>
        public async Task<T> WithTemporarySettingsAsync<T>(DbConnectionSettings settings, Func<Task<T>> action)
        {
            ApplySettingsToHttpClient(settings);
            try { return await action(); }
            finally
            {
                var saved = await GetSettingsAsync();
                if (saved != null) ApplySettingsToHttpClient(saved);
                else _httpClient.DefaultRequestHeaders.Remove("X-DB-Connection");
            }
        }

        public async Task LoadSettingsAsync()
        {
            var settings = await GetSettingsAsync();
            if (settings != null)
            {
                ApplySettingsToHttpClient(settings);
            }
        }

        void ApplySettingsToHttpClient(DbConnectionSettings settings)
        {
            _httpClient.DefaultRequestHeaders.Remove("X-DB-Connection");
            if (!string.IsNullOrWhiteSpace(settings.Server) && !string.IsNullOrWhiteSpace(settings.Database))
            {
                var json = JsonSerializer.Serialize(settings);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                _httpClient.DefaultRequestHeaders.Add("X-DB-Connection", base64);
            }
        }

        // DTO کمکی برای خواندن اطلاعات از سرور
        private class DebugInfoDto
        {
            public string? EnvironmentName { get; set; }
            public string? DatabaseServer { get; set; }
            public string? DatabaseName { get; set; }
        }
    }
}