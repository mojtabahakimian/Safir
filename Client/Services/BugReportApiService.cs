using Safir.Shared.Models.BugReport;
using System.Collections.Generic;
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

        public async Task<List<BugReportDto>?> GetBugReportsAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<BugReportDto>>("api/BugReport");
        }

        public async Task<BugReportDto?> GetBugReportByIdAsync(int id)
        {
            return await _httpClient.GetFromJsonAsync<BugReportDto>($"api/BugReport/{id}");
        }

        public async Task<HttpResponseMessage> UpdateBugReportStatusAsync(int id, string status, string? adminNote)
        {
            return await _httpClient.PatchAsJsonAsync($"api/BugReport/{id}/status", new { Status = status, AdminNote = adminNote });
        }
        public async Task<System.Collections.Generic.List<BugReportDto>?> GetMyBugReportsAsync()
        {
            return await _httpClient.GetFromJsonAsync<System.Collections.Generic.List<BugReportDto>>("api/BugReport/my-reports");
        }

        public async Task<HttpResponseMessage> UpdateBugReportUserNoteAsync(int id, string userNote)
        {
            return await _httpClient.PatchAsJsonAsync($"api/BugReport/{id}/usernote", new { UserNote = userNote });
        }
    }
}
