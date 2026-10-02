using System.Data;
using System.Globalization;
using Dapper;
using Safir.Shared.Models.Treasury;

namespace Safir.Server.Treasury
{
    // ═══════════════════════════════════════════════════════════════════
    //  سطرهای خزانه و چک‌ها — پورتِ CmdSaveRecord / NAHVA_AfterUpdate /
    //  DELETE_FACTOR22_Click و پنجره‌های چکِ WPF (Wins/WinMenus/Checkha):
    //
    //   دریافت ۲/۶  چک (غیرتجاری)   GETCHEK   → PAY_GETD   به‌حساب = ADA / ADV
    //   پرداخت ۲/۶  چک (غیرتجاری)   PAYCHEK   → PAY_GETP   از‌حساب = APA / APV
    //   پرداخت ۴    واگذاری چک       FORCHEK   PAY_GETD.N_KOL = گیرنده، VAZ = 4
    //   پرداخت ۵    برگشت چکِ دریافتی BAKCHEK   PAY_GETD.N_KOL2 = طرف، VAZ انتخابی
    //   دریافت ۵    برگشت چکِ پرداختی BAKCHEKP  PAY_GETP.N_KOL2 = APA، VAZ انتخابی
    //
    //  تفاوت‌های عمدی با WPF (همه به‌خاطرِ درستیِ داده؛ رفتارِ روزمره همان است):
    //   • همه‌ی نوشتن‌ها (چک + سطر + سند) در یک تراکنش‌اند؛ WPF آن‌ها را جدا می‌نوشت.
    //   • حذفِ سطرِ واگذاری/برگشتِ چکِ دریافتی، چک را در PAY_GETD آزاد می‌کند؛ WPF به‌اشتباه
    //     PAY_GETP را به‌روز می‌کرد و چک «واگذارشده» می‌ماند.
    //   • چکِ دریافتیِ واگذارشده به شخص هم «در گردش» حساب می‌شود و سطرِ دریافتش حذف یا
    //     مبلغش عوض نمی‌شود (WPF فقط وصول/برگشت را می‌دید).
    //   • چکِ گروهی سریالِ عددی (سریال + i) را در PGET_LST هم درست می‌نویسد (WPF رشته را
    //     می‌چسباند: «1000» + «1» = 10001).
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class TreasuryService
    {
        /// <summary>چکِ این سطر در PAY_GETP است (نه PAY_GETD)؟</summary>
        public static bool PayableTable(int noAm, int nahva)
            => (noAm == TreasuryOp.Payment && TreasuryMethod.IsNewCheque(nahva))
               || (noAm == TreasuryOp.Receipt && nahva == TreasuryMethod.ChequeReturn);

        internal const string GetdSelect = @"
            SELECT g.ID Id, CAST(0 AS bit) Payable, g.N_SERI NSeri, g.BANK Bank, CAST(b.NAMES AS nvarchar(200)) BankName,
                   g.DATE_S DateS, g.DATE Date, g.SHOBEH Shobeh, g.MABL Mabl, g.NAME_TAH NameTah, g.N_HESAB NHesab,
                   g.LIST_NO ListNo, g.SANDUGH Sandugh, g.SAYADI Sayadi, g.HES1 Hes1, g.HES2 Hes2, g.VAZ Vaz, g.KIND Kind,
                   g.RADIF Radif, CAST(g.CUST_NO AS nvarchar(40)) CustNo, g.N_KOL NKol, g.N_KOL2 NKol2, g.N_KOL3 NKol3,
                   CAST(g.N_S AS float) Ns
            FROM dbo.PAY_GETD g LEFT JOIN dbo.TCOD_BANKS b ON b.CODE = g.BANK";

        internal const string GetpSelect = @"
            SELECT g.ID Id, CAST(1 AS bit) Payable, g.N_SERI NSeri, g.BANK Bank, CAST(b.NAMES AS nvarchar(200)) BankName,
                   g.DATE_S DateS, g.DATE Date, g.SHOBEH Shobeh, g.MABL Mabl, g.NAME_TAH NameTah, g.N_HESAB NHesab,
                   CAST(NULL AS int) ListNo, CAST(NULL AS int) Sandugh, g.SAYADI Sayadi, g.HES1 Hes1, g.HES2 Hes2, g.VAZ Vaz,
                   g.KIND Kind, CAST(g.RADIF AS float) Radif, CAST(g.CUST_NO AS nvarchar(40)) CustNo,
                   g.N_KOL NKol, g.N_KOL2 NKol2, g.N_KOL3 NKol3, CAST(g.N_S AS float) Ns
            FROM dbo.PAY_GETP g LEFT JOIN dbo.TCOD_BANKS b ON b.CODE = g.BANK";

        /// <summary>
        /// چکِ یک سطر — مثل sanaddar_sub/sanadpar_sub: همان سریال و بانک، اول آن که مبلغش با سطر یکی است.
        /// </summary>
        internal async Task<TreasuryChequeDto?> RowChequeAsync(TreasuryRowDto r)
        {
            var inner = (PayableTable(r.NoAm, r.Nahva) ? GetpSelect : GetdSelect) + " WHERE g.N_SERI = @s AND g.BANK = @b";
            var c = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(
                $"SELECT TOP 1 * FROM ({inner}) x ORDER BY CASE WHEN x.Mabl = @m THEN 0 ELSE 1 END, x.DateS DESC",
                new { s = r.NSeri, b = r.Bank, m = r.Mabl })).FirstOrDefault();
            if (c is not null) c.BankName = NormalizeFa(c.BankName);
            return c;
        }

        internal static async Task<TreasuryChequeDto?> ChequeByIdAsync(IDbConnection conn, IDbTransaction? tx, bool payable, long id)
            => (await conn.QueryAsync<TreasuryChequeDto>((payable ? GetpSelect : GetdSelect) + " WHERE g.ID = @id", new { id }, tx)).FirstOrDefault();

