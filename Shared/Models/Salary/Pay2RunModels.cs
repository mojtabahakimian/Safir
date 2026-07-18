namespace Safir.Shared.Models.Salary
{
    public enum Pay2DeedMode : byte
    {
        CurrentSummary = 1,
        PersonTraceable = 2
    }
    public class Pay2DeedArticleDto
    {
        public string HES_CODE { get; set; } = string.Empty;
        public string SHARH { get; set; } = string.Empty;
        public long BED { get; set; }
        public long BES { get; set; }
        public string ACC_KEY { get; set; } = string.Empty;
        public int? EMP_ID { get; set; }
        public string? EmployeeName { get; set; } // برای نمایش در UI
    }

    public class Pay2DeedPreviewDto
    {
        public Pay2DeedMode ModeUsed { get; set; }
        public string ModeTitle { get; set; } = string.Empty;
        public List<Pay2DeedArticleDto> Articles { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();

        public long TotalDebit => Articles.Sum(x => x.BED);
        public long TotalCredit => Articles.Sum(x => x.BES);
        public long Difference => Math.Abs(TotalDebit - TotalCredit);
        public bool IsBalanced => TotalDebit > 0 && Difference == 0;
        public bool HasErrors => ValidationErrors.Any();
    }
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
        public string? NOTES { get; set; }

        public byte? DEED_MODE { get; set; }
        public short? DEED_GENERATOR_VERSION { get; set; }

        public string StatusText => STATUS switch
        {
            1 => "پیش‌نویس (موقت)",
            2 => "تأیید نهایی",
            3 => "سند صادر شده",
            _ => "نامشخص"
        };
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

    // ─── کلاس‌های گزارش مقایسه ماه‌ها ───
    public class Pay2MonthCompareRowDto
    {
        public int EMP_ID { get; set; }
        public string EMP_CODE { get; set; } = "";
        public string FULL_NAME { get; set; } = "";

        public long GROSS_PAY_1 { get; set; }
        public long GROSS_PAY_2 { get; set; }
        public long GROSS_PAY_DIFF => GROSS_PAY_2 - GROSS_PAY_1;

        public long TOTAL_DED_1 { get; set; }
        public long TOTAL_DED_2 { get; set; }
        public long TOTAL_DED_DIFF => TOTAL_DED_2 - TOTAL_DED_1;

        public long NET_PAY_1 { get; set; }
        public long NET_PAY_2 { get; set; }
        public long NET_PAY_DIFF => NET_PAY_2 - NET_PAY_1;

        // وضعیت تحلیلی (استخدام جدید، ترک کار یا عادی)
        public string Status => (GROSS_PAY_1 > 0 && GROSS_PAY_2 == 0) ? "قطع حقوق/ترک کار" :
                                (GROSS_PAY_1 == 0 && GROSS_PAY_2 > 0) ? "جدید/شروع کار" : "عادی";
    }

    public class Pay2MonthCompareResultDto
    {
        public string Period1Title { get; set; } = "";
        public string Period2Title { get; set; } = "";
        public List<Pay2MonthCompareRowDto> Rows { get; set; } = new();
    }
}