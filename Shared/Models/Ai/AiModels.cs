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
}
