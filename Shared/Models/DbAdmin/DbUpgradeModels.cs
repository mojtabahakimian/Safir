namespace Safir.Shared.Models.DbAdmin
{
    /// <summary>
    /// یک شیء شاخص که باید بعد از آخرین مهاجرت در دیتابیس موجود باشد.
    ///
    /// این‌ها «نمونه» هستند نه فهرست کامل — هدف این است که با چند کوئری
    /// ارزان بفهمیم دیتابیس عقب مانده یا نه، نه اینکه کل ساختار را
    /// بازبینی کنیم. هر بار که مهاجرتِ تازه‌ای اضافه شد، یک شیءِ شاخص از
    /// آن هم به فهرستِ DbUpgradeService.Probes اضافه شود.
    /// </summary>
    public class DbObjectProbe
    {
        /// <summary>نامِ شیء؛ برای ستون به شکل «جدول.ستون».</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>«جدول» / «ستون» / «رویه» / «نما» — فقط برای نمایش.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>اینکه نبودنش چه چیزی را می‌شکند.</summary>
        public string Description { get; set; } = string.Empty;

        public bool Exists { get; set; }
    }

    /// <summary>وضعیت دیتابیسِ جاری نسبت به آخرین مهاجرت‌های شناخته‌شده.</summary>
    public class DbUpgradeStatus
    {
        public string Server   { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;

        public List<DbObjectProbe> Probes { get; set; } = new();

        public int ProbesTotal   => Probes.Count;
        public int ProbesMissing => Probes.Count(p => !p.Exists);
        public bool UpToDate     => ProbesMissing == 0;

        /// <summary>اگر چیزی مانع اجراست، دلیلش.</summary>
        public string? Blocker { get; set; }
    }

    /// <summary>
    /// درخواستِ اجرا. تأییدیه بخشی از خودِ درخواست است، نه چیزی که فقط در
    /// رابط کاربری پرسیده شود — وگرنه یک POST ساده از بیرون همان کار را
    /// بدون هیچ پرسشی انجام می‌داد.
    /// </summary>
    public class DbUpgradeRequest
    {
        /// <summary>
        /// کاربر باید نامِ دقیقِ دیتابیس را تایپ کند. این تنها چیزی است که
        /// جلوی «روی دیتابیس اشتباه زدم» را می‌گیرد، چون کاربر ممکن است
        /// چند پایگاه داشته باشد و بین‌شان جابه‌جا شود.
        /// </summary>
        public string ConfirmDatabase { get; set; } = string.Empty;

        /// <summary>تأییدِ اینکه بکاپ گرفته شده. برای اجرای واقعی اجباری است.</summary>
        public bool BackupConfirmed { get; set; }

        /// <summary>اجرای خشک: فقط پارامترها بررسی می‌شوند و چیزی نوشته نمی‌شود.</summary>
        public bool PreviewOnly { get; set; }

        /// <summary>
        /// بلوکِ «اطلاعات پایه» هم اجرا شود یا نه (پارامتر isCustomCall در
        /// ScriptSqly).
        ///
        /// این بلوک دو چیزِ درهم دارد: حدود ۸۴ تغییرِ ساختاری که همه‌ی
        /// دیتابیس‌ها لازم دارند، و چهار فهرستِ مرجع (ارز، کشور، رتبه،
        /// نحوه‌ی پرداخت). خاموش کردنش هر دو را می‌خواباند، نه فقط
        /// درج داده را — پس پیش‌فرض روشن است و توضیحش در خودِ صفحه آمده.
        ///
        /// ⚠ روی دیتابیسی که هیچ سندی ندارد، ScriptSqly خودش این را
        /// زورکی روشن می‌کند؛ آن حالت از اینجا قابل خاموش کردن نیست.
        /// </summary>
        public bool IncludeBaseData { get; set; } = true;
    }

    /// <summary>خروجی خامِ اجرا، همان چیزی که Runner روی کنسول می‌نویسد.</summary>
    public class DbUpgradeResult
    {
        public bool     Success       { get; set; }
        public int      ExitCode      { get; set; }
        public string   Output        { get; set; } = string.Empty;
        public long     DurationMs    { get; set; }
        public DateTime StartedAtUtc  { get; set; }
        public bool     WasPreview    { get; set; }
    }
}
