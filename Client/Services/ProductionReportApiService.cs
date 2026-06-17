using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Safir.Shared.Models.Production;

namespace Safir.Client.Services
{
    public interface IProductionReportApiService
    {
        Task<List<ProductionReportDto>> GetReportsAsync();
        Task SaveReportAsync(ProductionReportDto report);
        Task DeleteReportAsync(int id);
    }

    public class ProductionReportApiService : IProductionReportApiService
    {
        private readonly HttpClient _http;

        public ProductionReportApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<ProductionReportDto>> GetReportsAsync()
        {
            return await _http.GetFromJsonAsync<List<ProductionReportDto>>("api/ProductionReports");
        }

        public async Task SaveReportAsync(ProductionReportDto report)
        {
            HttpResponseMessage response;
            if (report.Id == 0)
                response = await _http.PostAsJsonAsync("api/ProductionReports", report);
            else
                response = await _http.PutAsJsonAsync($"api/ProductionReports/{report.Id}", report);

            response.EnsureSuccessStatusCode();
        }

        public async Task DeleteReportAsync(int id)
        {
            var response = await _http.DeleteAsync($"api/ProductionReports/{id}");
            response.EnsureSuccessStatusCode();
        }
    }
}