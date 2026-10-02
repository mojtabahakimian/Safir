using System.Data;
using Dapper;
using Safir.Server.Treasury;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Sanad;
using Safir.Shared.Models.Treasury;
using ChequeRole = Safir.Shared.Models.Sanad.SanadChequeRole;

namespace Safir.Server.Sanad
{
    // ═══════════════════════════════════════════════════════════════════
    //  صدور و ویرایشِ اسنادِ حسابداری — پورتِ فرمِ DEED_HEADِ نرم‌افزار WPF
    //  (MrCorrect/Prg_UI/Wins/WinMenus/HESABDARI/DEED_HEAD.xaml.cs).
    //
    //  همان جدول‌ها و همان قواعد (WPF همچنان روی همین داده کار می‌کند):
    //  • سندِ دستی NO_S = 0 دارد؛ سندهای خودکار (خزانه، فاکتور، انبار، …) فقط
    //    دیده می‌شوند — ESLAH_Click: «سند اتوماتیک است و قابل اصلاح نیست».
    //  • سندِ قطعی (GHATEI) یا امضاشده قفل است؛ «اصلاح سند» قبل از باز کردن،
    //    نسخه‌ی فعلی را در TR_DEED_HED / TR_DEED_DTL نگه می‌دارد.
    //  • هر ردیف جدا ذخیره می‌شود (Child14_RowEditEnding → CmdSaveRecord) با همان
    //    اعتبارسنجی‌ها؛ ردیفِ بدهکارِ اسناد دریافتنی / بستانکارِ اسناد پرداختنی
    //    چکش را در PAY_GETD / PAY_GETP می‌نویسد (SGETCHEK / SPAYCHEK).
    //
    //  تفاوت‌های عمدی با WPF (همه به‌خاطرِ درستیِ داده):
    //  • شماره سند با همان قفلِ DeedNumberAllocation ِ خزانه و حقوق؛ WPF MAX+1
    //    بی‌قفل می‌گرفت و دو ذخیره‌ی هم‌زمان خطای کلید می‌داد.
    //  • هر ذخیره (سطر + چک) یک تراکنش است؛ حذفِ سطر یا سند، چکش را آزاد می‌کند
    //    (WPF چک را «نزد صندوق» جا می‌گذاشت).
    //  • KIND ِ چکِ دریافتی از ADA/ADV درست تعیین می‌شود (WPF شماره‌ی تفصیلی را
    //    با کدِ ADA مقایسه می‌کرد و همیشه صفر می‌نوشت).
    //  • مرکز هزینه (MHAZ_NO) واقعاً ذخیره می‌شود؛ WPF ستونش را پنهان و بی‌اثر داشت.
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class SanadService
    {
        public const string FormName = "DEED_HEAD";       // TFORMS — منوی SANAD
        public const string FinalizeForm = "F_MENU_ASNAD"; // «تایید و قطعی کردن اسناد» (WANTEDFORM("TAEED"))
        public const int Tg = 0;                           // TASKS.tg ِ سند حسابداری

        private readonly IDatabaseService _db;
        private readonly TreasuryService _trs;

        public SanadService(IDatabaseService db)
        {
            _db = db;
            _trs = new TreasuryService(db);
        }

        // ─────────────────────────── دسترسی ───────────────────────────

        public sealed class UserPerms
        {
            public bool Run, See, Inp, Upd, Del, Finalize;
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

        /// <summary>
        /// SETSECURITY("SANAD") روی SAL_CHEK ِ فرمِ DEED_HEAD. قطعی‌کردن (برگشت‌ناپذیر) اینجا
        /// علاوه بر باز کردنِ فرمِ F_MENU_ASNAD «بهنگام‌سازی» هم می‌خواهد.
        /// </summary>
        public async Task<UserPerms> GetPermsAsync(int userCo)
        {
            var rows = (await _db.DoGetDataSQLAsync<PermRow>(@"
                SELECT f.FORMNAME, CAST(sc.RUN AS bit) RUN, CAST(sc.SEE AS bit) SEE, CAST(sc.INP AS bit) INP,
                       CAST(sc.UPD AS bit) UPD, CAST(sc.DEL AS bit) DEL
                FROM dbo.TFORMS f
                JOIN dbo.SAL_CHEK sc ON sc.OBJECT = f.IDH AND sc.USERCO = @userCo
                WHERE f.FORMNAME IN (@f, @fin)",
                new { userCo, f = FormName, fin = FinalizeForm })).ToList();

            PermRow? Of(string n) => rows.FirstOrDefault(r => string.Equals(r.FORMNAME, n, StringComparison.OrdinalIgnoreCase));
            var main = Of(FormName);
            var fin = Of(FinalizeForm);
            return new UserPerms
            {
                Run = main?.RUN == true,
                See = main?.RUN == true && main.SEE != false,
                Inp = main?.RUN == true && main.INP == true,
                Upd = main?.RUN == true && main.UPD == true,
                Del = main?.RUN == true && main.DEL == true,
                Finalize = fin?.RUN == true && fin.SEE != false && fin.UPD == true
            };
        }

        private sealed class SignPermRow
        {
            public bool S1 { get; set; }
            public bool S2 { get; set; }
            public bool S3 { get; set; }
        }

        /// <summary>LetSigneTick(…, 0, …): ستون‌های SND_TAHI / SND_MALI / SND_MODIR ِ جدولِ SIGN.</summary>
        internal async Task<(bool S1, bool S2, bool S3)> SignPermsAsync(int userCo)
        {
            var r = (await _db.DoGetDataSQLAsync<SignPermRow>(
                "SELECT TOP 1 CAST(ISNULL(SND_TAHI,0) AS bit) S1, CAST(ISNULL(SND_MALI,0) AS bit) S2, CAST(ISNULL(SND_MODIR,0) AS bit) S3 FROM dbo.SIGN WHERE USERCO = @userCo",
                new { userCo })).FirstOrDefault();
            return r is null ? (false, false, false) : (r.S1, r.S2, r.S3);
        }

        public static readonly string[] SlotTitles = { "تنظیم کننده", "مدیر مالی", "مدیر عامل" };

        public async Task<SanadMetaDto> GetMetaAsync(UserPerms p, int userCo, string userName)
        {
            var s = await _trs.SazmanAsync();
            var sign = await SignPermsAsync(userCo);
            // فهرستِ ارجاع، بانک‌ها، موقعیت و حساب‌های بانکیِ چک همان‌هایی‌اند که خزانه می‌سازد
            var tm = await _trs.GetMetaAsync(new TreasuryService.UserPerms(), userCo, null, null, userName);
            var meta = new SanadMetaDto
            {
                CanSee = p.See,
                CanCreate = p.Inp,
                CanUpdate = p.Upd,
                CanDelete = p.Del,
                CanFinalize = p.Finalize,
                CanSign1 = sign.S1,
                CanSign2 = sign.S2,
                CanSign3 = sign.S3,
                SignTitles = SlotTitles.ToList(),
                Personnel = tm.Personnel,
                MarkazEnabled = TreasuryService.OptionOn(s.OPTIONSS, 43),
                Ada = s.AdaOr,
                Adv = s.AdvOr,
                Apa = s.ApaOr,
                Apv = s.ApvOr,
                Bankha = s.Bankha,
                Banks = tm.Banks,
                ChequeFunds = tm.ChequeFunds,
                BankAccounts = tm.BankAccounts,
                DateControl = s.CtlDt,
                FiscalYear = s.Yea,
                UserName = userName,
                UserId = userCo
            };
            if (meta.MarkazEnabled)
                meta.CostCenters = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                    "SELECT MHAZ_NO Id, CAST(MHAZNAME AS nvarchar(200)) Name FROM dbo.TCOD_MARKAZHAZ ORDER BY MHAZ_NO")).ToList();
            foreach (var c in meta.CostCenters) c.Name = TreasuryService.NormalizeFa(c.Name);
            return meta;
        }

