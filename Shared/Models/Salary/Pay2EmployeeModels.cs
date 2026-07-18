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
        public string? SHIFT_MODE { get; set; }
        public string? NOTES { get; set; }
        public string? TemplateName { get; set; }
    }

    // =========================================================
    // این کلاس جدید برای اقلام ریالی اضافه می‌شود
    // =========================================================
    public class Pay2DecreeLineDto
    {
        public int DEC_ID { get; set; }
        public int ITEM_ID { get; set; }
        public string? ITEM_NAME { get; set; }
        public decimal AMOUNT { get; set; }

        public bool? INS_OV { get; set; }
        public bool? TAX_OV { get; set; }
        public byte? BASIS_OV { get; set; }
        public string? SHIFT_MODE_OV { get; set; }

        // 👇 تغییر عدد 0 به 3 برای رفع مشکل خالی شدن کمبوباکس در Blazor
        public int InsCombo
        {
            get => INS_OV == null ? 3 : (INS_OV == true ? 1 : 2);
            set => INS_OV = value == 3 ? (bool?)null : (value == 1);
        }

        public int TaxCombo
        {
            get => TAX_OV == null ? 3 : (TAX_OV == true ? 1 : 2);
            set => TAX_OV = value == 3 ? (bool?)null : (value == 1);
        }

        // 👇 شناسه 9 برای "طبق تعریف پایه" (null) — مقادیر 1=روزانه، 2=ماهیانه، 3=ساعتی مستقیماً ذخیره می‌شوند
        public int BasisCombo
        {
            get => BASIS_OV == null ? 9 : BASIS_OV.Value;
            set => BASIS_OV = value == 9 ? (byte?)null : (byte)value;
        }

        public string InsText => INS_OV == null ? "پایه" : (INS_OV == true ? "مشمول" : "معاف");
        public string TaxText => TAX_OV == null ? "پایه" : (TAX_OV == true ? "مشمول" : "معاف");
        public string BasisText => BASIS_OV switch
        {
            null => "پایه",
            1 => "روزانه",
            3 => "ساعتی",
            _ => "ماهیانه"
        };
    }

    public class Pay2LeaveBalDto
    {
        public int EMP_ID { get; set; }
        public short YEAR { get; set; }

        // مقادیر بر حسب دقیقه هستند (۱ روز = ۴۴۰ دقیقه در نسخه v6)
        public int ENTITLEMENT_MIN { get; set; } = 11440; // پیش‌فرض 26 روز
        public int USED_MIN { get; set; } = 0;
        public int CARRIED_IN_MIN { get; set; } = 0;
        public int CARRIED_OUT_MIN { get; set; } = 0;

        // --- پراپرتی‌های نمایشی و محاسباتی ---
        public int BALANCE_MIN => ENTITLEMENT_MIN + CARRIED_IN_MIN - USED_MIN;
        public decimal BALANCE_DAYS => Math.Round((decimal)BALANCE_MIN / 440m, 2);
    }
    public class Pay2LoanDto
    {
        public int LOAN_ID { get; set; }
        public int EMP_ID { get; set; }
        public int WS_ID { get; set; }
        public byte LOAN_TYPE { get; set; } = 1;
        public long LOAN_DATE { get; set; }
        public long AMOUNT { get; set; }
        public long INSTALLMENT { get; set; }
        public short TOTAL_INST { get; set; }
        public short PAID_INST { get; set; }
        public long FIRST_PAY { get; set; }
        public string? PURPOSE { get; set; }
        public bool IS_ACTIVE { get; set; } = true;

        // --- پراپرتی‌های نمایشی و محاسباتی ---
        public string LoanTypeText => LOAN_TYPE switch
        {
            1 => "قرض‌الحسنه",
            2 => "رفاهی",
            3 => "ضروری",
            4 => "مسکن",
            5 => "سایر",
            _ => "نامشخص"
        };

        public long BALANCE => AMOUNT - (PAID_INST * INSTALLMENT);
    }
    public class Pay2OverrideDto
    {
        public int EMP_ID { get; set; }
        public int ITEM_ID { get; set; }
        public string? ITEM_NAME { get; set; }

        public bool? INS_OV { get; set; }
        public bool? TAX_OV { get; set; }
        public byte? BASIS_OV { get; set; }

        public long VALID_FROM { get; set; }
        public long? VALID_TO { get; set; }
        public string? REASON { get; set; }

        // --- Helper Properties برای بایند کردن به Dropdown ---
        // دقت کنید: شناسه 3 برای "بدون تغییر" است تا مشکل خالی شدن کمبوباکس پیش نیاید
        public int InsCombo
        {
            get => INS_OV == null ? 3 : (INS_OV == true ? 1 : 2);
            set => INS_OV = value == 3 ? (bool?)null : (value == 1);
        }

        public int TaxCombo
        {
            get => TAX_OV == null ? 3 : (TAX_OV == true ? 1 : 2);
            set => TAX_OV = value == 3 ? (bool?)null : (value == 1);
        }

        // 👇 شناسه 9 برای "بدون تغییر" (null) — مقادیر 1=روزانه، 2=ماهیانه، 3=ساعتی مستقیماً ذخیره می‌شوند
        public int BasisCombo
        {
            get => BASIS_OV == null ? 9 : BASIS_OV.Value;
            set => BASIS_OV = value == 9 ? (byte?)null : (byte)value;
        }

        // --- برای نمایش در جدول گرید ---
        public string InsText => INS_OV == null ? "بدون تغییر" : (INS_OV == true ? "مشمول" : "معاف");
        public string TaxText => TAX_OV == null ? "بدون تغییر" : (TAX_OV == true ? "مشمول" : "معاف");
        public string BasisText => BASIS_OV switch
        {
            null => "بدون تغییر",
            1 => "روزانه",
            3 => "ساعتی",
            _ => "ماهیانه"
        };
    }
    public class Pay2AdvanceExclDto
    {
        public int EXCL_ID { get; set; }
        public int EMP_ID { get; set; }
        public long PERIOD_DATE { get; set; }
        public long EXCL_AMOUNT { get; set; }
        public string? REASON { get; set; }
        public double? DEED_N_S { get; set; }
    }

    public class Pay2SettlementInputDto
    {
        public long SETTLE_DATE { get; set; }
        public long END_DATE { get; set; }
        public long OTHER_INCOME { get; set; }
        public long OTHER_DED { get; set; }
        public long PREV_CREDIT { get; set; }
    }

    public class Pay2SettlementDto
    {
        public int SET_ID { get; set; }
        public int EMP_ID { get; set; }
        public int WS_ID { get; set; }
        public long SETTLE_DATE { get; set; }
        public long END_DATE { get; set; }
        public int SENIORITY_DAYS { get; set; }
        public decimal SENIORITY_YEARS { get; set; }
        public long LAST_SALARY { get; set; }

        public int LEAVE_BAL_MIN { get; set; }
        public decimal LEAVE_BAL_DAYS { get; set; }

        public long EIDI { get; set; }
        public long BON { get; set; }
        public long LEAVE_PAY { get; set; }
        public long SANAVAT { get; set; }
        public long PREV_CREDIT { get; set; }
        public long OTHER_INCOME { get; set; }

        public long PREV_DEBIT { get; set; }
        public long EIDI_TAX { get; set; }
        public long LOAN_BALANCE { get; set; }
        public long OTHER_DED { get; set; }

        public byte STATUS { get; set; }

        // --- پراپرتی‌های محاسباتی و نمایشی ---
        public long TOTAL_INCOME => EIDI + BON + LEAVE_PAY + SANAVAT + PREV_CREDIT + OTHER_INCOME;
        public long TOTAL_DED => PREV_DEBIT + EIDI_TAX + LOAN_BALANCE + OTHER_DED;
        public long NET_SETTLE => TOTAL_INCOME - TOTAL_DED;

        public string StatusText => STATUS == 1 ? "پیش‌نویس" : (STATUS == 2 ? "تأیید نهایی" : "سند صادر شده");
        public string StatusColor => STATUS == 1 ? "#d97706" : (STATUS == 2 ? "#059669" : "#2563eb");
        public bool CanEdit => STATUS == 1; // فقط پیش‌نویس‌ها قابل حذف یا تایید هستند
    }

    public class Pay2ItemTemplateDto
    {
        public int TMPL_ID { get; set; }
        public string? TMPL_CODE { get; set; }
        public string? TMPL_NAME { get; set; }
        public int? WS_ID { get; set; }
        public bool IS_ACTIVE { get; set; } = true;
        public string? NOTES { get; set; }

        // پراپرتی‌های نمایشی برای گرید کلاینت
        public string? WorkshopName { get; set; }
        public string DisplayWorkshop => (WS_ID == null || WS_ID <= 0) ? "همه کارگاه‌ها" : WorkshopName ?? "نامشخص";
    }
    public class Pay2ItemTmplLineDto
    {
        public int TMPL_ID { get; set; }
        public int ITEM_ID { get; set; }
        public string? ITEM_NAME { get; set; }
        public decimal DEF_AMOUNT { get; set; }
        public bool? INS_OV { get; set; }
        public bool? TAX_OV { get; set; }
        public byte? BASIS_OV { get; set; }
        public string? SHIFT_MODE_OV { get; set; }

        // پراپرتی‌های کمکی برای بایندینگ به کامبوباکسِ Pay2Select در UI
        public int InsCombo { get => INS_OV == null ? 3 : (INS_OV == true ? 1 : 2); set => INS_OV = value == 3 ? (bool?)null : (value == 1); }
        public int TaxCombo { get => TAX_OV == null ? 3 : (TAX_OV == true ? 1 : 2); set => TAX_OV = value == 3 ? (bool?)null : (value == 1); }
        // 👇 شناسه 9 برای "طبق تعریف پایه" (null) — مقادیر 1=روزانه، 2=ماهیانه، 3=ساعتی مستقیماً ذخیره می‌شوند
        public int BasisCombo { get => BASIS_OV == null ? 9 : BASIS_OV.Value; set => BASIS_OV = value == 9 ? (byte?)null : (byte)value; }

        public string InsText => INS_OV == null ? "پایه" : (INS_OV == true ? "مشمول" : "معاف");
        public string TaxText => TAX_OV == null ? "پایه" : (TAX_OV == true ? "مشمول" : "معاف");
        public string BasisText => BASIS_OV switch
        {
            null => "پایه",
            1 => "روزانه",
            3 => "ساعتی",
            _ => "ماهیانه"
        };
    }
    public class Pay2JobDto
    {
        public int JOB_ID { get; set; }
        public string? JOB_CODE { get; set; }
        public string? JOB_NAME { get; set; }
        public string? JOB_GROUP { get; set; }
        public bool IS_ACTIVE { get; set; } = true;
    }

    // --- کلاس مخصوص گزارش کلان مرخصی ---
    public class Pay2LeaveReportRowDto
    {
        public string EMP_CODE { get; set; } = "";
        public string FULL_NAME { get; set; } = "";

        // کل سال
        public int ENTITLEMENT_MIN { get; set; }
        public int CARRIED_IN_MIN { get; set; }
        public int USED_MIN { get; set; }
        public int BALANCE_MIN { get; set; }
        public decimal BALANCE_DAYS { get; set; }

        // تا تاریخ روز (Pro-rata)
        public int PRORATA_ENTITLEMENT_MIN { get; set; }
        public int PRORATA_BALANCE_MIN { get; set; }
        public decimal PRORATA_BALANCE_DAYS { get; set; }
    }
}
