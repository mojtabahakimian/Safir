namespace Safir.Shared.Models.Salary
{
    public class PayslipLineDto
    {
        public string Title { get; set; } = string.Empty;
        public long Amount { get; set; }
    }

    public class PayslipReportDto
    {
        public string WorkshopName { get; set; } = string.Empty;
        public string EmployerName { get; set; } = string.Empty;
        public string PeriodTitle { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public decimal WorkDays { get; set; }

        public List<PayslipLineDto> Earnings { get; set; } = new();
        public List<PayslipLineDto> Deductions { get; set; } = new();

        public long GrossPay { get; set; }
        public long TotalDed { get; set; }
        public long NetPay { get; set; }
    }
}
