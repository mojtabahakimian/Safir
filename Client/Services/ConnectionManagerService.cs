using Blazored.LocalStorage;
using Safir.Shared.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Safir.Client.Services
{
    public class ConnectionManagerService
    {
        private const string ConnectionSettingsKey = "dbConnectionSettings";
        private readonly ILocalStorageService _localStorage;
        private readonly HttpClient _httpClient;

        public ConnectionManagerService(ILocalStorageService localStorage, HttpClient httpClient)
        {
            _localStorage = localStorage;
            _httpClient = httpClient;
        }

        public async Task<DbConnectionSettings?> GetSettingsAsync()
        {
            return await _localStorage.GetItemAsync<DbConnectionSettings>(ConnectionSettingsKey);
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

        private void ApplySettingsToHttpClient(DbConnectionSettings settings)
        {
            _httpClient.DefaultRequestHeaders.Remove("X-DB-Connection");
            if (!string.IsNullOrWhiteSpace(settings.Server) && !string.IsNullOrWhiteSpace(settings.Database))
            {
                var json = JsonSerializer.Serialize(settings);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                _httpClient.DefaultRequestHeaders.Add("X-DB-Connection", base64);
            }
        }
    }
}
