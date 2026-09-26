/* ═══════════════════════════════════════════════════════════════════
   تبدیل کالا به کالا — برگه‌ی TAG=30

   ── چه کاری است ──
   مقداری از یک کالا از انباری خارج می‌شود و کالای *دیگری* به انبار
   دیگری وارد می‌شود: خامه ۲۸٪ می‌رود و خامه ۶۵٪ می‌آید. چیزی تولید
   نشده و چیزی خریده نشده — همان ارزش، زیر نام دیگری، جای دیگری.

   ── چرا برگه‌ی تازه و نه رسید/فاکتور خرید ──
   تا امروز این کار با یک حواله خروج سایر و یک رسید خرید انجام می‌شد.
   کار می‌کرد ولی نباید می‌کرد: فاکتور خرید فقط یک سند حسابداری نیست،
   به گزارش‌های دارایی هم می‌رود. یک جابه‌جاییِ داخلی نباید به شکل خریدِ
   ساختگی در اظهارنامه بنشیند. پس برگه‌ی خودش را دارد.

   ── ساختار: یک سربرگ، یک سطر ──
   سربرگ مثل انتقالی است — ANBAR مبدأ، ANBARF مقصد — و *یک* سطر هر دو
   سر را نگه می‌دارد:

     CODE      کالای مبدأ        ANBAR     انبار مبدأ
     MEGHk     مقدار خروج        VAHED_K   واحد کالای مبدأ
     N_RASID   کالای مقصد        ANBARF    انبار مقصد
     MEGH_MAR  مقدار ورود
     MABL_K    ارزش — یکی، برای هر دو سر
     AVRAGE    میانگین مبدأ پس از خروج
     AVRAGE2   میانگین مقصد پس از ورود

   دقیقاً همان قراردادی که انتقالی (TAG=5) دارد؛ فقط کد کالا هم عوض
   می‌شود، پس دو ستونِ بی‌استفاده روی همین سطر آن را حمل می‌کنند.

   ⚠️ MEGH_MAR اینجا «مقدار مرجوعی» نیست. در ویوهای قدیمیِ موجودی
   (MOG_FR_SUB و بستگانش) فرمول SUM(MEGHk - MEGH_MAR) فقط روی
   TAG IN (2,8,10,11,26) اجرا می‌شود و TAG=30 اصلاً داخل آن فهرست
   نیست — پس این ستون روی این برگه آزاد است. هر شاخه‌ای که بعداً برای
   TAG=30 به آن ویوها اضافه شود باید *خودش* این را بداند و فقط MEGHk
   را کم کند.

   ── چرا یک MABL_K و نه دو تا ──
   چون آن‌وقت نمی‌توانند با هم اختلاف پیدا کنند. روشِ قدیمی دو مبلغ
   جدا داشت و بازسازی نرخ میانگین فقط یکی‌شان را به‌روز می‌کرد؛ نتیجه
   روی پایگاه پودر مروارید ۴٬۷۶۰٬۸۷۲ ریال مانده روی حساب واسط بود، از
   یک تبدیل. با یک ستون، تراز یک خاصیتِ ساختار است نه چیزی که باید
   نگهبانی شود.

   ── حسابداری ──
   مستقیم انبار به انبار، عیناً مثل انتقالی: موجودی انبار مبدأ
   بستانکار، موجودی انبار مقصد بدهکار، هر دو به همان MABL_K. هیچ حساب
   واسطی درگیر نمی‌شود، پس هیچ مانده‌ای هم نمی‌تواند رویش بماند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────────── ثبت نوع برگه در TAGCOD ─────────────────────

   ۳۰ خروج است و ۳۱ ورود. سطر ۳۱ هیچ‌وقت در INVO_LST نوشته نمی‌شود —
   موتور نرخ میانگین آن را از روی همان سطر ۳۰ می‌سازد، عیناً همان‌طور
   که انتقالیِ ورود (۶) را از انتقالیِ خروج (۵) می‌سازد. ولی باید در
   TAGCOD باشد، چون tartib آن ترتیبِ پردازشِ سمتِ ورود را در همان روز
   تعیین می‌کند.

   tartib از روی همسایه‌های منطقی‌اش انتخاب شده: ورود (۳۱) با انتقالیِ
   ورود هم‌رتبه است و خروج (۳۰) با انتقالیِ خروج — یعنی در یک روز، اول
   ورودها و بعد خروج‌ها، همان قاعده‌ای که کارت کالای واقعی دارد.

   BARGAH عمداً با همان فاصله‌های ابتدایی نوشته می‌شود که بقیه‌ی ردیف‌ها
   دارند؛ سیستم قدیمی ترتیب را از همین رشته درمی‌آورد. */

