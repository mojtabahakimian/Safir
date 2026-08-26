using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Utility;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  رفع دستیِ مغایرت CHK-02 (کارت انبار ≠ حسابداری) با یک سند اصلاحی —
    //  نه یک پورت از AUTO_BAZ (این مکانیزم آنجا وجود نداشت)، بلکه یک ابزار
    //  تازه به درخواست صاحب پروژه: کاربر یک حساب مقصد (مثلاً سود و زیان)
    //  انتخاب می‌کند، و اختلافِ کارت‌انبار−حسابداری بین حساب موجودیِ همان
    //  انبار (از CC_AnbarHes) و حساب مقصد جابه‌جا می‌شود تا مغایرت صفر شود.
    //
    //  NO_S = 0، طبق تأیید صاحب پروژه؛ همان کدی که سیستم برای «سند
    //  دستی/اصلاحی» عمومی (از جمله سند افتتاحیه) همیشه استفاده کرده.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class CorrectionPreviewLine
    {
        public long    ExceptionId      { get; set; }
        public int     Anbar            { get; set; }
        public long    Code             { get; set; }
        public string? ItemName         { get; set; }
        public double  Amount           { get; set; }   // کارت انبار − حسابداری، همان CC_Exception.Amount
        public double  AdjustAbs        { get; set; }   // مبلغ سند (قدرمطلق، گرد‌شده)
        public bool    DebitIsInventory { get; set; }   // true = انبار بدهکار / مقصد بستانکار
    }

    public sealed class CorrectionSkipped
    {
        public long   ExceptionId { get; set; }
        public string Reason      { get; set; } = string.Empty;
    }

    public sealed class CorrectionResult
    {
        public bool    Success      { get; set; }
        public int     Count        { get; set; }
        public double  TotalAbs     { get; set; }
        public double? SanadNumber  { get; set; }
        public List<CorrectionPreviewLine> Lines   { get; set; } = new();
        public List<CorrectionSkipped>     Skipped { get; set; } = new();
        public string? FirstError   { get; set; }
    }

    public sealed class CorrectionEntryService
    {
        private readonly IDatabaseService _db;
        public CorrectionEntryService(IDatabaseService db) => _db = db;

        private sealed class ExceptionRow
        {
            public long    ExceptionId { get; set; }
            public int?    Anbar       { get; set; }
            public long?   Code        { get; set; }
            public double? Amount      { get; set; }
            public bool    IsResolved  { get; set; }
            public string? ItemName    { get; set; }
        }

        private sealed class AnbarHesRow
        {
            public int Anbar   { get; set; }
            public int HesKol  { get; set; }
            public int HesMoin { get; set; }
        }

        private static string SqlNum(double? v) => v is null ? "NULL" : v.Value.ToString("0.##########", CultureInfo.InvariantCulture);
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];

        public async Task<CorrectionResult> PostAsync(
            List<long> exceptionIds, int targetKol, int targetMoin, int targetTaf,
            string? note, string userName, bool whatIf, long? dateS = null, CancellationToken ct = default)
        {
            var result = new CorrectionResult();
            if (exceptionIds.Count == 0) { result.Success = true; return result; }

            var rows = (await _db.DoGetDataSQLAsync<ExceptionRow>(
                @"SELECT e.ExceptionId, e.Anbar, e.Code, e.Amount, e.IsResolved, s.NAME AS ItemName
                  FROM   dbo.CC_Exception e
                  LEFT   JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = e.Code
                  WHERE  e.ExceptionId IN @ids AND e.RuleCode = 'CHK-02'",
                new { ids = exceptionIds })).ToList();

            var anbarHes = (await _db.DoGetDataSQLAsync<AnbarHesRow>(
                    "SELECT Anbar, HesKol, HesMoin FROM dbo.CC_AnbarHes"))
                .ToDictionary(a => a.Anbar);

            var lines   = new List<CorrectionPreviewLine>();
            var skipped = new List<CorrectionSkipped>();

            foreach (var id in exceptionIds)
            {
                var r = rows.FirstOrDefault(x => x.ExceptionId == id);
                if (r is null) { skipped.Add(new CorrectionSkipped { ExceptionId = id, Reason = "استثنا یافت نشد یا CHK-02 نیست" }); continue; }
                if (r.IsResolved) { skipped.Add(new CorrectionSkipped { ExceptionId = id, Reason = "قبلاً رفع شده" }); continue; }
                if (r.Anbar is null || r.Code is null || r.Amount is null)
                { skipped.Add(new CorrectionSkipped { ExceptionId = id, Reason = "انبار/کد/مبلغ ناقص است" }); continue; }
                if (!anbarHes.TryGetValue(r.Anbar.Value, out var ah))
                { skipped.Add(new CorrectionSkipped { ExceptionId = id, Reason = $"نگاشت حساب موجودی برای انبار {r.Anbar} در تنظیمات ثبت نشده" }); continue; }

                var adjust = Math.Round(Math.Abs(r.Amount.Value));
                if (adjust == 0)
                { skipped.Add(new CorrectionSkipped { ExceptionId = id, Reason = "مبلغ صفر است" }); continue; }

                lines.Add(new CorrectionPreviewLine
                {
                    ExceptionId      = r.ExceptionId,
                    Anbar            = r.Anbar.Value,
                    Code             = r.Code.Value,
                    ItemName         = r.ItemName,
                    Amount           = r.Amount.Value,
                    AdjustAbs        = adjust,
                    DebitIsInventory = r.Amount.Value > 0
                });
            }

            result.Count    = lines.Count;
            result.TotalAbs = lines.Sum(l => l.AdjustAbs);
            result.Lines    = lines;
            result.Skipped  = skipped;

            if (whatIf || lines.Count == 0)
            {
                result.Success = true;
                return result;
            }

            try
            {
                var headerNote = string.IsNullOrWhiteSpace(note)
                    ? "سند اصلاحی مغایرت کارت انبار/حسابداری (CHK-02)"
                    : $"سند اصلاحی مغایرت کارت انبار/حسابداری (CHK-02) - {note}";

                // ⚠️ تاریخ سند باید داخل بازه‌ی دوره‌ای باشه که این مغایرت‌ها ازش
                // اومدن (پایان همون اجرا)، نه تاریخ واقعیِ امروز — چون CHK-02
                // حسابداری رو با شرط h.DATE_S <= @DT2 می‌خونه؛ سندی که بعد از
                // پایان دوره تاریخ بخوره، توی خودِ همون بستنِ ماه دیده نمی‌شه و
                // مغایرت بسته‌نشده باقی می‌مونه (روی تست واقعی کشف شد).
                var effectiveDate = dateS ?? CL_Tarikh.GetCurrentPersianDateAsLong();

                var reserved = await SanadNumbering.ReserveBatchAsync(_db, 0, new List<SanadHeaderRequest>
                {
                    new SanadHeaderRequest
                    {
                        DATE_S    = effectiveDate,
                        SHARH_S   = LeftTrim(headerNote, 250),
                        USER_NAME = userName
                    }
                });
                var ns = reserved[0];
                result.SanadNumber = ns;

                await CreatHesAsync(targetKol, targetMoin, targetTaf, "اصلاح مغایرت کارت انبار/حسابداری");

                var targetHes = $"{targetKol}-{targetMoin}-{targetTaf}";
                var dtlRows = new List<string>(lines.Count * 2);
                double radif = 1;

                foreach (var l in lines)
                {
                    var ah = anbarHes[l.Anbar];
                    var itemHes = $"{ah.HesKol}-{ah.HesMoin}-{l.Code}";
                    var sharh = LeftTrim(
                        $"اصلاح مغایرت کارت انبار/حسابداری کد {l.Code} انبار {l.Anbar}"
                        + (string.IsNullOrWhiteSpace(note) ? "" : $" - {note}"), 255);

                    if (l.DebitIsInventory)
                    {
                        dtlRows.Add(DtlRow(ns, ah.HesKol, ah.HesMoin, l.Code, itemHes, sharh, l.AdjustAbs, 0, radif++));
                        dtlRows.Add(DtlRow(ns, targetKol, targetMoin, targetTaf, targetHes, sharh, 0, l.AdjustAbs, radif++));
                    }
                    else
                    {
                        dtlRows.Add(DtlRow(ns, targetKol, targetMoin, targetTaf, targetHes, sharh, l.AdjustAbs, 0, radif++));
                        dtlRows.Add(DtlRow(ns, ah.HesKol, ah.HesMoin, l.Code, itemHes, sharh, 0, l.AdjustAbs, radif++));
                    }
                }

                const int chunkSize = 500;
                for (int off = 0; off < dtlRows.Count; off += chunkSize)
                {
                    var sql = "INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES,SHARH,BED,BES,ARZD,RADIF) VALUES "
                             + string.Join(",", dtlRows.Skip(off).Take(chunkSize)) + ";";
                    await _db.DoExecuteSQLAsync(sql);
                }

                var resolveNote = $"سند اصلاحی شماره {ns}" + (string.IsNullOrWhiteSpace(note) ? "" : $" - {note}");
                await _db.DoExecuteSQLAsync(
                    @"UPDATE dbo.CC_Exception
                         SET IsResolved = 1, ResolvedBy = @user, ResolvedAtUtc = SYSUTCDATETIME(),
                             ResolutionNote = @note
                       WHERE ExceptionId IN @ids AND IsResolved = 0",
                    new { user = userName, note = resolveNote, ids = lines.Select(l => l.ExceptionId).ToList() });

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.FirstError = ex.Message;
            }

            return result;
        }

        private static string DtlRow(double ns, double hesK, double hesM, double hesT, string hes, string sharh, double bed, double bes, double radif)
            => $"({SqlNum(ns)},{SqlNum(hesK)},{SqlNum(hesM)},{SqlNum(hesT)},N'{SqlText(hes)}',N'{SqlText(sharh)}',{SqlNum(bed)},{SqlNum(bes)},1,{SqlNum(radif)})";

        private async Task CreatHesAsync(int kol, int moin, int taf, string name)
        {
            var exists = (await _db.DoGetDataSQLAsync<int>(
                "SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf",
                new { Kol = kol, Moin = moin, Taf = taf })).Any();
            if (exists) return;

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
    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
END CATCH;";
            await _db.DoExecuteSQLAsync(sql, new { Kol = kol, Moin = moin, Taf = taf, Name = LeftTrim(name, 250) });
        }
    }
}
