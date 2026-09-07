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

    /// <summary>یک کاربر در فهرست انتخاب.</summary>
    public class AiUserLookupDto
    {
        public int    UserCo   { get; set; }
        public string? UserName { get; set; }

        /// <summary>از قبل دسترسی دارد — تا دوباره اضافه نشود.</summary>
        public bool HasAccess { get; set; }

        public string Display => string.IsNullOrWhiteSpace(UserName)
                               ? $"کاربر {UserCo}"
                               : $"{UserName} ({UserCo})";
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

    // ───────────────────── تنظیمات سرویس ─────────────────────

    /// <summary>
    /// تنظیمات سرویس برای صفحه‌ی ادمین.
    ///
    /// ⚠ ApiKey اینجا نیست و هیچ‌وقت نخواهد بود. فقط HasApiKey و چهار
    /// نویسه‌ی آخر برمی‌گردد؛ دیدنِ کلید در رابط کاربری هیچ کاربردی ندارد
    /// و فقط راهی برای بیرون رفتنش می‌سازد.
    /// </summary>
    public class AiConfigDto
    {
        public bool    IsEnabled      { get; set; }
        public string  Provider       { get; set; } = "openai";
        public string? BaseUrl        { get; set; }
        public string? Model          { get; set; }
        public int     TimeoutSeconds { get; set; } = 120;
        public int     MaxToolLoops   { get; set; } = 4;

        public bool    HasApiKey      { get; set; }
        public string? ApiKeyTail     { get; set; }

        /// <summary>کلید از متغیر محیطی می‌آید، نه از این صفحه.</summary>
        public bool    KeyFromEnv     { get; set; }

        public string? UpdatedBy      { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }

    public class UpsertAiConfigRequest
    {
        public bool    IsEnabled      { get; set; }
        public string  Provider       { get; set; } = "openai";
        public string? BaseUrl        { get; set; }
        public string? Model          { get; set; }
        public int     TimeoutSeconds { get; set; } = 120;
        public int     MaxToolLoops   { get; set; } = 4;

        /// <summary>
        /// خالی یعنی «کلید فعلی را دست نزن». برای پاک کردن باید
        /// ClearApiKey را true فرستاد — وگرنه هر ذخیره‌ی ساده‌ای که کلید
        /// را دوباره تایپ نکرده، بی‌سروصدا کلید را پاک می‌کرد.
        /// </summary>
        public string? ApiKey         { get; set; }
        public bool    ClearApiKey    { get; set; }
    }

    public class AiConnectionTestDto
    {
        public bool    Ok      { get; set; }
        public string? Message { get; set; }
        public List<string> Models { get; set; } = new();
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

        /// <summary>
        /// متنِ استخراج‌شده‌ی فایل پیوست. سرور فایل را نگه نمی‌دارد، پس
        /// متن از همین‌جا می‌آید.
        /// </summary>
        public string? AttachmentName { get; set; }
        public string? AttachmentText { get; set; }
    }

    public class AiAttachmentDto
    {
        public string FileName { get; set; } = "";
        public string Text     { get; set; } = "";
        public int    Chars    { get; set; }
    }

    /// <summary>یک گفتگو در فهرست تاریخچه.</summary>
    public class AiConversationDto
    {
        public Guid     ConversationId { get; set; }
        public string   Title          { get; set; } = "";
        public DateTime UpdatedAtUtc   { get; set; }
        public int      Messages       { get; set; }
    }

    public class RenameConversationRequest
    {
        public string Title { get; set; } = "";
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
