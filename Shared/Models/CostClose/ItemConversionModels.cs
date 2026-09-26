namespace Safir.Shared.Models.CostClose
{
    // ═══════════════════════════════════════════════════════════════
    //  تبدیل کالا به کالا — برگه‌ی TAG=30
    //
    //  یک سربرگ، یک سطر، یک مبلغ. هر جا «مبلغ» می‌بینید همان یک عدد
    //  است که هم ارزشِ خروج است و هم ارزشِ ورود — دو تا نیست که بشود
    //  با هم اختلاف پیدا کنند.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>آنچه پیش از ثبت به کاربر نشان داده می‌شود.</summary>
    public sealed class ConversionPreviewDto
    {
        public string  FromCode      { get; set; } = "";
        public string  FromName      { get; set; } = "";
        public string  FromUnit      { get; set; } = "";
        public int     FromAnbar     { get; set; }
        public string  FromAnbarName { get; set; } = "";
        public double  FromQty       { get; set; }

        /// <summary>موجودی کالای مبدأ در آن انبار، تا تاریخ تبدیل.</summary>
        public double  OnHand        { get; set; }

        /// <summary>میانگین متحرک کاردکس در لحظه‌ی تبدیل.</summary>
        public double  Rate          { get; set; }

        /// <summary>ارزشی که جابه‌جا می‌شود — برای هر دو سر، یکی.</summary>
        public double  Value         { get; set; }

        public string  ToCode        { get; set; } = "";
        public string  ToName        { get; set; } = "";
        public string  ToUnit        { get; set; } = "";
        public int     ToAnbar       { get; set; }
        public string  ToAnbarName   { get; set; } = "";
        public double  ToQty         { get; set; }

        /// <summary>نرخ واحدِ کالای مقصد = مبلغ ÷ مقدار ورود.</summary>
        public double  ToRate        { get; set; }

        /// <summary>
        /// چیزهایی که مانع ثبت نیستند ولی کاربر باید ببیند — مثلاً نرخ
        /// صفر، یا موجودی کمتر از مقدار درخواستی.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>اگر پر باشد، ثبت ممکن نیست.</summary>
        public string? Blocker       { get; set; }

        public bool CanSubmit => Blocker is null;
    }

    public sealed class CreateConversionRequest
    {
        public long    DateN     { get; set; }
        public string  FromCode  { get; set; } = "";
        public int     FromAnbar { get; set; }
        public double  FromQty   { get; set; }
        public string  ToCode    { get; set; } = "";
        public int     ToAnbar   { get; set; }
        public double  ToQty     { get; set; }
        public string? Note      { get; set; }
    }

    public sealed class ConversionRowDto
    {
        /// <summary>شناسه‌ی سطر INVO_LST — خودِ برگه، جدول واسطی در کار نیست.</summary>
        public long    ConversionId  { get; set; }
        public double  Number        { get; set; }
        public long    DateN         { get; set; }

        /// <summary>شماره سند حسابداری، اگر صادر شده باشد.</summary>
        public double? SanadNo       { get; set; }

        public string  FromCode      { get; set; } = "";
        public string? FromName      { get; set; }
        public string? FromUnit      { get; set; }
        public int     FromAnbar     { get; set; }
        public string? FromAnbarName { get; set; }
        public double  FromQty       { get; set; }
        public double  FromRate      { get; set; }

        public string? ToCode        { get; set; }
        public string? ToName        { get; set; }
        public string? ToUnit        { get; set; }
        public int?    ToAnbar       { get; set; }
        public string? ToAnbarName   { get; set; }
        public double? ToQty         { get; set; }

        public double  Value         { get; set; }
        public double  ToRate        { get; set; }

        public double? FromAverage   { get; set; }
        public double? ToAverage     { get; set; }

        public string? Note          { get; set; }
        public string? CreatedBy     { get; set; }
        public DateTime? CreatedAt   { get; set; }

        /// <summary>
        /// برگه‌ای که مقصدش مشخص نیست، کالا را از انبار می‌برد و هیچ‌جا
        /// برنمی‌گرداند. همان چیزی که CHK-24 می‌گیرد.
        /// </summary>
        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(ToCode) && ToAnbar is not null && ToQty > 0;

        /// <summary>سند حسابداری خورده؟ اگر بله، ابطال ساده ممکن نیست.</summary>
        public bool HasSanad => SanadNo is > 0;
    }

    public sealed class ConversionResultDto
    {
        public bool    Ok           { get; set; }
        public long    ConversionId { get; set; }
        public double  Number       { get; set; }
        public double  Value        { get; set; }
        public string? Error        { get; set; }
    }
}
