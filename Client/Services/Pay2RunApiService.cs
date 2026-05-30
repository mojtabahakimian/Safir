using Safir.Shared.Models.Salary;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class Pay2RunApiService
    {
        private readonly HttpClient _http;
        public Pay2RunApiService(HttpClient http) => _http = http;

        public async Task<Pay2PeriodDto?> GetPeriodInfoAsync(int wsId, long periodDate)
        {
            var res = await _http.GetAsync($"api/pay2/run/period-info?wsId={wsId}&periodDate={periodDate}");

            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            try
            {
                return await res.Content.ReadFromJsonAsync<Pay2PeriodDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                // در صورتی که بادی کاملاً خالی باشد
                return null;
            }
        }

        public async Task<Pay2RunDto?> GetLatestRunAsync(int perId)
        {
            var res = await _http.GetAsync($"api/pay2/run/latest?perId={perId}");

            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            try
            {
                return await res.Content.ReadFromJsonAsync<Pay2RunDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        public async Task<List<Pay2RunLineDto>> GetRunLinesAsync(int runId)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/lines");
            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
                return new List<Pay2RunLineDto>();

            try
            {
                return await res.Content.ReadFromJsonAsync<List<Pay2RunLineDto>>() ?? new List<Pay2RunLineDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                return new List<Pay2RunLineDto>();
            }
        }

        public async Task<int> CalculateRunAsync(Pay2RunCalcRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/run/calculate", request);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
            return await res.Content.ReadFromJsonAsync<int>();
        }

        public async Task RevertRunAsync(int runId)
        {
            var res = await _http.PutAsync($"api/pay2/run/{runId}/revert", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task FinalizeRunAsync(int runId)
        {
            var res = await _http.PutAsync($"api/pay2/run/{runId}/finalize", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task GenerateDeedAsync(int runId)
        {
            var res = await _http.PostAsync($"api/pay2/run/{runId}/generate-deed", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }
    }
}