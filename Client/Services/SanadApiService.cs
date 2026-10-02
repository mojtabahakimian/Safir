using System.Globalization;
using System.Net.Http.Json;
using Safir.Shared.Models.Sanad;
using Safir.Shared.Models.Treasury;

namespace Safir.Client.Services
{
    /// <summary>API صدور و ویرایش اسناد. خطاهای سرور (فارسی) عیناً به صفحه برمی‌گردند.</summary>
    public class SanadApiService
    {
        private readonly HttpClient _http;
        public SanadApiService(HttpClient http) => _http = http;

        private const string Base = "api/sanad";
        private static string N(double ns) => ns.ToString("0", CultureInfo.InvariantCulture);

        public async Task<SanadMetaDto?> MetaAsync() => await _http.GetFromJsonAsync<SanadMetaDto>($"{Base}/meta");

        public async Task<(List<SanadListItemDto>? Items, string? Error)> ListAsync(long? from, long? to, string? kind, string? q)
        {
            var p = new List<string>();
            if (from is > 0) p.Add($"from={from}");
            if (to is > 0) p.Add($"to={to}");
            if (!string.IsNullOrWhiteSpace(kind)) p.Add($"kind={kind}");
            if (!string.IsNullOrWhiteSpace(q)) p.Add($"q={Uri.EscapeDataString(q)}");
            var res = await _http.GetAsync(Base + (p.Count > 0 ? "?" + string.Join("&", p) : ""));
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<List<SanadListItemDto>>(), null);
        }

