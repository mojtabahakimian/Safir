using Safir.Shared.Models.Treasury;

namespace Safir.Shared.Models.Sanad
{
    // ═══════════════════════════════════════════════════════════════════
    //  صدور و ویرایشِ اسنادِ حسابداری — DEED_HED (سربرگ) و DEED_DTL (ردیف‌ها)،
    //  همان جدول‌هایی که فرمِ DEED_HEADِ نرم‌افزار WPF می‌نویسد.
    //  حساب، چک، امضا و ارجاع همان مدل‌های خزانه‌اند (Safir.Shared.Models.Treasury).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>DEED_HED.NO_S — همان نگاشتِ WPF.</summary>
    public static class SanadKinds
    {
        public const int Manual = 0;

        public static readonly (int Id, string Name)[] All =
        {
            (0, "عمومی"), (1, "فاکتور خرید"), (2, "فاکتور فروش"), (3, "برگشت خرید"), (4, "برگشت فروش"),
            (5, "خزانه‌داری"), (6, "وصول چک دریافتی"), (7, "وصول چک پرداختی"), (8, "حواله خروج مواد"),
            (9, "ورود کالا به انبار"), (10, "حواله انتقالی"), (11, "حقوق و دستمزد"), (12, "خروج سایر مواد"),
            (17, "انبارگردانی")
        };

        public static string NameOf(int noS)
        {
            foreach (var (id, name) in All) if (id == noS) return name;
            return $"نوع {noS}";
        }
    }

    /// <summary>کدام ردیف چک می‌گیرد — مشترکِ صفحه و سرور.</summary>
    public enum SanadChequeRole { None, Received, Paid }

    public static class SanadRules
    {
        /// <summary>
        /// Child14_CellEditEnding ِ WPF: بدهکار روی ADA یا ADV → چکِ دریافتی (SGETCHEK)؛
        /// بستانکار روی APA یا APV → چکِ پرداختی (SPAYCHEK).
        /// </summary>
        public static SanadChequeRole RoleOf(string? hes, double bed, double bes, string? ada, string? adv, string? apa, string? apv)
        {
            hes = (hes ?? "").Trim();
            if (hes.Length == 0) return SanadChequeRole.None;
            bool Is(string? a) => !string.IsNullOrWhiteSpace(a) && hes == a.Trim();
            if (bed > 0 && (Is(ada) || Is(adv))) return SanadChequeRole.Received;
            if (bes > 0 && (Is(apa) || Is(apv))) return SanadChequeRole.Paid;
            return SanadChequeRole.None;
        }

        /// <summary>Child14_RowEditEnding: دقیقاً یکی از بدهکار/بستانکار، مثبت، ریالی (بی‌اعشار).</summary>
        public static string? AmountError(double bed, double bes)
        {
            if (double.IsNaN(bed) || double.IsNaN(bes) || double.IsInfinity(bed) || double.IsInfinity(bes) || bed < 0 || bes < 0)
                return "مبلغ صحیح نیست.";
            if (bed > 0 && bes > 0) return "بدهکار و بستانکارِ یک ردیف نمی‌توانند هر دو پر باشند.";
            if (bed == 0 && bes == 0) return "بدهکار یا بستانکار نمی‌تواند خالی باشد.";
            if (bed != Math.Floor(bed) || bes != Math.Floor(bes)) return "مبلغ باید به ریال و بدونِ اعشار باشد.";
            if (bed > 1e15 || bes > 1e15) return "مبلغ بیش از اندازه است.";
            return null;
        }
    }

    public class SanadListItemDto
    {
        public double Ns { get; set; }
        public long Date { get; set; }
        public string? Sharh { get; set; }
        /// <summary>DEED_HED.NO_S — ۰ سندِ دستی، باقی سندِ خودکارِ فرم‌های دیگر.</summary>
        public int NoS { get; set; }
        /// <summary>شماره‌ی مبنا (DEED_HED.base) — پرونده‌ی اتوماسیونِ سند با همین شماره است.</summary>
        public int Base { get; set; }
        /// <summary>شماره‌ی بایگانی.</summary>
        public int? Bayeg { get; set; }
        public string? UserName { get; set; }
        /// <summary>تأیید/چاپ‌شده — سندِ OKF فقط بعد از «اصلاح سند» ویرایش می‌شود.</summary>
        public bool Okf { get; set; }
        /// <summary>قطعی (GHATEI) — برای همیشه قفل.</summary>
        public bool Final { get; set; }
        public bool Sgn1 { get; set; }
        public bool Sgn2 { get; set; }
        public bool Sgn3 { get; set; }
        public int? Sgn1User { get; set; }
        public int? Sgn2User { get; set; }
        public int? Sgn3User { get; set; }
        public int RowCount { get; set; }
        public double SumBed { get; set; }
        public double SumBes { get; set; }
        public DateTime? Created { get; set; }