        /// <summary>
        /// فهرستِ چک‌های قابلِ انتخاب برای واگذاری/برگشت — همان RowSourceهای FORCHEK
        /// (FOR_CHK_SERCH)، BAKCHEK و BAKCHEKP. چکِ پارک‌شده روی ۹۱۱ (سطرِ دریافتش حذف شده) نمی‌آید.
        /// برگشتِ چکِ دریافتی بی‌جستجو فقط چک‌های نزدِ خودمان (صندوق/بانک) را می‌دهد: چک‌های واگذارشده به
        /// اشخاص هزاران‌اند و صاحبِ چکِ برگشتی سریالش را در دست دارد — با جستجو پیدا می‌شوند.
        /// </summary>
        public async Task<List<TreasuryChequeDto>> PickableChequesAsync(string mode, string? q)
        {
            var (select, where) = mode switch
            {
                // FORCHEK: N_SERI.RowSource — وصول‌شده (N_S) هم واگذار نمی‌شود
                "assign" => (GetdSelect, "(g.N_S IS NULL OR g.N_S = 0) AND g.N_KOL IS NULL AND g.N_KOL2 IS NULL AND g.N_KOL3 IS NULL"),
                "return-received" => (GetdSelect, "(g.N_S IS NULL OR g.N_S = 0) AND g.N_KOL2 IS NULL AND g.N_KOL3 IS NULL AND ISNULL(g.N_KOL, 0) <> 911"),
                "return-paid" => (GetpSelect, "(g.N_S IS NULL OR g.N_S = 0) AND g.N_KOL2 IS NULL AND g.N_KOL3 IS NULL AND ISNULL(g.N_KOL, 0) <> 911"),
                _ => (null, null)
            };
            if (select is null) return new();

            q = NormalizeFa(q);
            var args = new DynamicParameters();
            if (mode == "return-received" && q.Length == 0)
            {
                where += " AND (g.N_KOL IS NULL OR g.N_KOL = @bankha)";
                args.Add("bankha", (await SazmanAsync()).Bankha);
            }
            if (q.Length > 0)
            {
                where += " AND (CAST(CAST(g.N_SERI AS bigint) AS nvarchar(30)) LIKE @q + '%' OR g.NAME_TAH LIKE N'%' + @q + N'%' OR g.NAME_TAH LIKE N'%' + @qa + N'%'" +
                         " OR CAST(CAST(g.MABL AS bigint) AS nvarchar(30)) = @q OR g.SAYADI LIKE @q + '%')";
                args.Add("q", q);
                args.Add("qa", q.Replace('ی', 'ي').Replace('ک', 'ك'));
            }
            var list = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>($"{select} WHERE {where} ORDER BY g.DATE_S, g.N_SERI OFFSET 0 ROWS FETCH NEXT 300 ROWS ONLY", args)).ToList();
            foreach (var c in list) c.BankName = NormalizeFa(c.BankName);
            return list;
        }

        // ─────────────────────────── ثبت/اصلاحِ سطر ───────────────────────────

        private sealed class ExistingRow
        {
            public int Idh { get; set; }
            public int Id { get; set; }
            public int NoAm { get; set; }
            public int Nahva { get; set; }
            public double? NSeri { get; set; }
            public int? Bank { get; set; }
            public double Mabl { get; set; }
            public string? Fhes { get; set; }
            public string? Thes { get; set; }
            public double? Radif { get; set; }
        }

        private async Task<ExistingRow?> RowAsync(int idh)
            => (await _db.DoGetDataSQLAsync<ExistingRow>(@"
                   SELECT IDH Idh, ID Id, NO_AM NoAm, CAST(NAHVA AS int) Nahva, N_SERI NSeri, BANK Bank, MABL Mabl,
                          FHES Fhes, THES Thes, RADIF Radif
                   FROM dbo.PGET_LST WHERE IDH = @idh", new { idh })).FirstOrDefault();

        /// <summary>آنچه در PGET_LST نوشته می‌شود (یک سطر).</summary>
        private sealed class RowData
        {
            public int NoAm, Nahva;
            public string Fhes = "", Thes = "", Sharh = "";
            public double Mabl;
            public double? NSeri;
            public int? Bank;
            public int? MhazNo;
        }

        public async Task<TreasurySaveResult> SaveRowAsync(int id, int? idh, TreasuryRowSaveRequest r, UserPerms p, int userDept,
                                                           string userName, int userCo, string? clientIp = null)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            if (EditBlock(h, p, userName) is { } lr) return Fail(lr);

            if (r.NoAm is not (TreasuryOp.Receipt or TreasuryOp.Payment)) return Fail("نوع عملیات خالی یا نامعتبر است.");
            if (r.Nahva is < 1 or > 6) return Fail("نحوه خالی یا نامعتبر است.");
            if (!TreasuryMethod.Allowed(r.NoAm, r.Nahva)) return Fail("مقدار وارده مجاز نیست: «واگذاری چک» فقط برای پرداخت است.");
            if ((r.Sharh?.Length ?? 0) > 255) return Fail("شرح عملیات بیش از اندازه‌ی مجاز (۲۵۵ نویسه) است.");

            ExistingRow? old = null;
            Release? release = null;
            if (idh is not null)
            {
                old = await RowAsync(idh.Value);
                if (old is null || old.Id != id) return Fail("سطر پیدا نشد.");

                // نوع یا نحوه‌ی سطرِ چکی عوض شده: چکِ قبلی همان‌طور آزاد می‌شود که با حذفِ سطر، و در همان
                // تراکنش سطر با نوعِ تازه ذخیره می‌شود (همان IDH و ردیف). WPF این را نمی‌گذاشت و می‌گفت
                // «سطر را حذف و دوباره ثبت کنید»؛ اینجا همان دو کار یک‌جا و بی‌خطرِ نیمه‌کاره ماندن انجام می‌شود.
                if (!TreasuryMethod.SameChequeRole(old.NoAm, old.Nahva, r.NoAm, r.Nahva))
                {
                    var payable = PayableTable(old.NoAm, old.Nahva);
                    var oldCheque = old.NSeri is not null && old.Bank is not null ? await CurrentPickedAsync(old, payable) : null;
                    if (ChequeLockedReason(old.NoAm, old.Nahva, oldCheque, (await SazmanAsync()).Bankha) is { } why)
                        return Fail($"چکِ این سطر {why}؛ نوع و نحوه‌ی سطر دیگر عوض نمی‌شود.");
                    if (oldCheque is not null && r.PickedChequeId == oldCheque.Id && PayableTable(r.NoAm, r.Nahva) == payable)
                        return Fail("همان چکِ این سطر را نمی‌شود با نوعِ تازه دوباره انتخاب کرد؛ سطر را حذف و دوباره ثبت کنید.");

                    var was = old;
                    release = (conn, tx) => ReleaseRowChequeAsync(conn, tx, was.NoAm, was.Nahva, oldCheque, userName);
                    // از اینجا سطر «بی‌چک» است: Save* چکِ تازه می‌سازد/انتخاب می‌کند و فقط IDH را نگه می‌دارد
                    old = new ExistingRow { Idh = was.Idh, Id = was.Id, NoAm = was.NoAm, Nahva = was.Nahva, Mabl = was.Mabl };
                }
                // سطرِ نقد/سایر چکی ندارد؛ سریال/بانکِ جامانده از داده‌ی قدیمی نباید چکِ کسِ دیگری را «چکِ فعلی» کند
                else if (!TreasuryMethod.IsCheque(old.Nahva)) { old.NSeri = null; old.Bank = null; }
            }

            return r.Nahva switch
            {
                TreasuryMethod.Cash or TreasuryMethod.Other => await SavePlainRowAsync(h, idh, r, userCo, release),
                TreasuryMethod.Cheque or TreasuryMethod.NonTradeCheque => r.NoAm == TreasuryOp.Receipt
                    ? await SaveReceivedChequeAsync(h, old, r, userName, userCo, clientIp, release)
                    : await SavePaidChequeAsync(h, old, r, userName, userCo, clientIp, release),
                TreasuryMethod.ChequeAssign => await SaveAssignAsync(h, old, r, userName, userCo, release),
                _ => r.NoAm == TreasuryOp.Payment
                    ? await SaveReturnReceivedAsync(h, old, r, userName, userCo, clientIp, release)
                    : await SaveReturnPaidAsync(h, old, r, userName, userCo, clientIp, release)
            };
        }

        /// <summary>آزاد کردنِ چکِ قبلیِ سطر، اولِ تراکنشِ ذخیره (وقتی نوع/نحوه‌ی سطرِ چکی عوض شده).</summary>
        private delegate Task Release(IDbConnection conn, IDbTransaction tx);

        /// <summary>
        /// چکِ دریافتی/پرداختیِ سطر که دیگر دستِ ما نیست — نه سطرش حذف می‌شود نه نوعش عوض. null = آزاد.
        /// واگذاری و برگشت همیشه قابلِ برگشت‌اند.
        /// </summary>
        public static string? ChequeLockedReason(int noAm, int nahva, TreasuryChequeDto? c, int? bankha)
        {
            if (c is null || !TreasuryMethod.IsNewCheque(nahva)) return null;
            if (noAm == TreasuryOp.Receipt && c.InCirculation(bankha)) return "وصول، واگذار یا برگشت شده";
            if (noAm == TreasuryOp.Payment && (c.NKol2 is not null and not 911 || c.NKol3 is not null)) return "وصول یا برگشت شده";
            return null;
        }

        /// <summary>
        /// DELETE_FACTOR22_Click روی چکِ سطر: چکِ دریافتی/پرداختی روی حسابِ معلقِ ۹۱۱-۱-۱ پارک می‌شود؛
        /// واگذاری و برگشت آزاد می‌شوند.
        /// </summary>
        internal static async Task ReleaseRowChequeAsync(IDbConnection conn, IDbTransaction tx, int noAm, int nahva, TreasuryChequeDto? cheque, string userName)
        {
            if (cheque is null) return;
            switch (noAm, nahva)
            {
                case (TreasuryOp.Receipt, TreasuryMethod.Cheque or TreasuryMethod.NonTradeCheque):
                    await conn.ExecuteAsync("UPDATE dbo.PAY_GETD SET N_KOL = 911, N_MOIN = 1, N_TAF = 1, HES1 = N'911-1-1' WHERE ID = @id", new { id = cheque.Id }, tx);
                    await LogReceivedAsync(conn, tx, cheque.NSeri, cheque.Bank, cheque.DateS, 1, cheque.Sandugh, userName, setVaz: true);
                    break;
                case (TreasuryOp.Payment, TreasuryMethod.Cheque or TreasuryMethod.NonTradeCheque):
                    await conn.ExecuteAsync("UPDATE dbo.PAY_GETP SET N_KOL = 911, N_MOIN = 1, N_TAF = 1, HES1 = N'911-1-1' WHERE ID = @id", new { id = cheque.Id }, tx);
                    break;
                case (TreasuryOp.Payment, TreasuryMethod.ChequeAssign):
                    await ReleaseAssignAsync(conn, tx, cheque, userName);
                    break;
                case (TreasuryOp.Payment, TreasuryMethod.ChequeReturn):
                    await ReleaseReturnReceivedAsync(conn, tx, cheque, userName);
                    break;
                case (TreasuryOp.Receipt, TreasuryMethod.ChequeReturn):
                    await ReleaseReturnPaidAsync(conn, tx, cheque);
                    break;
            }
        }

        /// <summary>نقد و سایر — سمتِ صندوق در «نقد» خودکار است (NO_AM_AfterUpdate).</summary>
        private async Task<TreasurySaveResult> SavePlainRowAsync(TreasuryListItemDto h, int? idh, TreasuryRowSaveRequest r, int userCo, Release? release)
        {
            if (r.Mabl <= 0 || double.IsNaN(r.Mabl) || double.IsInfinity(r.Mabl)) return Fail("مبلغ صحیح نیست.");
            var fhes = r.Fhes?.Trim() ?? "";
            var thes = r.Thes?.Trim() ?? "";
            if (r.Nahva == TreasuryMethod.Cash)
            {
                var cash = await CashAccountAsync(h.Depatman, h.Shift);
                if (cash is null) return Fail("حسابِ صندوق در تنظیمات (حساب‌های خودگردان) تعریف نشده است.");
                if (r.NoAm == TreasuryOp.Receipt) thes = cash; else fhes = cash;
            }

            var d = new RowData { NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Sharh = r.Sharh?.Trim() ?? "", Mabl = r.Mabl, MhazNo = r.MhazNo };
            if (await ValidateAccountsAsync(d) is { } err) return Fail(err);

            var newIdh = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                if (release is not null) await release(conn, tx);
                var rowId = await UpsertRowAsync(conn, tx, h.Id, h.Date, idh, d, userCo);
                await RebuildSanadAsync(conn, tx, h.Id);
                return rowId;
            });
            return new TreasurySaveResult { Ok = true, Id = h.Id, Idh = newIdh };
        }

        /// <summary>هر دو حساب در CUST_HESAB؛ تفصیلیِ «از حساب» در TDETA_HES (کلیدِ خارجیِ PGET_LST)؛ مرکز هزینه.</summary>
        private async Task<string?> ValidateAccountsAsync(RowData d)
        {
            var f = SplitHes(d.Fhes);
            var t = SplitHes(d.Thes);
            if (f is null) return "فیلدِ «از حساب» خالی یا نادرست است.";
            if (t is null) return "فیلدِ «به حساب» خالی یا نادرست است.";
            var known = (await _db.DoGetDataSQLAsync<string>("SELECT hes FROM dbo.CUST_HESAB WHERE hes IN (@a, @b)", new { a = d.Fhes, b = d.Thes })).ToHashSet();
            if (!known.Contains(d.Fhes)) return $"حسابِ «از حساب» [{d.Fhes}] در سیستم وجود ندارد.";
            if (!known.Contains(d.Thes)) return $"حسابِ «به حساب» [{d.Thes}] در سیستم وجود ندارد.";
            if (!await TafExistsAsync(f)) return $"تفصیلیِ «از حساب» [{f[0]}-{f[1]}-{f[2]}] تعریف نشده است.";
            if (d.MhazNo is not null &&
                await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 MHAZ_NO FROM dbo.TCOD_MARKAZHAZ WHERE MHAZ_NO = @m", new { m = d.MhazNo }) is null)
                return "مرکز هزینه‌ی انتخاب‌شده معتبر نیست.";
            return null;
        }

        internal async Task<bool> TafExistsAsync(int[] a)
            => await _db.DoGetDataSQLAsyncSingle<int?>(
                   "SELECT TOP 1 1 FROM dbo.TDETA_HES WHERE N_KOL = @k AND NUMBER = @m AND TNUMBER = @t", new { k = a[0], m = a[1], t = a[2] }) is not null;

        private static object? Lvl(int[] a, int i) => a[i] == int.MinValue ? null : a[i];

        /// <summary>درج یا اصلاحِ یک سطرِ PGET_LST (RADIF = بیشترین + ۱ با قفل، مثل WPF).</summary>
        private static async Task<int> UpsertRowAsync(IDbConnection conn, IDbTransaction tx, int id, long date, int? idh, RowData d, int userCo)
        {
            var f = SplitHes(d.Fhes)!;
            var t = SplitHes(d.Thes)!;
            var args = new
            {
                ID = id, DATE = date, NO_AM = d.NoAm, NAHVA = (double)d.Nahva,
                FHES_K = f[0], FHES_M = f[1], FHES_T = f[2], FHES_T2 = Lvl(f, 3), FHES_T3 = Lvl(f, 4), FHES_T4 = Lvl(f, 5),
                THES_K = t[0], THES_M = t[1], THES_T = t[2], THES_T2 = Lvl(t, 3), THES_T3 = Lvl(t, 4), THES_T4 = Lvl(t, 5),
                SHARH = d.Sharh, MABL = d.Mabl, N_SERI = d.NSeri, BANK = d.Bank, FHES = d.Fhes, THES = d.Thes,
                MHAZ_NO = d.MhazNo, UID = userCo, IDH = idh
            };
            if (idh is null)
            {
                return await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.PGET_LST (ID, DATE, RADIF, NO_AM, NAHVA, FHES_K, FHES_M, FHES_T, THES_K, THES_M, THES_T,
                                              SHARH, MABL, N_SERI, BANK, FHES, THES, FHES_T2, THES_T2, FHES_T3, THES_T3, FHES_T4, THES_T4,
                                              MHAZ_NO, UID, CRT)
                    OUTPUT INSERTED.IDH
                    VALUES (@ID, @DATE, (SELECT ISNULL(MAX(RADIF), 0) + 1 FROM dbo.PGET_LST WITH (UPDLOCK, HOLDLOCK) WHERE ID = @ID),
                            @NO_AM, @NAHVA, @FHES_K, @FHES_M, @FHES_T, @THES_K, @THES_M, @THES_T,
                            @SHARH, @MABL, @N_SERI, @BANK, @FHES, @THES, @FHES_T2, @THES_T2, @FHES_T3, @THES_T3, @FHES_T4, @THES_T4,
                            @MHAZ_NO, @UID, GETDATE())", args, tx);
            }
            await conn.ExecuteAsync(@"
                UPDATE dbo.PGET_LST SET DATE = @DATE, NO_AM = @NO_AM, NAHVA = @NAHVA,
                       FHES_K = @FHES_K, FHES_M = @FHES_M, FHES_T = @FHES_T, THES_K = @THES_K, THES_M = @THES_M, THES_T = @THES_T,
                       SHARH = @SHARH, MABL = @MABL, N_SERI = @N_SERI, BANK = @BANK, FHES = @FHES, THES = @THES,
                       FHES_T2 = @FHES_T2, THES_T2 = @THES_T2, FHES_T3 = @FHES_T3, THES_T3 = @THES_T3,
                       FHES_T4 = @FHES_T4, THES_T4 = @THES_T4, MHAZ_NO = @MHAZ_NO
                WHERE IDH = @IDH AND ID = @ID", args, tx);
            return idh.Value;
        }

        // ─────────────────────────── چکِ دریافتی (GETCHEK) ───────────────────────────

        internal static string ChequeSerial(double nSeri) => nSeri.ToString("0", CultureInfo.InvariantCulture);

        internal static string Left255(string s) => s.Length > 255 ? s[..255] : s;
        private static string Right255(string s) => s.Length > 255 ? s[^255..] : s;

        internal async Task<string> BankNameAsync(int bank)
            => NormalizeFa(await _db.DoGetDataSQLAsyncSingle<string?>("SELECT TOP 1 CAST(NAMES AS nvarchar(200)) FROM dbo.TCOD_BANKS WHERE CODE = @bank", new { bank }));

        /// <summary>مشترکِ GETCHEK و PAYCHEK — پیام‌ها همان MsgListwin.</summary>
        internal async Task<List<string>> ValidateChequeInputAsync(TreasuryChequeInput c, long treasuryDate, bool receive, bool group)
        {
            var errors = new List<string>();
            if (receive && c.ListNo is null) errors.Add("کد شعبه صحیح نیست.");
            if (string.IsNullOrWhiteSpace(c.NameTah)) errors.Add(receive ? "نام پرداخت کننده نمی‌تواند خالی باشد." : "نام دریافت کننده نمی‌تواند خالی باشد.");
            else if (c.NameTah.Trim().Length > 190) errors.Add("نام پرداخت کننده باید مختصر و کوتاه باشد.");
            if (c.NSeri <= 0 || c.Bank <= 0 || c.DateS <= 0) errors.Add("شماره سریال، نام بانک و تاریخ سررسید نمی‌تواند خالی باشد!");
            if (await FiscalDateError(c.Date ?? treasuryDate, receive ? "تاریخ دریافت" : "تاریخ پرداخت") is { } de) errors.Add(de);
            if (c.DateS > 0 && !ValidDate(c.DateS)) errors.Add("تاریخ سررسید صحیح نیست.");
            if (!string.IsNullOrWhiteSpace(c.Sayadi) && (c.Sayadi.Trim().Length > 16 || !c.Sayadi.Trim().All(char.IsAsciiDigit)))
                errors.Add("شماره صیادی باید فقط رقم و حداکثر ۱۶ رقم باشد.");
            if ((c.Shobeh?.Trim().Length ?? 0) > 50) errors.Add("نام شعبه بیش از اندازه است.");
            if ((c.NHesab?.Trim().Length ?? 0) > (receive ? 50 : 198)) errors.Add("جاری چک بیش از اندازه است.");
            if (c.Bank > 0 && await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 1 FROM dbo.TCOD_BANKS WHERE CODE = @b", new { b = c.Bank }) is null)
                errors.Add("بانک انتخاب‌شده تعریف نشده است.");
            if (group)
            {
                if (c.Count is < 1 or > 60) errors.Add("تعداد فقره باید بین ۱ تا ۶۰ باشد.");
                if (c.GapMonths is < 1 or > 12) errors.Add("فاصله‌ی چک‌ها باید بین ۱ تا ۱۲ ماه باشد.");
            }
            return errors;
        }

        /// <summary>
        /// سررسیدِ iامین چکِ گروهی — CREATE_CHEKDP: ماه به ماه جلو، روز اگر در آن ماه نیست به آخرِ ماه.
        /// </summary>
        public static long AddMonthsFa(long date, int months)
        {
            if (months == 0) return date;
            int y = (int)(date / 10000), m = (int)(date / 100 % 100), d = (int)(date % 100);
            var total = y * 12 + (m - 1) + months;
            y = total / 12;
            m = total % 12 + 1;
            var max = new PersianCalendar().GetDaysInMonth(y, m);
            return y * 10000L + m * 100 + Math.Min(d, max);
        }

        private sealed class ChequeKey
        {
            public long Id { get; set; }
            public double? Radif { get; set; }
            public int? NKol { get; set; }
        }

        /// <summary>
        /// چکِ دریافتی (و غیرتجاری) — GETCHEK._SaveExit_Click و CREATE_CHEKDP (گروهی).
        /// به‌حساب = ADA (یا ADV)، از‌حساب = پرداخت‌کننده؛ PAY_GETD درج یا اصلاح، ردیفِ دفتر برای چکِ تازه.
        /// </summary>
        private async Task<TreasurySaveResult> SaveReceivedChequeAsync(TreasuryListItemDto h, ExistingRow? old, TreasuryRowSaveRequest r,
                                                                       string userName, int userCo, string? clientIp, Release? release)
        {
            if (r.Cheque is not { } c) return Fail("مشخصاتِ چک وارد نشده است.");
            var s = await SazmanAsync();
            var group = old is null && c.Count > 1;
            var errors = await ValidateChequeInputAsync(c, h.Date, true, group);
            if (r.Mabl <= 0) errors.Add("مبلغ نمی‌تواند خالی باشد.");
            if (errors.Count > 0) return Fail(string.Join("\n", errors.Distinct()));

            var thes = r.Nahva == TreasuryMethod.Cheque ? s.AdaOr : s.AdvOr;
            var fhes = r.Fhes?.Trim() ?? "";
            if (fhes.Length == 0) return Fail("«از حساب» (پرداخت‌کننده‌ی چک) را انتخاب کنید.");
            var kind = thes == s.AdaOr ? 1 : 0;

            // موقعیت چک — تفصیلی زیرِ ADA (پیش‌فرض ۱ مثل SANDUGH.SelectedValue = 1)
            var ada = SplitHes(s.AdaOr);
            var sandugh = c.Sandugh ?? 1;
            if (ada is not null && !await TafExistsAsync(new[] { ada[0], ada[1], sandugh }))
                return Fail("موقعیت چک (صندوق) معتبر نیست.");

            // واگذاری به بانک هنگامِ دریافت — فقط زیرِ کلِ بانک‌ها (HES_LostFocus)
            int[]? hes1 = null;
            var hes1Text = c.Hes1?.Trim();
            if (!string.IsNullOrEmpty(hes1Text) && hes1Text != "911-1-1")
            {
                hes1 = SplitHes(hes1Text);
                if (hes1 is null || hes1[0] != s.Bankha) return Fail("چک در این بخش فقط به بانک قابل واگذاری می‌باشد.");
                if (!await TafExistsAsync(hes1)) return Fail($"حسابِ بانکیِ [{hes1Text}] تعریف نشده است.");
            }
            else hes1Text = null;

            var d = new RowData { NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = r.Mabl, MhazNo = r.MhazNo, Bank = c.Bank };
            if (await ValidateAccountsAsync(d) is { } accErr) return Fail(accErr);

            // چکِ فعلیِ این سطر (اصلاح)
            TreasuryChequeDto? current = null;
            if (old?.NSeri is not null && old.Bank is not null)
                current = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(GetdSelect + " WHERE g.N_SERI = @s AND g.BANK = @b ORDER BY g.RADIF",
                    new { s = old.NSeri, b = old.Bank })).FirstOrDefault();
            if (current is not null && current.InCirculation(s.Bankha)
                && (current.NSeri != c.NSeri || current.Bank != c.Bank || current.DateS != c.DateS || current.Mabl != r.Mabl || old!.Nahva != r.Nahva))
                return Fail("این چک واگذار، وصول یا برگشت شده؛ سریال، بانک، سررسید، مبلغ و تجاری‌بودنش دیگر از اینجا عوض نمی‌شود.");

            var bankName = await BankNameAsync(c.Bank);
            var count = group ? c.Count : 1;
            var date = c.Date ?? h.Date;
            var infos = new List<string>();

            try
            {
                var newIdh = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    if (release is not null) await release(conn, tx);
                    var daft = await DaftAsync(conn, tx);

                    int firstIdh = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var serial = c.NSeri + i;
                        var dateS = AddMonthsFa(c.DateS, i * c.GapMonths);
                        var info = await WriteReceivedChequeAsync(conn, tx, new ReceivedChequeWrite
                        {
                            CurrentId = i == 0 ? current?.Id : null,
                            Serial = serial, Bank = c.Bank, DateS = dateS, Date = date, Shobeh = c.Shobeh, Mabl = r.Mabl,
                            NameTah = c.NameTah!, NHesab = c.NHesab, CustNo = fhes, ListNo = c.ListNo, Kind = kind,
                            Sandugh = sandugh, Sayadi = c.Sayadi, Hes1 = hes1, Hes1Text = hes1Text
                        }, daft, s.Bankha, userName, clientIp);
                        if (info is not null) infos.Add(info);

                        var row = new RowData
                        {
                            NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = r.Mabl, MhazNo = r.MhazNo,
                            NSeri = serial, Bank = c.Bank,
                            Sharh = Left255($" چك{ChequeSerial(serial)}بانك{bankName} {c.Shobeh?.Trim()} مورخ {FormatDate(dateS)}-{c.NameTah!.Trim()}")
                        };
                        var rowId = await UpsertRowAsync(conn, tx, h.Id, h.Date, i == 0 ? old?.Idh : null, row, userCo);
                        if (i == 0) firstIdh = rowId;
                    }
                    await RebuildSanadAsync(conn, tx, h.Id);
                    return firstIdh;
                });
                return new TreasurySaveResult { Ok = true, Id = h.Id, Idh = newIdh, Info = infos.Count == 0 ? null : string.Join(" · ", infos) };
            }
            catch (UserError ue) { return Fail(ue.Message); }
            catch (Exception ex) when (IsDuplicate(ex)) { return Fail("اطلاعات تکراری است: چکی با همین سریال، بانک و سررسید قبلاً ثبت شده است."); }
        }

        /// <summary>GETDLOG / GETCHEK: ردیفِ PAY_GETD_LOG و (در GETDLOG) VAZ ِ چک.</summary>
        internal static async Task LogReceivedAsync(IDbConnection conn, IDbTransaction tx, double serial, int bank, long dateS, int vaz, int? sandugh,
                                                   string userName, bool setVaz)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO dbo.PAY_GETD_LOG (N_SERI, BANK, DATE_S, DATE_V, DATETIM, VAZ, SANDUGH, USER_NAME)
                VALUES (@serial, @bank, @dateS, @today, GETDATE(), @vaz, @sandugh, @user)",
                new { serial, bank, dateS, today = Today(), vaz, sandugh, user = userName.Length > 40 ? userName[..40] : userName }, tx);
            if (setVaz)
                await conn.ExecuteAsync("UPDATE dbo.PAY_GETD SET VAZ = @vaz WHERE N_SERI = @serial AND BANK = @bank AND DATE_S = @dateS",
                    new { vaz, serial, bank, dateS }, tx);
        }

        // ─────────────────────────── چکِ پرداختی (PAYCHEK) ───────────────────────────

        /// <summary>
        /// چکِ خودمان — PAYCHEK._SaveExit_Click و CREATE_CHEKPDP (گروهی).
        /// از‌حساب = APA (یا APV)، به‌حساب = گیرنده؛ N_KOL.. = حسابِ بانکیِ پرداخت (پیش‌فرض اولین تفصیلیِ BANKHA).
        /// </summary>
        private async Task<TreasurySaveResult> SavePaidChequeAsync(TreasuryListItemDto h, ExistingRow? old, TreasuryRowSaveRequest r,
                                                                   string userName, int userCo, string? clientIp, Release? release)
        {
            if (r.Cheque is not { } c) return Fail("مشخصاتِ چک وارد نشده است.");
            var s = await SazmanAsync();
            var group = old is null && c.Count > 1;
            var errors = await ValidateChequeInputAsync(c, h.Date, false, group);
            if (r.Mabl <= 0) errors.Add("مبلغ نمی‌تواند خالی باشد.");
            if (errors.Count > 0) return Fail(string.Join("\n", errors.Distinct()));

            var fhes = r.Nahva == TreasuryMethod.Cheque ? s.ApaOr : s.ApvOr;
            var thes = r.Thes?.Trim() ?? "";
            if (thes.Length == 0) return Fail("«به حساب» (دریافت‌کننده‌ی چک) را انتخاب کنید.");
            var kind = fhes == s.ApaOr ? 1 : 0;

            var hes1Text = c.Hes1?.Trim();
            int[]? hes1;
            if (!string.IsNullOrEmpty(hes1Text) && hes1Text != "911-1-1")
            {
                hes1 = SplitHes(hes1Text);
                if (hes1 is null || hes1[0] != s.Bankha) return Fail("«پرداخت از حساب» باید یکی از حساب‌های بانکی باشد.");
                if (!await TafExistsAsync(hes1)) return Fail($"حسابِ بانکیِ [{hes1Text}] تعریف نشده است.");
            }
            else
            {
                // ApplyDefaultNKolFromBankha: DefaultValue ِ Access
                hes1 = s.Bankha is { } bk && await FirstTafAsync(bk) is { } first ? SplitHes(first) : null;
                hes1Text = "";
            }

            var d = new RowData { NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = r.Mabl, MhazNo = r.MhazNo, Bank = c.Bank };
            if (await ValidateAccountsAsync(d) is { } accErr) return Fail(accErr);

            TreasuryChequeDto? current = null;
            if (old?.NSeri is not null && old.Bank is not null)
                current = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(GetpSelect + " WHERE g.N_SERI = @s AND g.BANK = @b",
                    new { s = old.NSeri, b = old.Bank })).FirstOrDefault();
            if (current is not null && current.InCirculation(s.Bankha))
                return Fail("این چک وصول یا برگشت خورده و قابل اصلاح نیست.");

            var bankName = await BankNameAsync(c.Bank);
            var count = group ? c.Count : 1;
            var date = c.Date ?? h.Date;

            try
            {
                var newIdh = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    if (release is not null) await release(conn, tx);
                    int firstIdh = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var serial = c.NSeri + i;
                        var dateS = AddMonthsFa(c.DateS, i * c.GapMonths);
                        await WritePaidChequeAsync(conn, tx, new PaidChequeWrite
                        {
                            CurrentId = i == 0 ? current?.Id : null,
                            Serial = serial, Bank = c.Bank, DateS = dateS, Date = date, Shobeh = c.Shobeh, Mabl = r.Mabl,
                            NameTah = c.NameTah!, NHesab = c.NHesab, Kind = kind, Sayadi = c.Sayadi, Hes1 = hes1, Hes1Text = hes1Text
                        }, userName, clientIp);

                        var row = new RowData
                        {
                            NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = r.Mabl, MhazNo = r.MhazNo,
                            NSeri = serial, Bank = c.Bank,
                            Sharh = Left255($"چك {ChequeSerial(serial)}بانك {bankName} {c.Shobeh?.Trim()} مورخ {FormatDate(dateS)}-{c.NameTah!.Trim()}")
                        };
                        var rowId = await UpsertRowAsync(conn, tx, h.Id, h.Date, i == 0 ? old?.Idh : null, row, userCo);
                        if (i == 0) firstIdh = rowId;
                    }
                    await RebuildSanadAsync(conn, tx, h.Id);
                    return firstIdh;
                });
                return new TreasurySaveResult { Ok = true, Id = h.Id, Idh = newIdh };
            }
            catch (UserError ue) { return Fail(ue.Message); }
            catch (Exception ex) when (IsDuplicate(ex)) { return Fail("اطلاعات تکراری است: چکی با همین سریال، بانک و سررسید قبلاً ثبت شده است."); }
        }

        // ─────────────────────────── واگذاری و برگشت ───────────────────────────

        /// <summary>چکی که این سطرِ واگذاری/برگشت اکنون به آن اشاره دارد (اصلاح).</summary>
        private async Task<TreasuryChequeDto?> CurrentPickedAsync(ExistingRow? old, bool payable)
        {
            if (old?.NSeri is null || old.Bank is null) return null;
            var sql = (payable ? GetpSelect : GetdSelect) + " WHERE g.N_SERI = @s AND g.BANK = @b";
            var list = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(sql, new { s = old.NSeri, b = old.Bank })).ToList();
            return list.FirstOrDefault(x => x.Mabl == old.Mabl) ?? list.FirstOrDefault();
        }

        /// <summary>
        /// واگذاری چکِ دریافتی به شخص — FORCHEK._SaveExit_Click:
        /// به‌حساب = گیرنده، از‌حساب = ADA (چکِ غیرتجاری: ADV)؛ PAY_GETD.N_KOL.. = گیرنده، HES1، VAZ = 4، موقعیت.
        /// </summary>
        private async Task<TreasurySaveResult> SaveAssignAsync(TreasuryListItemDto h, ExistingRow? old, TreasuryRowSaveRequest r, string userName, int userCo,
                                                               Release? release)
        {
            if (r.PickedChequeId is not { } pick) return Fail("چکِ واگذاری را از فهرست انتخاب کنید.");
            if (r.Sandugh is null) return Fail("صندوق (موقعیت چک) نمی‌تواند خالی باشد.");
            var s = await SazmanAsync();
            var current = await CurrentPickedAsync(old, false);
            var cheque = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(GetdSelect + " WHERE g.ID = @pick", new { pick })).FirstOrDefault();
            if (cheque is null) return Fail("چک پیدا نشد.");
            var available = (cheque.NKol is null && cheque.NKol2 is null && cheque.NKol3 is null) || cheque.Id == current?.Id;
            if (!available) return Fail("این چک قبلاً واگذار، وصول یا برگشت شده است.");

            var thes = r.Thes?.Trim() ?? "";
            if (thes.Length == 0) return Fail("«به حساب» (گیرنده‌ی چک) را انتخاب کنید.");
            var t = SplitHes(thes);
            var fhes = cheque.Kind == 0 ? s.AdvOr : s.AdaOr;
            var d = new RowData
            {
                NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = cheque.Mabl, MhazNo = r.MhazNo,
                NSeri = cheque.NSeri, Bank = cheque.Bank,
                Sharh = Right255($"چك {ChequeSerial(cheque.NSeri)}بانك {NormalizeFa(cheque.BankName)} {cheque.Shobeh} مورخ {FormatDate(cheque.DateS)}")
            };
            if (await ValidateAccountsAsync(d) is { } accErr) return Fail(accErr);
            if (t is null || !await TafExistsAsync(t)) return Fail($"تفصیلیِ گیرنده [{thes}] تعریف نشده است.");

            var newIdh = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                if (release is not null) await release(conn, tx);
                if (current is not null && current.Id != cheque.Id)
                    await ReleaseAssignAsync(conn, tx, current, userName);

                await conn.ExecuteAsync(@"
                    UPDATE dbo.PAY_GETD SET N_KOL = @k, N_MOIN = @m, N_TAF = @t, HES1 = @hes, VAZ = 4, SANDUGH = @sandugh
                    WHERE ID = @id", new { k = t[0], m = t[1], t = t[2], hes = thes, sandugh = r.Sandugh, id = cheque.Id }, tx);
                await LogReceivedAsync(conn, tx, cheque.NSeri, cheque.Bank, cheque.DateS, 4, r.Sandugh, userName, setVaz: true);

                var rowId = await UpsertRowAsync(conn, tx, h.Id, h.Date, old?.Idh, d, userCo);
                await RebuildSanadAsync(conn, tx, h.Id);
                return rowId;
            });
            return new TreasurySaveResult { Ok = true, Id = h.Id, Idh = newIdh };
        }

        private static async Task ReleaseAssignAsync(IDbConnection conn, IDbTransaction tx, TreasuryChequeDto c, string userName)
        {
            await conn.ExecuteAsync("UPDATE dbo.PAY_GETD SET N_KOL = NULL, N_MOIN = NULL, N_TAF = NULL, HES1 = NULL WHERE ID = @id", new { id = c.Id }, tx);
            await LogReceivedAsync(conn, tx, c.NSeri, c.Bank, c.DateS, 1, c.Sandugh, userName, setVaz: true);
        }

        private static async Task ReleaseReturnReceivedAsync(IDbConnection conn, IDbTransaction tx, TreasuryChequeDto c, string userName)
        {
            await conn.ExecuteAsync("UPDATE dbo.PAY_GETD SET N_KOL2 = NULL, N_MOIN2 = NULL, N_TAF2 = NULL, HES2 = NULL WHERE ID = @id", new { id = c.Id }, tx);
            await LogReceivedAsync(conn, tx, c.NSeri, c.Bank, c.DateS, 1, c.Sandugh, userName, setVaz: true);
        }

        private static Task ReleaseReturnPaidAsync(IDbConnection conn, IDbTransaction tx, TreasuryChequeDto c)
            => conn.ExecuteAsync("UPDATE dbo.PAY_GETP SET N_KOL2 = NULL, N_MOIN2 = NULL, N_TAF2 = NULL, HES2 = NULL WHERE ID = @id", new { id = c.Id }, tx);

        /// <summary>
        /// برگشتِ چکِ دریافتی به صاحبش — BAKCHEK (ON_Close):
        /// به‌حساب = صاحبِ چک (پیش‌فرض CUST_NO)، از‌حساب = ADA/ADV؛ اگر چک به شخصی واگذار شده بود،
        /// از‌حساب = همان شخص («از حساب این شخص کسر شده و صاحب چک بدهکار می‌گردد»).
        /// PAY_GETD.N_KOL2.. = به‌حساب، HES2، VAZ و موقعیت.
        /// </summary>
        private async Task<TreasurySaveResult> SaveReturnReceivedAsync(TreasuryListItemDto h, ExistingRow? old, TreasuryRowSaveRequest r,
                                                                       string userName, int userCo, string? clientIp, Release? release)
        {
            if (r.PickedChequeId is not { } pick) return Fail("چکِ برگشتی را از فهرست انتخاب کنید.");
            if (r.Vaz is not (>= 1 and <= 6)) return Fail("وضعیتِ چک را انتخاب کنید.");
            var s = await SazmanAsync();
            var current = await CurrentPickedAsync(old, false);
            var cheque = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(GetdSelect + " WHERE g.ID = @pick", new { pick })).FirstOrDefault();
            if (cheque is null) return Fail("چک پیدا نشد.");
            var available = ((cheque.Ns is null or 0) && cheque.NKol2 is null && cheque.NKol3 is null && cheque.NKol != 911) || cheque.Id == current?.Id;
            if (!available) return Fail("این چک قبلاً وصول یا برگشت شده است.");

            var kol = cheque.NKol2 is > 0 ? cheque.NKol2 : cheque.NKol;
            var assignedToPerson = kol is not null && kol != s.Bankha;
            string fhes;
            string? info = null;
            if (assignedToPerson)
            {
                fhes = (cheque.Hes1 ?? cheque.Hes2 ?? "").Trim();
                if (fhes.Length == 0) return Fail("این چک واگذار شده ولی حسابِ گیرنده‌اش ثبت نشده است.");
                info = "این چک قبلاً واگذار شده بود؛ از حسابِ همان شخص کسر و صاحبِ چک بدهکار شد.";
            }
            else fhes = cheque.Kind == 0 ? s.AdvOr : s.AdaOr;

            var thes = r.Thes?.Trim();
            if (string.IsNullOrEmpty(thes)) thes = cheque.CustNo?.Trim();
            if (string.IsNullOrEmpty(thes)) return Fail("«به حساب» (صاحبِ چک) را انتخاب کنید.");
            var t = SplitHes(thes);

            var d = new RowData
            {
                NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = cheque.Mabl, MhazNo = r.MhazNo,
                NSeri = cheque.NSeri, Bank = cheque.Bank,
                Sharh = Right255($"چك برگشتي {ChequeSerial(cheque.NSeri)}بانك {NormalizeFa(cheque.BankName)} {cheque.Shobeh} مورخ {FormatDate(cheque.DateS)}")
            };
            if (await ValidateAccountsAsync(d) is { } accErr) return Fail(accErr);
            if (t is null || !await TafExistsAsync(t)) return Fail($"تفصیلیِ [{thes}] تعریف نشده است.");
            var sandugh = r.Sandugh ?? cheque.Sandugh;

            var newIdh = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                if (release is not null) await release(conn, tx);
                if (current is not null && current.Id != cheque.Id)
                    await ReleaseReturnReceivedAsync(conn, tx, current, userName);

                await CopyToHistoryAsync(conn, tx, "PAY_GETD", "ID = @id", new { id = cheque.Id }, true, userName, clientIp);
                await conn.ExecuteAsync(@"
                    UPDATE dbo.PAY_GETD SET N_KOL2 = @k, N_MOIN2 = @m, N_TAF2 = @t, HES2 = @hes, VAZ = @vaz, SANDUGH = @sandugh
                    WHERE ID = @id", new { k = t[0], m = t[1], t = t[2], hes = thes, vaz = r.Vaz, sandugh, id = cheque.Id }, tx);

                var hasLog = await conn.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 1 FROM dbo.PAY_GETD_LOG WHERE N_SERI = @s AND BANK = @b AND DATE_S = @d AND VAZ = 5",
                    new { s = cheque.NSeri, b = cheque.Bank, d = cheque.DateS }, tx);
                if (hasLog is null)
                {
                    await LogReceivedAsync(conn, tx, cheque.NSeri, cheque.Bank, cheque.DateS, 5, sandugh, userName, setVaz: false);
                    await LogReceivedAsync(conn, tx, cheque.NSeri, cheque.Bank, cheque.DateS, r.Vaz!.Value, sandugh, userName, setVaz: false);
                }

                var rowId = await UpsertRowAsync(conn, tx, h.Id, h.Date, old?.Idh, d, userCo);
                await RebuildSanadAsync(conn, tx, h.Id);
                return rowId;
            });
            return new TreasurySaveResult { Ok = true, Id = h.Id, Idh = newIdh, Info = info };
        }

        /// <summary>
        /// برگشتِ چکِ پرداختیِ خودمان — BAKCHEKP: به‌حساب = APA (غیرتجاری: APV)، از‌حساب = طرفی که برگرداند؛
        /// PAY_GETP.N_KOL2.. = به‌حساب و VAZ (۱ نزد شخص، ۲ عودت شده).
        /// </summary>
        private async Task<TreasurySaveResult> SaveReturnPaidAsync(TreasuryListItemDto h, ExistingRow? old, TreasuryRowSaveRequest r,
                                                                   string userName, int userCo, string? clientIp, Release? release)
        {
            if (r.PickedChequeId is not { } pick) return Fail("چکِ پرداختیِ برگشتی را از فهرست انتخاب کنید.");
            if (r.Vaz is not (1 or 2)) return Fail("وضعیتِ چک را انتخاب کنید.");
            var s = await SazmanAsync();
            var current = await CurrentPickedAsync(old, true);
            var cheque = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(GetpSelect + " WHERE g.ID = @pick", new { pick })).FirstOrDefault();
            if (cheque is null) return Fail("چک پیدا نشد.");
            var available = ((cheque.Ns is null or 0) && cheque.NKol2 is null && cheque.NKol3 is null && cheque.NKol != 911) || cheque.Id == current?.Id;
            if (!available) return Fail("این چک قبلاً وصول یا برگشت شده است.");

            var thes = cheque.Kind == 0 ? s.ApvOr : s.ApaOr;
            var fhes = r.Fhes?.Trim() ?? "";
            if (fhes.Length == 0) return Fail("«از حساب» (طرفی که چک را برگرداند) را انتخاب کنید.");
            var t = SplitHes(thes);
            var d = new RowData
            {
                NoAm = r.NoAm, Nahva = r.Nahva, Fhes = fhes, Thes = thes, Mabl = cheque.Mabl, MhazNo = r.MhazNo,
                NSeri = cheque.NSeri, Bank = cheque.Bank,
                Sharh = Right255($" برگشت چك پرداختي {ChequeSerial(cheque.NSeri)}بانك{NormalizeFa(cheque.BankName)} {cheque.Shobeh} مورخ {FormatDate(cheque.DateS)}")
            };
            if (await ValidateAccountsAsync(d) is { } accErr) return Fail(accErr);
            if (t is null) return Fail("حسابِ اسناد پرداختنی در تنظیمات (SAZMAN.APA) درست تعریف نشده است.");

            var newIdh = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                if (release is not null) await release(conn, tx);
                if (current is not null && current.Id != cheque.Id)
                    await ReleaseReturnPaidAsync(conn, tx, current);

                await CopyToHistoryAsync(conn, tx, "PAY_GETP", "ID = @id", new { id = cheque.Id }, true, userName, clientIp);
                await conn.ExecuteAsync("UPDATE dbo.PAY_GETP SET N_KOL2 = @k, N_MOIN2 = @m, N_TAF2 = @t, VAZ = @vaz WHERE ID = @id",
                    new { k = t[0], m = t[1], t = t[2], vaz = r.Vaz, id = cheque.Id }, tx);

                var rowId = await UpsertRowAsync(conn, tx, h.Id, h.Date, old?.Idh, d, userCo);
                await RebuildSanadAsync(conn, tx, h.Id);
                return rowId;
            });
            return new TreasurySaveResult { Ok = true, Id = h.Id, Idh = newIdh };
        }

        // ─────────────────────────── حذفِ سطر ───────────────────────────

        /// <summary>
        /// DELETE_FACTOR22_Click: اول نسخه‌ی TR، بعد برای سطرِ چکی وضعیتِ چک برمی‌گردد —
        /// چکِ دریافتی/پرداختی روی حسابِ معلقِ ۹۱۱-۱-۱ پارک می‌شود؛ واگذاری و برگشت آزاد می‌شوند.
        /// </summary>
        public async Task<TreasurySaveResult> DeleteRowAsync(int id, int idh, UserPerms p, int userDept, string userName, string? clientIp = null)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            if (EditBlock(h, p, userName) is { } lr) return Fail(lr);
            var row = await RowAsync(idh);
            if (row is null || row.Id != id) return Fail("سطر پیدا نشد.");
            var s = await SazmanAsync();

            TreasuryChequeDto? cheque = null;
            if (TreasuryMethod.IsCheque(row.Nahva) && row.NSeri is not null && row.Bank is not null)
                cheque = await CurrentPickedAsync(row, PayableTable(row.NoAm, row.Nahva));

            if (ChequeLockedReason(row.NoAm, row.Nahva, cheque, s.Bankha) is { } why)
                return Fail($"چکی که {why} قابل حذف نیست.");

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                await HistoryAsync(conn, tx, id, userName, clientIp);
                await ReleaseRowChequeAsync(conn, tx, row.NoAm, row.Nahva, cheque, userName);
                await conn.ExecuteAsync("DELETE FROM dbo.PGET_LST WHERE ID = @id AND IDH = @idh", new { id, idh }, tx);
                await RebuildSanadAsync(conn, tx, id);
                return 0;
            });
            return new TreasurySaveResult { Ok = true, Id = id };
        }

        /// <summary>خطایِ قابلِ نمایش به کاربر از داخلِ تراکنش (تراکنش برگردانده می‌شود).</summary>
        internal sealed class UserError : Exception
        {
            public UserError(string message) : base(message) { }
        }
    }
}
