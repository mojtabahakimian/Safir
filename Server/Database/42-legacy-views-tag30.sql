/* ═══════════════════════════════════════════════════════════════════
   یاد دادن TAG=30 (تبدیل کالا) به ویوهای قدیمیِ موجودی

   ── مسئله ──
   موجودی در این پایگاه از یک خانواده ویو و تابع درمی‌آید که فهرستِ
   نوع برگه‌ها را *هاردکد* دارند:

     خروج :  TAG = 2 OR 5 OR 8 OR 10 OR 11 OR 26
     ورود  :  TAG = 1 OR 7 OR 9 OR 24
     ورودِ انتقالی : TAG = 5، گروه‌بندی روی ANBARF

   برگه‌ی تبدیل (۳۰) در هیچ‌کدام نیست، پس تا امروز نه از انبار مبدأ کم
   می‌شود و نه به انبار مقصد اضافه — از نظر این گزارش‌ها اصلاً اتفاق
   نیفتاده است.

   ── چرا شاخه‌ی جدا و نه اضافه‌کردن ۳۰ به همان فهرست‌ها ──
   چون فرمولِ آن شاخه‌ها SUM(MEGHk - MEGH_MAR) است و روی برگه‌ی تبدیل،
   MEGH_MAR «مقدار مرجوعی» نیست — مقدارِ ورودِ کالای مقصد است. اگر ۳۰
   را داخل همان فهرست بیندازیم، خروجِ کالای مبدأ به‌اندازه‌ی مقدارِ
   ورودِ کالای مقصد کم گزارش می‌شود. بی‌صدا و در همه‌ی گزارش‌ها.

   پس هر شیء دو شاخه‌ی تازه می‌گیرد:
     خروج : TAG = 30، گروه روی (CODE، ANBAR)،  مقدار = MEGHk
     ورود : TAG = 30، گروه روی (N_RASID، ANBARF)، مقدار = MEGH_MAR

   ── محافظه‌کاری ──
   این اسکریپت ویوهای شرکت را بازنویسی می‌کند، و ویوها بین نصب‌ها فرق
   دارند. پس هر کدام فقط وقتی دست می‌خورد که:
     ۱. قبلاً TAG=30 را نداشته باشد (اجرای دوباره بی‌اثر است)، و
     ۲. تعریفِ فعلی‌اش همان امضای شناخته‌شده را داشته باشد.
   اگر نصبی ویو را خودش عوض کرده باشد، دست‌نخورده می‌ماند و اسکریپت
   نامش را چاپ می‌کند تا دستی بررسی شود. ساکت رد نمی‌شود.

   کارت کالا (KA_KH) هم اینجاست، ولی با روش دیگری: تعریفش بازنویسی
   نمی‌شود، فقط دو UNION به انتهای بدنه‌اش اضافه می‌شود. کارت کالا بین
   نصب‌ها ستون‌های متفاوتی دارد و بازنویسیِ کاملش یعنی پاک‌کردنِ
   تغییراتِ همان شرکت.

   MOGHA_ANBAR اینجا نیست چون خودمان صاحبش هستیم —
   21-mogha-anbar-tiebreak-fix.sql تعریفش را می‌سازد و شاخه‌های تبدیل
   همان‌جا اضافه شده‌اند.

   ⚠️ هنوز پوشش داده نشده: AK_MOGO_FR_SUB، AK_MOGO_AVL_KOL_SUB و
   MOG_FR_A_sub — گزارش‌های تاریخ‌دارِ تراز انبار.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


/* ───────── ۱) MOG_FR_SUB — خروج، در سطح کد کالا ───────── */

IF OBJECT_ID('dbo.MOG_FR_SUB','V') IS NOT NULL
BEGIN
    DECLARE @d1 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.MOG_FR_SUB'));

    IF @d1 LIKE '%TAG = 30%'
        PRINT N'MOG_FR_SUB از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d1 NOT LIKE '%TAG = 11%'
        PRINT N'⚠ MOG_FR_SUB تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        EXEC(N'
ALTER VIEW [dbo].[MOG_FR_SUB]
AS
SELECT     CODE, SUM(MEGHk - MEGH_MAR) AS MEG, SUM((MEGHk - MEGH_MAR) * AVRAGE) AS SumOfMABL_K
FROM         dbo.INVO_LST
WHERE     (TAG = 2) OR (TAG = 8) OR (TAG = 10) OR (TAG = 11) OR (TAG = 26)
GROUP BY CODE
UNION
/* تبدیل کالا — سمت خروج. MEGH_MAR اینجا مقدارِ ورودِ کالای مقصد است،
   پس عمداً کم نمی‌شود. */
