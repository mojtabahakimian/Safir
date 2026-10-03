using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Safir.Shared.Models.Hesabdari;

namespace Safir.Client.Services
{
    public class TrialBalanceApiService
    {
        private readonly HttpClient _http;
        public TrialBalanceApiService(HttpClient http) => _http = http;

        private const string Base = "api/trial-balance";

        public async Task<(TrialBalanceMetaDto? Meta, string? Error)> MetaAsync()
        {
            var res = await _http.GetAsync($"{Base}/meta");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<TrialBalanceMetaDto>(), null);
        }

        public async Task<(List<TrialBalanceRowDto>? Rows, string? Error)> LoadAsync(TrialBalanceQuery q, CancellationToken ct = default)
        {
            var res = await _http.GetAsync($"{Base}?{QueryString(q)}", ct);
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<List<TrialBalanceRowDto>>(cancellationToken: ct), null);
        }

        public async Task<(List<TrialBalanceMonthlyRowDto>? Rows, string? Error)> MonthlyAsync(TrialBalanceQuery q, CancellationToken ct = default)
        {
            var res = await _http.GetAsync($"{Base}/monthly?{QueryString(q)}", ct);
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<List<TrialBalanceMonthlyRowDto>>(cancellationToken: ct), null);
        }

        /// <summary>بدون kol: حساب‌های کل؛ با kol: معین‌های آن کل. خطا = فهرست خالی.</summary>
        public async Task<List<TrialBalanceAccountDto>> AccountsAsync(int? kol = null)
        {
            try { return await _http.GetFromJsonAsync<List<TrialBalanceAccountDto>>($"{Base}/accounts{(kol is null ? "" : $"?kol={kol}")}") ?? new(); }
            catch { return new(); }
        }

        public static string QueryString(TrialBalanceQuery q)
        {
            var p = new List<string> { $"level={q.Level}", $"from={q.From}", $"to={q.To}" };
            void Add(string k, object? v) { if (v is not null) p.Add($"{k}={Convert.ToString(v, CultureInfo.InvariantCulture)}"); }
            Add("sanadFrom", q.SanadFrom); Add("sanadTo", q.SanadTo);
            Add("kol", q.Kol); Add("moin", q.Moin); Add("tafsili", q.Tafsili);
            if (q.AllMoins) p.Add("allMoins=true");
            Add("tafsili2", q.Tafsili2); Add("tafsili3", q.Tafsili3);
            return string.Join("&", p);
        }

        private static async Task<string> ErrorText(HttpResponseMessage res)
        {
            var text = (await res.Content.ReadAsStringAsync()).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(text) && text.Length < 300 && !text.StartsWith("{")) return text;
            return res.StatusCode switch
            {
                HttpStatusCode.Forbidden => "اجازه‌ی دیدن این تراز را ندارید.",
                HttpStatusCode.Unauthorized => "لطفاً دوباره وارد شوید.",
                _ => $"خطای سرور ({(int)res.StatusCode})."
            };
        }
    }
}
