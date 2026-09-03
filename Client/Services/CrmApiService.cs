using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Crm;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;

namespace Safir.Client.Services
{
    public class CrmApiService : ICrmApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CrmApiService> _logger;

        public CrmApiService(HttpClient httpClient, ILogger<CrmApiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }

        public async Task<List<CrmCompanyDto>> GetCompaniesAsync(CrmFilterDto filter)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/crm/companies", filter);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<CrmCompanyDto>>() ?? new List<CrmCompanyDto>();
                }
                _logger.LogWarning("Failed to get CRM companies. Status: {StatusCode}", response.StatusCode);
                return new List<CrmCompanyDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling api/crm/companies");
                return new List<CrmCompanyDto>();
            }
        }

        public async Task<CrmCompanyDto?> GetCompanyByIdAsync(int id)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<CrmCompanyDto>($"api/crm/companies/{id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling api/crm/companies/{Id}", id);
                return null;
            }
        }

        public async Task<int> SaveCompanyAsync(CrmCompanyDto company)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/crm/save-company", company);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<int>();
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CRM company");
                return 0;
            }
        }

        public async Task<bool> DeleteCompanyAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/crm/companies/{id}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CRM company {Id}", id);
                return false;
            }
        }

        public async Task<List<CrmEventDto>> GetEventsByCompanyIdAsync(int companyId)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<CrmEventDto>>($"api/crm/events/{companyId}") ?? new List<CrmEventDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM events for company {CompanyId}", companyId);
                return new List<CrmEventDto>();
            }
        }

        public async Task<int> SaveEventAsync(CrmEventDto crmEvent)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/crm/save-event", crmEvent);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<int>();
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CRM event");
                return 0;
            }
        }

        public async Task<bool> DeleteEventAsync(int eventId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/crm/events/{eventId}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CRM event {EventId}", eventId);
                return false;
            }
        }

        public async Task<List<CrmStatusDto>> GetStatusListAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<CrmStatusDto>>("api/crm/status-list") ?? new List<CrmStatusDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM status list");
                return new List<CrmStatusDto>();
            }
        }

        public async Task<CrmDashboardSummaryDto> GetDashboardSummaryAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<CrmDashboardSummaryDto>("api/crm/dashboard-summary") ?? new CrmDashboardSummaryDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM dashboard summary");
                return new CrmDashboardSummaryDto();
            }
        }

        public async Task<CrmDuplicateCheckResultDto> CheckDuplicateAsync(string? companyName, string? tel, string? mobile, int? excludeCompanyId = null)
        {
            try
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                if (!string.IsNullOrWhiteSpace(companyName)) query["companyName"] = companyName;
                if (!string.IsNullOrWhiteSpace(tel)) query["tel"] = tel;
                if (!string.IsNullOrWhiteSpace(mobile)) query["mobile"] = mobile;
                if (excludeCompanyId.HasValue) query["excludeCompanyId"] = excludeCompanyId.Value.ToString();

                var uri = $"api/crm/check-duplicate?{query}";
                return await _httpClient.GetFromJsonAsync<CrmDuplicateCheckResultDto>(uri) ?? new CrmDuplicateCheckResultDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking duplicates");
                return new CrmDuplicateCheckResultDto();
            }
        }

        public async Task<List<CrmPhoneBookItemDto>> SearchPhoneBookAsync(string query)
        {
            try
            {
                var q = HttpUtility.UrlEncode(query ?? string.Empty);
                return await _httpClient.GetFromJsonAsync<List<CrmPhoneBookItemDto>>($"api/crm/phonebook?query={q}") ?? new List<CrmPhoneBookItemDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching CRM phone book");
                return new List<CrmPhoneBookItemDto>();
            }
        }

        public async Task<List<string>> GetDistinctSalersAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<string>>("api/crm/distinct-salers") ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting distinct salers");
                return new List<string>();
            }
        }

        public async Task<List<string>> GetDistinctBuyersAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<string>>("api/crm/distinct-buyers") ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting distinct buyers");
                return new List<string>();
            }
        }

        public async Task<List<string>> GetDistinctStatusFactsAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<string>>("api/crm/distinct-status-facts") ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting distinct status facts");
                return new List<string>();
            }
        }

        public async Task<List<CrmNoteDto>> GetNotesAsync(bool onlyPending = true)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<CrmNoteDto>>($"api/crm/notes?onlyPending={onlyPending}") ?? new List<CrmNoteDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM notes");
                return new List<CrmNoteDto>();
            }
        }

        public async Task<int> SaveNoteAsync(CrmNoteDto note)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/crm/save-note", note);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<int>();
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CRM note");
                return 0;
            }
        }

        public async Task<bool> ToggleNoteDoneAsync(int noteId, bool done)
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/crm/toggle-note?noteId={noteId}&done={done}", null);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling CRM note {NoteId}", noteId);
                return false;
            }
        }

        public async Task<bool> DeleteNoteAsync(int noteId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/crm/notes/{noteId}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CRM note {NoteId}", noteId);
                return false;
            }
        }

        public async Task<bool> SendSmsAsync(CrmSendSmsRequestDto request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/crm/send-sms", request);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS in CRM");
                return false;
            }
        }
    }
}