MERGE dbo.TAGCOD AS t
USING (VALUES
    (30, N'تبديل - خروج'),
    (31, N' تبديل - ورود')
) AS s (CODE, BARGAH)
ON t.CODE = s.CODE
WHEN NOT MATCHED THEN INSERT (CODE, BARGAH) VALUES (s.CODE, s.BARGAH);
GO

/* tartib فقط وقتی نوشته می‌شود که خالی باشد — همان محافظه‌کاریِ
   36-tagcod-tartib-seed.sql: ترتیبِ برگه‌ها تصمیمِ هر شرکت است. */
IF COL_LENGTH('dbo.TAGCOD', 'tartib') IS NOT NULL
BEGIN
    UPDATE dbo.TAGCOD SET tartib = 10 WHERE CODE = 31 AND tartib IS NULL;
    UPDATE dbo.TAGCOD SET tartib = 14 WHERE CODE = 30 AND tartib IS NULL;
END
GO

/* ───────────────────── نمای برگه‌های تبدیل ───────────────────── */

IF OBJECT_ID('dbo.CC_vw_ItemConversion','V') IS NOT NULL
    DROP VIEW dbo.CC_vw_ItemConversion;
GO

CREATE VIEW dbo.CC_vw_ItemConversion
AS
SELECT  ConversionId  = i.id,
        Number        = h.NUMBER,
        DateN         = h.DATE_N,
        SanadNo       = h.N_S,

        FromCode      = i.CODE,
        FromName      = sf.NAME,
        FromUnit      = vf.NAMES,
        FromAnbar     = i.ANBAR,
        FromAnbarName = af.NAMES,
        FromQty       = i.MEGHk,
        FromRate      = i.MABL,

        ToCode        = i.N_RASID,
        ToName        = st.NAME,
        ToUnit        = vt.NAMES,
        ToAnbar       = CAST(i.ANBARF AS INT),
        ToAnbarName   = at.NAMES,
        ToQty         = i.MEGH_MAR,

        /* ارزش یکی است و همان است که هر دو سر می‌گیرند. نرخ مقصد از
           تقسیمِ همین بر مقدارِ ورود درمی‌آید — تایپ نمی‌شود. */
        Value         = i.MABL_K,
        ToRate        = CASE WHEN ISNULL(i.MEGH_MAR, 0) = 0 THEN 0
                             ELSE i.MABL_K / i.MEGH_MAR END,

        FromAverage   = i.AVRAGE,
        ToAverage     = i.AVRAGE2,

        Note          = h.MOLAH,
        CreatedBy     = h.USER_NAME,
        CreatedAt     = h.CRT
FROM    dbo.INVO_LST i
INNER   JOIN dbo.HEAD_LST   h  ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
LEFT    JOIN dbo.STUF_DEF   sf ON sf.CODE = i.CODE
LEFT    JOIN dbo.STUF_DEF   st ON st.CODE = i.N_RASID
LEFT    JOIN dbo.TCOD_VAHEDS vf ON vf.CODE = sf.VAHED
LEFT    JOIN dbo.TCOD_VAHEDS vt ON vt.CODE = st.VAHED
LEFT    JOIN dbo.TCOD_ANBAR af ON af.CODE = i.ANBAR
LEFT    JOIN dbo.TCOD_ANBAR at ON at.CODE = CAST(i.ANBARF AS INT)
WHERE   i.TAG = 30;
GO

