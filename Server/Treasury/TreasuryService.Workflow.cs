using System.Data;
using ClosedXML.Excel;
using Dapper;
using Safir.Shared.Models.Treasury;
using Safir.Shared.Utility;

namespace Safir.Server.Treasury
{
    // ═══════════════════════════════════════════════════════════════════
    //  امضا، ارجاع، ضمیمه‌ی سطر، چاپ و خروجیِ اکسل — همان کارهایی که فرمِ
    //  PGET_HED ِ WPF با SGN1..3، PERSONEL، BTN_ATTACH، Command12/23/24 و
    //  EXPORTEXCEL می‌کند. همه روی پرونده‌ی اتوماسیونِ خزانه (TASKS/EVENTS با
    //  tg = 34) می‌نشینند تا در کارتابلِ اتوماسیون هم دیده شوند.
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class TreasuryService
    {
        public static readonly string[] SlotTitles = { "تنظیم کننده", "مدیر مالی", "مدیر عامل" };

        // ─────────────────────────── پرونده‌ی اتوماسیون ───────────────────────────

        /// <summary>
        /// Gettaskid(ID, 34). پرونده‌ی ضمیمه‌ی سطرها هم tg = 34 دارد (با num = IDH)؛ پرونده‌ی
        /// امضا/ارجاع skid = 34 دارد، پس آن اول می‌آید.
        /// </summary>
        private static Task<long?> TaskIdAsync(IDbConnection conn, IDbTransaction? tx, long num)
            => conn.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 IDNUM FROM dbo.TASKS WHERE num = @num AND tg = 34 ORDER BY CASE WHEN skid = 34 THEN 0 ELSE 1 END, IDNUM",
                new { num }, tx);

