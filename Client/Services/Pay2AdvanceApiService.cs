using System.Net.Http.Json;
using Safir.Shared.Models.Salary;

namespace Safir.Client.Services
{
    public class Pay2AdvanceApiService
    {
        private readonly HttpClient _http;

        public Pay2AdvanceApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<Pay2PeriodLookupDto>> GetPeriodsAsync(int wsId)
            => await _http.GetFromJsonAsync<List<Pay2PeriodLookupDto>>(
                $"api/pay2/attendance/periods?wsId={wsId}") ?? new();

        public async Task<Pay2SmartAdvanceSettingsDto> GetSettingsAsync(int wsId)
            => await _http.GetFromJsonAsync<Pay2SmartAdvanceSettingsDto>(
                $"api/pay2/advances/settings?wsId={wsId}") ?? new Pay2SmartAdvanceSettingsDto { WS_ID = wsId };

        public async Task SaveSettingsAsync(Pay2SmartAdvanceSettingsDto settings)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/advances/settings/save", settings);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task<List<Pay2SmartAdvanceRowDto>> CalculateAsync(Pay2SmartAdvanceCalcRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/advances/calculate", request);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());

            return await res.Content.ReadFromJsonAsync<List<Pay2SmartAdvanceRowDto>>() ?? new();
        }

        public async Task<List<Pay2AdvanceExclDto>> GetEmployeeExclsAsync(int empId)
            => await _http.GetFromJsonAsync<List<Pay2AdvanceExclDto>>(
                $"api/pay2/advances/employee/{empId}/excls") ?? new();

        public async Task SaveExclAsync(Pay2AdvanceExclDto excl)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/advances/excl/save", excl);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task DeleteExclAsync(int exclId)
        {
            var res = await _http.DeleteAsync($"api/pay2/advances/excl/{exclId}");
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }
    }
}