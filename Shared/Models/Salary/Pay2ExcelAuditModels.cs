namespace Safir.Shared.Models.Salary
{
    // ═══════════════════════════════════════════════════════════════════
    // مدل‌های داده برای خروجی اکسلِ تحلیلیِ فرمول‌دار (Excel Formula Audit)
    // این مدل‌ها فقط عملوندهای خام (raw operands) موردنیاز موتور اکسل را
    // از دیتابیس حمل می‌کنند تا فرمول‌های واقعی اکسل روی آن‌ها ساخته شوند.
    // عملیات کاملاً Read-Only است و هیچ اثری روی دیتابیس ندارد.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>یک کلید/مقدار از PAY2_CONFIG (اسنپ‌شاتِ تنظیمات در لحظهٔ گزارش).</summary>
    public class Pay2CfgRow
    {
        public string CFG_KEY { get; set; } = "";
        public string? CFG_VALUE { get; set; }
    }

    /// <summary>یک پلهٔ جدول مالیات پلکانی (PAY2_TAX_BRACKET) برای سالِ مالیاتیِ این اجرا.</summary>
    public class Pay2TaxBracketRow
    {
        public short SORT_ORDER { get; set; }
        public long UPPER_LIMIT { get; set; }
        public decimal RATE_PCT { get; set; }
        public long FIXED_TAX { get; set; }
    }

    /// <summary>کارکرد خام + صفات مؤثرِ هر پرسنل (ورودی‌های موتور).</summary>
    public class Pay2AuditEmpRow
    {
        public int EMP_ID { get; set; }
        public string? EMP_CODE { get; set; }
        public string? FULL_NAME { get; set; }

        // کارکرد خام
        public decimal WORK_DAYS { get; set; }
        public decimal DAYS { get; set; }      // کارکرد اسمی (مبنای بیمه)
        public decimal DAYSB { get; set; }     // کارکرد رسمی (مبنای پرداخت)
        public short FRID_COUNT { get; set; }
        public decimal TDAYS { get; set; }
        public decimal OT_NORMAL_H { get; set; }
        public decimal OT_HOLIDAY_H { get; set; }
        public decimal OT_ADMIN_H { get; set; }
        public decimal LEAVE_DAYS { get; set; }
        public long PERF_AMOUNT { get; set; }
        public long TRANSP_AMOUNT { get; set; }
        public long KASR_OTHER { get; set; }

        // صفات مؤثر بر بیمه/مالیات
        public byte INS_TYPE { get; set; }          // 3 = معاف از بیمه
        public bool TAX_EXEMPT { get; set; }
        public bool IS_MANAGER { get; set; }
        public bool IS_JANBAZ { get; set; }
        public byte REGION_DEPRIVATION { get; set; }
    }

    /// <summary>
    /// یک خط از حکمِ فعالِ پرسنل در بازهٔ دوره + عملوندهای موردنیاز فرمول
    /// (نرخ خام، basis مؤثر، مشمولیت‌های مؤثر، روزهای فعال حکم برای prorate و ...).
    /// </summary>
    public class Pay2AuditDecreeLineRow
    {
        public int EMP_ID { get; set; }
        public int DEC_ID { get; set; }
        public long EFF_FROM { get; set; }
        public long EFF_TO { get; set; }

        public int ITEM_ID { get; set; }
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
        public byte ITEM_TYPE { get; set; }
        public short SORT_ORDER { get; set; }

        public decimal RAW_AMOUNT { get; set; }     // نرخِ ثبت‌شده در حکم (روزانه/ماهانه/درصد/ساعتی)
        public byte EFF_BASIS { get; set; }         // basis مؤثر (پس از override): 1=روزانه،2=ماهانه،3=ساعتی
        public bool EFF_INS { get; set; }           // مشمول بیمه مؤثر
        public bool EFF_TAX { get; set; }           // مشمول مالیات مؤثر
        public byte PAY_BASE_DAYS { get; set; }     // 1=DAYS | 2=DAYSB
        public byte INS_BASE_DAYS { get; set; }     // 1=DAYS | 2=DAYSB
        public string? EFF_SHIFT_MODE { get; set; } // PCT | FIXED (برای آیتم SHIFT)

        public int ACTIVE_DAYS { get; set; }        // تعداد روزهای فعالِ حکم در این دوره (برای prorate)
        public decimal DEC_DAILY_BASE { get; set; } // پایهٔ روزانهٔ رسمی حکم (BASE_SAL_B با fallback به BASE_SAL) — ریلِ بیمهٔ شیفتِ درصدی
        public decimal DEC_DAILY_NOM { get; set; }  // پایهٔ روزانهٔ اسمی حکم (BASE_SAL با fallback به BASE_SAL_B) — ریلِ پرداختِ شیفتِ درصدی
    }

    /// <summary>مقدار دستیِ یک آیتم برای پرسنل در دوره (PAY2_ATT_VALUE) — مقدار قطعی، بدون فرمولِ ساخت.</summary>
    public class Pay2AuditAttValueRow
    {
        public int EMP_ID { get; set; }
        public int ITEM_ID { get; set; }
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
        public byte ITEM_TYPE { get; set; }
        public short SORT_ORDER { get; set; }
        public long VALUE { get; set; }
        public bool INS_SUBJECT { get; set; }
        public bool TAX_SUBJECT { get; set; }
    }

    /// <summary>نتیجهٔ قطعیِ موتور C# برای هر پرسنل (از PAY2_RUN_LINE) — برای شیت کنترل تطابق.</summary>
    public class Pay2AuditResultRow
    {
        public int EMP_ID { get; set; }
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
    }

    /// <summary>مبلغِ قطعیِ هر آیتم برای هر پرسنل (از PAY2_RUN_DETAIL) — برای تطبیق ستون‌های آیتم.</summary>
    public class Pay2AuditDetailRow
    {
        public int EMP_ID { get; set; }
        public string ITEM_CODE { get; set; } = "";
        public long AMOUNT { get; set; }
    }

    /// <summary>تعریف یک آیتم حقوقی (PAY2_ITEM_DEF) — برای تعیین مشمولیت/نوعِ آیتم‌های خودکار.</summary>
    public class Pay2AuditItemDefRow
    {
        public int ITEM_ID { get; set; }
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
        public byte ITEM_TYPE { get; set; }
        public bool INS_SUBJECT { get; set; }
        public bool TAX_SUBJECT { get; set; }
        public short SORT_ORDER { get; set; }
    }

    /// <summary>ستونِ پویا (آیتمِ حاضر در این اجرا) — هم‌ترتیب با گرید.</summary>
    public class Pay2AuditColumn
    {
        public string ITEM_CODE { get; set; } = "";
        public string ITEM_NAME { get; set; } = "";
        public short SORT_ORDER { get; set; }
    }

    /// <summary>ظرفِ کاملِ داده‌های موردنیاز موتور اکسل برای یک اجرا (Run).</summary>
    public class Pay2ExcelAuditData
    {
        public int RUN_ID { get; set; }
        public long PERIOD_DATE { get; set; }
        public string PERIOD_TITLE { get; set; } = "";
        public string WORKSHOP_NAME { get; set; } = "";
        public short TAX_YEAR { get; set; }
        public int MONTH_DAYS { get; set; }

        public List<Pay2CfgRow> Config { get; set; } = new();
        public List<Pay2TaxBracketRow> TaxBrackets { get; set; } = new();
        public List<Pay2AuditEmpRow> Employees { get; set; } = new();
        public List<Pay2AuditDecreeLineRow> DecreeLines { get; set; } = new();
        public List<Pay2AuditAttValueRow> AttValues { get; set; } = new();
        public List<Pay2AuditResultRow> Results { get; set; } = new();
        public List<Pay2AuditDetailRow> Details { get; set; } = new();
        public List<Pay2AuditColumn> Columns { get; set; } = new();
        public List<Pay2AuditItemDefRow> ItemDefs { get; set; } = new();
    }
}