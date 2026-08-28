using Safir.Shared.Interfaces;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند انتقالی مواد بین انبارها» (DEED_HED/DEED_DTL برای
    //  HEAD_LST.TAG=5، NO_S=10).
    //
    //  پورتِ دستیِ SANADENTEGHAL در CL_HESABDARI_AUTO_BAZ.cs — نه فراخوانیِ
    //  آن پروژه. منطق حسابداری عیناً همان است: هر ردیف کالای برگه‌ی
    //  انتقالی یک آرتیکل دوطرفه می‌سازد — بستانکار حساب موجودی انبار
    //  مبدأ، بدهکار حساب موجودی انبار مقصد، هر دو به همان مبلغ
    //  (MABL_K) — چون این صرفاً جابه‌جایی ارزش موجودی بین دو زیرحساب
    //  است، نه هزینه یا سود.
    //
    //  ⚠️ انبارهای «نوع ۱ یا ۲» (TCOD_ANBAR.KIND) وقتی شرکت در حالت
    //  غیرصنعتی است (SAZMAN.SANAT = صریحاً false) اصلاً سند نمی‌گیرند —
    //  عیناً همان استثنای کد اصلی. روی این دیتابیس SANAT=1 است، پس این
    //  استثنا عملاً فعال نیست، ولی برای شرکت‌های دیگر باید رعایت شود.
    //
    //  ⚠️ برخلاف SANADENTEGHAL اصلی (که فقط بازه‌ی NUMBER را فیلتر
    //  می‌کرد)، اینجا AND با بازه‌ی تاریخ هم شرط شده — همان درسی که از
    //  MaterialIssueRebuildService مستند است: شماره برگه لزوماً با
    //  تاریخ هم‌ترتیب نیست.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class TransferRebuildResult
    {
        public bool          Success         { get; set; }
        public int           SheetCount      { get; set; }
        public int           SkippedCount    { get; set; }
        public long?         LastSanadNumber { get; set; }
        public string?       FirstError      { get; set; }
        public List<string>  Log             { get; set; } = new();
    }

    public sealed class TransferRebuildService
    {
        private readonly IDatabaseService _db;

        public TransferRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? MOGODIA { get; set; }
            public bool?   SANAT   { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER   { get; set; }
            public int?    ANBAR    { get; set; }
            public int?    ANBARF   { get; set; }
            public long?   DATE_N   { get; set; }
            public double? N_S      { get; set; }
            public double? FNUMCO   { get; set; }
            public string? USER_NAME{ get; set; }
        }

        private sealed class AnbarKindRow
        {
            public int  CODE { get; set; }
            public int? KIND { get; set; }
        }

        private sealed class LineRow
        {
            public double? NUMBER  { get; set; }
            public string? CODE    { get; set; }
            public string? NAME    { get; set; }
            public int?    ANBAR   { get; set; }
            public int?    ANBARF  { get; set; }
            public double? MEGHk   { get; set; }
            public double? MABL_K  { get; set; }
        }

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";

        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");

        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];

        private static string PersianDate(long dateN)
            => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";

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

        public async Task<TransferRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new TransferRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;

            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg) { lock (logLock) { log.Add(msg); firstError ??= msg; } }

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>(
                "SELECT TOP 1 MOGODIA, SANAT FROM dbo.SAZMAN")).FirstOrDefault() ?? new SazmanAccounts();

            if (acc.MOGODIA is null)
            {
                result.Success = false;
                RecordFailure("حساب موجودی جنسی (SAZMAN.MOGODIA) تنظیم نشده؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            var anbarKind = new Dictionary<int, int?>();
            foreach (var a in await _db.DoGetDataSQLAsync<AnbarKindRow>(
                "SELECT CODE, KIND FROM dbo.TCOD_ANBAR"))
            {
                anbarKind[a.CODE] = a.KIND;
            }

            bool SkipsDocument(int? anbar)
            {
                if (!anbar.HasValue) return false;
                var kind = anbarKind.TryGetValue(anbar.Value, out var k) ? k : null;
                var nonIndustrialSkip = !(acc.SANAT == true || acc.SANAT is null);
                return (kind == 1 || kind == 2) && nonIndustrialSkip;
            }

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, ANBAR, ANBARF, DATE_N, N_S, FNUMCO, USER_NAME FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 5 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"SANADENTEGHAL: شروع بازسازی از برگ {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");

            if (headRows.Count == 0)
            {
                result.Success = true;
                return result;
            }

            var wanted = new HashSet<double>(headRows.Where(h => h.NUMBER is not null).Select(h => h.NUMBER!.Value));
            var linesBySheet = new Dictionary<double, List<LineRow>>();

            if (wanted.Count > 0)
            {
                var minN = wanted.Min(); var maxN = wanted.Max();
                var rows = await _db.DoGetDataSQLAsync<LineRow>(
                    "SELECT L.NUMBER, L.CODE, S.NAME, L.ANBAR, L.ANBARF, L.MEGHk, L.MABL_K " +
                    "FROM dbo.INVO_LST L INNER JOIN dbo.STUF_DEF S ON S.CODE = L.CODE " +
                    "WHERE L.TAG = 5 AND L.NUMBER BETWEEN @Min AND @Max",
                    new { Min = minN, Max = maxN });

                foreach (var r in rows)
                {
                    if (r.NUMBER is null || !wanted.Contains(r.NUMBER.Value)) continue;
                    if (!linesBySheet.TryGetValue(r.NUMBER.Value, out var list))
                        linesBySheet[r.NUMBER.Value] = list = new List<LineRow>();
                    list.Add(r);
                }
            }

            // پیش‌ساخت حساب‌های انبار مبدأ/مقصد برای همه‌ی کالاهای درگیر
            var existingAccounts = new ConcurrentDictionary<(long, long, long), bool>();

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

            async Task CreatHesAsync(double kol, int moin, string code, string name)
            {
                if (!TryGetAccountCode(code, out var taf))
                    throw new InvalidOperationException($"کد کالای '{code}' نامعتبر است.");

                var kolV = (long)kol; var moinV = (long)moin;
                if (await IsHesabAsync(kolV, moinV, taf)) return;

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
                    await _db.DoExecuteSQLAsync(sql, new { Kol = kolV, Moin = moinV, Taf = taf, Name = LeftTrim(name, 250) });
                    existingAccounts[(kolV, moinV, taf)] = true;
                }
                catch (Exception ex)
                {
                    existingAccounts.TryRemove((kolV, moinV, taf), out _);
                    RecordFailure($"[CREATHES] خطا در ساخت حساب {kolV}-{moinV}-{taf}: {ex.Message}");
                    throw;
                }
            }

            foreach (var lines in linesBySheet.Values)
            foreach (var line in lines)
            {
                if (line.MABL_K is null or 0 || string.IsNullOrEmpty(line.CODE)) continue;
                try
                {
                    if (line.ANBAR.HasValue) await CreatHesAsync(acc.MOGODIA.Value, line.ANBAR.Value, line.CODE, line.NAME ?? " ");
                    if (line.ANBARF.HasValue) await CreatHesAsync(acc.MOGODIA.Value, line.ANBARF.Value, line.CODE, line.NAME ?? " ");
                }
                catch { /* لاگ شده؛ ادامه می‌دهیم، خط INSERT خودش خطا می‌دهد اگر واقعاً لازم بود */ }
            }

            // رزرو دسته‌ای شماره سند فقط برای برگه‌هایی که واقعاً سند می‌گیرند
            var sheetUsable = new bool[headRows.Count];
            var needsNewHeader = new bool[headRows.Count];
            var skipped = 0;

            var candidateNs = new List<double>();
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101) { AddLog($"برگ {h.NUMBER}: تاریخ نامعتبر."); continue; }
                if (SkipsDocument(h.ANBAR)) { skipped++; continue; }
                sheetUsable[i] = true;
                if (h.N_S is > 0) candidateNs.Add(h.N_S.Value);
            }

            var existingHeaderNs = new HashSet<double>();
            if (candidateNs.Count > 0)
            {
                var found = await _db.DoGetDataSQLAsync<double?>(
                    "SELECT N_S FROM dbo.DEED_HED WHERE NO_S=10 AND N_S BETWEEN @Min AND @Max",
                    new { Min = candidateNs.Min(), Max = candidateNs.Max() });
                foreach (var v in found) if (v.HasValue) existingHeaderNs.Add(v.Value);
            }

            var claimed = new HashSet<double>();
            var newHeaderIdx = new List<int>();
            for (int i = 0; i < headRows.Count; i++)
            {
                if (!sheetUsable[i]) continue;
                var ns = headRows[i].N_S;
                var exists = ns is > 0 && existingHeaderNs.Contains(ns.Value);
                var owns = exists && claimed.Add(ns!.Value);
                if (!owns) { needsNewHeader[i] = true; newHeaderIdx.Add(i); }
            }

            if (newHeaderIdx.Count > 0)
            {
                var reserved = await SanadNumbering.ReserveBatchAsync(_db, 10, newHeaderIdx.Select(i => new SanadHeaderRequest
                {
                    DATE_S    = headRows[i].DATE_N!.Value,
                    SHARH_S   = BuildHeaderSharh(headRows[i]),
                    USER_NAME = headRows[i].USER_NAME
                }).ToList());

                for (int k = 0; k < newHeaderIdx.Count; k++)
                    headRows[newHeaderIdx[k]].N_S = reserved[k];
            }

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            var successFlag = 1;

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                var h = headRows[R];
                if (h.NUMBER is null) return;

                if (!sheetUsable[R])
                {
                    // انبار نوع ۱/۲ در حالت غیرصنعتی: سند وجود ندارد، جزئیات قدیمی هم حذف شود.
                    await _db.DoExecuteSQLAsync(
                        "DELETE FROM dbo.DEED_DTL WHERE NUMBER=@n AND TAG=5", new { n = h.NUMBER.Value });
                    return;
                }

                var ns = h.N_S!.Value;
                var lines = linesBySheet.TryGetValue(h.NUMBER.Value, out var l) ? l : new List<LineRow>();
                var rows = new List<string>();

                foreach (var line in lines)
                {
                    if (line.MABL_K is null or 0 || string.IsNullOrEmpty(line.CODE)) continue;
                    if (!line.ANBAR.HasValue || !line.ANBARF.HasValue) continue;

                    var mablRounded = SqlNum(Math.Round(line.MABL_K.Value));
                    var sharhBase = $"حواله انتقالي شماره {h.NUMBER}-{h.FNUMCO} مورخ {PersianDate(h.DATE_N!.Value)} به مقدار{line.MEGHk}";

                    var hesBes = $"{acc.MOGODIA}-{line.ANBAR}-{line.CODE}";
                    rows.Add($"({SqlNum(ns)},{SqlNum(acc.MOGODIA)},{line.ANBAR},{line.CODE}," +
                             $"N'{SqlText(hesBes)}',N'{SqlText(LeftTrim(sharhBase, 255))}',0,{mablRounded},{SqlNum(h.NUMBER)},5)");

                    var sharhBed = LeftTrim(sharhBase + $"  بابت {line.NAME}", 255);
                    var hesBed = $"{acc.MOGODIA}-{line.ANBARF}-{line.CODE}";
                    rows.Add($"({SqlNum(ns)},{SqlNum(acc.MOGODIA)},{line.ANBARF},{line.CODE}," +
                             $"N'{SqlText(hesBed)}',N'{SqlText(sharhBed)}',{mablRounded},0,{SqlNum(h.NUMBER)},5)");
                }

                try
                {
                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");

                    if (needsNewHeader[R])
                    {
                        batch.Append($"UPDATE dbo.HEAD_LST SET N_S={SqlNum(ns)} WHERE NUMBER={SqlNum(h.NUMBER)} AND TAG=5;");
                    }
                    else
                    {
                        var sharhS = BuildHeaderSharh(h);
                        batch.Append(
                            $"UPDATE dbo.DEED_HED SET DATE_S={SqlNum(h.DATE_N)}, SHARH_S=N'{SqlText(sharhS)}', " +
                            $"GHATEI=0, NO_S=10, OKF=1, USER_NAME=N'{SqlText(h.USER_NAME)}' WHERE NO_S=10 AND N_S={SqlNum(ns)};");
                    }

                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(h.NUMBER)} AND TAG=5;");

                    const int chunkSize = 500;
                    for (int off = 0; off < rows.Count; off += chunkSize)
                    {
                        batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES,SHARH,BED,BES,NUMBER,TAG) VALUES ");
                        batch.Append(string.Join(",", rows.Skip(off).Take(chunkSize)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");

                    await ExecuteWithDeadlockRetryAsync(() => _db.DoExecuteSQLAsync(batch.ToString()));
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    RecordFailure($"برگ {h.NUMBER} (سند {ns}): {ex.Message}");
                }
            });

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.SkippedCount = skipped;
            result.FirstError = firstError;
            for (int i = headRows.Count - 1; i >= 0; i--)
                if (sheetUsable[i] && headRows[i].N_S is not null) { result.LastSanadNumber = (long)headRows[i].N_S!.Value; break; }

            AddLog($"SANADENTEGHAL: پایان — {result.SheetCount} برگه (+{skipped} بدون سند)، موفق={result.Success}.");
            return result;
        }

        private static string BuildHeaderSharh(HeadRow h)
            => LeftTrim($" حواله انتقالي مواد شماره {h.NUMBER}-{h.FNUMCO} از انبار {h.ANBAR} به {h.ANBARF} مورخ {PersianDate(h.DATE_N!.Value)}", 100);

        private static bool TryGetAccountCode(string? value, out long result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return false;
            if (double.IsNaN(parsed) || parsed < int.MinValue || parsed > int.MaxValue) return false;
            result = (long)parsed;
            return true;
        }
    }
}
