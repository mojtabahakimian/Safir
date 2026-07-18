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
        public string WorkshopCode { get; set; } = string.Empty;
        public string WorkshopName { get; set; } = string.Empty;
        public string EmployerName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string PeriodYear { get; set; } = string.Empty;
        public string PeriodMonthName { get; set; } = string.Empty;

        public List<InsuranceEmployeeRowDto> Rows { get; set; } = new();

        public decimal TotalWorkDays => Rows.Sum(x => x.WorkDays);
        public long TotalMonthlyWage => Rows.Sum(x => x.MonthlyWage);
        public long TotalOtherBenefits => Rows.Sum(x => x.OtherSubjectBenefits);
        public long TotalMaritalAllowance => Rows.Sum(x => x.MaritalAllowance);
        public long TotalSeniorityBase => Rows.Sum(x => x.SeniorityBase);
        public long TotalSubjectToInsurance => Rows.Sum(x => x.TotalSubjectToInsurance);
        public long TotalGrossPay => Rows.Sum(x => x.TotalGrossPay);
        public long TotalWorkerPremium => Rows.Sum(x => x.WorkerPremium);

        public long TotalEmployerPremium => (long)(TotalSubjectToInsurance * 0.20m);
        public long TotalUnemploymentPremium => (long)(TotalSubjectToInsurance * 0.03m);
        public long TotalPayablePremium => TotalWorkerPremium + TotalEmployerPremium + TotalUnemploymentPremium;
    }

    // 🚀 اصلاح شد: کلاس‌ها از داخل InsuranceReportDto خارج شدند
    public class DiskettePreviewDto
    {
        public DisketteKarDto Kar { get; set; } = new();
        public List<DisketteWorDto> WorList { get; set; } = new();
    }

    public class DisketteKarDto
    {
        public string DSK_ID { get; set; } = "";
        public string DSK_NAME { get; set; } = "";
        public string DSK_FARM { get; set; } = "";
        public int DSK_NUM { get; set; }
        public int DSK_TDD { get; set; }
        public long DSK_TMASH { get; set; }
        public long DSK_TTOTL { get; set; }
        public long DSK_TBIME { get; set; }
        public long DSK_TKARF { get; set; }
        public long DSK_TBIC { get; set; }
        public long DSK_INC { get; set; }
        public string DSK_SPOUS { get; set; } = "0";
    }

    public class DisketteWorDto
    {
        public string DSW_ID1 { get; set; } = "";
        public string FULL_NAME { get; set; } = "";
        public string PER_NATCOD { get; set; } = "";
        public string DSW_OCP { get; set; } = "";
        public int DSW_DD { get; set; }
        public long DSW_ROOZ { get; set; }
        public long DSW_MAH { get; set; }
        public long DSW_MAZ { get; set; }
        public long DSW_MASH { get; set; }
        public long DSW_TOTL { get; set; }
        public long DSW_BIME { get; set; }
        public long DSW_INC { get; set; }
        public string DSW_SPOUS { get; set; } = "0";
    }
    // =================================================================
    // کلاس‌های پیش‌نمایش دیسکت مالیات (WP و WH)
    // =================================================================
    public class TaxDiskettePreviewDto
    {
        public List<TaxDisketteWpDto> WpList { get; set; } = new();
        public List<TaxDisketteWhDto> WhList { get; set; } = new();
    }

    public class TaxDisketteWpDto
    {
        public string NATIONAL_CODE { get; set; } = "";
        public string NATIONALITY { get; set; } = "";
        public string FIRST_NAME { get; set; } = "";
        public string LAST_NAME { get; set; } = "";
        public string FATHER_NAME { get; set; } = "";
        public string ID_NUMBER { get; set; } = "";
        public string BIRTH_PLACE { get; set; } = "";
        public string BIRTH_DATE { get; set; } = "";
        public string MARITAL { get; set; } = "";
        public string INS_CODE { get; set; } = "";
        public string JOB_NAME { get; set; } = "";
        public string HIRE_DATE { get; set; } = "";
        public string FIRE_DATE { get; set; } = "";
        public string POSTAL_CODE { get; set; } = "";
        public string MOBILE { get; set; } = "";
    }

    public class TaxDisketteWhDto
    {
        public string NATIONAL_CODE { get; set; } = "";
        public string YEAR { get; set; } = "";
        public string MONTH { get; set; } = "";
        public decimal WORK_DAYS { get; set; }
        public long BASE_SALARY { get; set; }
        public long MOSTAMAR { get; set; }
        public long GHEYRE_MOSTAMAR { get; set; }
        public long INS_WORKER { get; set; }
        public long TAX_AMOUNT { get; set; }
        public long EYDI { get; set; }
        public long SANAVAT { get; set; }
        public long TAX_BASE { get; set; }
    }
}