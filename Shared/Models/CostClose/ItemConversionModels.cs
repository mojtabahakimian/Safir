namespace Safir.Shared.Models.CostClose
{
    // ═══════════════════════════════════════════════════════════════
    //  تبدیل کالا به کالا
    //
    //  یک تبدیل همیشه دو سر دارد و دو سرش همیشه هم‌مبلغ‌اند. هر جا در
    //  این فایل «مبلغ» آمده، منظور مبلغِ سمتِ خروج است — سمت ورود از
    //  روی آن مشتق می‌شود و هیچ‌وقت مستقل تایپ نمی‌شود.
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

        /// <summary>مبلغی که از انبار مبدأ خارج می‌شود.</summary>
        public double  Value         { get; set; }

        public string  ToCode        { get; set; } = "";
        public string  ToName        { get; set; } = "";
        public string  ToUnit        { get; set; } = "";
        public int     ToAnbar       { get; set; }
        public string  ToAnbarName   { get; set; } = "";
        public double  ToQty         { get; set; }

        /// <summary>نرخ واحدِ کالای مقصد = مبلغ ÷ مقدار ورودی.</summary>
        public double  ToRate        { get; set; }

        public string  AccountCode   { get; set; } = "";
        public string  AccountName   { get; set; } = "";

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
        public long   DateN      { get; set; }
        public string FromCode   { get; set; } = "";
        public int    FromAnbar  { get; set; }
        public double FromQty    { get; set; }
        public string ToCode     { get; set; } = "";
        public int    ToAnbar    { get; set; }
        public double ToQty      { get; set; }
        public string AccountCode{ get; set; } = "";
        public string? Note      { get; set; }
    }

    public sealed class ConversionRowDto
    {
        public int     ConversionId  { get; set; }
        public long    DateN         { get; set; }
        public string  AccountCode   { get; set; } = "";

        public string  FromCode      { get; set; } = "";
        public string? FromName      { get; set; }
        public int     FromAnbar     { get; set; }
        public string? FromAnbarName { get; set; }
        public double  FromQty       { get; set; }

        public string  ToCode        { get; set; } = "";
        public string? ToName        { get; set; }
        public int     ToAnbar       { get; set; }
        public string? ToAnbarName   { get; set; }
        public double  ToQty         { get; set; }

        public double  IssueNumber   { get; set; }
        public double  ReceiptNumber { get; set; }
        public double? InvoiceNumber { get; set; }

        public double  RateAtEntry   { get; set; }
        public double  ValueAtEntry  { get; set; }

        /// <summary>مبلغ فعلی سمت خروج — پس از بازسازی نرخ میانگین.</summary>
        public double  IssueValue    { get; set; }
        public double  ReceiptValue  { get; set; }

        /// <summary>آنچه روی حساب واسط می‌ماند. باید صفر باشد.</summary>
        public double  Gap           { get; set; }
        public double  ToRate        { get; set; }

        public string? Note          { get; set; }
        public byte    Status        { get; set; }
        public string? CreatedBy     { get; set; }
        public DateTime CreatedAt    { get; set; }

        public bool IsBalanced => Math.Abs(Gap) <= 0.5;
        public bool IsVoided   => Status == 9;
    }

    public sealed class ConversionResultDto
    {
        public bool    Ok            { get; set; }
        public int     ConversionId  { get; set; }
        public double  IssueNumber   { get; set; }
        public double  ReceiptNumber { get; set; }
        public double  InvoiceNumber { get; set; }
        public double  Value         { get; set; }
        public string? Error         { get; set; }
    }
}