        public async Task<(SanadDetailDto? Item, string? Error)> GetAsync(double ns)
        {
            var res = await _http.GetAsync($"{Base}/{N(ns)}");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<SanadDetailDto>(), null);
        }

        public async Task<double?> ByBaseAsync(int @base)
        {
            var res = await _http.GetAsync($"{Base}/by-base/{@base}");
            return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<double>() : null;
        }

        public async Task<double?> NeighbourAsync(double ns, bool next)
        {
            var res = await _http.GetAsync($"{Base}/{N(ns)}/neighbour?next={(next ? "true" : "false")}");
            return res.StatusCode == System.Net.HttpStatusCode.OK ? await res.Content.ReadFromJsonAsync<double>() : null;
        }

        public async Task<List<TreasuryAccountDto>> AccountsAsync(string? q, CancellationToken ct = default)
            => await _http.GetFromJsonAsync<List<TreasuryAccountDto>>(
                   $"{Base}/accounts" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q)}"), ct) ?? new();

        public async Task<TreasuryBalanceDto?> BalanceAsync(string hes)
        {
            if (string.IsNullOrWhiteSpace(hes)) return null;
            var list = await _http.GetFromJsonAsync<List<TreasuryBalanceDto>>($"{Base}/balances?hes={Uri.EscapeDataString(hes)}") ?? new();
            return list.FirstOrDefault() ?? new TreasuryBalanceDto { Hes = hes };
        }

        public async Task<List<TreasuryLookupItem>> DescriptionsAsync()
            => await _http.GetFromJsonAsync<List<TreasuryLookupItem>>($"{Base}/descriptions") ?? new();

        public Task<SanadSaveResult> CreateAsync(SanadHeaderSaveRequest req) => Send(HttpMethod.Post, Base, req);
        public Task<SanadSaveResult> UpdateHeaderAsync(double ns, SanadHeaderSaveRequest req) => Send(HttpMethod.Put, $"{Base}/{N(ns)}", req);
        public Task<SanadSaveResult> UnlockAsync(double ns) => Send(HttpMethod.Post, $"{Base}/{N(ns)}/unlock", null);
        public Task<SanadSaveResult> DeleteAsync(double ns) => Send(HttpMethod.Delete, $"{Base}/{N(ns)}", null);
        public Task<SanadSaveResult> AddRowAsync(double ns, SanadRowSaveRequest req) => Send(HttpMethod.Post, $"{Base}/{N(ns)}/rows", req);
        public Task<SanadSaveResult> AddRowsAsync(double ns, List<SanadRowSaveRequest> req) => Send(HttpMethod.Post, $"{Base}/{N(ns)}/rows/batch", req);
        public Task<SanadSaveResult> UpdateRowAsync(double ns, long rowId, SanadRowSaveRequest req) => Send(HttpMethod.Put, $"{Base}/{N(ns)}/rows/{rowId}", req);
        public Task<SanadSaveResult> DeleteRowAsync(double ns, long rowId) => Send(HttpMethod.Delete, $"{Base}/{N(ns)}/rows/{rowId}", null);

        public Task<SanadSaveResult> SignAsync(double ns, int slot, bool on)
            => Send(HttpMethod.Post, $"{Base}/{N(ns)}/sign", new TreasurySignRequest { Slot = slot, On = on });

        public Task<SanadSaveResult> ReferAsync(double ns, int personel)
            => Send(HttpMethod.Post, $"{Base}/{N(ns)}/refer", new TreasuryReferRequest { Personel = personel });

        public async Task<List<SanadHistoryItemDto>> HistoryAsync(double ns)
            => await _http.GetFromJsonAsync<List<SanadHistoryItemDto>>($"{Base}/{N(ns)}/history") ?? new();

        public async Task<List<SanadHistoryRowDto>> HistoryRowsAsync(double ns, long tridd)
            => await _http.GetFromJsonAsync<List<SanadHistoryRowDto>>($"{Base}/{N(ns)}/history/{tridd}") ?? new();

        public async Task<(SanadFinalizePreviewDto? Result, string? Error)> FinalizeAsync(SanadFinalizeRequest req)
        {
            try
            {
                var res = await _http.PostAsJsonAsync($"{Base}/finalize", req);
                if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
                return (await res.Content.ReadFromJsonAsync<SanadFinalizePreviewDto>(), null);
            }
            catch (HttpRequestException) { return (null, "ارتباط با سرور برقرار نشد. دوباره تلاش کنید."); }
        }

        public async Task<(SanadPrintDto? Doc, string? Error)> PrintAsync(double ns, bool full)
        {
            var res = await _http.GetAsync($"{Base}/{N(ns)}/print?full={(full ? "true" : "false")}");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<SanadPrintDto>(), null);
        }

        public async Task<byte[]?> SignatureAsync(double ns, int slot)
        {
            var res = await _http.GetAsync($"{Base}/{N(ns)}/signature/{slot}");
            return res.IsSuccessStatusCode ? await res.Content.ReadAsByteArrayAsync() : null;
        }

        public async Task<(byte[]? Bytes, string? Error)> ExcelAsync(double ns)
        {
            var res = await _http.GetAsync($"{Base}/{N(ns)}/excel");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadAsByteArrayAsync(), null);
        }

        private async Task<SanadSaveResult> Send(HttpMethod method, string url, object? body)
        {
            try
            {
                using var msg = new HttpRequestMessage(method, url);
                if (body is not null) msg.Content = JsonContent.Create(body, body.GetType());
                var res = await _http.SendAsync(msg);
                if (res.IsSuccessStatusCode) return await res.Content.ReadFromJsonAsync<SanadSaveResult>() ?? new SanadSaveResult { Ok = true };
                try
                {
                    var r = await res.Content.ReadFromJsonAsync<SanadSaveResult>();
                    if (r is not null && !string.IsNullOrWhiteSpace(r.Error)) return r;
                }
                catch { /* متنِ ساده */ }
                return new SanadSaveResult { Ok = false, Error = await ErrorText(res) };
            }
            catch (HttpRequestException)
            {
                return new SanadSaveResult { Ok = false, Error = "ارتباط با سرور برقرار نشد. دوباره تلاش کنید." };
            }
        }

        private static async Task<string> ErrorText(HttpResponseMessage res)
        {
            var text = (await res.Content.ReadAsStringAsync()).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(text) && text.Length < 400 && !text.StartsWith("{") && !text.StartsWith("<")) return text;
            return res.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "اجازه‌ی این کار را ندارید.",
                System.Net.HttpStatusCode.NotFound => "پیدا نشد.",
                System.Net.HttpStatusCode.Unauthorized => "نشستِ شما تمام شده؛ دوباره وارد شوید.",
                _ => "خطا در ارتباط با سرور."
            };
        }
    }
}
