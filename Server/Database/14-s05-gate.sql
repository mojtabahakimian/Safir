/* ═══════════════════════════════════════════════════════════════════
   S05 — دروازه اعتبارسنجی

   دو کنترلی که امروز با دابل‌کلیک روی گزارش موجودی می‌گیرید:
     CHK-01  کاردکس منفی
     CHK-02  مغایرت کارت انبار و حسابداری
     CHK-13  حواله با مقدار صفر

   نتیجه مستقیم در CC_Exception می‌نشیند و صفحه مغایرت‌ها نشانش می‌دهد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند (YAZDSEPAR{YEAR} در تولید، SafirTest* در محیط تست).
   اسکریپت را روی پایگاه هدف اجرا کنید. بقیه اسکریپت‌های
   Server/Database/ هم همین قرارداد را دارند.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_S05_Gate
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  RunId = @RunId AND RuleCode IN ('CHK-01','CHK-02','CHK-13');

    /* ─────────────────────────────────────────────────────────────
       طبقه‌بندی جهت TAG (ورود/خروج انبار) — منبع مرجع، نه حدس

       نسخهٔ قبلی این دو کنترل با یک حدس چهارتایی (TAG IN (1,7,9,24)
       = ورود، هر چیز دیگری = خروج) کار می‌کرد. کاربر تأیید کرد که
       هم CHK-01 و هم CHK-02 هر دو با شمار خیلی بالا (به ترتیب ۱۹۹ و
       ۱۱۸۳ مورد) غلط بودند، و کارت کالای واقعی هرگز منفی نمی‌شود؛
       یعنی آن حدس اشتباه بود.

       این نسخه از سه تابع واقعیِ همین دیتابیس که «کارت کالا» و
       CC_sp_S07 (انبارگردانی) از قبل به آنها اعتماد دارند کپی شده:
       dbo.AK_MOGO_AVL_KOL/_SUB (ورودی‌ها)، dbo.AK_MOGO_FR/_SUB
       (خروجی‌ها)، و dbo.MOGUDI/dbo.AKMOGUDI_KOL_ANBAR (موجودی نقطه‌ای
       — پایه گزارش کارت کالا). طبقه‌بندی TAG دقیقاً از همان‌جا آمده:

         ورود:  ۱ رسید خرید، ۷ تولید-ورود، ۹ حواله ورود، ۲۴ برگشت فروش
                ۲۲ برگشت فروش (فقط مقدار مرجوعی)
                ۵ انتقالی — طرف انبار فرعی (ستون ANBARF) = ورود مقصد
         خروج:  ۲ حواله فروش، ۸ تولید-خروج، ۱۰ حواله خروج،
                ۱۱ حواله خروج سایر، ۲۶ برگشت خرید آزاد
                ۵ انتقالی — طرف انبار اصلی (ستون ANBAR) = خروج مبدأ
                ۲۰ پیش‌فاکتور تسویه‌شده (فقط TAMIR=1 یا ۴)

       این لیست خودِ ۴ TAG قدیمی را هم دربردارد؛ فقط دیگر «هرچیز غیر
       از این ۴تا خروج است» فرض نمی‌شود — TAG هایی که در هیچ‌کدام از
       دو طرف رویه‌های مرجع نیامده‌اند (۳,۱۲,۱۳,۱۴,۱۵,۱۷,۱۸,۲۷) اصلاً
       در این محاسبه شرکت نمی‌کنند، دقیقاً چون منبع مرجع هم شرکتشان
       نمی‌دهد. TAG=6 «انتقالی-ورود» هم عمداً نیامده — طرف دیگرِ همان
       سند TAG=5 است و اگر هر دو حساب شوند، انتقال دوبار شمرده می‌شود.

       انبارگردانی (ANBGRD_LST/ANBGRD_HEAD) اینجا رویداد‌به‌رویداد
       اعمال می‌شود (نه با قاعدهٔ جمع‌کلِ عجیب رویه‌های مرجع که کل
       اختلاف یک کالا/انبار را یک‌جا یا کاملاً ورود یا کاملاً خروج
       حساب می‌کند) — چون این کنترل به ترتیب واقعی رویدادها نیاز
       دارد، نه فقط مانده نهایی.
       ───────────────────────────────────────────────────────────── */

    /* ─────────────────────────────────────────────────────────────
       CHK-01 — کاردکس منفی

       موجودی تجمعی هر کالا در هر انبار به ترتیب تاریخ محاسبه و
       هر جا منفی شد علامت می‌خورد.

       موجودی ابتدای دوره: چون تراکنش‌های ماه‌های قبل هم روی موجودی
       اثر دارند (و اگر نادیده گرفته شوند، اولین حوالهٔ همین ماه
       کاذباً منفی به‌نظر می‌رسد)، مانده ابتدای دوره از dbo.MOGUDI —
       همان تابع مرجعِ کارت کالا — در تاریخ یک روز قبل از @DT1 خوانده
       می‌شود، نه از صفر. (DATE_N به‌صورت اعداد ۸رقمی YYYYMMDD ذخیره
       شده؛ DT1-1 حسابی همیشه زیر اولین تاریخ واقعی همان ماه و بالای
       آخرین تاریخ واقعی ماه قبل می‌افتد، چون هیچ تاریخ واقعی روز/ماه
       صفر وجود ندارد — نیازی به تقویم شمسی نیست.)

       ترتیب داخل یک روز: NUMBER به‌تنهایی بین انواع مختلف برگه
       دنباله‌ی واحد و قابل‌اتکایی نیست (هرکدام شماره‌گذاری مستقل
       خودشان را دارند). ترتیب واقعی طبق قرارداد این سیستم از شرح
       تگ (TAGCOD.BARGAH) می‌آید، نه از NUMBER.

       فقط اولین نقطه منفی هر کالا/انبار در همین دوره گزارش می‌شود؛
       بقیه دنباله همان یک مشکل‌اند و فهرست را شلوغ می‌کنند.
       ───────────────────────────────────────────────────────────── */
    IF OBJECT_ID('tempdb..#PM') IS NOT NULL DROP TABLE #PM;

    -- ستون‌ها صریحاً تعریف می‌شوند (نه SELECT…INTO) چون شاخهٔ انبارگردانی
    -- برای TAG مقدار NULL دارد و نمی‌خواهیم NOT NULL این ستون از شاخهٔ
    -- اول به‌صورت ضمنی استنتاج شود.
    CREATE TABLE #PM (
        Anbar   INT          NULL,
        code    BIGINT       NULL,
        DATE_N  BIGINT       NULL,
        NUMBER  FLOAT        NULL,
        TAG     FLOAT        NULL,
        Meghdar FLOAT        NULL
    );

    INSERT #PM
    SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code,
            hl.DATE_N, hl.NUMBER, il.TAG, (il.MEGHk - il.MEGH_MAR) AS Meghdar
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG IN (1, 7, 9, 24)
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG, il.MEGH_MAR
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 22
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG,
            (il.MEGHk - il.MEGH_MAR)
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 5
      AND   il.ANBARF IS NOT NULL
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG,
            -(il.MEGHk - il.MEGH_MAR)
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG IN (2, 5, 8, 10, 11, 26)
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG, -il.MEGHk
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 20
      AND   (hl.TAMIR = 1 OR hl.TAMIR = 4)
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT), ah.GRD_DATE, ah.GRD_NUM,
            CAST(NULL AS FLOAT), -(al.MOG - ISNULL(al.NUM3, 0))
    FROM    dbo.ANBGRD_LST al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.N_S IS NOT NULL
      AND   ah.GRD_ANBAR IS NOT NULL
      AND   ah.GRD_DATE BETWEEN @DT1 AND @DT2;

    ;WITH DistinctAnbars AS (
        SELECT DISTINCT Anbar FROM #PM WHERE Anbar IS NOT NULL
    ),
    Opening AS (
        -- مانده ابتدای دوره از تابع مرجع کارت کالا، فقط برای جفت‌های
        -- (انبار، کالا) که واقعاً در همین دوره حرکت دارند.
        SELECT  m.ANBAR AS Anbar, TRY_CAST(m.CODE AS BIGINT) AS code, m.MAND AS OpeningBalance
        FROM    DistinctAnbars da
        CROSS   APPLY dbo.MOGUDI(@DT1 - 1, CAST(da.Anbar AS NVARCHAR(50))) m
    ),
    AllMovement AS (
        SELECT  o.Anbar, o.code, CAST(0 AS BIGINT) AS DATE_N, CAST(0 AS FLOAT) AS NUMBER,
                CAST(NULL AS FLOAT) AS TAG, N'' AS Bargah, o.OpeningBalance AS Meghdar
        FROM    Opening o
        WHERE   EXISTS (SELECT 1 FROM #PM p WHERE p.Anbar = o.Anbar AND p.code = o.code)

        UNION ALL
        SELECT  p.Anbar, p.code, p.DATE_N, p.NUMBER, p.TAG,
                ISNULL(tc.BARGAH, N'') AS Bargah, p.Meghdar
        FROM    #PM p
        LEFT    JOIN dbo.TAGCOD tc ON tc.CODE = p.TAG
        WHERE   p.Anbar IS NOT NULL AND p.code IS NOT NULL
    ),
    Tajamoi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Bargah,
                SUM(Meghdar) OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Bargah, NUMBER
                    ROWS UNBOUNDED PRECEDING) AS Mande
        FROM    AllMovement
    ),
    AvvalinManfi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Bargah, Mande,
                ROW_NUMBER() OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Bargah, NUMBER) AS rn
        FROM    Tajamoi
        WHERE   Mande < -0.0001
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocTag, DocDate, Amount, Description)
    SELECT  @RunId, 'S05', 'CHK-01', 1, 2,
            m.Anbar, m.code, m.NUMBER, m.TAG, m.DATE_N, m.Mande,
            CONCAT(N'انبار ', m.Anbar, N' (', ISNULL(a.NAMES, N'نامشخص'), N'): موجودی در تاریخ ',
                   m.DATE_N / 10000, '/',
                   FORMAT(m.DATE_N / 100 % 100, '00'), '/',
                   FORMAT(m.DATE_N % 100, '00'),
                   N' منفی می‌شود')
    FROM    AvvalinManfi m
    LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = m.Anbar
    WHERE   m.rn = 1
      AND   m.DATE_N BETWEEN @DT1 AND @DT2;

    DROP TABLE #PM;

    /* ─────────────────────────────────────────────────────────────
       CHK-02 — مغایرت کارت انبار و حسابداری

       مانده ریالی کارت انبار با مانده حساب موجودی جنسی مقایسه
       می‌شود. کارت انبار اینجا مستقیماً از INVO_LST/HEAD_LST با
       همان طبقه‌بندی TAG بالا محاسبه می‌شود (نه KALAS، که فقط یک ویو
       گزارشی روی همین جدول‌هاست) — چون CHK-02 برخلاف CHK-01 فقط به
       مانده نهایی نیاز دارد، نه ترتیب تراکنش‌ها، آستانهٔ زمانی همان
       «<= @DT2» قبلی است (تجمعی از ابتدای تاریخچه، نه فقط این ماه).

       هر انبار زیر یک معین جداگانه در حسابداری ثبت می‌شود، نه یک
       معین ثابت مشترک برای همهٔ انبارها (تفصیلی طبق ساختار خودِ
       دیتابیس فقط زیر یک معین مشخص یکتاست: TDETA_HES.PK =
       (N_KOL,NUMBER,TNUMBER)). نگاشت واقعیِ انبار⇄معین از CC_AnbarHes
       (تنظیمات) خوانده می‌شود، نه هاردکد، چون شرکت‌به‌شرکت فرق می‌کند.

       آستانه یک ریال است چون این دو باید دقیقاً یکی باشند.
       ───────────────────────────────────────────────────────────── */
    IF NOT EXISTS (SELECT 1 FROM dbo.CC_AnbarHes)
    BEGIN
        -- بدون نگاشت انبار⇄معین نمی‌توان درست مقایسه کرد؛ به‌جای هزاران
        -- مورد کاذب، یک هشدار واحد می‌گوید چه باید تنظیم شود.
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Description)
        VALUES
            (@RunId, 'S05', 'CHK-02', 2, 1,
             N'نگاشت انبار به حساب موجودی (کل/معین) در تنظیمات ثبت نشده؛ این کنترل غیرفعال است.');
    END
    ELSE
    BEGIN
        ;WITH AnbarMovement AS (
            SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code, il.MABL_K AS Mablk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (1, 7, 9, 24)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), (il.MABL * il.MEGH_MAR)
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 22
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.MABL_K
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 5
              AND   il.ANBARF IS NOT NULL
              AND   hl.DATE_N <= @DT2
              AND   CAST(il.ANBARF AS INT) IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MABL_K
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (2, 5, 8, 10, 11, 26)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MABL_K
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 20
              AND   (hl.TAMIR = 1 OR hl.TAMIR = 4)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT),
                    -(al.MOG - ISNULL(al.NUM3, 0)) * ISNULL(al.MABL, 0)
            FROM    dbo.ANBGRD_LST al
            JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
            WHERE   ah.N_S IS NOT NULL
              AND   ah.GRD_DATE <= @DT2
              AND   ah.GRD_ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)
        ),
        KartAnbar AS (
            SELECT  Anbar, code, SUM(Mablk) AS Mande
            FROM    AnbarMovement
            WHERE   Anbar IS NOT NULL AND code IS NOT NULL
            GROUP BY Anbar, code
        ),
        Hesabdari AS (
            SELECT  am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code,
                    SUM(d.BED) - SUM(d.BES) AS Mande
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED  h  ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   h.DATE_S <= @DT2
            GROUP BY am.Anbar, TRY_CAST(d.HES_T AS BIGINT)
        )
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
        SELECT  @RunId, 'S05', 'CHK-02', 2, 2,
                ISNULL(k.Anbar, hh.Anbar), ISNULL(k.code, hh.code),
                ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0),
                CONCAT(N'انبار ', ISNULL(k.Anbar, hh.Anbar),
                       N' (', ISNULL(a.NAMES, N'نامشخص'), N'): کارت انبار ',
                       FORMAT(ISNULL(k.Mande, 0), 'N0'),
                       N' در برابر حسابداری ', FORMAT(ISNULL(hh.Mande, 0), 'N0'))
        FROM    KartAnbar k
        FULL    OUTER JOIN Hesabdari hh ON hh.Anbar = k.Anbar AND hh.code = k.code
        LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = ISNULL(k.Anbar, hh.Anbar)
        WHERE   ABS(ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0)) > 1;
    END

    /* ─────────────────────────────────────────────────────────────
       CHK-13 — حواله با مقدار صفر

       ماده‌ای که در فرمول مقدار دارد ولی حواله‌اش صفر است، یعنی
       فرمول پس از صدور حواله ویرایش شده و خروج مواد بازسازی نشده.
       این همان چیزی است که برای کالای ۲۸۴۱ در ماه تیر رخ داد.
       ───────────────────────────────────────────────────────────── */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocDate, Amount, Description)
    SELECT  DISTINCT @RunId, 'S05', 'CHK-13', 16, 2,
            i.ANBAR, CAST(i.CODE AS BIGINT), h.NUMBER, h.DATE_N, 0,
            N'حواله با مقدار صفر برای ماده‌ای که در فرمول ماه مقدار دارد'
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE   h.TAG = 10
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(i.MEGHK, 0) = 0
      AND   EXISTS (
                SELECT 1
                FROM   dbo.DTL_MANF d
                JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
                WHERE  CAST(d.CODE AS BIGINT) = CAST(i.CODE AS BIGINT)
                  AND  d.MEGHk > 0);

    /* ─────────────────────────── خلاصه ─────────────────────────── */
    SELECT  e.RuleCode                                            AS قاعده,
            r.RuleName                                            AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده'
                            ELSE N'هشدار' END                     AS شدت,
            COUNT(*)                                              AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.RunId = @RunId AND e.StepCode = 'S05' AND e.IsResolved = 0
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO

/* رویه آزمایشی قدیمی که جای خود را به CC_sp_S00_Preflight داده است. */
DROP PROCEDURE IF EXISTS dbo.CC_sp_Preflight;
GO

PRINT N'رويه دروازه اعتبارسنجي ايجاد شد.';
GO