        // ─────────────────────────── فهرست ───────────────────────────

        private const string ListSelect = @"
            SELECT h.N_S Ns, h.DATE_S Date, h.SHARH_S Sharh, CAST(h.NO_S AS int) NoS, h.base Base, h.BAYEG Bayeg,
                   h.USER_NAME UserName, CAST(ISNULL(h.OKF, 0) AS bit) Okf, CAST(ISNULL(h.GHATEI, 0) AS bit) Final,
                   CAST(ISNULL(h.SGN1, 0) AS bit) Sgn1, CAST(ISNULL(h.SGN2, 0) AS bit) Sgn2, CAST(ISNULL(h.SGN3, 0) AS bit) Sgn3,
                   h.sgn1usid Sgn1User, h.sgn2usid Sgn2User, h.sgn3usid Sgn3User, h.CRT Created,
                   ISNULL(s.Cnt, 0) [RowCount], ISNULL(s.Bed, 0) SumBed, ISNULL(s.Bes, 0) SumBes
            FROM dbo.DEED_HED h
            OUTER APPLY (SELECT COUNT(*) Cnt, SUM(d.BED) Bed, SUM(d.BES) Bes FROM dbo.DEED_DTL d WHERE d.N_S = h.N_S) s";

        /// <param name="kind">all | manual | auto | عدد (NO_S)</param>
        public async Task<List<SanadListItemDto>> ListAsync(long? from, long? to, string? kind, string? q)
        {
            var args = new DynamicParameters();
            var where = new List<string>();
            if (from is > 0) { where.Add("h.DATE_S >= @From"); args.Add("From", from); }
            if (to is > 0) { where.Add("h.DATE_S <= @To"); args.Add("To", to); }
            switch (kind)
            {
                case "manual": where.Add("h.NO_S = 0"); break;
                case "auto": where.Add("h.NO_S <> 0"); break;
                default:
                    if (int.TryParse(kind, out var k)) { where.Add("h.NO_S = @Kind"); args.Add("Kind", k); }
                    break;
            }
            q = TreasuryService.NormalizeFa(q);
            if (q.Length > 0)
            {
                // شماره سند / مبنا / بایگانی دقیق؛ شرح و کاربر با هر دو نگارشِ ی/ک
                var digits = new string(q.Where(char.IsAsciiDigit).ToArray());
                var parts = new List<string> { "h.SHARH_S LIKE N'%' + @q + N'%'", "h.SHARH_S LIKE N'%' + @qa + N'%'", "h.USER_NAME LIKE N'%' + @q + N'%'", "h.USER_NAME LIKE N'%' + @qa + N'%'" };
                if (digits.Length > 0 && digits.Length == q.Length && digits.Length <= 12 && long.TryParse(digits, out var n))
                {
                    parts.Add("h.N_S = @n");
                    parts.Add("h.base = @n");
                    parts.Add("h.BAYEG = @n");
                    args.Add("n", n);
                }
                where.Add("(" + string.Join(" OR ", parts) + ")");
                args.Add("q", q);
                args.Add("qa", q.Replace('ی', 'ي').Replace('ک', 'ك'));
            }
            var sql = $"{ListSelect} {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY h.N_S DESC";
            return (await _db.DoGetDataSQLAsync<SanadListItemDto>(sql, args)).ToList();
        }

