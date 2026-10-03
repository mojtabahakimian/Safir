namespace Safir.Shared.Models.Hesabdari
{
    /// <summary>
    /// سطحِ تراز آزمایشی چهارستونی — همان زنجیره‌ی پنجره‌های WPF:
    /// TARAZ_4 → TARAZ_4_MOIN → TARAZ_TAF_DIRECT → TAF2 → TAF3 → TAF4.
    /// </summary>
    public enum TrialBalanceLevel
    {
        Kol = 1,
        Moin = 2,
        Tafsili = 3,
        Tafsili2 = 4,
        Tafsili3 = 5,
        Tafsili4 = 6
    }

    public class TrialBalanceQuery
    {
        public TrialBalanceLevel Level { get; set; } = TrialBalanceLevel.Kol;
        public long From { get; set; }
        public long To { get; set; }
        /// <summary>بازه‌ی شماره‌ی سند؛ خالی یعنی همه (همان ۰ تا ۹۲۹۲۹۲۹۲۹ نرم‌افزار ویندوزی).</summary>
        public double? SanadFrom { get; set; }
        public double? SanadTo { get; set; }
        public int? Kol { get; set; }
        public int? Moin { get; set; }
        public int? Tafsili { get; set; }
        public int? Tafsili2 { get; set; }
        public int? Tafsili3 { get; set; }
    }

    public class TrialBalanceRowDto
    {
        public int? Kol { get; set; }
        public int? Moin { get; set; }
        public int? Tafsili { get; set; }
        public int? Tafsili2 { get; set; }
        public int? Tafsili3 { get; set; }
        public int? Tafsili4 { get; set; }
        public string? Name { get; set; }
        /// <summary>گردش بدهکار / بستانکار.</summary>
        public double SumBed { get; set; }
        public double SumBes { get; set; }
        /// <summary>مانده بدهکار / بستانکار.</summary>
        public double Bed { get; set; }
        public double Bes { get; set; }
    }

    public class TrialBalanceMetaDto
    {
        public int FiscalYear { get; set; }
        public bool CanKol { get; set; }
        public bool CanMoin { get; set; }
        public bool CanTafsili { get; set; }
    }
}
