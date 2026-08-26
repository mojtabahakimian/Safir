using Safir.Shared.Interfaces;
using Safir.Shared.Utility;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند حواله خروج سایر مواد» (DEED_HED/DEED_DTL برای
    //  HEAD_LST.TAG=11، NO_S=12).
    //
    //  پورتِ دستیِ SANADKHORUGSAYER در CL_HESABDARI_AUTO_BAZ.cs. برخلاف سند
    //  فروش/برگشت فروش، این تابع هیچ‌کدام از فیلدهای نقد/تخفیف/چک/مالیات/
    //  پورسانتِ HEAD_LST را نمی‌خواند — فقط برای هر ردیف INVO_LST.TAG=11
    //  یک آرتیکل دوطرفه می‌سازد:
    //   • بدهکار: حساب مقصدِ خروج — از N_RASID تعیین می‌شود:
    //       - اگر عددی باشد: شماره فرمول ساخت (HEAD_MANF.FNUMB) و حساب
    //         (N_KOL-NUMBER-TNUMBER) همان فرمول است.
    //       - اگر عددی نباشد: خودِ N_RASID یک کد حساب کامل «ک-م-ت[...]» است
    //         (GETTAF3) و فقط وقتی حساب واقعاً در TDETA_HES موجود باشد
    //         (ISHESAB) ثبت می‌شود؛ وگرنه فقط لاگ می‌شود و ردیف رد می‌شود
    //         — عیناً همان رفتار کد اصلی، نه یک CREATHES خودکار.
    //   • بستانکار: موجودی انبار (MOGODIA-انبار-کد کالا).
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class OtherIssueRebuildResult
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class OtherIssueRebuildService
    {
        private readonly IDatabaseService _db;
        public OtherIssueRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? MOGODIA { get; set; }
            public bool?   SNDKH   { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER    { get; set; }
            public double? FNUMCO    { get; set; }
            public long?   DATE_N    { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }
        }

        private sealed class LineRow
        {
            public double? SHEETNO  { get; set; }
            public double? SANAD_NO { get; set; }
            public string? N_RASID  { get; set; }
            public double? MABL_K   { get; set; }
            public double? MEGHk    { get; set; }
            public string? CODE     { get; set; }
            public int?    ANBAR    { get; set; }
        }

        private sealed class HeadManfRow
        {
            public int?    FNUMB   { get; set; }
            public int?    NUMBER  { get; set; }
            public int?    TNUMBER { get; set; }
            public int?    N_KOL   { get; set; }
            public string? NAMES   { get; set; }
        }

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];
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

        private static bool IsNumericRasid(string? s)
            => !string.IsNullOrWhiteSpace(s) && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

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

        public async Task<OtherIssueRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new OtherIssueRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;
            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>("SELECT TOP 1 MOGODIA, SNDKH FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();
            if (acc.MOGODIA is null)
            {
                result.Success = false;
                RecordFailure("حساب موجودی جنسی (SAZMAN.MOGODIA) تنظیم نشده؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }
            var isDailyMode = acc.SNDKH == true;

            var existingAccounts = new ConcurrentDictionary<(long, long, long), bool>();
            var tafNameCache = new ConcurrentDictionary<string, string>();

            async Task<bool> IsHesabAsync(long kol, long moin, long taf)
            {
                var key = (kol, moin, taf);
                if (existingAccounts.TryGetValue(key, out var cached) && cached) return true;
                var rows = await _db.DoGetDataSQLAsync<int>(
                    "SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf", new { Kol = kol, Moin = moin, Taf = taf });
                var exists = rows.Any();
                if (exists) existingAccounts[key] = true;
                return exists;
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

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, FNUMCO, DATE_N, N_S, USER_NAME FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 11 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"SANADKHORUGSAYER: شروع بازسازی از برگ {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");
            if (headRows.Count == 0) { result.Success = true; return result; }

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101) { AddLog($"برگ {h.NUMBER}: تاریخ نامعتبر."); continue; }
                sheetUsable[i] = true;
            }
            var usableIdx = Enumerable.Range(0, headRows.Count).Where(i => sheetUsable[i]).ToList();

            static string BuildSharh(HeadRow h) => LeftTrim($" حواله خروج ساير مواد از انبار شماره {h.NUMBER}-{h.FNUMCO}مورخ {PersianDate(h.DATE_N!.Value)}", 100);

            var needsNewHeader = new bool[headRows.Count];

            if (isDailyMode)
            {
                var dailyNs = new Dictionary<long, double>();
                var dates = usableIdx.Select(i => headRows[i].DATE_N!.Value).Distinct().ToList();
                if (dates.Count > 0)
                {
                    var minD = dates.Min(); var maxD = dates.Max();
                    foreach (var f in await _db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                        "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 12 AND DATE_S BETWEEN @Min AND @Max", new { Min = minD, Max = maxD }))
                        if (f.DATE_S is not null && f.N_S is not null && !dailyNs.ContainsKey(f.DATE_S.Value)) dailyNs[f.DATE_S.Value] = f.N_S.Value;

                    var missing = dates.Where(d => !dailyNs.ContainsKey(d)).ToList();
                    if (missing.Count > 0)
                    {
                        var requests = missing.Select(d =>
                        {
                            var sample = headRows[usableIdx.First(i => headRows[i].DATE_N!.Value == d)];
                            return new SanadHeaderRequest { DATE_S = d, SHARH_S = BuildSharh(sample), USER_NAME = sample.USER_NAME };
                        }).ToList();
                        var reserved = await SanadNumbering.ReserveBatchAsync(_db, 12, requests);
                        for (int k = 0; k < missing.Count; k++) dailyNs[missing[k]] = reserved[k];
                    }
                }
                foreach (var i in usableIdx)
                {
                    var resolved = dailyNs[headRows[i].DATE_N!.Value];
                    if (headRows[i].N_S != resolved)
                    {
                        headRows[i].N_S = resolved;
                        await _db.DoExecuteSQLAsync("UPDATE dbo.HEAD_LST SET N_S=@Ns WHERE NUMBER=@Num AND TAG=11", new { Ns = resolved, Num = headRows[i].NUMBER!.Value });
                    }
                }
            }
            else
            {
                var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
                var existingHeaderNumbers = new HashSet<double>();
                if (candidateNs.Count > 0)
                {
                    foreach (var found in await _db.DoGetDataSQLAsync<double?>(
                        "SELECT N_S FROM dbo.DEED_HED WHERE NO_S = 12 AND N_S BETWEEN @Min AND @Max", new { Min = candidateNs.Min(), Max = candidateNs.Max() }))
                        if (found.HasValue) existingHeaderNumbers.Add(found.Value);
                }
                var claimed = new HashSet<double>();
                var newHeaderIdx = new List<int>();
                foreach (var i in usableIdx)
                {
                    var ns = headRows[i].N_S;
                    var exists = ns is > 0 && existingHeaderNumbers.Contains(ns.Value);
                    var owns = exists && claimed.Add(ns!.Value);
                    if (!owns) { needsNewHeader[i] = true; newHeaderIdx.Add(i); }
                }
                if (newHeaderIdx.Count > 0)
                {
                    var requests = newHeaderIdx.Select(i => new SanadHeaderRequest { DATE_S = headRows[i].DATE_N!.Value, SHARH_S = BuildSharh(headRows[i]), USER_NAME = headRows[i].USER_NAME }).ToList();
                    var reserved = await SanadNumbering.ReserveBatchAsync(_db, 12, requests);
                    for (int k = 0; k < newHeaderIdx.Count; k++) headRows[newHeaderIdx[k]].N_S = reserved[k];
                }
            }

            var wanted = new HashSet<double>(usableIdx.Select(i => headRows[i].NUMBER!.Value));
            var linesBySheet = new Dictionary<double, List<LineRow>>();
            if (wanted.Count > 0)
            {
                var minN = wanted.Min(); var maxN = wanted.Max();
                foreach (var line in await _db.DoGetDataSQLAsync<LineRow>(
                    "SELECT NUMBER AS SHEETNO, SANAD_NO, N_RASID, MABL_K, MEGHk, CODE, ANBAR FROM dbo.INVO_LST " +
                    "WHERE NUMBER BETWEEN @Min AND @Max AND TAG = 11 ORDER BY NUMBER, id", new { Min = minN, Max = maxN }))
                {
                    if (line.SHEETNO is null || !wanted.Contains(line.SHEETNO.Value)) continue;
                    if (!linesBySheet.TryGetValue(line.SHEETNO.Value, out var b)) linesBySheet[line.SHEETNO.Value] = b = new List<LineRow>();
                    b.Add(line);
                }
            }

            var numericRasids = linesBySheet.SelectMany(kv => kv.Value)
                .Where(l => IsNumericRasid(l.N_RASID)).Select(l => (int)double.Parse(l.N_RASID!, CultureInfo.InvariantCulture))
                .Distinct().ToList();
            var headManfDict = new Dictionary<int, HeadManfRow>();
            if (numericRasids.Count > 0)
            {
                const int batchSize = 1000;
                for (int off = 0; off < numericRasids.Count; off += batchSize)
                {
                    var chunk = numericRasids.Skip(off).Take(batchSize).ToList();
                    var rows = await _db.DoGetDataSQLAsync<HeadManfRow>(
                        "SELECT FNUMB, NUMBER, TNUMBER, N_KOL, NAMES FROM dbo.HEAD_MANF WHERE FNUMB IN @Ids", new { Ids = chunk });
                    foreach (var r in rows) if (r.FNUMB is not null && !headManfDict.ContainsKey(r.FNUMB.Value)) headManfDict[r.FNUMB.Value] = r;
                }
            }

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            var successFlag = 1;

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                if (!sheetUsable[R]) return;
                var h = headRows[R];
                var sheetNo = h.NUMBER!.Value;
                var ns = h.N_S!.Value;
                var dateN = h.DATE_N!.Value;
                var rows = new List<string>();

                void AddDetail(double? hesK, double? hesM, double? hesT, double? hesT2, double? hesT3, double? hesT4,
                               string hes, double bed, double bes, string sharh, double? mhazNo)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},{SqlNum(hesT2)},{SqlNum(hesT3)},{SqlNum(hesT4)}," +
                             $"N'{SqlText(hes)}',{SqlNum(bed)},{SqlNum(bes)},N'{SqlText(LeftTrim(sharh, 255))}',{SqlNum(sheetNo)},{SqlNum(mhazNo)},11)");
                }

                try
                {
                    var lines = linesBySheet.TryGetValue(sheetNo, out var b) ? b : new List<LineRow>();
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrEmpty(line.N_RASID)) continue;
                        var mablK = line.MABL_K ?? 0d;
                        var meghK = line.MEGHk ?? 0d;
                        if (mablK == 0) continue;

                        var codeOk = TryGetAccountCode(line.CODE, out var codeLong) && line.ANBAR is not null;
                        var codeNum = (double)codeLong;
                        var anbar = line.ANBAR ?? 0;

                        void RequireInventoryKeys()
                        {
                            if (!codeOk) throw new InvalidOperationException($"کد کالا ('{line.CODE}') یا انبار برای برگه {sheetNo} معتبر نیست.");
                        }

                        if (IsNumericRasid(line.N_RASID))
                        {
                            var fnumb = (int)double.Parse(line.N_RASID!, CultureInfo.InvariantCulture);
                            if (headManfDict.TryGetValue(fnumb, out var jstt) && jstt.N_KOL is not null && jstt.NUMBER is not null && jstt.TNUMBER is not null)
                            {
                                var hes = $"{jstt.N_KOL}-{jstt.NUMBER}-{jstt.TNUMBER}";
                                var sharh1 = $"حواله خروج ساير شماره {sheetNo}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{meghK} جهت {(jstt.NAMES ?? "").Trim()}";
                                AddDetail(jstt.N_KOL, jstt.NUMBER, jstt.TNUMBER, null, null, null, hes, Math.Round(mablK), 0, sharh1, null);

                                RequireInventoryKeys();
                                var hes2 = $"{acc.MOGODIA}-{anbar}-{codeNum}";
                                var sharh2 = $"حواله خروج  ساير  مواد شماره {sheetNo}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{meghK}";
                                AddDetail(acc.MOGODIA, anbar, codeNum, null, null, null, hes2, 0, Math.Round(mablK), sharh2, line.SANAD_NO);
                            }
                        }
                        else
                        {
                            double? ckol = null, cmoin = null, ctaf = null, ctaf2 = null, ctaf3 = null, ctaf4 = null;
                            CL_HESABDARI.GETTAF3(line.N_RASID, ref ckol, ref cmoin, ref ctaf, ref ctaf2, ref ctaf3, ref ctaf4);
                            if (ctaf is null) continue;

                            var tafName = await GetTafNameAsync(line.N_RASID);
                            var sharh1 = $"حواله خروج ساير شماره {sheetNo}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{meghK} جهت {tafName.Trim()}";

                            if (await IsHesabAsync((long)ckol!.Value, (long)cmoin!.Value, (long)ctaf!.Value))
                            {
                                RequireInventoryKeys();
                                AddDetail(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, line.N_RASID, Math.Round(mablK), 0, sharh1, null);

                                var hes2 = $"{acc.MOGODIA}-{anbar}-{codeNum}";
                                var sharh2 = $"حواله خروج  ساير  مواد شماره {sheetNo}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{meghK}";
                                AddDetail(acc.MOGODIA, anbar, codeNum, null, null, null, hes2, 0, Math.Round(mablK), sharh2, line.SANAD_NO);
                            }
                            else
                            {
                                AddLog($"[SANADKHORUGSAYER] برگه {sheetNo}: حساب متناظر N_RASID='{line.N_RASID}' ({ckol}-{cmoin}-{ctaf}) در TDETA_HES وجود ندارد؛ این ردیف رد شد.");
                            }
                        }
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                    if (!isDailyMode)
                    {
                        if (needsNewHeader[R])
                            batch.Append($"UPDATE dbo.HEAD_LST SET N_S={SqlNum(ns)} WHERE NUMBER={SqlNum(sheetNo)} AND TAG=11;");
                        else
                            batch.Append($"UPDATE dbo.DEED_HED SET DATE_S={SqlNum(h.DATE_N)}, SHARH_S=N'{SqlText(BuildSharh(h))}', " +
                                         $"GHATEI=0, NO_S=12, OKF=-1, USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE NO_S=12 AND N_S={SqlNum(ns)};");
                    }
                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(sheetNo)} AND TAG=11;");
                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES_T2,HES_T3,HES_T4,HES,BED,BES,SHARH,NUMBER,MHAZ_NO,TAG) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");
                    await _db.DoExecuteSQLAsync(batch.ToString());
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    RecordFailure($"برگ {sheetNo} (سند {ns}): {ex.Message}");
                }
            });

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.FirstError = firstError;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { result.LastSanadNumber = (long)headRows[i].N_S!.Value; break; }

            AddLog($"SANADKHORUGSAYER: پایان — {result.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }
    }
}
