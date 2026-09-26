using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;
using System.Data;

namespace Safir.Server.CostClose.ItemConversion
{
    // ═══════════════════════════════════════════════════════════════════════
    //  تبدیل یک کالا به کالای دیگر — برگه‌ی TAG=30
    //
    //  ── یک سربرگ، یک سطر ──
    //  سربرگ مثل انتقالی (ANBAR مبدأ، ANBARF مقصد) و یک سطر که هر دو سر را
    //  حمل می‌کند: CODE/MEGHk کالای مبدأ، N_RASID/MEGH_MAR کالای مقصد،
    //  و یک MABL_K که ارزشِ هر دو سر است.
    //
    //  ── چرا نه رسید/فاکتور خرید ──
    //  چون فاکتور خرید به گزارش‌های دارایی می‌رود و یک جابه‌جاییِ داخلی
    //  نباید آنجا به شکل خرید بنشیند. تصمیم صاحب پروژه، و درست هم هست.
    //
    //  ── مبلغ از کجا می‌آید ──
    //  از میانگین متحرک کاردکسِ کالای مبدأ. کاربر نرخ را تایپ نمی‌کند.
    //  بعدها هم بازسازی نرخ میانگین (Case 30) همین یک ستون را دوباره
    //  می‌نویسد، پس هر دو سر با هم حرکت می‌کنند — نه اینکه یکی جا بماند.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class ItemConversionService
    {
        private readonly IDatabaseService _db;

        public ItemConversionService(IDatabaseService db) => _db = db;

        /// <summary>نوع برگه‌ی تبدیل در HEAD_LST/INVO_LST.</summary>
        public const int ConversionTag = 30;

        // ───────────────────────── پیش‌نمایش ─────────────────────────

        /// <summary>آنچه قرار است ثبت شود، پیش از ثبت. هیچ چیزی نمی‌نویسد.</summary>
        public async Task<ConversionPreviewDto> PreviewAsync(CreateConversionRequest req)
        {
            var dto = new ConversionPreviewDto
            {
                FromCode  = (req.FromCode ?? "").Trim(),
                FromAnbar = req.FromAnbar,
                FromQty   = req.FromQty,
                ToCode    = (req.ToCode ?? "").Trim(),
                ToAnbar   = req.ToAnbar,
                ToQty     = req.ToQty
            };

            if (dto.FromCode.Length == 0 || dto.ToCode.Length == 0)
            {
                dto.Blocker = "کالای مبدأ و مقصد باید انتخاب شوند.";
                return dto;
            }

            if (dto.FromCode == dto.ToCode && dto.FromAnbar == dto.ToAnbar)
            {
                dto.Blocker = "مبدأ و مقصد یکی هستند — این تبدیل نیست.";
                return dto;
            }

            if (req.FromQty <= 0 || req.ToQty <= 0)
            {
                dto.Blocker = "مقدار خروج و ورود باید بزرگ‌تر از صفر باشند.";
                return dto;
            }

            var from = await GetItemAsync(dto.FromCode);
            var to   = await GetItemAsync(dto.ToCode);

            if (from is null) { dto.Blocker = $"کالای {dto.FromCode} تعریف نشده است."; return dto; }
            if (to   is null) { dto.Blocker = $"کالای {dto.ToCode} تعریف نشده است.";   return dto; }

            dto.FromName = from.NAME ?? "";
            dto.FromUnit = from.UnitName ?? "";
            dto.ToName   = to.NAME ?? "";
            dto.ToUnit   = to.UnitName ?? "";

            dto.FromAnbarName = await GetAnbarNameAsync(dto.FromAnbar) ?? "";
            dto.ToAnbarName   = await GetAnbarNameAsync(dto.ToAnbar) ?? "";

            if (dto.FromAnbarName.Length == 0) { dto.Blocker = $"انبار {dto.FromAnbar} تعریف نشده است."; return dto; }
            if (dto.ToAnbarName.Length   == 0) { dto.Blocker = $"انبار {dto.ToAnbar} تعریف نشده است.";   return dto; }

            dto.OnHand = await GetOnHandAsync(dto.FromCode, dto.FromAnbar, req.DateN);
            dto.Rate   = await GetAverageRateAsync(dto.FromCode, dto.FromAnbar, req.DateN);
            dto.Value  = Math.Round(dto.Rate * req.FromQty);
            dto.ToRate = req.ToQty == 0 ? 0 : dto.Value / req.ToQty;

            // ⚠️ هشدار، نه مانع. موجودیِ کاردکس با موجودیِ فیزیکی همیشه یکی
            // نیست و انبارگردانی هنوز نخورده؛ جلوی کاربر را نمی‌گیریم، فقط
            // می‌گوییم چه می‌بینیم. CHK-01 سرِ جای خودش کارش را می‌کند.
            if (dto.OnHand < req.FromQty)
                dto.Warnings.Add(
                    $"موجودی کاردکس {dto.OnHand:N2} {dto.FromUnit} است و کمتر از {req.FromQty:N2} درخواستی — " +
                    "ثبت این تبدیل کاردکس را منفی می‌کند و CHK-01 جلوی بستن ماه را می‌گیرد.");

            if (dto.Rate <= 0)
                dto.Warnings.Add(
                    "نرخ میانگین این کالا در این انبار صفر است؛ کالای مقصد با ارزش صفر وارد می‌شود. " +
                    "اول «بازسازی نرخ میانگین» را اجرا کنید.");

            if (await IsMonthApprovedAsync(req.DateN))
                dto.Warnings.Add(
                    "ماه این تاریخ قبلاً تأیید شده است. ثبت تبدیل در ماه بسته، گزارش‌های تأییدشده را عوض می‌کند.");

            return dto;
        }

        // ───────────────────────── ثبت ─────────────────────────

        public async Task<ConversionResultDto> CreateAsync(CreateConversionRequest req, string user)
        {
            var preview = await PreviewAsync(req);
            if (!preview.CanSubmit)
                return new ConversionResultDto { Ok = false, Error = preview.Blocker };

            var note = string.IsNullOrWhiteSpace(req.Note)
                ? $"تبدیل {preview.FromName} به {preview.ToName}"
                : req.Note!.Trim();

            try
            {
                return await _db.ExecuteInTransactionAsync(async (cn, tx) =>
                {
                    // بدون UPDLOCK/HOLDLOCK دو کاربر همزمان یک شماره می‌گیرند
                    // — همان الگوی ProformasController.
                    var number = await cn.ExecuteScalarAsync<double?>(
                        "SELECT ISNULL(MAX(NUMBER), 0) + 1 FROM dbo.HEAD_LST WITH (UPDLOCK, HOLDLOCK) WHERE TAG = @Tag",
                        new { Tag = ConversionTag }, tx) ?? 1;

                    var fromVahed = await cn.ExecuteScalarAsync<int?>(
                        "SELECT VAHED FROM dbo.STUF_DEF WHERE CODE = @Code",
                        new { Code = preview.FromCode }, tx) ?? 0;

                    // سربرگ: ANBAR مبدأ، ANBARF مقصد — عیناً قرارداد انتقالی
                    await cn.ExecuteAsync(@"
INSERT dbo.HEAD_LST (NUMBER, TAG, DATE_N, MAS, VAS, ANBAR, ANBARF, MOLAH, USER_NAME,
                     M_NAGHD, MABL_VAR, MABL_HAV, MABL_HAZ, TAKHFIF, CRT)
VALUES (@Number, @Tag, @DateN, 0, 0, @FromAnbar, @ToAnbar, @Note, @User, 0, 0, 0, 0, 0, GETDATE())",
                        new { Number = number, Tag = ConversionTag, DateN = req.DateN,
                              FromAnbar = req.FromAnbar, ToAnbar = req.ToAnbar,
                              Note = Trim(note, 200), User = Trim(user, 50) }, tx);

                    // یک سطر، هر دو سر.
                    //   CODE / MEGHk / VAHED_K → کالای مبدأ
                    //   N_RASID / MEGH_MAR     → کالای مقصد
                    //   MABL_K                 → ارزش، برای هر دو سر یکی
                    await cn.ExecuteAsync(@"
INSERT dbo.INVO_LST (NUMBER, TAG, ANBAR, ANBARF, CODE, MEGH, MEGHk, MEGH_R,
                     N_RASID, MEGH_MAR, MABL, MABL_K, FROM_A, VAHED_K, CRT)
VALUES (@Number, @Tag, @FromAnbar, @ToAnbar, @FromCode, @FromQty, @FromQty, @FromQty,
        @ToCode, @ToQty, @Rate, @Value, 0, @Vahed, GETDATE())",
                        new { Number = number, Tag = ConversionTag,
                              FromAnbar = req.FromAnbar, ToAnbar = req.ToAnbar,
                              FromCode = preview.FromCode, FromQty = req.FromQty,
                              ToCode = preview.ToCode, ToQty = req.ToQty,
                              Rate = preview.Rate, Value = preview.Value, Vahed = fromVahed }, tx);

                    var id = await cn.ExecuteScalarAsync<long>(
                        "SELECT id FROM dbo.INVO_LST WHERE TAG = @Tag AND NUMBER = @Number",
                        new { Tag = ConversionTag, Number = number }, tx);

                    return new ConversionResultDto
                    {
                        Ok           = true,
                        ConversionId = id,
                        Number       = number,
                        Value        = preview.Value
                    };
                }, IsolationLevel.ReadCommitted);
            }
            catch (Exception ex)
            {
                return new ConversionResultDto { Ok = false, Error = ex.Message };
            }
        }

        // ───────────────────────── حذف ─────────────────────────

        /// <summary>
        /// برگه را پاک می‌کند.
        ///
        /// ⚠️ اگر سند حسابداری خورده باشد انجام نمی‌شود — پاک کردن برگه‌ای
        /// که سند دارد، سندِ یتیم می‌سازد و CHK-23 آن را به‌عنوان
        /// «حسابداری دارد، کاردکس ندارد» گزارش می‌کند. اول سند برگردانده
        /// شود.
        /// </summary>
        public async Task<(bool Ok, string? Error)> DeleteAsync(double number)
        {
            var sanad = (await _db.DoGetDataSQLAsync<double?>(
                "SELECT N_S FROM dbo.HEAD_LST WHERE TAG = @Tag AND NUMBER = @Number",
                new { Tag = ConversionTag, Number = number })).FirstOrDefault();

            if (sanad is > 0)
                return (false, $"سند حسابداری {sanad:0} برای این برگه صادر شده؛ اول سند را برگردانید.");

            try
            {
                await _db.ExecuteInTransactionAsync(async (cn, tx) =>
                {
                    await cn.ExecuteAsync(
                        "DELETE FROM dbo.INVO_LST WHERE TAG = @Tag AND NUMBER = @Number",
                        new { Tag = ConversionTag, Number = number }, tx);

                    await cn.ExecuteAsync(
                        "DELETE FROM dbo.HEAD_LST WHERE TAG = @Tag AND NUMBER = @Number",
                        new { Tag = ConversionTag, Number = number }, tx);
                }, IsolationLevel.ReadCommitted);

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ───────────────────────── فهرست ─────────────────────────

        public async Task<List<ConversionRowDto>> ListAsync(long? dt1, long? dt2)
        {
            const string sql = @"
SELECT  *
FROM    dbo.CC_vw_ItemConversion
WHERE   (@DT1 IS NULL OR DateN >= @DT1)
  AND   (@DT2 IS NULL OR DateN <= @DT2)
ORDER BY DateN DESC, Number DESC";

            return (await _db.DoGetDataSQLAsync<ConversionRowDto>(sql, new { DT1 = dt1, DT2 = dt2 })).ToList();
        }

        // ───────────────────────── کمکی‌ها ─────────────────────────

        private sealed class ItemRow
        {
            public string? NAME     { get; set; }
            public string? UnitName { get; set; }
        }

        private async Task<ItemRow?> GetItemAsync(string code)
            => (await _db.DoGetDataSQLAsync<ItemRow>(@"
SELECT  s.NAME, v.NAMES AS UnitName
FROM    dbo.STUF_DEF s
LEFT    JOIN dbo.TCOD_VAHEDS v ON v.CODE = s.VAHED
WHERE   s.CODE = @Code", new { Code = code })).FirstOrDefault();

        private async Task<string?> GetAnbarNameAsync(int anbar)
            => (await _db.DoGetDataSQLAsync<string>(
                "SELECT NAMES FROM dbo.TCOD_ANBAR WHERE CODE = @Anbar", new { Anbar = anbar }))
               .FirstOrDefault();

        /// <summary>
        /// موجودی کاردکس تا و شاملِ همان تاریخ. ورود و خروجِ هر نوع برگه با
        /// همان تفکیکی که موتور نرخ میانگین دارد — از جمله دو سرِ تبدیل، که
        /// روی *یک* سطر نشسته‌اند و باید دو بار شمرده شوند: یک‌بار خروج از
        /// ANBAR و یک‌بار ورود به ANBARF با کد مقصد.
        /// </summary>
        private async Task<double> GetOnHandAsync(string code, int anbar, long dateN)
            => (await _db.DoGetDataSQLAsync<double?>(@"
SELECT  ISNULL(SUM(q), 0)
FROM    (
    SELECT  q = CASE
                  WHEN i.TAG IN (1, 6, 9, 17, 22) THEN  i.MEGHk
                  WHEN i.TAG IN (4, 24)           THEN  ISNULL(NULLIF(i.MEGH_MAR, 0), i.MEGHk)
                  WHEN i.TAG IN (2, 5, 10, 11, 18, 26, 30) THEN -i.MEGHk
                  WHEN i.TAG = 3                  THEN -ISNULL(NULLIF(i.MEGH_MAR, 0), i.MEGHk)
                  ELSE 0 END
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE   i.CODE = @Code AND i.ANBAR = @Anbar AND h.DATE_N <= @DateN

    UNION ALL

    -- سمت ورودِ انتقالی: همان ردیف TAG=5، ولی در انبار مقصد
    SELECT  q = i.MEGHk
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE   i.CODE = @Code AND i.TAG = 5 AND i.ANBARF = @Anbar AND h.DATE_N <= @DateN

    UNION ALL

    -- سمت ورودِ تبدیل: کد مقصد در N_RASID و مقدارش در MEGH_MAR
    SELECT  q = i.MEGH_MAR
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE   i.TAG = 30 AND i.N_RASID = @Code
      AND   CAST(i.ANBARF AS INT) = @Anbar AND h.DATE_N <= @DateN
) x", new { Code = code, Anbar = anbar, DateN = dateN })).FirstOrDefault() ?? 0;

        /// <summary>
        /// میانگین متحرک در لحظه‌ی تبدیل: AVRAGE آخرین ردیفِ کاردکسِ همان
        /// (کالا، انبار) تا آن تاریخ — و AVRAGE2 وقتی کالا از سمتِ ورودِ یک
        /// انتقالی یا تبدیل آمده باشد.
        ///
        /// ⚠️ AVRAGE را بازسازی نرخ میانگین می‌نویسد. اگر برای ماه جاری هنوز
        /// اجرا نشده باشد، آخرین AVRAGEِ معتبر مالِ قبل‌تر است و همان بهترین
        /// چیزی است که داریم — عددِ نهایی را S07A بعداً روی همین یک ستون
        /// می‌نویسد.
        /// </summary>
        private async Task<double> GetAverageRateAsync(string code, int anbar, long dateN)
            => (await _db.DoGetDataSQLAsync<double?>(@"
SELECT  TOP 1 r.AVRAGE
FROM    (
    SELECT  i.AVRAGE, h.DATE_N, t.tartib, i.NUMBER, i.id
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    LEFT    JOIN dbo.TAGCOD t ON t.CODE = i.TAG
    WHERE   i.CODE = @Code AND i.ANBAR = @Anbar
      AND   h.DATE_N <= @DateN AND i.AVRAGE IS NOT NULL AND i.AVRAGE <> 0

    UNION ALL

    SELECT  i.AVRAGE2, h.DATE_N, t.tartib, i.NUMBER, i.id
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    LEFT    JOIN dbo.TAGCOD t ON t.CODE = i.TAG + 1
    WHERE   i.TAG = 5 AND i.CODE = @Code AND i.ANBARF = @Anbar
      AND   h.DATE_N <= @DateN AND i.AVRAGE2 IS NOT NULL AND i.AVRAGE2 <> 0

    UNION ALL

    SELECT  i.AVRAGE2, h.DATE_N, t.tartib, i.NUMBER, i.id
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    LEFT    JOIN dbo.TAGCOD t ON t.CODE = 31
    WHERE   i.TAG = 30 AND i.N_RASID = @Code AND CAST(i.ANBARF AS INT) = @Anbar
      AND   h.DATE_N <= @DateN AND i.AVRAGE2 IS NOT NULL AND i.AVRAGE2 <> 0
) r
ORDER BY r.DATE_N DESC, r.tartib DESC, r.NUMBER DESC, r.id DESC",
                new { Code = code, Anbar = anbar, DateN = dateN })).FirstOrDefault() ?? 0;

        /// <summary>
        /// ماه تأییدشده = اجرایی که ApprovedAtUtc دارد (همان چیزی که
        /// CC_sp_S14_Approve می‌گذارد). Status به‌تنهایی کافی نیست: ۳ فقط
        /// یعنی اجرا تمام شد، نه اینکه کسی تأییدش کرده.
        /// </summary>
        private async Task<bool> IsMonthApprovedAsync(long dateN)
            => (await _db.DoGetDataSQLAsync<int>(@"
SELECT  COUNT(*)
FROM    dbo.CC_Run
WHERE   ApprovedAtUtc IS NOT NULL
  AND   @DateN BETWEEN DateFrom AND DateTo",
                new { DateN = dateN })).FirstOrDefault() > 0;

        private static string Trim(string? s, int max)
        {
            s ??= "";
            return s.Length <= max ? s : s[..max];
        }
    }
}
