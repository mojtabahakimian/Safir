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
       CHK-01 — کاردکس منفی

       موجودی تجمعی هر کالا در هر انبار به ترتیب تاریخ محاسبه و
       هر جا منفی شد علامت می‌خورد. معمولاً یعنی تاریخ رسید بعد از
       تاریخ حواله ثبت شده است.

       فقط اولین نقطه منفی هر کالا/انبار گزارش می‌شود؛ بقیه
       دنباله همان یک مشکل‌اند و فهرست را شلوغ می‌کنند.
       ───────────────────────────────────────────────────────────── */
    ;WITH Harekat AS (
        SELECT  k.ANBAR,
                k.code,
                k.DATE_N,
                k.NUMBER,
                k.TAG,
                CASE WHEN k.TAG IN (1, 7, 9, 24) THEN k.MEGHk ELSE -k.MEGHk END AS Meghdar
        FROM    dbo.KALAS k
        WHERE   k.DATE_N <= @DT2
          AND   k.MEGHk <> 0
    ),
    Tajamoi AS (
        SELECT  ANBAR, code, DATE_N, NUMBER, TAG,
                SUM(Meghdar) OVER (
                    PARTITION BY ANBAR, code
                    ORDER BY DATE_N, NUMBER
                    ROWS UNBOUNDED PRECEDING) AS Mande
        FROM    Harekat
    ),
    AvvalinManfi AS (
        SELECT  ANBAR, code, DATE_N, NUMBER, TAG, Mande,
                ROW_NUMBER() OVER (
                    PARTITION BY ANBAR, code
                    ORDER BY DATE_N, NUMBER) AS rn
        FROM    Tajamoi
        WHERE   Mande < -0.0001
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocTag, DocDate, Amount, Description)
    SELECT  @RunId, 'S05', 'CHK-01', 1, 2,
            m.ANBAR, m.code, m.NUMBER, m.TAG, m.DATE_N, m.Mande,
            CONCAT(N'موجودی در تاریخ ',
                   m.DATE_N / 10000, '/',
                   FORMAT(m.DATE_N / 100 % 100, '00'), '/',
                   FORMAT(m.DATE_N % 100, '00'),
                   N' منفی می‌شود')
    FROM    AvvalinManfi m
    WHERE   m.rn = 1
      AND   m.DATE_N BETWEEN @DT1 AND @DT2;

    /* ─────────────────────────────────────────────────────────────
       CHK-02 — مغایرت کارت انبار و حسابداری

       مانده ریالی کارت انبار (KALAS) با مانده حساب موجودی جنسی
       (۱۲۱) مقایسه می‌شود. اختلاف معمولاً یعنی حواله‌ای که فاکتورش
       صادر نشده، یا تاریخ فاکتور در ماه بعد افتاده.

       آستانه یک ریال است چون این دو باید دقیقاً یکی باشند.
       ───────────────────────────────────────────────────────────── */
    ;WITH KartAnbar AS (
        SELECT  k.code,
                SUM(CASE WHEN k.TAG IN (1, 7, 9, 24)
                         THEN k.MABL_K ELSE -k.MABL_K END) AS Mande
        FROM    dbo.KALAS k
        WHERE   k.DATE_N <= @DT2
        GROUP BY k.code
    ),
    Hesabdari AS (
        SELECT  TRY_CAST(d.HES_T AS BIGINT) AS code,
                SUM(d.BED) - SUM(d.BES)     AS Mande
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.HES_K = 121
          AND   h.DATE_S <= @DT2
        GROUP BY TRY_CAST(d.HES_T AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S05', 'CHK-02', 2, 2,
            ISNULL(k.code, hh.code),
            ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0),
            CONCAT(N'کارت انبار ', FORMAT(ISNULL(k.Mande, 0), 'N0'),
                   N' در برابر حسابداری ', FORMAT(ISNULL(hh.Mande, 0), 'N0'))
    FROM    KartAnbar k
    FULL    OUTER JOIN Hesabdari hh ON hh.code = k.code
    WHERE   ABS(ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0)) > 1;

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
