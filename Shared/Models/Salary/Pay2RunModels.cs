using System;
using System.Collections.Generic;

namespace Safir.Shared.Models.Salary
{
    public class Pay2RunCalcRequest
    {
        public int WS_ID { get; set; }
        public int PER_ID { get; set; }
        public double? PAYROLL_N_S { get; set; }
        public bool IsReRun { get; set; } = false;
    }

    public class Pay2RunDto
    {
        public int RUN_ID { get; set; }
        public int PER_ID { get; set; }
        public short RUN_NO { get; set; }
        public bool IS_LATEST { get; set; }
        public DateTime CALC_AT { get; set; }
        public byte STATUS { get; set; }
        public int? DEED_ID_SAL { get; set; }
        public byte? DEED_MODE { get; set; }
        public short? DEED_GENERATOR_VERSION { get; set; }
        public string? NOTES { get; set; }

        public string StatusText => STATUS switch
        {
            1 => "پیش‌نویس (موقت)",
            2 => "تأیید نهایی",
            3 => "سند صادر شده",
            _ => "نامشخص"
        };
    }

    public class Pay2DeedArticleDto
    {
        public string HES_CODE { get; set; } = "";
        public string SHARH { get; set; } = "";
        public long BED { get; set; }
        public long BES { get; set; }
        public string ACC_KEY { get; set; } = "";
        public int? EMP_ID { get; set; }

        // For parsed values
        public int HesK { get; set; }
        public int HesM { get; set; }
        public int HesT { get; set; }
        public int? HesT2 { get; set; }
        public int? HesT3 { get; set; }
        public int? HesT4 { get; set; }
    }

    public class Pay2DeedPreviewDto
    {
        public int RUN_ID { get; set; }
        public byte DEED_MODE { get; set; }
        public string DEED_MODE_TITLE { get; set; } = "";
        public bool IsBalanced { get; set; }
        public long TotalBed { get; set; }
        public long TotalBes { get; set; }
        public long Difference => TotalBed - TotalBes;
        public List<Pay2DeedArticleDto> Articles { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
    }

    public class Pay2RunColumnDto
    {
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
    }

    public class Pay2RunResultDto
    {
        public List<Pay2RunColumnDto> Columns { get; set; } = new();
        public List<Pay2RunLineDto> Lines { get; set; } = new();
    }

    public class Pay2RunLineDto
    {
        public int RUN_ID { get; set; }
        public int EMP_ID { get; set; }
        public string? EMP_CODE { get; set; }
        public string? FULL_NAME { get; set; }
        public decimal WORK_DAYS { get; set; }

        public long GROSS_PAY { get; set; }
        public long INS_BASE { get; set; }
        public long INS_WORKER { get; set; }
        public long TAX_BASE { get; set; }
        public long TAX_AMOUNT { get; set; }
        public long LOAN_DED { get; set; }
        public long ADVANCE_DED { get; set; }
        public long OTHER_DED { get; set; }
        public long TOTAL_DED { get; set; }
        public long NET_PAY { get; set; }

        // 🚀 دیکشنری جدید برای نگهداری مقادیر ریزِ هر آیتم بر اساس ITEM_CODE
        public Dictionary<string, long> Details { get; set; } = new();
    }
}
