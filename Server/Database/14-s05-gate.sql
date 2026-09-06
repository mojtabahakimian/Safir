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
       دو طرف رویه‌های مرجع نیامده‌اند (۱۲,۱۳,۱۴,۱۵,۱۷,۱۸,۲۷) اصلاً
       در این محاسبه شرکت نمی‌کنند، دقیقاً چون منبع مرجع هم شرکتشان
       نمی‌دهد. TAG=6 «انتقالی-ورود» هم عمداً نیامده — طرف دیگرِ همان
       سند TAG=5 است و اگر هر دو حساب شوند، انتقال دوبار شمرده می‌شود.

       انبارگردانی (ANBGRD_LST/ANBGRD_HEAD) اینجا رویداد‌به‌رویداد
       اعمال می‌شود (نه با قاعدهٔ جمع‌کلِ عجیب رویه‌های مرجع که کل
       اختلاف یک کالا/انبار را یک‌جا یا کاملاً ورود یا کاملاً خروج
       حساب می‌کند) — چون این کنترل به ترتیب واقعی رویدادها نیاز
       دارد، نه فقط مانده نهایی.

       ⚠️ اصلاح دوم (بعد از تأیید کاربر که کاردکس منفی هنوز غلط بود):
       AK_MOGO_AVL_KOL_SUB/AK_MOGO_FR_SUB (منبع بالا) فقط «مانده‌ی
       نهایی تا یک تاریخ» را درست می‌دهند، نه ترتیب. TAG=3 (برگشت
       خرید) و TAG=4 (برگشت فروش) اصلاً به این شکل در HEAD_LST/INVO_LST
       ثبت نمی‌شوند؛ آن دو تابع به‌جایش MEGH_MAR را همان لحظه، مستقیم
       روی سند اصلی (خرید/فروش) کسر می‌کنند — یعنی مقدار برگشتی از
       همان تاریخِ سند اصلی از مانده کم می‌شود، نه از تاریخ واقعیِ خودِ
       برگشت. برای «مانده‌ی نهایی» (CHK-02، یا مانده ابتدای دوره) این
       فرقی نمی‌کند چون فقط جمع نهایی مهم است؛ اما برای این کنترل که
       به ترتیب واقعی نیاز دارد، دقیقاً همین فرق باعث منفیِ کاذب یا
       واقعیِ نادیده‌گرفته‌شده می‌شود — یک حواله همین ماه ممکن است روی
       مانده‌ای بنشیند که زودتر از موعد (در تاریخ خرید اصلی، نه تاریخ
       برگشت) کم شده.

       منبع واقعیِ ترتیب‌دار، تابع dbo.KA_KH است (پایه‌ی گزارش «کارت
       کالا» که طبق تأیید کاربر هرگز منفی نمی‌شود). آنجا TAG=3/4 از
       جدول جداگانه‌ی dbo.BACK_HEAD می‌آیند: BACK_HEAD.ta=1 یعنی برگشت
       خرید (⇒TAG=3، NUMBER1 به همان سند خرید در INVO_LST اشاره می‌کند)
       و BACK_HEAD.ta=2 یعنی برگشت فروش (⇒TAG=4). BACK_HEAD.DATE_N
       تاریخ واقعیِ خودِ برگشت است. به همین دلیل اینجا هم TAG(1,7,9,24)
       و TAG(2,5,8,10,11,26) دیگر MEGH_MAR را کم نمی‌کنند (تا دوبار
       حساب نشود) و به‌جایش دو شاخه‌ی جدید از BACK_HEAD اضافه شده که
       دقیقاً در تاریخ خودِ برگشت اثر می‌گذارند — عیناً مطابق KA_KH.

       همین جابه‌جاییِ تاریخ روی مانده‌ی ابتدای دوره (dbo.MOGUDI) هم اثر
       دارد: MOGUDI هم از همان دو تابع مرجع می‌آید، پس اگر سند اصلی قبل
       از @DT1 باشد ولی برگشتش در همین دوره (یا بعدش) ثبت شده باشد،
       MOGUDI آن را زودتر از موعد کم/زیاد کرده. OpeningBackHeadFix پایین
       دقیقاً همین موارد را پیدا و برمی‌گرداند تا در #PM (در تاریخ واقعی
       خودشان) دوباره اعمال شوند.
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
       خودشان را دارند).

       ⚠️ چهارمین اصلاح (بعد از تأیید کاربر): منبع درستِ ترتیب داخل یک
       روز، TAGCOD.BARGAH (متن) نیست — TAGCOD.tartib است، یک ستون عددی
       که دقیقاً برای همین منظور طراحی شده. مقایسه‌ی متنیِ BARGAH (چه
       خام، چه با LTRIM) چون به فاصله‌های ابتدایی ناهمسان و ترتیب
       الفبایی وابسته است، با ترتیب واقعی کسب‌وکار یکی نیست — مثلاً
       «انتقالی-ورود» (tartib=10) باید قبل از «برگشت خرید آزاد»
       (tartib=11) بیاید، ولی این دو به‌عنوان متن با هم می‌آمیزند. با
       گزارش واقعی کارت کالا تأیید شد که فقط tartib ترتیب درست را می‌دهد.

       فقط اولین نقطه منفی هر کالا/انبار در همین دوره گزارش می‌شود؛
       بقیه دنباله همان یک مشکل‌اند و فهرست را شلوغ می‌کنند.

       ⚠️ هفتمین اصلاح: آستانه دیگر عدد ثابت -0.0001 نیست — از
       CC_CheckRule.Threshold (قابل تنظیم در تنظیمات برنامه) خوانده
       می‌شود. چون MEGHk/MABL از نوع FLOAT هستند، بعضی کالاها به‌خاطر
       نسبت‌های تبدیل واحد یه باقیمانده‌ی واقعیِ خیلی کوچک دارند که هرگز
       دقیقاً صفر نمی‌شود؛ این آستانه برای فیلتر همین نویز است، نه برای
       نادیده گرفتن کمبود واقعی. پیش‌فرض -0.001.
       ───────────────────────────────────────────────────────────── */
    DECLARE @Chk01Threshold DECIMAL(18,6) =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode = 'CHK-01'), -0.001);

    IF OBJECT_ID('tempdb..#PM') IS NOT NULL DROP TABLE #PM;

    -- ستون‌ها صریحاً تعریف می‌شوند (نه SELECT…INTO) چون شاخهٔ انبارگردانی
    -- برای TAG مقدار NULL دارد و نمی‌خواهیم NOT NULL این ستون از شاخهٔ
    -- اول به‌صورت ضمنی استنتاج شود.
    --
    -- ⚠️ ششمین اصلاح: Meghdar عمداً DECIMAL است، نه FLOAT. MEGHk/MABL در
    -- خودِ INVO_LST از نوع FLOAT هستند، و جمع تجمعیِ FLOAT روی صدها ردیف
    -- یک ماه همیشه یک باقیمانده‌ی خیلی کوچک (مثلاً ۰٫۰۰۴) به‌جای صفر
    -- دقیق می‌گذارد — حتی وقتی ریاضی واقعی باید دقیقاً صفر شود. تبدیل به
    -- DECIMAL درست همینجا (قبل از SUM OVER)، نه بعدش، این خطای انباشتی
    -- را از ریشه حذف می‌کند.
    CREATE TABLE #PM (
        Anbar   INT             NULL,
        code    BIGINT          NULL,
        DATE_N  BIGINT          NULL,
        NUMBER  FLOAT           NULL,
        TAG     FLOAT           NULL,
        Meghdar DECIMAL(18,6)   NULL
    );

    INSERT #PM
    SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code,
            hl.DATE_N, hl.NUMBER, il.TAG, il.MEGHk AS Meghdar
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

    -- ⚠️ سومین اصلاح (بعد از تأیید کاربر با گزارش واقعی کارت کالا):
    -- طرف مقصدِ انتقالی (ANBARF) عمداً TAG=6 درج می‌شود، نه TAG=5 خودِ
    -- سند. دلیل: BARGAH طرف مقصد باید «انتقالی - ورود» باشد نه «انتقالی
    -- - خروج»، وگرنه ترتیب همان روز (پایین، ORDER BY ... LTRIM(BARGAH))
    -- غلط می‌شود. عیناً مطابق dbo.KA_KH که همین طرف را با «6 AS TA» برمی‌گرداند.
    INSERT #PM
    SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER,
            CAST(6 AS FLOAT), il.MEGHk
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 5
      AND   il.ANBARF IS NOT NULL
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG,
            -il.MEGHk
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

    -- ⚠️ پنجمین اصلاح: TAG این ردیف NULL نمی‌ماند — دقیقاً مثل UIIF در
    -- dbo.KA_KH باید TAG=18 (اضافه انبار) یا TAG=17 (کسری انبار) بگیرد،
    -- وگرنه در LEFT JOIN با TAGCOD هیچ tartib‌ای پیدا نمی‌کند، ISNULL آن
    -- را ۰ می‌گذارد، و همیشه اول همان روز پردازش می‌شود — حتی اگر واقعاً
    -- باید بعد از سند دیگری از همان روز بیاید (تأیید شد با گزارش واقعی
    -- کارت کالا: سند «اضافه انبار» زودتر از سند انتقالی-ورود همان روز
    -- پردازش می‌شد و مانده را کاذباً منفی نشان می‌داد).
    INSERT #PM
    SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT), ah.GRD_DATE, ah.GRD_NUM,
            CASE WHEN (al.MOG - ISNULL(al.NUM3, 0)) > 0 THEN CAST(18 AS FLOAT) ELSE CAST(17 AS FLOAT) END,
            -(al.MOG - ISNULL(al.NUM3, 0))
    FROM    dbo.ANBGRD_LST al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.N_S IS NOT NULL
      AND   ah.GRD_ANBAR IS NOT NULL
      AND   ((al.MOG - ISNULL(al.NUM3, 0)) * -1 <> 0)   -- مطابق dbo.KA_KH: ردیف بدون اختلاف واقعی حذف شود
      AND   ah.GRD_DATE BETWEEN @DT1 AND @DT2;

    -- TAG=3 برگشت خرید — از BACK_HEAD (ta=1)، در تاریخ واقعیِ خودِ
    -- برگشت، نه تاریخ سند خرید اصلی. مطابق dbo.KA_KH.
    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), bh.DATE_N, bh.NUMBER,
            CAST(3 AS FLOAT), -il.MEGH_MAR
    FROM    dbo.BACK_HEAD bh
    JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
    WHERE   bh.ta = 1
      AND   il.MEGH_MAR <> 0
      AND   bh.DATE_N BETWEEN @DT1 AND @DT2;

    -- TAG=4 برگشت فروش — از BACK_HEAD (ta=2)، همان منطق.
    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), bh.DATE_N, bh.NUMBER,
            CAST(4 AS FLOAT), il.MEGH_MAR
    FROM    dbo.BACK_HEAD bh
    JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
    WHERE   bh.ta = 2
      AND   il.MEGH_MAR <> 0
      AND   bh.DATE_N BETWEEN @DT1 AND @DT2;

    ;WITH DistinctAnbars AS (
        SELECT DISTINCT Anbar FROM #PM WHERE Anbar IS NOT NULL
    ),
    OpeningBackHeadFix AS (
        -- اصلاح مانده ابتدای دوره: MOGUDI مقدار برگشتی را در تاریخ سند
        -- اصلاح (نه تاریخ واقعی برگشت) کم/زیاد کرده. اگر سند اصلی قبل
        -- از @DT1 بوده ولی خودِ برگشت در @DT1 یا بعدش ثبت شده، آن اثر
        -- زودهنگام اینجا خنثی می‌شود تا در #PM (بالا) در تاریخ درست
        -- دوباره اعمال شود.
        SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code,
                SUM(CASE WHEN bh.ta = 1 THEN  il.MEGH_MAR
                         WHEN bh.ta = 2 THEN -il.MEGH_MAR
                    END) AS Fix
        FROM    dbo.BACK_HEAD bh
        JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
        JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
        WHERE   bh.ta IN (1, 2)
          AND   il.MEGH_MAR <> 0
          AND   hl.DATE_N < @DT1
          AND   bh.DATE_N >= @DT1
        GROUP BY il.ANBAR, TRY_CAST(il.CODE AS BIGINT)
    ),
    Opening AS (
        -- مانده ابتدای دوره از تابع مرجع کارت کالا، فقط برای جفت‌های
        -- (انبار، کالا) که واقعاً در همین دوره حرکت دارند.
        SELECT  m.ANBAR AS Anbar, TRY_CAST(m.CODE AS BIGINT) AS code,
                m.MAND + ISNULL(f.Fix, 0) AS OpeningBalance
        FROM    DistinctAnbars da
        CROSS   APPLY dbo.MOGUDI(@DT1 - 1, CAST(da.Anbar AS NVARCHAR(50))) m
        LEFT    JOIN OpeningBackHeadFix f
               ON f.Anbar = m.ANBAR AND f.code = TRY_CAST(m.CODE AS BIGINT)
    ),
    AllMovement AS (
        SELECT  o.Anbar, o.code, CAST(0 AS BIGINT) AS DATE_N, CAST(0 AS FLOAT) AS NUMBER,
                CAST(NULL AS FLOAT) AS TAG, CAST(0 AS INT) AS Tartib,
                CAST(o.OpeningBalance AS DECIMAL(18,6)) AS Meghdar
        FROM    Opening o
        WHERE   EXISTS (SELECT 1 FROM #PM p WHERE p.Anbar = o.Anbar AND p.code = o.code)

        UNION ALL
        SELECT  p.Anbar, p.code, p.DATE_N, p.NUMBER, p.TAG,
                ISNULL(tc.tartib, 0) AS Tartib, p.Meghdar
        FROM    #PM p
        LEFT    JOIN dbo.TAGCOD tc ON tc.CODE = p.TAG
        WHERE   p.Anbar IS NOT NULL AND p.code IS NOT NULL
    ),
    Tajamoi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Tartib,
                SUM(Meghdar) OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Tartib, NUMBER
                    ROWS UNBOUNDED PRECEDING) AS Mande
        FROM    AllMovement
    ),
    AvvalinManfi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Tartib, Mande,
                ROW_NUMBER() OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Tartib, NUMBER) AS rn
        FROM    Tajamoi
        WHERE   Mande < @Chk01Threshold
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
      AND   m.DATE_N BETWEEN @DT1 AND @DT2
      -- پذیرش دائمی (CC_AcceptedException) — عيناً همان مکانیزم
      -- CHK-03/CHK-04، به‌علاوه‌ی Anbar چون این کنترل روی جفت
      -- (انبار،کالا) کار می‌کند نه فقط کالا. چون این جدول مقید به
      -- RunId/ماه نیست، یک پذیرش هم دیگر در همین اجرا مسدود نمی‌کند
      -- هم در ماه‌های بعد دوباره ظاهر نمی‌شود.
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-01' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = m.Anbar)
                          AND (ae.Code  IS NULL OR ae.Code  = m.code));

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

       ⚠️ اصلاح دوم: مثل CHK-01، برگشت خرید (TAG=3) و برگشت فروش
       (TAG=4) هیچ‌جای این محاسبه نبودند — چون اصلاً روی HEAD_LST ثبت
       نمی‌شوند، بلکه از dbo.BACK_HEAD می‌آیند (نگاه کنید توضیح CHK-01
       بالا). برخلاف CHK-01، اینجا نیازی به حذف چیزی از شاخه‌های TAG=1/2
       نبود چون آن‌ها همیشه MABL_K کامل را بدون کسر MEGH_MAR گرفته‌اند؛
       فقط دو شاخه‌ی جدید اضافه شده.

       ⚠️ سومین اصلاح (بعد از تأیید کاربر با گزارش واقعی کارت کالا و
       سند حسابداری): موجودی اول دوره (dbo.STUF_FSK.MABL_A) اصلاً در
       KartAnbar جمع نمی‌شد — یعنی «فقط گردش» با «افتتاحیه + گردش»ِ
       حسابداری مقایسه می‌شد. برای هر کالایی که موجودی اول دوره‌ی
       غیرصفر دارد (نه فقط یک مورد خاص)، این دقیقاً به‌اندازه‌ی همان
       موجودی اول دوره مغایرت کاذب می‌ساخت. حالا یک شاخه‌ی جدید همان
       مبلغ را از STUF_FSK اضافه می‌کند — دقیقاً مثل Opening در CHK-01.
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
        /* ─────────────────────────────────────────────────────────
           چهارمین اصلاح (بعد از تأیید کاربر با dbo.MOGHA_ANBAR — تابع
           مرجعِ همین گزارش «مانده کارت انبار» که سیستم اصلی استفاده
           می‌کند): ارزش کارت انبار جمعِ خامِ MABL_K هر تراکنش نیست؛
           «مانده مقدار × آخرین نرخ میانگین ثبت‌شده» است.

           چرا فرق دارد: بازسازی نرخ میانگین (S07A) برای TAG=۲/۲۲/۲۴/۲۶
           (INVO_LST) و TAG مجازی ۳/۴ (BACK_HEAD) فقط ستون AVRAGE/AVRAGE2
           را به‌روز می‌کند — نه MABL_K/MABL خودِ ردیف را؛ این عیناً رفتار
           C0_TASK اصلی است (فقط TAG=۵/۹/۱۰/۱۱ و انبارگردانی MABL_K را هم
           بازنویسی می‌کنند). نتیجه: MABL_K روی ردیف فروش، ارزش لحظه‌ی صدور
           فاکتور را نگه می‌دارد، نه نرخ میانگینِ اصلاح‌شده — و سند
           حسابداری (GENSANADFROOSH و مشابه) همیشه AVRAGE تازه را پست
           می‌کند، نه MABL_K را. جمع‌زدن MABL_K خام برای «کارت انبار» پس
           با گذر زمان (و هر بار که S07A نرخ را عوض می‌کند) از حسابداری
           واگرا می‌شود، حتی وقتی هیچ‌کدام واقعاً اشتباه نیستند.

           راه‌حل mirror شده از dbo.MOGHA_ANBAR: فقط «مقدار» هر تراکنش جمع
           زده می‌شود (نه مبلغ)؛ مبلغ نهایی = مانده‌مقدار × آخرین AVRAGE/
           AVRAGE2 ثبت‌شده روی این (کالا، انبار) تا @DT2 — با همان ترتیب
           tie-break کد اصلی (DATE_N، سپس TAGCOD.tartib، سپس NUMBER؛ عیناً
           BARGAH اصلی ولی با tartib عددی به‌جای متن، به همان دلیلی که
           برای CHK-01 قبلاً تصحیح شد) و بازگشت به STUF_FSK.FI_A وقتی هیچ
           تراکنشی ثبت نشده.

           روی کد ۳۳۰۱/انبار ۲ تست شد: مانده مقدار ۲۰۸۰ × آخرین نرخ
           ۳,۲۹۲,۹۸۶.۷۴ = ۶,۸۴۹,۴۱۲,۴۱۴ — دقیقاً برابر سمت حسابداری.
           ───────────────────────────────────────────────────────── */
        ;WITH AnbarQtyMovement AS (
            SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code, il.MEGHk AS Meg
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (1, 7, 9, 24)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), il.MEGH_MAR
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 22
              AND   hl.DATE_N <= @DT2
              AND   il.MEGH_MAR <> 0
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.MEGHk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 5
              AND   il.ANBARF IS NOT NULL
              AND   hl.DATE_N <= @DT2
              AND   CAST(il.ANBARF AS INT) IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGHk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (2, 5, 8, 10, 11, 26)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGHk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 20
              AND   (hl.TAMIR = 1 OR hl.TAMIR = 4)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT),
                    -(al.MOG - ISNULL(al.NUM3, 0))
            FROM    dbo.ANBGRD_LST al
            JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
            WHERE   ah.N_S IS NOT NULL
              AND   ah.GRD_DATE <= @DT2
              AND   ah.GRD_ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            -- TAG=3 برگشت خرید (از BACK_HEAD؛ کالا از انبار خارج می‌شود)
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGH_MAR
            FROM    dbo.BACK_HEAD bh
            JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
            WHERE   bh.ta = 1
              AND   il.MEGH_MAR <> 0
              AND   bh.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            -- TAG=4 برگشت فروش (از BACK_HEAD؛ کالا به انبار برمی‌گردد)
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), il.MEGH_MAR
            FROM    dbo.BACK_HEAD bh
            JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
            WHERE   bh.ta = 2
              AND   il.MEGH_MAR <> 0
              AND   bh.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            -- موجودی اول دوره (فقط مقدار؛ ارزش از آخرین نرخ میانگین می‌آید)
            SELECT  f.ANBAR, TRY_CAST(f.CODE AS BIGINT), f.MOGODI_A
            FROM    dbo.STUF_FSK f
            WHERE   f.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)
        ),
        AnbarQty AS (
            SELECT  Anbar, code, SUM(Meg) AS Mand
            FROM    AnbarQtyMovement
            WHERE   Anbar IS NOT NULL AND code IS NOT NULL
            GROUP BY Anbar, code
        ),
        LastAvgSource AS (
            -- عیناً dbo.MOGHA_ANBAR.lastav_base: AVRAGE برای TAG۱/۷/۹/۲۴
            -- (ورود مستقیم)، AVRAGE2 برای TAG=۵ مقصد (ورود از انتقالی).
            SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code, il.AVRAGE AS Rate,
                    hl.DATE_N, t.tartib, il.NUMBER, il.ID
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON il.NUMBER = hl.NUMBER AND il.TAG = hl.TAG
            JOIN    dbo.TAGCOD t ON il.TAG = t.CODE
            WHERE   hl.DATE_N <= @DT2
              AND   il.TAG IN (1, 7, 9, 24)
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.AVRAGE2,
                    hl.DATE_N, t.tartib, il.NUMBER, il.ID
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON il.NUMBER = hl.NUMBER AND il.TAG = hl.TAG
            JOIN    dbo.TAGCOD t ON il.TAG = t.CODE
            WHERE   hl.DATE_N <= @DT2
              AND   il.TAG = 5
              AND   il.ANBARF IS NOT NULL
              AND   CAST(il.ANBARF AS INT) IN (SELECT Anbar FROM dbo.CC_AnbarHes)
        ),
        -- وقتی یک سند، یک کالا را در چند ردیف با نرخ‌های متفاوت ثبت کرده
        -- (مثلاً دو محموله‌ی هم‌روز با نرخ فرق)، این ردیف‌ها در
        -- (DATE_N،tartib،NUMBER) کاملاً هم‌تراز می‌شوند. قبلاً با
        -- ROW_NUMBER() بدون تای‌برک نهایی، یکی‌شان دلبخواهی انتخاب می‌شد —
        -- نتیجه‌ی CHK-02 بدون هیچ تغییری در داده، بین دو اجرای پشت‌سرهم
        -- عوض می‌شد (روی کد ۳۰۹۲/انبار۳: سند ۸۹۱ دو ردیف دارد — ۱۱ واحد
        -- با نرخ ۱,۵۸۰,۳۲۹ روی id کوچک‌تر، ۳۴۷ واحد با نرخ ۱,۷۰۰,۱۴۰ روی
        -- id بزرگ‌تر).
        --
        -- id DESC (ردیفی که آخر نوشته شده) درست است، نه ASC: نرخِ روی
        -- id بزرگ‌تر (۱,۷۰۰,۱۴۰) دقیقاً همان نرخی است که تمام اسناد
        -- انتقالِ *بعد* از سند ۸۹۱ (AVRAGE2شان) استفاده کرده‌اند —
        -- یعنی موتور نرخ میانگین، بعد از پردازش هر دو ردیفِ همین سند به
        -- ترتیب، روی همین عدد نهایی نشسته. با id DESC، مانده‌ی کارت
        -- انبار دقیقاً با حسابداری برابر شد (۱۰,۱۶۴,۳۸۹,۴۷۱ = ۱۰,۱۶۴,۳۸۹,۴۷۱،
        -- تا ریال). جالب این‌جاست که dbo.MOGHA_ANBAR (مرجع رسمی گزارش
        -- کارت انبار) هم همین باگ را دارد — بدون تای‌برک، این‌جا id
        -- کوچک‌تر را برمی‌گرداند و ۷۰۰+ میلیون مغایرت کاذب می‌سازد؛ فقط
        -- چون تا حالا این حالت (چند ردیف هم‌سند با نرخ فرق) به‌ندرت پیش
        -- اومده کسی متوجه نشده بود.
        LastAvgRanked AS (
            SELECT  Anbar, code, Rate,
                    ROW_NUMBER() OVER (PARTITION BY Anbar, code
                                        ORDER BY DATE_N DESC, tartib DESC, NUMBER DESC, ID DESC) AS rn
            FROM    LastAvgSource
        ),
        KartAnbar AS (
            SELECT  q.Anbar, q.code,
                    ROUND(q.Mand, 2) * ISNULL(la.Rate, f.FI_A) AS Mande
            FROM    AnbarQty q
            LEFT    JOIN LastAvgRanked la ON la.Anbar = q.Anbar AND la.code = q.code AND la.rn = 1
            LEFT    JOIN dbo.STUF_FSK  f  ON f.ANBAR = q.Anbar AND TRY_CAST(f.CODE AS BIGINT) = q.code
        ),
        Hesabdari AS (
            SELECT  am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code,
                    SUM(d.BED) - SUM(d.BES) AS Mande
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED  h  ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   h.DATE_S <= @DT2
            GROUP BY am.Anbar, TRY_CAST(d.HES_T AS BIGINT)
        ),
        -- تشخیص خودکارِ علتِ محتمل، تا اپراتور مجبور نباشد دستی SQL بزند:
        -- کدهایی که STUF_FSK موجودی اول دوره‌ی غیرصفر دارند ولی هیچ سند
        -- افتتاحیه‌ای زیر همان حساب (کل/معین/تفصیلی) در حسابداری ثبت
        -- نشده — دقیقاً همان الگویی که روی کد ۳۱۰۰/انبار۴ پیدا شد.
        -- این حالت را نمی‌شود خودکار اصلاح کرد (فقط تیم انبار/حسابداری
        -- می‌داند آن موجودی واقعی بوده یا نه)، پس فقط توضیح داده می‌شود.
        MissingOpening AS (
            SELECT DISTINCT am.Anbar, TRY_CAST(f.CODE AS BIGINT) AS code
            FROM    dbo.STUF_FSK f
            JOIN    dbo.CC_AnbarHes am ON am.Anbar = f.ANBAR
            WHERE   f.MOGODI_A <> 0
              AND   NOT EXISTS (
                        SELECT 1 FROM dbo.DEED_DTL d
                        JOIN   dbo.DEED_HED h ON h.N_S = d.N_S
                        WHERE  d.HES_K = am.HesKol AND d.HES_M = am.HesMoin
                          AND  TRY_CAST(d.HES_T AS BIGINT) = TRY_CAST(f.CODE AS BIGINT)
                          AND  d.SHARH LIKE N'%افتتاحيه%'
                    )
        ),
        -- جهتِ معکوسِ MissingOpening: سند افتتاحیه در حسابداری هست ولی
        -- کاردکس برای همان کالا/انبار موجودی اول دوره ندارد (MOGODI_A=0
        -- یا اصلاً ردیفی در STUF_FSK نیست). تا امروز فقط جهتِ اول تشخیص
        -- داده می‌شد و این حالت بدون هیچ توضیحی گزارش می‌شد.
        --
        -- روی داده‌ی واقعی (فروردین ۱۴۰۵، انبار ۸۰۷ «انبار محصول یزد»)
        -- سه کالا دقیقاً همین وضع را داشتند و مبلغ مغایرت مو‌به‌مو برابر
        -- سند افتتاحیه بود:
        --     ۲۸۸۲ → ۱۲,۶۰۷,۲۴۰   ۳۱۴۲ → ۲۵,۱۱۷,۰۶۸   ۳۳۴۲ → ۲۹,۲۷۹,۸۱۷
        -- مثل جهتِ اول، این هم خودکار قابل اصلاح نیست: فقط انبار/حسابداری
        -- می‌داند کدام سمت درست است.
        ExtraOpening AS (
            SELECT  DISTINCT am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   d.SHARH LIKE N'%افتتاحيه%'
              AND   NOT EXISTS (
                        SELECT 1 FROM dbo.STUF_FSK f
                        WHERE  f.ANBAR = am.Anbar
                          AND  TRY_CAST(f.CODE AS BIGINT) = TRY_CAST(d.HES_T AS BIGINT)
                          AND  f.MOGODI_A <> 0
                    )
        )
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
        SELECT  @RunId, 'S05', 'CHK-02', 2, 2,
                ISNULL(k.Anbar, hh.Anbar), ISNULL(k.code, hh.code),
                ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0),
                -- ⚠ علت، *اول* جمله می‌آید نه آخرش. قبلاً کلمه‌ی «افتتاحیه»
                -- ته یک جمله‌ی بلند بود و کاربر باید تا انتها می‌خواند تا
                -- بفهمد این مغایرت اصلاً از گردش ماه نیست. حالا اولین چیزی
                -- که بعد از نام انبار دیده می‌شود همین است.
                CONCAT(N'انبار ', ISNULL(k.Anbar, hh.Anbar),
                       N' (', ISNULL(a.NAMES, N'نامشخص'), N'): ',
                       CASE WHEN mo.code IS NOT NULL OR eo.code IS NOT NULL
                            THEN N'⚠ مغایرت مربوط به افتتاحیه است، نه گردش این ماه. '
                            ELSE N'' END,
                       N'کارت انبار ', FORMAT(ISNULL(k.Mande, 0), 'N0'),
                       N' در برابر حسابداری ', FORMAT(ISNULL(hh.Mande, 0), 'N0'),
                       CASE WHEN mo.code IS NOT NULL
                            THEN N' — موجودی اول دوره در کاردکس ثبت شده (STUF_FSK) ولی سند افتتاحیهٔ آن هرگز در حسابداری صادر نشده. تصمیم با انبار/حسابداری است؛ بازسازی خودکار درستش نمی‌کند.'
                            WHEN eo.code IS NOT NULL
                            THEN N' — سند افتتاحیه در حسابداری صادر شده ولی کاردکس موجودی اول دوره‌ای ندارد (STUF_FSK صفر است). مبلغ مغایرت معمولاً دقیقاً برابر همان سند افتتاحیه است. تصمیم با انبار/حسابداری است که کدام سمت درست است؛ بازسازی خودکار درستش نمی‌کند.'
                            ELSE N'' END)
        FROM    KartAnbar k
        FULL    OUTER JOIN Hesabdari hh ON hh.Anbar = k.Anbar AND hh.code = k.code
        LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = ISNULL(k.Anbar, hh.Anbar)
        LEFT    JOIN MissingOpening mo ON mo.Anbar = ISNULL(k.Anbar, hh.Anbar) AND mo.code = ISNULL(k.code, hh.code)
        LEFT    JOIN ExtraOpening   eo ON eo.Anbar = ISNULL(k.Anbar, hh.Anbar) AND eo.code = ISNULL(k.code, hh.code)
        WHERE   ABS(ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0)) > 1
          -- پذیرش دائمی — نگاه کنید توضیح بالای CHK-01. برای همین دلیل
          -- این‌جا هم اضافه شد: مورد شناخته‌شده‌ی «موجودی اول دوره سند
          -- افتتاحیه ندارد» (MissingOpening) دقیقاً همان چیزی است که
          -- معمولاً با این دکمه پذیرفته می‌شود.
          AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                            WHERE ae.RuleCode = 'CHK-02' AND ae.IsActive = 1
                              AND (ae.Anbar IS NULL OR ae.Anbar = ISNULL(k.Anbar, hh.Anbar))
                              AND (ae.Code  IS NULL OR ae.Code  = ISNULL(k.code, hh.code)));
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
