namespace Safir.Shared.Models.Treasury
{
    // ═══════════════════════════════════════════════════════════════════
    //  خزانه‌داری — PGET_HED (سربرگ) و PGET_LST (سطرها)، همان جدول‌هایی
    //  که فرمِ PGET_HEDِ نرم‌افزار WPF می‌نویسد. هر خزانه یک سند حسابداری
    //  با NO_S = 5 دارد که از روی سطرها بازسازی می‌شود.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>نوع عملیات (TCOD_DPS).</summary>
    public static class TreasuryOp
    {
        public const int Receipt = 1; // دریافت
        public const int Payment = 2; // پرداخت
    }

    /// <summary>نحوه (TCOD_DPSKIND).</summary>
    public static class TreasuryMethod
    {
        public const int Cash = 1;             // نقد
        public const int Cheque = 2;           // چک
        public const int Other = 3;            // سایر
        public const int ChequeAssign = 4;     // واگذاری چک
        public const int ChequeReturn = 5;     // برگشت چک
        public const int NonTradeCheque = 6;   // چک غیر تجاری

        /// <summary>سطرهایی که پنجره‌ی چک و جدول‌های PAY_GETD/PAY_GETP را درگیر می‌کنند.</summary>
        public static bool IsCheque(int nahva) => nahva is Cheque or ChequeAssign or ChequeReturn or NonTradeCheque;

        /// <summary>«چک» یا «چکِ غیرتجاری» — مشخصاتِ چک در همین سطر وارد می‌شود (GETCHEK / PAYCHEK).</summary>
        public static bool IsNewCheque(int nahva) => nahva is Cheque or NonTradeCheque;

        /// <summary>واگذاری یا برگشت — چکی که قبلاً ثبت شده از فهرست انتخاب می‌شود.</summary>
        public static bool PicksCheque(int nahva) => nahva is ChequeAssign or ChequeReturn;

        /// <summary>WPF: «واگذاری» برای دریافت مجاز نیست («مقدار وارده مجاز نیست»).</summary>
        public static bool Allowed(int noAm, int nahva) => !(noAm == TreasuryOp.Receipt && nahva == ChequeAssign);

        /// <summary>
        /// سطرِ اصلاح‌شده هنوز به همان چک و همان نقش اشاره دارد؟ نقد/سایر چکی ندارند؛ «چک» و «چکِ غیرتجاری»
        /// با همان نوعِ عملیات در یک جدول‌اند و فقط KIND ِ چک عوض می‌شود. وگرنه چکِ قبلیِ سطر مثلِ حذفِ سطر آزاد می‌شود.
        /// </summary>
        public static bool SameChequeRole(int oldNoAm, int oldNahva, int newNoAm, int newNahva)
            => !IsCheque(oldNahva)
               || (oldNoAm == newNoAm && (oldNahva == newNahva || (IsNewCheque(oldNahva) && IsNewCheque(newNahva))));
    }

    public class TreasuryListItemDto
    {
        public int Id { get; set; }
        public long Date { get; set; }
        public string? Molah { get; set; }
        public double? Ns { get; set; }
        public int? Depatman { get; set; }
        public string? DepName { get; set; }
        public int? Shift { get; set; }
        public string? UserName { get; set; }
        public int Kind { get; set; }
        public int? Idk { get; set; }
        public bool Okf { get; set; }
        public bool Sgn1 { get; set; }
        public bool Sgn2 { get; set; }
        public bool Sgn3 { get; set; }
        /// <summary>امضاکننده‌ها (sgn1usid..sgn3usid) — SALA_DTL.IDD.</summary>
        public int? Sgn1User { get; set; }
        public int? Sgn2User { get; set; }
        public int? Sgn3User { get; set; }
        /// <summary>سندِ حسابداری قطعی شده (DEED_HED.GHATEI) — دیگر هیچ تغییری مجاز نیست.</summary>
        public bool Final { get; set; }
        public int RowCount { get; set; }
        public double SumReceipt { get; set; }
        public double SumPayment { get; set; }
        public int ChequeRows { get; set; }

        public bool Signed => Sgn1 || Sgn2 || Sgn3;
    }

    public class TreasuryRowDto
    {
        public int Idh { get; set; }
        public int Id { get; set; }
        public double? Radif { get; set; }
        public int NoAm { get; set; }
        public int Nahva { get; set; }
        public string? Fhes { get; set; }
        public string? FhesName { get; set; }
        public string? Thes { get; set; }
        public string? ThesName { get; set; }
        public string? Sharh { get; set; }
        public double Mabl { get; set; }
        public double? NSeri { get; set; }
        public int? Bank { get; set; }
        public string? BankName { get; set; }
        public int? MhazNo { get; set; }
        public double? Arzd { get; set; }
        public long? ArzKind2 { get; set; }
        public bool HasAttachment { get; set; }
        /// <summary>چکِ این سطر در PAY_GETD یا PAY_GETP (فقط سطرهای چکی).</summary>
        public TreasuryChequeDto? Cheque { get; set; }
    }