        internal async Task<SanadListItemDto?> HeaderAsync(double ns)
            => (await _db.DoGetDataSQLAsync<SanadListItemDto>($"{ListSelect} WHERE h.N_S = @ns", new { ns })).FirstOrDefault();

        public async Task<double?> NsByBaseAsync(int @base)
            => await _db.DoGetDataSQLAsyncSingle<double?>("SELECT TOP 1 N_S FROM dbo.DEED_HED WHERE base = @base", new { @base });

        /// <summary>سندِ قبلی/بعدی (پیمایشِ First/Back/Next/End ِ WPF).</summary>
        public async Task<double?> NeighbourAsync(double ns, bool next)
            => await _db.DoGetDataSQLAsyncSingle<double?>(next
                   ? "SELECT MIN(N_S) FROM dbo.DEED_HED WHERE N_S > @ns"
                   : "SELECT MAX(N_S) FROM dbo.DEED_HED WHERE N_S < @ns", new { ns });

        // ─────────────────────────── جزئیات ───────────────────────────

        public async Task<SanadDetailDto?> GetAsync(double ns)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return null;
            var s = await _trs.SazmanAsync();

            var rows = (await _db.DoGetDataSQLAsync<SanadRowDto>(@"
                SELECT d.id Id, d.HES Hes, CAST(c.NAME AS nvarchar(300)) HesName, d.SHARH Sharh,
                       ISNULL(d.BED, 0) Bed, ISNULL(d.BES, 0) Bes, d.MHAZ_NO MhazNo, d.N_SERI NSeri, d.BANK Bank,
                       CAST(b.NAMES AS nvarchar(200)) BankName, d.NUMBER Number, d.TAG Tag
                FROM dbo.DEED_DTL d
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = d.HES) c
                LEFT JOIN dbo.TCOD_BANKS b ON b.CODE = d.BANK
                WHERE d.N_S = @ns
                ORDER BY d.id", new { ns })).ToList();

            foreach (var r in rows)
            {
                r.HesName = TreasuryService.NormalizeFa(r.HesName);
                r.BankName = TreasuryService.NormalizeFa(r.BankName);
                if (RoleOf(r.Hes, r.Bed, r.Bes, s) is var role && role != ChequeRole.None && r.NSeri is not null && r.Bank is not null)
                {
                    r.Cheque = await RowChequeAsync(role, r.NSeri.Value, r.Bank.Value, r.Bed + r.Bes);
                    if (r.Cheque?.CustNo is { Length: > 0 } owner)
                        r.ChequeOwnerName = TreasuryService.NormalizeFa(await _db.DoGetDataSQLAsyncSingle<string?>(
                            "SELECT TOP 1 CAST(NAME AS nvarchar(300)) FROM dbo.CUST_HESAB WHERE hes = @owner", new { owner = owner.Trim() }));
                }
            }

            var signers = new[] { h.Sgn1User, h.Sgn2User, h.Sgn3User }.Where(x => x is > 0).Distinct().ToList();
            var names = signers.Count == 0 ? new Dictionary<int, string>()
                : (await _db.DoGetDataSQLAsync<(int Idd, string? Name)>("SELECT IDD, SAL_NAME FROM dbo.SALA_DTL WHERE IDD IN @signers", new { signers }))
                    .ToDictionary(x => x.Idd, x => TreasuryService.DecodeUser(x.Name));
            string? NameOf(int? u) => u is > 0 && names.TryGetValue(u.Value, out var n) ? n : null;

            return new SanadDetailDto
            {
                Header = h,
                Rows = rows,
                LockReason = LockReason(h),
                Source = h.Manual ? null : await SourceAsync(h, rows),
                Printed = await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 1 FROM dbo.CHAPNUM WHERE NUMBER = @ns AND TAG = 0", new { ns }) is not null,
                HistoryCount = await _db.DoGetDataSQLAsyncSingle<int>("SELECT COUNT(*) FROM dbo.TR_DEED_HED WHERE N_S = @ns", new { ns }),
                Sgn1Name = h.Sgn1 ? NameOf(h.Sgn1User) : null,
                Sgn2Name = h.Sgn2 ? NameOf(h.Sgn2User) : null,
                Sgn3Name = h.Sgn3 ? NameOf(h.Sgn3User) : null,
                Task = await TaskAsync(h.Base)
            };
        }

