using System.Net;
using System.Net.Http.Json;
using Safir.Shared.Models.Treasury;

namespace Safir.Client.Services
{
    /// <summary>API خزانه‌داری. خطاهای سرور (فارسی) عیناً به صفحه برمی‌گردند.</summary>
    public class TreasuryApiService
    {
        private readonly HttpClient _http;
        public TreasuryApiService(HttpClient http) => _http = http;

        private const string Base = "api/treasury";

        public async Task<TreasuryMetaDto?> MetaAsync()
            => await _http.GetFromJsonAsync<TreasuryMetaDto>($"{Base}/meta");

        public async Task<(List<TreasuryListItemDto>? Items, string? Error)> ListAsync(long? from, long? to)
        {
            var q = new List<string>();
            if (from is > 0) q.Add($"from={from}");
            if (to is > 0) q.Add($"to={to}");
            var res = await _http.GetAsync(Base + (q.Count > 0 ? "?" + string.Join("&", q) : ""));
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<List<TreasuryListItemDto>>(), null);
        }

        public async Task<(TreasuryDetailDto? Item, string? Error)> GetAsync(int id)
        {
            var res = await _http.GetAsync($"{Base}/{id}");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<TreasuryDetailDto>(), null);
        }

        public async Task<List<TreasuryAccountDto>> AccountsAsync(string? q, CancellationToken ct = default)
            => await _http.GetFromJsonAsync<List<TreasuryAccountDto>>(
                   $"{Base}/accounts" + (string.IsNullOrWhiteSpace(q) ? "" : $"?q={Uri.EscapeDataString(q)}"), ct) ?? new();

        public async Task<List<TreasuryBalanceDto>> BalancesAsync(IEnumerable<string> hes)
        {
            var list = hes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList();
            if (list.Count == 0) return new();
            return await _http.GetFromJsonAsync<List<TreasuryBalanceDto>>(
                       $"{Base}/balances?" + string.Join("&", list.Select(h => "hes=" + Uri.EscapeDataString(h)))) ?? new();
        }

        public async Task<TreasuryAccountDto?> CashAccountAsync(int? depatman, int? shift)
        {
            var res = await _http.GetAsync($"{Base}/cash-account?depatman={depatman}&shift={shift}");
            return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<TreasuryAccountDto>() : null;
        }

        public Task<TreasurySaveResult> CreateAsync(TreasuryHeaderSaveRequest req) => Send(HttpMethod.Post, Base, req);
        public Task<TreasurySaveResult> UpdateHeaderAsync(int id, TreasuryHeaderSaveRequest req) => Send(HttpMethod.Put, $"{Base}/{id}", req);
        public Task<TreasurySaveResult> UnlockAsync(int id) => Send(HttpMethod.Post, $"{Base}/{id}/unlock", null);
        public Task<TreasurySaveResult> DeleteAsync(int id) => Send(HttpMethod.Delete, $"{Base}/{id}", null);
        public Task<TreasurySaveResult> AddRowAsync(int id, TreasuryRowSaveRequest req) => Send(HttpMethod.Post, $"{Base}/{id}/rows", req);
        public Task<TreasurySaveResult> UpdateRowAsync(int id, int idh, TreasuryRowSaveRequest req) => Send(HttpMethod.Put, $"{Base}/{id}/rows/{idh}", req);
        public Task<TreasurySaveResult> DeleteRowAsync(int id, int idh) => Send(HttpMethod.Delete, $"{Base}/{id}/rows/{idh}", null);

        // ─── چک ───

        /// <summary>mode: assign | return-received | return-paid</summary>
        public async Task<List<TreasuryChequeDto>> ChequesAsync(string mode, string? q, CancellationToken ct = default)
            => await _http.GetFromJsonAsync<List<TreasuryChequeDto>>(
                   $"{Base}/cheques?mode={mode}" + (string.IsNullOrWhiteSpace(q) ? "" : $"&q={Uri.EscapeDataString(q)}"), ct) ?? new();

        // ─── امضا، ارجاع ───

        public Task<TreasurySaveResult> SignAsync(int id, int slot, bool on)
            => Send(HttpMethod.Post, $"{Base}/{id}/sign", new TreasurySignRequest { Slot = slot, On = on });

        public Task<TreasurySaveResult> ReferAsync(int id, int personel)
            => Send(HttpMethod.Post, $"{Base}/{id}/refer", new TreasuryReferRequest { Personel = personel });

        // ─── ضمیمه‌ی سطر ───

        public async Task<byte[]?> AttachmentAsync(int id, int idh)
        {
            var res = await _http.GetAsync($"{Base}/{id}/rows/{idh}/attachment");
            return res.IsSuccessStatusCode ? await res.Content.ReadAsByteArrayAsync() : null;
        }

        public async Task<TreasurySaveResult> UploadAttachmentAsync(int id, int idh, byte[] bytes, string fileName, string contentType)
        {
            try
            {
                using var form = new MultipartFormDataContent();
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
                form.Add(file, "file", fileName);
                var res = await _http.PostAsync($"{Base}/{id}/rows/{idh}/attachment", form);
                if (res.IsSuccessStatusCode) return await res.Content.ReadFromJsonAsync<TreasurySaveResult>() ?? new TreasurySaveResult { Ok = true };
                try
                {
                    var r = await res.Content.ReadFromJsonAsync<TreasurySaveResult>();
                    if (r is not null && !string.IsNullOrWhiteSpace(r.Error)) return r;
                }
                catch { /* متنِ ساده */ }
                return new TreasurySaveResult { Ok = false, Error = await ErrorText(res) };
            }
            catch (HttpRequestException)
            {
                return new TreasurySaveResult { Ok = false, Error = "ارتباط با سرور برقرار نشد. دوباره تلاش کنید." };
            }
        }

        public Task<TreasurySaveResult> DeleteAttachmentAsync(int id, int idh) => Send(HttpMethod.Delete, $"{Base}/{id}/rows/{idh}/attachment", null);

        // ─── چاپ و اکسل ───

        public async Task<(TreasuryPrintDto? Doc, string? Error)> PrintAsync(int id, string kind)
        {
            var res = await _http.GetAsync($"{Base}/{id}/print?kind={kind}");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadFromJsonAsync<TreasuryPrintDto>(), null);
        }

        public async Task<byte[]?> SignatureAsync(int id, int slot)
        {
            var res = await _http.GetAsync($"{Base}/{id}/signature/{slot}");
            return res.IsSuccessStatusCode ? await res.Content.ReadAsByteArrayAsync() : null;
        }

        /// <summary>اکسل — با همین HttpClient (توکن) گرفته و با downloadFileFromBytes داده می‌شود.</summary>
        public async Task<(byte[]? Bytes, string? Error)> ExcelAsync(int id)
        {
            var res = await _http.GetAsync($"{Base}/{id}/excel");
            if (!res.IsSuccessStatusCode) return (null, await ErrorText(res));
            return (await res.Content.ReadAsByteArrayAsync(), null);
        }

        private async Task<TreasurySaveResult> Send(HttpMethod method, string url, object? body)
        {
            try
            {
                using var msg = new HttpRequestMessage(method, url);
                if (body is not null) msg.Content = JsonContent.Create(body);
                var res = await _http.SendAsync(msg);
                if (res.IsSuccessStatusCode)
                    return await res.Content.ReadFromJsonAsync<TreasurySaveResult>() ?? new TreasurySaveResult { Ok = true };
                if (res.StatusCode == HttpStatusCode.BadRequest)
                {
                    try
                    {
                        var r = await res.Content.ReadFromJsonAsync<TreasurySaveResult>();
                        if (r is not null && !string.IsNullOrWhiteSpace(r.Error)) return r;
                    }
                    catch { /* متنِ ساده */ }
                }
                return new TreasurySaveResult { Ok = false, Error = await ErrorText(res) };
            }
            catch (HttpRequestException)
            {
                return new TreasurySaveResult { Ok = false, Error = "ارتباط با سرور برقرار نشد. دوباره تلاش کنید." };
            }
        }

        private static async Task<string> ErrorText(HttpResponseMessage res)
        {
            var text = (await res.Content.ReadAsStringAsync()).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(text) && text.Length < 300 && !text.StartsWith("{")) return text;
            return res.StatusCode switch
            {
                HttpStatusCode.Forbidden => "اجازه‌ی این کار در خزانه‌داری را ندارید.",
                HttpStatusCode.NotFound => "خزانه پیدا نشد.",
                HttpStatusCode.Unauthorized => "لطفاً دوباره وارد شوید.",
                _ => $"خطای سرور ({(int)res.StatusCode})."
            };
        }
    }
}
