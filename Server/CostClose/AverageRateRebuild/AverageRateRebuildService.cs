using Safir.Shared.Interfaces;
using System.Globalization;

namespace Safir.Server.CostClose.AverageRateRebuild
{
    // ═══════════════════════════════════════════════════════════════════════
    //  بازسازی «نرخ میانگین» کاردکس (INVO_LST.AVRAGE/AVRAGE2/MABL/MABL_K)
    //
    //  این یک پورتِ دستیِ (نه کپی خودکار) از متد C0_TASK در MainWindow.xaml.cs
    //  (پروژه‌ی دسکتاپ AUTO_BAZ) است — نه فراخوانیِ آن پروژه. فقط شاخه‌ی اصلی
    //  (وقتی Strings.Mid(Baseknow.OPTIONSS, 66, 1) == "5") پورت شده؛ شاخه‌ی
    //  دیگر (نرخ استاندارد به‌جای میانگین وزنی برای بعضی گروه‌های کالا) طبق
    //  تأیید صاحب پروژه منسوخ است و عمداً پورت نشده.
    //
    //  منطق حسابداری (فرمول هر TAG، ترتیب پیمایش، رفتار حاشیه‌ای) عیناً همان
    //  است؛ فقط زیرساخت با معماری Safir جایگزین شده — دقیقاً همان تغییرات
    //  زیرساختی که در MaterialIssueRebuildService.cs مستند شده‌اند (کانکشن
    //  به‌ازای هر اجرا به‌جای استاتیک سراسری، کش‌های محلی به همین فراخوانی،
    //  Task.WhenAll+SemaphoreSlim به‌جای Parallel.For همگام، خروجی به‌جای
    //  فایل لاگ روی دیسک).
    //
    //  ⚠️ عمداً کل تاریخچه از @SinceDate بازسازی می‌شود، نه فقط دوره‌ی این
    //  اجرا: نرخ میانگین متحرک هر تراکنش به نرخ *همه‌ی* تراکنش‌های قبلی همان
    //  (کالا، انبار) وابسته است؛ محدود کردن به دوره‌ی جاری نرخ‌های قدیمی‌تر
    //  از این ماه را دست‌نخورده می‌گذارد و اگر ماه‌های قبل هم اصلاح شده
    //  باشند، محاسبه از همان‌جا غلط می‌شود. مقدار پیش‌فرض این پارامتر همان
    //  ثابتِ کد اصلی («از ابتدای تاریخ سیستم») است.
    //
    //  ⚠️ برخلاف MaterialIssueRebuildService (که فقط روی DEED_HED/DEED_DTL
    //  واقعی می‌نویسد و به همین دلیل پشت دیالوگ تأیید دستی نگه داشته شده)،
    //  این سرویس فقط به INVO_LST/DTL_MANF/ANBGRD_LST می‌نویسد — جدول‌های
    //  هزینه/موجودی، نه دفتر حسابداری. پس می‌تواند مثل S07/S08 خودکار در
    //  ارکستریتور اجرا شود.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class AverageRateRebuildResult
    {
        public bool   Success        { get; set; }
        public int    ItemsProcessed { get; set; }
        public int    RowsUpdated    { get; set; }
        public string? FirstError    { get; set; }
        public List<string> Log      { get; set; } = new();
    }

    public sealed class AverageRateRebuildService
    {
        private readonly IDatabaseService _db;

        public AverageRateRebuildService(IDatabaseService db) => _db = db;

        // ───────── مدل‌های داخلی (فقط ستون‌های لازم؛ مثل MaterialIssueRebuildService) ─────────

        private sealed class OpeningBalanceRow
        {
            public string? CODE     { get; set; }
            public int?    ANBAR    { get; set; }
            public double? MOGODI_A { get; set; }
            public double? FI_A     { get; set; }
            public double? MABL_A   { get; set; }
        }

        /// <summary>
        /// ردیف INVO_LST که در طول پیمایش جهش می‌کند.
        /// ⚠️ عمداً فقط CODE/ANBAR/id از دیتابیس خوانده می‌شوند (دقیقاً مثل کد
        /// اصلی: «SELECT CODE,ANBAR,ID FROM INVO_LST»). فیلدهای AVRAGE/AVRAGE2/
        /// MABL/MABL_K با صفر شروع می‌شوند و فقط وقتی یک TAG همان id را در طول
        /// همین پیمایش لمس کند مقدار می‌گیرند — این همان رفتاری است که کد اصلی
        /// دارد؛ اگر TAG=4 (برگشت فروش) به رکوردی برسد که خودش هرگز TAG دیگری
        /// نداشته، rst3Filter.AVRAGE هنوز صفر است و MBKM با صفر جمع می‌شود.
        /// این رفتار عمداً دست‌نخورده مانده، نه اصلاح شده.
        /// </summary>
        private sealed class InvoLstMutable
        {
            public long    id     { get; set; }
            public string? CODE   { get; set; }
            public int?    ANBAR  { get; set; }
            public double  AVRAGE  { get; set; }
            public double  AVRAGE2 { get; set; }
            public double  MABL    { get; set; }
            public double  MABL_K  { get; set; }

            /// <summary>
            /// true فقط بعد از این‌که Case 5 (انتقالی خروج) روی همین سطر
            /// MABL_K را در همین اجرا محاسبه کرده باشد — نگاه کنید توضیح
            /// Case 6 برای این‌که چرا لازم است.
            /// </summary>
            public bool    Touched { get; set; }
        }

        private sealed class AnbgrdLstRow
        {
            public string? CODE    { get; set; }
            public double? GRD_NUM { get; set; }
            public double  MABL    { get; set; }
        }

