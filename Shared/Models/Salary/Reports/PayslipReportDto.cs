namespace Safir.Shared.Models.Salary.Reports
{
    /// <summary>
    /// یک ردیف از اقلام فیش حقوقی (یک قلم مزایا یا یک قلم کسورات) — آمادهٔ چاپ.
    /// </summary>
    public class PayslipLineDto
    {
        public string Title { get; set; } = string.Empty;
        public long Amount { get; set; }
    }

    /// <summary>
    /// مدل صریح و «آمادهٔ چاپِ» فیش حقوقی یک پرسنل در یک دوره.
    /// سند QuestPDF فقط همین مدل تمیز را می‌بیند، نه دیکشنری خامِ Details.
    /// قرارداد داده‌ای: جمع مزایا − جمع کسورات = خالص پرداختی (NetPay).
    /// </summary>
    public class PayslipReportDto
    {
        // ── مشخصات پرسنل ──
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public string? NationalCode { get; set; }
        public string? JobTitle { get; set; }

        // ── دوره و کارگاه ──
        public string PeriodTitle { get; set; } = string.Empty;   // مثل «خرداد ۱۴۰۳»
        public long PeriodDate { get; set; }                       // YYYYMM00
        public string WorkshopName { get; set; } = string.Empty;
        public decimal WorkDays { get; set; }

        // ── اقلام پویا ──
        public List<PayslipLineDto> Earnings { get; set; } = new();
        public List<PayslipLineDto> Deductions { get; set; } = new();

        // ── جمع‌ها ──
        public long TotalEarnings => Earnings.Sum(x => x.Amount);
        public long TotalDeductions => Deductions.Sum(x => x.Amount);
        public long NetPay { get; set; }
        public string? NetPayInWords { get; set; }

        // ── اطلاعات تکمیلی فیش ──
        public long InsBase { get; set; }
        public decimal? LeaveBalanceDays { get; set; }
        public long? LoanBalance { get; set; }
        public string? PrintDate { get; set; }
    }
}