SELECT     CODE, SUM(MEGHk) AS MEG, SUM(MEGHk * AVRAGE) AS SumOfMABL_K
FROM         dbo.INVO_LST
WHERE     TAG = 30
GROUP BY CODE
UNION
SELECT     dbo.ANBGRD_LST.CODE, SUM((dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3)) AS MEG, SUM(dbo.ANBGRD_LST.MABL) AS mablk
FROM         dbo.ANBGRD_LST INNER JOIN
                      dbo.ANBGRD_HEAD ON dbo.ANBGRD_LST.GRD_NUM = dbo.ANBGRD_HEAD.GRD_NUM
WHERE     (NOT (dbo.ANBGRD_HEAD.N_S IS NULL)) AND ((dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) > 0)
GROUP BY dbo.ANBGRD_LST.CODE');
        PRINT N'MOG_FR_SUB به‌روز شد.';
    END
END
GO

/* ───────── ۲) MOGO_AVL_KOL_SUB — ورود، در سطح کد کالا ───────── */

IF OBJECT_ID('dbo.MOGO_AVL_KOL_SUB','V') IS NOT NULL
BEGIN
    DECLARE @d2 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.MOGO_AVL_KOL_SUB'));

    IF @d2 LIKE '%TAG = 30%'
        PRINT N'MOGO_AVL_KOL_SUB از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d2 NOT LIKE '%TAG = 24%'
        PRINT N'⚠ MOGO_AVL_KOL_SUB تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        EXEC(N'
ALTER VIEW [dbo].[MOGO_AVL_KOL_SUB]
AS
SELECT     CODE, SUM(MOGODI_A) AS MEG, SUM(MABL_A) AS SumOfMABL_A, 0 AS AA
FROM         dbo.STUF_FSK
GROUP BY CODE
UNION
SELECT     CODE, SUM(MEGHk - MEGH_MAR) AS MEG, SUM(MABL_K) AS SumOfMABL_K, 1 AS AA
FROM         dbo.INVO_LST
WHERE     (TAG = 1) OR (TAG = 7) OR (TAG = 9) OR (TAG = 24)
GROUP BY CODE
UNION
SELECT     CODE, SUM(MEGHk) AS MEG, SUM(MABL_K) AS SumOfMABL_K, 1 AS AA
FROM         dbo.INVO_LST
WHERE     (TAG = 22)
GROUP BY CODE
UNION
/* تبدیل کالا — سمت ورود. کد کالای مقصد در N_RASID و مقدارش در
   MEGH_MAR است؛ ارزشش همان MABL_K است چون یک مبلغ برای هر دو سر. */
SELECT     N_RASID, SUM(MEGH_MAR) AS MEG, SUM(MABL_K) AS SumOfMABL_K, 3 AS AA
FROM         dbo.INVO_LST
WHERE     TAG = 30 AND N_RASID IS NOT NULL AND MEGH_MAR > 0
GROUP BY N_RASID
UNION
SELECT     dbo.ANBGRD_LST.CODE, (dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) * - 1 AS MEG, 0 AS mabl, 2 AS aa
FROM         dbo.ANBGRD_LST INNER JOIN
                      dbo.ANBGRD_HEAD ON dbo.ANBGRD_LST.GRD_NUM = dbo.ANBGRD_HEAD.GRD_NUM
WHERE     ((dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) * - 1 > 0) AND (dbo.ANBGRD_HEAD.N_S IS NOT NULL)');
        PRINT N'MOGO_AVL_KOL_SUB به‌روز شد.';
    END
END
GO

/* ───────── ۳) B_MOG_FR_sub — خروج، در سطح انبار ───────── */

IF OBJECT_ID('dbo.B_MOG_FR_sub','V') IS NOT NULL
BEGIN
    DECLARE @d3 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.B_MOG_FR_sub'));

    IF @d3 LIKE '%TAG = 30%'
        PRINT N'B_MOG_FR_sub از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d3 NOT LIKE '%TAG = 11%'
        PRINT N'⚠ B_MOG_FR_sub تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        EXEC(N'
