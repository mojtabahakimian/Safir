using Safir.Shared.Models.Ai;
using System.Net.Http.Json;
using System.Text.Json;

namespace Safir.Client.Services
{
    /// <summary>دسترسی کلاینت به دستیار هوش مصنوعی.</summary>
    public class AiApiService
    {
        private readonly HttpClient _http;
        private const string Base = "api/ai";

        public AiApiService(HttpClient http) => _http = http;

        /// <summary>دستیار برای کاربر جاری چه می‌تواند بکند.</summary>
        public async Task<AiEffectiveAccessDto> GetMyAccessAsync()
            => await _http.GetFromJsonAsync<AiEffectiveAccessDto>($"{Base}/access")
               ?? new AiEffectiveAccessDto { DisabledReason = "پاسخی از سرور دریافت نشد." };

        /// <summary>
        /// پرسیدن از دستیار. شناسه‌ی گفتگو از هدر پاسخ خوانده می‌شود تا
        /// پیام‌های بعدی در همان گفتگو لاگ شوند و بازرسی بعدی بتواند کل
        /// یک مکالمه را کنار هم ببیند.
        /// </summary>
        public async Task<AiChatReplyDto> ChatAsync(AiChatRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/chat", req);

            if (!res.IsSuccessStatusCode)
                return new AiChatReplyDto
                {
                    Error = await res.Content.ReadAsStringAsync() is { Length: > 0 } m
                          ? m : $"خطای سرور (کد {(int)res.StatusCode})."
                };

            var reply = await res.Content.ReadFromJsonAsync<AiChatReplyDto>()
                        ?? new AiChatReplyDto { Error = "پاسخی دریافت نشد." };

            if (res.Headers.TryGetValues("X-Conversation-Id", out var v) &&
                Guid.TryParse(v.FirstOrDefault(), out var id))
                reply.ConversationId = id;

            return reply;
        }

        public async Task<List<AiUserAccessDto>> ListAccessAsync()
            => await _http.GetFromJsonAsync<List<AiUserAccessDto>>($"{Base}/admin/access") ?? new();

        public async Task<(bool Ok, string? Error)> SaveAccessAsync(int userCo, UpsertAiAccessRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/admin/access/{userCo}", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteAccessAsync(int userCo)
        {
            var res = await _http.DeleteAsync($"{Base}/admin/access/{userCo}");
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<List<JsonElement>> GetLogAsync(int? userCo = null, int take = 200)
            => await _http.GetFromJsonAsync<List<JsonElement>>(
                   $"{Base}/admin/log?take={take}" + (userCo is null ? "" : $"&userCo={userCo}")) ?? new();
    }
}