        public bool Signed => Sgn1 || Sgn2 || Sgn3;
        public bool Manual => NoS == SanadKinds.Manual;
        /// <summary>مثل IsBalanceSanadOk ِ WPF: اختلافِ گردشده‌ی بدهکار و بستانکار.</summary>
        public double Difference => Math.Round(SumBed - SumBes, 0);
        public bool Balanced => Difference == 0;
    }

    public class SanadRowDto
    {
        /// <summary>DEED_DTL.id</summary>
        public long Id { get; set; }
        public string Hes { get; set; } = "";
        public string? HesName { get; set; }
        public string? Sharh { get; set; }
        public double Bed { get; set; }
        public double Bes { get; set; }
        public int? MhazNo { get; set; }
        public double? NSeri { get; set; }
        public int? Bank { get; set; }
        public string? BankName { get; set; }
        /// <summary>اتصال به فاکتور (HEAD_LST) — فقط در سندهای خودکار.</summary>
        public double? Number { get; set; }
        public double? Tag { get; set; }
        /// <summary>چکِ این سطر — فقط ردیفِ بدهکارِ اسناد دریافتنی یا بستانکارِ اسناد پرداختنی.</summary>
        public TreasuryChequeDto? Cheque { get; set; }
        /// <summary>نامِ حسابِ صاحبِ چک (PAY_GETD.CUST_NO).</summary>
        public string? ChequeOwnerName { get; set; }
    }

    /// <summary>سندِ خودکار از کجا آمده — برای «از فرمِ مبدأ اصلاح کنید».</summary>
    public class SanadSourceDto
    {
        public string Title { get; set; } = "";
        /// <summary>صفحه‌ی مبدأ در Safir (مثلاً /treasury/12)؛ خالی اگر فقط در WPF است.</summary>
        public string? Link { get; set; }
    }

    public class SanadDetailDto
    {
        public SanadListItemDto Header { get; set; } = new();
        public List<SanadRowDto> Rows { get; set; } = new();
        /// <summary>چرا قابلِ اصلاح نیست (خالی = با «اصلاح سند» باز می‌شود).</summary>
        public string? LockReason { get; set; }
        public SanadSourceDto? Source { get; set; }
        /// <summary>CHAPNUM — قبلاً چاپ شده.</summary>
        public bool Printed { get; set; }
        /// <summary>تعدادِ نسخه‌های ذخیره‌شده در سوابقِ اصلاح (TR_DEED_HED).</summary>
        public int HistoryCount { get; set; }

        public string? Sgn1Name { get; set; }
        public string? Sgn2Name { get; set; }
        public string? Sgn3Name { get; set; }
        /// <summary>پرونده‌ی اتوماسیونِ سند (TASKS با tg = 0 و num = شماره‌ی مبنا).</summary>
        public TreasuryTaskDto? Task { get; set; }
    }

    public class SanadMetaDto
    {
        public bool CanSee { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
        /// <summary>فرمِ «تایید و قطعی کردن اسناد» (F_MENU_ASNAD).</summary>
        public bool CanFinalize { get; set; }

        /// <summary>جدولِ SIGN: SND_TAHI (تنظیم‌کننده)، SND_MALI (مدیر مالی)، SND_MODIR (مدیر عامل).</summary>
        public bool CanSign1 { get; set; }
        public bool CanSign2 { get; set; }
        public bool CanSign3 { get; set; }
        public List<string> SignTitles { get; set; } = new();
        public List<TreasuryLookupItem> Personnel { get; set; } = new();

        /// <summary>OPTIONSS[43] — ستونِ مرکزِ هزینه.</summary>
        public bool MarkazEnabled { get; set; }
        public List<TreasuryLookupItem> CostCenters { get; set; } = new();

        // ── چک (SGETCHEK / SPAYCHEK) ──
        public string? Ada { get; set; }
        public string? Adv { get; set; }
        public string? Apa { get; set; }
        public string? Apv { get; set; }
        public int? Bankha { get; set; }
        public List<TreasuryLookupItem> Banks { get; set; } = new();
        public List<TreasuryLookupItem> ChequeFunds { get; set; } = new();
        public List<TreasuryAccountDto> BankAccounts { get; set; } = new();

        public bool DateControl { get; set; }
        public int? FiscalYear { get; set; }
        public string? UserName { get; set; }
        public int UserId { get; set; }
    }