    /// <summary>
    /// یک چک — PAY_GETD (دریافتنی) یا PAY_GETP (پرداختنی). همان فیلدهایی که پنجره‌های
    /// GETCHEK / PAYCHEK / FORCHEK / BAKCHEK / BAKCHEKP نشان می‌دهند.
    /// </summary>
    public class TreasuryChequeDto
    {
        public long Id { get; set; }
        /// <summary>true = PAY_GETP (چکِ خودمان)، false = PAY_GETD (چکِ دریافتی).</summary>
        public bool Payable { get; set; }
        public double NSeri { get; set; }
        public int Bank { get; set; }
        public string? BankName { get; set; }
        /// <summary>تاریخ سررسید.</summary>
        public long DateS { get; set; }
        /// <summary>تاریخ دریافت / پرداخت.</summary>
        public long Date { get; set; }
        public string? Shobeh { get; set; }
        public double Mabl { get; set; }
        public string? NameTah { get; set; }
        /// <summary>جاری چک (شماره حساب صاحب چک).</summary>
        public string? NHesab { get; set; }
        /// <summary>کد شعبه (فقط PAY_GETD).</summary>
        public int? ListNo { get; set; }
        /// <summary>موقعیت چک — تفصیلیِ زیرِ اسناد دریافتنی (فقط PAY_GETD).</summary>
        public int? Sandugh { get; set; }
        public string? Sayadi { get; set; }
        /// <summary>حسابِ واگذاری (PAY_GETD) یا حسابِ بانکیِ پرداخت (PAY_GETP).</summary>
        public string? Hes1 { get; set; }
        public string? Hes2 { get; set; }
        public double? Vaz { get; set; }
        public int? Kind { get; set; }
        public double? Radif { get; set; }
        public string? CustNo { get; set; }
        public int? NKol { get; set; }
        public int? NKol2 { get; set; }
        public int? NKol3 { get; set; }
        public double? Ns { get; set; }

        /// <summary>
        /// چکِ دریافتی که به گردش افتاده (واگذار به شخص، وصول، برگشت) — مثل WPF مشخصاتش از
        /// سطرِ دریافت عوض نمی‌شود و سطرِ دریافتش حذف‌شدنی نیست.
        /// </summary>
        public bool InCirculation(int? bankha)
            => Vaz == 4 || NKol2 is not null and not 911 || NKol3 is not null
               || (!Payable && NKol is not null and not 911 && NKol != bankha);
    }

    /// <summary>وضعیت‌های چک (VAZ).</summary>
    public static class TreasuryChequeState
    {
        /// <summary>چکِ دریافتی (PAY_GETD) — BAKCHEK.</summary>
        public static readonly (int Id, string Name)[] Received =
        {
            (1, "نزد صندوق"), (2, "نزد بانک"), (3, "وصول شده"), (4, "واگذار شده"), (5, "برگشت شده"), (6, "مسترد شده")
        };

        /// <summary>برگشتِ چکِ پرداختی (PAY_GETP) — BAKCHEKP.</summary>
        public static readonly (int Id, string Name)[] PaidReturn = { (1, "نزد شخص"), (2, "عودت شده") };

        public static string NameOf(double? vaz, bool payable = false)
        {
            var list = payable ? PaidReturn : Received;
            foreach (var (id, name) in list) if (vaz == id) return name;
            return vaz is null ? "—" : vaz.Value.ToString("0");
        }
    }

    public class TreasuryDetailDto
    {
        public TreasuryListItemDto Header { get; set; } = new();
        public List<TreasuryRowDto> Rows { get; set; } = new();
        /// <summary>شماره‌ی مبنای سند (DEED_HED.base) — فقط نمایشی.</summary>
        public int? SanadBase { get; set; }
        /// <summary>چرا قابل ویرایش نیست (خالی = قابل ویرایش پس از «اصلاح»).</summary>
        public string? LockReason { get; set; }
        /// <summary>چرا «اصلاح خزانه» برای این کاربر بسته است (مثلاً خزانه‌ی کاربرِ دیگر بدونِ DPSEE).</summary>
        public string? UnlockBlock { get; set; }

        public string? Sgn1Name { get; set; }
        public string? Sgn2Name { get; set; }
        public string? Sgn3Name { get; set; }

