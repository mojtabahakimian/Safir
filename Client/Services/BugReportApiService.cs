using Safir.Shared.Models.BugReport;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Safir.Client.Services
{
    public class BugReportApiService
    {
        private readonly HttpClient _httpClient;

        public BugReportApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<HttpResponseMessage> SubmitBugReportAsync(BugReportDto bugReport)
        {
            return await _httpClient.PostAsJsonAsync("api/BugReport/submit", bugReport);
        }
    }
}
