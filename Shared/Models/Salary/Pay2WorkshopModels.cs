using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Salary
{
    public class Pay2WorkshopDto
    {
        public int WS_ID { get; set; }
        public string? WS_CODE { get; set; }
        public string? WS_NAME { get; set; }
        public string? NATIONAL_ID { get; set; }
        public string? SOCIAL_INS_CODE { get; set; }
        public string? TAX_CODE { get; set; }
        public string? ADDRESS { get; set; }
        public string? PHONE { get; set; }
        public bool IS_ACTIVE { get; set; } = true;
        public int INS_MODE { get; set; } = 1;

        public string InsModeText => INS_MODE switch
        {
            1 => "معمولی",
            2 => "ده‌درصدی",
            _ => "نامشخص"
        };
    }

    public class Pay2WorkshopAccDto
    {
        public int WS_ID { get; set; }
        public string? ADV_HES_K { get; set; }
        public string? ADV_HES_M { get; set; }
        public string? SALARY_EXP { get; set; }
        public string? SALARY_PAYABLE { get; set; }
        public string? INS_EXP { get; set; }
        public string? INS_PAYABLE { get; set; }
        public string? TAX_PAYABLE { get; set; }
    }

    public class Pay2WorkshopSaveRequest
    {
        public Pay2WorkshopDto Workshop { get; set; } = new();
        public Pay2WorkshopAccDto Accounts { get; set; } = new();
    }
}