        /// <summary>
        /// یک حواله‌ی انتقالی برای یک کالا: انبار مبدأ باید کامل پردازش شده
        /// باشد (و MABL_K روی سطر TAG=5 نوشته شده باشد) پیش از آن‌که انبار
        /// مقصد، Case 6 را برای همان سند اجرا کند — نگاه کنید
        /// <see cref="OrderAnbarsForTransferDependencies"/>.
        /// </summary>
        private sealed class TransferEdgeRow
        {
            public string? CODE { get; set; }
            public int?    Src  { get; set; }
            public int?    Dst  { get; set; }
        }

        private sealed class KardexRow
        {
            public long?   DATE_N   { get; set; }
            public int?    TAG      { get; set; }
            public double? NUMBER   { get; set; }
            public int?    ANBAR    { get; set; }
            public string? CODE     { get; set; }
            public double? MEGH     { get; set; }
            public double? MEGHk    { get; set; }
            public double? MEGH_MAR { get; set; }
            public double? MABL     { get; set; }
            public double? MABL_K   { get; set; }
            public double? N_KOL    { get; set; }
            public long?   id       { get; set; }
        }

        private sealed class StdPriceRow
        {
            public double? SumOfMABLK    { get; set; }
            public double? IMBIBE_MANF   { get; set; }
            public double? IMBIBE_SAR    { get; set; }
        }

        /// <summary>مانده‌ی متحرک (MBKM/MIAN/MOGUDI) یک (کالا،انبار) — کلاس
        /// به‌جای struct عمداً، چون باید در Dictionary&lt;int,...&gt; جهش‌پذیر
        /// بماند و بین ProcessRowAsync و هر دو حلقه‌ی فراخوان‌کننده (سریال
        /// تک‌انباره و ادغام‌شده‌ی چندانباره) به اشتراک برود.</summary>
        private sealed class AnbarState
        {
            public double MBKM;
            public double MIAN;
            public double MOGUDI;
        }

        // ───────── کمکی قالب‌بندی عدد مستقل از Culture (مثل N() کد اصلی) ─────────

        private static string SqlNum(double v) => v.ToString(CultureInfo.InvariantCulture);

        // ───────── محدودیت موازی‌سازی — مثل MaterialIssueRebuildService ─────────

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

        // ───────── GETSTANDARDPRICE_KOL / GETSTANDARDPRICE / GETFIRSTPRICE ─────────
        // پورت مستقیم از CL_HESABDARI_AUTO_BAZ.cs (فقط منبع خواندنی؛ بازسازی به
        // HEAD_MANF/DTL_MANF نمی‌نویسد).

