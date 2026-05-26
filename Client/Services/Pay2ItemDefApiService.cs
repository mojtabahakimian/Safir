using Safir.Shared.Models.Salary;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class Pay2ItemDefApiService
    {
        private readonly HttpClient _http;

        public Pay2ItemDefApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<Pay2ItemDefDto>> GetItemDefsAsync()
            => await _http.GetFromJsonAsync<List<Pay2ItemDefDto>>("api/pay2/itemdefs") ?? new();

        public async Task<int> SaveItemDefAsync(Pay2ItemDefDto item)
        {
            var response = await _http.PostAsJsonAsync("api/pay2/itemdefs/save", item);
            if (!response.IsSuccessStatusCode)
            {
                var msg = await response.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(msg) ? "خطا در ذخیره آیتم" : msg);
            }
            return await response.Content.ReadFromJsonAsync<int>();
        }

        public async Task DeleteItemDefAsync(int id)
        {
            var response = await _http.DeleteAsync($"api/pay2/itemdefs/{id}");
            if (!response.IsSuccessStatusCode)
            {
                var msg = await response.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(msg) ? "خطا در حذف آیتم" : msg);
            }
        }
    }
}