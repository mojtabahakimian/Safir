namespace Safir.Shared.Models.Ai
{
    /// <summary>
    /// اجازه‌ای که ادمین برای چتِ یک کاربر تعیین می‌کند.
    ///
    /// ⚠ این سقفِ بالاست، نه خودِ دسترسی. دسترسیِ واقعی همیشه اشتراکِ
    /// این با دسترسیِ خودِ کاربر در برنامه است — نگاه کنید به
    /// AiAccessService.
    /// </summary>
    public class AiUserAccessDto
    {
        public int    UserCo   { get; set; }
        public string? UserName { get; set; }

        public bool   IsEnabled     { get; set; }
        public byte   Mode          { get; set; }          // 0 = خواندن، 1 = پیشنهاد عمل
        public bool   AllowRawSql   { get; set; }
        public int    MaxRows       { get; set; } = 500;
        public int    DailyMessages { get; set; } = 100;
        public string? BlockedForms { get; set; }
        public string? Note         { get; set; }

        public string? UpdatedBy    { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>پیام‌های امروزِ کاربر — فقط برای نمایش در صفحه‌ی ادمین.</summary>
        public int TodayMessages { get; set; }
    }

    public class UpsertAiAccessRequest
    {
        public bool   IsEnabled     { get; set; }
        public byte   Mode          { get; set; }
        public bool   AllowRawSql   { get; set; }
        public int    MaxRows       { get; set; } = 500;
        public int    DailyMessages { get; set; } = 100;
        public string? BlockedForms { get; set; }
        public string? Note         { get; set; }
    }

    /// <summary>
    /// آنچه چت *واقعاً* برای این کاربر می‌تواند انجام دهد — بعد از اشتراک
    /// گرفتن با دسترسی خودش. رابط کاربری از همین تصمیم می‌گیرد چه چیزی
    /// نشان بدهد، و لایه‌ی ابزار هم قبل از هر فراخوانی همین را می‌پرسد.
    /// </summary>
    public class AiEffectiveAccessDto
    {
        public bool IsEnabled   { get; set; }
        public byte Mode        { get; set; }
        public bool AllowRawSql { get; set; }
        public int  MaxRows     { get; set; }

        public int  DailyMessages  { get; set; }
        public int  TodayMessages  { get; set; }
        public bool QuotaExhausted => TodayMessages >= DailyMessages;

        /// <summary>ابزارهایی که این کاربر مجاز به استفاده از آن‌هاست.</summary>
        public List<AiToolInfoDto> Tools { get; set; } = new();

        /// <summary>
        /// وقتی چت در دسترس نیست، دلیلش به زبان کاربر — نه یک صفحه‌ی خالی.
        /// </summary>
        public string? DisabledReason { get; set; }
    }

    public class AiToolInfoDto
    {
        public string Name        { get; set; } = "";
        public string Title       { get; set; } = "";
        public string Description { get; set; } = "";
    }

    // ───────────────────── گفتگو ─────────────────────

    public class AiChatTurnDto
    {
        public bool   IsUser { get; set; }
        public string Text   { get; set; } = "";
    }

    public class AiChatRequest
    {
        public Guid?  ConversationId { get; set; }
        public string Question       { get; set; } = "";

        /// <summary>
        /// تاریخچه از سمت کلاینت می‌آید و سرور حالتی نگه نمی‌دارد.
        /// ساده‌تر است و با چند نمونه‌ی سرور هم کار می‌کند؛ در عوض سرور
        /// فقط به آخرین چند نوبت اعتماد می‌کند و خودش از لاگ نمی‌خواند،
        /// چون کلاینت می‌تواند تاریخچه را دستکاری کند. مجوزها هرگز از
        /// این مسیر نمی‌آیند.
        /// </summary>
        public List<AiChatTurnDto> History { get; set; } = new();
    }

    public class AiChatStepDto
    {
        public string  Tool       { get; set; } = "";
        public bool    Ok         { get; set; }
        public int     Rows       { get; set; }
        public int     DurationMs { get; set; }
        public string? Note       { get; set; }
    }

    public class AiChatReplyDto
    {
        public string? Text  { get; set; }
        public string? Error { get; set; }

        /// <summary>
        /// چه ابزارهایی صدا زده شد و چند سطر برگشت. به کاربر نشان داده
        /// می‌شود: جوابِ بدونِ منبع در گزارش مالی قابل اتکا نیست.
        /// </summary>
        public List<AiChatStepDto> Steps { get; set; } = new();

        /// <summary>از هدر پاسخ پر می‌شود، نه از بدنه.</summary>
        public Guid? ConversationId { get; set; }
    }
}