        private async Task<TreasuryTaskDto?> TaskAsync(int id)
        {
            var t = (await _db.DoGetDataSQLAsync<TreasuryTaskDto>(@"
                SELECT TOP 1 IDNUM Idnum, PERSONEL Personel, STATUS Status FROM dbo.TASKS
                WHERE num = @id AND tg = 34 ORDER BY CASE WHEN skid = 34 THEN 0 ELSE 1 END, IDNUM", new { id })).FirstOrDefault();
            if (t is null) return null;
            if (t.Personel is > 0)
                t.PersonelName = DecodeUser(await _db.DoGetDataSQLAsyncSingle<string?>("SELECT TOP 1 SAL_NAME FROM dbo.SALA_DTL WHERE IDD = @p", new { p = t.Personel }));
            t.Events = (await _db.DoGetDataSQLAsync<TreasuryEventDto>(@"
                SELECT TOP 30 CAST(EVENTS AS nvarchar(4000)) Text, USERNAME UserName, CAST(STDATE AS bigint) Date, STTIME Time
                FROM dbo.EVENTS WHERE IDNUM = @idnum ORDER BY IDD DESC", new { idnum = t.Idnum })).ToList();
            return t;
        }

        /// <summary>شرحِ پرونده — «خزانه داري   شماره: X مورخ yyyy/mm/dd  به نام: کاربر» (همان متنِ WPF).</summary>
        private static string TaskText(TreasuryListItemDto h, string userName)
            => $"خزانه داري   شماره: {h.Id} مورخ {FormatDate(h.Date)}  به نام: {userName}";

        private static async Task<long> EnsureTaskAsync(IDbConnection conn, IDbTransaction tx, TreasuryListItemDto h, int personel, string userName, int userCo)
        {
            if (await TaskIdAsync(conn, tx, h.Id) is { } mid) return mid;
            var hes = await conn.ExecuteScalarAsync<string?>("SELECT TOP 1 HES FROM dbo.SALA_DTL WHERE IDD = @userCo", new { userCo }, tx);
            return await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO dbo.TASKS (PERSONEL, USERNAME, TASK, COMP_COD, STDATE, STTIME, skid, num, tg, CTIM, USERCO)
                OUTPUT INSERTED.IDNUM
                VALUES (@personel, @user, @task, @comp, @d, @t, 34, @num, 34, GETDATE(), @userCo)",
                new { personel, user = Cut(userName, 50), task = TaskText(h, userName), comp = hes ?? "", d = Today(), t = NowTime(), num = h.Id, userCo }, tx);
        }

        private static Task AddEventAsync(IDbConnection conn, IDbTransaction tx, long mid, int id, string text, string userName)
            => conn.ExecuteAsync(@"
                INSERT INTO dbo.EVENTS (IDNUM, USERNAME, EVENTS, STDATE, STTIME, skid, num, tg)
                VALUES (@mid, @user, @text, @d, @t, 34, @id, 34)",
                new { mid, user = Cut(userName, 50), text, d = Today(), t = NowTime(), id }, tx);

        private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

        // ─────────────────────────── امضا ───────────────────────────

        /// <summary>
        /// SGN1_Click / SGN2_Click / SGN3_Click: فقط با مجوزِ همان ستون در SIGN. رویدادِ «امضا شد» یا
        /// «امضا برداشته شد» در پرونده؛ اگر پرونده هست به ثبت‌کننده‌اش برمی‌گردد (PERSONEL = USERCO)،
        /// وگرنه ساخته می‌شود. OKF روشن و sgnXusid = کاربر.
        /// </summary>
        public async Task<TreasurySaveResult> SignAsync(int id, TreasurySignRequest req, UserPerms p, int userDept, string userName, int userCo)
        {
            if (req.Slot is < 1 or > 3) return Fail("امضای نامعتبر.");
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            var perms = await SignPermsAsync(userCo);
            var allowed = req.Slot switch { 1 => perms.S1, 2 => perms.S2, _ => perms.S3 };
            if (!allowed) return Fail($"اجازه‌ی امضای «{SlotTitles[req.Slot - 1]}» را ندارید (جدول امضاها).");
            var now = req.Slot switch { 1 => h.Sgn1, 2 => h.Sgn2, _ => h.Sgn3 };
            if (now == req.On) return new TreasurySaveResult { Ok = true, Id = id };

            var mark = req.Slot == 1
                ? (req.On ? " :امضا شد1 " : " :امضا برداشته شد1:")
                : (req.On ? $":امضا شد{req.Slot} " : $":امضا برداشته شد{req.Slot}:");

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                var existing = await TaskIdAsync(conn, tx, id);
                var mid = existing ?? await EnsureTaskAsync(conn, tx, h, userCo, userName, userCo);
                await AddEventAsync(conn, tx, mid, id, userName + mark, userName);
                if (existing is not null)
                    await conn.ExecuteAsync("UPDATE dbo.TASKS SET PERSONEL = USERCO, STATUS = 1 WHERE IDNUM = @mid", new { mid }, tx);

                await conn.ExecuteAsync(
                    $"UPDATE dbo.PGET_HED SET SGN{req.Slot} = @on, sgn{req.Slot}usid = @userCo, OKF = 1 WHERE ID = @id",
                    new { on = req.On, userCo, id }, tx);
                return 0;
            });
            return new TreasurySaveResult { Ok = true, Id = id };
        }

        // ─────────────────────────── ارجاع ───────────────────────────

        /// <summary>
        /// PERSONEL_AfterUpdate → PERSONELUpdate(34, ID, per, …): پرونده به آن شخص می‌رود (یا با او
        /// ساخته می‌شود) و رویدادِ «ارجاع شد به : X» ثبت می‌شود.
        /// </summary>
        public async Task<TreasurySaveResult> ReferAsync(int id, TreasuryReferRequest req, UserPerms p, int userDept, string userName, int userCo)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            var target = await _db.DoGetDataSQLAsyncSingle<string?>(
                "SELECT TOP 1 SAL_NAME FROM dbo.SALA_DTL WHERE IDD = @p AND ENABL = 0", new { p = req.Personel });
            if (target is null) return Fail("کاربرِ ارجاع پیدا نشد یا غیرفعال است.");
            var targetName = DecodeUser(target);

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                var existing = await TaskIdAsync(conn, tx, id);
                long mid;
                if (existing is { } m)
                {
                    mid = m;
                    await conn.ExecuteAsync("UPDATE dbo.TASKS SET PERSONEL = @per, STATUS = 1 WHERE IDNUM = @mid", new { per = req.Personel, mid }, tx);
                }
                else mid = await EnsureTaskAsync(conn, tx, h, req.Personel, userName, userCo);
                await AddEventAsync(conn, tx, mid, id, "ارجاع شد به  : " + targetName, userName);
                return 0;
            });
            return new TreasurySaveResult { Ok = true, Id = id, Info = $"ارجاع داده شد به {targetName}." };
        }

        // ─────────────────────────── ضمیمه‌ی سطر (تصویر چک) ───────────────────────────

        public static readonly string[] AttachmentExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        public async Task<(byte[]? Bytes, string? Ext)> GetAttachmentAsync(int id, int idh, UserPerms p, int userDept, string userName)
        {
            if (await HeaderAsync(id, p, userDept, userName) is null) return (null, null);
            var row = await RowAsync(idh);
            if (row is null || row.Id != id) return (null, null);
            var r = (await _db.DoGetDataSQLAsync<(byte[]? Pic, string? Ext)>(@"
                SELECT TOP 1 e.pic, e.FXTYPE FROM dbo.TASKS t JOIN dbo.EVENTS e ON e.IDNUM = t.IDNUM
                WHERE t.num = @idh AND t.tg = 34 AND e.pic IS NOT NULL ORDER BY e.IDD DESC", new { idh })).FirstOrDefault();
            return (r.Pic, r.Ext);
        }

        /// <summary>
        /// BTN_ATTACH_Click: پرونده‌ی سطر (tg = 34، num = IDH، skid = شماره‌ی خزانه) و رویدادی با تصویر —
        /// اگر رویدادِ تصویردار هست همان به‌روز می‌شود.
        /// </summary>
        public async Task<TreasurySaveResult> SaveAttachmentAsync(int id, int idh, byte[] bytes, string ext, UserPerms p, int userDept,
                                                                  string userName, int userCo)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            var row = (await _db.DoGetDataSQLAsync<TreasuryRowDto>(@"
                SELECT p.IDH Idh, p.ID Id, p.RADIF Radif, p.NO_AM NoAm, CAST(p.NAHVA AS int) Nahva, p.FHES Fhes, p.THES Thes,
                       CAST(cf.NAME AS nvarchar(300)) FhesName, CAST(ct.NAME AS nvarchar(300)) ThesName, p.SHARH Sharh, p.MABL Mabl
                FROM dbo.PGET_LST p
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = p.FHES) cf
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = p.THES) ct
                WHERE p.IDH = @idh", new { idh })).FirstOrDefault();
            if (row is null || row.Id != id) return Fail("سطر پیدا نشد.");
            if (bytes.Length == 0) return Fail("فایل خالی است.");
            if (bytes.Length > 10 * 1024 * 1024) return Fail("حجمِ تصویر نباید بیشتر از ۱۰ مگابایت باشد.");
            ext = ext.ToLowerInvariant();
            if (!AttachmentExtensions.Contains(ext)) return Fail("فقط تصویر (jpg، png، bmp) پذیرفته می‌شود.");

            var op = row.NoAm == 1 ? "دریافت" : row.NoAm == 2 ? "پرداخت" : "نامشخص";
            var nahva = row.Nahva switch { 1 => "نقد", 2 => "چک", 3 => "سایر", 4 => "واگذاری", 5 => "برگشتی", 6 => "مسترد", _ => "نامشخص" };
            var text = $"تصویر چک خزانه {id} ردیف {(row.Radif ?? idh):0} | {op} - {nahva} - از: {row.FhesName ?? row.Fhes} - به: {row.ThesName ?? row.Thes} - شرح: {row.Sharh ?? "-"} - مبلغ: {row.Mabl:N0}";

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                var taskId = await conn.ExecuteScalarAsync<long?>("SELECT TOP 1 IDNUM FROM dbo.TASKS WHERE num = @idh AND tg = 34", new { idh }, tx);
                taskId ??= await conn.ExecuteScalarAsync<long>(@"
                    INSERT INTO dbo.TASKS (PERSONEL, TASK, PERIORITY, STATUS, STDATE, STTIME, USERNAME, COMP_COD, skid, num, tg, CTIM, USERCO, SEE)
                    OUTPUT INSERTED.IDNUM
                    VALUES (@userCo, @task, 2, 1, @d, @t, @user, @comp, @id, @idh, 34, GETDATE(), @userCo, 0)",
                    new
                    {
                        userCo, task = $"تصویر چک خزانه {id} مورخ {FormatDate(h.Date)}  ردیف {(row.Radif ?? idh):0}",
                        d = Today(), t = NowTime(), user = Cut(userName, 50), comp = Cut(row.Fhes ?? "", 50), id, idh
                    }, tx);

                var eventId = await conn.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 IDD FROM dbo.EVENTS WHERE IDNUM = @taskId AND tg = 34 AND pic IS NOT NULL ORDER BY IDD DESC", new { taskId }, tx);
                var args = new { taskId, text, d = Today(), t = NowTime(), user = Cut(userName, 50), id, idh, ext, pic = bytes, eventId };
                if (eventId is null)
                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.EVENTS (IDNUM, EVENTS, STDATE, STTIME, USERNAME, SUMTIME, skid, num, tg, FXTYPE, pic)
                        VALUES (@taskId, @text, @d, @t, @user, 0, @id, @idh, 34, @ext, @pic)", args, tx);
                else
                    await conn.ExecuteAsync(@"
                        UPDATE dbo.EVENTS SET EVENTS = @text, STDATE = @d, STTIME = @t, USERNAME = @user, SUMTIME = 0,
                               skid = @id, num = @idh, tg = 34, FXTYPE = @ext, pic = @pic
                        WHERE IDNUM = @taskId AND IDD = @eventId", args, tx);
                return 0;
            });
            return new TreasurySaveResult { Ok = true, Id = id, Idh = idh };
        }

        /// <summary>«حذف تصویر سطر» (MenuItem_Click): pic ِ آخرین رویدادِ تصویردار خالی می‌شود.</summary>
        public async Task<TreasurySaveResult> DeleteAttachmentAsync(int id, int idh, UserPerms p, int userDept, string userName)
        {
            if (await HeaderAsync(id, p, userDept, userName) is null) return Fail("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            var row = await RowAsync(idh);
            if (row is null || row.Id != id) return Fail("سطر پیدا نشد.");
            var n = await _db.DoExecuteSQLAsync(@"
                UPDATE dbo.EVENTS SET pic = NULL
                WHERE IDD = (SELECT TOP 1 e.IDD FROM dbo.TASKS t JOIN dbo.EVENTS e ON e.IDNUM = t.IDNUM
                             WHERE t.num = @idh AND t.tg = 34 AND e.num = @idh AND e.pic IS NOT NULL ORDER BY e.IDD DESC)", new { idh });
            return n > 0 ? new TreasurySaveResult { Ok = true, Id = id, Idh = idh } : Fail("تصویری برای این سطر یافت نشد.");
        }

        // ─────────────────────────── چاپ ───────────────────────────

        private sealed class SignerRow
        {
            public string? Title { get; set; }
            public string? AccountName { get; set; }
            public bool HasImage { get; set; }
        }

        /// <summary>
        /// Command12 / Command23 / Command24. با SAZMAN.SIGN چاپ فقط بعد از دستِ‌کم یک امضا مجاز است
        /// (Baseknow.SIGN در Form_Current). «عملکرد» در WPF با IDK فیلتر می‌شد (که بینِ نوع‌های برگه
        /// تکراری است)؛ اینجا با شماره‌ی خزانه.
        /// </summary>
        public async Task<(TreasuryPrintDto? Doc, string? Error)> PrintAsync(int id, string kind, UserPerms p, int userDept, string userName)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null) return (null, "خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.");
            var s = await SazmanAsync();
            if (s.Sign && !h.Signed) return (null, "برای چاپ، خزانه باید دستِ‌کم یک امضا داشته باشد.");

            var company = (await _db.DoGetDataSQLAsync<(string? Name, string? Year)>("SELECT TOP 1 NAME, WIDTH_D FROM dbo.SAZMAN")).FirstOrDefault();
            var doc = new TreasuryPrintDto
            {
                Kind = kind, Id = h.Id, Idk = h.Idk, Date = h.Date, Molah = h.Molah, Ns = h.Ns, UserName = h.UserName,
                CompanyName = NormalizeFa(company.Name), FiscalYear = company.Year, PrintedOn = Today(),
                SanadBase = h.Ns is null ? null : await _db.DoGetDataSQLAsyncSingle<int?>("SELECT TOP 1 base FROM dbo.DEED_HED WHERE N_S = @ns", new { ns = h.Ns })
            };

            switch (kind)
            {
                case "daryaft":
                case "pardakht":
                    doc.Title = kind == "daryaft" ? "سند دریافت" : "سند پرداخت";
                    var view = kind == "daryaft" ? "sanaddar_sub" : "sanadpar_sub";
                    doc.Rows = (await _db.DoGetDataSQLAsync<TreasuryPrintRow>($@"
                        SELECT v.RADIF Radif, v.N_SERI NSeri, CAST(v.N_HESAB AS nvarchar(200)) NHesab, v.DATE_S DateS,
                               CAST(v.NAMES AS nvarchar(200)) BankName, CAST(v.SHOBEH AS nvarchar(100)) Shobeh, v.MABL Mabl,
                               CAST(v.NAME AS nvarchar(300)) FromName, v.FHES FromHes, v.THES ToHes, v.SHARH Sharh
                        FROM dbo.{view} v WHERE v.ID = @id ORDER BY v.RADIF, v.IDH", new { id })).ToList();
                    break;
                default:
                    doc.Kind = "amalkard";
                    doc.Title = "صورت‌حساب عملکرد خزانه";
                    doc.Rows = (await _db.DoGetDataSQLAsync<TreasuryPrintRow>(@"
                        SELECT l.RADIF Radif, CAST(o.NAMES AS nvarchar(100)) Operation, CAST(k.NAMES AS nvarchar(100)) Method,
                               CAST(ISNULL(fk.NAME, '') + N'-' + ISNULL(fm.NAME, '') + N'-' + ISNULL(ft.NAME, '') AS nvarchar(600)) FromName, l.FHES FromHes,
                               CAST(ISNULL(tk.NAME, '') + N'-' + ISNULL(tm.NAME, '') + N'-' + ISNULL(tt.NAME, '') AS nvarchar(600)) ToName, l.THES ToHes,
                               l.SHARH Sharh, l.MABL Mabl, l.N_SERI NSeri
                        FROM dbo.PGET_LST l
                        LEFT JOIN dbo.TCOD_DPS o ON o.CODE = l.NO_AM
                        LEFT JOIN dbo.TCOD_DPSKIND k ON k.CODE = l.NAHVA
                        LEFT JOIN dbo.TOTA_HES fk ON fk.NUMBER = l.FHES_K
                        LEFT JOIN dbo.DETA_HES fm ON fm.N_KOL = l.FHES_K AND fm.NUMBER = l.FHES_M
                        LEFT JOIN dbo.TDETA_HES ft ON ft.N_KOL = l.FHES_K AND ft.NUMBER = l.FHES_M AND ft.TNUMBER = l.FHES_T
                        LEFT JOIN dbo.TOTA_HES tk ON tk.NUMBER = l.THES_K
                        LEFT JOIN dbo.DETA_HES tm ON tm.N_KOL = l.THES_K AND tm.NUMBER = l.THES_M
                        LEFT JOIN dbo.TDETA_HES tt ON tt.N_KOL = l.THES_K AND tt.NUMBER = l.THES_M AND tt.TNUMBER = l.THES_T
                        WHERE l.ID = @id ORDER BY l.RADIF, l.IDH", new { id })).ToList();
                    break;
            }
            foreach (var r in doc.Rows)
            {
                r.FromName = NormalizeFa(r.FromName);
                r.ToName = NormalizeFa(r.ToName);
                r.BankName = NormalizeFa(r.BankName);
            }
            doc.Total = doc.Rows.Sum(r => r.Mabl);
            doc.TotalInWords = doc.Total > 0 ? CL_HESABDARI.ALPHANUM(doc.Total) + " ریال" : null;

            var flags = new[] { (h.Sgn1, h.Sgn1User), (h.Sgn2, h.Sgn2User), (h.Sgn3, h.Sgn3User) };
            for (int i = 0; i < 3; i++)
            {
                if (!flags[i].Item1 || flags[i].Item2 is not > 0) continue;
                var col = $"SGN0{i + 1}34TX";
                var alt = $"SGN0{i + 1}37TX";
                var info = (await _db.DoGetDataSQLAsync<SignerRow>($@"
                    SELECT CAST(COALESCE(NULLIF(sg.{col}, N''), NULLIF(sg.{alt}, N'')) AS nvarchar(200)) Title,
                           CAST(ch.NAME AS nvarchar(300)) AccountName,
                           CAST(CASE WHEN sd.EMZA IS NULL THEN 0 ELSE 1 END AS bit) HasImage
                    FROM dbo.SALA_DTL sd
                    LEFT JOIN dbo.SIGN sg ON sg.USERCO = sd.IDD
                    OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = sd.HES) ch
                    WHERE sd.IDD = @u", new { u = flags[i].Item2 })).FirstOrDefault();
                doc.Signers.Add(new TreasuryPrintSigner
                {
                    Slot = i + 1,
                    Title = string.IsNullOrWhiteSpace(info?.Title) ? SlotTitles[i] : NormalizeFa(info.Title),
                    Name = NormalizeFa(info?.AccountName),
                    HasImage = info?.HasImage == true
                });
            }
            return (doc, null);
        }

        /// <summary>تصویرِ امضای امضاکننده‌ی یک خزانه (SALA_DTL.EMZA) — فقط اگر واقعاً امضا کرده.</summary>
        public async Task<byte[]?> SignatureImageAsync(int id, int slot, UserPerms p, int userDept, string userName)
        {
            var h = await HeaderAsync(id, p, userDept, userName);
            if (h is null || slot is < 1 or > 3) return null;
            var (on, user) = slot switch { 1 => (h.Sgn1, h.Sgn1User), 2 => (h.Sgn2, h.Sgn2User), _ => (h.Sgn3, h.Sgn3User) };
            if (!on || user is not > 0) return null;
            return ExtractImage(await _db.DoGetDataSQLAsyncSingle<byte[]?>("SELECT TOP 1 EMZA FROM dbo.SALA_DTL WHERE IDD = @user", new { user }));
        }

        /// <summary>
        /// SALA_DTL.EMZA بیشتر «OLE Object» ِ Access است (سربرگِ 0x151C + «Bitmap Image / Paint.Picture»)
        /// و خودِ BMP داخلش است؛ بعضی هم PNG/JPEG ِ خام‌اند. تصویرِ قابلِ نمایش را برمی‌گرداند.
        /// </summary>
        public static byte[]? ExtractImage(byte[]? b)
        {
            if (b is null || b.Length < 8) return null;
            if (ImageContentType(b) != "application/octet-stream") return b;
            for (int i = 0; i < b.Length - 16; i++)
            {
                // PNG / JPEG داخلِ بسته
                if (b[i] == 0x89 && b[i + 1] == 0x50 && b[i + 2] == 0x4E && b[i + 3] == 0x47) return b[i..];
                if (b[i] == 0xFF && b[i + 1] == 0xD8 && b[i + 2] == 0xFF) return b[i..];
                // BMP: «BM» + اندازه‌ی کل (little-endian) که در باقیِ بسته جا شود
                if (b[i] == 0x42 && b[i + 1] == 0x4D)
                {
                    var size = BitConverter.ToInt32(b, i + 2);
                    var dataOffset = BitConverter.ToInt32(b, i + 10);
                    if (size > 26 && size <= b.Length - i && dataOffset > 0 && dataOffset < size) return b[i..(i + size)];
                }
            }
            return null;
        }

        public static string ImageContentType(byte[] b)
        {
            if (b.Length > 3 && b[0] == 0x89 && b[1] == 0x50) return "image/png";
            if (b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8) return "image/jpeg";
            if (b.Length > 2 && b[0] == 0x42 && b[1] == 0x4D) return "image/bmp";
            if (b.Length > 3 && b[0] == 0x47 && b[1] == 0x49) return "image/gif";
            return "application/octet-stream";
        }

        // ─────────────────────────── اکسل ───────────────────────────

        /// <summary>EXPORTEXCEL_BTN — سطرهای خزانه با نام حساب‌ها، راست‌به‌چپ.</summary>
        public async Task<byte[]?> ExcelAsync(int id, UserPerms p, int userDept, string userName)
        {
            var d = await GetAsync(id, p, userDept, userName);
            if (d is null) return null;
            var ops = new Dictionary<int, string> { [1] = "دریافت", [2] = "پرداخت" };
            var methods = new Dictionary<int, string> { [1] = "نقد", [2] = "چک", [3] = "سایر", [4] = "واگذاری چک", [5] = "برگشت چک", [6] = "چک غیرتجاری" };

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add($"خزانه {id}");
            ws.RightToLeft = true;
            string[] head = { "ردیف", "نوع عملیات", "نحوه", "از حساب", "نام از حساب", "به حساب", "نام به حساب", "شرح", "مبلغ", "سریال چک", "بانک", "سررسید" };
            for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
            var hr = ws.Range(1, 1, 1, head.Length);
            hr.Style.Font.Bold = true;
            hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");

            int row = 2;
            foreach (var r in d.Rows)
            {
                ws.Cell(row, 1).Value = r.Radif ?? 0;
                ws.Cell(row, 2).Value = ops.GetValueOrDefault(r.NoAm, r.NoAm.ToString());
                ws.Cell(row, 3).Value = methods.GetValueOrDefault(r.Nahva, r.Nahva.ToString());
                ws.Cell(row, 4).Value = r.Fhes;
                ws.Cell(row, 5).Value = NormalizeFa(r.FhesName);
                ws.Cell(row, 6).Value = r.Thes;
                ws.Cell(row, 7).Value = NormalizeFa(r.ThesName);
                ws.Cell(row, 8).Value = r.Sharh;
                ws.Cell(row, 9).Value = r.Mabl;
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
                if (r.NSeri is { } sr) ws.Cell(row, 10).Value = sr;
                ws.Cell(row, 11).Value = NormalizeFa(r.BankName);
                if (r.Cheque is { } c) ws.Cell(row, 12).Value = FormatDate(c.DateS);
                row++;
            }
            ws.Cell(row, 8).Value = "جمع";
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 9).FormulaA1 = $"SUM(I2:I{Math.Max(2, row - 1)})";
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 9).Style.Font.Bold = true;
            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
