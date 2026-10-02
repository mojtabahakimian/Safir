using System.Data;
using ClosedXML.Excel;
using Dapper;
using Safir.Server.Treasury;
using Safir.Shared.Models.Sanad;
using Safir.Shared.Models.Treasury;
using ChequeRole = Safir.Shared.Models.Sanad.SanadChequeRole;
using Safir.Shared.Utility;

namespace Safir.Server.Sanad
{
    // ═══════════════════════════════════════════════════════════════════
    //  امضا (SGN1..3)، ارجاع (PERSONEL)، چاپ (Command22 / Command3) و خروجیِ اکسلِ
    //  DEED_HEAD. پرونده‌ی اتوماسیونِ سند: TASKS با tg = 0 و num = شماره‌ی مبنا
    //  (Gettaskid(BASE, 0)) — همان که کارتابلِ WPF با MenuBaseOnKindOpen(0, …) باز می‌کند.
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class SanadService
    {
        private static Task<long?> TaskIdAsync(IDbConnection conn, IDbTransaction? tx, int @base)
            => conn.ExecuteScalarAsync<long?>("SELECT TOP 1 IDNUM FROM dbo.TASKS WHERE num = @base AND tg = 0 ORDER BY IDNUM", new { @base }, tx);

        private async Task<TreasuryTaskDto?> TaskAsync(int @base)
        {
            var t = (await _db.DoGetDataSQLAsync<TreasuryTaskDto>(@"
                SELECT TOP 1 IDNUM Idnum, PERSONEL Personel, STATUS Status FROM dbo.TASKS
                WHERE num = @base AND tg = 0 ORDER BY IDNUM", new { @base })).FirstOrDefault();
            if (t is null) return null;
            if (t.Personel is > 0)
                t.PersonelName = TreasuryService.DecodeUser(await _db.DoGetDataSQLAsyncSingle<string?>("SELECT TOP 1 SAL_NAME FROM dbo.SALA_DTL WHERE IDD = @p", new { p = t.Personel }));
            t.Events = (await _db.DoGetDataSQLAsync<TreasuryEventDto>(@"
                SELECT TOP 30 CAST(EVENTS AS nvarchar(4000)) Text, USERNAME UserName, CAST(STDATE AS bigint) Date, STTIME Time
                FROM dbo.EVENTS WHERE IDNUM = @idnum ORDER BY IDD DESC", new { idnum = t.Idnum })).ToList();
            return t;
        }

        /// <summary>شرحِ پرونده — «سند حسابداري شماره: N مورخ yyyy/mm/dd  به شرح: …» (همان متنِ WPF).</summary>
        public static string TaskText(SanadListItemDto h)
            => $"سند حسابداري شماره: {h.Ns:0} مورخ {TreasuryService.FormatDate(h.Date)}  به شرح: {h.Sharh}";

        private static async Task<long> EnsureTaskAsync(IDbConnection conn, IDbTransaction tx, SanadListItemDto h, int personel, string userName, int userCo)
        {
            if (await TaskIdAsync(conn, tx, h.Base) is { } mid) return mid;
            var hes = await conn.ExecuteScalarAsync<string?>("SELECT TOP 1 HES FROM dbo.SALA_DTL WHERE IDD = @userCo", new { userCo }, tx);
            return await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO dbo.TASKS (PERSONEL, USERNAME, TASK, COMP_COD, STDATE, STTIME, skid, num, tg, CTIM, USERCO)
                OUTPUT INSERTED.IDNUM
                VALUES (@personel, @user, @task, @comp, @d, @t, 0, @num, 0, GETDATE(), @userCo)",
                new { personel, user = Cut(userName, 50), task = TaskText(h), comp = hes ?? "", d = TreasuryService.Today(), t = TreasuryService.NowTime(), num = h.Base, userCo }, tx);
        }

        private static Task AddEventAsync(IDbConnection conn, IDbTransaction tx, long mid, int @base, string text, string userName)
            => conn.ExecuteAsync(@"
                INSERT INTO dbo.EVENTS (IDNUM, USERNAME, EVENTS, STDATE, STTIME, skid, num, tg)
                VALUES (@mid, @user, @text, @d, @t, 0, @base, 0)",
                new { mid, user = Cut(userName, 50), text, d = TreasuryService.Today(), t = TreasuryService.NowTime(), @base }, tx);

        private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

        // ─────────────────────────── امضا ───────────────────────────

        /// <summary>
        /// امضای بالاتر جلوی تغییرِ امضای پایین‌تر را می‌گیرد — Form_Current: با SGN3 «تنظیم‌کننده» و
        /// «مدیر مالی»، و با SGN2 «تنظیم‌کننده» قفل است.
        /// </summary>
        public static string? SlotBlock(int slot, bool sgn2, bool sgn3)
        {
            if (slot < 3 && sgn3) return "مدیر عامل امضا کرده؛ اول امضای او برداشته شود.";
            if (slot < 2 && sgn2) return "مدیر مالی امضا کرده؛ اول امضای او برداشته شود.";
            return null;
        }

        /// <summary>
        /// SGN1_Click / SGN2_Click / SGN3_Click: فقط با مجوزِ همان ستون (SND_*) در SIGN. رویدادِ «امضا شد»
        /// یا «امضا برداشته شد» در پرونده؛ اگر پرونده هست به ثبت‌کننده‌اش برمی‌گردد (PERSONEL = USERCO)،
        /// وگرنه ساخته می‌شود. OKF روشن و sgnXusid = کاربر.
        /// </summary>
        public async Task<SanadSaveResult> SignAsync(double ns, TreasurySignRequest req, string userName, int userCo)
        {
            if (req.Slot is < 1 or > 3) return Fail("امضای نامعتبر.");
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("سند پیدا نشد.");
            if (h.Final) return Fail("این سند قطعی شده و امضایش دیگر عوض نمی‌شود.");
            var perms = await SignPermsAsync(userCo);
            var allowed = req.Slot switch { 1 => perms.S1, 2 => perms.S2, _ => perms.S3 };
            if (!allowed) return Fail($"اجازه‌ی امضای «{SlotTitles[req.Slot - 1]}» را ندارید (جدول امضاها).");
            if (SlotBlock(req.Slot, h.Sgn2, h.Sgn3) is { } sb) return Fail(sb);
            var now = req.Slot switch { 1 => h.Sgn1, 2 => h.Sgn2, _ => h.Sgn3 };
            if (now == req.On) return new SanadSaveResult { Ok = true, Ns = ns };

            var mark = req.Slot == 1
                ? (req.On ? " :امضا شد1 " : " :امضا برداشته شد1:")
                : (req.On ? $":امضا شد{req.Slot} " : $":امضا برداشته شد{req.Slot}:");

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                var existing = await TaskIdAsync(conn, tx, h.Base);
                var mid = existing ?? await EnsureTaskAsync(conn, tx, h, userCo, userName, userCo);
                await AddEventAsync(conn, tx, mid, h.Base, userName + mark, userName);
                if (existing is not null)
                    await conn.ExecuteAsync("UPDATE dbo.TASKS SET PERSONEL = USERCO, STATUS = 1 WHERE IDNUM = @mid", new { mid }, tx);
                await conn.ExecuteAsync(
                    $"UPDATE dbo.DEED_HED SET SGN{req.Slot} = @on, sgn{req.Slot}usid = @userCo, OKF = 1 WHERE N_S = @ns",
                    new { on = req.On, userCo, ns }, tx);
                return 0;
            });
            return new SanadSaveResult { Ok = true, Ns = ns };
        }

        // ─────────────────────────── ارجاع ───────────────────────────

        /// <summary>PERSONEL_SelectionChanged → PERSONELUpdate(0, BASE, per, …).</summary>
        public async Task<SanadSaveResult> ReferAsync(double ns, TreasuryReferRequest req, string userName, int userCo)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return Fail("ابتدا سند را ذخیره و سپس ارجاع دهید.");
            var target = await _db.DoGetDataSQLAsyncSingle<string?>(
                "SELECT TOP 1 SAL_NAME FROM dbo.SALA_DTL WHERE IDD = @p AND ENABL = 0", new { p = req.Personel });
            if (target is null) return Fail("کاربرِ ارجاع پیدا نشد یا غیرفعال است.");
            var targetName = TreasuryService.DecodeUser(target);

            await _db.ExecuteInTransactionAsync(async (conn, tx) =>
            {
                var existing = await TaskIdAsync(conn, tx, h.Base);
                long mid;
                if (existing is { } m)
                {
                    mid = m;
                    await conn.ExecuteAsync("UPDATE dbo.TASKS SET PERSONEL = @per, STATUS = 1 WHERE IDNUM = @mid", new { per = req.Personel, mid }, tx);
                }
                else mid = await EnsureTaskAsync(conn, tx, h, req.Personel, userName, userCo);
                await AddEventAsync(conn, tx, mid, h.Base, "ارجاع شد به  : " + targetName, userName);
                return 0;
            });
            return new SanadSaveResult { Ok = true, Ns = ns, Info = $"ارجاع داده شد به {targetName}." };
        }

        // ─────────────────────────── چاپ ───────────────────────────

        private sealed class PrintLine
        {
            public long Id { get; set; }
            public string? Hes { get; set; }
            public string? Name { get; set; }
            public int Kol { get; set; }
            public string? KolName { get; set; }
            public string? Sharh { get; set; }
            public double Bed { get; set; }
            public double Bes { get; set; }
        }

        private sealed class SignerRow
        {
            public string? Title { get; set; }
            public string? AccountName { get; set; }
            public bool HasImage { get; set; }
        }

        /// <summary>
        /// Command22 «چاپ سند» (R_SANAD_PRINT) و Command3 «چاپ سند ۲» (R_SANAD_PRINT_B، بی مبنا و شرحِ سند).
        /// فقط بعد از دستِ‌کم یک امضا (Window_Loaded) و فقط سندِ تراز. همان گروه‌بندیِ DEAD_WITH_GRP:
        /// ردیف‌های بدهکار و بستانکار هر حسابِ کل جدا، با GRPAS نزولی — یعنی بدهکارها از بزرگ به کوچک، بعد
        /// بستانکارها از کوچک به بزرگ. WPF جدولِ موقتِ مشترکِ DEAD_DTL_PRINT را پر می‌کرد (دو چاپِ هم‌زمان
        /// قاطی می‌شد)؛ اینجا مستقیم از DEED_DTL.
        /// </summary>
        public async Task<(SanadPrintDto? Doc, string? Error)> PrintAsync(double ns, bool full)
        {
            var h = await HeaderAsync(ns);
            if (h is null) return (null, "سند پیدا نشد.");
            if (!h.Signed) return (null, "برای چاپ، سند باید دستِ‌کم یک امضا داشته باشد.");
            if (!h.Balanced) return (null, $"سند تراز نمی‌باشد. جمع بدهکار و بستانکار سند باید مساوی باشد. مبلغ اختلاف: {Math.Abs(h.Difference):N0}");

            var lines = (await _db.DoGetDataSQLAsync<PrintLine>(@"
                SELECT d.id Id, d.HES Hes, CAST(c.NAME AS nvarchar(300)) Name, d.HES_K Kol, CAST(k.NAME AS nvarchar(300)) KolName,
                       d.SHARH Sharh, ISNULL(d.BED, 0) Bed, ISNULL(d.BES, 0) Bes
                FROM dbo.DEED_DTL d
                OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = d.HES) c
                LEFT JOIN dbo.TOTA_HES k ON k.NUMBER = d.HES_K
                WHERE d.N_S = @ns ORDER BY d.id", new { ns })).ToList();

            var groups = lines.Where(l => l.Bed != 0).GroupBy(l => l.Kol).Select(g => MakeGroup(g, true))
                .Concat(lines.Where(l => l.Bes != 0).GroupBy(l => l.Kol).Select(g => MakeGroup(g, false)))
                .OrderByDescending(g => g.Debit ? g.Sum * 1000 + g.Kol : -g.Sum * 1000 - g.Kol)
                .ToList();

            var company = (await _db.DoGetDataSQLAsync<(string? Name, string? Year)>("SELECT TOP 1 NAME, WIDTH_D FROM dbo.SAZMAN")).FirstOrDefault();
            var doc = new SanadPrintDto
            {
                Full = full, CompanyName = TreasuryService.NormalizeFa(company.Name), FiscalYear = company.Year,
                Ns = h.Ns, Date = h.Date, Base = h.Base, Bayeg = h.Bayeg, Sharh = h.Sharh, UserName = h.UserName,
                Groups = groups, TotalBed = h.SumBed, TotalBes = h.SumBes, PrintedOn = TreasuryService.Today()
            };
            doc.TotalInWords = doc.TotalBed > 0 ? CL_HESABDARI.ALPHANUM(doc.TotalBed) + " ریال" : null;

            var flags = new[] { (h.Sgn1, h.Sgn1User, "SND_TAHITX"), (h.Sgn2, h.Sgn2User, "SND_MALITX"), (h.Sgn3, h.Sgn3User, "SND_MODIRTX") };
            for (int i = 0; i < 3; i++)
            {
                var (on, user, col) = flags[i];
                if (!on || user is not > 0) continue;
                var info = (await _db.DoGetDataSQLAsync<SignerRow>($@"
                    SELECT CAST(NULLIF(sg.{col}, N'') AS nvarchar(200)) Title, CAST(ch.NAME AS nvarchar(300)) AccountName,
                           CAST(CASE WHEN sd.EMZA IS NULL THEN 0 ELSE 1 END AS bit) HasImage
                    FROM dbo.SALA_DTL sd
                    LEFT JOIN dbo.SIGN sg ON sg.USERCO = sd.IDD
                    OUTER APPLY (SELECT TOP 1 NAME FROM dbo.CUST_HESAB WHERE hes = sd.HES) ch
                    WHERE sd.IDD = @u", new { u = user })).FirstOrDefault();
                doc.Signers.Add(new TreasuryPrintSigner
                {
                    Slot = i + 1,
                    Title = string.IsNullOrWhiteSpace(info?.Title) ? SlotTitles[i] : TreasuryService.NormalizeFa(info.Title),
                    Name = TreasuryService.NormalizeFa(info?.AccountName),
                    HasImage = info?.HasImage == true
                });
            }
            return (doc, null);
        }

        private static SanadPrintGroup MakeGroup(IGrouping<int, PrintLine> g, bool debit) => new()
        {
            Kol = g.Key,
            KolName = TreasuryService.NormalizeFa(g.First().KolName),
            Debit = debit,
            Sum = g.Sum(l => debit ? l.Bed : l.Bes),
            Rows = g.Select(l => new SanadPrintRow
            {
                Hes = l.Hes, Name = TreasuryService.NormalizeFa(l.Name), Sharh = l.Sharh, Amount = debit ? l.Bed : l.Bes
            }).ToList()
        };

        /// <summary>تصویرِ امضای امضاکننده‌ی یک سند (SALA_DTL.EMZA) — فقط اگر واقعاً امضا کرده.</summary>
        public async Task<byte[]?> SignatureImageAsync(double ns, int slot)
        {
            var h = await HeaderAsync(ns);
            if (h is null || slot is < 1 or > 3) return null;
            var (on, user) = slot switch { 1 => (h.Sgn1, h.Sgn1User), 2 => (h.Sgn2, h.Sgn2User), _ => (h.Sgn3, h.Sgn3User) };
            if (!on || user is not > 0) return null;
            return TreasuryService.ExtractImage(await _db.DoGetDataSQLAsyncSingle<byte[]?>("SELECT TOP 1 EMZA FROM dbo.SALA_DTL WHERE IDD = @user", new { user }));
        }

        // ─────────────────────────── اکسل ───────────────────────────

        /// <summary>EXPORTEXCEL_BTN — ردیف‌های سند با نام حساب، راست‌به‌چپ، با جمع.</summary>
        public async Task<byte[]?> ExcelAsync(double ns)
        {
            var d = await GetAsync(ns);
            if (d is null) return null;
            var costNames = (await _db.DoGetDataSQLAsync<TreasuryLookupItem>(
                "SELECT MHAZ_NO Id, CAST(MHAZNAME AS nvarchar(200)) Name FROM dbo.TCOD_MARKAZHAZ")).ToDictionary(x => x.Id, x => TreasuryService.NormalizeFa(x.Name));

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add($"سند {ns:0}");
            ws.RightToLeft = true;
            ws.Cell(1, 1).Value = $"سند حسابداری شماره {ns:0} — {TreasuryService.FormatDate(d.Header.Date)} — مبنا {d.Header.Base}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            if (!string.IsNullOrWhiteSpace(d.Header.Sharh)) ws.Cell(2, 1).Value = d.Header.Sharh;

            string[] head = { "ردیف", "کد حساب", "نام حساب", "شرح", "بدهکار", "بستانکار", "مرکز هزینه", "سریال چک", "بانک", "سررسید" };
            const int top = 4;
            for (int c = 0; c < head.Length; c++) ws.Cell(top, c + 1).Value = head[c];
            var hr = ws.Range(top, 1, top, head.Length);
            hr.Style.Font.Bold = true;
            hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");

            int row = top + 1, n = 0;
            foreach (var r in d.Rows)
            {
                ws.Cell(row, 1).Value = ++n;
                ws.Cell(row, 2).Value = r.Hes;
                ws.Cell(row, 3).Value = r.HesName;
                ws.Cell(row, 4).Value = r.Sharh;
                ws.Cell(row, 5).Value = r.Bed;
                ws.Cell(row, 6).Value = r.Bes;
                if (r.MhazNo is { } m) ws.Cell(row, 7).Value = costNames.GetValueOrDefault(m, m.ToString());
                if (r.NSeri is { } sr) ws.Cell(row, 8).Value = sr;
                ws.Cell(row, 9).Value = r.BankName;
                if (r.Cheque is { } c) ws.Cell(row, 10).Value = TreasuryService.FormatDate(c.DateS);
                row++;
            }
            ws.Cell(row, 4).Value = "جمع";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 5).FormulaA1 = $"SUM(E{top + 1}:E{Math.Max(top + 1, row - 1)})";
            ws.Cell(row, 6).FormulaA1 = $"SUM(F{top + 1}:F{Math.Max(top + 1, row - 1)})";
            ws.Range(top + 1, 5, row, 6).Style.NumberFormat.Format = "#,##0";
            ws.Range(row, 5, row, 6).Style.Font.Bold = true;
            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