        /// <summary>
        /// چرا این سند قابلِ تغییر نیست — ESLAH_Click و Form_Current: قطعی، خودکار، امضاشده.
        /// «تأیید» (OKF) به‌تنهایی قفل نیست؛ فقط یعنی قبل از ویرایش باید «اصلاح سند» زد.
        /// </summary>
        public static string? LockReason(SanadListItemDto h)
        {
            if (h.Final) return "این سند قطعی شده و دیگر قابل تغییر نیست.";
            if (!h.Manual) return $"این سند اتوماتیک است ({SanadKinds.NameOf(h.NoS)}) و از اینجا قابل اصلاح نیست؛ از فرمِ مبدأ اصلاحش کنید.";
            if (h.Signed) return "این سند امضا شده است؛ برای اصلاح، اول امضاها را بردارید.";
            return null;
        }

        private async Task<SanadSourceDto> SourceAsync(SanadListItemDto h, List<SanadRowDto> rows)
        {
            var kind = SanadKinds.NameOf(h.NoS);
            if (h.NoS == TreasuryService.SanadKind)
            {
                var id = await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 ID FROM dbo.PGET_HED WHERE N_S = @ns ORDER BY ID", new { ns = h.Ns });
                if (id is not null) return new SanadSourceDto { Title = $"خزانه‌ی شماره {id}", Link = $"/treasury/{id}" };
            }
            var invoice = rows.FirstOrDefault(r => r.Number is > 0)?.Number;
            return new SanadSourceDto { Title = invoice is null ? kind : $"{kind} شماره {invoice:0}" };
        }

