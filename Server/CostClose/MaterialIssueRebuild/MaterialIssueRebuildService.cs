using Dapper;
using Safir.Shared.Interfaces;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.MaterialIssueRebuild
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «سند حواله خروج مواد» (DEED_HED/DEED_DTL برای HEAD_LST.TAG=10)
    //  بعد از اصلاح نرخ فرمول تولید (S09-S11).
    //
    //  این یک پورتِ دستیِ (نه کپی خودکار) از متد SANADKHORUGMAVAD در ماژول
    //  حسابداری خودکار MR.CORRECT است — نه فراخوانیِ آن پروژه. منطق حسابداری
    //  (کدام حساب بدهکار/بستانکار می‌شود، ترتیب مراحل، رفتار روی خطا) عیناً
    //  همان است؛ فقط زیرساخت با معماری Safir جایگزین شده:
    //
    //   • CL_CCNNMANAGER (اتصال استاتیکِ سراسری، مخصوص یک برنامه دسکتاپ
    //     تک‌کاربره) حذف شد؛ به‌جایش از IDatabaseService همین Safir استفاده
    //     می‌شود که از قبل به‌ازای هر درخواست/اجرا رشته اتصال درست خودش را
    //     دارد — پس هیچ حالت سراسری قابل‌تغییر بین دو اجرای هم‌زمان به اشتراک
    //     گذاشته نمی‌شود.
    //   • Baseknow (کش سراسریِ تنظیمات شرکت، یک‌بار در استارتاپ برنامه
    //     دسکتاپ پر می‌شد) حذف شد؛ همان چند ستون لازم (حساب‌های موجودی/فاز
    //     تولید/هزینه تولید/عملکرد/کنترل کالا و پرچم FINALS) در ابتدای همین
    //     فراخوانی از SAZMAN خوانده می‌شوند — محلی به همین اجرا، نه سراسری.
    //   • Parallel.For (همگام) با یک Task.WhenAll+SemaphoreSlim جایگزین شد
    //     چون IDatabaseService ناهمگام (async) است.
    //   • LogWriter (نوشتن فایل روی مسیر ویندوزی C:\CORRECT\...) با یک
    //     List<string> جایگزین شد که همراه نتیجه برگردانده می‌شود.
    //   • هر کش (نام کالا، گروه کالا، وجود حساب) به این فراخوانی محدود است،
    //     نه یک کش سراسریِ فرایند — دو فراخوانی هم‌زمان (نظری) دادهٔ کهنهٔ
    //     یکدیگر را نمی‌بینند.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class MaterialIssueRebuildResult
    {
        public bool     Success        { get; set; }
        public int      SheetCount     { get; set; }
        public long?    LastSanadNumber{ get; set; }

        /// <summary>اولین خطای واقعیِ رخ‌داده — چون Log آخرین سطرش همیشه خلاصهٔ
        /// کلی «پایان» است و برای کاربر توضیح نمی‌دهد چرا شکست خورد.</summary>
        public string?  FirstError     { get; set; }
        public List<string> Log        { get; set; } = new();
    }

    public sealed class MaterialIssueRebuildService
    {
        private readonly IDatabaseService _db;

        public MaterialIssueRebuildService(IDatabaseService db) => _db = db;

        private sealed class SazmanAccounts
        {
            public double? MOGODIA  { get; set; }
            public double? PHAZ_TOL { get; set; }
            public double? HAZ_TOL  { get; set; }
            public double? AMALKARD { get; set; }
            public double? CONKAL   { get; set; }
            public bool?   FINALS   { get; set; }
        }

        private sealed class HeadRow
        {
            public double? NUMBER   { get; set; }
            public double? TAG      { get; set; }
            public int?    ANBAR    { get; set; }
            public long?   DATE_N   { get; set; }
            public double? N_S      { get; set; }
            public double? FNUMCO   { get; set; }
            public string? USER_NAME{ get; set; }
        }

        private sealed class LineRow
        {
            public double? SHEETNO { get; set; }
            public double? MABL_K  { get; set; }
            public double? MEGHk   { get; set; }
            public string? CODE    { get; set; }
            public int?    ANBAR   { get; set; }
            public string? COM     { get; set; }
            public string? NAM     { get; set; }
            public int?    N_KOL   { get; set; }
            public int?    NUMBER  { get; set; }
            public int?    TNUMBER { get; set; }
            public double? SMAB    { get; set; }
        }

        private sealed class FinalLineRow
        {
            public double? SHEETNO { get; set; }
            public double? MABL_K  { get; set; }
            public double? MEGHk   { get; set; }
            public int?    ANBAR   { get; set; }
            public string? CODE    { get; set; }
            public double? SMAB    { get; set; }
        }

        private sealed class BalanceRow
        {
            public double? N_S  { get; set; }
            public double? DIFF { get; set; }
        }

        private sealed class SanadHeaderRequest
        {
            public long    DATE_S   { get; set; }
            public string? SHARH_S  { get; set; }
            public string? USER_NAME{ get; set; }
        }

        // ───────── کمکی‌های قالب‌بندی SQL (برای دسته‌های VALUES که Dapper پارامتری نمی‌شوند) ─────────

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";

        private static string SqlText(string? v)
            => (v ?? string.Empty).Replace("'", "''");

        private static bool TryGetAccountCode(string? value, out long result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return false;
            if (double.IsNaN(parsed) || parsed < int.MinValue || parsed > int.MaxValue) return false;
            result = (long)parsed;
            return true;
        }

        private static string LeftTrim(string s, int max)
            => s.Length <= max ? s : s[..max];

        private static string PersianDate(long dateN)
            => $"{dateN / 10000:0000}/{dateN / 100 % 100:00}/{dateN % 100:00}";

        // ───────── بازتلاش روی بن‌بست (Deadlock) ─────────
        // مرحله ۳ پایین‌تر هر برگه را در تراکنش جدای خودش، موازی با بقیه‌ی
        // برگه‌ها اجرا می‌کند (SET DEADLOCK_PRIORITY LOW هم همین را نشان
        // می‌دهد — قبلاً هم پیش‌بینی شده بود). وقتی SQL Server بین دو
        // تراکنشِ هم‌زمانِ همین اجرا (یا حتی یک فرایند دیگر روی همین پایگاه)
        // بن‌بست تشخیص دهد، یکی را «قربانی» می‌کند و خطای ۱۲۰۵ می‌دهد — دقیقاً
        // همان خطایی که SQL Server خودش می‌گوید «Rerun the transaction».
        // چون کارِ هر برگه idempotent است (DELETE+INSERT مستقل روی همان
        // NUMBER/TAG=10)، تلاش دوباره‌ی همان تراکنش کاملاً امن است؛ بدون این،
        // کاربر باید کل بازسازی گروهی (همه‌ی برگه‌ها) را دستی دوباره بزند.
        private static async Task ExecuteWithDeadlockRetryAsync(
            Func<Task> action, int maxAttempts = 3)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && attempt < maxAttempts)
                {
                    await Task.Delay(Random.Shared.Next(150, 450) * attempt);
                }
            }
        }

        // ───────── محدودیت موازی‌سازی — جایگزین Parallel.For همگام ─────────

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

        /// <summary>
        /// بازسازی سند حواله خروج مواد برای برگه‌های HEAD_LST با TAG=10 در بازه
        /// [fromNumber, toNumber] که تاریخشان هم در [dateFrom, dateTo] باشد.
        ///
        /// چرا هر دو شرط لازم است: شماره برگه لزوماً با تاریخ هم‌ترتیب نیست (دقیقاً
        /// همان یافته‌ای که در همین PR باعث اصلاح CHK-01 شد) — یک برگهٔ ماه مجاور که
        /// دیرهنگام ثبت شده می‌تواند شماره‌اش داخل بازهٔ این ماه بیفتد. اگر فقط به
        /// بازهٔ شماره‌ای که فراخوان از تاریخ استخراج کرده اعتماد کنیم، آن برگهٔ
        /// نامرتبط هم بی‌صدا بازسازی می‌شود. اینجا خودِ این متد هم تاریخ را دوباره
        /// می‌سنجد تا این تضمین به فراخوان وابسته نماند.
        /// </summary>
        public async Task<MaterialIssueRebuildResult> RebuildAsync(
            long fromNumber, long toNumber, long dateFrom, long dateTo, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new MaterialIssueRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;

            // چند برگه هم‌زمان (تا ۱۶ Task) ممکن است لاگ بنویسند؛ List<string> برای
            // نویسنده‌های هم‌زمان امن نیست، پس هر افزودن از این دو تابع رد می‌شود.
            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg)
            {
                lock (logLock)
                {
                    log.Add(msg);
                    firstError ??= msg;
                }
            }

            var existingAccounts = new ConcurrentDictionary<(long, long, long), bool>();
            var kalaNameCache     = new ConcurrentDictionary<double, string>();
            var kalaGroupCache    = new ConcurrentDictionary<string, int>();

            var acc = (await _db.DoGetDataSQLAsync<SazmanAccounts>(
                "SELECT TOP 1 MOGODIA, PHAZ_TOL, HAZ_TOL, AMALKARD, CONKAL, FINALS FROM dbo.SAZMAN"))
                .FirstOrDefault() ?? new SazmanAccounts();

            if (acc.MOGODIA is null || acc.PHAZ_TOL is null || acc.HAZ_TOL is null
                || acc.AMALKARD is null || acc.CONKAL is null)
            {
                result.Success = false;
                RecordFailure("حساب‌های پایه (موجودی/فاز تولید/هزینه تولید/عملکرد/کنترل کالا) در SAZMAN تنظیم نشده‌اند؛ بازسازی متوقف شد.");
                result.FirstError = firstError;
                return result;
            }

            var useFinalMode = acc.FINALS == true;

            // ───── تابع‌های داخلی وابسته به acc/کش‌ها (closures، نه استاتیک سراسری) ─────

            async Task<bool> IsHesabAsync(double kol, double moin, double taf)
            {
                var key = ((long)kol, (long)moin, (long)taf);
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

            async Task<string> GetKalaNameAsync(double code)
            {
                if (kalaNameCache.TryGetValue(code, out var cached)) return cached;

                var row = (await _db.DoGetDataSQLAsync<string>(
                    "SELECT NAME FROM dbo.STUF_DEF WHERE CODE=@Code", new { Code = code.ToString(CultureInfo.InvariantCulture) }))
                    .FirstOrDefault();

                var name = string.IsNullOrEmpty(row) ? " " : row;
                kalaNameCache[code] = name;
                return name;
            }

            async Task<int> GetGroupKalaAsync(string? code)
            {
                var key = code ?? string.Empty;
                if (kalaGroupCache.TryGetValue(key, out var cached)) return cached;

                var radah = (await _db.DoGetDataSQLAsync<double?>(
                    "SELECT RADAH FROM dbo.STUF_DEF WHERE CODE=@Code", new { Code = code }))
                    .FirstOrDefault();

                var grp = radah.HasValue ? (int)radah.Value : 0;
                kalaGroupCache[key] = grp;
                return grp;
            }

            // ───── مرحله ۰-۱: خواندن برگه‌ها و تشخیص سربرگ موجود ─────

            var headRows = (await _db.DoGetDataSQLAsync<HeadRow>(
                "SELECT NUMBER, TAG, ANBAR, DATE_N, N_S, FNUMCO, USER_NAME FROM dbo.HEAD_LST " +
                "WHERE NUMBER BETWEEN @From AND @To AND TAG = 10 AND DATE_N BETWEEN @DateFrom AND @DateTo ORDER BY NUMBER",
                new { From = fromNumber, To = toNumber, DateFrom = dateFrom, DateTo = dateTo })).ToList();

            AddLog($"SANADKHORUGMAVAD: شروع بازسازی از برگ {fromNumber} تا {toNumber} — {headRows.Count} برگه یافت شد.");

            if (headRows.Count == 0)
            {
                result.Success = true;
                return result;
            }

            var sheetUsable = new bool[headRows.Count];
            for (int i = 0; i < headRows.Count; i++)
            {
                var h = headRows[i];
                if (h.NUMBER is null || h.DATE_N is null || h.DATE_N < 10101)
                {
                    AddLog($"برگ {h.NUMBER}: تاریخ نامعتبر ({h.DATE_N})؛ رد شد.");
                    continue;
                }
                sheetUsable[i] = true;
            }

            var candidateNs = headRows.Where((h, i) => sheetUsable[i] && h.N_S is > 0)
                                       .Select(h => h.N_S!.Value).Distinct().ToList();
            var existingHeaderNs = new HashSet<double>();
            if (candidateNs.Count > 0)
            {
                var found = await _db.DoGetDataSQLAsync<double?>(
                    "SELECT N_S FROM dbo.DEED_HED WHERE NO_S=8 AND N_S BETWEEN @Min AND @Max",
                    new { Min = candidateNs.Min(), Max = candidateNs.Max() });
                foreach (var v in found) if (v.HasValue) existingHeaderNs.Add(v.Value);
            }

            var needsNewHeader = new bool[headRows.Count];
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
                var reserved = await ReserveSanadNumbersBatchAsync(
                    newHeaderIdx.Select(i => new SanadHeaderRequest
                    {
                        DATE_S = headRows[i].DATE_N!.Value,
                        SHARH_S = BuildHeaderSharh(headRows[i]),
                        USER_NAME = headRows[i].USER_NAME
                    }).ToList());

                for (int k = 0; k < newHeaderIdx.Count; k++)
                    headRows[newHeaderIdx[k]].N_S = reserved[k];
            }

            // ───── مرحله ۲: پیش‌خوانی اقلام همه برگه‌ها با یک کوئری ─────

            var wanted = new HashSet<double>(headRows.Where((h, i) => sheetUsable[i]).Select(h => h.NUMBER!.Value));
            var linesBySheet      = new Dictionary<double, List<LineRow>>();
            var finalLinesBySheet = new Dictionary<double, List<FinalLineRow>>();

            if (wanted.Count > 0)
            {
                var minN = wanted.Min(); var maxN = wanted.Max();

                if (!useFinalMode)
                {
                    var rows = await _db.DoGetDataSQLAsync<LineRow>(
                        "SELECT L.NUMBER AS SHEETNO, L.MABL_K, L.MEGHk, L.CODE, L.ANBAR, " +
                        "HM.CODE AS COM, ISNULL(HM.NAMES, S.NAME) AS NAM, HM.N_KOL, HM.NUMBER, HM.TNUMBER, DM.SMABl AS SMAB " +
                        "FROM dbo.STUF_DEF S RIGHT OUTER JOIN dbo.HEAD_MANF HM ON S.CODE = HM.CODE " +
                        "INNER JOIN dbo.INVO_LST L ON HM.FNUMB = L.N_RASID " +
                        "INNER JOIN dbo.DTL_MANF DM ON DM.CODE = L.CODE AND HM.FNUMB = DM.FNUMB " +
                        "WHERE L.NUMBER BETWEEN @Min AND @Max AND L.TAG = 10 ORDER BY L.NUMBER, L.id",
                        new { Min = minN, Max = maxN });

                    foreach (var r in rows)
                    {
                        if (r.SHEETNO is null || !wanted.Contains(r.SHEETNO.Value)) continue;
                        if (!linesBySheet.TryGetValue(r.SHEETNO.Value, out var list))
                            linesBySheet[r.SHEETNO.Value] = list = new List<LineRow>();
                        list.Add(r);
                    }
                }
                else
                {
                    var rows = await _db.DoGetDataSQLAsync<FinalLineRow>(
                        "SELECT L.NUMBER AS SHEETNO, L.MABL_K, L.MEGHk, L.ANBAR, L.CODE, MAX(DM.SMABL) AS SMAB " +
                        "FROM dbo.INVO_LST L INNER JOIN dbo.DTL_MANF DM ON DM.CODE = L.CODE " +
                        "WHERE L.NUMBER BETWEEN @Min AND @Max AND L.TAG = 10 " +
                        "GROUP BY L.NUMBER, L.MABL_K, L.MEGHk, L.ANBAR, L.CODE " +
                        "ORDER BY L.NUMBER, L.CODE",
                        new { Min = minN, Max = maxN });

                    foreach (var r in rows)
                    {
                        if (r.SHEETNO is null || !wanted.Contains(r.SHEETNO.Value)) continue;
                        if (!finalLinesBySheet.TryGetValue(r.SHEETNO.Value, out var list))
                            finalLinesBySheet[r.SHEETNO.Value] = list = new List<FinalLineRow>();
                        list.Add(r);
                    }
                }
            }

            // ───── مرحله ۳ (موازی): هر برگه مستقل؛ همه دستورهایش یک رفت‌وبرگشت ─────

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            const int detailChunk = 500;
            const string detailPrefix =
                "INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,SHARH,HES,BED,BES,NUMBER,TAG) VALUES ";

            var successFlag = 1; // 1 = موفق؛ Interlocked برای دسترسی امن از چند Task

            await ParallelForAsync(headRows.Count, maxDegree, async R =>
            {
                if (!sheetUsable[R]) return;

                var sheet = headRows[R];
                var sheetNo = sheet.NUMBER!.Value;
                var nsValue = sheet.N_S!.Value;
                var valueRows = new List<string>();

                void AddDetail(double hesK, double hesM, double hesT, string hes, string sharh, double bed, double bes)
                    => valueRows.Add($"({SqlNum(nsValue)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)}," +
                                      $"N'{SqlText(sharh)}',N'{SqlText(hes)}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(sheetNo)},10)");

                try
                {
                    if (!useFinalMode)
                    {
                        var lines = linesBySheet.TryGetValue(sheetNo, out var b) ? b : new List<LineRow>();
                        foreach (var line in lines)
                        {
                            await ProcessNormalLineAsync(line, sheet, sheetNo, acc, AddDetail, CreatHesAsync, GetKalaNameAsync, GetGroupKalaAsync);
                        }
                    }
                    else
                    {
                        var lines = finalLinesBySheet.TryGetValue(sheetNo, out var b) ? b : new List<FinalLineRow>();
                        foreach (var line in lines)
                        {
                            await ProcessFinalLineAsync(line, sheet, sheetNo, acc, AddDetail, CreatHesAsync, IsHesabAsync, GetKalaNameAsync, AddLog);
                        }
                    }

                    var batch = new StringBuilder();
                    batch.Append("SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;");

                    if (needsNewHeader[R])
                    {
                        batch.Append($"UPDATE dbo.HEAD_LST SET N_S={SqlNum(nsValue)} WHERE NUMBER={SqlNum(sheetNo)} AND TAG=10;");
                    }
                    else
                    {
                        batch.Append(
                            $"UPDATE dbo.DEED_HED SET DATE_S={SqlNum(sheet.DATE_N)}, SHARH_S=N'{SqlText(BuildHeaderSharh(sheet))}', " +
                            $"GHATEI=0, NO_S=8, OKF=1, USER_NAME=N'{SqlText(sheet.USER_NAME)}' WHERE NO_S=8 AND N_S={SqlNum(nsValue)};");
                    }

                    batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(sheetNo)} AND TAG=10;");

                    for (int off = 0; off < valueRows.Count; off += detailChunk)
                    {
                        batch.Append(detailPrefix);
                        batch.Append(string.Join(",", valueRows.Skip(off).Take(detailChunk)));
                        batch.Append(';');
                    }
                    batch.Append("COMMIT TRANSACTION;");

                    await ExecuteWithDeadlockRetryAsync(() => _db.DoExecuteSQLAsync(batch.ToString()));
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref successFlag, 0);
                    RecordFailure($"برگ {sheetNo} (سند {nsValue}): {ex.Message}");
                }
            });

            // ───── مرحله ۴ (سریال): کسر دهم ریال ─────

            var sheetByNs = new Dictionary<double, HeadRow>();
            for (int i = 0; i < headRows.Count; i++)
                if (sheetUsable[i] && headRows[i].N_S is not null) sheetByNs[headRows[i].N_S!.Value] = headRows[i];

            if (sheetByNs.Count > 0)
            {
                var nsKeys = sheetByNs.Keys.ToList();
                var unbalanced = new List<BalanceRow>();
                const int balChunk = 1000;

                for (int off = 0; off < nsKeys.Count; off += balChunk)
                {
                    var nsIn = string.Join(",", nsKeys.Skip(off).Take(balChunk).Select(k => SqlNum(k)));
                    var sql = "SELECT N_S, SUM(BED)-SUM(BES) AS DIFF FROM dbo.DEED_DTL " +
                               $"WHERE N_S IN ({nsIn}) GROUP BY N_S " +
                               "HAVING SUM(BED)-SUM(BES) <> 0 AND ABS(SUM(BED)-SUM(BES)) <= 40 OPTION (MAXDOP 1)";
                    unbalanced.AddRange((await _db.DoGetDataSQLAsync<BalanceRow>(sql))
                        .Where(x => x.N_S is not null && x.DIFF is not null && sheetByNs.ContainsKey(x.N_S.Value)));
                }

                if (unbalanced.Count > 0)
                {
                    try { await CreatHesAsync(acc.AMALKARD, 99999, 99999, "كسر دهم ريال"); }
                    catch (Exception ex) { AddLog($"حساب کسر دهم ریال: {ex.Message}"); }

                    var rows = new List<string>();
                    foreach (var item in unbalanced)
                    {
                        var owner = sheetByNs[item.N_S!.Value];
                        var diff = item.DIFF!.Value;
                        var sharh = LeftTrim($"حواله خروج شماره {owner.NUMBER}-{owner.FNUMCO} مورخ {PersianDate(owner.DATE_N!.Value)}", 255);
                        var hes = $"{acc.AMALKARD}-99999-99999";
                        var bed = diff > 0 ? 0 : Math.Abs(diff);
                        var bes = diff > 0 ? diff : 0;
                        rows.Add($"({SqlNum(item.N_S!.Value)},{SqlNum(acc.AMALKARD)},99999,99999,N'{SqlText(sharh)}',N'{SqlText(hes)}',{SqlNum(bed)},{SqlNum(bes)},{SqlNum(owner.NUMBER)},10)");
                    }

                    try
                    {
                        for (int off = 0; off < rows.Count; off += detailChunk)
                        {
                            var chunkItems = unbalanced.Skip(off).Take(detailChunk).ToList();
                            var chunk = string.Join(",", rows.Skip(off).Take(detailChunk));
                            var nsIn = string.Join(",", chunkItems.Select(x => SqlNum(x.N_S!.Value)));

                            var batchSql =
                                "SET DEADLOCK_PRIORITY LOW; SET XACT_ABORT ON; BEGIN TRANSACTION;" +
                                $"DELETE FROM dbo.DEED_DTL WHERE N_S IN ({nsIn}) AND TAG=10 " +
                                $"AND HES_K={SqlNum(acc.AMALKARD)} AND HES_M=99999 AND HES_T=99999;" +
                                detailPrefix + chunk + ";COMMIT TRANSACTION;";

                            await ExecuteWithDeadlockRetryAsync(() => _db.DoExecuteSQLAsync(batchSql));
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Exchange(ref successFlag, 0);
                        RecordFailure($"درج ردیف کسر دهم ریال: {ex.Message}");
                    }
                }
            }

            result.Success = Volatile.Read(ref successFlag) == 1;
            result.SheetCount = sheetUsable.Count(u => u);
            result.FirstError = firstError;
            for (int i = headRows.Count - 1; i >= 0; i--)
            {
                if (sheetUsable[i] && headRows[i].N_S is not null)
                {
                    result.LastSanadNumber = (long)headRows[i].N_S!.Value;
                    break;
                }
            }

            // این خط عمداً از AddLog است نه RecordFailure: خلاصهٔ کلی است، نه علت
            // شکست — اگر با RecordFailure ثبت می‌شد، چون همیشه آخرین سطر Log است،
            // FirstError واقعی (که بالاتر، از دل خطای هر برگه ثبت شده) بازنویسی می‌شد.
            AddLog($"SANADKHORUGMAVAD: پایان — {result.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }

        private static string BuildHeaderSharh(HeadRow h)
            => LeftTrim($" حواله خروج مواد از انبار شماره {h.NUMBER}-{h.FNUMCO} مورخ {PersianDate(h.DATE_N!.Value)}", 100);

        private static async Task ProcessNormalLineAsync(
            LineRow line, HeadRow sheet, double sheetNo, SazmanAccounts acc,
            Action<double, double, double, string, string, double, double> addDetail,
            Func<double?, double?, double?, string, Task> creatHes,
            Func<double, Task<string>> getKalaName,
            Func<string?, Task<int>> getGroupKala)
        {
            var lineSharh = LeftTrim(
                $"حواله خروج شماره {sheet.NUMBER}-{sheet.FNUMCO} مورخ {PersianDate(sheet.DATE_N!.Value)} به مقدار{line.MEGHk} جهت {line.NAM?.Trim()}", 255);
            var mablK = line.MABL_K ?? 0d;
            var meghK = line.MEGHk ?? 0d;
            var smab  = line.SMAB ?? 0d;
            var sakht = smab * meghK;

            double codeNum = 0; string kalaName = "";

            if (mablK != 0)
            {
                if (!TryGetAccountCode(line.CODE, out var codeLong))
                    throw new InvalidOperationException($"کد کالای '{line.CODE}' در برگه {sheetNo} نامعتبر است.");
                codeNum = codeLong;
                var mablRounded = Math.Round(mablK);
                kalaName = await getKalaName(codeNum);

                if (line.ANBAR is null)
                    throw new InvalidOperationException($"شماره انبار برای کالای '{line.CODE}' در برگه {sheetNo} خالی است.");
                var anbar = line.ANBAR.Value;

                await creatHes(acc.MOGODIA, anbar, codeNum, kalaName);
                addDetail(acc.MOGODIA!.Value, anbar, codeNum, $"{acc.MOGODIA}-{anbar}-{codeNum}", lineSharh, 0, mablRounded);

                var rdd = await getGroupKala(line.CODE);
                var phazMoin = (rdd == 2 || rdd == 3) ? 2 : 1;
                await creatHes(acc.PHAZ_TOL, phazMoin, codeNum, kalaName);
                addDetail(acc.PHAZ_TOL!.Value, phazMoin, codeNum, $"{acc.PHAZ_TOL}-{phazMoin}-{codeNum}", lineSharh, 0, mablRounded);

                double hesK, hesM, hesT; string hesCombined;
                if (string.IsNullOrEmpty(line.COM))
                {
                    if (line.N_KOL is null || line.NUMBER is null || line.TNUMBER is null)
                        throw new InvalidOperationException($"حساب فرمول ساخت برای کالای '{line.CODE}' در برگه {sheetNo} خالی است.");
                    hesK = line.N_KOL.Value; hesM = line.NUMBER.Value; hesT = line.TNUMBER.Value;
                    hesCombined = $"{line.N_KOL}-{line.NUMBER}-{line.TNUMBER}";
                }
                else
                {
                    if (!TryGetAccountCode(line.COM, out var comLong))
                        throw new InvalidOperationException($"کد فرمول ساخت برای کالای '{line.CODE}' در برگه {sheetNo} نامعتبر است.");
                    hesK = acc.HAZ_TOL!.Value; hesM = comLong; hesT = codeNum;
                    hesCombined = $"{acc.HAZ_TOL}-{comLong}-{codeNum}";
                }
                await creatHes(hesK, hesM, hesT, kalaName);
                addDetail(hesK, hesM, hesT, hesCombined, lineSharh, mablRounded, 0);
            }

            var jamch = Math.Round(mablK);

            if (sakht != 0)
            {
                if (!TryGetAccountCode(line.CODE, out var codeLong2) || !TryGetAccountCode(line.COM, out var comLong2))
                    throw new InvalidOperationException($"کد کالا/فرمول برای برگه {sheetNo} نامعتبر است.");
                addDetail(acc.CONKAL!.Value, comLong2, codeLong2, $"{acc.CONKAL}-{comLong2}-{codeLong2}", lineSharh, Math.Round(sakht), 0);
            }

            if (jamch - sakht != 0)
            {
                var amalIsBed = jamch > sakht;
                var amalValue = amalIsBed ? Math.Round(jamch - sakht) : Math.Round(sakht - jamch);

                if (!TryGetAccountCode(line.CODE, out var codeLong3) || !TryGetAccountCode(line.COM, out var comLong3))
                    throw new InvalidOperationException($"کد کالا/فرمول برای برگه {sheetNo} نامعتبر است.");

                if (codeNum == 0) codeNum = codeLong3;
                if (string.IsNullOrEmpty(kalaName)) kalaName = await getKalaName(codeNum);

                await creatHes(acc.AMALKARD, comLong3, codeLong3, kalaName);
                addDetail(acc.AMALKARD!.Value, comLong3, codeLong3, $"{acc.AMALKARD}-{comLong3}-{codeLong3}",
                    lineSharh, amalIsBed ? amalValue : 0, amalIsBed ? 0 : amalValue);
            }
        }

        private static async Task ProcessFinalLineAsync(
            FinalLineRow line, HeadRow sheet, double sheetNo, SazmanAccounts acc,
            Action<double, double, double, string, string, double, double> addDetail,
            Func<double?, double?, double?, string, Task> creatHes,
            Func<double, double, double, Task<bool>> isHesab,
            Func<double, Task<string>> getKalaName,
            Action<string> addLog)
        {
            var lineSharh = LeftTrim(
                $"حواله خروج شماره {sheet.NUMBER}-{sheet.FNUMCO} مورخ {PersianDate(sheet.DATE_N!.Value)} به مقدار{line.MEGHk}", 255);
            var mablK = line.MABL_K ?? 0d;
            var meghK = line.MEGHk ?? 0d;
            var smab  = line.SMAB ?? 0d;
            var sakht = smab * meghK;
            var jamch = Math.Round(mablK);

            if (mablK == 0 && sakht == 0 && jamch - sakht == 0) return;

            if (!TryGetAccountCode(line.CODE, out var codeLong))
                throw new InvalidOperationException($"کد کالای '{line.CODE}' در برگه {sheetNo} نامعتبر است.");
            var codeNum = (double)codeLong;
            var kalaName = await getKalaName(codeNum);

            if (mablK != 0)
            {
                var mablRounded = Math.Round(mablK);
                if (line.ANBAR is null)
                    throw new InvalidOperationException($"شماره انبار برای کالای '{line.CODE}' در برگه {sheetNo} خالی است.");
                var anbar = line.ANBAR.Value;

                addDetail(acc.MOGODIA!.Value, anbar, codeNum, $"{acc.MOGODIA}-{anbar}-{codeNum}", lineSharh, 0, mablRounded);
                addDetail(acc.PHAZ_TOL!.Value, 1, codeNum, $"{acc.PHAZ_TOL}-1-{codeNum}", lineSharh, 0, mablRounded);

                if (!await isHesab(acc.HAZ_TOL!.Value, 99999, codeNum))
                    await creatHes(acc.HAZ_TOL, 99999, codeNum, kalaName);
                addDetail(acc.HAZ_TOL!.Value, 99999, codeNum, $"{acc.HAZ_TOL}-99999-{codeNum}", lineSharh, mablRounded, 0);
            }

            if (sakht != 0)
            {
                await creatHes(acc.CONKAL, 99999, codeNum, kalaName);
                addDetail(acc.CONKAL!.Value, 99999, codeNum, $"{acc.CONKAL}-99999-{codeNum}", lineSharh, Math.Round(sakht), 0);
            }

            if (jamch - sakht != 0)
            {
                try { await creatHes(acc.AMALKARD, 99999, codeNum, kalaName); }
                catch (Exception ex) { addLog($"برگ {sheetNo}: حساب عملکرد {acc.AMALKARD}-99999-{line.CODE}: {ex.Message}"); }

                var amalIsBed = jamch > sakht;
                var amalValue = amalIsBed ? Math.Round(jamch - sakht) : Math.Round(sakht - jamch);
                addDetail(acc.AMALKARD!.Value, 99999, codeNum, $"{acc.AMALKARD}-99999-{codeNum}",
                    lineSharh, amalIsBed ? amalValue : 0, amalIsBed ? 0 : amalValue);
            }
        }

        // ───── رزرو دسته‌ای شماره سند (قفل عمدی + Serializable، یک تراکنش برای کل دسته) ─────

        private async Task<List<double>> ReserveSanadNumbersBatchAsync(List<SanadHeaderRequest> headers)
        {
            var reserved = new List<double>(headers.Count);
            if (headers.Count == 0) return reserved;

            const int batchSize = 5000;
            for (int start = 0; start < headers.Count; start += batchSize)
            {
                var length = Math.Min(batchSize, headers.Count - start);
                var chunkReserved = await _db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    var chunk = new List<double>(length);

                    // قفل صریح روی همان منبع نام‌گذاری‌شده‌ای که Pay2RunController برای
                    // شماره‌گذاری DEED_HED استفاده می‌کند (Pay2RunController.cs:596-599).
                    // برخلاف ترفند «UPDATE یک ردیف ثابت»، این روش وقتی جدول خالی است هم
                    // واقعاً قفل می‌گیرد.
                    await conn.ExecuteAsync(
                        "EXEC sp_getapplock @Resource = 'DeedNumberAllocation', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
                        transaction: tx, commandTimeout: 3600);

                    var maxNs = (await conn.QueryAsync<double?>("SELECT MAX(N_S) FROM dbo.DEED_HED WITH (UPDLOCK)", transaction: tx)).FirstOrDefault();
                    var maxBg = (await conn.QueryAsync<double?>("SELECT MAX(BAYEG) FROM dbo.DEED_HED WITH (UPDLOCK)", transaction: tx)).FirstOrDefault();

                    var nextNs = (maxNs ?? 0) + 1;
                    var nextBg = maxBg.HasValue ? maxBg.Value + 1 : 100000000;

                    var values = new List<string>(length);
                    for (int i = 0; i < length; i++)
                    {
                        var h = headers[start + i];
                        var ns = nextNs + i;
                        var bg = nextBg + i;
                        chunk.Add(ns);
                        values.Add($"({SqlNum(ns)},{h.DATE_S},N'{SqlText(h.SHARH_S)}',0,8,1,N'{SqlText(h.USER_NAME)}',GETDATE(),NULL,{SqlNum(bg)})");
                    }

                    const int insertChunk = 500;
                    for (int off = 0; off < values.Count; off += insertChunk)
                    {
                        var sql = "INSERT INTO dbo.DEED_HED (N_S,DATE_S,SHARH_S,GHATEI,NO_S,OKF,USER_NAME,CRT,uid,BAYEG) VALUES " +
                                   string.Join(",", values.Skip(off).Take(insertChunk));
                        await conn.ExecuteAsync(sql, transaction: tx, commandTimeout: 3600);
                    }

                    return chunk;
                }, IsolationLevel.Serializable);

                reserved.AddRange(chunkReserved);
            }

            return reserved;
        }
    }
}