/* ─────────────────────── CHK-24 : برگه‌ی ناقص ───────────────────────

   با یک سطر، دو سرِ تبدیل نمی‌توانند نامتوازن شوند — ولی می‌توانند
   *ناقص* باشند: کد کالای مقصد خالی، مقدار ورود صفر، یا کالایی که در
   STUF_DEF نیست. هر سه یعنی کالا از انبار رفته و هیچ‌جا وارد نشده، و
   هر سه بی‌صدا هستند اگر کسی نگاهشان نکند.

   این رویه از S05 صدا زده می‌شود (همان‌جا که بقیه‌ی کنترل‌ها هستند). */

IF OBJECT_ID('dbo.CC_sp_CheckConversions','P') IS NOT NULL
    DROP PROCEDURE dbo.CC_sp_CheckConversions;
GO

CREATE PROCEDURE dbo.CC_sp_CheckConversions
    @RunId INT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code,
         DocNumber, DocTag, DocDate, Amount, Description)
    SELECT  @RunId, 'S05', 'CHK-24', 26, 2,
            v.FromAnbar, TRY_CAST(v.FromCode AS BIGINT),
            CAST(v.Number AS INT), 30, v.DateN,
            v.Value,
            CONCAT(N'برگه تبدیل ', CAST(v.Number AS BIGINT),
                   N' مورخ ', FORMAT(v.DateN, '0000/00/00'), N': ',
                   CASE
                     WHEN NULLIF(LTRIM(RTRIM(ISNULL(v.ToCode, N''))), N'') IS NULL
                          THEN N'کالای مقصد مشخص نشده'
                     WHEN v.ToName IS NULL
                          THEN CONCAT(N'کالای مقصد «', v.ToCode, N'» در فهرست کالاها نیست')
                     WHEN ISNULL(v.ToQty, 0) <= 0
                          THEN N'مقدار ورود صفر است'
                     ELSE N'انبار مقصد مشخص نشده'
                   END,
                   N' — ', v.FromName, N' از انبار خارج شده و هیچ‌جا وارد نمی‌شود')
    FROM    dbo.CC_vw_ItemConversion v
    WHERE   v.DateN BETWEEN @DT1 AND @DT2
      AND   (NULLIF(LTRIM(RTRIM(ISNULL(v.ToCode, N''))), N'') IS NULL
         OR  v.ToName IS NULL
         OR  ISNULL(v.ToQty, 0) <= 0
         OR  v.ToAnbar IS NULL)
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-24' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = v.FromAnbar)
                          AND (ae.Code  IS NULL OR ae.Code  = TRY_CAST(v.FromCode AS BIGINT)));
END
GO

/* ───────────────────────── ثبت قاعده ─────────────────────────

   مسدودکننده است (Severity=2) و این عمدی است: برخلاف بقیه‌ی کنترل‌ها
   که گزارشِ وضعیت‌اند، این یکی یعنی موجودی از بین رفته. ادامه‌ی بستن
   ماه روی آن، یک ماهِ کامل روی عددِ غلط می‌سازد — همان معیاری که برای
   CHK-01 هست. */

MERGE dbo.CC_CheckRule AS t
USING (VALUES
 ('CHK-24', N'برگه تبدیل ناقص', 'S05', 26, 2, NULL,
  N'کالای مقصد و مقدار ورود را روی برگه تبدیل کامل کنید. تا وقتی مقصد مشخص نباشد، کالای خارج‌شده هیچ‌جا وارد نمی‌شود و موجودی کم می‌ماند.', 240)
) AS s (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
ON t.RuleCode = s.RuleCode
WHEN MATCHED THEN UPDATE SET
    t.RuleName = s.RuleName, t.StepCode = s.StepCode, t.ExType = s.ExType,
    t.DefaultSeverity = s.DefaultSeverity, t.Threshold = s.Threshold,
    t.RemedyText = s.RemedyText, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT
    (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
    VALUES (s.RuleCode, s.RuleName, s.StepCode, s.ExType, s.DefaultSeverity,
            s.Threshold, s.RemedyText, s.SortOrder);
GO

IF COL_LENGTH('dbo.CC_CheckRule', 'IsBlocking') IS NOT NULL
    UPDATE dbo.CC_CheckRule SET IsBlocking = 1 WHERE RuleCode = 'CHK-24';
GO

PRINT N'تبدیل کالا: TAGCOD 30/31، نما، کنترل CHK-24 آماده شد.';
GO
