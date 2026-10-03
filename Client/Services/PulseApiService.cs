using System.Net;
using System.Net.Http.Json;
using Safir.Shared.Models.Pulse;

namespace Safir.Client.Services
{
    public class PulseApiService
    {
        private readonly HttpClient _http;
        public PulseApiService(HttpClient http) => _http = http;

        public async Task<(PulseDto? Data, string? Error)> LoadAsync()
        {
            var res = await _http.GetAsync("api/pulse");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<PulseDto>(), null);
        }

        private static async Task<string> ErrorText(HttpResponseMessage res)
        {
            var text = (await res.Content.ReadAsStringAsync()).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(text) && text.Length < 300 && !text.StartsWith("{")) return text;
            return res.StatusCode switch
            {
                HttpStatusCode.Forbidden => "اجازه‌ی دیدنِ نبض سازمان را ندارید.",
                HttpStatusCode.Unauthorized => "لطفاً دوباره وارد شوید.",
                _ => $"خطای سرور ({(int)res.StatusCode})."
            };
        }
    }
}
