namespace Safir.Shared.Models.CostClose
{
    // ───────────────────────── شمارنده‌ها ─────────────────────────

    public enum CostRunKind : byte
    {
        Trial = 1,   // آزمایشی
        Final = 2    // قطعی
    }

    public enum CostRunStatus : byte
    {
        Draft      = 0,
        Running    = 1,
        Paused     = 2,
        Completed  = 3,
        Failed     = 4,
        RolledBack = 5
    }

    public enum CostStepStatus : byte
    {
        Pending = 0,
        Running = 1,
        Success = 2,
        Warning = 3,
        Failed  = 4,
        Skipped = 5
    }

    public enum CostSeverity : byte
    {
        Warning  = 1,
        Blocking = 2
    }

    public enum VarianceMode : byte
    {
        Manual  = 1,   // اختصاص به یک فرمول
        Prorata = 2,   // تسهیم به نسبت مصرف
        Ignore  = 3    // بدون تخصیص
    }

    // ───────────────────────── اجرا ─────────────────────────

    public class CostRunDto
    {
        public int    RunId          { get; set; }
        public short  FiscalYear     { get; set; }
        public byte   PeriodMonth    { get; set; }
        public long   DateFrom       { get; set; }
        public long   DateTo         { get; set; }
        public short  RunNo          { get; set; }
        public int?   PrevRunId      { get; set; }
        public bool   IsLatest       { get; set; }
        public byte   RunKind        { get; set; }
        public byte   Status         { get; set; }
        public bool   FormulasDirty  { get; set; }
        public DateTime? StartedAtUtc  { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public string StartedByUser  { get; set; } = string.Empty;
        public string? ApprovedByUser { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public string? Note          { get; set; }

        public string StatusText => Status switch
        {
            0 => "پیش‌نویس",
            1 => "در حال اجرا",
            2 => "متوقف",
            3 => "تکمیل",
            4 => "خطا",
            5 => "بازگردانی شده",
            _ => "نامشخص"
        };

        public string KindText => RunKind == 2 ? "قطعی" : "آزمایشی";
    }

    public class CostRunStepDto
    {
        public int    RunStepId     { get; set; }
        public int    RunId         { get; set; }
        public string StepCode      { get; set; } = string.Empty;
        public string StepTitle     { get; set; } = string.Empty;
        public short  SeqNo         { get; set; }
        // int و نه byte — باید با نوع ستون CC_RunStep.Attempt یکی بماند.
        // آن ستون از TINYINT به INT عریض شد (نگاه کنید 22-runstep-attempt-int.sql)
        // چون شمارنده‌ی تلاش با حلقه‌ی همگرایی S07A↔S11 از ۲۵۵ رد می‌شود.
        // ماندنِ این خاصیت روی byte یعنی Dapper موقع map کردن همان ستون
        // شکست می‌خورد و کل «خواندن وضعیت اجرا» خطا می‌دهد.
        public int    Attempt       { get; set; }
        public byte   Status        { get; set; }
        public DateTime? StartedAtUtc  { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public int?   DurationMs    { get; set; }
        public int?   RowsAffected  { get; set; }
        public string? ResultJson   { get; set; }
        public string? ErrorMessage { get; set; }

        public string DurationText =>
            DurationMs is null ? "—"
            : DurationMs < 1000 ? $"{DurationMs} ms"
            : $"{DurationMs / 1000.0:0.0} s";
    }

    public class CostRunLogDto
    {
        public long   LogId      { get; set; }
        public int?   RunId      { get; set; }
        public string? StepCode  { get; set; }
        public DateTime LoggedAtUtc { get; set; }
        public byte   Severity   { get; set; }
        public string Message    { get; set; } = string.Empty;
        public string? ContextJson { get; set; }
    }

    /// <summary>وضعیت کامل یک اجرا — برای بازیابی پس از رفرش صفحه</summary>
    public class CostRunStateDto
    {
        public CostRunDto?          Run   { get; set; }
        public List<CostRunStepDto> Steps { get; set; } = new();
        public int     OpenBlockingCount   { get; set; }
        public int     OpenWarningCount    { get; set; }
        public decimal RemainingVariance   { get; set; }
    }

    // ───────────────────────── استثناها ─────────────────────────

    public class CostExceptionDto
    {
        public long    ExceptionId   { get; set; }
        public int?    RunId         { get; set; }
        public string  StepCode      { get; set; } = string.Empty;
        public string? RuleCode      { get; set; }
        public string? RuleName      { get; set; }
        public byte    ExType        { get; set; }
        public byte    Severity      { get; set; }
        public int?    Anbar         { get; set; }
        public string? AnbarName     { get; set; }
        public long?   Code          { get; set; }
        public string? ItemName      { get; set; }
        public int?    DocNumber     { get; set; }
        public int?    DocTag        { get; set; }
        public long?   DocDate       { get; set; }
        public double? Amount        { get; set; }
        public string? RefList       { get; set; }   // فهرست شماره برگه‌ها
        public bool    CanAutoFix    { get; set; }
        public string  Description   { get; set; } = string.Empty;
        public string? RemedyText    { get; set; }
        public string? FixButtonText { get; set; }
        public bool    IsResolved    { get; set; }
        public string? ResolvedBy    { get; set; }
        public DateTime? ResolvedAtUtc  { get; set; }
        public string? ResolutionNote  { get; set; }

        /// <summary>
        /// فقط برای CHK-02: یعنی سمت حسابداریِ این (انبار،کد) دقیقاً یک سند
        /// دارد و آن سند «افتتاحیه» است — یعنی موجودی اول دوره در STUF_FSK
        /// هیچ‌وقت seed نشده. این‌ها بخش عمده‌ی مغایرت‌های CHK-02 هستند و
        /// معمولاً همه‌شان با یک سند اصلاحیِ دسته‌جمعی رفع می‌شوند.
        /// </summary>
        public bool    IsOpeningOnly { get; set; }

        public bool IsBlocking => Severity == 2;
    }

    public class CostCheckRuleDto
    {
        public string  RuleCode        { get; set; } = string.Empty;
        public string  RuleName        { get; set; } = string.Empty;
        public string  StepCode        { get; set; } = string.Empty;
        public byte    ExType          { get; set; }
        public byte    DefaultSeverity { get; set; }
        public double? Threshold       { get; set; }
        public string  RemedyText      { get; set; } = string.Empty;
        public string? FixProcName     { get; set; }
        public string? FixButtonText   { get; set; }
        public bool    IsActive        { get; set; }
        public short   SortOrder       { get; set; }
    }

    // ───────────────────────── درخواست‌ها ─────────────────────────

    public class UpdateRuleThresholdRequest
    {
        public double? Threshold { get; set; }
    }

    public class CreateCostRunRequest
    {
        public short  FiscalYear  { get; set; }
        public byte   PeriodMonth { get; set; }
        public long   DateFrom    { get; set; }
        public long   DateTo      { get; set; }
        public byte   RunKind     { get; set; } = 1;
        public string? Note       { get; set; }
    }

    public class ResolveExceptionRequest
    {
        public string? Note { get; set; }
    }

    public class BulkResolveRequest
    {
        public List<long> ExceptionIds { get; set; } = new();
        public string?     Note         { get; set; }
    }

    /// <summary>رفع مغایرت CHK-18: کدام سند («الف» یا «ب») تاریخ درست را دارد</summary>
    public class FixDateMismatchRequest
    {
        public bool UseA { get; set; }
    }

    /// <summary>یک استثنای پذیرفته‌شده‌ی دائمی — دیگر در هیچ اجرا/ماهی مسدود نمی‌کند تا لغو شود</summary>
    public class AcceptedExceptionDto
    {
        public int      Id            { get; set; }
        public string   RuleCode      { get; set; } = "";
        public long?    Code          { get; set; }
        public string?  CodeName      { get; set; }
        public int?     Anbar         { get; set; }
        public string?  AnbarName     { get; set; }
        public string   Reason        { get; set; } = "";
        public string   AcceptedBy    { get; set; } = "";
        public DateTime AcceptedAtUtc { get; set; }
    }

    /// <summary>
    /// درخواست «رفع مغایرت CHK-02 با سند اصلاحی» — اختلاف کارت‌انبار/حسابداری
    /// بین حساب موجودیِ همان انبار و یک حساب مقصدِ دلخواه (مثلاً سود و زیان)
    /// جابه‌جا می‌شود. WhatIf=true فقط پیش‌نمایش می‌دهد، چیزی نمی‌نویسد.
    /// </summary>
    public class PostCorrectionRequest
    {
        public List<long> ExceptionIds { get; set; } = new();
        public int     TargetKol  { get; set; }
        public int     TargetMoin { get; set; }
        public int     TargetTaf  { get; set; } = 1;
        public string? Note       { get; set; }
        public bool    WhatIf     { get; set; } = true;

        /// <summary>
        /// تاریخ سند — باید داخل (یا آخرِ) بازه‌ی همون دوره‌ای باشد که این
        /// مغایرت‌ها ازش آمده‌اند، وگرنه CHK-02 (که فقط تا @DT2 آن دوره
        /// می‌خواند) سند را نمی‌بیند و مغایرت بسته‌نشده می‌ماند. خالی =
        /// تاریخ امروز (فقط برای استفاده‌ی موردیِ خارج از یک اجرای مشخص).
        /// </summary>
        public long?   DateS      { get; set; }
    }

    public class CorrectionPreviewLineDto
    {
        public long    ExceptionId      { get; set; }
        public int     Anbar            { get; set; }
        public long    Code             { get; set; }
        public string? ItemName         { get; set; }
        public double  Amount           { get; set; }
        public double  AdjustAbs        { get; set; }
        public bool    DebitIsInventory { get; set; }
    }

    public class CorrectionSkippedDto
    {
        public long   ExceptionId { get; set; }
        public string Reason      { get; set; } = string.Empty;
    }

    public class PostCorrectionResultDto
    {
        public bool    Success      { get; set; }
        public int     Count        { get; set; }
        public double  TotalAbs     { get; set; }
        public double? SanadNumber  { get; set; }
        public List<CorrectionPreviewLineDto> Lines   { get; set; } = new();
        public List<CorrectionSkippedDto>     Skipped { get; set; } = new();
        public string? FirstError   { get; set; }
    }

    public class AutoFixRequest
    {
        public byte  PeriodMonth  { get; set; }
        public long  DateFrom     { get; set; }
        public long  DateTo       { get; set; }
        public int?  RunId        { get; set; }
        public long? ExceptionId  { get; set; }   // خالی = همه موارد قابل اصلاح
        public bool  WhatIf       { get; set; } = true;
    }

    /// <summary>یک سطر از پیش‌نمایش اصلاح خودکار</summary>
    public class AutoFixPreviewRow
    {
        public int    ProdNo   { get; set; }
        public long   ProdDate { get; set; }
        public long   Code     { get; set; }
        public double? OldFnumb { get; set; }
        public int    NewFnumb { get; set; }
        public double Meghdar  { get; set; }
    }

    public class AutoFixResultDto
    {
        public bool   WasPreview  { get; set; }
        public int    RowCount    { get; set; }
        public string Message     { get; set; } = string.Empty;
        public List<AutoFixPreviewRow> Rows     { get; set; } = new();
        public List<string>            Warnings { get; set; } = new();
    }

    /// <summary>
    /// یک فرمولِ نامزد برای کپی به ماهِ جاری — خروجی CC_sp_FormulaOptions.
    ///
    /// وقتی کالا برای ماهِ جاری هیچ فرمولی ندارد، «اصلاح خودکار» کاری از
    /// دستش برنمی‌آید (چیزی نیست که نسبت داده شود). قاعده‌ی صاحب پروژه:
    /// فرمولِ ماه قبل پیشنهاد شود، و اگر ماه قبل هم نداشت، همه‌ی فرمول‌های
    /// آن کالا بدون توجه به ماه فهرست شوند تا کاربر خودش انتخاب کند.
    /// </summary>
    public class FormulaOptionDto
    {
        public int    Fnumb       { get; set; }
        public int    Mah         { get; set; }
        public long?  DateActiv   { get; set; }
        public double Wage        { get; set; }
        public double Overhead    { get; set; }
        public int    LineCount   { get; set; }

        /// <summary>فرمولِ ماهِ قبل — پیشنهادِ پیش‌فرض</summary>
        public bool   IsPrevMonth { get; set; }

        /// <summary>تا حالا برگه‌ی تولیدی به این فرمول وصل شده؟ متروک نباشد.</summary>
        public bool   EverUsed    { get; set; }
    }

    /// <summary>کپیِ یک فرمول از ماهِ دیگر به ماهِ جاری و وصل‌کردنش به برگه‌ها</summary>
    public class CopyFormulaRequest
    {
        public long  Code        { get; set; }
        public byte  PeriodMonth { get; set; }
        public int   SourceFnumb { get; set; }
        public long  DateFrom    { get; set; }
        public long  DateTo      { get; set; }
        public int?  RunId       { get; set; }
        public long? ExceptionId { get; set; }
        public bool  WhatIf      { get; set; } = true;
    }

    public class CopyFormulaResultDto
    {
        public bool   WasPreview { get; set; }
        public int    RowCount   { get; set; }

        /// <summary>شماره‌ی فرمولِ تازه‌ساخته‌شده — فقط بعد از اجرای واقعی</summary>
        public int?   NewFnumb   { get; set; }
        public string Message    { get; set; } = string.Empty;
        public List<AutoFixPreviewRow> Rows { get; set; } = new();
    }

    /// <summary>
    /// یک سطر از صورت‌های مالی. Kind شکلِ نمایش را تعیین می‌کند، نه معنا:
    /// ۰=سطر عادی، ۱=جمع جزء، ۲=جمع نهایی، ۳=سطر اطلاعی/تطبیق.
    /// </summary>
    public class FinLineDto
    {
        public int     Row    { get; set; }
        public string? Text   { get; set; }
        public double? Amount { get; set; }
        public byte    Kind   { get; set; }
    }

    /// <summary>یک سرفصل هزینه و سهمش — تا هر رقمِ صورت قابل ردیابی باشد</summary>
    public class FinExpenseDto
    {
        public string? Category { get; set; }
        public int     Kol      { get; set; }
        public int?    Moin     { get; set; }
        public int?    Tafsili  { get; set; }
        public decimal Ratio    { get; set; }
        public double  Balance  { get; set; }
        public double  Share    { get; set; }
        public string? Note     { get; set; }
    }

    /// <summary>
    /// یک سرفصل هزینه‌ی دوره. برخلاف CC_UnitAcc که به واحد تولیدی می‌چسبد،
    /// این‌ها هزینه‌ی کل شرکت‌اند و در تولید جذب نمی‌شوند.
    /// </summary>
    public class CostExpenseAccDto
    {
        public int     Id          { get; set; }
        public byte    ExpenseKind { get; set; }   // ۱=فروش ۲=اداری ۳=مالی ۴=سایر
        public int     HesKol      { get; set; }
        public int?    HesMoin     { get; set; }
        public int?    HesTafsili  { get; set; }
        public decimal Ratio       { get; set; } = 1;
        public bool    IsActive    { get; set; } = true;
        public string? Note        { get; set; }

        public string? KolName     { get; set; }
        public string? MoinName    { get; set; }
        public string? TafsiliName { get; set; }

        public string KindName => ExpenseKind switch
        {
            1 => "فروش", 2 => "اداری", 3 => "مالی", _ => "سایر"
        };
    }

    public class UpsertExpenseAccRequest
    {
        public byte    ExpenseKind { get; set; }
        public int     HesKol      { get; set; }
        public int?    HesMoin     { get; set; }
        public int?    HesTafsili  { get; set; }
        public decimal Ratio       { get; set; } = 1;
        public bool    IsActive    { get; set; } = true;
        public string? Note        { get; set; }
    }

    /// <summary>خروجی CC_sp_FinancialStatements — سه صورت به‌علاوه تفکیک هزینه</summary>
    public class FinancialStatementsDto
    {
        /// <summary>صورت بهای تمام‌شده کالای ساخته‌شده</summary>
        public List<FinLineDto> Cogm     { get; set; } = new();
        /// <summary>صورت بهای تمام‌شده کالای فروش‌رفته</summary>
        public List<FinLineDto> Cogs     { get; set; } = new();
        /// <summary>صورت سود و زیان</summary>
        public List<FinLineDto> Income   { get; set; } = new();
        public List<FinExpenseDto> Expenses { get; set; } = new();
    }

    public class RebuildRatesResultDto
    {
        public int Remaining { get; set; }
    }

    public class FixNegativeFormulaQtyRequest
    {
        public long   ExceptionId { get; set; }
        public string Action      { get; set; } = "zero";   // "zero" یا "delete"
        public int?   RunId       { get; set; }
        public bool   WhatIf      { get; set; } = true;
    }

    public class FixNegativeFormulaQtyResultDto
    {
        public bool   Changed { get; set; }
        public string Status  { get; set; } = string.Empty;
    }

    // ───────────────────────── انحراف مصرف ─────────────────────────

    public class VarianceRowDto
    {
        public long    Code          { get; set; }
        public string? ItemName      { get; set; }
        public int     Anbar         { get; set; }
        public double  QtyVariance   { get; set; }
        public double? UnitRate      { get; set; }
        public double? AmountVariance { get; set; }
        public double? ConsumedQty   { get; set; }
        public double? VariancePct   { get; set; }
        public bool    IsKeyItem     { get; set; }

        public byte    Mode          { get; set; } = 2;
        public long?   TargetCode    { get; set; }
        public string? TargetName    { get; set; }
        public int?    TargetFNUMB   { get; set; }
        public string? LastMonthHint { get; set; }
        public string? Note          { get; set; }
    }

    public class VarianceDecisionInput
    {
        public long  Code       { get; set; }
        public byte  Mode       { get; set; }
        public long? TargetCode { get; set; }
        public string? Note     { get; set; }
    }

    // ───────────────────────── هزینه تبدیل ─────────────────────────

    public class ConversionCostDto
    {
        public int      UnitId          { get; set; }
        public string?  UnitName        { get; set; }
        public byte     CostKind        { get; set; }   // 0=کل 1=دستمزد 2=سربار
        public decimal  AbsorbedAmount  { get; set; }
        public decimal? AbsorbedFromWip { get; set; }
        public decimal  ActualAmount    { get; set; }
        public decimal  AdjustFactor    { get; set; }
        public string?  ApprovedBy      { get; set; }

        public decimal Difference  => ActualAmount - AbsorbedAmount;
        public decimal? WipControl => AbsorbedFromWip - AbsorbedAmount;

        public string KindText => CostKind switch
        {
            0 => "کل هزینه تبدیل",
            1 => "دستمزد",
            _ => "سربار"
        };
    }

    /// <summary>یک تغییر نرخ یا مقدار در فرمول — برای پاسخ به «چرا این عدد عوض شد؟»</summary>
    public class FormulaChangeDto
    {
        public long    ChangeId     { get; set; }
        public int     RunId        { get; set; }
        public string  StepCode     { get; set; } = string.Empty;
        public int     FNUMB        { get; set; }
        public long?   ParentCode   { get; set; }
        public string? ParentName   { get; set; }
        public long?   ChildCode    { get; set; }
        public string? ChildName    { get; set; }
        public string  FieldName    { get; set; } = string.Empty;
        public double? OldValue     { get; set; }
        public double? NewValue     { get; set; }
        public string? Reason       { get; set; }
        public DateTime ChangedAtUtc { get; set; }

        public double? Delta => NewValue - OldValue;
        public double? DeltaPct =>
            OldValue is null or 0 ? null : (NewValue - OldValue) / OldValue * 100;
    }

    public class ItemCostDto
    {
        public long    Code         { get; set; }
        public string? ItemName     { get; set; }
        public short   LowLevelCode { get; set; }
        public byte    SourceKind   { get; set; }
        public int?    FNUMB        { get; set; }
        public double  MaterialCost { get; set; }
        public double  WageCost     { get; set; }
        public double  OverheadCost { get; set; }
        public double  TotalCost    { get; set; }

        public string SourceText => SourceKind switch
        {
            1 => "میانگین انبار",
            2 => "محاسبه از فرمول",
            _ => "بدون منبع نرخ"
        };
    }

    public class ItemMarginDto
    {
        public long    Code        { get; set; }
        public string? ItemName    { get; set; }
        public double  QtySold     { get; set; }
        public double? WeightKg    { get; set; }
        public double  SalesAmount { get; set; }
        public double  CostAmount  { get; set; }
        public double  Profit      { get; set; }
        public double? UnitCost    { get; set; }
        public double? UnitPrice   { get; set; }

        // تفکیک فروش
        public double? GrossSales   { get; set; }   // پیش از تخفیف
        public double? Discount     { get; set; }
        public double? ReturnAmount { get; set; }   // برگشت از فروش
        public double? ReturnQty    { get; set; }

        // هدف حاشیه: 1=سود صفر (متعادل‌کننده دستی) 2=درصد مشخص 3=آزاد
        // 4=سود صفر با پخش خودکار بین همه کالاهای سودده (بدون BalancingCode)
        public byte     TargetKind    { get; set; } = 3;   // 3 = آزاد
        public decimal? TargetPct     { get; set; }
        public long?    BalancingCode { get; set; }
        public string?  BalancingName { get; set; }

        public double ProfitPct =>
            SalesAmount == 0 ? 0 : Profit / SalesAmount * 100;

        public bool IsLoss => Profit < 0;

        /// <summary>مبلغی که باید جابه‌جا شود تا هدف محقق شود</summary>
        public double AdjustAmount => TargetKind switch
        {
            1 => CostAmount - SalesAmount,
            2 => CostAmount - SalesAmount * (1 - (double)(TargetPct ?? 0) / 100),
            4 => CostAmount - SalesAmount,
            _ => 0
        };
    }

    public class MarginTargetInput
    {
        public long     Code          { get; set; }
        public byte     TargetKind    { get; set; }
        public decimal? TargetPct     { get; set; }
        public long?    BalancingCode { get; set; }
    }

    // ───────── جابه‌جایی مصرف ماده بین فرمول‌ها ─────────

    /// <summary>یک ماده که در فرمول‌های ماه جاری مصرف شده — برای انتخاب ماده در ابزار جابه‌جایی</summary>
    public class FormulaMaterialDto
    {
        public long    Code { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>یک فرمول که ماده‌ی انتخاب‌شده را مصرف می‌کند — برای انتخاب فرمول مبدأ/مقصد</summary>
    public class MaterialConsumerDto
    {
        public int     FNUMB      { get; set; }   // یک کالا می‌تواند در یک ماه چند فرمول داشته باشد
        public long    ParentCode { get; set; }
        public string? ParentName { get; set; }
        public double  MEGHk      { get; set; }   // مصرف فعلی به‌ازای هر واحد محصول
        public double  Rate       { get; set; }   // نرخ ماده (ریال به‌ازای واحد)
        public double? ProdQty    { get; set; }   // مقدار تولید این فرمول در ماه (null یعنی سند تولید ندارد)
    }

    // ───────── سود و زیان به تفکیک واحد تولید ─────────

    /// <summary>
    /// یک سطر سود و زیان برای یک کالا در یک واحد تولید.
    /// جمعِ سطرهای یک کالا روی همه‌ی واحدها ≈ سطر همان کالا در گزارش کل.
    /// </summary>
    public class ItemMarginUnitDto : ItemMarginDto
    {
        /// <summary>null یعنی فروش از انباری که به هیچ واحدی نگاشت ندارد</summary>
        public int?    UnitId   { get; set; }
        public string? UnitName { get; set; }
    }

    /// <summary>سرجمع یک واحد تولید — برای کارت‌های بالای گزارش</summary>
    public class UnitMarginSummaryDto
    {
        public int?    UnitId      { get; set; }
        public string  UnitName    { get; set; } = string.Empty;
        public int     Items       { get; set; }
        public int     LossItems   { get; set; }
        public double  SalesAmount { get; set; }
        public double  CostAmount  { get; set; }
        public double  Profit      { get; set; }

        public double ProfitPct => SalesAmount == 0 ? 0 : Profit / SalesAmount * 100;
    }

    // ───────── پیشنهاد خودکار جابه‌جایی مواد ─────────

    /// <summary>
    /// یک مادهٔ نامزد برای جابه‌جایی، خروجی CC_sp_RebalanceSuggest.
    /// دستمزد و سربار هرگز اهرم نیستند — تنها مقدار مواد.
    /// </summary>
    public class RebalanceSuggestionDto
    {
        public long    MaterialCode   { get; set; }
        public string? MaterialName   { get; set; }

        /// <summary>۱ = مادهٔ مستقیم فرمول، ۲ = ماده‌ای داخل یک نیمه‌ساخته</summary>
        public byte    Depth          { get; set; }
        public long?   ViaCode        { get; set; }
        public string? ViaName        { get; set; }

        public double  AvailableQty   { get; set; }
        public double  Rate           { get; set; }
        public double  RemovableValue { get; set; }

        /// <summary>برای عمق ۲ کمتر از ۱۰۰ است: اثر بین همه مصرف‌کنندگان نیمه‌ساخته پخش می‌شود</summary>
        public double  DilutionPct    { get; set; }
        public double  EffectiveValue { get; set; }

        public int     DestCount      { get; set; }
        public double  DestCapacity   { get; set; }
        public double  Deficit        { get; set; }

        /// <summary>چقدر از کسری واقعاً با این ماده پوشش داده می‌شود</summary>
        public double  Coverage       { get; set; }

        public bool    IsRemembered      { get; set; }
        public long?   RememberedTarget  { get; set; }

        /// <summary>هیچ کالای سوددهی این ماده را مصرف نمی‌کند — قابل استفاده نیست</summary>
        public bool NoDestination => DestCount == 0;

        /// <summary>کسری را کامل می‌پوشاند</summary>
        public bool CoversFully => Deficit > 0 && Coverage >= Deficit;
    }

    /// <summary>یک کالای سوددهِ مقصد برای یک ماده</summary>
    public class RebalanceDestinationDto
    {
        public long    MaterialCode { get; set; }
        public long    TargetCode   { get; set; }
        public string? TargetName   { get; set; }

        /// <summary>
        /// ظرفیتِ خالص: چقدر بار می‌تواند بگیرد بدون اینکه کالای سوددهی را
        /// زیان‌ده کند، پس از کسرِ سهمی که به خودِ کالای مبدأ برمی‌گردد.
        /// همین عدد است که با کسری مقایسه می‌شود.
        /// </summary>
        public double  Capacity     { get; set; }

        /// <summary>ظرفیت پیش از کسرِ بازگشت — برای وقتی کاربر بخواهد تفاوت را ببیند</summary>
        public double  GrossCapacity { get; set; }

        /// <summary>
        /// چند درصد از باری که روی این مقصد گذاشته می‌شود از راه فرمول‌ها به
        /// خودِ کالای زیان‌ده برمی‌گردد. تسکینِ خالص = ظرفیت × (۱ − این).
        /// </summary>
        public double  BouncePct    { get; set; }

        /// <summary>چند کالای زیان‌دهِ دیگر پایین‌دستِ این مقصدند — زیانشان بیشتر می‌شود</summary>
        public int     LoserCount   { get; set; }

        /// <summary>نیمه‌ساخته است (فروش ندارد)؛ ظرفیتش از سود کالاهای پایین‌دست آمده</summary>
        public bool    IsSemi       { get; set; }

        public bool    IsRemembered { get; set; }
    }

    public class RebalanceSuggestResultDto
    {
        public double Deficit { get; set; }
        public List<RebalanceSuggestionDto>  Materials    { get; set; } = new();
        public List<RebalanceDestinationDto> Destinations { get; set; } = new();
    }

    /// <summary>انتخاب کاربر برای اجرا — یک ماده، یک یا چند مقصد</summary>
    public class RebalanceApplyRequest
    {
        public long   SourceCode   { get; set; }
        public long   MaterialCode { get; set; }

        /// <summary>مقصدها؛ اگر خالی باشد، خودکار به‌ترتیب ظرفیت پر می‌شود</summary>
        public List<long> TargetCodes { get; set; } = new();

        /// <summary>انتخاب برای دفعات بعد در CC_RebalancePref ذخیره شود؟</summary>
        public bool Remember { get; set; }
    }

    /// <summary>
    /// سبد جابه‌جایی: چند عملیات با هم، با یک دفترِ ظرفیتِ مشترک و فقط
    /// یک بازمحاسبه در پایان.
    ///
    /// چرا لازم است: وقتی عملیات‌ها تک‌تک اجرا شوند، هرکدام ظرفیت مقصد را
    /// از CC_ItemMargin می‌خواند که تا پایان بازمحاسبه به‌روز نمی‌شود — پس
    /// دو عملیات می‌توانند ظرفیت یک کالای سودده را دوبار کامل خرج کنند و
    /// آن را به زیان ببرند. ضمناً صف برای هر اجرا فقط یک کار می‌پذیرد، پس
    /// بازمحاسبه‌ی عملیات دوم به بعد اصلاً در صف نمی‌رفت.
    /// </summary>
    public class RebalanceBatchRequest
    {
        public List<RebalanceApplyRequest> Items { get; set; } = new();
    }

    public class RebalanceBatchResultDto
    {
        public List<RebalanceApplyResultDto> Results { get; set; } = new();
        public double TotalMoved     { get; set; }
        public double TotalRemaining { get; set; }
        public bool   Recomputing    { get; set; }
        public string Message        { get; set; } = string.Empty;
    }

    /// <summary>گزارش اجرای انتقال — شامل باقیمانده وقتی ظرفیت کافی نبوده</summary>
    public class RebalanceApplyResultDto
    {
        /// <summary>کالای مبدأ — در گزارش سبد لازم است تا سطرها قابل تفکیک باشند</summary>
        public long   SourceCode   { get; set; }
        public string? SourceName  { get; set; }
        public double Deficit      { get; set; }
        public double Moved        { get; set; }
        public double Remaining    { get; set; }
        public int    TargetsUsed  { get; set; }

        /// <summary>زنجیره بازمحاسبه در صف رفت؟ تا تمام نشود عدد سود تغییر نمی‌کند.</summary>
        public bool   Recomputing  { get; set; }
        public string Message      { get; set; } = string.Empty;
    }

    public class RebalanceMaterialRequest
    {
        public long   MaterialCode   { get; set; }
        public long   FromParentCode { get; set; }
        public long   ToParentCode   { get; set; }
        public double Qty            { get; set; }

        /// <summary>
        /// FNUMB فرمول‌هایی که کاربر تیک زده. خالی/null یعنی «همه‌ی فرمول‌های
        /// هر دو کالا که این ماده را مصرف می‌کنند و سند تولید دارند» — که حالت
        /// درست و پیش‌فرض است، چون تغییرِ یکسان روی همه، کل مصرف فیزیکی ماه را
        /// ثابت نگه می‌دارد و انحراف مصرف نمی‌سازد.
        /// </summary>
        public List<int>? SelectedFNUMBs { get; set; }
    }

    /// <summary>خروجی پیش‌نمایش/اعمالِ CC_sp_RebalanceMaterialQty برای یک طرف (مبدأ یا مقصد)</summary>
    public class RebalancePreviewDto
    {
        public int     FNUMB             { get; set; }
        public long    ParentCode        { get; set; }
        /// <summary>«مقدار» — واحد خودِ ردیف. MEGHk = MEGH × نسبت واحد.</summary>
        public double  MEGHBefore        { get; set; }
        public double  MEGHAfter         { get; set; }
        public string? ParentName        { get; set; }
        public double  MEGHkBefore       { get; set; }
        public double  MEGHkAfter        { get; set; }
        public double  Rate              { get; set; }
        public double  CostPerUnitBefore { get; set; }
        public double  CostPerUnitAfter  { get; set; }
        public double  ProdQty           { get; set; }
    }

    /// <summary>یک هدف حاشیه سود فعال — مستقل از فیلتر جدولِ سود و زیان (نگاه کنید GetActiveMarginTargets)</summary>
    public class ActiveMarginTargetDto
    {
        public int      Id            { get; set; }
        public long     Code          { get; set; }
        public string?  ItemName      { get; set; }
        public byte     TargetKind    { get; set; }
        public decimal? TargetPct     { get; set; }
        public long?    BalancingCode { get; set; }
        public string?  BalancingName { get; set; }
    }

    public class RollbackRequest
    {
        public string? StepCode { get; set; }
        public bool    WhatIf   { get; set; } = true;
    }

    /// <summary>نتیجه بازسازی سند حواله خروج مواد (بعد از اصلاح فرمول/نرخ).</summary>
    public class MaterialIssueRebuildResultDto
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public long?        LastSanadNumber { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
    }

    /// <summary>
    /// مشترک بین بازسازی سندهای انتقالی، فروش، برگشت فروش، انبارگردانی،
    /// خروج سایر و تولید — همان قرارداد MaterialIssueRebuildResultDto،
    /// به‌علاوه‌ی تعداد برگه‌هایی که عمداً سند نگرفتند (مثلاً انبار
    /// خرده‌فروشی در حالت غیرصنعتی).
    /// </summary>
    public class GroupDocumentRebuildResultDto
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public int          SkippedCount    { get; set; }
        public long?        LastSanadNumber { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
    }

    // ───────────────────────── تنظیمات ─────────────────────────

    public class CostUnitDto
    {
        public int    UnitId    { get; set; }
        public string UnitName  { get; set; } = string.Empty;
        public int?   Depatman  { get; set; }
        public byte   SplitMode { get; set; }
        public bool   IsActive  { get; set; }
        public short  SeqNo     { get; set; }
        public List<CostUnitAnbarDto> Anbars   { get; set; } = new();
        public List<CostUnitAccDto>   Accounts { get; set; } = new();
    }

    public class CostUnitAnbarDto
    {
        public int   UnitId       { get; set; }
        public int   Anbar        { get; set; }
        public string? AnbarName  { get; set; }
        public byte  AnbarRole    { get; set; }
        public bool  DoStockCount { get; set; }
        public short SeqNo        { get; set; }

        public string RoleText => AnbarRole switch
        {
            1 => "مبنای انحراف",
            2 => "مواد اولیه",
            3 => "محصول",
            _ => "سایر"
        };
    }

    public class CostUnitAccDto
    {
        public int     Id          { get; set; }
        public int     UnitId      { get; set; }
        public int     HesKol      { get; set; }
        public int?    HesMoin     { get; set; }
        public int?    HesTafsili  { get; set; }
        public byte    CostKind    { get; set; }   // 1=دستمزد 2=سربار
        public decimal Ratio       { get; set; }
        public bool    IsActive    { get; set; }
        public string? Note        { get; set; }
        public string? KolName     { get; set; }
        public string? MoinName    { get; set; }
        public string? TafsiliName { get; set; }

        public string CostKindText => CostKind == 1 ? "دستمزد" : "سربار";
    }

    // ───────────────────────── مدیریت واحدها (تنظیمات) ─────────────────────────

    public class UpsertUnitRequest
    {
        public string UnitName  { get; set; } = string.Empty;
        public int?   Depatman  { get; set; }
        public byte   SplitMode { get; set; } = 1;
        public bool   IsActive  { get; set; } = true;
        public short  SeqNo     { get; set; } = 1;
    }

    public class UpsertUnitAnbarRequest
    {
        public int   Anbar        { get; set; }
        public byte  AnbarRole    { get; set; }
        public bool  DoStockCount { get; set; } = true;
        public short SeqNo        { get; set; } = 1;
    }

    public class UpsertUnitAccRequest
    {
        public int     HesKol     { get; set; }
        public int?    HesMoin    { get; set; }
        public int?    HesTafsili { get; set; }
        public byte    CostKind   { get; set; }
        public decimal Ratio      { get; set; } = 1;
        public bool    IsActive   { get; set; } = true;
        public string? Note       { get; set; }
    }

    /// <summary>یک نتیجهٔ جستجوی زنجیره‌ای حساب (کل/معین/تفصیلی).</summary>
    public class AccountLookupDto
    {
        public long   Code { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>نگاشت انبار به حساب موجودی جنسی (کل/معین)، برای CHK-02.</summary>
    public class CostAnbarHesDto
    {
        public int     Anbar    { get; set; }
        public string? AnbarName { get; set; }
        public int     HesKol   { get; set; }
        public int     HesMoin  { get; set; }
        public string? KolName  { get; set; }
        public string? MoinName { get; set; }
        public string? Note     { get; set; }
    }

    public class UpsertAnbarHesRequest
    {
        public int     Anbar   { get; set; }
        public int     HesKol  { get; set; }
        public int     HesMoin { get; set; }
        public string? Note    { get; set; }
    }

    /// <summary>ضریب جذب دستمزد به تفکیک (واحد تولیدی، کالا) — مبنای تقسیم
    /// دستمزد واقعیِ هر واحد بین کالاهایش در گام S07B.</summary>
    public class CostLaborRateDto
    {
        public int     UnitId              { get; set; }
        public string? UnitName            { get; set; }
        public string  Code                { get; set; } = "";
        public string? ItemName            { get; set; }
        public double? Coefficient         { get; set; }
        /// <summary>ضریب جذب سربار — اگر خالی باشد، همان Coefficient (ضریب
        /// دستمزد) برایش استفاده می‌شود.</summary>
        public double? OverheadCoefficient { get; set; }
        /// <summary>کارمزدی — نرخ این کالا در این واحد ثابت است؛ نه S07B
        /// (تقسیم بر اساس ضریب) و نه S10 (ضریب تعدیل) دست‌شان نمی‌زنند.</summary>
        public bool     IsFixed             { get; set; }
        public string? Note                { get; set; }
    }

    public class UpsertLaborRateRequest
    {
        public int     UnitId              { get; set; }
        public string  Code                { get; set; } = "";
        public double? Coefficient         { get; set; }
        public double? OverheadCoefficient { get; set; }
        public bool     IsFixed             { get; set; }
        public string? Note                { get; set; }
    }

    public class ItemLookupDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
