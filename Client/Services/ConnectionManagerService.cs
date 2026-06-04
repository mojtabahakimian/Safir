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

        public ConnectionManagerService(ILocalStorageService localStorage, HttpClient httpClient)
        {
            _localStorage = localStorage;
            _httpClient = httpClient;
        }

        public async Task<DbConnectionSettings?> GetSettingsAsync()
        {
            return await _localStorage.GetItemAsync<DbConnectionSettings>(ConnectionSettingsKey);
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
            await _localStorage.SetItemAsync(ConnectionSettingsKey, settings);
            ApplySettingsToHttpClient(settings);
        }

        public async Task ClearSettingsAsync()
        {
            await _localStorage.RemoveItemAsync(ConnectionSettingsKey);
            _httpClient.DefaultRequestHeaders.Remove("X-DB-Connection");
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