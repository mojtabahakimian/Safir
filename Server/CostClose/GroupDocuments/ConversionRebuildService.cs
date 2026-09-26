using Safir.Shared.Interfaces;
using System.Globalization;
using System.Text;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  سند حسابداری برگه‌ی تبدیل کالا (TAG=30، NO_S=10)
    //
    //  ── منطق ──
    //  عیناً همان سند انتقالی: یک آرتیکل دوطرفه به همان مبلغ، چون این هم
    //  فقط جابه‌جاییِ ارزشِ موجودی بین دو زیرحساب است نه هزینه یا سود.
    //    بستانکار: موجودی انبار مبدأ، زیرحسابِ کالای مبدأ
    //    بدهکار:   موجودی انبار مقصد، زیرحسابِ کالای *مقصد*
    //
    //  تنها تفاوتش با SANADENTEGHAL همین کلمه‌ی آخر است — آنجا هر دو طرف
    //  یک کد کالا دارند، اینجا دو کد. و دقیقاً به‌خاطر همین تفاوت است که
    //  نمی‌شد از خود برگه‌ی انتقالی استفاده کرد.
    //
    //  ── چرا حساب واسط ندارد ──
    //  روشِ قدیمی از یک حساب واسط («تبدیل») رد می‌شد، چون با دو برگه‌ی جدا
    //  راه دیگری نبود. با یک برگه، دو طرفِ سند مستقیم به هم می‌رسند و هیچ
    //  حسابی وسط نمی‌ماند که مانده بگیرد.
    //
    //  ── تک‌نخی، برخلاف انتقالی ──
    //  انتقالی روی یک پایگاه واقعی ۶۳۳ برگه دارد و موازی‌سازی‌اش ارزش
    //  داشت. تبدیل چند تا در ماه است؛ پیچیدگیِ موازی‌سازی و بازتلاشِ
    //  بن‌بست را نمی‌ارزد.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class ConversionRebuildResult
    {
        public bool         Success         { get; set; } = true;
        public int          SheetCount      { get; set; }
        public string?      FirstError      { get; set; }
        public List<string> Log             { get; set; } = new();
        public long?        LastSanadNumber { get; set; }
    }

    public sealed class ConversionRebuildService
    {
        private readonly IDatabaseService _db;
        public ConversionRebuildService(IDatabaseService db) => _db = db;

        private sealed class SheetRow
        {
            public double? NUMBER    { get; set; }
            public long?   DATE_N    { get; set; }
            public double? N_S       { get; set; }
            public string? USER_NAME { get; set; }

            public string? FromCode  { get; set; }
            public string? FromName  { get; set; }
            public int?    FromAnbar { get; set; }
            public double? FromQty   { get; set; }

            public string? ToCode    { get; set; }
            public string? ToName    { get; set; }
            public int?    ToAnbar   { get; set; }
            public double? ToQty     { get; set; }

            public double? MABL_K    { get; set; }
        }

        private static string SqlNum(double? v)
            => v.HasValue ? v.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "NULL";
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");
        private static string LeftTrim(string s, int max) => s.Length <= max ? s : s[..max];
        private static string PersianDate(long d) => $"{d / 10000:0000}/{d / 100 % 100:00}/{d % 100:00}";

        /// <summary>
        /// سند یک برگه‌ی مشخص — همان چیزی که دکمه‌ی «صدور سند» روی صفحه‌ی
        /// تبدیل صدا می‌زند. بازه‌ی تاریخ باز گذاشته می‌شود چون شماره‌ی برگه
        /// خودش یکتاست.
        /// </summary>
        /// <param name="allowZeroValue">
        /// سند با مبلغ صفر هم صادر شود؟ پیش‌فرض نه — چون سندِ صفر هیچ رقمی
        /// در دفتر نمی‌گذارد و معمولاً یعنی نرخ هنوز بازسازی نشده. ولی وقتی
        /// نرخِ کالا *واقعاً* صفر است (کالایی که با مبلغ صفر خریده شده)،
        /// برگه هیچ‌وقت مبلغ‌دار نمی‌شود و بی‌سند ماندنش برای تطبیق بدتر
        /// است. آن تصمیم با کاربر است، نه با این سرویس.
        /// </param>
        public Task<ConversionRebuildResult> RebuildOneAsync(double number, bool allowZeroValue = false)
            => RebuildAsync(0, 99999999, number, allowZeroValue);

        public async Task<ConversionRebuildResult> RebuildAsync(
            long dt1, long dt2, double? onlyNumber = null, bool allowZeroValue = false)
        {
            var result = new ConversionRebuildResult();
            void Log(string m) => result.Log.Add(m);

            var mogodia = (await _db.DoGetDataSQLAsync<double?>(
                "SELECT TOP 1 MOGODIA FROM dbo.SAZMAN")).FirstOrDefault();

            if (mogodia is null)
            {
                result.Success = false;
                result.FirstError = "حساب موجودی جنسی (SAZMAN.MOGODIA) تنظیم نشده؛ بازسازی متوقف شد.";
                Log(result.FirstError);
                return result;
            }

            var sheets = (await _db.DoGetDataSQLAsync<SheetRow>(@"
SELECT  h.NUMBER, h.DATE_N, h.N_S, h.USER_NAME,
        FromCode = i.CODE, FromName = sf.NAME, FromAnbar = i.ANBAR, FromQty = i.MEGHk,
        ToCode = i.N_RASID, ToName = st.NAME, ToAnbar = CAST(i.ANBARF AS INT), ToQty = i.MEGH_MAR,
        i.MABL_K
FROM    dbo.HEAD_LST h
JOIN    dbo.INVO_LST i ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
LEFT    JOIN dbo.STUF_DEF sf ON sf.CODE = i.CODE
LEFT    JOIN dbo.STUF_DEF st ON st.CODE = i.N_RASID
WHERE   h.TAG = 30 AND h.DATE_N BETWEEN @DT1 AND @DT2
  AND   (@OnlyNumber IS NULL OR h.NUMBER = @OnlyNumber)
ORDER BY h.DATE_N, h.NUMBER", new { DT1 = dt1, DT2 = dt2, OnlyNumber = onlyNumber })).ToList();

            Log($"SANADTABDIL: {sheets.Count} برگه تبدیل در بازه.");
            if (sheets.Count == 0) return result;

            // ⚠️ برگه‌ی ناقص سند نمی‌گیرد. اگر کالای مقصد مشخص نباشد، طرفِ
            // بدهکارِ سند وجود ندارد و نوشتنِ فقط طرفِ بستانکار یعنی یک سندِ
            // نامتوازن — بدتر از نداشتنِ سند. CHK-24 خودش این برگه‌ها را
            // گزارش می‌کند.
            var usable = sheets.Where(s =>
                s.NUMBER is not null && s.DATE_N is > 10100 &&
                !string.IsNullOrWhiteSpace(s.FromCode) && s.FromAnbar is not null &&
                !string.IsNullOrWhiteSpace(s.ToCode)   && s.ToAnbar   is not null &&
                (allowZeroValue ? s.MABL_K is not null : s.MABL_K is not null and not 0)).ToList();

            var skipped = sheets.Count - usable.Count;
            if (skipped > 0) Log($"{skipped} برگه ناقص بود و سند نگرفت — نگاه کنید CHK-24.");
            if (usable.Count == 0) return result;

            foreach (var s in usable)
            {
                await EnsureAccountAsync(mogodia.Value, s.FromAnbar!.Value, s.FromCode!, s.FromName);
                await EnsureAccountAsync(mogodia.Value, s.ToAnbar!.Value,   s.ToCode!,   s.ToName);
            }

            // شماره سند فقط برای برگه‌هایی که ندارند
            var needNew = usable.Where(s => s.N_S is null or 0).ToList();
            if (needNew.Count > 0)
            {
                var reserved = await SanadNumbering.ReserveBatchAsync(_db, 10,
                    needNew.Select(s => new SanadHeaderRequest
                    {
                        DATE_S    = s.DATE_N!.Value,
                        SHARH_S   = LeftTrim($"سند تبديل کالا مورخ {PersianDate(s.DATE_N!.Value)}", 255),
                        USER_NAME = s.USER_NAME
                    }).ToList());

                for (int i = 0; i < needNew.Count && i < reserved.Count; i++)
                    needNew[i].N_S = reserved[i];
            }

            foreach (var s in usable)
            {
                if (s.N_S is null or 0) continue;

                var ns    = s.N_S!.Value;
                var mabl  = SqlNum(Math.Round(s.MABL_K!.Value));
                var sharh = $"برگه تبديل شماره {s.NUMBER} مورخ {PersianDate(s.DATE_N!.Value)}";

                var hesBes = $"{mogodia}-{s.FromAnbar}-{s.FromCode}";
                var hesBed = $"{mogodia}-{s.ToAnbar}-{s.ToCode}";

                var bes = $"({SqlNum(ns)},{SqlNum(mogodia)},{s.FromAnbar},{SqlText(s.FromCode)}," +
                          $"N'{SqlText(hesBes)}',N'{SqlText(LeftTrim($"{sharh} — خروج {s.FromQty} {s.FromName}", 255))}'," +
                          $"0,{mabl},{SqlNum(s.NUMBER)},30)";

                var bed = $"({SqlNum(ns)},{SqlNum(mogodia)},{s.ToAnbar},{SqlText(s.ToCode)}," +
                          $"N'{SqlText(hesBed)}',N'{SqlText(LeftTrim($"{sharh} — ورود {s.ToQty} {s.ToName}", 255))}'," +
                          $"{mabl},0,{SqlNum(s.NUMBER)},30)";

                var batch = new StringBuilder();
                batch.Append("SET XACT_ABORT ON; BEGIN TRANSACTION;");
                batch.Append($"UPDATE dbo.HEAD_LST SET N_S={SqlNum(ns)} WHERE NUMBER={SqlNum(s.NUMBER)} AND TAG=30;");
                batch.Append($"UPDATE dbo.DEED_HED SET DATE_S={SqlNum(s.DATE_N)}, GHATEI=0, NO_S=10, OKF=1 WHERE NO_S=10 AND N_S={SqlNum(ns)};");
                // پاک‌کردن پیش از درج، تا اجرای دوباره سند را دو برابر نکند
                batch.Append($"DELETE FROM dbo.DEED_DTL WHERE NUMBER={SqlNum(s.NUMBER)} AND TAG=30;");
                batch.Append("INSERT INTO dbo.DEED_DTL (N_S,HES_K,HES_M,HES_T,HES,SHARH,BED,BES,NUMBER,TAG) VALUES ");
                batch.Append(bes).Append(',').Append(bed).Append(';');
                batch.Append("COMMIT TRANSACTION;");

                try
                {
                    await _db.DoExecuteSQLAsync(batch.ToString(), commandTimeout: CostCloseTuning.BatchTimeoutSeconds);
                    result.SheetCount++;
                    result.LastSanadNumber = (long)ns;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.FirstError ??= $"برگه {s.NUMBER} (سند {ns}): {ex.Message}";
                    Log(result.FirstError);
                }
            }

            Log($"SANADTABDIL: پایان — {result.SheetCount} برگه، موفق={result.Success}.");
            return result;
        }

        /// <summary>
        /// زیرحساب موجودیِ (انبار، کالا) اگر نباشد ساخته می‌شود. بدون این،
        /// درجِ آرتیکل روی کالایی که اولین بار به آن انبار می‌آید خطا
        /// می‌دهد — و کالای مقصدِ یک تبدیل دقیقاً همین حالت است.
        /// </summary>
        private async Task EnsureAccountAsync(double kol, int anbar, string code, string? name)
        {
            const string sql = @"
BEGIN TRY
    IF NOT EXISTS (SELECT 1 FROM dbo.DETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin)
        INSERT INTO dbo.DETA_HES (N_KOL, NUMBER, NAME) VALUES (@Kol, @Moin, @Name);
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
END CATCH;
BEGIN TRY
    IF NOT EXISTS (SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Kol AND NUMBER=@Moin AND TNUMBER=@Taf)
        INSERT INTO dbo.TDETA_HES (N_KOL, NUMBER, TNUMBER, NAME) VALUES (@Kol, @Moin, @Taf, @Name);
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
END CATCH;";

            if (!long.TryParse(code, out var taf)) return;

            try
            {
                await _db.DoExecuteSQLAsync(sql, new
                {
                    Kol  = (long)kol,
                    Moin = (long)anbar,
                    Taf  = taf,
                    Name = LeftTrim(name ?? " ", 250)
                });
            }
            catch { /* درجِ آرتیکل خودش خطا می‌دهد اگر واقعاً لازم بوده */ }
        }
    }
}
