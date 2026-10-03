using System.Data;
using Dapper;
using Safir.Server.Treasury;
using Safir.Shared.Models.Sanad;
using Safir.Shared.Models.Treasury;
using ChequeRole = Safir.Shared.Models.Sanad.SanadChequeRole;

namespace Safir.Server.Sanad
{
    // ═══════════════════════════════════════════════════════════════════
    //  ردیف‌های سند — Child14_CellEditEnding / Child14_RowEditEnding / CmdSaveRecord
    //  و پنجره‌های SGETCHEK / SPAYCHEK ِ DEED_HEAD.
    //
    //  اعتبارسنجی همان WPF است: حساب در CUST_HESAB و آخرین سطح (ISTAF)، تفصیلی در
    //  TDETA_HES (کلیدِ خارجیِ DEED_DTL)، دقیقاً یکی از بدهکار/بستانکار، شرح ≤ ۲۵۰.
    //  ردیفِ بدهکارِ ADA/ADV چکِ دریافتی و ردیفِ بستانکارِ APA/APV چکِ پرداختی
    //  می‌گیرد (اختیاری — مثل «خروج» از پنجره‌ی چک). چک و ردیف در یک تراکنش.
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class SanadService
    {
        internal static ChequeRole RoleOf(string? hes, double bed, double bes, TreasuryService.SazmanRow s)
            => SanadRules.RoleOf(hes, bed, bes, s.AdaOr, s.AdvOr, s.ApaOr, s.ApvOr);

        /// <summary>همان شکلِ سطرِ خزانه — برای قواعدِ مشترکِ قفل و آزاد کردنِ چک.</summary>
        internal static (int NoAm, int Nahva) TreasuryShape(ChequeRole role)
            => role == ChequeRole.Paid ? (TreasuryOp.Payment, TreasuryMethod.Cheque) : (TreasuryOp.Receipt, TreasuryMethod.Cheque);

        /// <summary>چکِ یک ردیف — همان سریال و بانک، اول آن که مبلغش با ردیف یکی است.</summary>
        internal async Task<TreasuryChequeDto?> RowChequeAsync(ChequeRole role, double nSeri, int bank, double amount)
        {
            var inner = (role == ChequeRole.Paid ? TreasuryService.GetpSelect : TreasuryService.GetdSelect) + " WHERE g.N_SERI = @s AND g.BANK = @b";
            var c = (await _db.DoGetDataSQLAsync<TreasuryChequeDto>(
                $"SELECT TOP 1 * FROM ({inner}) x ORDER BY CASE WHEN x.Mabl = @m THEN 0 ELSE 1 END, x.DateS DESC",
                new { s = nSeri, b = bank, m = amount })).FirstOrDefault();
            if (c is not null) c.BankName = TreasuryService.NormalizeFa(c.BankName);
            return c;
        }

        /// <summary>
        /// شرحِ ردیفِ چکی — SGETCHEK: «چك{سریال}بانك{بانک} {شعبه} مورخ {سررسید}-{نام}»؛
        /// SPAYCHEK همان با فاصله بعد از «چك» و «بانك».
        /// </summary>
        public static string AutoSharh(ChequeRole role, double serial, string? bankName, string? shobeh, long dateS, string? nameTah)
        {
            var sr = TreasuryService.ChequeSerial(serial);
            var text = role == ChequeRole.Paid
                ? $"چك {sr}بانك {bankName} {shobeh?.Trim()} مورخ {TreasuryService.FormatDate(dateS)}-{nameTah?.Trim()}"
                : $"چك{sr}بانك{bankName} {shobeh?.Trim()} مورخ {TreasuryService.FormatDate(dateS)}-{nameTah?.Trim()}";
            return TreasuryService.Left255(text);
        }

        internal sealed class ExistingRow
        {
            public long Id { get; set; }
            public double Ns { get; set; }
            public string Hes { get; set; } = "";
            public double Bed { get; set; }
            public double Bes { get; set; }
            public double? NSeri { get; set; }
            public int? Bank { get; set; }
            public string? Sharh { get; set; }
        }

        private const string ExistingSelect = @"
            SELECT id Id, N_S Ns, HES Hes, ISNULL(BED, 0) Bed, ISNULL(BES, 0) Bes, N_SERI NSeri, BANK Bank, SHARH Sharh
            FROM dbo.DEED_DTL";

        internal async Task<List<ExistingRow>> RowsForWriteAsync(double ns)
            => (await _db.DoGetDataSQLAsync<ExistingRow>(ExistingSelect + " WHERE N_S = @ns", new { ns })).ToList();

        private async Task<ExistingRow?> RowAsync(long id)
            => (await _db.DoGetDataSQLAsync<ExistingRow>(ExistingSelect + " WHERE id = @id", new { id })).FirstOrDefault();

        // ─────────────────────────── اعتبارسنجیِ ردیف ───────────────────────────

        /// <summary>یک ردیفِ آماده‌ی نوشتن در DEED_DTL.</summary>
        internal sealed class RowWrite
        {
            public string Hes = "";
            public int[] P = Array.Empty<int>();
            public string Sharh = "";
            public double Bed, Bes;
            public int? MhazNo;
        }

        /// <summary>
        /// Child14_RowEditEnding: «حساب به درستی انتخاب نشده»، «حساب کل نمی‌تواند خالی باشد»، ISTAF،
        /// «بدهکار یا بستانکار نمی‌تواند خالی باشد»، «بدهكار و بستانكار سند صحيح نمي باشد»، شرح ≤ ۲۵۰.
        /// </summary>
        internal async Task<(RowWrite? Row, string? Error)> ValidateRowAsync(SanadRowSaveRequest r)
        {
            var hes = Clean(r.Hes);
            if (hes.Length == 0) return (null, "حساب به درستی انتخاب نشده.");
            var p = TreasuryService.SplitHes(hes);
            if (p is null) return (null, $"کد حساب [{hes}] درست نیست.");
            if (await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 1 FROM dbo.CUST_HESAB WHERE hes = @hes", new { hes }) is null)
                return (null, $"حسابِ [{hes}] در سیستم وجود ندارد.");
            if (!await _trs.TafExistsAsync(p)) return (null, $"تفصیلیِ حساب [{p[0]}-{p[1]}-{p[2]}] تعریف نشده است.");
            if ((await GroupAccountsAsync(new[] { hes })).Count > 0)
                return (null, "حساب مورد نظر دارای تفضیلی می‌باشد؛ باید تفضیلیِ آن را انتخاب کنید!");

            if (SanadRules.AmountError(r.Bed, r.Bes) is { } ae) return (null, ae);

            var sharh = Clean(r.Sharh);
            if (sharh.Length > 250) return (null, "طول شرح حداکثر می‌تواند ۲۵۰ نویسه باشد.");
            if (r.MhazNo is not null &&
                await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 MHAZ_NO FROM dbo.TCOD_MARKAZHAZ WHERE MHAZ_NO = @m", new { m = r.MhazNo }) is null)
                return (null, "مرکز هزینه‌ی انتخاب‌شده معتبر نیست.");

            return (new RowWrite { Hes = hes, P = p, Sharh = sharh, Bed = r.Bed, Bes = r.Bes, MhazNo = r.MhazNo }, null);
        }

        private static object? Lvl(int[] a, int i) => a[i] == int.MinValue ? null : a[i];

        private static async Task<long> InsertRowAsync(IDbConnection conn, IDbTransaction tx, double ns, RowWrite w, double? nSeri, int? bank, int userCo)
            => await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO dbo.DEED_DTL (N_S, HES_K, HES_M, HES_T, HES_T2, HES_T3, HES_T4, HES, SHARH, BED, BES, N_SERI, BANK, ARZD, MHAZ_NO, UID)
                OUTPUT INSERTED.id
                VALUES (@ns, @k, @m, @t, @t2, @t3, @t4, @hes, @sharh, @bed, @bes, @nSeri, @bank, 1, @mhaz, @uid)",
                new
                {
                    ns, k = w.P[0], m = w.P[1], t = w.P[2], t2 = Lvl(w.P, 3), t3 = Lvl(w.P, 4), t4 = Lvl(w.P, 5),
                    hes = w.Hes, sharh = w.Sharh, bed = w.Bed, bes = w.Bes, nSeri, bank, mhaz = w.MhazNo, uid = userCo
                }, tx);

        // ─────────────────────────── ثبت/اصلاحِ ردیف ───────────────────────────

        public async Task<SanadSaveResult> SaveRowAsync(double ns, long? rowId, SanadRowSaveRequest r, string userName, int userCo, string? clientIp)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (LockReason(h) is { } lr) return Fail(lr);

            var (w, err) = await ValidateRowAsync(r);
            if (w is null) return Fail(err!);

            ExistingRow? old = null;
            if (rowId is not null)
            {
                old = await RowAsync(rowId.Value);
                if (old is null || old.Ns != ns) return Fail("ردیف پیدا نشد.");
            }

            var s = await _trs.SazmanAsync();
            var role = RoleOf(w.Hes, w.Bed, w.Bes, s);
            var c = r.Cheque;
            if (c is not null && role == ChequeRole.None)
                return Fail("فقط ردیفِ بدهکارِ اسناد دریافتنی یا بستانکارِ اسناد پرداختنی چک می‌گیرد.");

            var oldRole = old is null ? ChequeRole.None : RoleOf(old.Hes, old.Bed, old.Bes, s);
            var oldCheque = old is { NSeri: not null, Bank: not null } && oldRole != ChequeRole.None
                ? await RowChequeAsync(oldRole, old.NSeri!.Value, old.Bank!.Value, old.Bed + old.Bes) : null;
            var keepCheque = oldCheque is not null && c is not null && role == oldRole;

            // چکِ قبلی دیگر به این ردیف نمی‌خورد (حساب یا طرفِ ردیف عوض شد، یا چک برداشته شد) — مثلِ حذفِ سطر آزاد می‌شود
            Func<IDbConnection, IDbTransaction, Task>? release = null;
            if (oldCheque is not null && !keepCheque)
            {
                var (noAm, nahva) = TreasuryShape(oldRole);
                if (TreasuryService.ChequeLockedReason(noAm, nahva, oldCheque, s.Bankha) is { } why)
                    return Fail($"چکِ این ردیف {why}؛ حساب، مبلغ و چکش دیگر از اینجا عوض نمی‌شود.");
                release = (conn, tx) => TreasuryService.ReleaseRowChequeAsync(conn, tx, noAm, nahva, oldCheque, userName);
            }

            // ── مشخصاتِ چکِ تازه/اصلاح‌شده ──
            int kind = 0, sandugh = 1;
            int[]? hes1 = null;
            string? hes1Text = null, owner = null, bankName = null;
            var writeCheque = c is not null;
            if (c is not null)
            {
                var receive = role == ChequeRole.Received;
                var errors = await _trs.ValidateChequeInputAsync(c, h.Date, receive, group: false);
                if (errors.Count > 0) return Fail(string.Join("\n", errors.Distinct()));
                var amount = w.Bed + w.Bes;
                bankName = await _trs.BankNameAsync(c.Bank);

                if (receive)
                {
                    kind = w.Hes == s.AdaOr ? 1 : 0;
                    var ada = TreasuryService.SplitHes(s.AdaOr);
                    sandugh = c.Sandugh ?? 1;
                    if (ada is not null && !await _trs.TafExistsAsync(new[] { ada[0], ada[1], sandugh }))
                        return Fail("موقعیت چک (صندوق) معتبر نیست.");
                    hes1Text = c.Hes1?.Trim();
                    if (!string.IsNullOrEmpty(hes1Text) && hes1Text != "911-1-1")
                    {
                        hes1 = TreasuryService.SplitHes(hes1Text);
                        if (hes1 is null || hes1[0] != s.Bankha) return Fail("چک در این بخش فقط به بانک قابل واگذاری می‌باشد.");
                        if (!await _trs.TafExistsAsync(hes1)) return Fail($"حسابِ بانکیِ [{hes1Text}] تعریف نشده است.");
                    }
                    else hes1Text = null;
                    owner = Clean(r.ChequeOwner);
                    if (owner.Length > 0 &&
                        await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 1 FROM dbo.CUST_HESAB WHERE hes = @owner", new { owner }) is null)
                        return Fail($"حسابِ صاحبِ چک [{owner}] در سیستم وجود ندارد.");

                    if (keepCheque && oldCheque!.InCirculation(s.Bankha)
                        && (oldCheque.NSeri != c.NSeri || oldCheque.Bank != c.Bank || oldCheque.DateS != c.DateS || oldCheque.Mabl != amount || oldCheque.Kind != kind))
                        return Fail("این چک واگذار، وصول یا برگشت شده؛ سریال، بانک، سررسید، مبلغ و تجاری‌بودنش دیگر از اینجا عوض نمی‌شود.");
                }
                else
                {
                    kind = w.Hes == s.ApaOr ? 1 : 0;
                    hes1Text = c.Hes1?.Trim();
                    if (!string.IsNullOrEmpty(hes1Text) && hes1Text != "911-1-1")
                    {
                        hes1 = TreasuryService.SplitHes(hes1Text);
                        if (hes1 is null || hes1[0] != s.Bankha) return Fail("«پرداخت از حساب» باید یکی از حساب‌های بانکی باشد.");
                        if (!await _trs.TafExistsAsync(hes1)) return Fail($"حسابِ بانکیِ [{hes1Text}] تعریف نشده است.");
                    }
                    else
                    {
                        // ApplyDefaultNKolFromBankha: اولین تفصیلیِ BANKHA
                        hes1 = s.Bankha is { } bk && await _trs.FirstTafAsync(bk) is { } first ? TreasuryService.SplitHes(first) : null;
                        hes1Text = "";
                    }
                    if (keepCheque && oldCheque!.InCirculation(s.Bankha))
                    {
                        if (oldCheque.NSeri != c.NSeri || oldCheque.Bank != c.Bank || oldCheque.DateS != c.DateS || oldCheque.Mabl != amount || oldCheque.Kind != kind)
                            return Fail("این چک وصول یا برگشت خورده؛ مشخصات و مبلغش دیگر از اینجا عوض نمی‌شود.");
                        writeCheque = false; // فقط شرح/مرکز هزینه عوض شده؛ چکِ در گردش دست نمی‌خورد
                    }
                }

                // شرح: خالی، یا هنوز همان شرحِ خودکارِ چکِ قبلی → از روی چک ساخته می‌شود
                var oldAuto = old is not null && oldCheque is not null && Clean(old.Sharh) ==
                              AutoSharh(oldRole, oldCheque.NSeri, oldCheque.BankName, oldCheque.Shobeh, oldCheque.DateS, oldCheque.NameTah);
                if (w.Sharh.Length == 0 || (oldAuto && w.Sharh == Clean(old!.Sharh)))
                    w.Sharh = AutoSharh(role, c.NSeri, bankName, c.Shobeh, c.DateS, c.NameTah);
            }

            // N_SERI/BANK ِ ردیف: از چک؛ ردیفِ قدیمیِ بی‌نقشِ چکی همان مقدارِ قبلی‌اش را نگه می‌دارد (WPF دست نمی‌زد)
            double? nSeri = c?.NSeri;
            int? bankCode = c?.Bank;
            if (c is null && old is not null && oldRole == ChequeRole.None && role == ChequeRole.None)
            {
                nSeri = old.NSeri;
                bankCode = old.Bank;
            }

            try
            {
                var (id, info) = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    if (release is not null) await release(conn, tx);
                    string? info = null;
                    if (c is not null && writeCheque)
                    {
                        if (role == ChequeRole.Received)
                        {
                            var daft = await TreasuryService.DaftAsync(conn, tx);
                            info = await TreasuryService.WriteReceivedChequeAsync(conn, tx, new TreasuryService.ReceivedChequeWrite
                            {
                                CurrentId = keepCheque ? oldCheque!.Id : null,
                                Serial = c.NSeri, Bank = c.Bank, DateS = c.DateS, Date = c.Date ?? h.Date, Shobeh = c.Shobeh,
                                Mabl = w.Bed, NameTah = c.NameTah!, NHesab = c.NHesab, CustNo = owner, ListNo = c.ListNo,
                                Kind = kind, Sandugh = sandugh, Sayadi = c.Sayadi, Hes1 = hes1, Hes1Text = hes1Text
                            }, daft, s.Bankha, userName, clientIp);
                        }
                        else
                        {
                            await TreasuryService.WritePaidChequeAsync(conn, tx, new TreasuryService.PaidChequeWrite
                            {
                                CurrentId = keepCheque ? oldCheque!.Id : null,
                                Serial = c.NSeri, Bank = c.Bank, DateS = c.DateS, Date = c.Date ?? h.Date, Shobeh = c.Shobeh,
                                Mabl = w.Bes, NameTah = c.NameTah!, NHesab = c.NHesab, Kind = kind, Sayadi = c.Sayadi,
                                Hes1 = hes1, Hes1Text = hes1Text
                            }, userName, clientIp);
                        }
                    }

                    long id;
                    if (old is null)
                        id = await InsertRowAsync(conn, tx, ns, w, nSeri, bankCode, userCo);
                    else
                    {
                        id = old.Id;
                        await conn.ExecuteAsync(@"
                            UPDATE dbo.DEED_DTL SET HES_K = @k, HES_M = @m, HES_T = @t, HES_T2 = @t2, HES_T3 = @t3, HES_T4 = @t4,
                                   HES = @hes, SHARH = @sharh, BED = @bed, BES = @bes, N_SERI = @nSeri, BANK = @bank, ARZD = 1, MHAZ_NO = @mhaz
                            WHERE id = @id AND N_S = @ns",
                            new
                            {
                                id, ns, k = w.P[0], m = w.P[1], t = w.P[2], t2 = Lvl(w.P, 3), t3 = Lvl(w.P, 4), t4 = Lvl(w.P, 5),
                                hes = w.Hes, sharh = w.Sharh, bed = w.Bed, bes = w.Bes, nSeri, bank = bankCode, mhaz = w.MhazNo
                            }, tx);
                    }
                    return (id, info);
                });
                return new SanadSaveResult { Ok = true, Ns = ns, RowId = id, Info = info };
            }
            catch (TreasuryService.UserError ue) { return Fail(ue.Message); }
            catch (Exception ex) when (TreasuryService.IsDuplicate(ex)) { return Fail("اطلاعات تکراری است: چکی با همین سریال، بانک و سررسید قبلاً ثبت شده است."); }
            catch (Exception ex) when (IsFk(ex)) { return Fail("حساب، بانک یا مرکزِ هزینه‌ی این ردیف در اطلاعاتِ پایه تعریف نشده است."); }
        }

