using System.Data;
using System.Globalization;
using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Treasury;
using Safir.Shared.Utility;

namespace Safir.Server.Treasury
{
    // ═══════════════════════════════════════════════════════════════════
    //  خزانه‌داری — پورتِ فرمِ PGET_HEDِ نرم‌افزار WPF
    //  (MrCorrect/Prg_UI/Wins/WinMenus/HESABDARI/PGET_HED.xaml.cs).
    //
    //  همان جدول‌ها و همان قواعد؛ چیزی «مدرن‌سازی» نشده چون WPF همچنان
    //  روی همین داده کار می‌کند:
    //
    //  • هر خزانه یک سند با NO_S = 5 دارد. PGET_HED.N_S کلیدِ خارجی به
    //    DEED_HED است، پس سندِ خالی باید قبل از سربرگ ساخته شود (مثل
    //    Createsanad در WPF) — هر دو در یک تراکنش.
    //  • بعد از هر تغییرِ سطر، سند با همان منطقِ GENSANADKHAZ از نو ساخته
    //    می‌شود: برای هر سطر «به حساب» بدهکار و «از حساب» بستانکار به مبلغ.
    //  • سندِ قطعی (GHATEI) یا خزانه‌ی امضاشده قابل تغییر نیست؛ خزانه‌ی
    //    کاربرِ دیگر فقط با DPSEE (Form_Current در WPF).
    //  • «اصلاح خزانه» قبل از باز کردنِ قفل، نسخه‌ی فعلی را در TR_PGET_HED و
    //    TR_PGET_LST نگه می‌دارد (CL_HESABDARI.TR).
    //
    //  چک‌ها (PAY_GETD / PAY_GETP) در TreasuryService.Cheques.cs؛ امضا، ارجاع،
    //  ضمیمه و چاپ در TreasuryService.Workflow.cs.
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class TreasuryService
    {
        public const string FormName = "PGET_HED";   // TFORMS
        public const string SeeAllForm = "DPDEED";   // «دیدن همه‌ی خزانه‌ها»
        public const string OwnDeptForm = "DEPEMAL"; // «فقط واحد کاربر»
        public const string EditOthersForm = "DPSEE"; // «اصلاحِ خزانه‌ی کاربرانِ دیگر»
        public const byte SanadKind = 5;             // DEED_HED.NO_S
        public const int Tg = 34;                    // TASKS.tg خزانه

        private readonly IDatabaseService _db;
        public TreasuryService(IDatabaseService db) => _db = db;

        // ─────────────────────────── دسترسی ───────────────────────────

        public sealed class UserPerms
        {
            public bool Run, See, Inp, Upd, Del, SeeAll, OwnDept, EditOthers;
        }

        private sealed class PermRow
        {
            public string? FORMNAME { get; set; }
            public bool? RUN { get; set; }
            public bool? SEE { get; set; }
            public bool? INP { get; set; }
            public bool? UPD { get; set; }
            public bool? DEL { get; set; }
        }

        public async Task<UserPerms> GetPermsAsync(int userCo)
        {
            var rows = (await _db.DoGetDataSQLAsync<PermRow>(@"
                SELECT f.FORMNAME, CAST(sc.RUN AS bit) RUN, CAST(sc.SEE AS bit) SEE, CAST(sc.INP AS bit) INP,
                       CAST(sc.UPD AS bit) UPD, CAST(sc.DEL AS bit) DEL
                FROM dbo.TFORMS f
                JOIN dbo.SAL_CHEK sc ON sc.OBJECT = f.IDH AND sc.USERCO = @userCo
                WHERE f.FORMNAME IN (@f, @all, @dept, @others)",
                new { userCo, f = FormName, all = SeeAllForm, dept = OwnDeptForm, others = EditOthersForm })).ToList();

            PermRow? Of(string n) => rows.FirstOrDefault(r => string.Equals(r.FORMNAME, n, StringComparison.OrdinalIgnoreCase));
            var main = Of(FormName);
            return new UserPerms
            {
                Run = main?.RUN == true,
                See = main?.RUN == true && main.SEE != false,
                Inp = main?.RUN == true && main.INP == true,
                Upd = main?.RUN == true && main.UPD == true,
                Del = main?.RUN == true && main.DEL == true,
                SeeAll = Of(SeeAllForm)?.RUN == true,
                OwnDept = Of(OwnDeptForm)?.RUN == true,
                EditOthers = Of(EditOthersForm)?.RUN == true
            };
        }

        /// <summary>
        /// کدام خزانه‌ها دیده می‌شوند — GetRestrictedSqlQueryForPGET_HED در WPF:
        /// DPDEED همه؛ وگرنه DEPEMAL فقط واحدِ کاربر؛ وگرنه فقط آن‌هایی که خودش ثبت کرده
        /// (با هر سه نگارشِ ی/ک، چون نام کاربری در داده‌ی قدیمی گاهی عربی است).
        ///
        /// ⚠ فیلترِ «زیرمجموعه‌ی چارت» (chartfilter) در WPF به UserOnChart وابسته است؛
        /// اینجا برای چنین کاربری سخت‌گیرانه‌تر رفتار می‌شود (فقط خزانه‌های خودش)،
        /// نه آزادتر.
        /// </summary>
        public static (string Sql, object Args) VisibilityFilter(UserPerms p, int userDept, string userName)
        {
            if (p.SeeAll) return ("1 = 1", new { });
            if (p.OwnDept && userDept > 0) return ("h.DEPATMAN = @VisDept", new { VisDept = userDept });
            var fa = userName.Replace('ي', 'ی').Replace('ك', 'ک');
            var ar = userName.Replace('ی', 'ي').Replace('ک', 'ك');
            return ("h.USER_NAME IN (@VisU1, @VisU2, @VisU3)", new { VisU1 = userName, VisU2 = fa, VisU3 = ar });
        }

        /// <summary>
        /// Form_Current در WPF: خزانه‌ای که کاربرِ دیگری ثبت کرده فقط با LETSGO("DPSEE")
        /// «اصلاح» می‌شود (مقایسه‌ی نام با Fixp — ی/ک یکسان).
        /// </summary>
        public static string? OthersBlock(TreasuryListItemDto h, UserPerms p, string userName)
        {
            if (p.EditOthers) return null;
            return string.Equals(NormalizeFa(h.UserName), NormalizeFa(userName), StringComparison.OrdinalIgnoreCase)
                ? null
                : "این خزانه را کاربرِ دیگری ثبت کرده و اجازه‌ی اصلاحِ خزانه‌ی دیگران (DPSEE) را ندارید.";
        }

        // ─────────────────────────── تنظیمات و فهرست‌ها ───────────────────────────

        internal sealed class SazmanRow
        {
            public double? SANDOGH { get; set; }
            public string? OPTIONSS { get; set; }
            public string? ADA { get; set; }
            public string? APA { get; set; }
            public string? ADV { get; set; }
            public string? APV { get; set; }
            public double? BANKHA { get; set; }
            public bool Sign { get; set; }
            public bool CtlDt { get; set; }
            public int? Yea { get; set; }

            public string AdaOr => (ADA ?? "").Trim();
            public string ApaOr => (APA ?? "").Trim();
            /// <summary>WPF: اگر ADV/APV خالی باشد عملاً همان ADA/APA است.</summary>
            public string AdvOr => string.IsNullOrWhiteSpace(ADV) ? AdaOr : ADV.Trim();
            public string ApvOr => string.IsNullOrWhiteSpace(APV) ? ApaOr : APV.Trim();
            public int? Bankha => BANKHA is > 0 ? (int)BANKHA.Value : null;
        }

        private SazmanRow? _sazman;

        internal async Task<SazmanRow> SazmanAsync()
            => _sazman ??= (await _db.DoGetDataSQLAsync<SazmanRow>(@"
                   SELECT TOP 1 SANDOGH, OPTIONSS, ADA, APA, ADV, APV, BANKHA,
                          CAST(ISNULL(SIGN, 0) AS bit) Sign, CAST(ISNULL(CTL_DT, 0) AS bit) CtlDt, CAST(YEA AS int) Yea
                   FROM dbo.SAZMAN")).FirstOrDefault() ?? new();

        /// <summary>
        /// IsMarkazPriceKhazanehEnabled در WPF: ستونِ «مرکز هزینه» فقط وقتی OPTIONSS[10]=5 و
        /// کدِ چاپِ شرکت در [11..12] یکی از کدهای خاص («19») است.
        /// </summary>
        public static bool MarkazEnabled(string? options)
            => options is { Length: >= 12 } && options[9] == '5' && options.Substring(10, 2) == "19";

        public static bool OptionOn(string? options, int oneBasedIndex)
            => options is not null && options.Length >= oneBasedIndex && options[oneBasedIndex - 1] == '5';

        private sealed class SignPermRow
        {
            public bool S1 { get; set; }
            public bool S2 { get; set; }
            public bool S3 { get; set; }
        }

        /// <summary>LetSigneTick(…, 34, …): ستون‌های SGN0134 / SGN0234 / SGN0334 ِ جدولِ SIGN.</summary>
        internal async Task<(bool S1, bool S2, bool S3)> SignPermsAsync(int userCo)
        {
            var r = (await _db.DoGetDataSQLAsync<SignPermRow>(
                "SELECT TOP 1 CAST(SGN0134 AS bit) S1, CAST(SGN0234 AS bit) S2, CAST(SGN0334 AS bit) S3 FROM dbo.SIGN WHERE USERCO = @userCo",
                new { userCo })).FirstOrDefault();
            return r is null ? (false, false, false) : (r.S1, r.S2, r.S3);
        }

        /// <param name="vahed">واحدِ جاری کاربر (VAHED_OF_USER — انتخابِ موقعِ ورود)</param>
        /// <param name="shift">شیفتِ جاری کاربر (SHIFT_OF_USER)</param>
        public async Task<TreasuryMetaDto> GetMetaAsync(UserPerms p, int userCo, int? vahed, int? shift, string userName)
        {
            var sazman = await SazmanAsync();
            var sign = await SignPermsAsync(userCo);
            var meta = new TreasuryMetaDto
            {
                CanSee = p.See,
                CanCreate = p.Inp,
                CanUpdate = p.Upd,
                CanDelete = p.Del,
                SeesAll = p.SeeAll,
                ArzActive = OptionOn(sazman.OPTIONSS, 14),
                MarkazEnabled = MarkazEnabled(sazman.OPTIONSS),
                CashKol = (int)(sazman.SANDOGH ?? 0),
                CashByDeptShift = OptionOn(sazman.OPTIONSS, 38),
                UserName = userName,
                UserId = userCo,
                CanSign1 = sign.S1,
                CanSign2 = sign.S2,
                CanSign3 = sign.S3,
                PrintNeedsSignature = sazman.Sign,
                Ada = sazman.AdaOr,
                Apa = sazman.ApaOr,
                Adv = sazman.AdvOr,
                Apv = sazman.ApvOr,
                Bankha = sazman.Bankha,
                DateControl = sazman.CtlDt,
                FiscalYear = sazman.Yea
            };

            meta.Operations = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                "SELECT CAST(CODE AS int) Id, CAST(NAMES AS nvarchar(100)) Name FROM dbo.TCOD_DPS ORDER BY CODE")).ToList();
            meta.Methods = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                "SELECT CAST(CODE AS int) Id, CAST(NAMES AS nvarchar(100)) Name FROM dbo.TCOD_DPSKIND ORDER BY CODE")).ToList();
            meta.Departments = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                // DEPART بیش از هزار شعبه‌ی مشتری هم دارد؛ واحدهایی که در خزانه به کار رفته‌اند اول می‌آیند.
                @"SELECT d.DEPATMAN Id, CAST(d.DEPNAME AS nvarchar(200)) Name,
                         CAST(CASE WHEN u.n IS NULL THEN 0 ELSE 1 END AS bit) Frequent
                  FROM dbo.DEPART d
                  LEFT JOIN (SELECT DEPATMAN, COUNT(*) n FROM dbo.PGET_HED GROUP BY DEPATMAN) u ON u.DEPATMAN = d.DEPATMAN
                  ORDER BY CASE WHEN u.n IS NULL THEN 1 ELSE 0 END, u.n DESC, d.DEPNAME")).ToList();
            meta.Shifts = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                "SELECT SHIFT_ID Id, CAST(SHNAME AS nvarchar(100)) Name FROM dbo.SHIFT ORDER BY SHNAME")).ToList();
            if (meta.MarkazEnabled)
            {
                meta.CostCenters = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                    "SELECT MHAZ_NO Id, CAST(MHAZNAME AS nvarchar(200)) Name FROM dbo.TCOD_MARKAZHAZ ORDER BY MHAZ_NO")).ToList();
            }
            foreach (var l in meta.Departments) l.Name = NormalizeFa(l.Name);

            // ارجاع — FillAllComboBoxes: کاربرانِ فعال (ENABL = 0) با ترتیبِ شخصیِ USER_PERSONEL_ORDER
            var people = await _db.DoGetDataSQLAsync<(int Idd, string? Name)>(@"
                SELECT sd.IDD, sd.SAL_NAME
                FROM dbo.SALA_DTL sd
                LEFT JOIN dbo.USER_PERSONEL_ORDER uo ON sd.IDD = uo.PERSONEL_ID AND uo.USER_ID = @userCo
                WHERE sd.ENABL = 0
                ORDER BY CASE WHEN uo.SORT_ORDER IS NULL THEN 1 ELSE 0 END, uo.SORT_ORDER, sd.SAL_NAME", new { userCo });
            meta.Personnel = people.Select(x => new TreasuryLookupItem { Id = x.Idd, Name = DecodeUser(x.Name) }).ToList();

            // چک
            meta.Banks = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                "SELECT CODE Id, CAST(NAMES AS nvarchar(200)) Name FROM dbo.TCOD_BANKS ORDER BY NAMES")).ToList();
            foreach (var b in meta.Banks) b.Name = NormalizeFa(b.Name);
            var ada = SplitHes(sazman.AdaOr);
            if (ada is not null)
            {
                meta.ChequeFunds = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                    "SELECT TNUMBER Id, CAST(NAME AS nvarchar(200)) Name FROM dbo.TDETA_HES WHERE N_KOL = @k AND NUMBER = @m ORDER BY TNUMBER",
                    new { k = ada[0], m = ada[1] })).ToList();
            }
            if (sazman.Bankha is { } bk)
            {
                meta.BankAccounts = (await _db.DoGetDataSQLAsync<TreasuryAccountDto>(@"
                    SELECT CAST(N_KOL AS nvarchar(10)) + '-' + CAST(NUMBER AS nvarchar(10)) + '-' + CAST(TNUMBER AS nvarchar(10)) Hes,
                           CAST(NAME AS nvarchar(300)) Name
                    FROM dbo.TDETA_HES WHERE N_KOL = @bk ORDER BY NUMBER, TNUMBER", new { bk })).ToList();
            }

            // مثل WPF (DEPATMAN.SelectedValue = VAHED_OF_USER): واحد و شیفتی که کاربر موقعِ ورود
            // انتخاب کرده؛ اگر در فهرست نباشد خالی می‌ماند تا کاربر خودش انتخاب کند.
            meta.DefaultDepatman = meta.Departments.Any(d => d.Id == vahed) ? vahed : null;
            meta.DefaultShift = meta.Shifts.Any(x => x.Id == shift) ? shift : null;
            return meta;
        }

        public static string NormalizeFa(string? s) => (s ?? "").Replace('ي', 'ی').Replace('ك', 'ک').Trim();

        /// <summary>نامِ کاربری در SALA_DTL کدشده است (DECODEUN) — بخش ۲ ِ AGENTS.md.</summary>
        internal static string DecodeUser(string? encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return "";
            try { return NormalizeFa(CL_METHODS.DECODEUN(encoded)); } catch { return encoded; }
        }

        // ─────────────────────────── فهرست و جزئیات ───────────────────────────

        private const string ListSelect = @"
            SELECT h.ID Id, h.DATE Date, h.MOLAH Molah, h.N_S Ns, h.DEPATMAN Depatman,
                   CAST(d.DEPNAME AS nvarchar(200)) DepName, h.SHIFT Shift, h.USER_NAME UserName,
                   ISNULL(h.KIND, 0) Kind, h.IDK Idk, ISNULL(h.OKF, 0) Okf,
                   ISNULL(h.SGN1, 0) Sgn1, ISNULL(h.SGN2, 0) Sgn2, ISNULL(h.SGN3, 0) Sgn3,
                   h.sgn1usid Sgn1User, h.sgn2usid Sgn2User, h.sgn3usid Sgn3User,
                   CAST(ISNULL(dh.GHATEI, 0) AS bit) Final,
                   ISNULL(s.Cnt, 0) [RowCount], ISNULL(s.Rec, 0) SumReceipt, ISNULL(s.Pay, 0) SumPayment, ISNULL(s.Chq, 0) ChequeRows
            FROM dbo.PGET_HED h
            LEFT JOIN dbo.DEPART d ON d.DEPATMAN = h.DEPATMAN
            LEFT JOIN dbo.DEED_HED dh ON dh.N_S = h.N_S
            OUTER APPLY (SELECT COUNT(*) Cnt,
                                SUM(CASE WHEN l.NO_AM = 1 THEN l.MABL ELSE 0 END) Rec,
                                SUM(CASE WHEN l.NO_AM = 2 THEN l.MABL ELSE 0 END) Pay,
                                SUM(CASE WHEN l.NAHVA IN (2,4,5,6) THEN 1 ELSE 0 END) Chq
                         FROM dbo.PGET_LST l WHERE l.ID = h.ID) s";

        public async Task<List<TreasuryListItemDto>> ListAsync(UserPerms p, int userDept, string userName, long? from, long? to)
        {
            var (vis, visArgs) = VisibilityFilter(p, userDept, userName);
            var args = new DynamicParameters(visArgs);
            var where = $"WHERE {vis}";
            if (from is > 0) { where += " AND h.DATE >= @From"; args.Add("From", from); }
            if (to is > 0) { where += " AND h.DATE <= @To"; args.Add("To", to); }
            var list = (await _db.DoGetDataSQLAsync<TreasuryListItemDto>($"{ListSelect} {where} ORDER BY h.DATE DESC, h.ID DESC", args)).ToList();
            foreach (var x in list) x.DepName = NormalizeFa(x.DepName);
            return list;
        }

        internal async Task<TreasuryListItemDto?> HeaderAsync(int id, UserPerms p, int userDept, string userName)
        {
            var (vis, visArgs) = VisibilityFilter(p, userDept, userName);
            var args = new DynamicParameters(visArgs);
            args.Add("Id", id);
            var h = (await _db.DoGetDataSQLAsync<TreasuryListItemDto>($"{ListSelect} WHERE h.ID = @Id AND {vis}", args)).FirstOrDefault();
            if (h is not null) h.DepName = NormalizeFa(h.DepName);
            return h;
        }

        public async Task<TreasuryDetailDto?> GetAsync(int id, UserPerms p, int userDept, string userName)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return null;

            var rows = (await _db.DoGetDataSQLAsync<TreasuryRowDto>(@"
                SELECT p.IDH Idh, p.ID Id, p.RADIF Radif, p.NO_AM NoAm, CAST(p.NAHVA AS int) Nahva,
                       p.FHES Fhes, CAST(cf.NAME AS nvarchar(300)) FhesName,
                       p.THES Thes, CAST(ct.NAME AS nvarchar(300)) ThesName,
                       p.SHARH Sharh, p.MABL Mabl, p.N_SERI NSeri, p.BANK Bank, CAST(b.NAMES AS nvarchar(200)) BankName,
                       p.MHAZ_NO MhazNo, p.ARZD Arzd, p.ARZKIND2 ArzKind2,
                       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.TASKS t JOIN dbo.EVENTS e ON e.IDNUM = t.IDNUM
                                              WHERE t.tg = 34 AND t.num = p.IDH AND e.pic IS NOT NULL) THEN 1 ELSE 0 END AS bit) HasAttachment
                FROM dbo.PGET_LST p
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = p.FHES) cf
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = p.THES) ct
                LEFT JOIN dbo.TCOD_BANKS b ON b.CODE = p.BANK
                WHERE p.ID = @id
                ORDER BY p.IDH", new { id })).ToList();

            foreach (var r in rows.Where(r => TreasuryMethod.IsCheque(r.Nahva) && r.NSeri is not null && r.Bank is not null))
                r.Cheque = await RowChequeAsync(r);

            var sanadBase = h.Ns is null ? null
                : (await _db.DoGetDataSQLAsync<int?>("SELECT TOP 1 base FROM dbo.DEED_HED WHERE N_S = @ns", new { ns = h.Ns })).FirstOrDefault();

            var signers = new[] { h.Sgn1User, h.Sgn2User, h.Sgn3User }.Where(x => x is > 0).Distinct().ToList();
            var names = signers.Count == 0 ? new Dictionary<int, string>()
                : (await _db.DoGetDataSQLAsync<(int Idd, string? Name)>("SELECT IDD, SAL_NAME FROM dbo.SALA_DTL WHERE IDD IN @signers", new { signers }))
                    .ToDictionary(x => x.Idd, x => DecodeUser(x.Name));
            string? NameOf(int? u) => u is > 0 && names.TryGetValue(u.Value, out var n) ? n : null;

            return new TreasuryDetailDto
            {
                Header = h,
                Rows = rows,
                SanadBase = sanadBase,
                LockReason = LockReason(h),
                UnlockBlock = OthersBlock(h, p, userName),
                Sgn1Name = h.Sgn1 ? NameOf(h.Sgn1User) : null,
                Sgn2Name = h.Sgn2 ? NameOf(h.Sgn2User) : null,
                Sgn3Name = h.Sgn3 ? NameOf(h.Sgn3User) : null,
                Task = await TaskAsync(id)
            };
        }

        /// <summary>
        /// چرا این خزانه قابل تغییر نیست — مثل Form_Current و ESLAH_Click در WPF.
        /// «تأیید خزانه» (OKF) به‌تنهایی قفل نیست؛ فقط یعنی قبل از ویرایش باید «اصلاح» زد.
        /// </summary>
        public static string? LockReason(TreasuryListItemDto h)
        {
            if (h.Final) return "سند حسابداری این خزانه قطعی شده و دیگر قابل تغییر نیست.";
            if (h.Sgn1 || h.Sgn2 || h.Sgn3) return "این خزانه امضا شده است؛ برای اصلاح، اول امضاها را بردارید.";
            return null;
        }

        /// <summary>قفل + محدودیتِ خزانه‌ی دیگران — هر تغییری از این رد می‌شود.</summary>
        private static string? EditBlock(TreasuryListItemDto h, UserPerms p, string userName)
            => LockReason(h) ?? OthersBlock(h, p, userName);

        // ─────────────────────────── حساب‌ها ───────────────────────────

        public async Task<List<TreasuryAccountDto>> SearchAccountsAsync(string? q)
        {
            q = NormalizeFa(q);
            if (q.Length == 0)
                return (await _db.DoGetDataSQLAsync<TreasuryAccountDto>(
                    "SELECT TOP 30 hes Hes, CAST(NAME AS nvarchar(300)) Name FROM dbo.CUST_HESAB ORDER BY hes")).ToList();

            var ar = q.Replace('ی', 'ي').Replace('ک', 'ك');
            return (await _db.DoGetDataSQLAsync<TreasuryAccountDto>(@"
                SELECT TOP 40 hes Hes, CAST(NAME AS nvarchar(300)) Name
                FROM dbo.CUST_HESAB
                WHERE hes LIKE @p + '%' OR NAME LIKE N'%' + @q + N'%' OR NAME LIKE N'%' + @ar + N'%'
                ORDER BY CASE WHEN hes LIKE @p + '%' THEN 0 ELSE 1 END, LEN(hes), hes",
                new { p = q, q, ar })).ToList();
        }

        public async Task<List<TreasuryBalanceDto>> BalancesAsync(IEnumerable<string> hes)
        {
            var list = hes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().Take(20).ToList();
            if (list.Count == 0) return new();
            return (await _db.DoGetDataSQLAsync<TreasuryBalanceDto>(@"
                SELECT HES Hes, ISNULL(SUM(BED), 0) Bed, ISNULL(SUM(BES), 0) Bes
                FROM dbo.DEED_DTL WHERE HES IN @list GROUP BY HES", new { list })).ToList();
        }

        /// <summary>کد حساب «کل-معین-تفصیلی[-ت۲[-ت۳[-ت۴]]]» → اجزا. null اگر شکلش درست نیست.</summary>
        public static int[]? SplitHes(string? hes)
        {
            var parts = (hes ?? "").Trim().Split('-');
            if (parts.Length is < 3 or > 6) return null;
            var nums = new int[6];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i])) return null;
            for (int i = parts.Length; i < 6; i++) nums[i] = int.MinValue; // سطحِ نبوده
            return nums;
        }

        /// <summary>
        /// حسابِ صندوق برای سطرِ «نقد» — NO_AM_AfterUpdate در WPF:
        /// کل = SAZMAN.SANDOGH؛ اگر OPTIONSS[38]=5 و واحد و شیفت داده شده، معین = واحد و تفصیلی = شیفت؛
        /// وگرنه اولین معین و اولین تفصیلیِ آن کل (FIRSTM / FIRSTT).
        /// </summary>
        public async Task<string?> CashAccountAsync(int? depatman, int? shift)
        {
            var sazman = await SazmanAsync();
            if (sazman.SANDOGH is not > 0) return null;
            var kol = (int)sazman.SANDOGH.Value;
            if (OptionOn(sazman.OPTIONSS, 38) && depatman is not null && shift is not null)
                return $"{kol}-{depatman}-{shift}";
            return await FirstTafAsync(kol);
        }

        /// <summary>FIRSTM / FIRSTT: اولین معین و اولین تفصیلیِ یک کل.</summary>
        internal async Task<string?> FirstTafAsync(int kol)
        {
            var moin = (await _db.DoGetDataSQLAsync<int?>("SELECT MIN(NUMBER) FROM dbo.DETA_HES WHERE N_KOL = @kol", new { kol })).FirstOrDefault();
            if (moin is null) return null;
            var taf = (await _db.DoGetDataSQLAsync<int?>("SELECT MIN(TNUMBER) FROM dbo.TDETA_HES WHERE N_KOL = @kol AND NUMBER = @moin", new { kol, moin })).FirstOrDefault();
            return taf is null ? null : $"{kol}-{moin}-{taf}";
        }

        // ─────────────────────────── سربرگ ───────────────────────────

        public static string SanadSharh(int id, long date) => $"خزانه داري شماره {id} مورخ {FormatDate(date)}";

        public static string FormatDate(long date)
        {
            var d = date.ToString(CultureInfo.InvariantCulture);
            return d.Length == 8 ? $"{d[..4]}/{d.Substring(4, 2)}/{d.Substring(6, 2)}" : d;
        }

        private async Task<string?> ValidateDeptShiftAsync(TreasuryHeaderSaveRequest req)
        {
            if (req.Depatman is not null &&
                await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 1 FROM dbo.DEPART WHERE DEPATMAN = @d", new { d = req.Depatman }) is null)
                return "واحدِ انتخاب‌شده معتبر نیست.";
            if (req.Shift is not null &&
                await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 1 FROM dbo.SHIFT WHERE SHIFT_ID = @s", new { s = req.Shift }) is null)
                return "شیفتِ انتخاب‌شده معتبر نیست.";
            return null;
        }

        /// <summary>
        /// DATE_BeforeUpdate → CHEKDATEM: تاریخِ معتبر، و با SAZMAN.CTL_DT در سالِ مالیِ جاری
        /// (Tarikh.IsSyncedDateNow).
        /// </summary>
        internal async Task<string?> FiscalDateError(long date, string what = "تاریخ")
        {
            if (!ValidDate(date)) return $"{what} صحیح نیست.";
            var s = await SazmanAsync();
            if (s.CtlDt && s.Yea is > 0 && date / 10000 != s.Yea) return $"{what} مربوط به سال مالیِ جاری ({s.Yea}) نیست.";
            return null;
        }

        public async Task<TreasurySaveResult> CreateAsync(TreasuryHeaderSaveRequest req, string userName, int userCo)
        {
            if (await FiscalDateError(req.Date) is { } de) return Fail(de);
            if (await ValidateDeptShiftAsync(req) is { } ds) return Fail(ds);
            if (req.Kind is not (0 or 2 or 3)) return Fail("نوع برگه معتبر نیست.");
            if ((req.Molah?.Length ?? 0) > 60) return Fail("ملاحظات بیش از ۶۰ نویسه است.");

            try
            {
                var (id, ns) = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    // شماره‌ی خزانه — مثل WPF با قفلِ جدول
                    var maxId = await conn.ExecuteScalarAsync<int?>("SELECT MAX(ID) FROM dbo.PGET_HED WITH (TABLOCKX, HOLDLOCK)", transaction: tx);
                    var newId = (maxId ?? 0) + 1;
                    var maxIdk = await conn.ExecuteScalarAsync<int?>("SELECT MAX(IDK) FROM dbo.PGET_HED WHERE KIND = @k", new { k = req.Kind }, tx);
                    var idk = (maxIdk ?? 0) + 1;

                    // سندِ خالی — PGET_HED.N_S به DEED_HED کلیدِ خارجی دارد
                    var newNs = await ReserveSanadAsync(conn, tx, req.Date, SanadSharh(newId, req.Date), userName);

                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.PGET_HED (ID, DATE, MOLAH, DEPATMAN, SHIFT, USER_NAME, KIND, OKF, IDK, UID, N_S, SGN1, SGN2, SGN3, CRT)
                        VALUES (@ID, @DATE, @MOLAH, @DEPATMAN, @SHIFT, @USER_NAME, @KIND, 1, @IDK, @UID, @N_S, 0, 0, 0, GETDATE())",
                        new
                        {
                            ID = newId, DATE = req.Date, MOLAH = req.Molah?.Trim(), DEPATMAN = req.Depatman, SHIFT = req.Shift,
                            USER_NAME = userName, KIND = req.Kind, IDK = idk, UID = userCo, N_S = newNs
                        }, tx);
                    return (newId, newNs);
                }, IsolationLevel.Serializable);

                return new TreasurySaveResult { Ok = true, Id = id, Ns = ns };
            }
            catch (Exception ex) when (IsDuplicate(ex))
            {
                return Fail("شماره‌ی خزانه همزمان توسط کاربر دیگری گرفته شد؛ دوباره ذخیره کنید.");
            }
        }

        public async Task<TreasurySaveResult> UpdateHeaderAsync(int id, TreasuryHeaderSaveRequest req, UserPerms p, int userDept, string userName)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            if (EditBlock(h, p, userName) is { } lr) return Fail(lr);
            if (await FiscalDateError(req.Date) is { } de) return Fail(de);
            if (await ValidateDeptShiftAsync(req) is { } ds) return Fail(ds);
            if (req.Kind is not (0 or 2 or 3)) return Fail("نوع برگه معتبر نیست.");
            if ((req.Molah?.Length ?? 0) > 60) return Fail("ملاحظات بیش از ۶۰ نویسه است.");

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                // DATE در PGET_LST با کلیدِ خارجیِ ON UPDATE CASCADE خودش عوض می‌شود.
                await conn.ExecuteAsync(@"
                    UPDATE dbo.PGET_HED SET DATE = @DATE, MOLAH = @MOLAH, DEPATMAN = @DEPATMAN, SHIFT = @SHIFT,
                           KIND = @KIND, OKF = 1
                    WHERE ID = @ID",
                    new { ID = id, DATE = req.Date, MOLAH = req.Molah?.Trim(), DEPATMAN = req.Depatman, SHIFT = req.Shift, KIND = req.Kind }, tx);
                await RebuildSanadAsync(conn, tx, id);
                return 0;
            });
            return new TreasurySaveResult { Ok = true, Id = id };
        }

        /// <summary>
        /// «اصلاح خزانه» — ESLAH_Click: نسخه‌ی فعلی در TR_PGET_HED و TR_PGET_LST (CL_HESABDARI.TR)
        /// و بعد صفحه اجازه‌ی ویرایش می‌دهد. خزانه‌ی امضاشده («اول امضاء را برداريد»)، سندِ قطعی
        /// یا خزانه‌ی کاربرِ دیگر بدونِ DPSEE باز نمی‌شود.
        /// </summary>
        public async Task<TreasurySaveResult> UnlockAsync(int id, UserPerms p, int userDept, string userName, string? clientIp)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            if (EditBlock(h, p, userName) is { } lr) return Fail(lr);

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                await HistoryAsync(conn, tx, id, userName, clientIp);
                return 0;
            });
            return new TreasurySaveResult { Ok = true, Id = id };
        }

        /// <summary>نسخه‌ی فعلیِ سربرگ و سطرها در TR_PGET_HED / TR_PGET_LST.</summary>
        private static async Task HistoryAsync(IDbConnection conn, IDbTransaction tx, int id, string userName, string? clientIp)
        {
            await CopyToHistoryAsync(conn, tx, "PGET_HED", "ID = @id", new { id }, true, userName, clientIp);
            await CopyToHistoryAsync(conn, tx, "PGET_LST", "ID = @id", new { id }, false, userName, clientIp);
        }

        /// <summary>
        /// CL_HESABDARI.TR: ستون‌های مشترکِ جدول و نسخه‌ی TR_ (GETfldlist) + زمانِ تغییر.
        /// flag 1 (سربرگ/چک) کاربر و دستگاه را هم دارد؛ flag 2 (سطر) فقط زمان را.
        /// </summary>
        internal static async Task CopyToHistoryAsync(IDbConnection conn, IDbTransaction tx, string table, string where, object args,
                                                      bool withUser, string userName, string? clientIp)
        {
            var cols = (await conn.QueryAsync<string>(@"
                SELECT c.name FROM sys.columns c
                WHERE c.object_id = OBJECT_ID('dbo.' + @table) AND c.is_computed = 0
                  AND EXISTS (SELECT 1 FROM sys.columns t WHERE t.object_id = OBJECT_ID('dbo.TR_' + @table)
                                                         AND t.name = c.name AND t.is_identity = 0 AND t.is_computed = 0)",
                new { table }, tx)).ToList();
            if (cols.Count == 0) return; // جدولِ TR هنوز ساخته نشده — WPF اولین بار می‌سازدش

            var now = DateTime.Now;
            var pc = new PersianCalendar();
            long faDate = pc.GetYear(now) * 10000L + pc.GetMonth(now) * 100 + pc.GetDayOfMonth(now);
            var fl = string.Join(",", cols.Select(c => $"[{c}]"));
            var extra = withUser ? ", UP_DATE, UP_TIME, UP_USER_NAME, PC_NAME, IPADD" : ", UP_TIME, UP_DATE";
            var vals = withUser ? ", @TrD, @TrT, @TrU, @TrPc, @TrIp" : ", @TrT, @TrD";
            var p = new DynamicParameters(args);
            p.Add("TrD", faDate);
            p.Add("TrT", now.ToOADate());
            p.Add("TrU", userName);
            p.Add("TrPc", "Safir");
            p.Add("TrIp", clientIp ?? "");
            await conn.ExecuteAsync($"INSERT INTO dbo.TR_{table} ({fl}{extra}) SELECT {fl}{vals} FROM dbo.{table} WHERE {where}", p, tx);
        }

        public async Task<TreasurySaveResult> DeleteAsync(int id, UserPerms p, int userDept, string userName)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            if (EditBlock(h, p, userName) is { } lr) return Fail(lr);
            if (h.RowCount > 0) return Fail("این خزانه‌داری سطر دارد؛ ابتدا سطرها را حذف کنید، سپس خزانه را.");

            // مثل WPF فقط سربرگ حذف می‌شود؛ سندِ خالی‌اش را گامِ «حذف اسناد خالی» جمع می‌کند.
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM dbo.PGET_HED WHERE ID = @id", new { id });
            }
            catch (Exception ex) when (ex is System.Data.SqlClient.SqlException { Number: 547 } || ex.InnerException is System.Data.SqlClient.SqlException { Number: 547 })
            {
                return Fail("این خزانه دارای اطلاعات وابسته است، ابتدا آن را حذف کنید.");
            }
            return new TreasurySaveResult { Ok = true, Id = id };
        }

        // ─────────────────────────── سند ───────────────────────────

        /// <summary>
        /// بازسازیِ سندِ یک خزانه — GENSANADKHAZ (AUTO_BAZ) برای یک ID:
        /// اگر سند با NO_S = 5 هست و مالکش همین خزانه است، سربرگ به‌روز و ردیف‌ها بازنویسی
        /// می‌شوند (BAYEG و base دست نمی‌خورند)؛ وگرنه شماره‌ی تازه رزرو و روی خزانه ثبت می‌شود.
        /// </summary>
        public static async Task RebuildSanadAsync(IDbConnection conn, IDbTransaction tx, int id)
        {
            var h = (await conn.QueryAsync<(double? Ns, long Date, string? UserName)>(
                "SELECT N_S, DATE, USER_NAME FROM dbo.PGET_HED WHERE ID = @id", new { id }, tx)).FirstOrDefault();

            var ownsHeader = h.Ns is not null && await conn.ExecuteScalarAsync<int>(@"
                SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.DEED_HED WHERE NO_S = 5 AND N_S = @ns)
                             AND NOT EXISTS (SELECT 1 FROM dbo.PGET_HED WHERE N_S = @ns AND ID < @id) THEN 1 ELSE 0 END",
                new { ns = h.Ns, id }, tx) == 1;

            var final = h.Ns is not null && await conn.ExecuteScalarAsync<bool?>(
                "SELECT GHATEI FROM dbo.DEED_HED WHERE N_S = @ns", new { ns = h.Ns }, tx) == true;
            if (final) throw new InvalidOperationException("سند این خزانه قطعی است.");

            var sharh = SanadSharh(id, h.Date);
            double ns;
            if (ownsHeader)
            {
                ns = h.Ns!.Value;
                await conn.ExecuteAsync(@"
                    UPDATE dbo.DEED_HED SET DATE_S = @d, SHARH_S = @s, USER_NAME = @u, OKF = 1 WHERE NO_S = 5 AND N_S = @ns;
                    DELETE FROM dbo.DEED_DTL WHERE N_S = @ns;",
                    new { d = h.Date, s = sharh, u = h.UserName ?? "", ns }, tx);
            }
            else
            {
                ns = await ReserveSanadAsync(conn, tx, h.Date, sharh, h.UserName ?? "");
                await conn.ExecuteAsync("UPDATE dbo.PGET_HED SET N_S = @ns WHERE ID = @id", new { ns, id }, tx);
            }

            var arz = await conn.ExecuteScalarAsync<int>(@"
                SELECT CASE WHEN COL_LENGTH('dbo.DEED_DTL','ARZD') IS NOT NULL AND COL_LENGTH('dbo.PGET_LST','ARZD') IS NOT NULL THEN 1 ELSE 0 END
                     + CASE WHEN COL_LENGTH('dbo.DEED_DTL','ARZKIND2') IS NOT NULL AND COL_LENGTH('dbo.PGET_LST','ARZKIND2') IS NOT NULL THEN 2 ELSE 0 END", transaction: tx);
            var arzCols = ((arz & 1) != 0 ? ", ARZD" : "") + ((arz & 2) != 0 ? ", ARZKIND2" : "");

            await conn.ExecuteAsync(
                $"INSERT INTO dbo.DEED_DTL (HES_K, HES_M, HES_T, HES_T2, HES_T3, HES_T4, SHARH, BED, N_SERI, BANK, N_S, HES, MHAZ_NO{arzCols}) " +
                $"SELECT THES_K, THES_M, THES_T, THES_T2, THES_T3, THES_T4, SHARH, MABL, N_SERI, BANK, @ns, THES, MHAZ_NO{arzCols} FROM dbo.PGET_LST WHERE ID = @id;" +
                $"INSERT INTO dbo.DEED_DTL (HES_K, HES_M, HES_T, HES_T2, HES_T3, HES_T4, SHARH, BES, N_SERI, BANK, N_S, HES, MHAZ_NO{arzCols}) " +
                $"SELECT FHES_K, FHES_M, FHES_T, FHES_T2, FHES_T3, FHES_T4, SHARH, MABL, N_SERI, BANK, @ns, FHES, MHAZ_NO{arzCols} FROM dbo.PGET_LST WHERE ID = @id;",
                new { ns, id }, tx);
        }

        /// <summary>
        /// یک شماره سند (DEED_HED) — همان قفلِ نام‌گذاری‌شده‌ی SanadNumbering و Pay2RunController،
        /// داخلِ تراکنشِ فراخوان تا سند و خزانه با هم ثبت یا با هم برگردانده شوند.
        /// </summary>
        public static async Task<double> ReserveSanadAsync(IDbConnection conn, IDbTransaction tx, long date, string sharh, string userName)
        {
            await conn.ExecuteAsync(
                "EXEC sp_getapplock @Resource = 'DeedNumberAllocation', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
                transaction: tx);
            var maxNs = await conn.ExecuteScalarAsync<double?>("SELECT MAX(N_S) FROM dbo.DEED_HED WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
            var maxBg = await conn.ExecuteScalarAsync<double?>("SELECT MAX(BAYEG) FROM dbo.DEED_HED WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
            var ns = (maxNs ?? 0) + 1;
            var bg = maxBg.HasValue ? maxBg.Value + 1 : 100000000;
            await conn.ExecuteAsync(@"
                INSERT INTO dbo.DEED_HED (N_S, DATE_S, SHARH_S, GHATEI, NO_S, OKF, USER_NAME, CRT, uid, BAYEG)
                VALUES (@ns, @date, @sharh, 0, 5, 1, @user, GETDATE(), NULL, @bg)",
                new { ns, date, sharh, user = userName, bg }, tx);
            return ns;
        }

        // ─────────────────────────── کمکی ───────────────────────────

        public static bool ValidDate(long d)
        {
            if (d < 13000101 || d > 15001231) return false;
            int y = (int)(d / 10000), m = (int)(d / 100 % 100), day = (int)(d % 100);
            if (m is < 1 or > 12 || day < 1) return false;
            try { return day <= new PersianCalendar().GetDaysInMonth(y, m); } catch { return false; }
        }

        /// <summary>FARSIDATE: امروز به شمسی (yyyymmdd).</summary>
        internal static long Today()
        {
            var now = DateTime.Now;
            var pc = new PersianCalendar();
            return pc.GetYear(now) * 10000L + pc.GetMonth(now) * 100 + pc.GetDayOfMonth(now);
        }

        internal static int NowTime() => DateTime.Now.Hour * 100 + DateTime.Now.Minute;

        private static bool IsDuplicate(Exception ex)
            => ex is System.Data.SqlClient.SqlException { Number: 2627 or 2601 }
               || ex.InnerException is System.Data.SqlClient.SqlException { Number: 2627 or 2601 };

        private static TreasurySaveResult Fail(string m) => new() { Ok = false, Error = m };
    }
}
