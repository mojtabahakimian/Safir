using System.Collections.Generic;
using System.Linq;

namespace Safir.Shared.Models.Salary.Reports
{
    public class TaxEmployeeRowDto
    {
        public int RowIndex { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public decimal WorkDays { get; set; }
        public string HireDate { get; set; } = string.Empty;
        public string FireDate { get; set; } = string.Empty;
        public long BaseDailyWage { get; set; }
        public long SeniorityDailyBase { get; set; }
        public long TotalDailyWage => BaseDailyWage + SeniorityDailyBase;
        public long MonthlyWage { get; set; }
        public long OtherSubjectBenefits { get; set; }
        public long TotalSubject { get; set; }
        public long GrossPay { get; set; }
        public long TaxBase { get; set; }
        public long TaxAmount { get; set; }
        public long WorkerPremium { get; set; }
        public long NetPayable { get; set; }
    }

    public class TaxReportDto
    {
        public string WorkshopCode { get; set; } = string.Empty;
        public string WorkshopName { get; set; } = string.Empty;
        public string EmployerName { get; set; } = string.Empty;
        public string TaxCode { get; set; } = string.Empty; // شناسه/کد اقتصادی
        public string Address { get; set; } = string.Empty;
        public string PeriodYear { get; set; } = string.Empty;
        public string PeriodMonthName { get; set; } = string.Empty;

        public List<TaxEmployeeRowDto> Rows { get; set; } = new();

        // جمع کل‌ها (برای فوتر)
        public decimal TotalWorkDays => Rows.Sum(x => x.WorkDays);
        public long TotalGrossPay => Rows.Sum(x => x.GrossPay);
        public long TotalTaxBase => Rows.Sum(x => x.TaxBase);
        public long TotalTaxAmount => Rows.Sum(x => x.TaxAmount);
    }
}
