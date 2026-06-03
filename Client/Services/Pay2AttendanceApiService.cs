using System.Net.Http.Json;
using Safir.Shared.Models.Salary;

namespace Safir.Client.Services
{
    public class Pay2AttendanceApiService
    {
        private readonly HttpClient _http;
        public Pay2AttendanceApiService(HttpClient http) => _http = http;

        public async Task<Pay2AttendanceSaveRequest> InitPeriodAsync(int wsId, long periodDate)
            => await _http.GetFromJsonAsync<Pay2AttendanceSaveRequest>($"api/pay2/attendance/init?wsId={wsId}&periodDate={periodDate}") ?? new();

        public async Task SaveBulkAsync(Pay2AttendanceSaveRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/attendance/save", request);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task<List<Pay2AttValueDto>> GetDynamicValuesAsync(int perId, int empId)
            => await _http.GetFromJsonAsync<List<Pay2AttValueDto>>($"api/pay2/attendance/dynamic-values?perId={perId}&empId={empId}") ?? new();

        public async Task SaveDynamicValuesAsync(int perId, int empId, List<Pay2AttValueDto> values)
        {
            var res = await _http.PostAsJsonAsync($"api/pay2/attendance/dynamic-values/save?perId={perId}&empId={empId}", values);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task ClosePeriodAsync(int perId)
        {
            var res = await _http.PostAsync($"api/pay2/attendance/close-period/{perId}", null);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }
        public async Task<List<Pay2PeriodLookupDto>> GetPeriodsAsync(int wsId) => await _http.GetFromJsonAsync<List<Pay2PeriodLookupDto>>($"api/pay2/attendance/periods?wsId={wsId}") ?? new();

        public async Task DeletePeriodAsync(int perId)
        {
            var res = await _http.DeleteAsync($"api/pay2/attendance/period/{perId}");
            if (!res.IsSuccessStatusCode)
            {
                var msg = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(msg) ? "خطا در حذف دوره" : msg);
            }
        }
    }
}