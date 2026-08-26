using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Utility;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند فروش» (DEED_HED/DEED_DTL برای HEAD_LST.TAG=13، NO_S=2).
    //
    //  پورتِ دستیِ GENSANADFROOSH در CL_HESABDARI_AUTO_BAZ.cs — نه فراخوانیِ
    //  آن پروژه. هر شاخه (تخفیف الگویی/ردیفی، پورسانت ویزیتور، بهای تمام‌شده
    //  استاندارد مواد/دستمزد/سربار، حالت سند روزانه/تک‌سندی، چک/نقد/حواله/
    //  واریزی/ماليات) عیناً پیاده شده و از تنظیمات SAZMAN در زمان اجرا خوانده
    //  می‌شود — نه بر اساس مقادیر امروزِ این دیتابیس. طبق تأکید صریح کاربر:
    //  «هارد کد ننویس، تابع را باید کامل بنویسی».
    //
    //  ⚠️ برخلاف GENSANADFROOSH اصلی (که فقط بازه‌ی NUMBER را فیلتر می‌کرد)،
    //  اینجا AND با بازه‌ی تاریخ هم شرط شده — همان الگوی TransferRebuildService
    //  و MaterialIssueRebuildService.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class SaleRebuildResult
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class SaleRebuildService
    {
        private readonly IDatabaseService _db;

        public SaleRebuildService(IDatabaseService db) => _db = db;

        // ───────────────────────────── مدل‌های ردیف ─────────────────────────────

        private sealed class SazmanAccounts
        {
            public double? MOGODIA  { get; set; }
            public double? FROSH    { get; set; }
            public double? DARAM    { get; set; }
            public double? GHEYMAT  { get; set; }
            public double? TFROSH   { get; set; }
            public double? SANDOGH  { get; set; }
            public string? ADA      { get; set; }
            public string? HESMBAA  { get; set; }
            public string? HPOR     { get; set; }
            public string? OPTIONSS { get; set; }
            public bool?   SNDKH    { get; set; }
            public int?    TKHF     { get; set; }
            public bool?   SANAT    { get; set; }
            public byte?   ARSESH   { get; set; }
            public string? tindata  { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER    { get; set; }
            public double? NUMBER1   { get; set; }
            public double? FNUMCO    { get; set; }
            public long?   DATE_N    { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }
            public string? CUST_NO   { get; set; }
            public int?    CUST_KIND { get; set; }
            public int?    DEPATMAN  { get; set; }
            public int?    SHIFT     { get; set; }
            public double? ARZD      { get; set; }
            public byte?   SADER     { get; set; }
            public double? MABL_HAZ  { get; set; }
            public string? MOIN_HAZ  { get; set; }
            public double? MBAA      { get; set; }
            public string? HMBAA     { get; set; }
            public double? TAKHFIF   { get; set; }
            public double? MABL_HAV  { get; set; }
            public string? MOIN_HAV  { get; set; }
            public double? MABL_VAR  { get; set; }
            public string? MOIN_VAR  { get; set; }
            public double? M_NAGHD   { get; set; }
            public string? MOLAH     { get; set; }
        }

        private sealed class LineRow
        {
            public double? NUMBER { get; set; }
            public double? MABL_K { get; set; }
            public double? MEGHk  { get; set; }
            public string? CODE   { get; set; }
            public int?    ANBAR  { get; set; }
            public string? NAME   { get; set; }
            public double? AVRAGE { get; set; }
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

        private sealed class TakhPersRow
        {
            public double? NUMBER  { get; set; }
            public int?    CUST_CO { get; set; }
            public string? TAKH_COD{ get; set; }
            public short?  TAFPER  { get; set; }
            public double? MABL_K  { get; set; }
        }

        private sealed class LineDiscountRow
        {
            public double? NUMBER { get; set; }
            public double? N_MOIN { get; set; }
            public string? CODE   { get; set; }
        }

        private sealed class VisitorRow
        {
            public double? NUMBER  { get; set; }
            public string? CUST_NO { get; set; }
            public double? DARSAD  { get; set; }
            public double  PURSANT { get; set; }
            public string? TOZIH   { get; set; }
            public bool?   STAT    { get; set; }
            public int?    PORID   { get; set; }
        }

        private sealed class PorsantLineRow
        {
            public double? NUMBER { get; set; }
            public string? CODE   { get; set; }
            public double? MABLK  { get; set; }
        }

        private sealed class PorsantKalaEntry
        {
            public double  Porsant   { get; set; }
            public bool    Duplicate { get; set; }
        }

        private sealed class PorsantKalaRow
        {
            public int?    PORID   { get; set; }
            public string? CODE    { get; set; }
            public double? PORSANT { get; set; }
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

        private static string PersianDate(long dateN)
            => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";

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
        //  متد اصلی
        // ═══════════════════════════════════════════════════════════════

        public async Task<SaleRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new SaleRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;

            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            // ───── تنظیمات SAZMAN (شامل tindata که ستونش در همه‌ی شرکت‌ها وجود ندارد) ─────

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>(
                "SELECT TOP 1 MOGODIA, FROSH, DARAM, GHEYMAT, TFROSH, SANDOGH, ADA, HESMBAA, HPOR, " +
                "OPTIONSS, SNDKH, TKHF, SANAT, ARSESH FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();

            var hasTindata = (await _db.DoGetDataSQLAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SAZMAN' AND COLUMN_NAME='tindata'"))
                .FirstOrDefault() > 0;
            if (hasTindata)
            {
                acc.tindata = (await _db.DoGetDataSQLAsync<string>("SELECT TOP 1 tindata FROM dbo.SAZMAN")).FirstOrDefault();
            }

            if (acc.MOGODIA is null || acc.FROSH is null || acc.DARAM is null || acc.GHEYMAT is null
                || acc.TFROSH is null || acc.SANDOGH is null || string.IsNullOrWhiteSpace(acc.ADA))
            {
                result.Success = false;
                RecordFailure("حساب‌های پایه (موجودی/فروش/درآمد/قیمت تمام‌شده/تخفیف/صندوق/اسناد دریافتنی) در SAZMAN تنظیم نشده‌اند؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            var optionss = acc.OPTIONSS ?? string.Empty;
            var sanatPriceNeeded = OptChar(optionss, 66) != "5";
            var isDailyMode = acc.SNDKH == true;
            var tkhf = acc.TKHF ?? 0;

            // ───── کش‌های محلی همین اجرا ─────

            var existingAccounts = new ConcurrentDictionary<(long, long, long), bool>();
            var kalaNameCache     = new ConcurrentDictionary<string, string>();
            var tafNameCache      = new ConcurrentDictionary<string, string>();
            var bankNameCache     = new ConcurrentDictionary<int, string>();
            var departNameCache   = new ConcurrentDictionary<int, string>();
            var lastFrCache        = new ConcurrentDictionary<(string, long), double>();
            var standardPriceMavad = new ConcurrentDictionary<(string, long), double>();
            var standardPriceDast  = new ConcurrentDictionary<(string, long), double>();
            var standardPriceSar   = new ConcurrentDictionary<(string, long), double>();

            async Task<bool> IsHesabAsync(long kol, long moin, long taf)
            {
                var key = (kol, moin, taf);
                if (existingAccounts.TryGetValue(key, out var cached) && cached) return true;
                var rows = await _db.DoGetDataSQLAsync<int>(
                    "SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                    new { Kol = kol, Moin = moin, Taf = taf });
                var exists = rows.Any();
                if (exists) existingAccounts[key] = true;
                return exists;
            }

            async Task CreatHesAsync(double? kol, double? moin, double? taf, string name)
            {
                if (kol is null || moin is null || taf is null)
                {
                    throw new InvalidOperationException($"[CREATHES] حساب نامعتبر: KOL={kol}, MOIN={moin}, TAF={taf}");
                }

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
                    await _db.DoExecuteSQLAsync(sql, new { Kol = kolV, Moin = moinV, Taf = tafV, Name = accName });
                    existingAccounts[(kolV, moinV, tafV)] = true;
                }
                catch (Exception ex)
                {
                    existingAccounts.TryRemove((kolV, moinV, tafV), out _);
                    RecordFailure($"[CREATHES] خطا در ساخت حساب {kolV}-{moinV}-{tafV} ({accName}): {ex.Message}");
                    throw;
                }
            }

            async Task<string> GetKalaNameAsync(string? code)
            {
                var key = code ?? string.Empty;
                if (kalaNameCache.TryGetValue(key, out var cached)) return cached;
                var row = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT NAME FROM dbo.STUF_DEF WHERE CODE=@Code", new { Code = key })).FirstOrDefault();
                var name = string.IsNullOrEmpty(row) ? " " : row;
                kalaNameCache[key] = name;
                return name;
            }

            async Task<string> GetTafNameAsync(string? hes)
            {
                var key = hes ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) return " ";
                if (tafNameCache.TryGetValue(key, out var cached)) return cached;

                double? k = null, m = null, t = null, t2 = null, t3 = null, t4 = null;
                CL_HESABDARI.GETTAF3(key, ref k, ref m, ref t, ref t2, ref t3, ref t4);
                if (k is null || m is null || t is null) { tafNameCache[key] = " "; return " "; }

                var name = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT NAME FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                    new { Kol = (long)k.Value, Moin = (long)m.Value, Taf = (long)t.Value })).FirstOrDefault();
                name = string.IsNullOrEmpty(name) ? " " : name;
                tafNameCache[key] = name;
                return name;
            }

            async Task<string> GetBankNameAsync(int? bank)
            {
                if (bank is null) return " ";
                if (bankNameCache.TryGetValue(bank.Value, out var cached)) return cached;
                var name = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT NAMES FROM dbo.TCOD_BANKS WHERE CODE=@Code", new { Code = bank.Value })).FirstOrDefault();
                name = string.IsNullOrEmpty(name) ? bank.Value.ToString(CultureInfo.InvariantCulture) : name;
                bankNameCache[bank.Value] = name;
                return name;
            }

            async Task<string> GetDepartNameAsync(int? depatman)
            {
                if (depatman is null) return " ";
                if (departNameCache.TryGetValue(depatman.Value, out var cached)) return cached;
                var name = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT DEPNAME FROM dbo.DEPART WHERE DEPATMAN=@D", new { D = depatman.Value })).FirstOrDefault();
                name = string.IsNullOrEmpty(name) ? depatman.Value.ToString(CultureInfo.InvariantCulture) : name;
                departNameCache[depatman.Value] = name;
                return name;
            }

            // ───── GETLASTFR / GETSTANDARDPRICE_MAVAD/DAST/SAR — بهای تمام‌شده استاندارد فرمول ساخت ─────
            // فقط وقتی OPTIONSS[66] <> "5" واقعاً به کار می‌آید (sanatPriceNeeded)، ولی طبق دستور
            // «هاردکد ننویس»، همیشه از تنظیمات پویا خوانده و کامل پیاده‌سازی می‌شود.

            async Task<double> GetLastFrAsync(string co, long dt)
            {
                var key = (co ?? string.Empty, dt);
                if (lastFrCache.TryGetValue(key, out var cached)) return cached;

                double result0;
                var nKol = (await _db.DoGetDataSQLAsync<double?>(
                    "SELECT TOP 1 L.N_KOL FROM dbo.INVO_LST L INNER JOIN dbo.HEAD_LST H " +
                    "ON L.NUMBER = H.NUMBER AND L.TAG = H.TAG " +
                    "WHERE L.TAG = 9 AND L.CODE = @Co AND H.DATE_N <= @Dt ORDER BY L.NUMBER DESC",
                    new { Co = co, Dt = dt })).FirstOrDefault();

                if (nKol is null)
                {
                    var f = (await _db.DoGetDataSQLAsync<double?>(
                        "SELECT TOP 1 FNUMB FROM dbo.HEAD_MANF WHERE CODE=@Co ORDER BY FNUMB DESC", new { Co = co })).FirstOrDefault();
                    result0 = f ?? 0d;
                }
                else
                {
                    var fnn = (long)nKol.Value;
                    var f2 = (await _db.DoGetDataSQLAsync<double?>(
                        "SELECT TOP 1 FNUMB FROM dbo.HEAD_MANF WHERE FNUMB=@Fnn AND CODE=@Co", new { Fnn = fnn, Co = co })).FirstOrDefault();
                    if (f2 is not null)
                    {
                        result0 = f2.Value;
                    }
                    else
                    {
                        var f3 = (await _db.DoGetDataSQLAsync<double?>(
                            "SELECT TOP 1 FNUMB FROM dbo.HEAD_MANF WHERE CODE=@Co ORDER BY FNUMB DESC", new { Co = co })).FirstOrDefault();
                        result0 = f3 ?? 0d;
                    }
                }

                lastFrCache[key] = result0;
                return result0;
            }

            async Task<HeadManfRow?> GetHeadManfSumsAsync(string co, double fnum)
            {
                if (fnum == 0)
                {
                    return (await _db.DoGetDataSQLAsync<HeadManfRow>(
                        "SELECT SUM(DM.MABLK) AS SumOfMABLK, HM.IMBIBE_MANF AS SumOfIMBIBE_MANF, " +
                        "HM.IMBIBE_SAR AS SumOfIMBIBE_SAR, HM.FNUMB FROM dbo.HEAD_MANF HM " +
                        "INNER JOIN dbo.DTL_MANF DM ON HM.FNUMB = DM.FNUMB WHERE HM.CODE = @Co " +
                        "GROUP BY HM.IMBIBE_MANF, HM.IMBIBE_SAR, HM.FNUMB ORDER BY HM.FNUMB",
                        new { Co = co })).FirstOrDefault();
                }
                return (await _db.DoGetDataSQLAsync<HeadManfRow>(
                    "SELECT SUM(DM.MABLK) AS SumOfMABLK, HM.IMBIBE_MANF AS SumOfIMBIBE_MANF, " +
                    "HM.IMBIBE_SAR AS SumOfIMBIBE_SAR, HM.FNUMB FROM dbo.HEAD_MANF HM " +
                    "INNER JOIN dbo.DTL_MANF DM ON HM.FNUMB = DM.FNUMB WHERE HM.CODE = @Co AND HM.FNUMB = @Fnum " +
                    "GROUP BY HM.IMBIBE_MANF, HM.IMBIBE_SAR, HM.FNUMB",
                    new { Co = co, Fnum = fnum })).FirstOrDefault();
            }

            async Task<double> GetStandardPriceMavadAsync(string code, long dt)
            {
                var key = (code ?? string.Empty, dt);
                if (standardPriceMavad.TryGetValue(key, out var cached)) return cached;
                var fnum = await GetLastFrAsync(code, dt);
                var row = await GetHeadManfSumsAsync(code, fnum);
                var v = row?.SumOfMABLK ?? 0d;
                standardPriceMavad[key] = v;
                return v;
            }

            async Task<double> GetStandardPriceDastAsync(string code, long dt)
            {
                var key = (code ?? string.Empty, dt);
                if (standardPriceDast.TryGetValue(key, out var cached)) return cached;
                var fnum = await GetLastFrAsync(code, dt);
                var row = await GetHeadManfSumsAsync(code, fnum);
                var v = row?.SumOfIMBIBE_MANF ?? 0d;
                standardPriceDast[key] = v;
                return v;
            }

            async Task<double> GetStandardPriceSarAsync(string code, long dt)
            {
                var key = (code ?? string.Empty, dt);
                if (standardPriceSar.TryGetValue(key, out var cached)) return cached;
                var fnum = await GetLastFrAsync(code, dt);
                var row = await GetHeadManfSumsAsync(code, fnum);
                var v = row?.SumOfIMBIBE_SAR ?? 0d;
                standardPriceSar[key] = v;
                return v;
            }

            // ───── مرحله ۱: خواندن فاکتورهای فروش ─────

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, NUMBER1, FNUMCO, DATE_N, N_S, USER_NAME, CUST_NO, CUST_KIND, DEPATMAN, SHIFT, " +
                "ARZD, SADER, MABL_HAZ, MOIN_HAZ, MBAA, HMBAA, TAKHFIF, MABL_HAV, MOIN_HAV, MABL_VAR, MOIN_VAR, " +
                "M_NAGHD, MOLAH FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 13 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"GENSANADFROOSH: شروع بازسازی از فاکتور {fromNumber} تا {toNumber} — {headRows.Count} فاکتور یافت شد.");

            if (headRows.Count == 0)
            {
                result.Success = true;
                return result;
            }

            var wanted = new HashSet<double>(headRows.Where(h => h.NUMBER is not null).Select(h => h.NUMBER!.Value));

            var jamfByInvoice  = new Dictionary<double, double>();
            var jamchByInvoice = new Dictionary<double, double>();
            var linesBySheet        = new Dictionary<double, List<LineRow>>();
            var linesWithAnbarBySheet = new Dictionary<double, List<LineRow>>();
            var chequesBySheet   = new Dictionary<double, List<ChequeRow>>();
            var takhPersBySheet  = new Dictionary<(double Number, double CustKind), List<TakhPersRow>>();
            var lineDiscountsBySheet = new Dictionary<double, List<LineDiscountRow>>();
            var visitorsBySheet  = new Dictionary<double, List<VisitorRow>>();
            var porsantLinesBySheet = new Dictionary<double, List<PorsantLineRow>>();
            var porsantKala = new Dictionary<(int Porid, string Code), PorsantKalaEntry>();

            if (wanted.Count > 0)
            {
                var minN = wanted.Min(); var maxN = wanted.Max();

                foreach (var r in await _db.DoGetDataSQLAsync<(double? NUMBER, double? Total)>(
                    "SELECT NUMBER, SUM(MABL_K) AS Total FROM dbo.INVO_LST WHERE TAG = 2 AND NUMBER BETWEEN @Min AND @Max GROUP BY NUMBER",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is not null && r.Total is not null && wanted.Contains(r.NUMBER.Value))
                        jamfByInvoice[r.NUMBER.Value] = r.Total.Value;
                }

                foreach (var r in await _db.DoGetDataSQLAsync<(double? NUMBER, double? Total)>(
                    "SELECT NUMBER, SUM(MABL) AS Total FROM dbo.PAY_GETD WHERE TAG = 2 AND NUMBER BETWEEN @Min AND @Max GROUP BY NUMBER",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is not null && r.Total is not null && wanted.Contains(r.NUMBER.Value))
                        jamchByInvoice[r.NUMBER.Value] = r.Total.Value;
                }

                foreach (var r in await _db.DoGetDataSQLAsync<LineRow>(
                    "SELECT L.NUMBER, L.MABL_K, L.MEGHk, L.CODE, L.ANBAR, S.NAME, L.AVRAGE " +
                    "FROM dbo.INVO_LST L INNER JOIN dbo.STUF_DEF S ON S.CODE = L.CODE " +
                    "WHERE L.TAG = 2 AND L.NUMBER BETWEEN @Min AND @Max",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is null || !wanted.Contains(r.NUMBER.Value)) continue;
                    if (!linesBySheet.TryGetValue(r.NUMBER.Value, out var all)) linesBySheet[r.NUMBER.Value] = all = new List<LineRow>();
                    all.Add(r);
                    if (r.ANBAR is not null && r.ANBAR.Value != 0)
                    {
                        if (!linesWithAnbarBySheet.TryGetValue(r.NUMBER.Value, out var wa)) linesWithAnbarBySheet[r.NUMBER.Value] = wa = new List<LineRow>();
                        wa.Add(r);
                    }
                }

                foreach (var r in await _db.DoGetDataSQLAsync<ChequeRow>(
                    "SELECT N_SERI, BANK, DATE_S, SHOBEH, MABL, NUMBER FROM dbo.PAY_GETD WHERE TAG = 2 AND NUMBER BETWEEN @Min AND @Max",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is null || !wanted.Contains(r.NUMBER.Value)) continue;
                    if (!chequesBySheet.TryGetValue(r.NUMBER.Value, out var l)) chequesBySheet[r.NUMBER.Value] = l = new List<ChequeRow>();
                    l.Add(r);
                }

                foreach (var r in await _db.DoGetDataSQLAsync<TakhPersRow>(
                    "SELECT L.NUMBER, T.CUST_CO, T.TAKH_COD, T.TAFPER, L.MABL_K " +
                    "FROM dbo.INVO_LST L INNER JOIN dbo.TAKHPERS T ON L.CODE = T.TAKH_COD " +
                    "WHERE L.TAG = 2 AND L.NUMBER BETWEEN @Min AND @Max",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is null || r.CUST_CO is null || !wanted.Contains(r.NUMBER.Value)) continue;
                    var key = (r.NUMBER.Value, (double)r.CUST_CO.Value);
                    if (!takhPersBySheet.TryGetValue(key, out var l)) takhPersBySheet[key] = l = new List<TakhPersRow>();
                    l.Add(r);
                }

                // rst7: همان JOIN عجیبِ کد اصلی — INVO_LST به HEAD_LST روی (NUMBER, TAG) با شرط H.TAG = 2،
                // عیناً حفظ شده چون تغییر آن رفتار بازسازی را از نسخهٔ اصلی منحرف می‌کند.
                foreach (var r in await _db.DoGetDataSQLAsync<LineDiscountRow>(
                    "SELECT H.NUMBER, L.N_MOIN, L.CODE " +
                    "FROM dbo.INVO_LST L INNER JOIN dbo.HEAD_LST H ON L.NUMBER = H.NUMBER AND L.TAG = H.TAG " +
                    "WHERE H.TAG = 2 AND H.NUMBER BETWEEN @Min AND @Max",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is null || !wanted.Contains(r.NUMBER.Value)) continue;
                    if (!lineDiscountsBySheet.TryGetValue(r.NUMBER.Value, out var l)) lineDiscountsBySheet[r.NUMBER.Value] = l = new List<LineDiscountRow>();
                    l.Add(r);
                }

                foreach (var r in await _db.DoGetDataSQLAsync<VisitorRow>(
                    "SELECT NUMBER, CUST_NO, DARSAD, PURSANT, TOZIH, STAT, PORID FROM dbo.VISITOR_DTL WHERE TAG = 2 AND NUMBER BETWEEN @Min AND @Max",
                    new { Min = minN, Max = maxN }))
                {
                    if (r.NUMBER is null || !wanted.Contains(r.NUMBER.Value)) continue;
                    if (!visitorsBySheet.TryGetValue(r.NUMBER.Value, out var l)) visitorsBySheet[r.NUMBER.Value] = l = new List<VisitorRow>();
                    l.Add(r);
                }

                if (visitorsBySheet.Any(kv => kv.Value.Any(v => v.PORID is not null && v.PORID.Value != 0)))
                {
                    foreach (var r in await _db.DoGetDataSQLAsync<PorsantLineRow>(
                        "SELECT NUMBER, CODE, MABL_K - N_MOIN AS MABLK FROM dbo.INVO_LST WHERE TAG = 2 AND NUMBER BETWEEN @Min AND @Max",
                        new { Min = minN, Max = maxN }))
                    {
                        if (r.NUMBER is null || !wanted.Contains(r.NUMBER.Value)) continue;
                        if (!porsantLinesBySheet.TryGetValue(r.NUMBER.Value, out var l)) porsantLinesBySheet[r.NUMBER.Value] = l = new List<PorsantLineRow>();
                        l.Add(r);
                    }

                    foreach (var r in await _db.DoGetDataSQLAsync<PorsantKalaRow>("SELECT PORID, CODE, PORSANT FROM dbo.VISITORS_PORSANT_KALA"))
                    {
                        if (r.CODE is null) continue;
                        var key = (r.PORID ?? 0, r.CODE);
                        if (porsantKala.TryGetValue(key, out var existing)) existing.Duplicate = true;
                        else porsantKala[key] = new PorsantKalaEntry { Porsant = r.PORSANT ?? 0d };
                    }
                }
            }

            // ───── مرحله ۲: اعتبارسنجی تاریخ + رزرو دسته‌ای شماره سند (روزانه/تک‌سندی) ─────

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101)
                {
                    AddLog($"فاکتور {h.NUMBER}: تاریخ نامعتبر ({h.DATE_N})؛ رد شد.");
                    continue;
                }
                sheetUsable[i] = true;
            }

            var usableIdx = new List<int>();
            for (int i = 0; i < headRows.Count; i++) if (sheetUsable[i]) usableIdx.Add(i);

            var singleExistingHeaderDates = new Dictionary<double, long?>();
            var singleNeedsNewHeader = new bool[headRows.Count];

            async Task<string> BuildDailySharhAsync(long dateN)
                => LeftTrim($" فاكتورهاي  فروش  مورخ {PersianDate(dateN)}", 255);

            async Task<string> BuildSingleSharhAsync(HeadRow h)
                => LeftTrim($" فاكتور فروش شماره {h.NUMBER1} مورخ {PersianDate(h.DATE_N!.Value)} خريدار: {await GetTafNameAsync(h.CUST_NO)}", 255);

            if (isDailyMode)
            {
                var dailyNs = new Dictionary<long, double>();
                var dates = usableIdx.Select(i => headRows[i].DATE_N!.Value).Distinct().ToList();

                if (dates.Count > 0)
                {
                    var minD = dates.Min(); var maxD = dates.Max();
                    foreach (var f in await _db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 2 AND DATE_S BETWEEN @Min AND @Max",
                        new { Min = minD, Max = maxD }))
                    {
                        if (f.DATE_S is not null && f.N_S is not null && !dailyNs.ContainsKey(f.DATE_S.Value))
                            dailyNs[f.DATE_S.Value] = f.N_S.Value;
                    }

                    var missing = dates.Where(d => !dailyNs.ContainsKey(d)).ToList();
                    if (missing.Count > 0)
                    {
                        var requests = new List<SanadHeaderRequest>();
                        foreach (var d in missing)
                        {
                            var sample = headRows[usableIdx.First(i => headRows[i].DATE_N!.Value == d)];
                            requests.Add(new SanadHeaderRequest { DATE_S = d, SHARH_S = await BuildDailySharhAsync(d), USER_NAME = sample.USER_NAME });
                        }
                        var reserved = await SanadNumbering.ReserveBatchAsync(_db, 2, requests);
                        for (int k = 0; k < missing.Count; k++) dailyNs[missing[k]] = reserved[k];
                    }
                }

                foreach (var i in usableIdx)
                {
                    var resolved = dailyNs[headRows[i].DATE_N!.Value];
                    if (headRows[i].N_S != resolved)
                    {
                        headRows[i].N_S = resolved;
                        await _db.DoExecuteSQLAsync(
                            "UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=13",
                            new { Ns = resolved, Num = headRows[i].NUMBER!.Value });
                    }
                }
            }
            else
            {
                var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
                if (candidateNs.Count > 0)
                {
                    var found = await _db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 2 AND N_S BETWEEN @Min AND @Max",
                        new { Min = candidateNs.Min(), Max = candidateNs.Max() });
                    foreach (var f in found)
                        if (f.N_S is not null && !singleExistingHeaderDates.ContainsKey(f.N_S.Value)) singleExistingHeaderDates[f.N_S.Value] = f.DATE_S;
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
                    var requests = new List<SanadHeaderRequest>();
                    foreach (var i in newHeaderIdx)
                        requests.Add(new SanadHeaderRequest { DATE_S = headRows[i].DATE_N!.Value, SHARH_S = await BuildSingleSharhAsync(headRows[i]), USER_NAME = headRows[i].USER_NAME });

                    var reserved = await SanadNumbering.ReserveBatchAsync(_db, 2, requests);
                    for (int k = 0; k < newHeaderIdx.Count; k++)
                    {
                        headRows[newHeaderIdx[k]].N_S = reserved[k];
                        await _db.DoExecuteSQLAsync(
                            "UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=13",
                            new { Ns = reserved[k], Num = headRows[newHeaderIdx[k]].NUMBER!.Value });
                    }
                }
            }

            // ───── مرحله ۳ (موازی): هر فاکتور مستقل؛ همه دستورهایش یک رفت‌وبرگشت ─────

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            var successFlag = 1;

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                if (!sheetUsable[R]) return;
                var h = headRows[R];
                var num = h.NUMBER!.Value;
                var ns = h.N_S!.Value;
                var dateN = h.DATE_N!.Value;
                var arzd = h.ARZD ?? 1d;

                var rows = new List<string>();

                void AddRow(double? hesK, double? hesM, double? hesT, double? hesT2, double? hesT3, double? hesT4,
                            string hes, string sharh, double bed, double bes, double? radif = null, double? nSeri = null, int? bank = null)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},{SqlNum(hesT2)},{SqlNum(hesT3)},{SqlNum(hesT4)}," +
                             $"N'{SqlText(hes)}',N'{SqlText(LeftTrim(sharh, 255))}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(arzd)}," +
                             $"{SqlNum(num)},13,{SqlNum(radif)},{SqlNum(nSeri)},{(bank.HasValue ? bank.Value.ToString(CultureInfo.InvariantCulture) : "NULL")})");
                }

                try
                {
                    // ── حساب مشتری (K-M-T[-T2][-T3][-T4]) ──
                    double? ckol = null, cmoin = null, ctaf = null, ctaf2 = null, ctaf3 = null, ctaf4 = null;
                    if (!string.IsNullOrWhiteSpace(h.CUST_NO))
                    {
                        CL_HESABDARI.GETTAF3(h.CUST_NO, ref ckol, ref cmoin, ref ctaf, ref ctaf2, ref ctaf3, ref ctaf4);
                        if (ckol is > 0 && cmoin is > 0 && ctaf is > 0)
                        {
                            await CreatHesAsync(ckol, cmoin, ctaf, await GetTafNameAsync(h.CUST_NO));
                        }
                    }

                    // ── سطر سربرگ فاکتور (بدهکار مشتری) ──
                    var jamf = jamfByInvoice.TryGetValue(num, out var jf) ? jf : 0d;
                    var jamch = jamchByInvoice.TryGetValue(num, out var jc) ? jc : 0d;

                    if ((jamf + (h.MABL_HAZ ?? 0d) + (h.MBAA ?? 0d) - (h.TAKHFIF ?? 0d)) != 0d)
                    {
                        if (ckol is not null && cmoin is not null && ctaf is not null)
                            await CreatHesAsync(ckol, cmoin, ctaf, await GetTafNameAsync(h.CUST_NO));

                        var deptSuffix = OptChar(optionss, 55) == "5" ? $" - {await GetDepartNameAsync(h.DEPATMAN)}" : " ";
                        var headerSharh = RightTrim($"ف ف ش {h.NUMBER1}-{h.FNUMCO} مورخ{PersianDate(dateN)}{h.MOLAH}{deptSuffix}", 255);
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, headerSharh,
                            Math.Round(jamf + (h.MABL_HAZ ?? 0d) + (h.MBAA ?? 0d) - (h.TAKHFIF ?? 0d)), 0, radif: num);
                    }

                    // ── ردیف‌های کالا: درآمد فروش (jst_sec) ──
                    var jstSec = linesBySheet.TryGetValue(num, out var secLines) ? secLines : new List<LineRow>();
                    foreach (var line in jstSec)
                    {
                        if (!TryGetAccountCode(line.CODE, out var codeLong)) continue;
                        var codeNum = (double)codeLong;
                        var lineSharh = $"فاكتور فروش شماره {h.NUMBER1} مورخ {PersianDate(dateN)} به مقدار{line.MEGHk} فروش {(line.NAME ?? "").Trim()}";
                        var meghK = line.MEGHk ?? 0d;
                        var mablK = line.MABL_K ?? 0d;

                        if (OptChar(optionss, 13) == "5")
                        {
                            if (h.SADER == 0)
                            {
                                await CreatHesAsync(acc.FROSH, 1, codeNum, line.NAME ?? " ");
                                if (mablK > 0)
                                    AddRow(acc.FROSH, 1, codeNum, null, null, null, $"{acc.FROSH}-1-{codeNum}", lineSharh, 0, Math.Round(mablK));
                            }
                            else if (line.ANBAR != 0)
                            {
                                await CreatHesAsync(acc.FROSH, 2, codeNum, line.NAME ?? " ");
                                if (mablK > 0)
                                    AddRow(acc.FROSH, 2, codeNum, null, null, null, $"{acc.FROSH}-1-{codeNum}", lineSharh, 0, Math.Round(mablK));
                            }
                            else
                            {
                                await CreatHesAsync(acc.DARAM, h.DEPATMAN, codeNum, line.NAME ?? " ");
                                AddRow(acc.DARAM, h.DEPATMAN, codeNum, null, null, null, $"{acc.DARAM}-{h.DEPATMAN}-{codeNum}", lineSharh, 0, Math.Round(mablK));
                            }
                        }
                        else if (line.ANBAR != 0)
                        {
                            await CreatHesAsync(acc.FROSH, codeNum, codeNum, line.NAME ?? " ");
                            if (mablK > 0)
                                AddRow(acc.FROSH, codeNum, codeNum, null, null, null, $"{acc.FROSH}-{codeNum}-{codeNum}", lineSharh, 0, Math.Round(mablK));
                        }
                        else
                        {
                            await CreatHesAsync(acc.DARAM, h.DEPATMAN, codeNum, line.NAME ?? " ");
                            AddRow(acc.DARAM, h.DEPATMAN, codeNum, null, null, null, $"{acc.DARAM}-{h.DEPATMAN}-{codeNum}", lineSharh, 0, Math.Round(mablK));
                        }
                    }

                    // ── ردیف‌های کالا: خروج انبار + بهای تمام‌شده (jst_thr) ──
                    var char9Is1 = SafeToDouble(acc.tindata != null && acc.tindata.Length >= 9 ? acc.tindata.Substring(8, 1) : "") == 1d;
                    var sanatGate = acc.SANAT == true || acc.SANAT is null || acc.tindata is null || char9Is1;

                    if (sanatGate)
                    {
                        var jstThr = linesWithAnbarBySheet.TryGetValue(num, out var thrLines) ? thrLines : new List<LineRow>();
                        foreach (var line in jstThr)
                        {
                            if (!TryGetAccountCode(line.CODE, out var codeLong)) continue;
                            var codeNum = (double)codeLong;
                            var meghK = line.MEGHk ?? 0d;
                            var name = line.NAME ?? " ";

                            var mavad = sanatPriceNeeded ? Math.Round(await GetStandardPriceMavadAsync(line.CODE!, dateN) * meghK) : 0d;
                            var dast  = sanatPriceNeeded ? Math.Round(await GetStandardPriceDastAsync(line.CODE!, dateN) * meghK) : 0d;
                            var sar   = sanatPriceNeeded ? Math.Round((double)await GetStandardPriceSarAsync(line.CODE!, dateN) * meghK) : 0d;

                            await CreatHesAsync(acc.MOGODIA, line.ANBAR, codeNum, name);

                            var exitSharh = $"فاكتور فروش شماره {h.NUMBER1} مورخ {PersianDate(dateN)} به مقدار{meghK} خروج {name.Trim()}";
                            var costSharh = $"فاكتور فروش شماره {h.NUMBER1} مورخ {PersianDate(dateN)} به مقدار{meghK} فروش {name.Trim()}";

                            if (mavad + dast + sar != 0d && OptChar(optionss, 66) != "5")
                            {
                                AddRow(acc.MOGODIA, line.ANBAR, codeNum, null, null, null, $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}", exitSharh, 0, mavad + dast + sar);

                                if (!char9Is1)
                                    await CreatHesAsync(acc.GHEYMAT, codeNum, codeNum, name);

                                if (mavad > 0)
                                {
                                    if (char9Is1)
                                        AddRow(acc.GHEYMAT, 1, 1, null, null, null, $"{acc.GHEYMAT}-1-1", costSharh, mavad, 0);
                                    else
                                        AddRow(acc.GHEYMAT, codeNum, codeNum, null, null, null, $"{acc.GHEYMAT}-{codeNum}-{codeNum}", costSharh, mavad, 0);
                                }

                                await CreatHesAsync(acc.GHEYMAT, codeNum, 9999999, $"دستمزد {name}");
                                if (dast != 0d)
                                    AddRow(acc.GHEYMAT, codeNum, 9999999, null, null, null, $"{acc.GHEYMAT}-{codeNum}-9999999", costSharh, dast, 0);

                                if (sar != 0d)
                                {
                                    await CreatHesAsync(acc.GHEYMAT, codeNum, 9999998, $"سربار {name}");
                                    AddRow(acc.GHEYMAT, codeNum, 9999998, null, null, null, $"{acc.GHEYMAT}-{codeNum}-9999998", costSharh, sar, 0);
                                }
                            }
                            else if ((line.AVRAGE ?? 0d) > 0)
                            {
                                var avrageAmount = Math.Round((line.AVRAGE ?? 0d) * meghK);
                                AddRow(acc.MOGODIA, line.ANBAR, codeNum, null, null, null, $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}", exitSharh, 0, avrageAmount);

                                double gheyM, gheyT; string gheyHes;
                                if (!string.IsNullOrEmpty(acc.tindata) && char9Is1)
                                {
                                    gheyM = 1; gheyT = 1; gheyHes = $"{acc.GHEYMAT}-1-1";
                                }
                                else
                                {
                                    gheyM = codeNum; gheyT = codeNum; gheyHes = $"{acc.GHEYMAT}-{codeNum}-{codeNum}";
                                }
                                // نکته: کد اصلی همیشه CREATHES را با (GHEYMAT, CODE, CODE) صدا می‌زند —
                                // even وقتی char9==1 و ردیف واقعی روی حساب تجمیعی GHEYMAT-1-1 درج می‌شود.
                                // عیناً همان رفتار حفظ شده، نه «اصلاح» بی‌اجازه‌ی این ناهم‌خوانی.
                                await CreatHesAsync(acc.GHEYMAT, codeNum, codeNum, $"قیمیت تمام شده {name}");
                                AddRow(acc.GHEYMAT, gheyM, gheyT, null, null, null, gheyHes, costSharh, avrageAmount, 0);
                            }
                        }
                    }

                    // ── هزینه حمل/خدمات (MABL_HAZ) ──
                    if ((h.MABL_HAZ ?? 0d) != 0)
                    {
                        if (string.IsNullOrWhiteSpace(h.MOIN_HAZ))
                        {
                            AddLog($"فاکتور {h.NUMBER1}: حساب معین سرویس (MOIN_HAZ) مشخص نشده؛ ردیف هزینه حمل/خدمات ثبت نشد.");
                        }
                        else
                        {
                            double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                            CL_HESABDARI.GETTAF3(h.MOIN_HAZ, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            if (hk is not null && hm is not null && ht is not null) await CreatHesAsync(hk, hm, ht, await GetTafNameAsync(h.MOIN_HAZ));
                            var sharh = RightTrim($"سرويس فاكتور فروش شماره {h.NUMBER1} - {await GetTafNameAsync(h.MOIN_HAZ)}", 255);
                            AddRow(hk, hm, ht, ht2, ht3, ht4, h.MOIN_HAZ, sharh, 0, Math.Round(h.MABL_HAZ!.Value));
                        }
                    }

                    // ── چک‌های دریافتی ──
                    if (jamch != 0d)
                    {
                        var cheques = chequesBySheet.TryGetValue(num, out var cl) ? cl : new List<ChequeRow>();
                        foreach (var chk in cheques)
                        {
                            double? ak = null, am = null, at = null, at2 = null, at3 = null, at4 = null;
                            CL_HESABDARI.GETTAF3(acc.ADA, ref ak, ref am, ref at, ref at2, ref at3, ref at4);
                            var bankName = await GetBankNameAsync(chk.BANK);
                            var sharh1 = RightTrim($"چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}", 255);
                            AddRow(ak, am, at, null, null, null, acc.ADA ?? string.Empty, sharh1, chk.MABL ?? 0d, 0,
                                nSeri: chk.N_SERI, bank: chk.BANK);

                            var sharh2 = RightTrim($"ف.ف.{h.NUMBER1} - چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}", 255);
                            AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh2, 0, chk.MABL ?? 0d);
                        }
                    }

                    // ── نقد (شخص) ──
                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var sharh = RightTrim($"مبلغ نقد فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                        if (h.M_NAGHD > 0) AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, 0, h.M_NAGHD.Value);
                        else AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, Math.Abs(h.M_NAGHD.Value), 0);
                    }

                    // ── نقد (صندوق) ──
                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var hes = $"{acc.SANDOGH}-{h.DEPATMAN}-{h.SHIFT}";
                        var sharh = RightTrim($"مبلغ نقد فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                        if (h.M_NAGHD > 0) AddRow(acc.SANDOGH, h.DEPATMAN, h.SHIFT, null, null, null, hes, sharh, h.M_NAGHD.Value, 0);
                        else AddRow(acc.SANDOGH, h.DEPATMAN, h.SHIFT, null, null, null, hes, sharh, 0, Math.Abs(h.M_NAGHD.Value));
                    }

                    // ── تخفیف ──
                    double takh = 0d;
                    if (tkhf == 1)
                    {
                        if ((h.TAKHFIF ?? 0d) != 0)
                        {
                            await CreatHesAsync(acc.TFROSH, 1, 1, "تخفيف");
                            var sharh = RightTrim($"مبلغ تخفيف فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                            AddRow(acc.TFROSH, 1, 1, null, null, null, $"{acc.TFROSH}-1-1", sharh, h.TAKHFIF!.Value, 0);
                        }
                    }
                    if (tkhf != 1)
                    {
                        // تخفیف الگویی (TKHF==2) یا ردیفی (سایر مقادیر) — پیاده‌سازی کامل در پایین.
                        takh = await ApplyDiscountAsync(h, num, ns, dateN, ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, arzd, tkhf,
                            acc, optionss, takhPersBySheet, lineDiscountsBySheet, AddRow, CreatHesAsync, GetKalaNameAsync, RecordFailure);
                    }

                    // ── حواله (شخص + مقصد) ──
                    if ((h.MABL_HAV ?? 0d) != 0)
                    {
                        var sharh = RightTrim($"مبلغ حواله فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, 0, h.MABL_HAV!.Value);

                        double? vk = null, vm = null, vt = null, vt2 = null, vt3 = null, vt4 = null;
                        if (!string.IsNullOrWhiteSpace(h.MOIN_HAV))
                        {
                            CL_HESABDARI.GETTAF3(h.MOIN_HAV, ref vk, ref vm, ref vt, ref vt2, ref vt3, ref vt4);
                            if (vk is not null && vm is not null && vt is not null) await CreatHesAsync(vk, vm, vt, await GetTafNameAsync(h.MOIN_HAV));
                        }
                        AddRow(vk, vm, vt, vt2, vt3, vt4, h.MOIN_HAV ?? string.Empty, sharh, h.MABL_HAV!.Value, 0);
                    }

                    // ── واریزی (شخص + مقصد) ──
                    if ((h.MABL_VAR ?? 0d) != 0)
                    {
                        var sharh = RightTrim($"مبلغ واريزي فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                        AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.CUST_NO ?? string.Empty, sharh, 0, h.MABL_VAR!.Value);

                        double? vk = null, vm = null, vt = null, vt2 = null, vt3 = null, vt4 = null;
                        if (!string.IsNullOrWhiteSpace(h.MOIN_VAR))
                        {
                            CL_HESABDARI.GETTAF3(h.MOIN_VAR, ref vk, ref vm, ref vt, ref vt2, ref vt3, ref vt4);
                            if (vk is not null && vm is not null && vt is not null) await CreatHesAsync(vk, vm, vt, await GetTafNameAsync(h.MOIN_VAR));
                        }
                        AddRow(vk, vm, vt, vt2, vt3, vt4, h.MOIN_VAR ?? string.Empty, sharh, h.MABL_VAR!.Value, 0);
                    }

                    // ── مالیات بر ارزش افزوده ──
                    if ((h.MBAA ?? 0d) != 0)
                    {
                        var sharh = RightTrim($"{acc.ARSESH}% ماليات بر ارزش افزوده فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                        double? vk = null, vm = null, vt = null, vt2 = null, vt3 = null, vt4 = null;
                        string hesText;
                        if (!string.IsNullOrWhiteSpace(h.HMBAA))
                        {
                            CL_HESABDARI.GETTAF3(h.HMBAA, ref vk, ref vm, ref vt, ref vt2, ref vt3, ref vt4);
                            hesText = h.HMBAA!;
                        }
                        else
                        {
                            RecordFailure($"#WARNING در بازسازی سند فروش: برای فاکتور {h.NUMBER1} به شرح {sharh} حساب مالیات آن وجود نداشت؛ با حساب پیش‌فرض مالیات در حساب‌های خودگردان سند زده شد.");
                            CL_HESABDARI.GETTAF3(acc.HESMBAA, ref vk, ref vm, ref vt, ref vt2, ref vt3, ref vt4);
                            hesText = acc.HESMBAA ?? string.Empty;
                        }
                        if (vk is not null && vm is not null && vt is not null) await CreatHesAsync(vk, vm, vt, await GetTafNameAsync(hesText));
                        AddRow(vk, vm, vt, vt2, vt3, vt4, hesText, sharh, 0, h.MBAA!.Value);
                    }

                    // ── پورسانت ویزیتور ──
                    double jamp = 0d;
                    if (jamf > 0d)
                    {
                        var prst = visitorsBySheet.TryGetValue(num, out var vl) ? vl : new List<VisitorRow>();
                        string? tamir = null;

                        foreach (var v in prst)
                        {
                            double? pk = null, pm = null, pt = null, pt2 = null, pt3 = null, pt4 = null;
                            if (!string.IsNullOrWhiteSpace(v.CUST_NO))
                                CL_HESABDARI.GETTAF3(v.CUST_NO, ref pk, ref pm, ref pt, ref pt2, ref pt3, ref pt4);

                            tamir ??= v.CUST_NO;

                            var opt62Is5 = OptChar(optionss, 62) == "5";
                            var baseAmount = jamf - (h.TAKHFIF ?? 0d) + (opt62Is5 ? (h.MBAA ?? 0d) : 0d);

                            if (v.PORID is null or 0)
                            {
                                if (v.STAT != true)
                                {
                                    var computed = Math.Round(baseAmount * (v.DARSAD ?? 0d) / 100d);
                                    if (computed != v.PURSANT)
                                    {
                                        v.PURSANT = computed;
                                        await _db.DoExecuteSQLAsync(
                                            "UPDATE dbo.VISITOR_DTL SET PURSANT=@P WHERE NUMBER=@N AND CUST_NO=@C AND TAG=2",
                                            new { P = computed, N = num, C = v.CUST_NO ?? string.Empty });
                                    }
                                }
                                else if (baseAmount != 0 && v.DARSAD != v.PURSANT / baseAmount * 100)
                                {
                                    var newDarsad = v.PURSANT / baseAmount * 100;
                                    v.DARSAD = newDarsad;
                                    await _db.DoExecuteSQLAsync(
                                        "UPDATE dbo.VISITOR_DTL SET DARSAD=@D WHERE NUMBER=@N AND CUST_NO=@C AND TAG=2",
                                        new { D = newDarsad, N = num, C = v.CUST_NO ?? string.Empty });
                                }
                            }
                            else
                            {
                                double prs = 0d, mbk = 0d;
                                var porsantLines = porsantLinesBySheet.TryGetValue(num, out var pl) ? pl : new List<PorsantLineRow>();
                                foreach (var pline in porsantLines)
                                {
                                    var key = (v.PORID.Value, pline.CODE ?? string.Empty);
                                    if (porsantKala.TryGetValue(key, out var entry) && !entry.Duplicate)
                                    {
                                        prs += Math.Round((pline.MABLK ?? 0d) * entry.Porsant / 100d);
                                        mbk += pline.MABLK ?? 0d;
                                    }
                                    else
                                    {
                                        AddLog($"تذكر مهم: كالای {await GetKalaNameAsync(pline.CODE)} فاقد الگو برای این ویزیتور است و پورسانت محاسبه نشد. فاكتور شماره: {num}");
                                    }
                                }
                                v.PURSANT = prs;
                                if (mbk > 0) v.DARSAD = prs / mbk * 100;
                                await _db.DoExecuteSQLAsync(
                                    "UPDATE dbo.VISITOR_DTL SET PURSANT=@P, DARSAD=@D WHERE NUMBER=@N AND CUST_NO=@C AND TAG=2",
                                    new { P = prs, D = v.DARSAD ?? 0d, N = num, C = v.CUST_NO ?? string.Empty });
                            }

                            jamp += v.PURSANT;

                            if (v.PURSANT != 0)
                            {
                                var sharh = RightTrim(
                                    $" فاكتور فروش شماره {num} : {h.NUMBER1} بابت {v.DARSAD}% مورخ {PersianDate(dateN)}{v.TOZIH ?? string.Empty}مبلغ :  " +
                                    $"{Math.Round(jamf + (h.MABL_HAZ ?? 0d) + (h.MBAA ?? 0d) - (h.TAKHFIF ?? 0d)):#,###} {await GetTafNameAsync(h.CUST_NO)}", 255);
                                AddRow(pk, pm, pt, pt2, pt3, pt4, v.CUST_NO ?? string.Empty, sharh, 0, v.PURSANT);
                            }
                        }

                        if (jamp != 0)
                        {
                            if (string.IsNullOrWhiteSpace(acc.HPOR))
                            {
                                AddLog($"فاکتور {h.NUMBER1}: حساب پورسانت در حساب‌های خودگردان مشخص نشده؛ ردیف پورسانت سند نامتوازن است.");
                            }
                            else
                            {
                                double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                                CL_HESABDARI.GETTAF3(acc.HPOR, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                                if (hk is not null && hm is not null && ht is not null) await CreatHesAsync(hk, hm, ht, "پورسانت");
                                var sharh = LeftTrim($"بابت درصد سهم  فاكتور فروش شماره {num} : {h.NUMBER1}{await GetTafNameAsync(tamir)}", 255);
                                AddRow(hk, hm, ht, null, null, null, acc.HPOR, sharh, jamp, 0);
                            }
                        }
                    }

                    // ── نوشتن به پایگاه‌داده: سربرگ سند (در صورت لزوم) + جزئیات ──
                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");

                    if (isDailyMode)
                    {
                        // سند روزانه از پیش رزرو و روی HEAD_LST نوشته شده؛ اینجا کاری لازم نیست.
                    }
                    else if (singleNeedsNewHeader[R])
                    {
                        // شماره از پیش رزرو و روی HEAD_LST نوشته شده.
                    }
                    else if (singleExistingHeaderDates.TryGetValue(ns, out var existingDate) && existingDate != dateN)
                    {
                        var sharh = await BuildSingleSharhAsync(h);
                        batch.Append(
                            $"UPDATE dbo.DEED_HED SET DATE_S={dateN}, SHARH_S=N'{SqlText(sharh)}', GHATEI=0, NO_S=2, OKF=-1, " +
                            $"USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE N_S={SqlNum(ns)};");
                    }

                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(num)} AND TAG=13;");

                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES_T2,HES_T3,HES_T4,HES,SHARH,BED,BES,ARZD,NUMBER,TAG,RADIF,N_SERI,BANK) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");

                    await _db.DoExecuteSQLAsync(batch.ToString());
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    RecordFailure($"فاکتور {num} (سند {ns}): {ex.Message}");
                }
            });

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.FirstError = firstError;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { result.LastSanadNumber = (long)headRows[i].N_S!.Value; break; }

            AddLog($"GENSANADFROOSH: پایان — {result.SheetCount} فاکتور، موفق={result.Success}.");
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  تخفیف: الگویی (TKHF==2، بر اساس TAKHPERS/CUST_KIND) یا ردیفی
        //  (سایر مقادیر TKHF، بر اساس INVO_LST.N_MOIN) + تراز باقیمانده.
        // ─────────────────────────────────────────────────────────────────
        private static async Task<double> ApplyDiscountAsync(
            HeadRow h, double num, double ns, long dateN,
            double? ckol, double? cmoin, double? ctaf, double? ctaf2, double? ctaf3, double? ctaf4, double arzd,
            int tkhf, SazmanAccounts acc, string optionss,
            Dictionary<(double Number, double CustKind), List<TakhPersRow>> takhPersBySheet,
            Dictionary<double, List<LineDiscountRow>> lineDiscountsBySheet,
            Action<double?, double?, double?, double?, double?, double?, string, string, double, double, double?, double?, int?> addRow,
            Func<double?, double?, double?, string, Task> creatHes,
            Func<string?, Task<string>> getKalaName,
            Action<string> recordFailure)
        {
            double takh = 0d;

            if (tkhf == 2)
            {
                var rst6 = h.CUST_KIND is not null && takhPersBySheet.TryGetValue((num, (double)h.CUST_KIND.Value), out var tp)
                    ? tp : new List<TakhPersRow>();

                foreach (var t in rst6)
                {
                    // TAFPER٪ روی MABL_K همان ردیف کالای INVO_LST که با این کد تخفیف JOIN شده اعمال می‌شود.
                    if (!TryGetAccountCode(t.TAKH_COD, out var takhCodLong)) continue;
                    var amount = Math.Round((t.MABL_K ?? 0d) / 100d * (t.TAFPER ?? 0));
                    if (amount == 0) continue;

                    var sharh = RightTrim($"مبلغ تخفيف فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                    if (OptChar(optionss, 13) == "5")
                    {
                        await creatHes(acc.TFROSH, 3, takhCodLong, $"تخفيف {await getKalaName(t.TAKH_COD)}");
                        addRow(acc.TFROSH, 3, takhCodLong, null, null, null, $"{acc.TFROSH}-3-{takhCodLong}", sharh, amount, 0, null, null, null);
                    }
                    else
                    {
                        try
                        {
                            await creatHes(acc.TFROSH, h.CUST_KIND, takhCodLong, $"تخفيف {t.TAKH_COD}");
                            addRow(acc.TFROSH, h.CUST_KIND, takhCodLong, null, null, null, $"{acc.TFROSH}-{h.CUST_KIND}-{takhCodLong}", sharh, amount, 0, null, null, null);
                        }
                        catch (Exception ex)
                        {
                            recordFailure($"خطا در قسمت تخفيف فروش: {sharh} {num}: {ex.Message}");
                        }
                    }
                    takh += amount;
                }
            }
            else
            {
                var rst7 = lineDiscountsBySheet.TryGetValue(num, out var ld) ? ld : new List<LineDiscountRow>();
                foreach (var d in rst7)
                {
                    if (d.N_MOIN is null) continue;
                    var amount = Math.Round(d.N_MOIN.Value);
                    if (amount == 0) continue;
                    if (!TryGetAccountCode(d.CODE, out var codeLong)) continue;

                    var sharh = RightTrim($"مبلغ تخفيف فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                    if (OptChar(optionss, 13) == "5")
                    {
                        await creatHes(acc.TFROSH, 3, codeLong, $"تخفيف {await getKalaName(d.CODE)}");
                        addRow(acc.TFROSH, 3, codeLong, null, null, null, $"{acc.TFROSH}-3-{codeLong}", sharh, amount, 0, null, null, null);
                    }
                    else
                    {
                        try
                        {
                            await creatHes(acc.TFROSH, h.CUST_KIND, codeLong, $"تخفيف {d.CODE}");
                            addRow(acc.TFROSH, h.CUST_KIND, codeLong, null, null, null, $"{acc.TFROSH}-{h.CUST_KIND}-{codeLong}", sharh, amount, 0, null, null, null);
                        }
                        catch (Exception ex)
                        {
                            recordFailure($"خطا در قسمت تخفيف فروش: {sharh} {num}: {ex.Message}");
                        }
                    }
                    takh += amount;
                }
            }

            var residual = (h.TAKHFIF ?? 0d) - takh;
            if (residual != 0)
            {
                await creatHes(acc.TFROSH, 1, 1, "تخفيف");
                var sharh = RightTrim($"مبلغ تخفيف فاكتور فروش شماره {h.NUMBER1} مورخ{PersianDate(dateN)}", 255);
                if (residual > 0) addRow(acc.TFROSH, 1, 1, null, null, null, $"{acc.TFROSH}-1-1", sharh, Math.Abs(residual), 0, null, null, null);
                else addRow(acc.TFROSH, 1, 1, null, null, null, $"{acc.TFROSH}-1-1", sharh, 0, Math.Abs(residual), null, null, null);
            }

            return takh;
        }
    }
}
