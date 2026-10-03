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
        /// <summary>
        /// فقط برای سطح تفصیلی: همه‌ی معین‌های یک کل (همان «%» در فیلد معینِ
        /// «لیست تراز آزمایشی چهارستونی تفصیلی» در WPF).
        /// </summary>
        public bool AllMoins { get; set; }
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

    /// <summary>
    /// تراز ماهانه — همان TARAZ_4_MAH و TARAZ_4_MAH_TAF در WPF: گردشِ هر حساب در هر ماه
    /// به‌صورت مانده‌ی بدهکار یا بستانکارِ همان ماه.
    /// </summary>
    public class TrialBalanceMonthlyRowDto
    {
        public int? Kol { get; set; }
        public int? Moin { get; set; }
        public int? Tafsili { get; set; }
        public string? Name { get; set; }
        /// <summary>سال و ماه به‌صورت YYYYMM.</summary>
        public int Ym { get; set; }
        public double Bed { get; set; }
        public double Bes { get; set; }
    }

    /// <summary>برای انتخاب حساب کل یا معین در فیلترها.</summary>
    public class TrialBalanceAccountDto
    {
        public int Number { get; set; }
        public string? Name { get; set; }
    }

    public class TrialBalanceMetaDto
    {
        public string? CompanyName { get; set; }
        public int FiscalYear { get; set; }
        public bool CanKol { get; set; }
        public bool CanMoin { get; set; }
        public bool CanTafsili { get; set; }
    }
}
