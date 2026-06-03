using System.Net.Http.Json;
using Safir.Shared.Models.Salary;

namespace Safir.Client.Services
{
    public class Pay2DashboardApiService
    {
        readonly HttpClient _http;

        public Pay2DashboardApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<Pay2DashboardDataDto> GetDashboardDataAsync(int wsId)
        {
            var response = await _http.GetAsync($"api/pay2/dashboard/{wsId}");
            if (!response.IsSuccessStatusCode)
            {
                var msg = await response.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(msg) ? "خطا در دریافت اطلاعات داشبورد" : msg);
            }
            return await response.Content.ReadFromJsonAsync<Pay2DashboardDataDto>() ?? new Pay2DashboardDataDto();
        }
    }
}