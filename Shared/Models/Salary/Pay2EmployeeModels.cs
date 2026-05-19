using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Salary
{
    public class Pay2EmployeeDto
    {
        public int EMP_ID { get; set; }
        public string? EMP_CODE { get; set; }
        public int WS_ID { get; set; }
        public string? FIRST_NAME { get; set; }
        public string? LAST_NAME { get; set; }
        public string? FATHER_NAME { get; set; }
        public string? NATIONAL_CODE { get; set; }
        public string? ID_NUMBER { get; set; }
        public string? BIRTH_PLACE { get; set; }
        public long? BIRTH_DATE { get; set; }
        public byte GENDER { get; set; } = 1;
        public byte NATIONALITY { get; set; } = 1;
        public bool IS_JANBAZ { get; set; } = false;
        public byte MARITAL { get; set; } = 2;
        public long HIRE_DATE { get; set; }
        public long? FIRE_DATE { get; set; }
        public int? JOB_ID { get; set; }
        public byte? UNIT { get; set; }
        public byte? EDU_LEVEL { get; set; }
        public string? INS_CODE { get; set; }
        public byte INS_TYPE { get; set; } = 1;
        public bool TAX_EXEMPT { get; set; } = false;
        public byte REGION_DEPRIVATION { get; set; } = 0;
        public string? ACC_T { get; set; }
        public string? CARD_NO { get; set; }
        public string? MOBILE { get; set; }
        public string? BANK_ACC { get; set; }
        public string? IBAN { get; set; }
        public bool IS_ACTIVE { get; set; } = true;
        public string? NOTES { get; set; }

        // فیلدهای نمایشی
        public string? WorkshopName { get; set; }
        public string? JobName { get; set; }
        public string FullDisplayName => $"{EMP_CODE} - {LAST_NAME} {FIRST_NAME}";
        public string StatusText => IS_ACTIVE ? "فعال" : "غیرفعال";
    }

    public class Pay2DecreeDto
    {
        public int DEC_ID { get; set; }
        public int EMP_ID { get; set; }
        public int WS_ID { get; set; }
        public long ISSUED_DATE { get; set; }
        public long EFF_FROM { get; set; }
        public long? EFF_TO { get; set; }
        public byte? EDU_LEVEL { get; set; }
        public byte MARITAL { get; set; } = 1;
        public bool IS_MANAGER { get; set; } = false;
        public int? TMPL_ID { get; set; }
        public bool IS_CONFIRMED { get; set; } = false;
        public string? NOTES { get; set; }
        public string? TemplateName { get; set; }
    }
}
