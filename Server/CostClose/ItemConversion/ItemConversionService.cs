using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;
using System.Data;

namespace Safir.Server.CostClose.ItemConversion
{
    // ═══════════════════════════════════════════════════════════════════════
    //  تبدیل یک کالا به کالای دیگر
    //
    //  ── چه می‌سازد ──
    //  سه برگه، در یک تراکنش، عیناً همان سه برگه‌ای که تا امروز دستی صادر
    //  می‌شد:
    //    TAG=11  حواله خروج سایر   از انبار مبدأ، به بدهکار حساب واسط
    //    TAG=1   رسید خرید          به انبار مقصد
    //    TAG=12  فاکتور خرید        سربرگِ حسابداریِ همان رسید
    //
    //  هیچ نوع برگه‌ی تازه‌ای اختراع نشده و هیچ TAG جدیدی ساخته نشده. دلیلش
    //  ساده است: کل زنجیره‌ی بهای تمام‌شده، گزارش‌های انبار، و نرم‌افزار
    //  قدیمی همه با همین سه نوع کار می‌کنند. یک TAG تازه یعنی دست‌زدن به
    //  همه‌ی آنها.
    //
    //  ── مبلغ از کجا می‌آید ──
    //  از میانگین متحرک کاردکسِ کالای مبدأ در همان انبار. کاربر نرخ را
    //  تایپ نمی‌کند — و این عمدی است، چون تایپِ نرخ دقیقاً همان کاری است
    //  که امروز مانده روی حساب واسط می‌گذارد (توضیح کامل در
    //  41-item-conversion.sql).
    //
    //  ── و اگر نرخ بعداً عوض شد ──
    //  می‌شود؛ حتماً می‌شود. بازسازی نرخ میانگین ردیف TAG=11 را دوباره
    //  قیمت می‌زند. برای همین گام S11B بعد از همگرایی، مبلغ رسید را از
    //  روی مبلغِ نهاییِ حواله بازنویسی می‌کند. این سرویس فقط شروع کار
    //  است، نه تضمینِ آن.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class ItemConversionService
    {
        private readonly IDatabaseService _db;

        public ItemConversionService(IDatabaseService db) => _db = db;

        /// <summary>
        /// زیر این مبلغ، اختلاف را «گرد کردن» می‌شماریم نه مغایرت. همان
        /// آستانه‌ای که در CC_sp_ResyncConversions هست.
        /// </summary>
        public const double BalanceEpsilon = 0.5;

        // ───────────────────────── پیش‌نمایش ─────────────────────────

        /// <summary>
        /// آنچه قرار است ثبت شود، پیش از ثبت. هیچ چیزی نمی‌نویسد.
        /// </summary>
        public async Task<ConversionPreviewDto> PreviewAsync(CreateConversionRequest req)
        {
            var dto = new ConversionPreviewDto
            {
                FromCode    = (req.FromCode ?? "").Trim(),
                FromAnbar   = req.FromAnbar,
                FromQty     = req.FromQty,
                ToCode      = (req.ToCode ?? "").Trim(),
                ToAnbar     = req.ToAnbar,
                ToQty       = req.ToQty,
                AccountCode = (req.AccountCode ?? "").Trim()
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

            var account = await GetAccountNameAsync(dto.AccountCode);
            if (account is null)
            {
                dto.Blocker = $"حساب «{dto.AccountCode}» در سرفصل‌ها پیدا نشد.";
                return dto;
            }
            dto.AccountName = account;

            dto.OnHand = await GetOnHandAsync(dto.FromCode, dto.FromAnbar, req.DateN);
            dto.Rate   = await GetAverageRateAsync(dto.FromCode, dto.FromAnbar, req.DateN);
            dto.Value  = Math.Round(dto.Rate * req.FromQty);
            dto.ToRate = req.ToQty == 0 ? 0 : dto.Value / req.ToQty;

            // ⚠️ هشدار، نه مانع. موجودیِ محاسبه‌شده از کاردکس با موجودیِ
            // فیزیکی همیشه یکی نیست و انبارگردانی هنوز نخورده است؛ جلوی
            // کاربر را نمی‌گیریم، فقط می‌گوییم چه می‌بینیم.
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

        /// <summary>
        /// سه برگه را در یک تراکنش می‌سازد. یا هر سه، یا هیچ‌کدام — یک
        /// حواله‌ی بی‌رسید، موجودی را می‌برد و هیچ‌جا برنمی‌گرداند.
        /// </summary>
        public async Task<ConversionResultDto> CreateAsync(CreateConversionRequest req, string user)
        {
            var preview = await PreviewAsync(req);
            if (!preview.CanSubmit)
                return new ConversionResultDto { Ok = false, Error = preview.Blocker };

            var fromCode = preview.FromCode;
            var toCode   = preview.ToCode;
            var note     = string.IsNullOrWhiteSpace(req.Note)
                ? $"تبدیل {preview.FromName} به {preview.ToName}"
                : req.Note!.Trim();

            try
            {
                return await _db.ExecuteInTransactionAsync(async (cn, tx) =>
                {
                    // شماره‌ی هر نوع برگه جداگانه و زیر قفل گرفته می‌شود —
                    // همان الگوی ProformasController. بدون UPDLOCK/HOLDLOCK دو
                    // کاربر همزمان یک شماره می‌گیرند و کلید تکراری می‌خورد.
                    var issueNo   = await NextNumberAsync(cn, tx, 11);
                    var receiptNo = await NextNumberAsync(cn, tx, 1);
                    var invoiceNo = receiptNo;   // فاکتور و رسید هم‌شماره‌اند

                    var fromVahed = await cn.ExecuteScalarAsync<int?>(
                        "SELECT VAHED FROM dbo.STUF_DEF WHERE CODE = @Code",
                        new { Code = fromCode }, tx) ?? 0;
                    var toVahed = await cn.ExecuteScalarAsync<int?>(
                        "SELECT VAHED FROM dbo.STUF_DEF WHERE CODE = @Code",
                        new { Code = toCode }, tx) ?? 0;

                    // ── سمت خروج: حواله خروج سایر ──
                    await cn.ExecuteAsync(@"
INSERT dbo.HEAD_LST (NUMBER, TAG, DATE_N, MAS, VAS, CUST_NO, MOLAH, USER_NAME,
                     M_NAGHD, MABL_VAR, MABL_HAV, MABL_HAZ, TAKHFIF, CRT)
VALUES (@Number, 11, @DateN, 0, 0, @Account, @Note, @User, 0, 0, 0, 0, 0, GETDATE())",
                        new { Number = issueNo, DateN = req.DateN, Account = preview.AccountCode,
                              Note = Trim(note, 200), User = Trim(user, 50) }, tx);

                    // N_RASID همان کد حسابِ واسط است. عمداً *متنِ* کامل
                    // «کل-معین-تفصیل» و نه عدد: OtherIssueRebuildService
                    // مقدار عددی را شماره‌ی فرمول ساخت می‌فهمد و سند را به
                    // حساب فرمول می‌زند، نه به حساب ما.
                    await cn.ExecuteAsync(@"
INSERT dbo.INVO_LST (NUMBER, TAG, ANBAR, CODE, MEGH, MEGHk, MEGH_MAR, MEGH_R,
                     MABL, MABL_K, FROM_A, N_RASID, VAHED_K, CRT)
VALUES (@Number, 11, @Anbar, @Code, @Qty, @Qty, 0, @Qty,
        @Rate, @Value, 0, @Account, @Vahed, GETDATE())",
                        new { Number = issueNo, Anbar = req.FromAnbar, Code = fromCode,
                              Qty = req.FromQty, Rate = preview.Rate, Value = preview.Value,
                              Account = preview.AccountCode, Vahed = fromVahed }, tx);

                    // ── سمت ورود: رسید خرید ──
                    await cn.ExecuteAsync(@"
INSERT dbo.HEAD_LST (NUMBER, TAG, DATE_N, MAS, VAS, ANBARF, CUST_NO, MOLAH, USER_NAME,
                     M_NAGHD, MABL_VAR, MABL_HAV, MABL_HAZ, TAKHFIF, CRT)
VALUES (@Number, 1, @DateN, 0, 0, 0, @Account, @Note, @User, 0, 0, 0, 0, 0, GETDATE())",
                        new { Number = receiptNo, DateN = req.DateN, Account = preview.AccountCode,
                              Note = Trim(note, 200), User = Trim(user, 50) }, tx);

                    await cn.ExecuteAsync(@"
INSERT dbo.INVO_LST (NUMBER, TAG, ANBAR, CODE, MEGH, MEGHk, MEGH_MAR, MEGH_R,
                     MABL, MABL_K, FROM_A, VAHED_K, CRT)
VALUES (@Number, 1, @Anbar, @Code, @Qty, @Qty, 0, @Qty,
        @Rate, @Value, 0, @Vahed, GETDATE())",
                        new { Number = receiptNo, Anbar = req.ToAnbar, Code = toCode,
                              Qty = req.ToQty, Rate = preview.ToRate, Value = preview.Value,
                              Vahed = toVahed }, tx);

                    // ── فاکتور خرید ──
                    // سربرگ بدونِ ردیف، عیناً مثل خریدهای عادی: سند حسابداری
                    // روی فاکتور می‌نشیند و مبلغش از ردیف‌های رسیدِ هم‌شماره
                    // خوانده می‌شود. بدون این، CHK-23 برگه را «بی‌فاکتور»
                    // گزارش می‌کند.
                    await cn.ExecuteAsync(@"
INSERT dbo.HEAD_LST (NUMBER, TAG, DATE_N, MAS, VAS, ANBARF, CUST_NO, MOLAH, USER_NAME,
                     M_NAGHD, MABL_VAR, MABL_HAV, MABL_HAZ, TAKHFIF, CRT)
VALUES (@Number, 12, @DateN, 0, 0, 0, @Account, @Note, @User, 0, 0, 0, 0, 0, GETDATE())",
                        new { Number = invoiceNo, DateN = req.DateN, Account = preview.AccountCode,
                              Note = Trim(note, 200), User = Trim(user, 50) }, tx);

                    var id = await cn.ExecuteScalarAsync<int>(@"
INSERT dbo.CC_ItemConversion
    (DateN, AccountCode, FromCode, FromAnbar, FromQty, ToCode, ToAnbar, ToQty,
     IssueNumber, ReceiptNumber, InvoiceNumber, RateAtEntry, ValueAtEntry, Note, CreatedBy)
OUTPUT INSERTED.ConversionId
VALUES (@DateN, @Account, @FromCode, @FromAnbar, @FromQty, @ToCode, @ToAnbar, @ToQty,
        @IssueNo, @ReceiptNo, @InvoiceNo, @Rate, @Value, @Note, @User)",
                        new { DateN = req.DateN, Account = preview.AccountCode,
                              FromCode = fromCode, FromAnbar = req.FromAnbar, FromQty = req.FromQty,
                              ToCode = toCode, ToAnbar = req.ToAnbar, ToQty = req.ToQty,
                              IssueNo = issueNo, ReceiptNo = receiptNo, InvoiceNo = invoiceNo,
                              Rate = preview.Rate, Value = preview.Value,
                              Note = Trim(note, 200), User = Trim(user, 100) }, tx);

                    return new ConversionResultDto
                    {
                        Ok            = true,
                        ConversionId  = id,
                        IssueNumber   = issueNo,
                        ReceiptNumber = receiptNo,
                        InvoiceNumber = invoiceNo,
                        Value         = preview.Value
                    };
                }, IsolationLevel.ReadCommitted);
            }
            catch (Exception ex)
            {
                return new ConversionResultDto { Ok = false, Error = ex.Message };
            }
        }

        // ───────────────────────── ابطال ─────────────────────────

        /// <summary>
        /// هر سه برگه را پاک می‌کند و تبدیل را «ابطال‌شده» علامت می‌زند.
        ///
        /// ⚠️ اگر سند حسابداری برای هر کدام صادر شده باشد، ابطال انجام
        /// نمی‌شود — پاک کردن برگه‌ای که سند دارد، سندِ یتیم می‌سازد و
        /// CHK-23 آن را به‌عنوان «حسابداری دارد، کاردکس ندارد» گزارش
        /// می‌کند. در آن حالت باید اول سند برگردانده شود.
        /// </summary>
        public async Task<(bool Ok, string? Error)> VoidAsync(int conversionId, string user)
        {
            var row = (await _db.DoGetDataSQLAsync<ConversionRowDto>(
                "SELECT * FROM dbo.CC_vw_ItemConversion WHERE ConversionId = @Id",
                new { Id = conversionId })).FirstOrDefault();

            if (row is null)   return (false, "این تبدیل پیدا نشد.");
            if (row.IsVoided)  return (false, "این تبدیل قبلاً ابطال شده است.");

            var withSanad = await _db.DoGetDataSQLAsync<string>(@"
SELECT  CONCAT(tc.BARGAH, N' ', CAST(h.NUMBER AS BIGINT), N' — سند ', CAST(h.N_S AS BIGINT))
FROM    dbo.HEAD_LST h
LEFT    JOIN dbo.TAGCOD tc ON tc.CODE = h.TAG
WHERE   ((h.TAG = 11 AND h.NUMBER = @Issue)
      OR (h.TAG IN (1, 12) AND h.NUMBER = @Receipt))
  AND   h.N_S IS NOT NULL AND h.N_S <> 0",
                new { Issue = row.IssueNumber, Receipt = row.ReceiptNumber });

            var blocked = withSanad.ToList();
            if (blocked.Count > 0)
                return (false,
                    "برای این برگه‌ها سند حسابداری صادر شده و بدون برگرداندن سند نمی‌شود پاکشان کرد: "
                    + string.Join("، ", blocked));

            try
            {
                await _db.ExecuteInTransactionAsync(async (cn, tx) =>
                {
                    await cn.ExecuteAsync(
                        "DELETE FROM dbo.INVO_LST WHERE (TAG = 11 AND NUMBER = @Issue) OR (TAG = 1 AND NUMBER = @Receipt)",
                        new { Issue = row.IssueNumber, Receipt = row.ReceiptNumber }, tx);

                    await cn.ExecuteAsync(
                        "DELETE FROM dbo.HEAD_LST WHERE (TAG = 11 AND NUMBER = @Issue) OR (TAG IN (1, 12) AND NUMBER = @Receipt)",
                        new { Issue = row.IssueNumber, Receipt = row.ReceiptNumber }, tx);

                    await cn.ExecuteAsync(@"
UPDATE dbo.CC_ItemConversion
SET    Status = 9,
       Note   = LEFT(ISNULL(Note, N'') + N' — ابطال توسط ' + @User, 200)
WHERE  ConversionId = @Id",
                        new { Id = conversionId, User = user }, tx);
                }, IsolationLevel.ReadCommitted);

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ───────────────────────── فهرست ─────────────────────────

        public async Task<List<ConversionRowDto>> ListAsync(long? dt1, long? dt2, bool includeVoided)
        {
            var sql = @"
SELECT  *
FROM    dbo.CC_vw_ItemConversion
WHERE   (@DT1 IS NULL OR DateN >= @DT1)
  AND   (@DT2 IS NULL OR DateN <= @DT2)
  AND   (@All = 1 OR Status = 1)
ORDER BY DateN DESC, ConversionId DESC";

            return (await _db.DoGetDataSQLAsync<ConversionRowDto>(
                sql, new { DT1 = dt1, DT2 = dt2, All = includeVoided ? 1 : 0 })).ToList();
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
        /// حساب به شکل «کل-معین-تفصیل». اگر تفصیل نداشته باشد دو جزء هم
        /// پذیرفته می‌شود — سرفصل‌های بعضی شرکت‌ها همین‌اند.
        /// </summary>
        private async Task<string?> GetAccountNameAsync(string code)
        {
            var parts = (code ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            if (!int.TryParse(parts[0], out var kol) || !int.TryParse(parts[1], out var moin))
                return null;

            int? taf = parts.Length > 2 && int.TryParse(parts[2], out var t) ? t : null;

            return (await _db.DoGetDataSQLAsync<string>(@"
SELECT  NAME FROM dbo.TDETA_HES
WHERE   N_KOL = @Kol AND NUMBER = @Moin
  AND   (@Taf IS NULL OR TNUMBER = @Taf)",
                new { Kol = kol, Moin = moin, Taf = taf })).FirstOrDefault();
        }

        /// <summary>
        /// موجودی کاردکس تا و شاملِ همان تاریخ. مجموع ورودها منهای خروج‌ها،
        /// با همان تفکیکی که بازسازی نرخ میانگین دارد: انتقالی (TAG=5) در
        /// انبار مبدأ خروج است و در ANBARF ورود.
        /// </summary>
        private async Task<double> GetOnHandAsync(string code, int anbar, long dateN)
            => (await _db.DoGetDataSQLAsync<double?>(@"
SELECT  ISNULL(SUM(q), 0)
FROM    (
    SELECT  q = CASE
                  WHEN i.TAG IN (1, 6, 9, 17, 22) THEN  i.MEGHk
                  WHEN i.TAG IN (4, 24)           THEN  ISNULL(NULLIF(i.MEGH_MAR, 0), i.MEGHk)
                  WHEN i.TAG IN (2, 5, 10, 11, 18, 26) THEN -i.MEGHk
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
) x", new { Code = code, Anbar = anbar, DateN = dateN })).FirstOrDefault() ?? 0;

        /// <summary>
        /// میانگین متحرک در لحظه‌ی تبدیل: AVRAGE آخرین ردیفِ کاردکسِ همان
        /// (کالا، انبار) تا آن تاریخ.
        ///
        /// ⚠️ AVRAGE را بازسازی نرخ میانگین می‌نویسد. اگر برای ماه جاری
        /// هنوز اجرا نشده باشد، آخرین AVRAGEِ معتبر مالِ قبل‌تر است و
        /// همان بهترین چیزی است که داریم — عددِ نهایی را S11B بعداً
        /// می‌نویسد، پس این فقط نقطه‌ی شروع است.
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
    LEFT    JOIN dbo.TAGCOD t ON t.CODE = i.TAG
    WHERE   i.CODE = @Code AND i.TAG = 5 AND i.ANBARF = @Anbar
      AND   h.DATE_N <= @DateN AND i.AVRAGE2 IS NOT NULL AND i.AVRAGE2 <> 0
) r
ORDER BY r.DATE_N DESC, r.tartib DESC, r.NUMBER DESC, r.id DESC",
                new { Code = code, Anbar = anbar, DateN = dateN })).FirstOrDefault() ?? 0;

        /// <summary>
        /// ماه تأییدشده = اجرای قطعی‌ای که ApprovedAtUtc دارد (همان چیزی که
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

        private static async Task<double> NextNumberAsync(IDbConnection cn, IDbTransaction tx, int tag)
            => await cn.ExecuteScalarAsync<double?>(
                   "SELECT ISNULL(MAX(NUMBER), 0) + 1 FROM dbo.HEAD_LST WITH (UPDLOCK, HOLDLOCK) WHERE TAG = @Tag",
                   new { Tag = tag }, tx) ?? 1;

        private static string Trim(string? s, int max)
        {
            s ??= "";
            return s.Length <= max ? s : s[..max];
        }
    }
}
