using Safir.Shared.Models.Ai;
using System.Net.Http.Json;
using System.Text.Json;

namespace Safir.Client.Services
{
    /// <summary>دسترسی کلاینت به دستیار هوش مصنوعی.</summary>
    public class AiApiService
    {
        /// <summary>
        /// HttpClient مشترکِ برنامه. توکن ورود روی همین نمونه و به‌صورت
        /// DefaultRequestHeaders.Authorization نشسته (AuthService و
        /// ApiAuthenticationStateProvider)، پس هر کلاینتِ *دیگری* بدون
        /// توکن می‌رود و ۴۰۱ می‌گیرد — همان اشتباهی که یک بار مرتکب شدم.
        /// </summary>
        private readonly HttpClient _http;

        /// <summary>
        /// کلاینتِ جداگانه فقط برای «پرسیدن از دستیار»، چون آن یکی درخواستی
        /// است که می‌تواند دقیقه‌ها طول بکشد و Timeout پیش‌فرضِ ۱۰۰ ثانیه
        /// وسطش قطع می‌کرد. Timeout روی خودِ HttpClient است نه روی درخواست،
        /// پس با CancellationToken نمی‌شد بلندترش کرد و کلاینت دوم لازم بود.
        ///
        /// بلند کردنِ Timeout مشترک گزینه نبود: بقیه‌ی صفحات باید سریع شکست
        /// بخورند، وگرنه یک کوئریِ گیرکرده صفحه را دقیقه‌ها معطل می‌کند.
        /// </summary>
        private readonly HttpClient _slow;

        private const string Base = "api/ai";

        public AiApiService(HttpClient http)
        {
            _http = http;
            _slow = new HttpClient
            {
                BaseAddress = http.BaseAddress,
                Timeout     = TimeSpan.FromMinutes(10)
            };
        }

        /// <summary>
        /// توکن در هر فراخوانی از کلاینت مشترک کپی می‌شود، نه یک بار در
        /// سازنده: کاربر ممکن است بین دو سؤال خارج و دوباره وارد شود و
        /// توکن عوض شود.
        /// </summary>
        private HttpClient Slow()
        {
            _slow.DefaultRequestHeaders.Authorization = _http.DefaultRequestHeaders.Authorization;
            return _slow;
        }

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
            var res = await Slow().PostAsJsonAsync($"{Base}/chat", req);

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

        // ───────── تنظیمات سرویس ─────────

        public async Task<AiConfigDto> GetConfigAsync()
            => await _http.GetFromJsonAsync<AiConfigDto>($"{Base}/admin/config") ?? new();

        public async Task<(bool Ok, string? Error)> SaveConfigAsync(UpsertAiConfigRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/admin/config", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// آزمایش با کلاینتِ بلندمدت، نه مشترک: اگر آدرس اشتباه باشد پاسخ
        /// می‌تواند تا تایم‌اوت طول بکشد و ۱۰۰ ثانیه‌ی پیش‌فرض وسطش
        /// می‌شکست — خطایی که به نظر می‌رسید از خودِ برنامه است.
        /// </summary>
        public async Task<AiConnectionTestDto> TestConnectionAsync(UpsertAiConfigRequest draft)
        {
            var res = await Slow().PostAsJsonAsync($"{Base}/admin/config/test", draft);

            return res.IsSuccessStatusCode
                 ? await res.Content.ReadFromJsonAsync<AiConnectionTestDto>()
                   ?? new AiConnectionTestDto { Message = "پاسخی دریافت نشد." }
                 : new AiConnectionTestDto
                   {
                       Ok = false,
                       Message = $"خطای سرور (کد {(int)res.StatusCode})."
                   };
        }

        /// <summary>کاربران فعال برای فهرست انتخاب.</summary>
        public async Task<List<AiUserLookupDto>> ListUsersAsync()
            => await _http.GetFromJsonAsync<List<AiUserLookupDto>>($"{Base}/admin/users") ?? new();

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
