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
        public byte   Attempt       { get; set; }
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

    // ───────────────────────── تنظیمات ─────────────────────────

    public class CostUnitDto
    {
        public int    UnitId    { get; set; }
        public string UnitName  { get; set; } = string.Empty;
        public int?   Depatman  { get; set; }
        public byte   SplitMode { get; set; }
        public bool   IsActive  { get; set; }
        public short  SeqNo     { get; set; }
        public List<CostUnitAnbarDto> Anbars { get; set; } = new();
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
}
