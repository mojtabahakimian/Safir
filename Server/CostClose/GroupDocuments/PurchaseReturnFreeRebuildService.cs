using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Utility;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند برگشت خرید آزاد» (DEED_HED/DEED_DTL برای HEAD_LST.TAG=26،
    //  NO_S=3) — پورتِ دستیِ SANAD() از فرم «برگشت خرید آزاد» در AUTO_BAZ،
    //  عیناً از روی سورس VB که صاحب پروژه فرستاد (نه حدس زده شده).
    //
    //  ⚠️ این سرویس دقیقاً همان چیزی است که کشفِ مغایرت کد ۳۶۸/انبار ۲ نشان
    //  داد که غایب است: هیچ‌کدام از ۶ سرویس سند گروهیِ قبلی TAG=26/27 را
    //  پوشش نمی‌دادند، پس این سند هرگز با نرخ تازه بازسازی نمی‌شد.
    //
    //  نکته‌ی کلیدیِ منطق (که علتِ خودِ مغایرت بود): ارزش موجودیِ هر ردیف
    //  Round(MEGHk × AVRAGE) است — نرخ AVRAGE هر سطر INVO_LST.TAG=26 (که
    //  AverageRateRebuildService می‌نویسد)، نه MABL_K خامِ ثبت‌شده روی سطر.
    //  اگر MABL_K با این مقدارِ تازه فرق داشته باشد، اختلاف در یک سطرِ جداگانه
    //  («کنترل») به حساب عملکردِ AMALKARD-99999-CODE پست می‌شود — دقیقاً طبق
    //  کدِ اصلی، نه یک اصلاح.
    //
    //  GETGRPKALA (نامِ گروه کالا برای RADAH=5..10) در سورسِ فرستاده‌شده نبود؛
    //  چون روی این دیتابیس RADAH هرگز از ۵ فراتر نمی‌رود (فقط ۰..۵ دیده شد)،
    //  یک برچسبِ عمومیِ "گروه {n}" جایگزین شده — فقط متنِ توضیح را عوض
    //  می‌کند، نه حساب یا مبلغ را.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class PurchaseReturnFreeRebuildResult
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class PurchaseReturnFreeRebuildService
    {
        private readonly IDatabaseService _db;

        public PurchaseReturnFreeRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? MOGODIA  { get; set; }
            public double? KHARID   { get; set; }
            public double? AMALKARD { get; set; }
            public double? TKHARID  { get; set; }
            public int?    PKHARID  { get; set; }
            public string? ADA      { get; set; }
            public double? SANDOGH  { get; set; }
            public string? HESMBAA  { get; set; }
            public string? OPTIONSS { get; set; }
            public bool?   SNDKH    { get; set; }
            public byte?   ARSESH   { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER    { get; set; }
            public long?   DATE_N    { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }
            public string? CUST_NO   { get; set; }
            public int?    DEPATMAN  { get; set; }
            public int?    SHIFT     { get; set; }
            public double? ARZD      { get; set; }
            public double? MABL_HAZ  { get; set; }
            public string? MOIN_HAZ  { get; set; }
            public double? MBAA      { get; set; }
            public string? HMBAA     { get; set; }
            public double? TAKHFIF   { get; set; }
            public double? M_NAGHD   { get; set; }
            public double? MABL_HAV  { get; set; }
            public string? MOIN_HAV  { get; set; }
            public double? MABL_VAR  { get; set; }
            public string? MOIN_VAR  { get; set; }
            public double? FNUMCO    { get; set; }
            public string? MOLAH     { get; set; }
        }

        private sealed class LineRow
        {
            public string? CODE   { get; set; }
            public double? MEGHk  { get; set; }
            public double? MABL_K { get; set; }
            public double? AVRAGE { get; set; }
            public int?    ANBAR  { get; set; }
            public double? RADAH  { get; set; }
            public string? NAME   { get; set; }
        }

        private sealed class ChequeRow
        {
            public double? N_SERI { get; set; }
            public int?    BANK   { get; set; }
            public long?   DATE_S { get; set; }
            public string? SHOBEH { get; set; }
            public double? MABL   { get; set; }
        }

        // ───────────────────────────── کمکی‌های قالب‌بندی (مثل سرویس‌های خواهر) ─────────────────────────────

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];
        private static string RightTrim(string s, int max) => s.Length <= max ? s : s[^max..];
        private static string PersianDate(long dateN) => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";

        private static bool TryGetAccountCode(string? value, out long result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return false;
            if (double.IsNaN(parsed) || parsed < int.MinValue || parsed > int.MaxValue) return false;
            result = (long)parsed;
            return true;
        }

        private static string GrpKalaLabel(int radahPlus4) => $"گروه {radahPlus4}"; // نگاه کنید توضیح بالای فایل

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

        // ───────── CREATHES/ISHESAB — کش‌های محلی به همین فراخوانی ─────────

        private readonly ConcurrentDictionary<(long, long, long), bool> _existingAccounts = new();
        private readonly ConcurrentDictionary<int, string> _bankNameCache = new();

        private async Task<bool> IsHesabAsync(long kol, long moin, long taf)
        {
            var key = (kol, moin, taf);
            if (_existingAccounts.TryGetValue(key, out var cached) && cached) return true;
            var exists = (await _db.DoGetDataSQLAsync<int>(
                "SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                new { Kol = kol, Moin = moin, Taf = taf })).Any();
            if (exists) _existingAccounts[key] = true;
            return exists;
        }

        private async Task CreatHesAsync(double? kol, double? moin, double? taf, string name)
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
            await _db.DoExecuteSQLAsync(sql, new { Kol = kolV, Moin = moinV, Taf = tafV, Name = accName });
            _existingAccounts[(kolV, moinV, tafV)] = true;
        }

        private async Task<string> GetBankNameAsync(int? bank)
        {
            if (bank is null) return " ";
            if (_bankNameCache.TryGetValue(bank.Value, out var cached)) return cached;
            var name = (await _db.DoGetDataSQLAsync<string>(
                "SELECT NAMES FROM dbo.TCOD_BANKS WHERE CODE=@Code", new { Code = bank.Value })).FirstOrDefault();
            name = string.IsNullOrEmpty(name) ? bank.Value.ToString(CultureInfo.InvariantCulture) : name;
            _bankNameCache[bank.Value] = name;
            return name;
        }

        // ═══════════════════════════════════════════════════════════════
        //  متد اصلی
        // ═══════════════════════════════════════════════════════════════

        public async Task<PurchaseReturnFreeRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new PurchaseReturnFreeRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;
            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>(
                "SELECT TOP 1 MOGODIA, KHARID, AMALKARD, TKHARID, PKHARID, ADA, SANDOGH, HESMBAA, " +
                "OPTIONSS, SNDKH, ARSESH FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();

            if (acc.MOGODIA is null || acc.KHARID is null || acc.AMALKARD is null || acc.TKHARID is null
                || acc.PKHARID is null || string.IsNullOrWhiteSpace(acc.ADA) || acc.SANDOGH is null)
            {
                result.Success = false;
                RecordFailure("حساب‌های پایه (موجودی/خرید/عملکرد/تخفیف/پایاپای خرید/اسناد دریافتنی/صندوق) در SAZMAN تنظیم نشده‌اند؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            var optionss = acc.OPTIONSS ?? string.Empty;
            var isDailyMode = acc.SNDKH == true;

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, DATE_N, N_S, USER_NAME, CUST_NO, DEPATMAN, SHIFT, ARZD, MABL_HAZ, MOIN_HAZ, " +
                "MBAA, HMBAA, TAKHFIF, M_NAGHD, MABL_HAV, MOIN_HAV, MABL_VAR, MOIN_VAR, FNUMCO, MOLAH FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 26 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"برگشت خرید آزاد (TAG=26): شروع بازسازی از {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");
            if (headRows.Count == 0) { result.Success = true; return result; }

            // ردیف همراهِ TAG=27 در HEAD_LST باید موجود باشد؛ FK_DEED_DTL_HEAD_LST دقیقاً همین را چک می‌کند.
            // بدون آن، برگه به‌جای throw کردنِ خطای FK، با یک هشدار رد می‌شود.
            var pairedNumbers = new HashSet<double>(await _db.DoGetDataSQLAsync<double>(
                "SELECT NUMBER FROM dbo.HEAD_LST WHERE TAG = 27 AND NUMBER BETWEEN @From AND @To",
                new { From = fromNumber, To = toNumber }));

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101) { AddLog($"برگه {h.NUMBER}: تاریخ نامعتبر."); continue; }
                if (!pairedNumbers.Contains(h.NUMBER.Value)) { AddLog($"برگه {h.NUMBER}: ردیف همراه TAG=27 در HEAD_LST یافت نشد — رد شد."); continue; }
                sheetUsable[i] = true;
            }
            var usableIdx = Enumerable.Range(0, headRows.Count).Where(i => sheetUsable[i]).ToList();

            static string BuildSharh(HeadRow h) => LeftTrim($"فاكتور برگشت خريد شماره {h.NUMBER} مورخ {PersianDate(h.DATE_N!.Value)}", 255);

            // ───── شماره‌گذاری سند (سند روزانه یا تکی، طبق SNDKH — مثل سرویس‌های خواهر) ─────
            var dailyNs = new Dictionary<long, double>();
            var singleExistingHeaderDates = new Dictionary<double, long?>();
            var singleNeedsNewHeader = new bool[headRows.Count];

            if (isDailyMode)
            {
                var dates = usableIdx.Select(i => headRows[i].DATE_N!.Value).Distinct().ToList();
                if (dates.Count > 0)
                {
                    var minD = dates.Min(); var maxD = dates.Max();
                    foreach (var f in await _db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 3 AND DATE_S BETWEEN @Min AND @Max", new { Min = minD, Max = maxD }))
                        if (f.DATE_S is not null && f.N_S is not null && !dailyNs.ContainsKey(f.DATE_S.Value)) dailyNs[f.DATE_S.Value] = f.N_S.Value;

                    var missing = dates.Where(d => !dailyNs.ContainsKey(d)).ToList();
                    if (missing.Count > 0)
                    {
                        var requests = missing.Select(d =>
                        {
                            var sample = headRows[usableIdx.First(i => headRows[i].DATE_N!.Value == d)];
                            return new SanadHeaderRequest { DATE_S = d, SHARH_S = BuildSharh(sample), USER_NAME = sample.USER_NAME };
                        }).ToList();
                        var reserved = await SanadNumbering.ReserveBatchAsync(_db, 3, requests);
                        for (int k = 0; k < missing.Count; k++) dailyNs[missing[k]] = reserved[k];
                    }
                }
                foreach (var i in usableIdx)
                {
                    var resolved = dailyNs[headRows[i].DATE_N!.Value];
                    if (headRows[i].N_S != resolved)
                    {
                        headRows[i].N_S = resolved;
                        await _db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=26", new { Ns = resolved, Num = headRows[i].NUMBER!.Value });
                    }
                }
            }
            else
            {
                var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
                if (candidateNs.Count > 0)
                {
                    var found = await _db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 3 AND N_S BETWEEN @Min AND @Max", new { Min = candidateNs.Min(), Max = candidateNs.Max() });
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
                    var reserved = await SanadNumbering.ReserveBatchAsync(_db, 3, requests);
                    for (int k = 0; k < newHeaderIdx.Count; k++)
                    {
                        headRows[newHeaderIdx[k]].N_S = reserved[k];
                        await _db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=26", new { Ns = reserved[k], Num = headRows[newHeaderIdx[k]].NUMBER!.Value });
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
                var arzd = h.ARZD ?? 1d;
                var rows = new List<string>();

                void AddRow(double? hesK, double? hesM, double? hesT, string hes, string sharh, double bed, double bes)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},NULL,NULL,NULL," +
                             $"N'{SqlText(hes)}',N'{SqlText(RightTrim(sharh, 255))}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(arzd)},{SqlNum(num)},27,NULL,NULL,NULL)");
                }

                try
                {
                    double? ckol = null, cmoin = null, ctaf = null, ctaf2 = null, ctaf3 = null, ctaf4 = null;
                    if (!string.IsNullOrWhiteSpace(h.CUST_NO))
                        CL_HESABDARI.GETTAF3(h.CUST_NO, ref ckol, ref cmoin, ref ctaf, ref ctaf2, ref ctaf3, ref ctaf4);

                    // JAMF = مجموع ارزشِ همه‌ی ردیف‌های TAG=26 همین برگه (بر اساس MABL_K خامِ ثبت‌شده،
                    // عیناً VB — نه نرخ تازه؛ نرخ تازه فقط برای سطرِ موجودی و کنترل استفاده می‌شود پایین‌تر).
                    var jamf = (await _db.DoGetDataSQLAsync<double?>(
                        "SELECT SUM(MABL_K) FROM dbo.INVO_LST WHERE NUMBER=@N AND TAG=26", new { N = num })).FirstOrDefault() ?? 0d;
                    var jamch = (await _db.DoGetDataSQLAsync<double?>(
                        "SELECT SUM(MABL) FROM dbo.PAY_GETD WHERE TAG=27 AND NUMBER=@N", new { N = num })).FirstOrDefault() ?? 0d;

                    if (jamf + (h.MBAA ?? 0d) > 0)
                    {
                        var sharh = $"فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, sharh, jamf + (h.MBAA ?? 0d), 0);
                    }

                    if ((h.MABL_HAZ ?? 0d) != 0)
                    {
                        var sharh1 = $"خدمات فاكتور برگشت خريد  شماره {num}-{h.FNUMCO} مورخ{PersianDate(dateN)}";
                        AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, sharh1, h.MABL_HAZ!.Value, 0);

                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        if (!string.IsNullOrWhiteSpace(h.MOIN_HAZ))
                        {
                            CL_HESABDARI.GETTAF3(h.MOIN_HAZ, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                            if (hk is not null && hm is not null && ht is not null) await CreatHesAsync(hk, hm, ht, h.MOIN_HAZ!);
                        }
                        var sharh2 = $"خدمات فاكتور برگشت خريد شماره {num} - {h.MOIN_HAZ}";
                        AddRow(hk, hm, ht, h.MOIN_HAZ ?? string.Empty, sharh2, 0, h.MABL_HAZ!.Value);
                    }

                    if (jamch != 0d)
                    {
                        var cheques = await _db.DoGetDataSQLAsync<ChequeRow>(
                            "SELECT N_SERI, BANK, DATE_S, SHOBEH, MABL FROM dbo.PAY_GETD WHERE NUMBER=@N AND TAG=27", new { N = num });
                        double? akol = null, amoin = null, ataf = null, ataf2 = null, ataf3 = null, ataf4 = null;
                        CL_HESABDARI.GETTAF3(acc.ADA, ref akol, ref amoin, ref ataf, ref ataf2, ref ataf3, ref ataf4);
                        foreach (var chk in cheques)
                        {
                            var bankName = await GetBankNameAsync(chk.BANK);
                            var s1 = $"چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}";
                            AddRow(akol, amoin, ataf, acc.ADA ?? string.Empty, s1, chk.MABL ?? 0d, 0);

                            var s2 = $"ف.ف.{num} - چك {chk.N_SERI}بانك {bankName} {chk.SHOBEH} مورخ {PersianDate(chk.DATE_S ?? 0)}";
                            AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, s2, 0, chk.MABL ?? 0d);
                        }
                    }

                    if ((h.M_NAGHD ?? 0d) != 0)
                    {
                        var sharh1 = $"مبلغ نقد فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, sharh1, 0, h.M_NAGHD!.Value);

                        var sharh2 = $"مبلغ نقد فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        var hesText = $"{acc.SANDOGH}-{h.DEPATMAN}-{h.SHIFT}";
                        AddRow(acc.SANDOGH, h.DEPATMAN, h.SHIFT, hesText, sharh2, h.M_NAGHD!.Value, 0);
                    }

                    if ((h.TAKHFIF ?? 0d) != 0)
                    {
                        var sharh1 = $"مبلغ تخفيف فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        var hesText = $"{acc.TKHARID}-1-1";
                        await CreatHesAsync(acc.TKHARID, 1, 1, "تخفیف برگشت خرید");
                        AddRow(acc.TKHARID, 1, 1, hesText, sharh1, h.TAKHFIF!.Value, 0);

                        var sharh2 = $"مبلغ تخفيف فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, sharh2, 0, h.TAKHFIF!.Value);
                    }

                    if ((h.MABL_HAV ?? 0d) != 0)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        if (!string.IsNullOrWhiteSpace(h.MOIN_HAV))
                            CL_HESABDARI.GETTAF3(h.MOIN_HAV, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                        var sharh1 = $"مبلغ حواله فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(hk, hm, ht, h.MOIN_HAV ?? string.Empty, sharh1, h.MABL_HAV!.Value, 0);

                        var sharh2 = $"مبلغ حواله فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, sharh2, 0, h.MABL_HAV!.Value);
                    }

                    if ((h.MABL_VAR ?? 0d) != 0)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        if (!string.IsNullOrWhiteSpace(h.MOIN_VAR))
                            CL_HESABDARI.GETTAF3(h.MOIN_VAR, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                        var sharh1 = $"مبلغ واريزي فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(hk, hm, ht, h.MOIN_VAR ?? string.Empty, sharh1, h.MABL_VAR!.Value, 0);

                        var sharh2 = $"مبلغ واريزي فاكتور برگشت خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(ckol, cmoin, ctaf, h.CUST_NO ?? string.Empty, sharh2, 0, h.MABL_VAR!.Value);
                    }

                    // ───── ردیف‌های موجودی + کنترلِ نرخ (اصلِ همان چیزی که مغایرت را می‌بست) ─────
                    var khmavad = 0d; var khnim = 0d; var khsakht = 0d; var khsay = 0d; var bazar = 0d;
                    var hs = new double[7];

                    var lines = await _db.DoGetDataSQLAsync<LineRow>(
                        "SELECT L.CODE, L.MEGHk, L.MABL_K, L.AVRAGE, L.ANBAR, S.RADAH, S.NAME " +
                        "FROM dbo.INVO_LST L INNER JOIN dbo.STUF_DEF S ON L.CODE = S.CODE " +
                        "WHERE L.NUMBER=@N AND L.TAG=26", new { N = num });

                    foreach (var line in lines)
                    {
                        if (!TryGetAccountCode(line.CODE, out var codeLong)) continue;
                        var codeNum = (double)codeLong;
                        var meghk = line.MEGHk ?? 0d;
                        var avrage = line.AVRAGE ?? 0d;
                        var mablK = line.MABL_K ?? 0d;
                        var name = line.NAME ?? " ";
                        var fresh = Math.Round(meghk * avrage);

                        if (fresh != 0d)
                        {
                            await CreatHesAsync(acc.MOGODIA, line.ANBAR, codeNum, name);
                            var hesText = $"{acc.MOGODIA}-{line.ANBAR}-{codeNum}";
                            var sharh = $"برگشت خريد فاكتور شماره {num} مورخ {PersianDate(dateN)}فروشنده: {await GetTafNameAsync(h.CUST_NO)}";
                            AddRow(acc.MOGODIA, line.ANBAR, codeNum, hesText, sharh, 0, fresh);

                            switch ((int)(line.RADAH ?? -1))
                            {
                                case 1: khmavad += mablK; break;
                                case 2: khnim += mablK; break;
                                case 3: khsakht += mablK; break;
                                case 4: bazar += mablK; break;
                                case 5: hs[1] += mablK; break;
                                case 6: hs[2] += mablK; break;
                                case 7: hs[3] += mablK; break;
                                case 8: hs[4] += mablK; break;
                                case 9: hs[5] += mablK; break;
                                case 10: hs[6] += mablK; break;
                                default: khsay += mablK; break;
                            }
                        }

                        if (mablK != fresh)
                        {
                            await CreatHesAsync(acc.AMALKARD, 99999, codeNum, name);
                            var hesText = $"{acc.AMALKARD}-99999-{codeNum}";
                            var sharh = $"برگشت خريد فاكتور شماره {num} مورخ {PersianDate(dateN)}فروشنده: {await GetTafNameAsync(h.CUST_NO)}";
                            if (mablK > fresh)
                                AddRow(acc.AMALKARD, 99999, codeNum, hesText, sharh, 0, mablK - fresh);
                            else
                                AddRow(acc.AMALKARD, 99999, codeNum, hesText, sharh, fresh - mablK, 0);
                        }
                    }

                    var custName = await GetTafNameAsync(h.CUST_NO);

                    if (khmavad != 0)
                    {
                        await CreatHesAsync(acc.KHARID, 1, 2, "برگشت مواد اولیه");
                        var sharh = $" برگشت خريد مواد اوليه فاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.KHARID, 1, 2, $"{acc.KHARID}-1-2", sharh, 0, khmavad);
                    }
                    if (khnim != 0)
                    {
                        await CreatHesAsync(acc.KHARID, 2, 2, "برگشت نیمه ساخته");
                        var sharh = $"برگشت خريد نيمه ساخته فاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.KHARID, 2, 2, $"{acc.KHARID}-2-2", sharh, 0, khnim);
                    }
                    if (khsakht != 0)
                    {
                        await CreatHesAsync(acc.KHARID, 3, 2, "برگشت ساخته شده");
                        var sharh = $"برگشت خريد ساخته شده فاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.KHARID, 3, 2, $"{acc.KHARID}-3-2", sharh, 0, khsakht);
                    }
                    if (bazar != 0)
                    {
                        await CreatHesAsync(acc.KHARID, 4, 2, "برگشت بازرگانی");
                        var sharh = $"برگشت خريد بازرگاني فاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.KHARID, 4, 2, $"{acc.KHARID}-4-2", sharh, 0, bazar);
                    }
                    if (khsay != 0)
                    {
                        await CreatHesAsync(acc.KHARID, 11, 2, "برگشت سایر 2");
                        var sharh = $"برگشت خريد ساير فاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.KHARID, 11, 2, $"{acc.KHARID}-11-2", sharh, 0, khsay);
                    }
                    var hs7 = 0d;
                    for (int i = 1; i <= 6; i++)
                    {
                        if (hs[i] == 0) continue;
                        var grpName = GrpKalaLabel(i + 4);
                        await CreatHesAsync(acc.KHARID, i + 4, 2, $"برگشت {grpName}");
                        var sharh = $"برگشت خريد {grpName} فاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.KHARID, i + 4, 2, $"{acc.KHARID}-{i + 4}-2", sharh, 0, hs[i]);
                        hs7 += hs[i];
                    }
                    if (khsay + khsakht + khnim + khmavad + bazar + hs7 > 0)
                    {
                        var sharh = $"خريدفاكتورشماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)}فروشنده: {custName}";
                        AddRow(acc.PKHARID, 1, 1, $"{acc.PKHARID}-1-1", sharh, khsay + khsakht + khnim + khmavad + bazar + hs7, 0);
                    }

                    if ((h.MBAA ?? 0d) != 0)
                    {
                        double? hk = null, hm = null, ht = null, ht2 = null, ht3 = null, ht4 = null;
                        var hesText = h.HMBAA ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(h.HMBAA))
                            CL_HESABDARI.GETTAF3(h.HMBAA, ref hk, ref hm, ref ht, ref ht2, ref ht3, ref ht4);
                        var sharh = $"{acc.ARSESH}% مالليات بر ارزش افزوده فاكتور خريد شماره {num} مورخ{PersianDate(dateN)}";
                        AddRow(hk, hm, ht, hesText, sharh, 0, h.MBAA!.Value);
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                    if (!isDailyMode && !singleNeedsNewHeader[R] &&
                        singleExistingHeaderDates.TryGetValue(ns, out var exDate) && exDate != dateN)
                    {
                        var sharh = BuildSharh(h);
                        batch.Append($"UPDATE dbo.DEED_HED SET DATE_S={dateN}, SHARH_S=N'{SqlText(sharh)}', GHATEI=0, NO_S=3, OKF=-1, " +
                                     $"USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE N_S={SqlNum(ns)};");
                    }
                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(num)} AND TAG=27;");
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
                    RecordFailure($"برگه برگشت خرید آزاد {num} (سند {ns}): {ex.Message}");
                }
            });

            long? last = null;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { last = (long)headRows[i].N_S!.Value; break; }

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.FirstError = firstError;
            result.LastSanadNumber = last;
            AddLog($"برگشت خرید آزاد: پایان — {result.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }

        private readonly ConcurrentDictionary<string, string> _tafNameCache = new();

        private async Task<string> GetTafNameAsync(string? hes)
        {
            var key = hes ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return " ";
            if (_tafNameCache.TryGetValue(key, out var cached)) return cached;
            double? k = null, m = null, t = null, t2 = null, t3 = null, t4 = null;
            CL_HESABDARI.GETTAF3(key, ref k, ref m, ref t, ref t2, ref t3, ref t4);
            if (k is null || m is null || t is null) { _tafNameCache[key] = " "; return " "; }
            var name = (await _db.DoGetDataSQLAsync<string>(
                "SELECT NAME FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                new { Kol = (long)k.Value, Moin = (long)m.Value, Taf = (long)t.Value })).FirstOrDefault();
            name = string.IsNullOrEmpty(name) ? " " : name;
            _tafNameCache[key] = name;
            return name;
        }
    }
}