    public class SanadHeaderSaveRequest
    {
        public long Date { get; set; }
        public string? Sharh { get; set; }
    }

    public class SanadRowSaveRequest
    {
        public string? Hes { get; set; }
        public string? Sharh { get; set; }
        public double Bed { get; set; }
        public double Bes { get; set; }
        public int? MhazNo { get; set; }
        /// <summary>
        /// مشخصاتِ چک — فقط برای ردیفِ بدهکارِ اسناد دریافتنی (ADA/ADV) یا بستانکارِ اسناد پرداختنی
        /// (APA/APV)؛ خالی = ردیفِ بی‌چک (مثل «خروج» از پنجره‌ی چکِ WPF).
        /// </summary>
        public TreasuryChequeInput? Cheque { get; set; }
        /// <summary>حسابِ صاحبِ چکِ دریافتی (PAY_GETD.CUST_NO) — اختیاری.</summary>
        public string? ChequeOwner { get; set; }
    }

    public class SanadSaveResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public double? Ns { get; set; }
        /// <summary>DEED_DTL.id ِ ردیفِ ذخیره‌شده.</summary>
        public long? RowId { get; set; }
        public string? Info { get; set; }
        public int Count { get; set; }
    }

    public class SanadHistoryItemDto
    {
        public long Tridd { get; set; }
        public long UpDate { get; set; }
        public double UpTime { get; set; }
        public string? User { get; set; }
        public string? Pc { get; set; }
        public long Date { get; set; }
        public string? Sharh { get; set; }
        public int RowCount { get; set; }
        public double SumBed { get; set; }
        public double SumBes { get; set; }
    }

    public class SanadHistoryRowDto
    {
        public string? Hes { get; set; }
        public string? HesName { get; set; }
        public string? Sharh { get; set; }
        public double Bed { get; set; }
        public double Bes { get; set; }
    }

    public class SanadFinalizeRequest
    {
        public double From { get; set; }
        public double To { get; set; }
        /// <summary>false = فقط پیش‌نمایش (چند سند، چندتا ناتراز).</summary>
        public bool Apply { get; set; }
    }

    public class SanadFinalizePreviewDto
    {
        public int Count { get; set; }
        public int AlreadyFinal { get; set; }
        public int Unbalanced { get; set; }
        public int Unsigned { get; set; }
        public int Applied { get; set; }
    }

    /// <summary>
    /// چاپِ سند — R_SANAD_PRINT («چاپ سند») و R_SANAD_PRINT_B («چاپ سند ۲»، بی مبنا و شرحِ سند).
    /// ردیف‌ها زیرِ حسابِ کل گروه می‌شوند؛ گروه‌های بدهکار اول (بزرگ به کوچک)، بعد بستانکار.
    /// </summary>
    public class SanadPrintDto
    {
        public bool Full { get; set; } = true;
        public string? CompanyName { get; set; }
        public string? FiscalYear { get; set; }
        public double Ns { get; set; }
        public long Date { get; set; }
        public int Base { get; set; }
        public int? Bayeg { get; set; }
        public string? Sharh { get; set; }
        public string? UserName { get; set; }
        public List<SanadPrintGroup> Groups { get; set; } = new();
        public double TotalBed { get; set; }
        public double TotalBes { get; set; }
        public string? TotalInWords { get; set; }
        public List<TreasuryPrintSigner> Signers { get; set; } = new();
        public long PrintedOn { get; set; }
    }

    public class SanadPrintGroup
    {
        public int Kol { get; set; }
        public string? KolName { get; set; }
        public bool Debit { get; set; }
        public double Sum { get; set; }
        public List<SanadPrintRow> Rows { get; set; } = new();
    }

    public class SanadPrintRow
    {
        public string? Hes { get; set; }
        public string? Name { get; set; }
        public string? Sharh { get; set; }
        public double Amount { get; set; }
    }
}
