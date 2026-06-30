using System;
using System.Collections.Generic;

namespace Safir.Shared.Models.Salary
{
    public class Pay2ExcelAuditDto
    {
        public List<Pay2ExcelConfigDto> Configs { get; set; } = new();
        public List<Pay2ExcelTaxBracketDto> TaxBrackets { get; set; } = new();
        public List<Pay2ExcelEmployeeLineDto> AuditLines { get; set; } = new();
    }

    public class Pay2ExcelConfigDto
    {
        public string CFG_KEY { get; set; } = "";
        public string CFG_VALUE { get; set; } = "";
        public string DATA_TYPE { get; set; } = "";
    }

    public class Pay2ExcelTaxBracketDto
    {
        public long UPPER_LIMIT { get; set; }
        public decimal RATE_PCT { get; set; }
        public long FIXED_TAX { get; set; }
        public int SORT_ORDER { get; set; }
    }

    public class Pay2ExcelEmployeeLineDto
    {
        public int EMP_ID { get; set; }
        public string EMP_CODE { get; set; } = "";
        public string FULL_NAME { get; set; } = "";

        // داده‌های خام کارکرد (Raw Data)
        public decimal WORK_DAYS { get; set; }
        public decimal DAYSB { get; set; }
        public decimal OT_NORMAL_H { get; set; }
        public decimal OT_HOLIDAY_H { get; set; }
        public decimal ABSENT_DAYS { get; set; }

        // خروجی قطعی C# برای مقایسه (Validation)
        public long GROSS_PAY { get; set; }
        public long INS_BASE { get; set; }
        public long INS_WORKER { get; set; }
        public long TAX_BASE { get; set; }
        public long TAX_AMOUNT { get; set; }
        public long TOTAL_DED { get; set; }
        public long NET_PAY { get; set; }

        public long ADVANCE_DED { get; set; }
        public long LOAN_DED { get; set; }
        public long OTHER_DED { get; set; }

        // جزئیات آیتم‌ها - به صورت خام
        public Dictionary<string, long> BaseValues { get; set; } = new(); // مبالغ پایه حکم
        public Dictionary<string, long> ComputedDetails { get; set; } = new(); // مبالغ محاسبه شده توسط سی‌شارپ
    }
}
