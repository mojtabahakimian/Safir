using Safir.Shared.Models.CostClose;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class ItemConversionApiService
    {
        private readonly HttpClient _http;
        public ItemConversionApiService(HttpClient http) => _http = http;

        private const string Base = "api/item-conversion";

        public async Task<List<ConversionRowDto>> ListAsync(long? dt1 = null, long? dt2 = null)
        {
            var q = new List<string>();
            if (dt1 is not null) q.Add($"dt1={dt1}");
            if (dt2 is not null) q.Add($"dt2={dt2}");
            var url = Base + (q.Count > 0 ? "?" + string.Join("&", q) : "");

            return await _http.GetFromJsonAsync<List<ConversionRowDto>>(url) ?? new();
        }

        public async Task<ConversionPreviewDto?> PreviewAsync(CreateConversionRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/preview", req);
            return res.IsSuccessStatusCode
                ? await res.Content.ReadFromJsonAsync<ConversionPreviewDto>()
                : null;
        }

        public async Task<ConversionResultDto> CreateAsync(CreateConversionRequest req)
        {
            var res = await _http.PostAsJsonAsync(Base, req);

            // هر دو حالت همان DTO را برمی‌گردانند؛ خطا هم ساختاردار است تا
            // صفحه مجبور نباشد متن خام HTTP را نشان بدهد.
            var dto = await res.Content.ReadFromJsonAsync<ConversionResultDto>();
            return dto ?? new ConversionResultDto { Ok = false, Error = "پاسخ نامعتبر از سرور." };
        }

        public async Task<(bool Ok, string? Error)> DeleteAsync(double number)
        {
            var res = await _http.DeleteAsync($"{Base}/{number}");
            return res.IsSuccessStatusCode
                ? (true, null)
                : (false, await res.Content.ReadAsStringAsync());
        }
    }
}
