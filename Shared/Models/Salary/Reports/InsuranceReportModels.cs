using System.Collections.Generic;
using System.Linq;

namespace Safir.Shared.Models.Salary.Reports
{
    public class InsuranceEmployeeRowDto
    {
        public int RowIndex { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public string InsuranceCode { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;

        // کارکرد
        public decimal WorkDays { get; set; }

        // مبالغ
        public long DailyWage { get; set; }
        public long MonthlyWage { get; set; }
        public long OtherSubjectBenefits { get; set; } // مزایای مشمول غیر از تاهل و سنوات
        public long MaritalAllowance { get; set; }     // حق تاهل (قانون 1405)
        public long SeniorityBase { get; set; }        // پایه سنوات (قانون 1405)

        public long TotalSubjectToInsurance { get; set; } // جمع مشمول
        public long TotalGrossPay { get; set; }           // جمع ناخالص
        public long WorkerPremium { get; set; }           // حق بیمه سهم کارگر (7%)
    }

    public class InsuranceReportDto
    {
        // مشخصات هدر
        public string WorkshopCode { get; set; } = string.Empty;
        public string WorkshopName { get; set; } = string.Empty;
        public string EmployerName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string PeriodYear { get; set; } = string.Empty;
        public string PeriodMonthName { get; set; } = string.Empty;

        // لیست پرسنل
        public List<InsuranceEmployeeRowDto> Rows { get; set; } = new();

        // جمع کل‌ها (برای فوتر)
        public decimal TotalWorkDays => Rows.Sum(x => x.WorkDays);
        public long TotalMonthlyWage => Rows.Sum(x => x.MonthlyWage);
        public long TotalOtherBenefits => Rows.Sum(x => x.OtherSubjectBenefits);
        public long TotalMaritalAllowance => Rows.Sum(x => x.MaritalAllowance);
        public long TotalSeniorityBase => Rows.Sum(x => x.SeniorityBase);
        public long TotalSubjectToInsurance => Rows.Sum(x => x.TotalSubjectToInsurance);
        public long TotalGrossPay => Rows.Sum(x => x.TotalGrossPay);
        public long TotalWorkerPremium => Rows.Sum(x => x.WorkerPremium);

        // محاسبات کارفرما
        public long TotalEmployerPremium => (long)(TotalSubjectToInsurance * 0.20m); // 20% سهم کارفرما
        public long TotalUnemploymentPremium => (long)(TotalSubjectToInsurance * 0.03m); // 3% بیکاری
        public long TotalPayablePremium => TotalWorkerPremium + TotalEmployerPremium + TotalUnemploymentPremium; // جمع 30%
    }
}