        private async Task<double> GetStandardPriceKolAsync(string code)
        {
            var rst = (await _db.DoGetDataSQLAsync<StdPriceRow>(
                @"SELECT TOP 1 SUM(dm.MABLK) AS SumOfMABLK, hm.IMBIBE_MANF, hm.IMBIBE_SAR
                  FROM dbo.HEAD_MANF hm INNER JOIN dbo.DTL_MANF dm ON hm.FNUMB = dm.FNUMB
                  WHERE hm.CODE = @Code
                  GROUP BY hm.IMBIBE_MANF, hm.IMBIBE_SAR, hm.FNUMB
                  ORDER BY hm.FNUMB", new { Code = code })).FirstOrDefault();

            if (rst is null) return 0d;
            return (rst.SumOfMABLK ?? 0) + (rst.IMBIBE_MANF ?? 0) + (rst.IMBIBE_SAR ?? 0);
        }

        private Task<double> GetStandardPriceAsync(string code) => GetStandardPriceKolAsync(code);

        private async Task<double> GetFirstPriceAsync(string code)
        {
            var fi = (await _db.DoGetDataSQLAsync<double?>(
                "SELECT TOP 1 FI_A FROM dbo.STUF_FSK WHERE CODE = @Code ORDER BY FI_A DESC",
                new { Code = code })).FirstOrDefault();
            return fi ?? 0d;
        }

        // ═══════════════════════════════════════════════════════════════
        //  متد اصلی
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// بازسازی نرخ میانگین متحرک برای همه‌ی کالاهایی که تراکنش دارند، از
        /// <paramref name="sinceDate"/> به بعد. پیش‌فرض ۱۰۱۰۱ (معادل «از ابتدا»
        /// در کد اصلی؛ چون مقایسه عددی است، هر مقدار کوچک‌تر از کوچک‌ترین
        /// DATE_N واقعی همان اثر را دارد).
        /// </summary>
        public async Task<AverageRateRebuildResult> RebuildAsync(long sinceDate = 10101, CancellationToken ct = default)
        {
            var log = new List<string>();
            var result = new AverageRateRebuildResult { Log = log };
            var logLock = new object();
            string? firstError = null;
            var rowsUpdated = 0;

            void AddLog(string msg) { lock (logLock) { log.Add(msg); } }
            void RecordFailure(string msg)
            {
                lock (logLock) { log.Add(msg); firstError ??= msg; }
            }

            AddLog($"بازسازی نرخ میانگین: شروع از تاریخ {sinceDate}.");

            // آیا جدول‌های پشتیبان سال قبل (FBK/KBK) روی این دیتابیس وجود دارند؟
            // بعضی شرکت‌ها این دو جدول را ندارند (رول‌آور سال مالی که هرگز
            // برایشان اجرا نشده)؛ کوئری منبع کاردکس باید بدون آن‌ها هم کار کند.
            var hasFbk = (await _db.DoGetDataSQLAsync<int>(
                "SELECT 1 FROM sys.tables WHERE name = 'HEAD_LST_FBK'")).Any();
            var hasKbk = (await _db.DoGetDataSQLAsync<int>(
                "SELECT 1 FROM sys.tables WHERE name = 'HEAD_LST_KBK'")).Any();

            // فلگ «هزینه تولید از HEAD_MANF/DTL_MANF بخواند» — همان
            // Strings.Mid(Baseknow.OPTIONSS, 56, 1) == "5" کد اصلی.
            var optionss = (await _db.DoGetDataSQLAsync<string>(
                "SELECT TOP 1 OPTIONSS FROM dbo.SAZMAN")).FirstOrDefault() ?? string.Empty;
            var useManfCostForProduction = optionss.Length >= 56 && optionss[55] == '5';

            var openingBalances = (await _db.DoGetDataSQLAsync<OpeningBalanceRow>(
                @"SELECT S.CODE, F.ANBAR, F.MOGODI_A, F.FI_A, F.MABL_A
                  FROM dbo.STUF_DEF S INNER JOIN dbo.STUF_FSK F ON S.CODE = F.CODE")).ToList();

            var allInvoLines = (await _db.DoGetDataSQLAsync<InvoLstMutable>(
                "SELECT CODE, ANBAR, ID AS id FROM dbo.INVO_LST")).ToList();
            var invoById = new Dictionary<long, InvoLstMutable>(allInvoLines.Count);
            foreach (var row in allInvoLines)
                if (!invoById.ContainsKey(row.id)) invoById[row.id] = row;

            var anbgrdLines = (await _db.DoGetDataSQLAsync<AnbgrdLstRow>(
                "SELECT CODE, GRD_NUM, MABL FROM dbo.ANBGRD_LST")).ToList();

            // یال‌های وابستگیِ حواله‌ی انتقالی (مبدأ → مقصد) به‌ازای هر کالا —
            // برای مرتب‌سازی امنِ انبارها پیش از پیمایش؛ نگاه کنید
            // OrderAnbarsForTransferDependencies.
            var edgesByCode = (await _db.DoGetDataSQLAsync<TransferEdgeRow>(
                    @"SELECT DISTINCT i.CODE, i.ANBAR AS Src, CAST(i.ANBARF AS INT) AS Dst
                      FROM dbo.INVO_LST i
                      WHERE i.TAG = 5 AND i.ANBARF IS NOT NULL AND i.ANBAR <> CAST(i.ANBARF AS INT)"))
                .Where(e => e.CODE is not null && e.Src is not null && e.Dst is not null)
                .GroupBy(e => e.CODE!)
                .ToDictionary(g => g.Key, g => g.Select(e => (Src: e.Src!.Value, Dst: e.Dst!.Value)).ToList());

            AddLog($"موجودی اول دوره: {openingBalances.Count} ردیف. کاردکس: {allInvoLines.Count} ردیف. انبارگردانی: {anbgrdLines.Count} ردیف. یال انتقالی: {edgesByCode.Sum(kv => kv.Value.Count)} مورد.");

            // فقط کالاهایی که واقعاً تراکنش دارند (فاکتور یا انبارگردانی) —
            // مثل کد اصلی، برای جلوگیری از کوئری بی‌مورد روی کالاهای بدون گردش.
            var codesWithInvoice = allInvoLines
                .Select(x => (Code: x.CODE?.Trim(), Anbar: x.ANBAR ?? 0))
                .ToHashSet();
            var codesWithStockCount = anbgrdLines
                .Select(x => x.CODE?.Trim())
                .ToHashSet();

            var groupedByCode = openingBalances
                .Where(r => codesWithInvoice.Contains((r.CODE?.Trim(), r.ANBAR ?? 0))
                            || codesWithStockCount.Contains(r.CODE?.Trim()))
                .GroupBy(x => x.CODE)
                .ToList();

            AddLog($"{groupedByCode.Count} کالا برای بازسازی یافت شد.");

            var maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);

            // ───────── پردازش یک سطر کاردکس روی مانده‌ی متحرکِ یک انبار ─────────
            // این تابع محلی هم از حلقه‌ی سریالِ تک‌انباره (اکثریت کالاها) و هم
            // از حلقه‌ی ادغام‌شده‌ی چندانباره (کالاهای چرخه‌دار) صدا زده می‌شود؛
            // دقیقاً همان فرمول هر Case، فقط MBKM/MIAN/MOGUDI محلی جایش را به
            // st (مانده‌ی همان انبارِ مشخص) داده است.
            async Task ProcessRowAsync(string code, KardexRow t, AnbarState st, List<string> pending)
            {
                InvoLstMutable? line = null;
                if (t.id.HasValue) invoById.TryGetValue(t.id.Value, out line);

                switch (t.TAG)
                {
                    case 1: // خرید
                    {
                        st.MBKM += t.MABL_K ?? 0;
                        st.MOGUDI += t.MEGHk ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 22: // برگشت فروش سال قبل
                    {
                        if (st.MBKM <= 0d) st.MBKM = (t.MABL ?? 0) * (t.MEGH_MAR ?? 0);
                        else st.MBKM += st.MIAN * (t.MEGH_MAR ?? 0);
                        st.MOGUDI += t.MEGH_MAR ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 24: // برگشت فروش سال قبل
                    {
                        if (st.MBKM <= 0d) st.MBKM = t.MABL_K ?? 0;
                        else st.MBKM += (t.MEGHk ?? 0) * st.MIAN;
                        st.MOGUDI += t.MEGHk ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 2: // فروش
                    {
                        st.MBKM -= (t.MEGHk ?? 0) * st.MIAN;
                        st.MOGUDI -= t.MEGHk ?? 0;
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 3: // برگشت خرید
                    {
                        st.MBKM -= (t.MEGH_MAR ?? 0) * st.MIAN;
                        st.MOGUDI -= t.MEGH_MAR ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }
                        if (line is not null)
                        {
                            line.AVRAGE2 = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE2 = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 4: // برگشت فروش
                    {
                        // ⚠️ عیناً کد اصلی: کالا با نرخ منجمدِ لحظه‌ی فروشِ
                        // اصلی (line.AVRAGE) وارد انبار می‌شود؛ این طبیعتاً
                        // روی میانگین جاری اثر می‌گذارد و نتیجه‌ی بلندشده در
                        // AVRAGE2 نوشته می‌شود — تأیید کاربر. (رفع ترتیب
                        // واقعی مشکل در BuildKardexAsync/شاخه‌ی BACK_HEAD بود،
                        // نه در این فرمول — نگاه کنید کامنت آن‌جا.)
                        st.MBKM += (t.MEGH_MAR ?? 0) * (line?.AVRAGE ?? 0);
                        st.MOGUDI += t.MEGH_MAR ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }
                        if (line is not null)
                        {
                            line.AVRAGE2 = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE2 = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 5: // انتقالی خروج
                    {
                        st.MBKM -= (t.MEGHk ?? 0) * st.MIAN;
                        st.MOGUDI -= t.MEGHk ?? 0;
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            line.MABL = st.MIAN;
                            line.MABL_K = Math.Round(st.MIAN * (t.MEGHk ?? 0));
                            line.Touched = true;
                            pending.Add($@"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)}, MABL = {SqlNum(st.MIAN)}, MABL_K = {SqlNum(line.MABL_K)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 6: // انتقالی ورود
                    {
                        // ⚠️ عمداً از line.MABL_K (مقدار زنده‌ی همین اجرا، اگر
                        // Case 5 قبلاً همین سطر را لمس کرده) به‌جای t.MABL_K
                        // (عکسِ لحظه‌ی fetch اولیه) استفاده می‌شود. برای کالاهای
                        // بدون چرخه، هر دو یکی‌اند چون BuildKardexAsync برای
                        // انبار مقصد بعد از commit شدنِ انبار مبدأ دوباره از
                        // دیتابیس می‌خواند. برای کالاهای چرخه‌دار که همه‌ی
                        // انبارها یک‌جا و از قبل fetch شده‌اند (نگاه کنید
                        // ProcessCyclicCodeAsync)، t.MABL_K یک عکسِ قدیمی
                        // است و فقط line.MABL_K درست است — این همان باگی
                        // بود که روی کد ۳۶۸/انبار ۲ کشف شد.
                        var mablK = (line is not null && line.Touched) ? line.MABL_K : (t.MABL_K ?? 0);
                        st.MBKM += mablK;
                        st.MOGUDI += t.MEGHk ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }
                        if (line is not null)
                        {
                            line.AVRAGE2 = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE2 = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 10: // مواد خروج
                    case 11: // مواد سایر خروج
                    {
                        st.MBKM -= (t.MEGHk ?? 0) * st.MIAN;
                        st.MOGUDI -= t.MEGHk ?? 0;
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            line.MABL = st.MIAN;
                            line.MABL_K = Math.Round(st.MIAN * (t.MEGHk ?? 0));
                            pending.Add($@"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)}, MABL = {SqlNum(st.MIAN)}, MABL_K = {SqlNum(line.MABL_K)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 9: // تولید
                    {
                        double produced;
                        if (useManfCostForProduction && t.N_KOL is > 0)
                        {
                            var manf = (await _db.DoGetDataSQLAsync<StdPriceRow>(
                                @"SELECT SUM(dm.MABLK) AS SumOfMABLK, hm.IMBIBE_MANF, hm.IMBIBE_SAR
                                  FROM dbo.HEAD_MANF hm INNER JOIN dbo.DTL_MANF dm ON hm.FNUMB = dm.FNUMB
                                  WHERE hm.FNUMB = @Fnumb
                                  GROUP BY hm.IMBIBE_MANF, hm.IMBIBE_SAR",
                                new { Fnumb = t.N_KOL })).FirstOrDefault();
                            produced = manf is null
                                ? 0
                                : (manf.IMBIBE_MANF ?? 0) + (manf.IMBIBE_SAR ?? 0) + (manf.SumOfMABLK ?? 0);
                        }
                        else
                        {
                            produced = await GetStandardPriceKolAsync(code);
                            if (produced == 0) produced = await GetFirstPriceAsync(code);
                        }

                        if (line is not null)
                        {
                            line.MABL = produced;
                            line.MABL_K = Math.Round(produced * (t.MEGHk ?? 0));

                            st.MBKM += line.MABL_K;
                            st.MOGUDI += t.MEGHk ?? 0;
                            if (st.MBKM == 0d) { }
                            else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                            else { st.MIAN = st.MBKM / st.MOGUDI; }
                            line.AVRAGE = st.MIAN;

                            pending.Add($@"UPDATE dbo.INVO_LST SET MABL = {SqlNum(line.MABL)}, MABL_K = {SqlNum(line.MABL_K)}, AVRAGE = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                    case 17: // کسری انبار
                    {
                        st.MBKM += st.MIAN * (t.MEGHk ?? 0);
                        st.MOGUDI += t.MEGHk ?? 0;
                        if (st.MBKM == 0d) { }
                        else if (st.MOGUDI == 0d) { st.MBKM = 0d; }
                        else { st.MIAN = st.MBKM / st.MOGUDI; }

                        var grd = anbgrdLines.FirstOrDefault(x => x.CODE == t.CODE && x.GRD_NUM == t.NUMBER);
                        if (grd is not null) grd.MABL = st.MIAN;
                        pending.Add($"UPDATE dbo.ANBGRD_LST SET MABL = {SqlNum(st.MIAN)} WHERE CODE = '{t.CODE}' AND GRD_NUM = {SqlNum(t.NUMBER ?? 0)}");
                        break;
                    }
                    case 18: // اضافه انبار
                    {
                        // ⚠️ عمداً MIAN دوباره محاسبه نمی‌شود — عیناً کد اصلی.
                        st.MBKM -= (t.MEGHk ?? 0) * st.MIAN;
                        st.MOGUDI -= t.MEGHk ?? 0;
                        var grd = anbgrdLines.FirstOrDefault(x => x.CODE == t.CODE && x.GRD_NUM == t.NUMBER);
                        if (grd is not null) grd.MABL = st.MIAN;
                        pending.Add($"UPDATE dbo.ANBGRD_LST SET MABL = {SqlNum(st.MIAN)} WHERE CODE = '{t.CODE}' AND GRD_NUM = {SqlNum(t.NUMBER ?? 0)}");
                        break;
                    }
                    case 26: // برگشت خرید
                    {
                        st.MBKM -= (t.MEGHk ?? 0) * st.MIAN;
                        st.MOGUDI -= t.MEGHk ?? 0;
                        if (line is not null)
                        {
                            line.AVRAGE = st.MIAN;
                            pending.Add($"UPDATE dbo.INVO_LST SET AVRAGE = {SqlNum(st.MIAN)} WHERE ID = {line.id}");
                        }
                        break;
                    }
                }
            }

            async Task FlushPendingAsync(string code, int anbar, List<string> pending)
            {
                const int chunkSize = 200;
                for (int off = 0; off < pending.Count; off += chunkSize)
                {
                    var batch = new System.Text.StringBuilder();
                    var endAt = Math.Min(off + chunkSize, pending.Count);
                    for (int k = off; k < endAt; k++) batch.Append(pending[k]).Append(';').Append('\n');
                    try
                    {
                        await _db.DoExecuteSQLAsync(batch.ToString());
                        Interlocked.Add(ref rowsUpdated, endAt - off);
                    }
                    catch (Exception ex)
                    {
                        RecordFailure($"کالا {code} انبار {anbar}: خطا در اجرای دسته‌ی به‌روزرسانی: {ex.Message}");
                    }
                }
            }

            await ParallelForAsync(groupedByCode.Count, maxDegree, async groupIndex =>
            {
                if (ct.IsCancellationRequested) return;

                var codeGroup = groupedByCode[groupIndex];
                var codeKey = codeGroup.Key ?? string.Empty;

                // انبارهای یک کالا سریال پردازش می‌شوند: حواله‌ی انتقالی بین
                // انبارها وابستگی ترتیبی می‌سازد (TAG=5 مقداری می‌نویسد که
                // TAG=6 همان کالا در انبار دیگر بعداً می‌خواند). ترتیب پیش‌فرضِ
                // برگشتیِ کوئری (بدون ORDER BY) این وابستگی را تضمین نمی‌کند؛
                // اینجا با یک topological sort روی یال‌های مبدأ→مقصدِ حواله‌های
                // انتقالیِ همین کالا، ترتیب درست تحمیل می‌شود — نگاه کنید
                // OrderAnbarsForTransferDependencies. اگر چرخه باشد (topological
                // sort ناممکن)، به‌جای حدس زدنِ یک ترتیب، تمام انبارهای این
                // کالا در یک جریان زمانیِ واحد ادغام می‌شوند — نگاه کنید
                // ProcessCyclicCodeAsync.
                var (orderedAnbars, hasCycle) = OrderAnbarsForTransferDependencies(
                    codeGroup.ToList(),
                    edgesByCode.TryGetValue(codeKey, out var edges) ? edges : null,
                    msg => AddLog($"کالا {codeKey}: {msg}"));

                if (hasCycle)
                {
                    await ProcessCyclicCodeAsync(
                        codeKey, orderedAnbars, sinceDate, hasFbk, hasKbk,
                        ProcessRowAsync, FlushPendingAsync, RecordFailure, AddLog, ct);
                    return;
                }

                foreach (var rRow in orderedAnbars)
                {
                    if (ct.IsCancellationRequested) return;
                    if (string.IsNullOrWhiteSpace(rRow.CODE) || rRow.ANBAR is null) continue;

                    var code = rRow.CODE!;
                    var anbar = rRow.ANBAR!.Value;

                    var st = new AnbarState
                    {
                        MBKM   = rRow.MABL_A ?? 0,
                        MIAN   = rRow.FI_A ?? 0,
                        MOGUDI = rRow.MOGODI_A ?? 0
                    };

                    if (st.MIAN == 0d)
                    {
                        st.MIAN = await GetStandardPriceAsync(code);
                        if (st.MIAN == 0d) st.MIAN = await GetFirstPriceAsync(code);
                    }

                    List<KardexRow> kardex;
                    try
                    {
                        kardex = (await BuildKardexAsync(code, anbar, sinceDate, hasFbk, hasKbk)).ToList();
                    }
                    catch (Exception ex)
                    {
                        RecordFailure($"کالا {code} انبار {anbar}: خطا در خواندن کاردکس: {ex.Message}");
                        return;
                    }

                    var pending = new List<string>(kardex.Count);

                    foreach (var t in kardex)
                    {
                        if (ct.IsCancellationRequested) return;
                        await ProcessRowAsync(code, t, st, pending);
                    }

                    // اجرای دسته‌ای پیش از رفتن به انبار بعدیِ همین کالا — دقیقاً
                    // مثل کد اصلی، بدون BEGIN TRANSACTION (هر دستور Idempotent است).
                    await FlushPendingAsync(code, anbar, pending);
                }
            });

            result.Success = firstError is null;
            result.ItemsProcessed = groupedByCode.Count;
            result.RowsUpdated = rowsUpdated;
            result.FirstError = firstError;
            AddLog($"بازسازی نرخ میانگین: پایان — {groupedByCode.Count} کالا، {rowsUpdated} ردیف به‌روز شد، موفق={result.Success}.");
            return result;
        }

        /// <summary>
        /// کالاهای چرخه‌دار (نگاه کنید OrderAnbarsForTransferDependencies): به‌جای
        /// پردازش هر انبار به‌طور کامل و مجزا (که برای چرخه غیرممکن است — هیچ
        /// ترتیبِ خطیِ ثابتی هر دو طرف را همزمان درست نمی‌کند)، کاردکسِ *همه‌ی*
        /// انبارهای این کالا یک‌جا خوانده و بر اساس (DATE_N, BARGAH) در یک
        /// جریان زمانیِ واحد ادغام می‌شود. هر انبار مانده‌ی متحرکِ (MBKM/MIAN/
        /// MOGUDI) مستقلِ خودش را دارد؛ چون رویدادها به ترتیب زمانیِ واقعی
        /// پردازش می‌شوند (نه به ترتیبِ «انبار به انبار»)، Case ۵ی هر حواله‌ی
        /// انتقالی همیشه قبل از Case ۶ متناظرش پردازش می‌شود — چه چرخه باشد
        /// چه نباشد. UPDATEها همه در یک دسته‌ی نهایی flush می‌شوند (نه به‌ازای
        /// هر انبار)، چون دیگر concept «انبار بعدی» معنا ندارد.
        /// </summary>
        private async Task ProcessCyclicCodeAsync(
            string code,
            List<OpeningBalanceRow> anbarRows,
            long sinceDate, bool hasFbk, bool hasKbk,
            Func<string, KardexRow, AnbarState, List<string>, Task> processRowAsync,
            Func<string, int, List<string>, Task> flushPendingAsync,
            Action<string> recordFailure,
            Action<string> addLog,
            CancellationToken ct)
        {
            var states = new Dictionary<int, AnbarState>();
            foreach (var r in anbarRows)
            {
                if (r.ANBAR is null) continue;
                var st = new AnbarState
                {
                    MBKM   = r.MABL_A ?? 0,
                    MIAN   = r.FI_A ?? 0,
                    MOGUDI = r.MOGODI_A ?? 0
                };
                if (st.MIAN == 0d)
                {
                    st.MIAN = await GetStandardPriceAsync(code);
                    if (st.MIAN == 0d) st.MIAN = await GetFirstPriceAsync(code);
                }
                states[r.ANBAR.Value] = st;
            }

            List<KardexRow> kardex;
            try
            {
                kardex = (await BuildKardexAsync(code, anbar: null, sinceDate, hasFbk, hasKbk)).ToList();
            }
            catch (Exception ex)
            {
                recordFailure($"کالا {code} (چرخه‌دار): خطا در خواندن کاردکس ادغام‌شده: {ex.Message}");
                return;
            }

            addLog($"کالا {code}: پردازش ادغام‌شده روی {states.Count} انبار، {kardex.Count} تراکنش.");

            var pending = new List<string>(kardex.Count);

            foreach (var t in kardex)
            {
                if (ct.IsCancellationRequested) return;
                if (t.ANBAR is null || !states.TryGetValue(t.ANBAR.Value, out var st)) continue;

                await processRowAsync(code, t, st, pending);
            }

            await flushPendingAsync(code, -1, pending);
        }

        /// <summary>
        /// انبارهای یک کالا را طوری مرتب می‌کند که مبدأ هر حواله‌ی انتقالی
        /// (TAG=5) همیشه قبل از مقصدش (TAG=6، در انبار دیگر) پردازش شود —
        /// یک topological sort سادهٔ Kahn روی یال‌های مبدأ→مقصد. اگر انبارهای
        /// این کالا هیچ حواله‌ی انتقالی‌ای بینشان نداشته باشند (اکثر کالاها)،
        /// فوراً همان ترتیب ورودی را برمی‌گرداند — بدون سربار.
        ///
        /// چرا merge کردن کاردکسِ همه‌ی انبارها به یک جریان زمانی مشترک کافی
        /// نیست: TAGCOD.tartib برای TAG=5 (انتقالی-خروج) برابر ۱۴ و برای TAG=6
        /// (انتقالی-ورود) برابر ۱۰ است — یعنی مرتب‌سازی صرف بر اساس
        /// (DATE_N, tartib) دقیقاً برعکسِ چیزی می‌شود که لازم است (مقصد قبل
        /// از مبدأ). این تابع به‌جای آن روی سطح «کدام انبار کامل شده» تصمیم
        /// می‌گیرد، بدون دست‌زدن به ترتیب داخلیِ هر انبار (BuildKardexAsync).
        ///
        /// اگر بین انبارهای این کالا یک چرخه باشد (مثلاً هم از A به B و هم،
        /// در تاریخ دیگری، از B به A انتقال داده شده)، HasCycle=true برمی‌گردد
        /// تا فراخوان‌کننده به‌جای این تابع، از ProcessCyclicCodeAsync (ادغام
        /// واقعیِ همه‌ی انبارها به یک جریان زمانی مشترک) استفاده کند.
        /// </summary>
        private static (List<OpeningBalanceRow> Ordered, bool HasCycle) OrderAnbarsForTransferDependencies(
            List<OpeningBalanceRow> codeGroup,
            List<(int Src, int Dst)>? edges,
            Action<string> logWarning)
        {
            if (edges is null || edges.Count == 0 || codeGroup.Count <= 1)
                return (codeGroup, false);

            var anbarSet = codeGroup.Select(r => r.ANBAR).Where(a => a is not null).Select(a => a!.Value).ToHashSet();
            var relevantEdges = edges.Where(e => anbarSet.Contains(e.Src) && anbarSet.Contains(e.Dst)).Distinct().ToList();
            if (relevantEdges.Count == 0)
                return (codeGroup, false);

            var indegree = anbarSet.ToDictionary(a => a, _ => 0);
            var adjacency = anbarSet.ToDictionary(a => a, _ => new List<int>());
            foreach (var (src, dst) in relevantEdges)
            {
                adjacency[src].Add(dst);
                indegree[dst]++;
            }

            var queue = new Queue<int>(anbarSet.Where(a => indegree[a] == 0));
            var orderedAnbars = new List<int>(anbarSet.Count);
            while (queue.Count > 0)
            {
                var a = queue.Dequeue();
                orderedAnbars.Add(a);
                foreach (var next in adjacency[a])
                {
                    if (--indegree[next] == 0) queue.Enqueue(next);
                }
            }

            if (orderedAnbars.Count < anbarSet.Count)
            {
                var cyclic = anbarSet.Except(orderedAnbars);
                logWarning($"⚠️ چرخه‌ی وابستگیِ حواله‌ی انتقالی بین انبارهای [{string.Join(",", cyclic)}] — " +
                           "پردازش ادغام‌شده (جریان زمانی مشترک) استفاده می‌شود.");
                return (codeGroup, true);
            }

            var rank = orderedAnbars.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i);
            return (codeGroup.OrderBy(r => r.ANBAR is not null && rank.TryGetValue(r.ANBAR.Value, out var i) ? i : int.MaxValue).ToList(), false);
        }

        /// <summary>
        /// معادل BuildAvgRebuildSourceSql کد اصلی: تمام تراکنش‌های یک (کالا،
        /// انبار) از <paramref name="sinceDate"/> به بعد، به ترتیب تاریخ. با
        /// پارامتر Dapper به‌جای رشته‌چسبانی (بی‌نیاز از escape دستی).
        ///
        /// <paramref name="anbar"/> = null یعنی «همه‌ی انبارهای این کالا یک‌جا»
        /// (فقط برای کالاهای چرخه‌دار در ProcessCyclicCodeAsync استفاده می‌شود)؛
        /// شرط‌های ANBAR/ANBARF در SQL با «@Anbar IS NULL OR …» این حالت را
        /// بدون تکرار کوئری پوشش می‌دهند.
        /// </summary>
        private async Task<IEnumerable<KardexRow>> BuildKardexAsync(
            string code, int? anbar, long sinceDate, bool hasFbk, bool hasKbk)
        {
            // ⚠️ ترتیب داخل یک روز: TAGCOD.BARGAH (متن) نیست — TAGCOD.tartib
            // است، همان اصلاحی که با تأیید کاربر روی 14-s05-gate.sql زده شد
            // (مقایسه‌ی متنیِ BARGAH به ترتیب الفبای فارسی وابسته است، نه
            // ترتیب واقعیِ کسب‌وکار؛ فقط tartib عددی آن را می‌دهد). بدون «id»
            // به‌عنوان تای‌برک نهایی هم، ترتیب ردیف‌های هم‌روز و هم‌tartib
            // (مثلاً چند فاکتور فروش یا چند رسید خرید در یک روز — که برای کد
            // ۳۳۶۵ در ده‌ها روز از سال پیش می‌آید) توسط SQL Server نامعین
            // است؛ چون این پیمایش متوالی و حالت‌مند است (هر ردیف روی میانگین
            // متحرکِ ردیف قبلی بنا می‌شود)، یک تای نامعین در همان ابتدای سال
            // کل مسیر میانگین را تا انتهای ماه منحرف می‌کند — دقیقاً همان
            // علتِ نوسان ۹۷۰۴۲۴۲۹۵۳ ریالیِ سود کد ۳۳۶۵ بین اجراهای پیاپی.
            // id شناسه‌ی IDENTITY است، پس صعودی = ترتیب واقعیِ درج/رویداد.
            var parts = new List<string>
            {
                @"SELECT h.DATE_N, i.TAG, i.NUMBER, i.ANBAR, i.CODE, i.MEGH, i.MEGHk, i.MEGH_MAR,
                         i.MABL, i.MABL_K, i.N_KOL, i.ID AS id, t.tartib
                  FROM dbo.INVO_LST i
                  INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
                  INNER JOIN dbo.TAGCOD t ON h.TAG = t.CODE
                  WHERE i.CODE = @Code AND (@Anbar IS NULL OR i.ANBAR = @Anbar) AND h.DATE_N > @SinceDate",

                @"SELECT h.DATE_N, 6 AS TAG, i.NUMBER, i.ANBARF AS ANBAR, i.CODE, i.MEGH, i.MEGHk, i.MEGH_MAR,
                         i.MABL, i.MABL_K, i.N_KOL, i.ID AS id, t.tartib
                  FROM dbo.INVO_LST i
                  INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
                  INNER JOIN dbo.TAGCOD t ON h.TAG + 1 = t.CODE
                  WHERE i.CODE = @Code AND (@Anbar IS NULL OR i.ANBARF = @Anbar) AND h.DATE_N > @SinceDate AND i.TAG = 5",

                // tartib هم اینجا هاردکد است، نه از TAGCOD خوانده می‌شود —
                // چون TAG این شاخه (17 یا 18) واقعی نیست، خودِ همین کوئری با
                // CASE می‌سازدش؛ پس معادل عددی همان دو ردیف TAGCOD (کد ۱۷
                // tartib=5، کد ۱۸ tartib=13) را با همان شرط تکرار می‌کنیم.
                @"SELECT ah.GRD_DATE AS DATE_N,
                         CASE WHEN (al.MOG - al.NUM3) > 0 THEN 18 ELSE 17 END AS TAG,
                         al.GRD_NUM AS NUMBER, ah.GRD_ANBAR AS ANBAR, al.CODE,
                         (al.MOG - al.NUM3) AS MEGH, ABS(al.MOG - al.NUM3) AS MEGHk, 0 AS MEGH_MAR,
                         al.MABL AS MABL, ABS(al.MOG - al.NUM3) * al.MABL AS MABL_K,
                         CAST(NULL AS FLOAT) AS N_KOL, 0 AS id,
                         CASE WHEN (al.MOG - al.NUM3) > 0 THEN 13 ELSE 5 END AS tartib
                  FROM dbo.ANBGRD_LST al
                  INNER JOIN dbo.ANBGRD_HEAD ah ON al.GRD_NUM = ah.GRD_NUM
                  INNER JOIN dbo.STUF_DEF s ON al.CODE = s.CODE
                  WHERE al.CODE = @Code AND (@Anbar IS NULL OR ah.GRD_ANBAR = @Anbar)
                    AND (al.MOG - al.NUM3) * -1 <> 0 AND ah.GRD_DATE > @SinceDate
                    AND ah.N_S IS NOT NULL",

                // برگشت فروش نوع ۱ («همان TAG=2 است، هدرش TAG=4 هست» — طبق
                // توضیح صاحب پروژه): بر خلاف بقیه‌ی شاخه‌های این کوئری، این
                // یکی در TMPAV101 اصلی وجود نداشت (کد قدیمی هیچ‌جا BACK_HEAD
                // را نمی‌خواند) — عمداً و آگاهانه اضافه شده، نه یک پورت وفادار:
                // ردیف INVO_LST خودِ فروش (TAG=2) دوباره اینجا با برچسب TAG=4
                // در تاریخ واقعیِ برگشت (bh.DATE_N، نه تاریخ فروش اصلی) ظاهر
                // می‌شود تا Case 4 اجرا شود و AVRAGE2 را به‌روز کند. چون همان
                // ID فروش اصلی است، line.AVRAGE در Case 4 همان MIANِ لحظه‌ی
                // فروش خواهد بود — دقیقاً رفتاری که کامنت Case 4 توصیف می‌کند.
                //
                // ⚠️ اصلاح ترتیب (بعد از تست واقعی روی کد ۳۳۶۰/انبار ۸۰۷ کشف
                // شد): وقتی فروش اصلی (NUMBER1) هم‌روزِ خودِ برگشت باشد،
                // tartib واقعیِ TAGCOD برای کد ۴ («برگشت فروش») برابر ۶ است و
                // برای کد ۲ («حواله انبار فروش») برابر ۱۸ — یعنی حتی با
                // tartib عددی هم این شاخه قبل از خودِ فروش پردازش می‌شود،
                // دقیقاً همان لحظه‌ای که کامنتِ بالا می‌گوید «line.AVRAGE همان
                // MIANِ فروش خواهد بود»، هنوز صفر است (فروش هنوز پردازش
                // نشده)، پس MBKM با صفر جمع می‌شود ولی MOGUDI بدون مقابل
                // ارزشی رشد می‌کند — نرخ را مصنوعاً پایین می‌کشد (روی این
                // داده: ۸۸۱K به ۵۷۷K سقوط کرد).
                //
                // ⚠️ اصلاح دوم (بعد از تست واقعی روی برگشت فروش شماره ۵ / کد
                // ۳۶۸ انبار ۲ کشف شد): سنتینلِ همیشگیِ ۹۹۹۹ بیش از حد لازم
                // تهاجمی بود — وقتی تاریخ برگشت با تاریخ فروش اصلی یکی
                // نیست (این مورد: برگشت ۱۶/۰۲، فروش ۳۱/۰۱)، line.AVRAGE از
                // قبل درست تنظیم شده و نیازی به رفتن به انتهای روز نیست؛
                // هل‌دادنش به انتهای روز باعث می‌شد این ردیف بعد از یک جفتِ
                // ورود/فروشِ نامرتبطِ همان روز (۱۱۴۷۵ ورودی، ۱۲۰۵۵ فروش،
                // خالص ۵۸۰-) پردازش شود؛ MOGUDI درست روی صفر می‌نشست (۵۸۰-
                // + ۵۸۰ برگشت) و طبق قاعده‌ی مشترکِ «MOGUDI=۰ ⇒ MIAN
                // دست‌نخورده بماند»، AVRAGE2 به‌جای بازتابِ برگشت، میانگینِ
                // نامرتبطِ همان جفتِ رویداد را می‌گرفت. راه‌حل: سنتینل فقط
                // وقتی لازم است که برگشت واقعاً هم‌روزِ فروش اصلی باشد؛ در
                // غیر این صورت با tartib طبیعیِ خودش (۶) در جای واقعی‌اش
                // در همان روز مرتب شود.
                @"SELECT bh.DATE_N, 4 AS TAG, il.NUMBER, il.ANBAR, il.CODE, il.MEGH, il.MEGHk, il.MEGH_MAR,
                         il.MABL, il.MABL_K, il.N_KOL, il.ID AS id,
                         CASE WHEN bh.DATE_N = hs.DATE_N THEN 9999 ELSE 6 END AS tartib
                  FROM dbo.BACK_HEAD bh
                  INNER JOIN dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
                  INNER JOIN dbo.HEAD_LST hs ON hs.NUMBER = il.NUMBER AND hs.TAG = il.TAG
                  WHERE bh.ta = 2 AND il.MEGH_MAR <> 0
                    AND il.CODE = @Code AND (@Anbar IS NULL OR il.ANBAR = @Anbar) AND bh.DATE_N > @SinceDate"
            };

            if (hasFbk)
            {
                parts.Add(@"SELECT fb.DATE_N, 4 AS TAG, i.NUMBER, i.ANBAR, i.CODE, i.MEGH, i.MEGHk, i.MEGH_MAR,
                                   i.MABL, i.MABL_K, i.N_KOL, i.ID AS id, t.tartib
                            FROM dbo.INVO_LST i
                            INNER JOIN dbo.HEAD_LST_FBK fb ON i.NUMBER = fb.NUMBER1 AND i.TAG = fb.dtag
                            INNER JOIN dbo.TAGCOD t ON fb.htag = t.CODE
                            WHERE i.CODE = @Code AND (@Anbar IS NULL OR i.ANBAR = @Anbar) AND fb.DATE_N > @SinceDate");
            }

            if (hasKbk)
            {
                parts.Add(@"SELECT kb.DATE_N, 3 AS TAG, i.NUMBER, i.ANBAR, i.CODE, i.MEGH, i.MEGHk, i.MEGH_MAR,
                                   i.MABL, i.MABL_K, i.N_KOL, i.ID AS id, t.tartib
                            FROM dbo.INVO_LST i
                            INNER JOIN dbo.HEAD_LST_KBK kb ON i.NUMBER = kb.NUMBER1 AND i.TAG = kb.dtag
                            INNER JOIN dbo.TAGCOD t ON kb.htag = t.CODE
                            WHERE i.CODE = @Code AND (@Anbar IS NULL OR i.ANBAR = @Anbar) AND kb.DATE_N > @SinceDate");
            }

            var sql = "SELECT * FROM (" + string.Join(" UNION ", parts) + ") AS AVGSRC ORDER BY DATE_N, tartib, id";

            return await _db.DoGetDataSQLAsync<KardexRow>(sql, new { Code = code, Anbar = anbar, SinceDate = sinceDate });
        }
    }
}
