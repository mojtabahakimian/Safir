using Safir.Shared.Interfaces;
using Safir.Shared.Utility;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند انبارگردانی» (DEED_HED/DEED_DTL برای ANBGRD_HEAD، NO_S=17).
    //
    //  پورتِ دستیِ GENSANADANBARGARD در CL_HESABDARI_AUTO_BAZ.cs. برخلاف بقیه‌ی
    //  اسناد گروهی، سرِ سند اینجا از HEAD_LST نمی‌آید — از ANBGRD_HEAD
    //  (GRD_NUM به‌جای NUMBER، GRD_DATE به‌جای DATE_N، GRD_ANBAR، GRD_HES
    //  به‌عنوان حساب مقابلِ مغایرت). ردیف‌های شمارش از ANBGRD_LST می‌آیند؛
    //  فقط ردیف‌هایی که (MOG-NUM1)<>0 و (MOG-NUM2)<>0 هستند در نظر گرفته
    //  می‌شوند، و اختلاف واقعی از EKH=MOG-NUM3 محاسبه می‌شود — دقیقاً همان
    //  دو ستون (tartib/تفکیک TAG) که در بازسازیِ CHK-01/CC_sp_S05_Gate هم
    //  برای طبقه‌بندی ردیف‌های انبارگردانی استفاده شد.
    //
    //  برای هر قلم با اختلاف غیرصفر یک ردیف موجودی انبار (MOGODIA-انبار-کد)
    //  ثبت می‌شود (BES اگر مازاد، BED اگر کسری)، و جمع کل مغایرت‌ها در یک
    //  ردیف مقابل به حساب GRD_HES سرِ برگه می‌خورد.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class StockCountRebuildResult
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class StockCountRebuildService
    {
        private readonly IDatabaseService _db;
        public StockCountRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? MOGODIA { get; set; }
        }

        private sealed class HeadRow
        {
            public int?    GRD_NUM   { get; set; }
            public long?   GRD_DATE  { get; set; }
            public int?    GRD_ANBAR { get; set; }
            public string? GRD_HES   { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }
        }

        private sealed class LineRow
        {
            public int?    GRD_NUM { get; set; }
            public string? CODE    { get; set; }
            public double? MOG     { get; set; }
            public double? NUM1    { get; set; }
            public double? NUM2    { get; set; }
            public double? NUM3    { get; set; }
            public double? MABL    { get; set; }
            public double? EKH     { get; set; }
        }

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];
        private static string PersianDate(long dateN) => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";

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

        public async Task<StockCountRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new StockCountRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;
            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>("SELECT TOP 1 MOGODIA FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();
            if (acc.MOGODIA is null)
            {
                result.Success = false;
                RecordFailure("حساب موجودی جنسی (SAZMAN.MOGODIA) تنظیم نشده؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT GRD_NUM, GRD_DATE, GRD_ANBAR, GRD_HES, N_S, USER_NAME FROM dbo.ANBGRD_HEAD " +
                "WHERE GRD_NUM BETWEEN @From AND @To AND GRD_DATE BETWEEN @DateFrom AND @DateTo ORDER BY GRD_NUM",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"GENSANADANBARGARD: شروع بازسازی از برگ {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");
            if (headRows.Count == 0) { result.Success = true; return result; }

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.GRD_NUM is null || h.GRD_DATE is null || h.GRD_DATE < 10101) { AddLog($"برگ {h.GRD_NUM}: تاریخ نامعتبر."); continue; }
                sheetUsable[i] = true;
            }
            var usableIdx = Enumerable.Range(0, headRows.Count).Where(i => sheetUsable[i]).ToList();

            static string BuildSharh(HeadRow h) => LeftTrim($" انبار گرداني شماره {h.GRD_NUM} از انبار {h.GRD_ANBAR} مورخ {PersianDate(h.GRD_DATE!.Value)}", 100);

            var existingHeaderNumbers = new HashSet<double>();
            var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
            if (candidateNs.Count > 0)
            {
                foreach (var found in await _db.DoGetDataSQLAsync<double?>(
                    "SELECT N_S FROM dbo.DEED_HED WHERE NO_S = 17 AND N_S BETWEEN @Min AND @Max", new { Min = candidateNs.Min(), Max = candidateNs.Max() }))
                    if (found.HasValue) existingHeaderNumbers.Add(found.Value);
            }

            var needsNewHeader = new bool[headRows.Count];
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
                var requests = newHeaderIdx.Select(i => new SanadHeaderRequest
                {
                    DATE_S = headRows[i].GRD_DATE!.Value,
                    SHARH_S = BuildSharh(headRows[i]),
                    USER_NAME = headRows[i].USER_NAME
                }).ToList();
                var reserved = await SanadNumbering.ReserveBatchAsync(_db, 17, requests);
                for (int k = 0; k < newHeaderIdx.Count; k++)
                {
                    headRows[newHeaderIdx[k]].N_S = reserved[k];
                    await _db.DoExecuteSQLAsync("UPDATE dbo.ANBGRD_HEAD SET N_S=@Ns WHERE GRD_NUM=@Num", new { Ns = reserved[k], Num = headRows[newHeaderIdx[k]].GRD_NUM!.Value });
                }
            }

            var wanted = new HashSet<int>(usableIdx.Select(i => headRows[i].GRD_NUM!.Value));
            var linesByHead = new Dictionary<int, List<LineRow>>();
            if (wanted.Count > 0)
            {
                var minN = wanted.Min(); var maxN = wanted.Max();
                foreach (var line in await _db.DoGetDataSQLAsync<LineRow>(
                    "SELECT *, MOG - NUM3 AS EKH FROM dbo.ANBGRD_LST " +
                    "WHERE (MOG - NUM2 <> 0) AND (MOG - NUM1 <> 0) AND GRD_NUM BETWEEN @Min AND @Max", new { Min = minN, Max = maxN }))
                {
                    if (line.GRD_NUM is null || !wanted.Contains(line.GRD_NUM.Value)) continue;
                    if (!linesByHead.TryGetValue(line.GRD_NUM.Value, out var b)) linesByHead[line.GRD_NUM.Value] = b = new List<LineRow>();
                    b.Add(line);
                }
            }

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            var successFlag = 1;

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                if (!sheetUsable[R]) return;
                var h = headRows[R];
                var grdNum = h.GRD_NUM!.Value;
                var ns = h.N_S!.Value;
                var dateN = h.GRD_DATE!.Value;
                var rows = new List<string>();
                var extraStatements = new List<string>();

                void AddRow(double? hesK, double? hesM, double? hesT, double? hesT2, double? hesT3, double? hesT4,
                            string hes, string sharh, double bed, double bes)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},{SqlNum(hesT2)},{SqlNum(hesT3)},{SqlNum(hesT4)}," +
                             $"N'{SqlText(hes)}',N'{SqlText(LeftTrim(sharh, 255))}',{SqlNum(bed)},{SqlNum(bes)})");
                }

                try
                {
                    double jamf = 0d;
                    var lines = linesByHead.TryGetValue(grdNum, out var l) ? l : new List<LineRow>();
                    foreach (var line in lines)
                    {
                        var lastmab = line.MABL ?? 0d;
                        var ekh = line.EKH ?? 0d;
                        var itemDiff = Math.Round(lastmab * ekh);

                        if (itemDiff != 0d)
                        {
                            var hes = $"{acc.MOGODIA}-{h.GRD_ANBAR}-{line.CODE}";
                            if (ekh > 0)
                            {
                                var sharh = $" انبار گرداني شماره {grdNum} از انبار {h.GRD_ANBAR} مورخ {PersianDate(dateN)} به مقدار{ekh}";
                                AddRow(acc.MOGODIA, h.GRD_ANBAR, TryParseCode(line.CODE), null, null, null, hes, sharh, 0, itemDiff);
                            }
                            else
                            {
                                var sharh = $" انبار گرداني شماره {grdNum} از انبار {h.GRD_ANBAR} مورخ {PersianDate(dateN)} به مقدار{ekh * -1}";
                                AddRow(acc.MOGODIA, h.GRD_ANBAR, TryParseCode(line.CODE), null, null, null, hes, sharh, Math.Round(lastmab * ekh * -1), 0);
                            }
                        }

                        extraStatements.Add($"UPDATE dbo.ANBGRD_LST SET MABL={SqlNum(lastmab)} WHERE GRD_NUM={grdNum} AND CODE=N'{SqlText(line.CODE)}';");
                        jamf += itemDiff;
                    }

                    if (jamf != 0d)
                    {
                        double? ckol = null, cmoin = null, ctaf = null, ctaf2 = null, ctaf3 = null, ctaf4 = null;
                        CL_HESABDARI.GETTAF3(h.GRD_HES, ref ckol, ref cmoin, ref ctaf, ref ctaf2, ref ctaf3, ref ctaf4);
                        var sharh = $"انبار گرداني شماره {grdNum} از انبار {h.GRD_ANBAR} مورخ {PersianDate(dateN)}";
                        if (jamf > 0d)
                            AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.GRD_HES ?? string.Empty, sharh, jamf, 0);
                        else
                            AddRow(ckol, cmoin, ctaf, ctaf2, ctaf3, ctaf4, h.GRD_HES ?? string.Empty, sharh, 0, jamf * -1);
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                    if (!needsNewHeader[R])
                    {
                        var sharhS = BuildSharh(h);
                        batch.Append($"UPDATE dbo.DEED_HED SET DATE_S={SqlNum(h.GRD_DATE)}, SHARH_S=N'{SqlText(sharhS)}', " +
                                     $"GHATEI=0, NO_S=17, OKF=1, USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE NO_S=17 AND N_S={SqlNum(ns)};");
                    }
                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE N_S={SqlNum(ns)};");
                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES_T2,HES_T3,HES_T4,HES,SHARH,BED,BES) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    foreach (var s in extraStatements) batch.Append(s);
                    batch.Append("COMMIT TRANSACTION;");
                    await _db.DoExecuteSQLAsync(batch.ToString());
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    RecordFailure($"برگ {grdNum} (سند {ns}): {ex.Message}");
                }
            });

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.FirstError = firstError;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { result.LastSanadNumber = (long)headRows[i].N_S!.Value; break; }

            AddLog($"GENSANADANBARGARD: پایان — {result.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }

        private static double? TryParseCode(string? code)
            => double.TryParse(code, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