        /// <summary>پرونده‌ی اتوماسیونِ این خزانه (TASKS با tg = 34) — امضا و ارجاع آنجا ثبت می‌شوند.</summary>
        public TreasuryTaskDto? Task { get; set; }
    }

    public class TreasuryTaskDto
    {
        public long Idnum { get; set; }
        public int? Personel { get; set; }
        public string? PersonelName { get; set; }
        public int? Status { get; set; }
        public List<TreasuryEventDto> Events { get; set; } = new();
    }

    public class TreasuryEventDto
    {
        public string? Text { get; set; }
        public string? UserName { get; set; }
        public long? Date { get; set; }
        public int? Time { get; set; }
    }

    public class TreasuryLookupItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>برای واحدها: قبلاً در خزانه به کار رفته است.</summary>
        public bool Frequent { get; set; }
    }

    public class TreasuryAccountDto
    {
        public string Hes { get; set; } = "";
        public string? Name { get; set; }
    }

    public class TreasuryMetaDto
    {
        public bool CanSee { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
        /// <summary>DPDEED — همه‌ی خزانه‌ها را می‌بیند (وگرنه فقط واحد یا خودش).</summary>
        public bool SeesAll { get; set; }

        public bool ArzActive { get; set; }
        public bool MarkazEnabled { get; set; }

        /// <summary>حسابِ صندوق (SAZMAN.SANDOGH) — کل.</summary>
        public int CashKol { get; set; }
        /// <summary>OPTIONSS[38]=5: معین و تفصیلیِ صندوق = واحد و شیفتِ خزانه.</summary>
        public bool CashByDeptShift { get; set; }

        public int? DefaultDepatman { get; set; }
        public int? DefaultShift { get; set; }
        public string? UserName { get; set; }
        public int UserId { get; set; }

        public List<TreasuryLookupItem> Operations { get; set; } = new();
        public List<TreasuryLookupItem> Methods { get; set; } = new();
        public List<TreasuryLookupItem> Departments { get; set; } = new();
        public List<TreasuryLookupItem> Shifts { get; set; } = new();
        public List<TreasuryLookupItem> CostCenters { get; set; } = new();

        // ── امضا و ارجاع ──
        /// <summary>جدولِ SIGN: SGN0134 (تنظیم‌کننده)، SGN0234 (مدیر مالی)، SGN0334 (مدیر عامل).</summary>
        public bool CanSign1 { get; set; }
        public bool CanSign2 { get; set; }
        public bool CanSign3 { get; set; }
        /// <summary>SAZMAN.SIGN — چاپ فقط بعد از دستِ‌کم یک امضا (Baseknow.SIGN در WPF).</summary>
        public bool PrintNeedsSignature { get; set; }
        /// <summary>فهرستِ ارجاع — SALA_DTL فعال، با ترتیبِ USER_PERSONEL_ORDER ِ همین کاربر.</summary>
        public List<TreasuryLookupItem> Personnel { get; set; } = new();

        // ── چک ──
        public List<TreasuryLookupItem> Banks { get; set; } = new();
        /// <summary>موقعیت چک — تفصیلی‌های زیرِ معینِ اسناد دریافتنی.</summary>
        public List<TreasuryLookupItem> ChequeFunds { get; set; } = new();
        /// <summary>حساب‌های بانکی (کلِ BANKHA) — واگذاری چکِ دریافتی به بانک یا پرداخت از حساب.</summary>
        public List<TreasuryAccountDto> BankAccounts { get; set; } = new();
        public string? Ada { get; set; }
        public string? Apa { get; set; }
        public string? Adv { get; set; }
        public string? Apv { get; set; }
        public int? Bankha { get; set; }

        /// <summary>SAZMAN.CTL_DT — تاریخ باید در سالِ مالیِ SAZMAN.YEA باشد.</summary>
        public bool DateControl { get; set; }
        public int? FiscalYear { get; set; }
    }

    public class TreasuryHeaderSaveRequest
    {
        public long Date { get; set; }
        public string? Molah { get; set; }
        public int? Depatman { get; set; }
        public int? Shift { get; set; }
        /// <summary>۰ عادی، ۲ سند دریافت، ۳ سند پرداخت.</summary>
        public int Kind { get; set; }
    }

    /// <summary>
    /// مشخصاتِ چک برای سطرهای «چک» (دریافت/پرداخت) — GETCHEK و PAYCHEK.
    /// </summary>
    public class TreasuryChequeInput
    {
        public double NSeri { get; set; }
        public int Bank { get; set; }
        public long DateS { get; set; }
        /// <summary>تاریخ دریافت/پرداخت؛ خالی = تاریخِ خزانه.</summary>
        public long? Date { get; set; }
        public string? Shobeh { get; set; }
        public int? ListNo { get; set; }
        public string? NameTah { get; set; }
        public string? NHesab { get; set; }
        public int? Sandugh { get; set; }
        public string? Sayadi { get; set; }
        public string? Hes1 { get; set; }
        /// <summary>چند فقره پشتِ سرِ هم (اضافه‌ی چکِ گروهی) — سریال +۱ و سررسید + فاصله.</summary>
        public int Count { get; set; } = 1;
        /// <summary>فاصله‌ی سررسیدها به ماه (چکِ گروهی).</summary>
        public int GapMonths { get; set; } = 1;
    }

    public class TreasuryRowSaveRequest
    {
        public int NoAm { get; set; }
        public int Nahva { get; set; }
        public string? Fhes { get; set; }
        public string? Thes { get; set; }
        public string? Sharh { get; set; }
        public double Mabl { get; set; }
        public int? MhazNo { get; set; }

        /// <summary>سطرِ «چک» (دریافت/پرداخت، نحوه‌ی ۲ و ۶).</summary>
        public TreasuryChequeInput? Cheque { get; set; }
        /// <summary>واگذاری و برگشت: چکِ انتخاب‌شده از فهرست (PAY_GETD.ID یا PAY_GETP.ID).</summary>
        public long? PickedChequeId { get; set; }
        /// <summary>واگذاری و برگشتِ چکِ دریافتی: موقعیت چک.</summary>
        public int? Sandugh { get; set; }
        /// <summary>برگشت: وضعیتِ تازه‌ی چک.</summary>
        public int? Vaz { get; set; }
    }

    public class TreasurySignRequest
    {
        /// <summary>۱ تنظیم‌کننده، ۲ مدیر مالی، ۳ مدیر عامل.</summary>
        public int Slot { get; set; }
        public bool On { get; set; }
    }

    public class TreasuryReferRequest
    {
        public int Personel { get; set; }
    }

    public class TreasurySaveResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public int? Id { get; set; }
        public int? Idh { get; set; }
        public double? Ns { get; set; }
        /// <summary>پیامِ اطلاع (مثلاً «شماره دفتر: ۹۵۷۶» بعد از ثبتِ چکِ دریافتی).</summary>
        public string? Info { get; set; }
    }

    /// <summary>
    /// چاپ‌های خزانه — همان سه گزارشِ Stimulsoft ِ WPF (Rpts/Khazane_Reports):
    /// amalkard «چاپ عملکرد خزانه»، daryaft «سند دریافت» (sanaddar_sub)، pardakht «سند پرداخت» (sanadpar_sub).
    /// </summary>
    public class TreasuryPrintDto
    {
        public string Kind { get; set; } = "amalkard";
        public string Title { get; set; } = "";
        public string? CompanyName { get; set; }
        public string? FiscalYear { get; set; }
        public int Id { get; set; }
        public int? Idk { get; set; }
        public long Date { get; set; }
        public string? Molah { get; set; }
        public double? Ns { get; set; }
        public int? SanadBase { get; set; }
        public string? UserName { get; set; }
        public List<TreasuryPrintRow> Rows { get; set; } = new();
        public double Total { get; set; }
        public string? TotalInWords { get; set; }
        public List<TreasuryPrintSigner> Signers { get; set; } = new();
        public long PrintedOn { get; set; }
    }

    public class TreasuryPrintRow
    {
        public double? Radif { get; set; }
        public string? Operation { get; set; }
        public string? Method { get; set; }
        public string? FromName { get; set; }
        public string? FromHes { get; set; }
        public string? ToName { get; set; }
        public string? ToHes { get; set; }
        public string? Sharh { get; set; }
        public double Mabl { get; set; }
        public double? NSeri { get; set; }
        public string? NHesab { get; set; }
        public long? DateS { get; set; }
        public string? BankName { get; set; }
        public string? Shobeh { get; set; }
    }

    public class TreasuryPrintSigner
    {
        public int Slot { get; set; }
        /// <summary>سمت — SIGN.SGN0x34TX (پیش‌فرض: تنظیم‌کننده / مدیر مالی / مدیر عامل).</summary>
        public string Title { get; set; } = "";
        /// <summary>نامِ حسابِ امضاکننده (GETHESNAME(GETUSERHES)).</summary>
        public string? Name { get; set; }
        public bool HasImage { get; set; }
    }

    public class TreasuryBalanceDto
    {
        public string Hes { get; set; } = "";
        public double Bed { get; set; }
        public double Bes { get; set; }
        public double Balance => Bed - Bes;
    }
}