ALTER VIEW [dbo].[B_MOG_FR_sub]
AS
SELECT     CODE, SUM(MEGHk - MEGH_MAR) AS MEG, ANBAR
FROM         dbo.INVO_LST
WHERE     (TAG = 2) OR (TAG = 5) OR (TAG = 8) OR (TAG = 10) OR (TAG = 11) OR (TAG = 26)
GROUP BY ANBAR, CODE
UNION
SELECT     CODE, SUM(MEGHk) AS MEG, ANBAR
FROM         dbo.INVO_LST
WHERE     TAG = 30
GROUP BY ANBAR, CODE
UNION
SELECT     dbo.ANBGRD_LST.CODE, (dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) AS MEG, dbo.ANBGRD_HEAD.GRD_ANBAR AS anbar
FROM         dbo.ANBGRD_LST INNER JOIN
                      dbo.ANBGRD_HEAD ON dbo.ANBGRD_LST.GRD_NUM = dbo.ANBGRD_HEAD.GRD_NUM
WHERE     ((dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) >= 0 AND (NOT (dbo.ANBGRD_HEAD.N_S IS NULL)))');
        PRINT N'B_MOG_FR_sub به‌روز شد.';
    END
END
GO

/* ───────── ۴) B_MOG_KOL_SUB — ورود، در سطح انبار ───────── */

IF OBJECT_ID('dbo.B_MOG_KOL_SUB','V') IS NOT NULL
BEGIN
    DECLARE @d4 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.B_MOG_KOL_SUB'));

    IF @d4 LIKE '%TAG = 30%'
        PRINT N'B_MOG_KOL_SUB از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d4 NOT LIKE '%TAG = 24%'
        PRINT N'⚠ B_MOG_KOL_SUB تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        EXEC(N'
ALTER VIEW [dbo].[B_MOG_KOL_SUB]
AS
SELECT     ANBAR, CODE, SUM(MEGHk - MEGH_MAR) AS MEG, 0 AS AA
FROM         dbo.INVO_LST
WHERE     (TAG = 1) OR (TAG = 7) OR (TAG = 9) OR (TAG = 24)
GROUP BY ANBAR, CODE
UNION
SELECT     ANBAR, CODE, SUM(MOGODI_A) AS MEG, 1 AS AA
FROM         dbo.STUF_FSK
GROUP BY ANBAR, CODE
UNION
SELECT     ANBARF, CODE, SUM(MEGHk - MEGH_MAR) AS MEG, 2 AS AA
FROM         dbo.INVO_LST
WHERE     (TAG = 5)
GROUP BY ANBARF, CODE
UNION
SELECT     ANBAR, CODE, SUM(MEGH_MAR) AS MEG, 0 AS AA
FROM         dbo.INVO_LST
WHERE     (TAG = 22)
GROUP BY ANBAR, CODE
UNION
/* تبدیل کالا — سمت ورود: انبار مقصد، کد مقصد، مقدارِ ورود. */
SELECT     CAST(ANBARF AS INT), N_RASID, SUM(MEGH_MAR) AS MEG, 3 AS AA
FROM         dbo.INVO_LST
WHERE     TAG = 30 AND N_RASID IS NOT NULL AND ANBARF IS NOT NULL AND MEGH_MAR > 0
GROUP BY CAST(ANBARF AS INT), N_RASID');
        PRINT N'B_MOG_KOL_SUB به‌روز شد.';
    END
END
GO

/* ───────── ۵) mogudi_1 — ورودِ یک انبار ───────── */

IF OBJECT_ID('dbo.mogudi_1','IF') IS NOT NULL
BEGIN
    DECLARE @d5 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.mogudi_1'));

    IF @d5 LIKE '%TAG = 30%'
        PRINT N'mogudi_1 از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d5 NOT LIKE '%TAG = 24%'
        PRINT N'⚠ mogudi_1 تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        EXEC(N'
