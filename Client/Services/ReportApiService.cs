using Safir.Shared.Models;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class ReportApiService
    {
        private readonly HttpClient _http;

        public ReportApiService(HttpClient http) => _http = http;

        public async Task<byte[]?> GeneratePdfAsync(string reportName, Dictionary<string, object> parameters)
        {
            var req = new ReportRequest
            {
                ReportName = reportName,
                Parameters = parameters
            };

            var resp = await _http.PostAsJsonAsync("api/reports/generate", req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
    }
}