        /// <summary>
        /// جای‌گذاریِ چند ردیف (Ctrl+V ِ WPF: ValidateDataGridRow برای هر ردیف). ردیف‌ها بی‌چک ثبت می‌شوند
        /// (هر چک یکتاست) و همه با هم: اگر یکی ایراد داشت هیچ‌کدام ثبت نمی‌شود و فهرستِ ایرادها برمی‌گردد.
        /// </summary>
        public async Task<SanadSaveResult> AddRowsAsync(double ns, List<SanadRowSaveRequest> rows, int userCo)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (LockReason(h) is { } lr) return Fail(lr);
            if (rows.Count == 0) return Fail("ردیفی برای جای‌گذاری نیست.");
            if (rows.Count > 500) return Fail("در هر بار حداکثر ۵۰۰ ردیف جای‌گذاری می‌شود.");

            var ready = new List<RowWrite>();
            var errors = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var (w, err) = await ValidateRowAsync(rows[i]);
                if (w is null) errors.Add($"ردیف {i + 1}: {err}");
                else ready.Add(w);
            }
            if (errors.Count > 0) return Fail(string.Join("\n", errors.Take(10)) + (errors.Count > 10 ? $"\n… و {errors.Count - 10} ایرادِ دیگر" : ""));

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                foreach (var w in ready) await InsertRowAsync(conn, tx, ns, w, null, null, userCo);
                return 0;
            });
            return new SanadSaveResult { Ok = true, Ns = ns, Count = ready.Count };
        }

        // ─────────────────────────── حذفِ ردیف ───────────────────────────

        /// <summary>
        /// DELETE_Click / SANAD_Row_Deleter: اول نسخه‌ی سابقه (TR)، بعد ردیف؛ چکِ ردیف مثلِ حذفِ سطرِ
        /// خزانه برمی‌گردد (WPF آن را «نزد صندوق» جا می‌گذاشت). چکِ در گردش حذف نمی‌شود.
        /// </summary>
        public async Task<SanadSaveResult> DeleteRowAsync(double ns, long rowId, string userName, string? clientIp)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (LockReason(h) is { } lr) return Fail(lr);
            var old = await RowAsync(rowId);
            if (old is null || old.Ns != ns) return Fail("ردیف پیدا نشد.");

            var s = await _trs.SazmanAsync();
            var role = RoleOf(old.Hes, old.Bed, old.Bes, s);
            var cheque = role != ChequeRole.None && old is { NSeri: not null, Bank: not null }
                ? await RowChequeAsync(role, old.NSeri!.Value, old.Bank!.Value, old.Bed + old.Bes) : null;
            if (cheque is not null)
            {
                var (noAm, nahva) = TreasuryShape(role);
                if (TreasuryService.ChequeLockedReason(noAm, nahva, cheque, s.Bankha) is { } why)
                    return Fail($"چکی که {why} قابل حذف نیست.");
            }

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                await HistoryAsync(conn, tx, ns, userName, clientIp);
                if (cheque is not null)
                {
                    var (noAm, nahva) = TreasuryShape(role);
                    await TreasuryService.ReleaseRowChequeAsync(conn, tx, noAm, nahva, cheque, userName);
                }
                await conn.ExecuteAsync("DELETE FROM dbo.DEED_DTL WHERE id = @rowId AND N_S = @ns", new { rowId, ns }, tx);
                return 0;
            });
            return new SanadSaveResult { Ok = true, Ns = ns };
        }
    }
}
