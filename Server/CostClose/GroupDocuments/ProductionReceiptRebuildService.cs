using Safir.Shared.Interfaces;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند ورود کالای ساخته‌شده به انبار» (DEED_HED/DEED_DTL برای
    //  HEAD_LST.TAG=9، NO_S=9).
    //
    //  پورتِ دستیِ SANADVORUDSAKHT در CL_HESABDARI_AUTO_BAZ.cs.
    //
    //  ⚠️ برخلاف بقیه‌ی اسناد گروهی، این تابع اصلاً حالت «سند روزانه»
    //  (SNDKH) ندارد — همیشه یک سند مستقل به‌ازای هر برگه.
    //
    //  ⚠️ دو مسیر کاملاً متفاوت دارد که با OPTIONSS[56] انتخاب می‌شوند:
    //   • OPTIONSS[56] <> '5': فرمول ساخت فقط از روی کد کالا پیدا می‌شود
    //     (هر فرمولی که HEAD_MANF.CODE با کد کالا یکی باشد).
    //   • OPTIONSS[56] == '5': فرمول باید دقیقاً همان FNUMB باشد که خودِ
    //     ردیف INVO_LST در ستون N_KOL مشخص کرده (HEAD_MANF.FNUMB =
    //     ISNULL(INVO_LST.N_KOL,0)) — روی این دیتابیس OPTIONSS[56]='5' است،
    //     یعنی این شرکت از همین مسیر دوم استفاده می‌کند؛ ولی طبق دستور کاربر
    //     («هاردکد ننویس»)، هر دو مسیر کامل پیاده شده‌اند.
    //
    //  اگر SAZMAN.SANAT صریحاً false باشد (شرکت غیرصنعتی)، کد اصلی اصلاً
    //  سندی نمی‌سازد و فقط ردیف‌های قدیمی TAG=9 در بازه را پاک می‌کند —
    //  همان رفتار اینجا هم حفظ شده.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class ProductionReceiptRebuildResult
    {
        public bool         Success         { get; set; }
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class ProductionReceiptRebuildService
    {
        private readonly IDatabaseService _db;
        public ProductionReceiptRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? CONKAL   { get; set; }
            public double? MOGODIA  { get; set; }
            public bool?   SANAT    { get; set; }
            public string? OPTIONSS { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER    { get; set; }
            public double? FNUMCO    { get; set; }
            public long?   DATE_N    { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }
        }

        // ردیفِ سرجمعِ هر (برگه، کد کالا، انبار) — چه با N_KOL (مسیر ۵۶='5') چه بدون آن.
        private sealed class ChrstRow
        {
            public double? NUMBER    { get; set; }
            public double? N_KOL     { get; set; }
            public int?    ANBAR     { get; set; }
            public string? CODE      { get; set; }
            public double? SumOfMEGHk{ get; set; }
            public string? NAME      { get; set; }
        }

        // ردیفِ اقلام فرمول (DTL_MANF) برای هر (برگه، کد فرمول COM، انبار[، FNUMB]).
        private sealed class JstLineRow
        {
            public string? CODE      { get; set; }
            public double? MABLK     { get; set; }
            public string? NAME      { get; set; }
            public double? NUMBER    { get; set; }
            public double? SumOfMEGHk{ get; set; }
            public string? COM       { get; set; }
            public double? MEGHM     { get; set; }
            public int?    ANBAR     { get; set; }
            public int?    FNUMB     { get; set; }
        }

        // نرخ دستمزد/سربارِ سربرگ فرمول (HEAD_MANF).
        private sealed class ManfRateRow
        {
            public double? IMBIBE_MANF { get; set; }
            public double? IMBIBE_SAR  { get; set; }
            public string? CODE        { get; set; }
            public int?    FNUMB       { get; set; }
            public double? NUMBER      { get; set; }
            public double? MEGHk       { get; set; }
            public string? NAME        { get; set; }
        }

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];
        private static string PersianDate(long dateN) => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";
        private static string OptChar(string? options, int pos)
            => (options != null && pos >= 1 && pos <= options.Length) ? options.Substring(pos - 1, 1) : "";

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

        public async Task<ProductionReceiptRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new ProductionReceiptRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;
            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>("SELECT TOP 1 CONKAL, MOGODIA, SANAT, OPTIONSS FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();
            if (acc.CONKAL is null || acc.MOGODIA is null)
            {
                result.Success = false;
                RecordFailure("حساب‌های پایه (کنترل کالا/موجودی جنسی) در SAZMAN تنظیم نشده‌اند؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            // شرکت غیرصنعتی: هیچ سندی ساخته نمی‌شود، فقط ردیف‌های قدیمی پاک می‌شوند.
            if (!(acc.SANAT == true || acc.SANAT is null))
            {
                await _db.DoExecuteSQLAsync(
                    "DELETE FROM dbo.DEED_DTL WHERE TAG = 9 AND NUMBER BETWEEN @From AND @To",
                    new { From = fromNumber, To = toNumber });
                AddLog("SANADVORUDSAKHT: شرکت در حالت غیرصنعتی است (SANAT=false)؛ فقط ردیف‌های قدیمی پاک شدند.");
                result.Success = true;
                return result;
            }

            var isOption56_5 = OptChar(acc.OPTIONSS, 56) == "5";

            var existingAccounts = new ConcurrentDictionary<(long, long, long), bool>();
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

            async Task CreatHesAsync(double? kol, double? moin, double? taf, string name)
            {
                if (kol is null || moin is null || taf is null) return;
                var kolV = (long)kol.Value; var moinV = (long)moin.Value; var tafV = (long)taf.Value;
                if (await IsHesabAsync(kolV, moinV, tafV)) return;
                var accName = LeftTrim(name ?? string.Empty, 250);
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
                }
            }

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, FNUMCO, DATE_N, N_S, USER_NAME FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 9 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"SANADVORUDSAKHT: شروع بازسازی از برگ {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");
            if (headRows.Count == 0) { result.Success = true; return result; }

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101) { AddLog($"برگ {h.NUMBER}: تاریخ نامعتبر."); continue; }
                sheetUsable[i] = true;
            }
            var usableIdx = Enumerable.Range(0, headRows.Count).Where(i => sheetUsable[i]).ToList();

            static string BuildSharh(HeadRow h) => LeftTrim($" برگه ورود كالا به انبار شماره {h.NUMBER}-{h.FNUMCO}مورخ {PersianDate(h.DATE_N!.Value)}", 100);

            // بدون حالت «سند روزانه» — همیشه یک سند مستقل به‌ازای هر برگه.
            var existingHeaderNumbers = new HashSet<double>();
            var candidateNs = usableIdx.Select(i => headRows[i].N_S).Where(ns => ns is > 0).Select(ns => ns!.Value).ToList();
            var headerDateByNs = new Dictionary<double, long>();
            if (candidateNs.Count > 0)
            {
                foreach (var found in await _db.DoGetDataSQLAsync<(double? N_S, long? DATE_S)>(
                    "SELECT N_S, DATE_S FROM dbo.DEED_HED WHERE NO_S = 9 AND N_S BETWEEN @Min AND @Max", new { Min = candidateNs.Min(), Max = candidateNs.Max() }))
                {
                    if (found.N_S is not null) { existingHeaderNumbers.Add(found.N_S.Value); headerDateByNs[found.N_S.Value] = found.DATE_S ?? 0L; }
                }
            }

            var needsNewHeader = new bool[headRows.Count];
            var claimed = new HashSet<double>();
            var newHeaderIdx = new List<int>();
            var headerUpdates = new List<string>();
            foreach (var i in usableIdx)
            {
                var ns = headRows[i].N_S;
                var exists = ns is > 0 && existingHeaderNumbers.Contains(ns.Value);
                var owns = exists && claimed.Add(ns!.Value);
                if (!owns) { needsNewHeader[i] = true; newHeaderIdx.Add(i); }
                else if (headerDateByNs.TryGetValue(ns!.Value, out var headerDate) && headerDate != headRows[i].DATE_N)
                {
                    headerUpdates.Add(
                        $"UPDATE dbo.DEED_HED SET DATE_S={SqlNum(headRows[i].DATE_N)}, SHARH_S=N'{SqlText(BuildSharh(headRows[i]))}', " +
                        $"USER_NAME=N'{SqlText(headRows[i].USER_NAME)}', OKF=1 WHERE NO_S=9 AND N_S={SqlNum(ns.Value)};");
                }
            }
            if (newHeaderIdx.Count > 0)
            {
                var requests = newHeaderIdx.Select(i => new SanadHeaderRequest { DATE_S = headRows[i].DATE_N!.Value, SHARH_S = BuildSharh(headRows[i]), USER_NAME = headRows[i].USER_NAME }).ToList();
                var reserved = await SanadNumbering.ReserveBatchAsync(_db, 9, requests);
                for (int k = 0; k < newHeaderIdx.Count; k++)
                {
                    headRows[newHeaderIdx[k]].N_S = reserved[k];
                    headerUpdates.Add($"UPDATE dbo.HEAD_LST SET N_S={SqlNum(reserved[k])} WHERE NUMBER={SqlNum(headRows[newHeaderIdx[k]].NUMBER!.Value)} AND TAG=9;");
                }
            }
            const int headUpdateChunk = 500;
            for (int off = 0; off < headerUpdates.Count; off += headUpdateChunk)
            {
                var b = new StringBuilder();
                b.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                foreach (var s in headerUpdates.Skip(off).Take(headUpdateChunk)) b.Append(s);
                b.Append("COMMIT TRANSACTION;");
                await _db.DoExecuteSQLAsync(b.ToString());
            }

            var wanted = new HashSet<double>(usableIdx.Select(i => headRows[i].NUMBER!.Value));
            var chrstBySheet = new Dictionary<double, List<ChrstRow>>();
            var jstByKey = new Dictionary<(double Num, string? Code, int? Anbar, int Fnumb), List<JstLineRow>>();
            var rateByFnumb = new Dictionary<int, ManfRateRow>();
            // فقط برای مسیر OPTIONSS[56] <> '5' لازم است (بدون فیلتر Fnumb).
            var rateByCodeNum = new Dictionary<(double Num, string? Code), List<ManfRateRow>>();

            if (wanted.Count > 0)
            {
                var minN = wanted.Min(); var maxN = wanted.Max();

                foreach (var c in await _db.DoGetDataSQLAsync<ChrstRow>(
                    "SELECT L.NUMBER, L.N_KOL, L.ANBAR, L.CODE, SUM(L.MEGHk) AS SumOfMEGHk, S.NAME " +
                    "FROM dbo.STUF_DEF S INNER JOIN dbo.INVO_LST L ON S.CODE = L.CODE " +
                    "WHERE L.TAG = 9 AND L.NUMBER BETWEEN @Min AND @Max " +
                    "GROUP BY L.NUMBER, L.N_KOL, L.ANBAR, L.CODE, S.NAME", new { Min = minN, Max = maxN }))
                {
                    if (c.NUMBER is null || !wanted.Contains(c.NUMBER.Value)) continue;
                    if (!chrstBySheet.TryGetValue(c.NUMBER.Value, out var b)) chrstBySheet[c.NUMBER.Value] = b = new List<ChrstRow>();
                    b.Add(c);
                }

                if (isOption56_5)
                {
                    // دقیقاً معادل sqlJst1 در منبع: STUF_DEF.CODE=DTL_MANF.CODE (نام قطعه)،
                    // INVO_LST.CODE=HEAD_MANF.CODE (کالای ساخته‌شده)، HEAD_MANF.FNUMB=DTL_MANF.FNUMB.
                    foreach (var j in await _db.DoGetDataSQLAsync<JstLineRow>(
                        "SELECT HM.FNUMB, DM.CODE, DM.MABLK, S.NAME, L.NUMBER, SUM(L.MEGHk) AS SumOfMEGHk, L.CODE AS COM, " +
                        "(DM.MEGHk + DM.PERT) AS MEGHM, L.ANBAR " +
                        "FROM dbo.INVO_LST L " +
                        "INNER JOIN dbo.HEAD_MANF HM ON L.CODE = HM.CODE " +
                        "INNER JOIN dbo.DTL_MANF DM ON HM.FNUMB = DM.FNUMB " +
                        "INNER JOIN dbo.STUF_DEF S ON S.CODE = DM.CODE " +
                        "WHERE L.TAG = 9 AND L.NUMBER BETWEEN @Min AND @Max AND HM.FNUMB = ISNULL(L.N_KOL, 0) " +
                        "GROUP BY HM.FNUMB, DM.CODE, DM.MABLK, S.NAME, L.NUMBER, L.CODE, DM.MEGHk, DM.PERT, L.ANBAR",
                        new { Min = minN, Max = maxN }))
                    {
                        if (j.NUMBER is null) continue;
                        var key = (j.NUMBER.Value, j.COM, j.ANBAR, j.FNUMB ?? 0);
                        if (!jstByKey.TryGetValue(key, out var b)) jstByKey[key] = b = new List<JstLineRow>();
                        b.Add(j);
                    }

                    foreach (var r in await _db.DoGetDataSQLAsync<ManfRateRow>(
                        "SELECT IMBIBE_MANF, IMBIBE_SAR, CODE, FNUMB FROM dbo.HEAD_MANF"))
                    {
                        if (r.FNUMB is not null && !rateByFnumb.ContainsKey(r.FNUMB.Value)) rateByFnumb[r.FNUMB.Value] = r;
                    }
                }
                else
                {
                    // معادل sqlJst0 در منبع — بدون فیلتر HEAD_MANF.FNUMB=N_KOL.
                    foreach (var j in await _db.DoGetDataSQLAsync<JstLineRow>(
                        "SELECT DM.CODE, DM.MABLK, S.NAME, L.TAG, L.NUMBER, SUM(L.MEGHk) AS SumOfMEGHk, L.CODE AS COM, " +
                        "(DM.MEGHk + DM.PERT) AS MEGHM, L.ANBAR " +
                        "FROM dbo.INVO_LST L " +
                        "INNER JOIN dbo.HEAD_MANF HM ON L.CODE = HM.CODE " +
                        "INNER JOIN dbo.DTL_MANF DM ON HM.FNUMB = DM.FNUMB " +
                        "INNER JOIN dbo.STUF_DEF S ON S.CODE = DM.CODE " +
                        "WHERE L.TAG = 9 AND L.NUMBER BETWEEN @Min AND @Max " +
                        "GROUP BY DM.CODE, DM.MABLK, S.NAME, L.TAG, L.NUMBER, L.CODE, DM.MEGHk, DM.PERT, L.ANBAR",
                        new { Min = minN, Max = maxN }))
                    {
                        if (j.NUMBER is null) continue;
                        var key = (j.NUMBER.Value, j.COM, j.ANBAR, 0);
                        if (!jstByKey.TryGetValue(key, out var b)) jstByKey[key] = b = new List<JstLineRow>();
                        b.Add(j);
                    }

                    foreach (var r in await _db.DoGetDataSQLAsync<ManfRateRow>(
                        "SELECT S.NAME, L.TAG, L.NUMBER, L.MEGHk, HM.IMBIBE_MANF, HM.IMBIBE_SAR, L.CODE " +
                        "FROM dbo.STUF_DEF S INNER JOIN dbo.INVO_LST L ON S.CODE = L.CODE INNER JOIN dbo.HEAD_MANF HM ON L.CODE = HM.CODE " +
                        "WHERE L.TAG = 9 AND L.NUMBER BETWEEN @Min AND @Max", new { Min = minN, Max = maxN }))
                    {
                        if (r.NUMBER is null) continue;
                        var key = (r.NUMBER.Value, r.CODE);
                        if (!rateByCodeNum.TryGetValue(key, out var b)) rateByCodeNum[key] = b = new List<ManfRateRow>();
                        b.Add(r);
                    }
                }

                if (isOption56_5)
                {
                    foreach (var j in jstByKey.Values.SelectMany(v => v))
                    {
                        if (TryGetAccountCode(j.COM, out var comL) && TryGetAccountCode(j.CODE, out var codeL))
                            await CreatHesAsync(acc.CONKAL, comL, codeL, string.IsNullOrEmpty(j.NAME) ? " " : j.NAME);
                    }
                    foreach (var c in chrstBySheet.Values.SelectMany(v => v))
                    {
                        var fnumb = c.N_KOL.HasValue ? (int)c.N_KOL.Value : 0;
                        if (rateByFnumb.TryGetValue(fnumb, out var rr) && TryGetAccountCode(c.CODE, out var codeL))
                        {
                            if ((rr.IMBIBE_SAR ?? 0) * (c.SumOfMEGHk ?? 0) > 0) await CreatHesAsync(acc.CONKAL, codeL, 99999998, "سربار");
                            if ((rr.IMBIBE_MANF ?? 0) * (c.SumOfMEGHk ?? 0) > 0) await CreatHesAsync(acc.CONKAL, codeL, 99999999, "دستمزد");
                        }
                    }
                }
                else
                {
                    foreach (var j in jstByKey.Values.SelectMany(v => v))
                    {
                        if (TryGetAccountCode(j.COM, out var comL) && TryGetAccountCode(j.CODE, out var codeL))
                            await CreatHesAsync(acc.CONKAL, comL, codeL, string.IsNullOrEmpty(j.NAME) ? " " : j.NAME);
                    }
                    foreach (var kv in rateByCodeNum)
                    {
                        foreach (var r in kv.Value)
                        {
                            if (!TryGetAccountCode(r.CODE, out var codeL)) continue;
                            if ((r.IMBIBE_SAR ?? 0) * (r.MEGHk ?? 0) > 0) await CreatHesAsync(acc.CONKAL, codeL, 99999998, "سربار");
                            if ((r.IMBIBE_MANF ?? 0) * (r.MEGHk ?? 0) > 0) await CreatHesAsync(acc.CONKAL, codeL, 99999999, "دستمزد");
                        }
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
                var rows = new List<string>();

                void AddDetail(double? hesK, double? hesM, double? hesT, string hes, string sharh, double bed, double bes)
                {
                    rows.Add($"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},N'{SqlText(hes)}',N'{SqlText(LeftTrim(sharh, 255))}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(num)},9)");
                }

                try
                {
                    var chrstList = chrstBySheet.TryGetValue(num, out var cl) ? cl : new List<ChrstRow>();
                    foreach (var chrst in chrstList)
                    {
                        double jamch = 0d;
                        var fnumb = isOption56_5 ? (chrst.N_KOL.HasValue ? (int)chrst.N_KOL.Value : 0) : 0;
                        var key = (num, chrst.CODE, chrst.ANBAR, fnumb);

                        if (jstByKey.TryGetValue(key, out var jList))
                        {
                            foreach (var j in jList)
                            {
                                var mablk = j.MABLK ?? 0d;
                                var sumMeghk = chrst.SumOfMEGHk ?? 0d;
                                if (mablk * sumMeghk == 0d) continue;
                                if (!TryGetAccountCode(j.COM, out var comL) || !TryGetAccountCode(j.CODE, out var codeL))
                                {
                                    AddLog($"SANADVORUDSAKHT: کد نامعتبر (COM='{j.COM}', CODE='{j.CODE}') در برگه {num}؛ این قلم ثبت نشد.");
                                    continue;
                                }
                                var sharh = $"برگه ورود شماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{(j.MEGHM ?? 0d) * sumMeghk} جهت {(chrst.NAME ?? "").Trim()}" +
                                            (isOption56_5 ? $" فرمول: {chrst.N_KOL}" : "");
                                var bes = Math.Round(mablk * sumMeghk);
                                jamch += bes;
                                AddDetail(acc.CONKAL, comL, codeL, $"{acc.CONKAL}-{(double)comL}-{(double)codeL}", sharh, 0, bes);
                            }
                        }

                        if (isOption56_5)
                        {
                            if (rateByFnumb.TryGetValue(fnumb, out var rr) && TryGetAccountCode(chrst.CODE, out var chrstCodeL))
                            {
                                var sumMeghk = chrst.SumOfMEGHk ?? 0d;
                                var codeVal = (double)chrstCodeL;
                                var sharh = $"برگه ورود شماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{sumMeghk} جهت {(chrst.NAME ?? "").Trim()} فرمول: {chrst.N_KOL}";
                                if ((rr.IMBIBE_SAR ?? 0d) * sumMeghk > 0d)
                                {
                                    var bes = Math.Round((rr.IMBIBE_SAR ?? 0d) * sumMeghk);
                                    jamch += bes;
                                    AddDetail(acc.CONKAL, chrstCodeL, 99999998, $"{acc.CONKAL}-{codeVal}-99999998", sharh, 0, bes);
                                }
                                if ((rr.IMBIBE_MANF ?? 0d) * sumMeghk > 0d)
                                {
                                    var bes = Math.Round((rr.IMBIBE_MANF ?? 0d) * sumMeghk);
                                    jamch += bes;
                                    AddDetail(acc.CONKAL, chrstCodeL, 99999999, $"{acc.CONKAL}-{codeVal}-99999999", sharh, 0, bes);
                                }
                            }
                        }
                        else
                        {
                            var rkey = (num, chrst.CODE);
                            if (rateByCodeNum.TryGetValue(rkey, out var rList) && rList.Count > 0 && TryGetAccountCode(rList[0].CODE, out var jstCodeL))
                            {
                                var rr = rList[0];
                                var meghk = chrst.SumOfMEGHk ?? 0d;
                                var codeVal = (double)jstCodeL;
                                var sharh = $"برگه ورود شماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{meghk} جهت {(rr.NAME ?? "").Trim()}";
                                if ((rr.IMBIBE_SAR ?? 0d) * (rr.MEGHk ?? 0d) > 0d)
                                {
                                    var bes = Math.Round((rr.IMBIBE_SAR ?? 0d) * meghk);
                                    jamch += bes;
                                    AddDetail(acc.CONKAL, jstCodeL, 99999998, $"{acc.CONKAL}-{codeVal}-99999998", sharh, 0, bes);
                                }
                                if ((rr.IMBIBE_MANF ?? 0d) * (rr.MEGHk ?? 0d) > 0d)
                                {
                                    var bes = Math.Round((rr.IMBIBE_MANF ?? 0d) * meghk);
                                    jamch += bes;
                                    AddDetail(acc.CONKAL, jstCodeL, 99999999, $"{acc.CONKAL}-{codeVal}-99999999", sharh, 0, bes);
                                }
                            }
                        }

                        if (jamch != 0d)
                        {
                            if (!TryGetAccountCode(chrst.CODE, out var chrstCodeL2) || chrst.ANBAR is null)
                                throw new InvalidOperationException($"کد کالا ('{chrst.CODE}') یا انبار برای برگه {num} معتبر نیست.");

                            var codeVal = (double)chrstCodeL2;
                            var sharh = $"برگه ورود شماره {num}-{h.FNUMCO} مورخ {PersianDate(dateN)} به مقدار{chrst.SumOfMEGHk} جهت {(chrst.NAME ?? "").Trim()}" +
                                        (isOption56_5 ? $" فرمول: {chrst.N_KOL}" : "");
                            AddDetail(acc.MOGODIA, chrst.ANBAR, chrstCodeL2, $"{acc.MOGODIA}-{chrst.ANBAR}-{codeVal}", sharh, Math.Round(jamch), 0);
                        }
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");
                    if (needsNewHeader[R])
                        batch.Append($"UPDATE dbo.HEAD_LST SET N_S={SqlNum(ns)} WHERE NUMBER={SqlNum(num)} AND TAG=9;");
                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE TAG = 9 AND NUMBER = {SqlNum(num)};");
                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES,SHARH,BED,BES,NUMBER,TAG) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");
                    await _db.DoExecuteSQLAsync(batch.ToString());
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    RecordFailure($"برگ {num} (سند {ns}): {ex.Message}");
                }
            });

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.FirstError = firstError;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { result.LastSanadNumber = (long)headRows[i].N_S!.Value; break; }

            AddLog($"SANADVORUDSAKHT: پایان — {result.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }
    }
}
