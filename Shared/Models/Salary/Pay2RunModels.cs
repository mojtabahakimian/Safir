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
        public string? NOTES { get; set; }

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

    // ═══════════════════════════════════════════════════════════════
    // 🚀 DTO‌های جدید برای خروجی اکسل با فرمول (Excel Audit)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// داده‌های خام یک کارمند برای شیت RawData — شامل روزهای کارکرد، اضافه‌کاری و سندها
    /// </summary>
    public class Pay2ExcelRawLineDto
    {
        public int EMP_ID { get; set; }
        public string? EMP_CODE { get; set; }
        public string? FULL_NAME { get; set; }

        // --- روزها و ساعات کارکرد ---
        public decimal WORK_DAYS { get; set; }
        public decimal DAYS { get; set; }
        public decimal DAYSB { get; set; }
        public decimal OT_NORMAL_H { get; set; }
        public decimal OT_HOLIDAY_H { get; set; }
        public decimal OT_ADMIN_H { get; set; }
        public decimal LEAVE_DAYS { get; set; }

        // --- مبالغ attendance ---
        public long PERF_AMOUNT { get; set; }
        public long TRANSP_AMOUNT { get; set; }
        public long KASR_OTHER { get; set; }

        // --- سندها (Decree Lines): آیتم‌های پایه حقوق ---
        public List<Pay2ExcelDecreeLineDto> DecreeLines { get; set; } = new();

        // --- مقادیر ATT_VALUE (آیتم‌های متغیر از کارکرد) ---
        public Dictionary<string, long> AttValues { get; set; } = new();

        // --- مقادیر نهایی محاسبه‌شده توسط SP (برای شیت تطبیق) ---
        public long SP_GROSS_PAY { get; set; }
        public long SP_INS_BASE { get; set; }
        public long SP_INS_WORKER { get; set; }
        public long SP_TAX_BASE { get; set; }
        public long SP_TAX_AMOUNT { get; set; }
        public long SP_LOAN_DED { get; set; }
        public long SP_ADVANCE_DED { get; set; }
        public long SP_OTHER_DED { get; set; }
        public long SP_TOTAL_DED { get; set; }
        public long SP_NET_PAY { get; set; }

        // --- جزئیات آیتم‌های محاسبه‌شده (Details) ---
        public Dictionary<string, long> Details { get; set; } = new();
    }

    /// <summary>
    /// یک خط سند (Decree Line) — مبلغ پایه یک آیتم حقوقی
    /// </summary>
    public class Pay2ExcelDecreeLineDto
    {
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
        public int ITEM_TYPE { get; set; }       // 1=پرداختی روزانه, 2=پرداختی ماهانه, 3=ساعتی, 4=کسورات, 5=متغیر
        public int CALC_BASIS { get; set; }       // 1=روزانه, 2=ماهانه, 3=ساعتی
        public bool INS_SUBJECT { get; set; }     // مشمول بیمه
        public bool TAX_SUBJECT { get; set; }     // مشمول مالیات
        public long AMOUNT { get; set; }          // مبلغ پایه از سند
        public long? INS_OV { get; set; }         // اورراید بیمه
        public long? TAX_OV { get; set; }         // اورراید مالیات
        public long? BASIS_OV { get; set; }       // اورراید مبنای محاسبه
    }

    /// <summary>
    /// تعریف آیتم حقوقی (برای شیت Settings — ساختار ستون‌های پویا)
    /// </summary>
    public class Pay2ExcelItemDefDto
    {
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
        public int ITEM_TYPE { get; set; }
        public int CALC_BASIS { get; set; }
        public bool INS_SUBJECT { get; set; }
        public bool TAX_SUBJECT { get; set; }
    }

    /// <summary>
    /// کل داده‌های لازم برای تولید اکسل حسابرسی فرمولی
    /// </summary>
    public class Pay2ExcelAuditDataDto
    {
        // --- تنظیمات PAY2_CONFIG (کلید→مقدار) ---
        public Dictionary<string, string> Config { get; set; } = new();

        // --- پل‌های مالیات progressive ---
        public List<Pay2TaxBracketDto> TaxBrackets { get; set; } = new();

        // --- تعاریف آیتم‌های حقوقی ---
        public List<Pay2ExcelItemDefDto> ItemDefs { get; set; } = new();

        // --- ستون‌های پویای آیتم‌ها (از RUN) ---
        public List<Pay2RunColumnDto> Columns { get; set; } = new();

        // --- داده‌های خام هر کارمند ---
        public List<Pay2ExcelRawLineDto> RawLines { get; set; } = new();

        // --- شناسه اجرا و دوره ---
        public int RUN_ID { get; set; }
        public long PERIOD_DATE { get; set; }
        public string? WS_NAME { get; set; }
    }
}