namespace Safir.Shared.Models.Salary
{
    /// <summary>
    /// DTO کارگاه — مطابق PAY2_WORKSHOP
    /// </summary>
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
        public string? SHIFT_MODE { get; set; }
        public bool IS_ACTIVE { get; set; } = true;
        public int INS_MODE { get; set; } = 1;
        public string? POSTAL_CODE { get; set; }
        public string? EMPLOYER_NAME { get; set; }

        public string? PROVINCE { get; set; }
        public string? CITY { get; set; }
        public string? REGISTRATION_NUMBER { get; set; }
        public string? SSO_BRANCH { get; set; }
        public string? FINANCIAL_MANAGER { get; set; }
        public string? ADMIN_MANAGER { get; set; }

        public byte DEFAULT_DEED_MODE { get; set; } = 1;

        public string InsModeText => INS_MODE switch
        {
            1 => "معمولی",
            2 => "ده‌درصدی",
            _ => "نامشخص"
        };
    }

    /// <summary>
    /// DTO سرفصل‌های حسابداری کارگاه — مطابق PAY2_WORKSHOP_ACC
    ///
    /// ADV_HES: کد ترکیبی حساب مساعده هوشمند، فرمت: "کل-معین[-تفصیلی[-تفصیلی2...]]"
    ///          مثال: "112-1"  یا  "213-1-5"
    ///          این مقدار مستقیماً در SP_PAY2_GET_ADVANCES با ACC_KEY='ADV_HES' خوانده می‌شود.
    ///          (قبلاً به اشتباه به صورت دو فیلد ADV_HES_K و ADV_HES_M ذخیره می‌شد)
    /// </summary>
    public class Pay2WorkshopAccDto
    {
        public int WS_ID { get; set; }

        // ── مساعده هوشمند ─────────────────────────────────────────────────────────
        /// <summary>
        /// کد ترکیبی حساب مساعده — با خط‌فاصله جدا می‌شود.
        /// مثال: "112-1"  →  کل=112، معین=1
        /// مثال: "213-1-5"  →  کل=213، معین=1، تفصیلی=5
        /// SP_PAY2_GET_ADVANCES این رشته را parse می‌کند.
        /// </summary>
        public string? ADV_HES { get; set; }

        // ── سند حقوق ──────────────────────────────────────────────────────────────
        public string? SALARY_EXP { get; set; }
        public string? SALARY_PAYABLE { get; set; }

        // ── سند بیمه و مالیات ─────────────────────────────────────────────────────
        public string? INS_EXP { get; set; }
        public string? INS_PAYABLE { get; set; }
        public string? TAX_PAYABLE { get; set; }

        public string? SALARY_EXP_TOLID { get; set; }
        public string? SALARY_EXP_EDARI { get; set; }
        public string? SALARY_EXP_FOROSH { get; set; }
        public string? SALARY_EXP_KHADAMAT { get; set; }
        public string? LOAN_HES { get; set; }
        public string? BANK_PAY_HES { get; set; }

        // 👇 فیلد جدید اضافه شد (برای حساب سایر کسورات)
        public string? OTHER_DED_HES { get; set; }
    }

    /// <summary>
    /// درخواست ذخیره کارگاه (Workshop + Accounts در یک تراکنش)
    /// </summary>
    public class Pay2WorkshopSaveRequest
    {
        public Pay2WorkshopDto Workshop { get; set; } = new();
        public Pay2WorkshopAccDto Accounts { get; set; } = new();
    }
}