        // ─────────────────────────── حساب‌ها ───────────────────────────

        /// <summary>
        /// جستجوی حساب (CUST_HESAB) با کد یا نام — همان SearchAccountsAsync ِ خزانه، به‌اضافه‌ی
        /// علامتِ «زیرحساب دارد» (ISTAF) تا حسابِ غیرِ آخرین‌سطح از اول انتخاب نشود.
        /// </summary>
        public async Task<List<TreasuryAccountDto>> SearchAccountsAsync(string? q)
        {
            var list = await _trs.SearchAccountsAsync(q);
            foreach (var a in list) a.Name = TreasuryService.NormalizeFa(a.Name);
            var groups = await GroupAccountsAsync(list.Select(a => a.Hes));
            foreach (var a in list) a.IsGroup = groups.Contains(a.Hes);
            return list;
        }

        public Task<List<TreasuryBalanceDto>> BalancesAsync(IEnumerable<string> hes) => _trs.BalancesAsync(hes);

        /// <summary>ISTAF برای چند حساب با یک کوئری در هر سطح.</summary>
        internal async Task<HashSet<string>> GroupAccountsAsync(IEnumerable<string> hesList)
        {
            var result = new HashSet<string>();
            var parsed = hesList.Select(h => (Hes: h, P: TreasuryService.SplitHes(h))).Where(x => x.P is not null).ToList();
            async Task Check(int depth, string table, string cols)
            {
                var at = parsed.Where(x => Depth(x.P!) == depth).ToList();
                if (at.Count == 0) return;
                var keys = at.Select(x => string.Join("-", x.P!.Take(depth))).ToList();
                var found = (await _db.DoGetDataSQLAsync<string>(
                    $"SELECT DISTINCT {cols} FROM dbo.{table} WHERE {cols} IN @keys", new { keys })).ToHashSet();
                foreach (var x in at) if (found.Contains(string.Join("-", x.P!.Take(depth)))) result.Add(x.Hes);
            }
            const string k3 = "CAST(N_KOL AS varchar(12)) + '-' + CAST(NUMBER AS varchar(12)) + '-' + CAST(TNUMBER AS varchar(12))";
            await Check(3, "TDETA_HES2", k3);
            await Check(4, "TDETA_HES3", k3 + " + '-' + CAST(TNUMBER2 AS varchar(12))");
            await Check(5, "TDETA_HES4", k3 + " + '-' + CAST(TNUMBER2 AS varchar(12)) + '-' + CAST(TNUMBER3 AS varchar(12))");
            return result;
        }

        /// <summary>تعدادِ سطح‌های واقعیِ یک کدِ حساب (۳ تا ۶).</summary>
        public static int Depth(int[] p) => p.Count(x => x != int.MinValue);