ALTER FUNCTION [dbo].[mogudi_1] (@Forms___F_MENU_ANBAR___MANBAR int)
RETURNS TABLE
AS
RETURN ( SELECT CODE, SUM(MOGODI_A) AS MEG, ANBAR, 0 AS AA
 FROM dbo.STUF_FSK GROUP BY CODE, ANBAR
 HAVING (ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT CODE, SUM(MEGHk - MEGH_MAR) AS MEG, ANBAR, 1 AS AA
 FROM dbo.INVO_LST
 WHERE (TAG = 1) OR (TAG = 7) OR (TAG = 9) OR (TAG = 24)
 GROUP BY CODE, ANBAR HAVING (ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT CODE, SUM(MEGH_MAR) AS MEG, ANBAR, 1 AS AA
 FROM dbo.INVO_LST WHERE (TAG = 22)
 GROUP BY CODE, ANBAR HAVING (ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT CODE, SUM(MEGHk - MEGH_MAR) AS MEG, ANBARF, 2 AS AA
 FROM dbo.INVO_LST WHERE (TAG = 5)
 GROUP BY CODE, ANBARF HAVING (ANBARF = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT N_RASID, SUM(MEGH_MAR) AS MEG, CAST(ANBARF AS INT), 4 AS AA
 FROM dbo.INVO_LST
 WHERE TAG = 30 AND N_RASID IS NOT NULL AND MEGH_MAR > 0
 GROUP BY N_RASID, CAST(ANBARF AS INT)
 HAVING (CAST(ANBARF AS INT) = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT dbo.ANBGRD_LST.CODE, SUM((dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) * - 1) AS MEG, dbo.ANBGRD_HEAD.GRD_ANBAR AS anbar, 3 AS AA
 FROM dbo.ANBGRD_LST INNER JOIN dbo.ANBGRD_HEAD ON dbo.ANBGRD_LST.GRD_NUM = dbo.ANBGRD_HEAD.GRD_NUM
 WHERE (NOT (dbo.ANBGRD_HEAD.N_S IS NULL)) AND (dbo.ANBGRD_HEAD.GRD_ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 GROUP BY dbo.ANBGRD_LST.CODE, dbo.ANBGRD_HEAD.GRD_ANBAR
 HAVING (SUM((dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) * - 1) >= 0) )');
        PRINT N'mogudi_1 به‌روز شد.';
    END
END
GO

/* ───────── ۶) mogudi_2 — خروجِ یک انبار ───────── */

IF OBJECT_ID('dbo.mogudi_2','IF') IS NOT NULL
BEGIN
    DECLARE @d6 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.mogudi_2'));

    IF @d6 LIKE '%TAG = 30%'
        PRINT N'mogudi_2 از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d6 NOT LIKE '%TAG = 11%'
        PRINT N'⚠ mogudi_2 تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        EXEC(N'
ALTER FUNCTION [dbo].[mogudi_2] (@Forms___F_MENU_ANBAR___MANBAR int)
RETURNS TABLE
AS
RETURN (SELECT CODE, SUM(MEGHk - MEGH_MAR) AS MEG, ANBAR, 0 AS kk
 FROM dbo.INVO_LST
 WHERE (TAG = 2 OR TAG = 5 OR TAG = 8 OR TAG = 10 OR TAG = 11 OR TAG = 26)
 GROUP BY CODE, ANBAR HAVING (ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT CODE, SUM(MEGHk) AS MEG, ANBAR, 2 AS kk
 FROM dbo.INVO_LST WHERE TAG = 30
 GROUP BY CODE, ANBAR HAVING (ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 UNION
 SELECT dbo.ANBGRD_LST.CODE, SUM(dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) AS MEG, dbo.ANBGRD_HEAD.GRD_ANBAR, 1 AS kk
 FROM dbo.ANBGRD_LST INNER JOIN dbo.ANBGRD_HEAD ON dbo.ANBGRD_LST.GRD_NUM = dbo.ANBGRD_HEAD.GRD_NUM
 WHERE (NOT (dbo.ANBGRD_HEAD.N_S IS NULL)) AND (dbo.ANBGRD_HEAD.GRD_ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
 GROUP BY dbo.ANBGRD_LST.CODE, dbo.ANBGRD_HEAD.GRD_ANBAR
 HAVING (SUM(dbo.ANBGRD_LST.MOG - dbo.ANBGRD_LST.NUM3) > 0)
 UNION
 SELECT dbo.INVO_LST.CODE, SUM(dbo.INVO_LST.MEGHk) AS MEG, dbo.INVO_LST.ANBAR, 4 AS AA
 FROM dbo.HEAD_LST INNER JOIN dbo.INVO_LST ON dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG AND dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER
 WHERE (dbo.INVO_LST.TAG = 20) AND (dbo.HEAD_LST.TAMIR = 1)
 GROUP BY dbo.INVO_LST.CODE, dbo.INVO_LST.ANBAR
 HAVING (dbo.INVO_LST.ANBAR = @Forms___F_MENU_ANBAR___MANBAR) )');
        PRINT N'mogudi_2 به‌روز شد.';
    END
END
GO

/* ───────── ۷) KA_KH — کارت کالا ─────────

   این همان چیزی است که کاربر باز می‌کند تا ببیند یک کالا کِی و با چه
   نرخی آمده و رفته. تا امروز برگه‌ی تبدیل در آن اصلاً ردیفی نداشت —
   نه در کارتِ کالای مبدأ و نه در کارتِ کالای مقصد.

   دو ردیف اضافه می‌شود، هرکدام از یک سرِ همان یک سطر:

     خروج : انبار و کد مبدأ، مقدار منفی، نرخ = AVRAGE،
            BEDNAME = نام انبار مقصد («به کجا رفت»)
     ورود : انبار و کد مقصد، مقدار مثبت، نرخ = MABL_K ÷ MEGH_MAR،
            BEDNAME = نام انبار مبدأ («از کجا آمد»)

   ⚠️ نرخِ سمت ورود عمداً MABL نیست. MABL نرخِ کالای *مبدأ* است؛ نرخِ
   کالای مقصد از تقسیم مبلغ بر مقدارِ ورود درمی‌آید. همان الگوی TAG=5
   که آن هم AVRAGE2 را برای سمت مقصد نشان می‌دهد. */

IF OBJECT_ID('dbo.KA_KH','IF') IS NOT NULL
BEGIN
    DECLARE @d7 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.KA_KH'));

    IF @d7 LIKE '%TAG = 30%'
        PRINT N'KA_KH از قبل TAG=30 را می‌شناسد.';
    ELSE IF @d7 NOT LIKE '%BEDNAME%' OR @d7 NOT LIKE '%TAG = 11%'
        PRINT N'⚠ KA_KH تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        DECLARE @newKaKh NVARCHAR(MAX) =
            REPLACE(@d7, 'CREATE FUNCTION', 'ALTER FUNCTION');

        /* آخرین پرانتزِ بسته‌ی بدنه را پیدا می‌کنیم و دو UNION را
           درست پیش از آن می‌گذاریم. کلِ تعریف بازنویسی نمی‌شود — هرچه
           این نصب دارد سرِ جایش می‌ماند و فقط دو شاخه اضافه می‌شود.
           اگر ساختار آن‌قدر فرق داشته باشد که این پرانتز پیدا نشود،
           دست نمی‌زنیم. */
        DECLARE @cut INT = LEN(@newKaKh) - CHARINDEX(')', REVERSE(@newKaKh));

        IF @cut <= 0
            PRINT N'⚠ KA_KH تعریف غیرمنتظره دارد — دست نخورد.';
        ELSE
        BEGIN
            SET @newKaKh =
                LEFT(@newKaKh, @cut) + N'
        UNION
        /* تبدیل کالا — سمت خروج (کارتِ کالای مبدأ) */
        SELECT     i.ANBAR, i.CODE, i.MEGHk * -1 AS MEG, i.MABL, i.MABL_K, h.TAG,
                   i.MEGHk, h.DATE_N, i.NUMBER, ta.NAMES AS BEDNAME, i.AVRAGE, h.FNUMCO,
                   i.id, ISNULL(i.MANDAH, N''  '') AS mol
        FROM       dbo.HEAD_LST h
                   INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
                   LEFT OUTER JOIN dbo.TCOD_ANBAR ta ON ta.CODE = CAST(i.ANBARF AS INT)
        WHERE      h.TAG = 30
        UNION
        /* تبدیل کالا — سمت ورود (کارتِ کالای مقصد) */
        SELECT     CAST(i.ANBARF AS INT), i.N_RASID, i.MEGH_MAR AS MEG,
                   CASE WHEN ISNULL(i.MEGH_MAR, 0) = 0 THEN 0 ELSE i.MABL_K / i.MEGH_MAR END,
                   i.MABL_K, 31 AS TAG,
                   i.MEGH_MAR, h.DATE_N, i.NUMBER, ta.NAMES AS BEDNAME, i.AVRAGE2, h.FNUMCO,
                   i.id, ISNULL(i.MANDAH, N''  '') AS mol
        FROM       dbo.HEAD_LST h
                   INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
                   LEFT OUTER JOIN dbo.TCOD_ANBAR ta ON ta.CODE = i.ANBAR
        WHERE      h.TAG = 30 AND i.N_RASID IS NOT NULL AND i.ANBARF IS NOT NULL
   ' + SUBSTRING(@newKaKh, @cut + 1, LEN(@newKaKh));

            BEGIN TRY
                EXEC sp_executesql @newKaKh;
                PRINT N'KA_KH به‌روز شد.';
            END TRY
            BEGIN CATCH
                PRINT N'⚠ KA_KH به‌روز نشد: ' + ERROR_MESSAGE();
            END CATCH
        END
    END
END
GO


PRINT N'ویوهای موجودی و کارت کالا: TAG=30 بررسی شد.';
PRINT N'⚠ هنوز پوشش داده نشده: AK_MOGO_FR_SUB، AK_MOGO_AVL_KOL_SUB، MOG_FR_A_sub.';
GO
