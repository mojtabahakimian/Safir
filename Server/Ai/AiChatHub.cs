using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Safir.Server.Ai
{
    /// <summary>
    /// گزارشِ زنده‌ی اینکه دستیار همین حالا مشغول چه کاری است.
    ///
    /// ── چرا لازم است ──
    /// یک سؤال می‌تواند چند مرحله و ده‌ها ثانیه طول بکشد و در تمام آن مدت
    /// صفحه بی‌حرکت بود. کاربر نمی‌دانست کار می‌کند یا گیر کرده، و
    /// معمولاً دوباره می‌فرستاد.
    ///
    /// ── چرا پیامِ واقعی و نه انیمیشنِ تزئینی ──
    /// «در حال فکر کردن…» چیزی نمی‌گوید. اینکه *کدام* ابزار در حال اجراست
    /// و چند سطر برگشت، هم انتظار را قابل تحمل می‌کند و هم اگر جواب نهایی
    /// عجیب بود، کاربر می‌داند از کجا آمده.
    ///
    /// گروه‌بندی بر اساس شناسه‌ی گفتگو است، نه کاربر: یک نفر می‌تواند دو
    /// زبانه باز داشته باشد و پیام‌ها نباید قاطی شوند.
    /// </summary>
    [Authorize]
    public sealed class AiChatHub : Hub
    {
        public Task Subscribe(string conversationId)
            => Groups.AddToGroupAsync(Context.ConnectionId, Group(conversationId));

        public Task Unsubscribe(string conversationId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(conversationId));

        internal static string Group(string conversationId) => $"ai-chat-{conversationId}";
    }

    public interface IAiChatNotifier
    {
        Task StatusAsync(Guid conversationId, string message);
    }

    public sealed class AiChatNotifier : IAiChatNotifier
    {
        private readonly IHubContext<AiChatHub> _hub;
        public AiChatNotifier(IHubContext<AiChatHub> hub) => _hub = hub;

        /// <summary>
        /// اگر فرستادن پیام شکست بخورد (کاربر صفحه را بسته)، خودِ گفتگو
        /// نباید بشکند — این فقط اطلاع‌رسانی است.
        /// </summary>
        public async Task StatusAsync(Guid conversationId, string message)
        {
            try
            {
                await _hub.Clients
                    .Group(AiChatHub.Group(conversationId.ToString()))
                    .SendAsync("Status", message);
            }
            catch { /* اطلاع‌رسانی، نه بخشی از پاسخ */ }
        }
    }
}