        public async Task<List<TreasuryLookupItem>> CannedDescriptionsAsync()
        {
            var list = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                "SELECT ID Id, CAST(SHARH AS nvarchar(300)) Name FROM dbo.SHARH ORDER BY ID")).ToList();
            foreach (var x in list) x.Name = TreasuryService.NormalizeFa(x.Name);
            return list.Where(x => x.Name.Length > 0).ToList();
        }

        // ─────────────────────────── سربرگ ───────────────────────────

        private static SanadSaveResult Fail(string m) => new() { Ok = false, Error = m };

        /// <summary>
        /// SAVE_BTN_Click برای سندِ تازه: DATE_VALIDATE، شماره‌ی تازه (با قفل) و BAYEG، NO_S = 0،
        /// OKF = 0 — تا سندِ تازه بدونِ «اصلاح» ردیف بپذیرد.
        /// </summary>
        public async Task<SanadSaveResult> CreateAsync(SanadHeaderSaveRequest req, string userName, int userCo)
        {
            if (await _trs.FiscalDateError(req.Date, "تاریخ سند") is { } de) return Fail(de);
            var sharh = Clean(req.Sharh);
            if (sharh.Length > 255) return Fail("شرح سند بیش از ۲۵۵ نویسه است.");

            var ns = await _db.ExecuteInTransactionAsync((conn, tx) =>
                TreasuryService.ReserveSanadAsync(conn, tx, req.Date, sharh, userName, noS: SanadKinds.Manual, okf: false, uid: userCo),
                IsolationLevel.Serializable);
            return new SanadSaveResult { Ok = true, Ns = ns };
        }

        /// <summary>SAVE_BTN_Click برای سندِ موجود: تاریخ و شرحِ سند.</summary>
        public async Task<SanadSaveResult> UpdateHeaderAsync(double ns, SanadHeaderSaveRequest req)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (LockReason(h) is { } lr) return Fail(lr);
            if (await _trs.FiscalDateError(req.Date, "تاریخ سند") is { } de) return Fail(de);
            var sharh = Clean(req.Sharh);
            if (sharh.Length > 255) return Fail("شرح سند بیش از ۲۵۵ نویسه است.");

            await _db.DoExecuteSQLAsync("UPDATE dbo.DEED_HED SET DATE_S = @d, SHARH_S = @s WHERE N_S = @ns AND NO_S = 0",
                new { d = req.Date, s = sharh, ns });
            return new SanadSaveResult { Ok = true, Ns = ns };
        }

        /// <summary>
        /// «اصلاح سند» — ESLAH_Click: سندِ قطعی، خودکار یا امضاشده باز نمی‌شود؛ وگرنه نسخه‌ی فعلی در
        /// TR_DEED_HED و TR_DEED_DTL (CL_HESABDARI.TR با FLAGU = 1) و بعد صفحه اجازه‌ی ویرایش می‌دهد.
        /// (WPF نسخه را حتی وقتی امضا مانع می‌شد هم می‌نوشت.)
        /// </summary>
        public async Task<SanadSaveResult> UnlockAsync(double ns, string userName, string? clientIp)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (LockReason(h) is { } lr) return Fail(lr);
            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                await HistoryAsync(conn, tx, ns, userName, clientIp);
                return 0;
            });
            return new SanadSaveResult { Ok = true, Ns = ns };
        }

        private static async Task HistoryAsync(IDbConnection conn, IDbTransaction tx, double ns, string userName, string? clientIp)
        {
            var at = DateTime.Now;
            await TreasuryService.CopyToHistoryAsync(conn, tx, "DEED_HED", "N_S = @ns", new { ns }, true, userName, clientIp, at);
            await TreasuryService.CopyToHistoryAsync(conn, tx, "DEED_DTL", "N_S = @ns", new { ns }, true, userName, clientIp, at);
        }

        /// <summary>
        /// ردیفِ x از TR_DEED_DTL به کدام نسخه‌ی TR_DEED_HED (h) تعلق دارد: هم‌روز، کمتر از ۰٫۸۶ ثانیه بعد از h
        /// (پنجره‌ی TR_DEED_HEAD ِ WPF) و هیچ نسخه‌ی دیگری بینِ h و x نیست — دو نسخه‌ی پشتِ سرِ هم قاطی نمی‌شوند.
        /// WPF سربرگ و ردیف‌ها را با دو DateTime.Now ِ جدا (چند میلی‌ثانیه فاصله) می‌نوشت.
        /// </summary>
        private const string HistoryRowOfVersion = @"
            x.N_S = h.N_S AND x.UP_DATE = h.UP_DATE
            AND x.UP_TIME >= h.UP_TIME - 0.0000001 AND x.UP_TIME - h.UP_TIME < 0.00001
            AND NOT EXISTS (SELECT 1 FROM dbo.TR_DEED_HED h3
                            WHERE h3.N_S = h.N_S AND h3.UP_DATE = h.UP_DATE
                              AND h3.UP_TIME > h.UP_TIME + 0.0000001 AND h3.UP_TIME <= x.UP_TIME + 0.0000001)";

        /// <summary>
        /// حذفِ سند — DELETE_Click. WPF اول ردیف‌ها را یکی‌یکی و بعد سربرگِ خالی را حذف می‌کرد؛ اینجا یک‌جا
        /// و در یک تراکنش، با نسخه‌ی سابقه و آزاد کردنِ چک‌ها. سندی که جای دیگری به آن ارجاع داده
        /// (فاکتور، خزانه، …) حذف نمی‌شود.
        /// </summary>
        public async Task<SanadSaveResult> DeleteAsync(double ns, string userName, string? clientIp)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (LockReason(h) is { } lr) return Fail(lr);

            var s = await _trs.SazmanAsync();
            var rows = await RowsForWriteAsync(ns);
            var releases = new List<(ChequeRole Role, TreasuryChequeDto Cheque)>();
            foreach (var r in rows)
            {
                var role = RoleOf(r.Hes, r.Bed, r.Bes, s);
                if (role == ChequeRole.None || r.NSeri is null || r.Bank is null) continue;
                var c = await RowChequeAsync(role, r.NSeri.Value, r.Bank.Value, r.Bed + r.Bes);
                if (c is null) continue;
                var (noAm, nahva) = TreasuryShape(role);
                if (TreasuryService.ChequeLockedReason(noAm, nahva, c, s.Bankha) is { } why)
                    return Fail($"چکِ {TreasuryService.ChequeSerial(c.NSeri)} {why}؛ این سند حذف نمی‌شود.");
                releases.Add((role, c));
            }

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    await HistoryAsync(conn, tx, ns, userName, clientIp);
                    foreach (var (role, c) in releases)
                    {
                        var (noAm, nahva) = TreasuryShape(role);
                        await TreasuryService.ReleaseRowChequeAsync(conn, tx, noAm, nahva, c, userName);
                    }
                    await conn.ExecuteAsync("DELETE FROM dbo.DEED_DTL WHERE N_S = @ns; DELETE FROM dbo.DEED_HED WHERE N_S = @ns AND NO_S = 0;", new { ns }, tx);
                    return 0;
                });
            }
            catch (Exception ex) when (IsFk(ex))
            {
                return Fail("این سند دارای اطلاعات وابسته است و نمی‌توان آن را حذف کرد.");
            }
            return new SanadSaveResult { Ok = true, Ns = ns, Count = rows.Count };
        }

        internal static bool IsFk(Exception ex)
            => ex is System.Data.SqlClient.SqlException { Number: 547 }
               || ex.InnerException is System.Data.SqlClient.SqlException { Number: 547 };

        private static string Clean(string? s) => (s ?? "").Trim();

        // ─────────────────────────── سوابقِ اصلاح ───────────────────────────

        /// <summary>نسخه‌های ذخیره‌شده در TR_DEED_HED (پنجره‌ی «سوابق سند دستی» ِ WPF) برای یک سند.</summary>
        public async Task<List<SanadHistoryItemDto>> HistoryListAsync(double ns)
        {
            var list = (await _db.DoGetDataSQLAsync<SanadHistoryItemDto>($@"
                SELECT h.TRIDD Tridd, CAST(h.UP_DATE AS bigint) [UpDate], CAST(h.UP_TIME AS float) [UpTime],
                       CAST(h.UP_USER_NAME AS nvarchar(100)) [User], CAST(h.PC_NAME AS nvarchar(100)) Pc,
                       h.DATE_S Date, h.SHARH_S Sharh,
                       ISNULL(d.Cnt, 0) [RowCount], ISNULL(d.Bed, 0) SumBed, ISNULL(d.Bes, 0) SumBes
                FROM dbo.TR_DEED_HED h
                OUTER APPLY (SELECT COUNT(*) Cnt, SUM(BED) Bed, SUM(BES) Bes FROM dbo.TR_DEED_DTL x
                             WHERE {HistoryRowOfVersion}) d
                WHERE h.N_S = @ns
                ORDER BY h.TRIDD DESC", new { ns })).ToList();
            foreach (var x in list) x.User = TreasuryService.NormalizeFa(x.User);
            return list;
        }

        public async Task<List<SanadHistoryRowDto>> HistoryRowsAsync(double ns, long tridd)
        {
            var list = (await _db.DoGetDataSQLAsync<SanadHistoryRowDto>($@"
                SELECT x.HES Hes, CAST(c.NAME AS nvarchar(300)) HesName, x.SHARH Sharh, ISNULL(x.BED, 0) Bed, ISNULL(x.BES, 0) Bes
                FROM dbo.TR_DEED_HED h
                JOIN dbo.TR_DEED_DTL x ON {HistoryRowOfVersion}
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = x.HES) c
                WHERE h.TRIDD = @tridd AND h.N_S = @ns
                ORDER BY x.id", new { tridd, ns })).ToList();
            foreach (var x in list) x.HesName = TreasuryService.NormalizeFa(x.HesName);
            return list;
        }

        // ─────────────────────────── قطعی کردن ───────────────────────────

        /// <summary>
        /// F_MENU_ASNAD.BTN_GO_Click: GHATEI ِ همه‌ی سندهای بازه‌ی شماره = ۱ (برگشت‌ناپذیر). پیش‌نمایش
        /// می‌گوید چند سند، چندتا ناتراز و چندتا بی‌امضا — WPF همه را بی‌پرسش قطعی می‌کرد.
        /// </summary>
        public async Task<SanadFinalizePreviewDto> FinalizeAsync(SanadFinalizeRequest req)
        {
            var args = new { from = Math.Min(req.From, req.To), to = Math.Max(req.From, req.To) };
            var preview = (await _db.DoGetDataSQLAsync<SanadFinalizePreviewDto>(@"
                SELECT COUNT(*) Count,
                       SUM(CASE WHEN ISNULL(h.GHATEI, 0) = 1 THEN 1 ELSE 0 END) AlreadyFinal,
                       SUM(CASE WHEN ROUND(ISNULL(s.Bed, 0) - ISNULL(s.Bes, 0), 0) <> 0 THEN 1 ELSE 0 END) Unbalanced,
                       SUM(CASE WHEN ISNULL(h.SGN1,0) = 0 AND ISNULL(h.SGN2,0) = 0 AND ISNULL(h.SGN3,0) = 0 THEN 1 ELSE 0 END) Unsigned
                FROM dbo.DEED_HED h
                OUTER APPLY (SELECT SUM(BED) Bed, SUM(BES) Bes FROM dbo.DEED_DTL d WHERE d.N_S = h.N_S) s
                WHERE h.N_S BETWEEN @from AND @to", args)).FirstOrDefault() ?? new();
            if (req.Apply && preview.Count > 0)
                preview.Applied = await _db.DoExecuteSQLAsync(
                    "UPDATE dbo.DEED_HED SET GHATEI = 1 WHERE N_S BETWEEN @from AND @to AND ISNULL(GHATEI, 0) = 0", args);
            return preview;
        }
    }
}
