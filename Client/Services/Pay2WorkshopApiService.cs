using Safir.Shared.Models.Salary;
using System.Net.Http.Json;

namespace Safir.Client.Services;

public class Pay2WorkshopApiService
{
    private readonly HttpClient _http;

    public Pay2WorkshopApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Pay2WorkshopDto>> GetWorkshopsAsync()
        => await _http.GetFromJsonAsync<List<Pay2WorkshopDto>>("api/pay2/workshops")
           ?? new List<Pay2WorkshopDto>();

    public async Task<Pay2WorkshopAccDto> GetAccountsAsync(int wsId)
        => await _http.GetFromJsonAsync<Pay2WorkshopAccDto>($"api/pay2/workshops/{wsId}/accounts")
           ?? new Pay2WorkshopAccDto { WS_ID = wsId };

    public async Task<int> SaveAsync(Pay2WorkshopSaveRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/pay2/workshops/save", request);
        if (!response.IsSuccessStatusCode)
        {
            var msg = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrWhiteSpace(msg) ? "خطای ناشناخته" : msg);
        }
        return await response.Content.ReadFromJsonAsync<int>();
    }
}