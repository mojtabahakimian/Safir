using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Utility;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند برگشت فروش» — پورتِ دستیِ دو تابع در CL_HESABDARI_AUTO_BAZ.cs:
    //
    //   • Pass1 = gensanadbargashfroosh   → HEAD_LST.TAG=4  (سند از روی
    //     INVO_LST_TAKH.TAG=2، کلید NUMBER1 = شماره فاکتور اصلی)
    //   • Pass2 = gensanadbargashfroosh2  → HEAD_LST.TAG=25 (سند از روی
    //     INVO_LST.TAG=24)
    //
    //  هر دو NO_S=4 دارند و از همان استخر سند روزانه استفاده می‌کنند؛ کد اصلی
    //  آن‌ها را پشت‌سرهم (نه هم‌زمان) صدا می‌زند تا اگر هر دو TAG در یک روز
    //  برگه داشته باشند، سند روزانه‌ی مشترک را به‌درستی پیدا/بسازند — همین
    //  ترتیب اینجا هم حفظ شده: Pass2 فقط بعد از پایان کامل Pass1 اجرا می‌شود.
    //
    //  روی YAZDSEPAR1405: TAG=4 فقط ۱۳ ردیف دارد، TAG=25 حدود ۱۳۰۰ ردیف —
    //  یعنی Pass2 مسیر غالب این شرکت است، نه یک مسیر فرعیِ قابل‌حذف.
    //
    //  ⚠️ دو تابع اصلی در برخورد با علامتِ M_NAGHD «متفاوت» رفتار می‌کنند:
    //  GENSANADFROOSH/سایر توابع رویِ علامت شاخه می‌زنند (BES وقتی مثبت،
    //  BED با قدرمطلق وقتی منفی)، ولی هر دو Pass همین تابع مقدار خام M_NAGHD
    //  را بدون شاخه‌زنی مستقیم در ستون BED هر دو طرف می‌گذارند — وقتی مقدار
    //  منفی است، همان عدد منفی در BED اثر یک بستانکاری را می‌دهد. این رفتار
    //  عیناً (نه با «اصلاح» بی‌اجازه) حفظ شده چون در خودِ منبع سازگار است.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class SaleReturnRebuildResult
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class SaleReturnRebuildService
    {
        private readonly IDatabaseService _db;

        public SaleReturnRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? MOGODIA  { get; set; }
            public double? MFROSH   { get; set; }
            public double? DARAM    { get; set; }
            public double? GHEYMAT  { get; set; }
            public double? TFROSH   { get; set; }
            public double? SANDOGH  { get; set; }
            public string? APA      { get; set; }
            public string? HESMBAA  { get; set; }
            public string? HPOR     { get; set; }
            public string? OPTIONSS { get; set; }
            public bool?   SNDKH    { get; set; }
            public bool?   SANAT    { get; set; }
            public byte?   ARSESH   { get; set; }
            public string? tindata  { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER    { get; set; }
            public double? NUMBER1   { get; set; }
            public long?   DATE_N    { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }
            public string? CUST_NO   { get; set; }
            public int?    CUST_KIND { get; set; }
            public int?    DEPATMAN  { get; set; }
            public int?    SHIFT     { get; set; }
            public double? ARZD      { get; set; }
            public double? MABL_HAZ  { get; set; }
            public string? MOIN_HAZ  { get; set; }
            public double? MBAA      { get; set; }
            public string? HMBAA     { get; set; }
            public double? TAKHFIF   { get; set; }
            public double? M_NAGHD   { get; set; }
            public string? MOLAH     { get; set; }
        }

        private sealed class Pass1LineRow
        {
            public double? MABL_K   { get; set; }
            public double? MEGH_MAR { get; set; }
            public string? CODE     { get; set; }
            public int?    ANBAR    { get; set; }
            public string? NAME     { get; set; }
            public double? AVRAGE   { get; set; }
        }

        private sealed class Pass2LineRow
        {
            public double? MABL_K { get; set; }
            public double? MEGHk  { get; set; }
            public string? CODE   { get; set; }
            public int?    ANBAR  { get; set; }
            public string? NAME   { get; set; }
            public double? AVRAGE { get; set; }
        }

        private sealed class DiscountRow
        {
            public double? JAMT      { get; set; }
            public string? CODE      { get; set; }
            public int?    CUST_KIND { get; set; }
        }

        private sealed class ChequeRow
        {
            public double? N_SERI { get; set; }
            public int?    BANK   { get; set; }
            public long?   DATE_S { get; set; }
            public string? SHOBEH { get; set; }
            public double? MABL   { get; set; }
            public double? NUMBER { get; set; }
        }

        private sealed class VisitorRow
        {
            public double? NUMBER  { get; set; }
            public string? CUST_NO { get; set; }
            public double? DARSAD  { get; set; }
            public double  PURSANT { get; set; }
            public string? TOZIH   { get; set; }
            public bool?   STAT    { get; set; }
        }

        private sealed class HeadManfRow
        {
            public double? SumOfMABLK       { get; set; }
            public double? SumOfIMBIBE_MANF { get; set; }
            public double? SumOfIMBIBE_SAR  { get; set; }
            public double? FNUMB            { get; set; }
        }

        // ───────────────────────────── کمکی‌های قالب‌بندی ─────────────────────────────

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";

        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];
        private static string RightTrim(string s, int max) => s.Length <= max ? s : s[^max..];
        private static string PersianDate(long dateN) => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";
        private static string OptChar(string? options, int pos)
            => (options != null && pos >= 1 && pos <= options.Length) ? options.Substring(pos - 1, 1) : "";
        private static double SafeToDouble(string? s)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0d;

        private static bool TryGetAccountCode(string? value, out long result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return false;
            if (double.IsNaN(parsed) || parsed < int.MinValue || parsed > int.MaxValue) return false;
            result = (long)parsed;
            return true;
        }

        // بازتلاش روی بن‌بست (Deadlock) — هر برگه تراکنش جدای خودش را موازی
        // با بقیه اجرا می‌کند (SET DEADLOCK_PRIORITY LOW)؛ چون کار هر برگه
        // idempotent است، تلاش دوباره‌ی همان تراکنش پس از خطای ۱۲۰۵ امن است.
        private static async Task ExecuteWithDeadlockRetryAsync(Func<Task> action, int maxAttempts = 3)
        {
            for (var attempt = 1; ; attempt++)
            {
                try { await action(); return; }
                catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && attempt < maxAttempts)
                {
                    await Task.Delay(Random.Shared.Next(150, 450) * attempt);
                }
            }
        }

        private static async Task ParallelForAsync(int count, int maxDegree, Func<int, Task> body)
        {
            if (count == 0) return;
            maxDegree = Math.Max(1, Math.Min(maxDegree, count));
            using var gate = new SemaphoreSlim(maxDegree);
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                var idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try { await body(idx); }
                    finally { gate.Release(); }
                });
            }
            await Task.WhenAll(tasks);
        }

        // ═══════════════════════════════════════════════════════════════
        //  متد اصلی — Pass1 سپس Pass2، هر دو روی همان بازه‌ی شماره/تاریخ
        // ═══════════════════════════════════════════════════════════════

        public async Task<SaleReturnRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new SaleReturnRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;
            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>(
                "SELECT TOP 1 MOGODIA, MFROSH, DARAM, GHEYMAT, TFROSH, SANDOGH, APA, HESMBAA, HPOR, " +
                "OPTIONSS, SNDKH, SANAT, ARSESH FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();

            var hasTindata = (await _db.DoGetDataSQLAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SAZMAN' AND COLUMN_NAME='tindata'"))
                .FirstOrDefault() > 0;
            if (hasTindata)
                acc.tindata = (await _db.DoGetDataSQLAsync<string>("SELECT TOP 1 tindata FROM dbo.SAZMAN")).FirstOrDefault();

            if (acc.MOGODIA is null || acc.MFROSH is null || acc.DARAM is null || acc.GHEYMAT is null
                || acc.TFROSH is null || acc.SANDOGH is null || string.IsNullOrWhiteSpace(acc.APA))
            {
                result.Success = false;
                RecordFailure("حساب‌های پایه (موجودی/برگشت فروش/درآمد/قیمت تمام‌شده/تخفیف/صندوق/اسناد پرداختنی) در SAZMAN تنظیم نشده‌اند؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            var ctx = new SharedCtx(_db, acc, AddLog, RecordFailure);

            var r1 = await RunPass1Async(ctx, fromNumber, toNumber, dateFrom, dateTo);
            var r2 = await RunPass2Async(ctx, fromNumber, toNumber, dateFrom, dateTo);

            result.Success = r1.Success && r2.Success;
            result.SheetCount = r1.SheetCount + r2.SheetCount;
            result.FirstError = firstError;
            result.LastSanadNumber = r2.LastSanadNumber ?? r1.LastSanadNumber;
            AddLog($"gensanadbargashfroosh: پایان — TAG=4: {r1.SheetCount} برگه، TAG=25: {r2.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }

        // ───── زیرساخت مشترک بین دو Pass (کش‌ها و CREATHES) ─────

        private sealed class SharedCtx
        {
            public readonly IDatabaseService Db;
            public readonly SazmanAccounts Acc;
            public readonly Action<string> AddLog;
            public readonly Action<string> RecordFailure;
            public readonly ConcurrentDictionary<(long, long, long), bool> ExistingAccounts = new();
            public readonly ConcurrentDictionary<string, string> KalaNameCache = new();
            public readonly ConcurrentDictionary<string, string> TafNameCache = new();
            public readonly ConcurrentDictionary<int, string> BankNameCache = new();
            public readonly ConcurrentDictionary<(string, long), double> LastFrCache = new();
            public readonly ConcurrentDictionary<(string, long), double> PriceMavad = new();
            public readonly ConcurrentDictionary<(string, long), double> PriceDast = new();
            public readonly ConcurrentDictionary<(string, long), double> PriceSar = new();

            public SharedCtx(IDatabaseService db, SazmanAccounts acc, Action<string> addLog, Action<string> recordFailure)
            {
                Db = db; Acc = acc; AddLog = addLog; RecordFailure = recordFailure;
            }

            public async Task<bool> IsHesabAsync(long kol, long moin, long taf)
            {
                var key = (kol, moin, taf);
                if (ExistingAccounts.TryGetValue(key, out var cached) && cached) return true;
                var rows = await Db.DoGetDataSQLAsync<int>(
                    "SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                    new { Kol = kol, Moin = moin, Taf = taf });
                var exists = rows.Any();
                if (exists) ExistingAccounts[key] = true;
                return exists;
            }

            public async Task CreatHesAsync(double? kol, double? moin, double? taf, string name)
            {
                if (kol is null || moin is null || taf is null)
                    throw new InvalidOperationException($"[CREATHES] حساب نامعتبر: KOL={kol}, MOIN={moin}, TAF={taf}");

                var kolV = (long)kol.Value; var moinV = (long)moin.Value; var tafV = (long)taf.Value;
                var accName = LeftTrim(name ?? string.Empty, 250);
                if (await IsHesabAsync(kolV, moinV, tafV)) return;

                const string sql = @"
BEGIN TRY
    IF NOT EXISTS (SELECT 1 FROM dbo.DETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin)
    BEGIN
        INSERT INTO dbo.DETA_HES (N_KOL, NUMBER, NAME) VALUES (@Kol, @Moin, @Name);
    END
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
END CATCH;
BEGIN TRY
    INSERT INTO dbo.TDETA_HES (N_KOL, NUMBER, TNUMBER, NAME) VALUES (@Kol, @Moin, @Taf, @Name);
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() IN (2601, 2627)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf)
        BEGIN
            INSERT INTO dbo.TDETA_HES (N_KOL, NUMBER, TNUMBER, NAME)
            VALUES (@Kol, @Moin, @Taf, LEFT(@Name,240) + N' (' + CAST(CAST(@Taf AS INT) AS NVARCHAR(20)) + N')');
        END
    END
    ELSE THROW;
END CATCH;";
                try
                {
                    await Db.DoExecuteSQLAsync(sql, new { Kol = kolV, Moin = moinV, Taf = tafV, Name = accName });
                    ExistingAccounts[(kolV, moinV, tafV)] = true;
                }
                catch (Exception ex)
                {
                    ExistingAccounts.TryRemove((kolV, moinV, tafV), out _);
                    RecordFailure($"[CREATHES] خطا در ساخت حساب {kolV}-{moinV}-{tafV} ({accName}): {ex.Message}");
                    throw;
                }
            }

            public async Task<string> GetKalaNameAsync(string? code)
            {
                var key = code ?? string.Empty;
                if (KalaNameCache.TryGetValue(key, out var cached)) return cached;
                var row = (await Db.DoGetDataSQLAsync<string>("SELECT NAME FROM dbo.STUF_DEF WHERE CODE=@Code", new { Code = key })).FirstOrDefault();
                var name = string.IsNullOrEmpty(row) ? " " : row;
                KalaNameCache[key] = name;
                return name;
            }

            public async Task<string> GetTafNameAsync(string? hes)
            {
                var key = hes ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) return " ";
                if (TafNameCache.TryGetValue(key, out var cached)) return cached;
                double? k = null, m = null, t = null, t2 = null, t3 = null, t4 = null;
                CL_HESABDARI.GETTAF3(key, ref k, ref m, ref t, ref t2, ref t3, ref t4);
                if (k is null || m is null || t is null) { TafNameCache[key] = " "; return " "; }
                var name = (await Db.DoGetDataSQLAsync<string>(
                    "SELECT NAME FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                    new { Kol = (long)k.Value, Moin = (long)m.Value, Taf = (long)t.Value })).FirstOrDefault();
                name = string.IsNullOrEmpty(name) ? " " : name;
                TafNameCache[key] = name;
                return name;
            }

            public async Task<string> GetBankNameAsync(int? bank)
            {
                if (bank is null) return " ";
                if (BankNameCache.TryGetValue(bank.Value, out var cached)) return cached;
                var name = (await Db.DoGetDataSQLAsync<string>("SELECT NAMES FROM dbo.TCOD_BANKS WHERE CODE=@Code", new { Code = bank.Value })).FirstOrDefault();
                name = string.IsNullOrEmpty(name) ? bank.Value.ToString(CultureInfo.InvariantCulture) : name;
                BankNameCache[bank.Value] = name;
                return name;
            }

            public async Task<double> GetLastFrAsync(string co, long dt)
            {
                var key = (co ?? string.Empty, dt);
                if (LastFrCache.TryGetValue(key, out var cached)) return cached;
                double result0;
                var nKol = (await Db.DoGetDataSQLAsync<double?>(
                    "SELECT TOP 1 L.N_KOL FROM dbo.INVO_LST L INNER JOIN dbo.HEAD_LST H ON L.NUMBER = H.NUMBER AND L.TAG = H.TAG " +
                    "WHERE L.TAG = 9 AND L.CODE = @Co AND H.DATE_N <= @Dt ORDER BY L.NUMBER DESC", new { Co = co, Dt = dt })).FirstOrDefault();
                if (nKol is null)
                {
                    var f = (await Db.DoGetDataSQLAsync<double?>("SELECT TOP 1 FNUMB FROM dbo.HEAD_MANF WHERE CODE=@Co ORDER BY FNUMB DESC", new { Co = co })).FirstOrDefault();
                    result0 = f ?? 0d;
                }
                else
                {
                    var fnn = (long)nKol.Value;
                    var f2 = (await Db.DoGetDataSQLAsync<double?>("SELECT TOP 1 FNUMB FROM dbo.HEAD_MANF WHERE FNUMB=@Fnn AND CODE=@Co", new { Fnn = fnn, Co = co })).FirstOrDefault();
                    if (f2 is not null) { result0 = f2.Value; }
                    else
                    {
                        var f3 = (await Db.DoGetDataSQLAsync<double?>("SELECT TOP 1 FNUMB FROM dbo.HEAD_MANF WHERE CODE=@Co ORDER BY FNUMB DESC", new { Co = co })).FirstOrDefault();
                        result0 = f3 ?? 0d;
                    }
                }
                LastFrCache[key] = result0;
                return result0;
            }

            private async Task<HeadManfRow?> GetHeadManfSumsAsync(string co, double fnum)
            {
                if (fnum == 0)
                {
                    return (await Db.DoGetDataSQLAsync<HeadManfRow>(
                        "SELECT SUM(DM.MABLK) AS SumOfMABLK, HM.IMBIBE_MANF AS SumOfIMBIBE_MANF, HM.IMBIBE_SAR AS SumOfIMBIBE_SAR, HM.FNUMB " +
                        "FROM dbo.HEAD_MANF HM INNER JOIN dbo.DTL_MANF DM ON HM.FNUMB = DM.FNUMB WHERE HM.CODE = @Co " +
                        "GROUP BY HM.IMBIBE_MANF, HM.IMBIBE_SAR, HM.FNUMB ORDER BY HM.FNUMB", new { Co = co })).FirstOrDefault();
                }
                return (await Db.DoGetDataSQLAsync<HeadManfRow>(
                    "SELECT SUM(DM.MABLK) AS SumOfMABLK, HM.IMBIBE_MANF AS SumOfIMBIBE_MANF, HM.IMBIBE_SAR AS SumOfIMBIBE_SAR, HM.FNUMB " +
                    "FROM dbo.HEAD_MANF HM INNER JOIN dbo.DTL_MANF DM ON HM.FNUMB = DM.FNUMB WHERE HM.CODE = @Co AND HM.FNUMB = @Fnum " +
                    "GROUP BY HM.IMBIBE_MANF, HM.IMBIBE_SAR, HM.FNUMB", new { Co = co, Fnum = fnum })).FirstOrDefault();
            }

            public async Task<double> GetStdPriceMavadAsync(string code, long dt)
            {
                var key = (code, dt);
                if (PriceMavad.TryGetValue(key, out var c)) return c;
                var fnum = await GetLastFrAsync(code, dt);
                var row = await GetHeadManfSumsAsync(code, fnum);
                var v = row?.SumOfMABLK ?? 0d; PriceMavad[key] = v; return v;
            }
            public async Task<double> GetStdPriceDastAsync(string code, long dt)
            {
                var key = (code, dt);
                if (PriceDast.TryGetValue(key, out var c)) return c;
                var fnum = await GetLastFrAsync(code, dt);
                var row = await GetHeadManfSumsAsync(code, fnum);
                var v = row?.SumOfIMBIBE_MANF ?? 0d; PriceDast[key] = v; return v;
            }
            public async Task<double> GetStdPriceSarAsync(string code, long dt)
            {
                var key = (code, dt);
                if (PriceSar.TryGetValue(key, out var c)) return c;
                var fnum = await GetLastFrAsync(code, dt);
                var row = await GetHeadManfSumsAsync(code, fnum);
                var v = row?.SumOfIMBIBE_SAR ?? 0d; PriceSar[key] = v; return v;
            }
        }

        /// <summary>
        /// اگر تاریخ برگه‌ی مبدأ (HEAD_LST.DATE_N) بعد از صدور سند حسابداری
        /// اصلاح/عوض شده باشد — مثلاً برگه‌ای که اول فروردین بوده، بعداً به
        /// اردیبهشت تصحیح شده — سند حسابداریِ قدیمی (که با تاریخ فروردین
        /// پست شده) دیگر با هیچ اجرای بعدی پاک نمی‌شود، چون فیلتر تاریخِ هر
        /// پاس فقط برگه‌های همان بازه را انتخاب می‌کند و این برگه دیگر در آن
        /// بازه نیست — نتیجه یک سند حسابداریِ یتیم که تا ابد در ماهِ اشتباه
        /// می‌ماند. کشف شد روی کد ۳۲۹۴/انبار۸۱۳: سند حسابداری ۱۸۵۸ (۱۴۶
        /// ردیف، همه‌ی کدها) با تاریخ ۱۴۰۵/۰۱/۲۳ پست شده بود ولی هر ۱۴۶
        /// برگه‌ی مبدأش الان ۱۴۰۵/۰۲/۲۳ نشان می‌دهند.
        ///
        /// این‌جا، قبل از هر پاس، سطرهای همین TAG را که تاریخ سند
        /// حسابداری‌شان (DEED_HED.DATE_S) در بازه‌ی همین اجراست ولی تاریخ
        /// فعلیِ برگه‌ی مبدأشان (HEAD_LST.DATE_N) دیگر در این بازه نیست، پاک
        /// می‌کنیم — بدون دست‌کاری دستیِ تاریخ سند. وقتی پاسِ ماهِ درست
        /// (همان‌جایی که HEAD_LST.DATE_N الان واقعاً به آن اشاره می‌کند)
        /// اجرا شود، این برگه‌ها را در محدوده‌ی خودشان می‌بیند و با تاریخ و
        /// نرخ درست دوباره ثبت می‌کند — نه فقط برای فروردین، برای هر ماهی.
        /// </summary>
        /// <remarks>
        /// خودِ منطق به <see cref="DriftedAccountingCleanup"/> منتقل شد تا
        /// سرویس‌های دیگر هم بتوانند صدایش بزنند — نبودنش در بازسازیِ
        /// «فروش» باعث شده بود مغایرتِ کد ۳۱۳۵/انبار۸۰۷ با هیچ بازسازی‌ای
        /// رفع نشود.
        /// </remarks>
        private static async Task CleanupDriftedAccountingAsync(
            IDatabaseService db, double tag, long dateFrom, long dateTo, SharedCtx c)
        {
            var rows = await DriftedAccountingCleanup.RunAsync(db, tag, dateFrom, dateTo);
            if (rows > 0) c.AddLog(DriftedAccountingCleanup.LogMessage(tag, rows));
        }

        // ═══════════════════════════════════════════════════════════════
        //  Pass1 — gensanadbargashfroosh (HEAD_LST.TAG=4)
        // ═══════════════════════════════════════════════════════════════

        private async Task<(bool Success, int SheetCount, long? LastSanadNumber)> RunPass1Async(
            SharedCtx c, long fromNumber, long toNumber, long dateFrom, long dateTo)
        {
            var db = c.Db; var acc = c.Acc; var optionss = acc.OPTIONSS ?? string.Empty;
            var isDailyMode = acc.SNDKH == true;

            await CleanupDriftedAccountingAsync(db, 4, dateFrom, dateTo, c);

            var headRows = (await db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, NUMBER1, DATE_N, N_S, USER_NAME, CUST_NO, CUST_KIND, DEPATMAN, SHIFT, ARZD, " +
                "MABL_HAZ, MOIN_HAZ, MBAA, HMBAA, TAKHFIF, M_NAGHD, MOLAH FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 4 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            c.AddLog($"gensanadbargashfroosh (TAG=4): شروع بازسازی از {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");
            if (headRows.Count == 0) return (true, 0, null);

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101) { c.AddLog($"برگه {h.NUMBER}: تاریخ نامعتبر."); continue; }
                sheetUsable[i] = true;
            }
            var usableIdx = Enumerable.Range(0, headRows.Count).Where(i => sheetUsable[i]).ToList();

            static string BuildSharh(HeadRow h) => RightTrim($"فاكتور برگشت فروش شماره {h.NUMBER} مورخ {PersianDate(h.DATE_N!.Value)}", 100);

            var singleExistingHeaderDates = new Dictionary<double, long?>();
            var singleNeedsNewHeader = new bool[headRows.Count];

            if (isDailyMode)
            {
                var dailyNs = new Dictionary<long, double>();
                var dates = usableIdx.Select(i => headRows[i].DATE_N!.Value).Distinct().ToList();
                if (dates.Count > 0)
                {
                    var minD = dates.Min(); var maxD = dates.Max();
                    foreach (var f in await db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 4 AND DATE_S BETWEEN @Min AND @Max", new { Min = minD, Max = maxD }))
                        if (f.DATE_S is not null && f.N_S is not null && !dailyNs.ContainsKey(f.DATE_S.Value)) dailyNs[f.DATE_S.Value] = f.N_S.Value;

                    var missing = dates.Where(d => !dailyNs.ContainsKey(d)).ToList();
                    if (missing.Count > 0)
                    {
                        var requests = missing.Select(d =>
                        {
                            var sample = headRows[usableIdx.First(i => headRows[i].DATE_N!.Value == d)];
                            return new SanadHeaderRequest { DATE_S = d, SHARH_S = BuildSharh(sample), USER_NAME = sample.USER_NAME };
                        }).ToList();
                        var reserved = await SanadNumbering.ReserveBatchAsync(db, 4, requests);
                        for (int k = 0; k < missing.Count; k++) dailyNs[missing[k]] = reserved[k];
                    }
                }
                foreach (var i in usableIdx)
                {
                    var resolved = dailyNs[headRows[i].DATE_N!.Value];
                    if (headRows[i].N_S != resolved)
                    {
                        headRows[i].N_S = resolved;
                        await db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=4", new { Ns = resolved, Num = headRows[i].NUMBER!.Value });
                    }
                }
            }
            else
            {
                var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
                if (candidateNs.Count > 0)
                {
                    var found = await db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 4 AND N_S BETWEEN @Min AND @Max", new { Min = candidateNs.Min(), Max = candidateNs.Max() });
                    foreach (var f in found) if (f.N_S is not null && !singleExistingHeaderDates.ContainsKey(f.N_S.Value)) singleExistingHeaderDates[f.N_S.Value] = f.DATE_S;
                }
                var claimed = new HashSet<double>();
                var newHeaderIdx = new List<int>();
                foreach (var i in usableIdx)
                {
                    var ns = headRows[i].N_S;
                    var exists = ns is > 0 && singleExistingHeaderDates.ContainsKey(ns.Value);
                    var owns = exists && claimed.Add(ns!.Value);
                    if (!owns) { singleNeedsNewHeader[i] = true; newHeaderIdx.Add(i); }
                }
                if (newHeaderIdx.Count > 0)
                {
                    var requests = newHeaderIdx.Select(i => new SanadHeaderRequest { DATE_S = headRows[i].DATE_N!.Value, SHARH_S = BuildSharh(headRows[i]), USER_NAME = headRows[i].USER_NAME }).ToList();
                    var reserved = await SanadNumbering.ReserveBatchAsync(db, 4, requests);
                    for (int k = 0; k < newHeaderIdx.Count; k++)
                    {
                        headRows[newHeaderIdx[k]].N_S = reserved[k];
                        await db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=4", new { Ns = reserved[k], Num = headRows[newHeaderIdx[k]].NUMBER!.Value });
                    }
                }
            }

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            var successFlag = 1;

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                if (!sheetUsable[R]) return;
                var h = headRows[R];
                var num = h.NUMBER!.Value;
                var ns = h.N_S!.Value;
                var dateN = h.DATE_N!.Value;
                var arzd = h.ARZD ?? 4d;
                var rows = new List<string>();

                void AddRow(double? hesK, double? hesM, double? hesT, double? hesT2, double? hesT3, double? hesT4,
                            string hes, string sharh, double bed, double bes)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},{SqlNum(hesT2)},{SqlNum(hesT3)},{SqlNum(hesT4)}," +
                             $"N'{SqlText(hes)}',N'{SqlText(LeftTrim(sharh, 255))}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(arzd)},{SqlNum(num)},4," +
                             $"NULL,NULL,NULL)");
                }

                try
                {
                    double? ckol = null, cmoin = null, ctaf = null, ctaf2 = null, ctaf3 = null, ctaf4 = null;
                    if (!string.IsNullOrWhiteSpace(h.CUST_NO))
                        CL_HESABDARI.GETTAF3(h.CUST_NO, ref ckol, ref cmoin, ref ctaf, ref ctaf2, ref ctaf3, ref ctaf4);

                    if (!isDailyMode && !singleNeedsNewHeader[R] &&
                        singleExistingHeaderDates.TryGetValue(ns, out var existingDate) && existingDate != dateN)
                    {
                        // در بدنه‌ی موازی UPDATE می‌شود (پایین‌تر، بخش نوشتن به دیتابیس).
                    }

                    // JAMF از NUMBER1 (فاکتور اصلی) خوانده می‌شود، نه شماره‌ی خودِ برگه‌ی برگشت.
                    var jamf = (await db.DoGetDataSQLAsync<double?>(
                        "SELECT SUM(MEGH_MAR*MABL) FROM dbo.INVO_LST WHERE NUMBER=@N1 AND TAG=2", new { N1 = h.NUMBER1 ?? 0d })).FirstOrDefault() ?? 0d;
                    var jamch = (await db.DoGetDataSQLAsync<double?>(
                        "SELECT SUM(MABL) FROM dbo.PAY_GETD WHERE TAG=4 AND NUMBER=@N", new { N = num })).FirstOrDefault() ?? 0d;

                    double takh = 0d;

                    var jst3 = await db.DoGetDataSQLAsync<Pass1LineRow>(
                        "SELECT T.MEGH_MAR * T.MABL AS MABL_K, T.MEGH_MAR, T.CODE, T.ANBAR, S.NAME, T.AVRAGE " +
                        "FROM dbo.STUF_DEF S INNER JOIN dbo.INVO_LST_TAKH T ON S.CODE = T.CODE " +
                        "WHERE T.MEGH_MAR <> 0 AND T.NUMBER = @N1 AND T.TAG = 2", new { N1 = h.NUMBER1 ?? 0d });

                    foreach (var line in jst3)
                    {
                        if (!TryGetAccountCode(line.CODE, out var codeLong)) continue;
                        var codeNum = (double)codeLong;
                        var name = line.NAME ?? " ";
                        var meghMar = line.MEGH_MAR ?? 0d;
                        var mablK = line.MABL_K ?? 0d;
                        var sharh1 = $"برگشت فروش  فاكتور شماره {num} مورخ {PersianDate(dateN)} به مقدار{meghMar} برگشت فروش {name.Trim()}";

                        if (OptChar(optionss, 13) == "5")
                        {
                            await c.CreatHesAsync(acc.MFROSH, 4, codeNum, name);
                            AddRow(acc.MFROSH, 4, codeNum, null, null, null, $"{acc.MFROSH}-4-{codeNum}", sharh1, Math.Round(mablK), 0);
                        }
                        else if (line.ANBAR != 0)
                        {
                            await c.CreatHesAsync(acc.MFROSH, codeNum, codeNum, name);
                            if (mablK > 0)
                            {
                                var tind1 = SafeToDouble(acc.tindata != null && acc.tindata.Length >= 9 ? acc.tindata.Substring(8, 1) : "") == 1d;
                                double hm, ht; string hes;
                                if (tind1) { hm = 1; ht = 1; hes = $"{acc.MFROSH}-1-1"; }
                                else { hm = codeNum; ht = codeNum; hes = $"{acc.MFROSH}-{codeNum}-{codeNum}"; }
                                AddRow(acc.MFROSH, hm, ht, null, null, null, hes, sharh1, Math.Round(mablK), 0);
                            }
                        }
                        else
                        {
                            await c.CreatHesAsync(acc.DARAM, h.DEPATMAN, codeNum, name);
                            AddRow(acc.DARAM, h.DEPATMAN, codeNum, null, null, null, $"{acc.DARAM}-{h.DEPATMAN}-{codeNum}", sharh1, Math.Round(mablK), 0);
                        }

                        if (acc.SANAT == true || acc.SANAT is null)
                        {
                            var mavad = Math.Round(await c.GetStdPriceMavadAsync(line.CODE!, dateN) * meghMar);
                            var dast = Math.Round(await c.GetStdPriceDastAsync(line.CODE!, dateN) * meghMar);
                            var sar = Math.Round(await c.GetStdPriceSarAsync(line.CODE!, dateN) * meghMar);
                            await c.CreatHesAsync(acc.MOGODIA, line.ANBAR, codeNum, name);

                            var sharh2 = $"برگشت فروش.  فاكتور شماره  {num} مورخ {PersianDate(dateN)} به مقدار{meghMar} برگشت {name.Trim()}";
                            var tind1 = SafeToDouble(acc.tindata != null && acc.tindata.Length >= 9 ? acc.tindata.Substring(8, 1) : "") == 1d;

                            if (mavad + dast + sar != 0d && OptChar(optionss, 66) != "5")
                            {
                                AddRow(acc.MOGODIA, line.ANBAR, codeNum, null, null, null, $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}", sharh2, Math.Round(mavad + dast + sar), 0);

                                if (!tind1) await c.CreatHesAsync(acc.GHEYMAT, codeNum, codeNum, name);

                                if (mavad > 0)
                                {
                                    double hm2, ht2; string hes2;
                                    if (tind1) { hm2 = 1; ht2 = 1; hes2 = $"{acc.GHEYMAT}-1-1"; }
                                    else { hm2 = codeNum; ht2 = codeNum; hes2 = $"{acc.GHEYMAT}-{codeNum}-{codeNum}"; }
                                    AddRow(acc.GHEYMAT, hm2, ht2, null, null, null, hes2, sharh2, 0, mavad);
                                }
                                if (dast != 0d)
                                {
                                    await c.CreatHesAsync(acc.GHEYMAT, codeNum, 9999999, $"دستمزد {name}");
                                    AddRow(acc.GHEYMAT, codeNum, 9999999, null, null, null, $"{acc.GHEYMAT}-{codeNum}-9999999", sharh2, 0, dast);
                                }
                                if (sar != 0d)
                                {
                                    await c.CreatHesAsync(acc.GHEYMAT, codeNum, 9999998, $"سربار {name}");
                                    AddRow(acc.GHEYMAT, codeNum, 9999998, null, null, null, $"{acc.GHEYMAT}-{codeNum}-9999998", sharh2, 0, sar);
                                }
                            }
                            else if ((line.AVRAGE ?? 0d) > 0)
                            {
                                var avrAmount = Math.Round((line.AVRAGE ?? 0d) * meghMar);
                                AddRow(acc.MOGODIA, line.ANBAR, codeNum, null, null, null, $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}", sharh2, avrAmount, 0);
                                double hm3, ht3; string hes3;
                                if (tind1) { hm3 = 1; ht3 = 1; hes3 = $"{acc.GHEYMAT}-1-1"; }
                                else { hm3 = codeNum; ht3 = codeNum; hes3 = $"{acc.GHEYMAT}-{codeNum}-{codeNum}"; }
                                await c.CreatHesAsync(acc.GHEYMAT, hm3, ht3, name);
                                AddRow(acc.GHEYMAT, hm3, ht3, null, null, null, hes3, sharh2, 0, avrAmount);
                            }
                        }
                    }

                    if ((h.MABL_HAZ ?? 0d) != 0)
                    {
                        if (string.IsNullOrWhiteSpace(h.MOIN_HAZ))
                        {
                            c.AddLog($"برگه {h.NUMBER}: حساب معین سرویس مشخص نشده؛ ردیف ثبت نشد.");
                        }
                        else
                        {
                            double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                            CL_HESABDARI.GETTAF3(h.MOIN_HAZ, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            if (hk is not null && hm is not null && ht is not null) await c.CreatHesAsync(hk, hm, ht, await c.GetTafNameAsync(h.MOIN_HAZ));
                            var sharh = RightTrim($"فاكتور برگشت فروش  شماره {num} - {await c.GetTafNameAsync(h.MOIN_HAZ)}", 255);
                            AddRow(hk, hm, ht, ht2, ht3, ht4, h.MOIN_HAZ, sharh, h.MABL_HAZ!.Value, 0);
                        }
                    }

                    if (jamch != 0d)
                    {
                        var cheques = await db.DoGetDataSQLAsync<ChequeRow>(
                            "SELECT N_SERI, BANK, DATE_S, SHOBEH, MABL, NUMBER FROM dbo.PAY_GETP WHERE NUMBER=@N AND TAG=4", new { N = num });
                        foreach (var chk in cheques)
                        {
                            var bankName = await c.GetBankNameAsync(chk.BANK);
                            var s1 = RightTrim($"چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}", 255);
                            AddRow(CL_HESABDARI.GETKOL(acc.APA ?? string.Empty), CL_HESABDARI.GETMOIN(acc.APA ?? string.Empty),
                                   CL_HESABDARI.GETTAF(acc.APA ?? string.Empty), null, null, null, acc.APA ?? string.Empty, s1, 0, chk.MABL ?? 0d);

                            var s2 = RightTrim($"ف.ب.ف.{num} - چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}", 255);
                            AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, s2, chk.MABL ?? 0d, 0);
                        }
                    }

                    if ((h.TAKHFIF ?? 0d) != 0)
                    {
                        var rstopen = await db.DoGetDataSQLAsync<DiscountRow>(
                            "SELECT SUM(L.N_KOL / L.MEGHk * L.MEGH_MAR) AS JAMT, L.CODE, H.CUST_KIND " +
                            "FROM dbo.INVO_LST L INNER JOIN dbo.HEAD_LST H ON L.NUMBER = H.NUMBER AND L.TAG = H.TAG " +
                            "WHERE L.NUMBER = @N1 AND L.TAG = 2 GROUP BY L.CODE, H.CUST_KIND", new { N1 = h.NUMBER1 ?? 0d });

                        foreach (var d in rstopen)
                        {
                            if (Math.Round(d.JAMT ?? 0d) == 0) continue;
                            if (!TryGetAccountCode(d.CODE, out var codeLong)) continue;
                            var amt = Math.Round(d.JAMT ?? 0d);
                            var sharh = RightTrim($"مبلغ برگشت تخفيف فروش فاكتور  شماره  {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                            if (OptChar(optionss, 13) == "5")
                            {
                                await c.CreatHesAsync(acc.TFROSH, 3, codeLong, $"تخفيف {await c.GetKalaNameAsync(d.CODE)}");
                                AddRow(acc.TFROSH, 3, codeLong, null, null, null, $"{acc.TFROSH}-3-{codeLong}", sharh, 0, amt);
                            }
                            else
                            {
                                await c.CreatHesAsync(acc.TFROSH, d.CUST_KIND, codeLong, $"تخفيف {await c.GetKalaNameAsync(d.CODE)}");
                                AddRow(acc.TFROSH, d.CUST_KIND, codeLong, null, null, null, $"{acc.TFROSH}-{d.CUST_KIND}-{codeLong}", sharh, 0, amt);
                            }
                            takh += amt;
                        }
                        // نکته: کد اصلی اینجا TAKHFIF محلی را با takh جایگزین می‌کند ولی هرگز
                        // UPDATE HEAD_LST نمی‌زند (برخلاف Pass2) — عیناً حفظ شده.
                        if ((h.TAKHFIF ?? 0d) != takh) h.TAKHFIF = takh;
                    }

                    if ((h.MBAA ?? 0d) != 0)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        string hesText;
                        if (!string.IsNullOrWhiteSpace(h.HMBAA))
                        {
                            CL_HESABDARI.GETTAF3(h.HMBAA, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            hesText = h.HMBAA!;
                        }
                        else
                        {
                            c.RecordFailure($"#WARNING در بازسازی سند برگشت فروش: برای فاکتور {h.NUMBER1} حساب مالیات وجود نداشت؛ با حساب پیش‌فرض سند زده شد.");
                            CL_HESABDARI.GETTAF3(acc.HESMBAA, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            hesText = acc.HESMBAA ?? string.Empty;
                        }
                        if (hk is not null && hm is not null && ht is not null) await c.CreatHesAsync(hk, hm, ht, await c.GetTafNameAsync(hesText));
                        var sharh = RightTrim($"{acc.ARSESH}% ماليات بر ارزش افزوده فاكتور برگشت فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                        AddRow(hk, hm, ht, ht2, ht3, ht4, hesText, sharh, h.MBAA!.Value, 0);
                    }

                    if (jamf + (h.MABL_HAZ ?? 0d) - (h.TAKHFIF ?? 0d) > 0)
                    {
                        var sharh = RightTrim($"فاكتور برگشت فروش  شماره {num} مورخ{PersianDate(dateN)}{h.MOLAH}", 255);
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, 0,
                            Math.Round(jamf + (h.MABL_HAZ ?? 0d) - (h.TAKHFIF ?? 0d) + (h.MBAA ?? 0d)));
                    }

                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var sharh = RightTrim($"مبلغ نقد فاكتور برگشت فروش  شماره {num} مورخ{PersianDate(dateN)}", 255);
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, Math.Round(h.M_NAGHD!.Value), 0);
                    }
                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var hes = $"{acc.SANDOGH}-{h.DEPATMAN}-{h.SHIFT}";
                        var sharh = RightTrim($"مبلغ نقد فاكتور برگشت فروش  شماره {num} مورخ{PersianDate(dateN)}", 255);
                        AddRow(acc.SANDOGH, h.DEPATMAN, h.SHIFT, null, null, null, hes, sharh, 0, Math.Round(h.M_NAGHD!.Value));
                    }

                    double jamp = 0d;
                    var prst = await db.DoGetDataSQLAsync<VisitorRow>(
                        "SELECT NUMBER, CUST_NO, DARSAD, PURSANT, TOZIH, STAT FROM dbo.VISITOR_DTL WHERE NUMBER=@N AND TAG=4", new { N = num });
                    string visitorn = "";
                    foreach (var v in prst)
                    {
                        visitorn = await c.GetTafNameAsync(v.CUST_NO);
                        var opt62Is5 = SafeToDouble(OptChar(optionss, 62)) == 5d;
                        var extra = opt62Is5 ? (h.MBAA ?? 0d) : 0d;

                        if (v.STAT != true)
                        {
                            var computed = Math.Round((jamf - (h.TAKHFIF ?? 0d) + extra) * (v.DARSAD ?? 0d) / 100);
                            if (computed != v.PURSANT)
                            {
                                v.PURSANT = computed;
                                await db.DoExecuteSQLAsync("UPDATE dbo.VISITOR_DTL SET PURSANT=@P WHERE NUMBER=@N AND TAG=4 AND CUST_NO=@C",
                                    new { P = computed, N = num, C = v.CUST_NO ?? string.Empty });
                            }
                        }
                        else
                        {
                            var denom = jamf - (h.TAKHFIF ?? 0d) + extra;
                            if (denom != 0 && v.DARSAD != v.PURSANT / denom * 100)
                            {
                                var newDarsad = v.PURSANT / denom * 100;
                                v.DARSAD = newDarsad;
                                await db.DoExecuteSQLAsync("UPDATE dbo.VISITOR_DTL SET DARSAD=@D WHERE NUMBER=@N AND TAG=4 AND CUST_NO=@C",
                                    new { D = newDarsad, N = num, C = v.CUST_NO ?? string.Empty });
                            }
                        }

                        if (v.PURSANT != 0)
                        {
                            double? pk = null, pm = null, pt = null, pt2 = null, pt3 = null, pt4 = null;
                            CL_HESABDARI.GETTAF3(v.CUST_NO, ref pk, ref pm, ref pt, ref pt2, ref pt3, ref pt4);
                            var sharh = RightTrim($" فاكتور برگشت فروش شماره {num} بابت {v.DARSAD}درصد سهم پورسانت {await c.GetTafNameAsync(v.CUST_NO)} مورخ {PersianDate(dateN)}{v.TOZIH ?? string.Empty}", 255);
                            AddRow(pk, pm, pt, pt2, pt3, pt4, v.CUST_NO ?? string.Empty, sharh, v.PURSANT, 0);
                            jamp += v.PURSANT;
                        }
                    }
                    if (jamp > 0d)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        CL_HESABDARI.GETTAF3(acc.HPOR, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                        if (hk is not null && hm is not null && ht is not null) await c.CreatHesAsync(hk, hm, ht, "پورسانت");
                        var sharh = LeftTrim($"بابت درصد سهم  فاكتور برگشت فروش شماره {num} {visitorn}", 255);
                        AddRow(hk, hm, ht, null, null, null, acc.HPOR ?? string.Empty, sharh, 0, jamp);
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                    if (!isDailyMode && !singleNeedsNewHeader[R] &&
                        singleExistingHeaderDates.TryGetValue(ns, out var exDate) && exDate != dateN)
                    {
                        var sharh = BuildSharh(h);
                        batch.Append($"UPDATE dbo.DEED_HED SET DATE_S={dateN}, SHARH_S=N'{SqlText(sharh)}', GHATEI=0, NO_S=4, OKF=-1, " +
                                     $"USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE N_S={SqlNum(ns)};");
                    }
                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(num)} AND TAG=4;");
                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES_T2,HES_T3,HES_T4,HES,SHARH,BED,BES,ARZD,NUMBER,TAG,RADIF,N_SERI,BANK) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");
                    await ExecuteWithDeadlockRetryAsync(() => db.DoExecuteSQLAsync(batch.ToString(), commandTimeout: CostCloseTuning.BatchTimeoutSeconds));
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    c.RecordFailure($"برگه برگشت فروش {num} (سند {ns}, TAG=4): {ex.Message}");
                }
            });

            long? last = null;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { last = (long)headRows[i].N_S!.Value; break; }

            return (Volatile.Read(ref successFlag) == 1, sheetUsable.Count(u => u), last);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Pass2 — gensanadbargashfroosh2 (HEAD_LST.TAG=25، از INVO_LST.TAG=24)
        // ═══════════════════════════════════════════════════════════════

        private async Task<(bool Success, int SheetCount, long? LastSanadNumber)> RunPass2Async(
            SharedCtx c, long fromNumber, long toNumber, long dateFrom, long dateTo)
        {
            var db = c.Db; var acc = c.Acc; var optionss = acc.OPTIONSS ?? string.Empty;
            var isDailyMode = acc.SNDKH == true;

            double? tindataFlag = null;
            if (!string.IsNullOrEmpty(acc.tindata) && acc.tindata.Length >= 9 &&
                double.TryParse(acc.tindata.Substring(8, 1), out var parsedFlag))
                tindataFlag = parsedFlag;
            var tind1 = tindataFlag is null || tindataFlag == 1d;

            await CleanupDriftedAccountingAsync(db, 25, dateFrom, dateTo, c);

            var headRows = (await db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, NUMBER1, DATE_N, N_S, USER_NAME, CUST_NO, CUST_KIND, DEPATMAN, SHIFT, ARZD, " +
                "MABL_HAZ, MOIN_HAZ, MBAA, HMBAA, TAKHFIF, M_NAGHD, MOLAH FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 25 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            c.AddLog($"gensanadbargashfroosh2 (TAG=25): شروع بازسازی از {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");
            if (headRows.Count == 0) return (true, 0, null);

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101) { c.AddLog($"برگه {h.NUMBER}: تاریخ نامعتبر."); continue; }
                sheetUsable[i] = true;
            }
            var usableIdx = Enumerable.Range(0, headRows.Count).Where(i => sheetUsable[i]).ToList();

            static string BuildSharh(HeadRow h) => RightTrim($"فاكتور برگشت فروش شماره {h.NUMBER} مورخ {PersianDate(h.DATE_N!.Value)}", 100);

            var singleExistingHeaderDates = new Dictionary<double, long?>();
            var singleNeedsNewHeader = new bool[headRows.Count];

            if (isDailyMode)
            {
                var dailyNs = new Dictionary<long, double>();
                var dates = usableIdx.Select(i => headRows[i].DATE_N!.Value).Distinct().ToList();
                if (dates.Count > 0)
                {
                    var minD = dates.Min(); var maxD = dates.Max();
                    foreach (var f in await db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 4 AND DATE_S BETWEEN @Min AND @Max", new { Min = minD, Max = maxD }))
                        if (f.DATE_S is not null && f.N_S is not null && !dailyNs.ContainsKey(f.DATE_S.Value)) dailyNs[f.DATE_S.Value] = f.N_S.Value;

                    var missing = dates.Where(d => !dailyNs.ContainsKey(d)).ToList();
                    if (missing.Count > 0)
                    {
                        var requests = missing.Select(d =>
                        {
                            var sample = headRows[usableIdx.First(i => headRows[i].DATE_N!.Value == d)];
                            return new SanadHeaderRequest { DATE_S = d, SHARH_S = BuildSharh(sample), USER_NAME = sample.USER_NAME };
                        }).ToList();
                        var reserved = await SanadNumbering.ReserveBatchAsync(db, 4, requests);
                        for (int k = 0; k < missing.Count; k++) dailyNs[missing[k]] = reserved[k];
                    }
                }
                foreach (var i in usableIdx)
                {
                    var resolved = dailyNs[headRows[i].DATE_N!.Value];
                    if (headRows[i].N_S != resolved)
                    {
                        headRows[i].N_S = resolved;
                        await db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=25", new { Ns = resolved, Num = headRows[i].NUMBER!.Value });
                    }
                }
            }
            else
            {
                var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
                if (candidateNs.Count > 0)
                {
                    var found = await db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 4 AND N_S BETWEEN @Min AND @Max", new { Min = candidateNs.Min(), Max = candidateNs.Max() });
                    foreach (var f in found) if (f.N_S is not null && !singleExistingHeaderDates.ContainsKey(f.N_S.Value)) singleExistingHeaderDates[f.N_S.Value] = f.DATE_S;
                }
                var claimed = new HashSet<double>();
                var newHeaderIdx = new List<int>();
                foreach (var i in usableIdx)
                {
                    var ns = headRows[i].N_S;
                    var exists = ns is > 0 && singleExistingHeaderDates.ContainsKey(ns.Value);
                    var owns = exists && claimed.Add(ns!.Value);
                    if (!owns) { singleNeedsNewHeader[i] = true; newHeaderIdx.Add(i); }
                    else if (singleExistingHeaderDates[ns!.Value] != headRows[i].DATE_N)
                    {
                        // بروزرسانی تاریخ سند موجود در بدنه‌ی موازی انجام می‌شود.
                    }
                }
                if (newHeaderIdx.Count > 0)
                {
                    var requests = newHeaderIdx.Select(i => new SanadHeaderRequest { DATE_S = headRows[i].DATE_N!.Value, SHARH_S = BuildSharh(headRows[i]), USER_NAME = headRows[i].USER_NAME }).ToList();
                    var reserved = await SanadNumbering.ReserveBatchAsync(db, 4, requests);
                    for (int k = 0; k < newHeaderIdx.Count; k++)
                    {
                        headRows[newHeaderIdx[k]].N_S = reserved[k];
                        await db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=25", new { Ns = reserved[k], Num = headRows[newHeaderIdx[k]].NUMBER!.Value });
                    }
                }
            }

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            var successFlag = 1;

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                if (!sheetUsable[R]) return;
                var h = headRows[R];
                var num = h.NUMBER!.Value;
                var ns = h.N_S!.Value;
                var dateN = h.DATE_N!.Value;
                var arzd = h.ARZD ?? 4d;
                var rows = new List<string>();

                void AddRow(double? hesK, double? hesM, double? hesT, double? hesT2, double? hesT3, double? hesT4,
                            string hes, string sharh, double bed, double bes)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},{SqlNum(hesT2)},{SqlNum(hesT3)},{SqlNum(hesT4)}," +
                             $"N'{SqlText(hes)}',N'{SqlText(LeftTrim(sharh, 255))}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(arzd)},{SqlNum(num)},25," +
                             $"NULL,NULL,NULL)");
                }

                try
                {
                    double? ckol = null, cmoin = null, ctaf = null, ctaf2 = null, ctaf3 = null, ctaf4 = null;
                    if (!string.IsNullOrWhiteSpace(h.CUST_NO))
                        CL_HESABDARI.GETTAF3(h.CUST_NO, ref ckol, ref cmoin, ref ctaf, ref ctaf2, ref ctaf3, ref ctaf4);

                    // ⚠️ محدودیت تاریخ عمداً اضافه شده: NUMBER به‌تنهایی بین
                    // TAG=25 (این برگه) و TAG=24 (منبع ضایعات/رسید) یکتای
                    // سراسری نیست — اگر همین شماره در ماهی دیگر برای یک
                    // سند TAG=24 کاملاً نامرتبط دوباره استفاده شده باشد،
                    // بدون این شرط، مبلغِ آن سندِ ماهِ دیگر به‌جای منبع
                    // واقعیِ همین دوره در سند حسابداریِ این ماه نوشته
                    // می‌شود. روی کد ۳۲۹۴/انبار۸۱۳ دیده شد: ۷ شماره
                    // (۴۸۱،۴۸۲،۴۸۳،۴۸۴،۴۸۶،۵۱۳،۵۱۴) در TAG=25 به تاریخ
                    // فروردین ثبت شده بودند ولی TAG=24 هم‌شماره‌شان به
                    // ۱۴۰۵/۰۲/۲۳ (اردیبهشت) تعلق داشت — نتیجه: مغایرت
                    // CHK-02 بین کارت انبار (که DATE_N را چک می‌کند) و
                    // حسابداری (که تا الان چک نمی‌کرد).
                    var jamf = (await db.DoGetDataSQLAsync<double?>(
                        "SELECT SUM(L.MABL_K) FROM dbo.INVO_LST L " +
                        "INNER JOIN dbo.HEAD_LST H24 ON H24.NUMBER = L.NUMBER AND H24.TAG = L.TAG " +
                        "WHERE L.NUMBER=@N AND L.TAG=24 AND L.ANBAR <> 0 AND H24.DATE_N BETWEEN @DateFrom AND @DateTo",
                        new { N = num, DateFrom = dateFrom, DateTo = dateTo })).FirstOrDefault() ?? 0d;
                    var jamch = (await db.DoGetDataSQLAsync<double?>(
                        "SELECT SUM(MABL) FROM dbo.PAY_GETP WHERE TAG=24 AND NUMBER=@N", new { N = num })).FirstOrDefault() ?? 0d;

                    double takh = 0d;

                    var jstSec = await db.DoGetDataSQLAsync<Pass2LineRow>(
                        "SELECT L.MABL_K, L.MEGHk, L.CODE, L.ANBAR, S.NAME, L.AVRAGE " +
                        "FROM dbo.STUF_DEF S INNER JOIN dbo.INVO_LST L ON S.CODE = L.CODE " +
                        "INNER JOIN dbo.HEAD_LST H24 ON H24.NUMBER = L.NUMBER AND H24.TAG = L.TAG " +
                        "WHERE L.NUMBER = @N AND L.TAG = 24 AND H24.DATE_N BETWEEN @DateFrom AND @DateTo",
                        new { N = num, DateFrom = dateFrom, DateTo = dateTo });

                    foreach (var line in jstSec)
                    {
                        if (!TryGetAccountCode(line.CODE, out var codeLong)) continue;
                        var codeNum = (double)codeLong;
                        var name = line.NAME ?? " ";
                        var meghK = line.MEGHk ?? 0d;
                        var mablK = line.MABL_K ?? 0d;
                        var sharh1 = $"برگشت فروش.  فاكتور شماره {num} مورخ {PersianDate(dateN)} به مقدار{meghK} برگشت فروش. {name.Trim()}";

                        if (OptChar(optionss, 13) == "5")
                        {
                            await c.CreatHesAsync(acc.MFROSH, 4, codeNum, name);
                            AddRow(acc.MFROSH, 4, codeNum, null, null, null, $"{acc.MFROSH}-4-{codeNum}", sharh1, Math.Round(mablK), 0);
                        }
                        else if (line.ANBAR != 0)
                        {
                            if (mablK > 0)
                            {
                                double hm, ht; string hes;
                                if (tind1) { hm = 1; ht = 1; hes = $"{acc.MFROSH}-1-1"; }
                                else { hm = codeNum; ht = codeNum; hes = $"{acc.MFROSH}-{codeNum}-{codeNum}"; }
                                await c.CreatHesAsync(acc.MFROSH, hm, ht, name);
                                AddRow(acc.MFROSH, hm, ht, null, null, null, hes, sharh1, Math.Round(mablK), 0);
                            }
                        }
                        else
                        {
                            await c.CreatHesAsync(acc.DARAM, h.DEPATMAN, codeNum, name);
                            AddRow(acc.DARAM, h.DEPATMAN, codeNum, null, null, null, $"{acc.DARAM}-{h.DEPATMAN}-{codeNum}", sharh1, Math.Round(mablK), 0);
                        }

                        // این Pass بدون شرط SANAT همیشه بهای تمام‌شده را حساب می‌کند (کد اصلی: «|| true»).
                        var mavad = Math.Round(await c.GetStdPriceMavadAsync(line.CODE!, dateN) * meghK);
                        var dast = Math.Round(await c.GetStdPriceDastAsync(line.CODE!, dateN) * meghK);
                        var sar = Math.Round(await c.GetStdPriceSarAsync(line.CODE!, dateN) * meghK);
                        await c.CreatHesAsync(acc.MOGODIA, line.ANBAR, codeNum, name);

                        var sharh2 = $"برگشت فروش.  فاكتور شماره {num} مورخ {PersianDate(dateN)} به مقدار{meghK} برگشت فروش. {name.Trim()}";

                        if (mavad + dast + sar != 0d && OptChar(optionss, 66) != "5")
                        {
                            AddRow(acc.MOGODIA, line.ANBAR, codeNum, null, null, null, $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}", sharh2, Math.Round(mavad + dast + sar), 0);

                            if (!tind1) await c.CreatHesAsync(acc.GHEYMAT, codeNum, codeNum, name);

                            if (mavad > 0)
                            {
                                double hm2, ht2; string hes2;
                                if (tind1) { hm2 = 1; ht2 = 1; hes2 = $"{acc.GHEYMAT}-1-1"; }
                                else { hm2 = codeNum; ht2 = codeNum; hes2 = $"{acc.GHEYMAT}-{codeNum}-{codeNum}"; }
                                await c.CreatHesAsync(acc.GHEYMAT, hm2, ht2, $"مواد {name}");
                                AddRow(acc.GHEYMAT, hm2, ht2, null, null, null, hes2, sharh2, 0, mavad);
                            }
                            if (dast != 0d)
                            {
                                double hm3 = tind1 ? 1 : codeNum, ht3 = 9999999;
                                var hes3 = tind1 ? $"{acc.GHEYMAT}-1-9999999" : $"{acc.GHEYMAT}-{codeNum}-9999999";
                                await c.CreatHesAsync(acc.GHEYMAT, codeNum, 9999999, $"دستمزد {name}");
                                AddRow(acc.GHEYMAT, hm3, ht3, null, null, null, hes3, sharh2, 0, dast);
                            }
                            if (sar != 0d)
                            {
                                double hm4 = tind1 ? 1 : codeNum, ht4 = 9999998;
                                var hes4 = tind1 ? $"{acc.GHEYMAT}-1-9999998" : $"{acc.GHEYMAT}-{codeNum}-9999998";
                                await c.CreatHesAsync(acc.GHEYMAT, hm4, ht4, $"سربار {name}");
                                AddRow(acc.GHEYMAT, hm4, ht4, null, null, null, hes4, sharh2, 0, sar);
                            }
                        }
                        else if ((line.AVRAGE ?? 0d) > 0)
                        {
                            var avrAmount = Math.Round((line.AVRAGE ?? 0d) * meghK);
                            AddRow(acc.MOGODIA, line.ANBAR, codeNum, null, null, null, $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}", sharh2, avrAmount, 0);
                            double hm5, ht5; string hes5;
                            if (tind1) { hm5 = 1; ht5 = 1; hes5 = $"{acc.GHEYMAT}-1-1"; await c.CreatHesAsync(acc.GHEYMAT, 1, 1, $"دستمزد {name}"); }
                            else { hm5 = codeNum; ht5 = codeNum; hes5 = $"{acc.GHEYMAT}-{codeNum}-{codeNum}"; await c.CreatHesAsync(acc.GHEYMAT, hm5, (long)ht5, $"قیمت تمام شده  {name}"); }
                            AddRow(acc.GHEYMAT, hm5, ht5, null, null, null, hes5, sharh2, 0, avrAmount);
                        }
                    }

                    if ((h.MABL_HAZ ?? 0d) != 0 && !string.IsNullOrWhiteSpace(h.MOIN_HAZ))
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        CL_HESABDARI.GETTAF3(h.MOIN_HAZ, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                        var sharh = LeftTrim($"فاكتور برگشت فروش.  شماره{num}{await c.GetTafNameAsync(h.MOIN_HAZ)}", 255);
                        AddRow(hk, hm, ht, ht2, ht3, ht4, h.MOIN_HAZ, sharh, h.MABL_HAZ!.Value, 0);
                    }

                    if (jamch != 0d)
                    {
                        var cheques = await db.DoGetDataSQLAsync<ChequeRow>(
                            "SELECT N_SERI, BANK, DATE_S, SHOBEH, MABL, NUMBER FROM dbo.PAY_GETP WHERE NUMBER=@N AND TAG=24", new { N = num });
                        foreach (var chk in cheques)
                        {
                            var bankName = await c.GetBankNameAsync(chk.BANK);
                            var s1 = RightTrim($"چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}", 255);
                            AddRow(CL_HESABDARI.GETKOL(acc.APA ?? string.Empty), CL_HESABDARI.GETMOIN(acc.APA ?? string.Empty),
                                   CL_HESABDARI.GETTAF(acc.APA ?? string.Empty), null, null, null, acc.APA ?? string.Empty, s1, 0, chk.MABL ?? 0d);

                            var s2 = RightTrim($"ف.ب.ف.{h.NUMBER1} - چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}", 255);
                            AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, s2, chk.MABL ?? 0d, 0);
                        }
                    }

                    if ((h.TAKHFIF ?? 0d) != 0)
                    {
                        var rst = await db.DoGetDataSQLAsync<DiscountRow>(
                            "SELECT SUM(L.N_KOL * L.MABL * L.MEGHk / 100) AS JAMT, L.CODE, H.CUST_KIND " +
                            "FROM dbo.INVO_LST L INNER JOIN dbo.HEAD_LST H ON L.NUMBER = H.NUMBER AND L.TAG = H.TAG - 1 " +
                            "WHERE L.NUMBER = @N AND L.TAG = 24 GROUP BY L.CODE, H.CUST_KIND", new { N = num });

                        foreach (var d in rst)
                        {
                            var jamt = d.JAMT ?? 0d;
                            if (!TryGetAccountCode(d.CODE, out var codeLong)) continue;
                            var sharh = LeftTrim($"مبلغ برگشت تخفيف فروش. فاكتور  شماره  {h.NUMBER1} مورخ {PersianDate(dateN)}", 255);
                            if (OptChar(optionss, 13) == "5")
                            {
                                await c.CreatHesAsync(acc.TFROSH, 3, codeLong, $"تخفيف {await c.GetKalaNameAsync(d.CODE)}");
                                AddRow(acc.TFROSH, 3, codeLong, null, null, null, $"{acc.TFROSH}-3-{codeLong}", sharh, 0, Math.Round(jamt));
                                takh += Math.Round(jamt);
                            }
                            else if (Math.Round(jamt) != 0)
                            {
                                await c.CreatHesAsync(acc.TFROSH, d.CUST_KIND, codeLong, $"تخفيف {await c.GetKalaNameAsync(d.CODE)}");
                                AddRow(acc.TFROSH, d.CUST_KIND, codeLong, null, null, null, $"{acc.TFROSH}-{d.CUST_KIND}-{codeLong}", sharh, 0, Math.Round(jamt));
                                takh += Math.Round(jamt);
                            }
                        }
                        if ((h.TAKHFIF ?? 0d) != takh)
                        {
                            h.TAKHFIF = takh;
                            await db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET TAKHFIF=@T WHERE NUMBER=@N AND TAG=24", new { T = takh, N = num });
                        }
                    }

                    if ((h.MBAA ?? 0d) != 0)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        string hesText;
                        if (!string.IsNullOrWhiteSpace(h.HMBAA))
                        {
                            CL_HESABDARI.GETTAF3(h.HMBAA, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            hesText = h.HMBAA!;
                        }
                        else
                        {
                            c.AddLog($"#WARNING در بازسازی سند برگشت فروش آزاد: برای فاکتور {h.NUMBER1} حساب مالیات وجود نداشت؛ با حساب پیش‌فرض سند زده شد.");
                            CL_HESABDARI.GETTAF3(acc.HESMBAA, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            hesText = acc.HESMBAA ?? string.Empty;
                        }
                        var sharh = LeftTrim($"{acc.ARSESH}% ماليات بر ارزش افزوده فاكتور برگشت فروش شماره {h.NUMBER1} مورخ {PersianDate(dateN)}", 255);
                        AddRow(hk, hm, ht, ht2, ht3, ht4, hesText, sharh, Math.Round(h.MBAA!.Value), 0);
                    }

                    if (jamf + (h.MABL_HAZ ?? 0d) - (h.TAKHFIF ?? 0d) + (h.MBAA ?? 0d) > 0)
                    {
                        var sharh = LeftTrim($"فاكتور برگشت فروش.  شماره{num}مورخ{PersianDate(dateN)}{h.MOLAH}", 255);
                        // ⚠️ ctaf2/3/4 و نه null: کدِ مشتری سطحِ تفصیلی۲ است، و با
                        // null فقط در رشته‌ی نمایشیِ HES می‌نشست («۱۱۵-۵-۲۶-۳۵۲۰»)
                        // ولی ستون HES_T2 خالی می‌ماند. هر گزارشی که با ستون‌های
                        // عددی گروه‌بندی می‌کند — یعنی همه‌ی گزارش‌های تفصیلی —
                        // این ردیف‌ها را زیر «۱۱۵-۵-۲۶» بدون مشتری می‌دید.
                        // Pass1 از اول درست بود؛ فقط این شاخه جا مانده بود.
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, 0,
                            Math.Round(jamf + (h.MABL_HAZ ?? 0d) - (h.TAKHFIF ?? 0d) + (h.MBAA ?? 0d)));
                    }

                    // ⚠️ هر دو طرف نقد بدون شاخه‌زنی روی علامت، مقدار خام را در BED می‌گذارند
                    // (عیناً کد اصلی — یادداشت بالای کلاس).
                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var sharh = LeftTrim($"مبلغ نقد فاكتور برگشت فروش.  شماره{num}مورخ{PersianDate(dateN)}", 255);
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, Math.Round(h.M_NAGHD!.Value), 0);
                    }
                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var hes = $"{acc.SANDOGH}-{h.DEPATMAN}-{h.SHIFT}";
                        var sharh = LeftTrim($"مبلغ نقد فاكتور برگشت فروش.  شماره{num}مورخ{PersianDate(dateN)}", 255);
                        AddRow(acc.SANDOGH, h.DEPATMAN, h.SHIFT, null, null, null, hes, sharh, Math.Round(h.M_NAGHD!.Value), 0);
                    }

                    double jamp = 0d;
                    var prst = await db.DoGetDataSQLAsync<VisitorRow>(
                        "SELECT NUMBER, CUST_NO, DARSAD, PURSANT, TOZIH, STAT FROM dbo.VISITOR_DTL WHERE NUMBER=@N AND TAG=24", new { N = num });
                    string visitorn = "";
                    foreach (var v in prst)
                    {
                        visitorn = await c.GetTafNameAsync(v.CUST_NO);
                        var opt62Is5 = OptChar(optionss, 62) == "5";
                        var extra = opt62Is5 ? (h.MBAA ?? 0d) : 0d;

                        if (v.STAT != true)
                        {
                            var sumu = jamf - (h.TAKHFIF ?? 0d) + extra;
                            var computed = Math.Round(sumu * (v.DARSAD ?? 0d) / 100);
                            if (computed != v.PURSANT)
                            {
                                v.PURSANT = computed;
                                await db.DoExecuteSQLAsync("UPDATE dbo.VISITOR_DTL SET PURSANT=@P WHERE NUMBER=@N AND TAG=24 AND CUST_NO=@C",
                                    new { P = computed, N = num, C = v.CUST_NO ?? string.Empty });
                            }
                        }
                        else
                        {
                            var denom = jamf - (h.TAKHFIF ?? 0d) + extra;
                            if (denom != 0 && v.PURSANT != v.PURSANT / denom * 100)
                            {
                                var newDarsad = v.PURSANT / denom * 100;
                                v.DARSAD = newDarsad;
                                await db.DoExecuteSQLAsync("UPDATE dbo.VISITOR_DTL SET DARSAD=@D WHERE NUMBER=@N AND TAG=24 AND CUST_NO=@C",
                                    new { D = newDarsad, N = num, C = v.CUST_NO ?? string.Empty });
                            }
                        }

                        if (v.PURSANT != 0)
                        {
                            double? pk = null, pm = null, pt = null, pt2 = null, pt3 = null, pt4 = null;
                            CL_HESABDARI.GETTAF3(v.CUST_NO, ref pk, ref pm, ref pt, ref pt2, ref pt3, ref pt4);
                            var sharh = LeftTrim($" فاكتور برگشت فروش . شماره{num}بابت {v.DARSAD}درصد سهم پورسانت {await c.GetTafNameAsync(v.CUST_NO)} مورخ {PersianDate(dateN)}{v.TOZIH ?? string.Empty}", 255);
                            AddRow(pk, pm, pt, pt2, pt3, pt4, v.CUST_NO ?? string.Empty, sharh, Math.Round(v.PURSANT), 0);
                            jamp += v.PURSANT;
                        }
                    }
                    if (jamp > 0d)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        CL_HESABDARI.GETTAF3(acc.HPOR, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                        var sharh = LeftTrim($"بابت درصد سهم  فاكتور برگشت فروش . شماره{num}{visitorn}", 255);
                        AddRow(hk, hm, ht, null, null, null, acc.HPOR ?? string.Empty, sharh, 0, jamp);
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                    if (!isDailyMode && !singleNeedsNewHeader[R] &&
                        singleExistingHeaderDates.TryGetValue(ns, out var exDate) && exDate != dateN)
                    {
                        var sharh = BuildSharh(h);
                        batch.Append($"UPDATE dbo.DEED_HED SET DATE_S={dateN}, SHARH_S=N'{SqlText(sharh)}', GHATEI=0, NO_S=4, OKF=-1, " +
                                     $"USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE N_S={SqlNum(ns)};");
                    }
                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(num)} AND TAG=25;");
                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES_T2,HES_T3,HES_T4,HES,SHARH,BED,BES,ARZD,NUMBER,TAG,RADIF,N_SERI,BANK) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");
                    await ExecuteWithDeadlockRetryAsync(() => db.DoExecuteSQLAsync(batch.ToString(), commandTimeout: CostCloseTuning.BatchTimeoutSeconds));
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    c.RecordFailure($"برگه برگشت فروش {num} (سند {ns}, TAG=25): {ex.Message}");
                }
            });

            long? last = null;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { last = (long)headRows[i].N_S!.Value; break; }

            return (Volatile.Read(ref successFlag) == 1, sheetUsable.Count(u => u), last);
        }
    }
}
