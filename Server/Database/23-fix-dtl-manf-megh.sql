/* ═══════════════════════════════════════════════════════════════════
   همگام‌سازی DTL_MANF.MEGH («مقدار») با MEGHk («مقدار کل»)

   ── قرارداد ──
   فرم فرمولِ نرم‌افزار قدیمی این رابطه را نگه می‌دارد:

       MEGHk = MEGH * VAHEDS.NESBAT        (نسبت واحد ردیف به واحد اصلی)
       MABLK = (PERT + MEGHk) * SMABL

   ── چه چیزی خراب شده بود ──
   گام‌های بستن ماه و ابزار «جابه‌جایی مصرف ماده بین فرمول‌ها» فقط MEGHk را
   می‌نوشتند و MEGH را دست‌نخورده می‌گذاشتند، چون S11 برای بهای تمام‌شده
   همان MEGHk را می‌خواند. ولی MEGH مرده نیست: در S07
   (17-variance-steps.sql) حواله‌ی خروج مواد از روی هر دو ساخته می‌شود —

       INVO_LST.MEGH  = (dm.MEGH  + dm.PERT) * مقدار توليد
       INVO_LST.MEGHK = (dm.MEGHK + dm.PERT) * مقدار توليد

   پس مقدارِ فیزیکیِ ثبت‌شده در حواله‌ها با مبنای بها نمی‌خواند و کاربر در
   فرم فرمول ستون «مقدار» را کهنه می‌بیند. انحراف بین اجراها انباشته می‌شد.

   ── جهت اصلاح ──
   MEGHk مرجع است (تأیید صاحب پروژه): تنظیماتی که S09/S11 و بستن ماه روی
   MEGHk نوشته‌اند درست‌اند و نباید برگردند. پس

       MEGH := MEGHk / NESBAT

   نه برعکس. بازمحاسبه‌ی MEGHk از روی MEGH همه‌ی آن تنظیمات را پاک می‌کرد.

   ── دامنه ──
   ردیف‌هایی که نسبت واحدشان در VAHEDS تعریف نشده کنار گذاشته می‌شوند —
   بدون نسبت، «مقدار» قابل استخراج نیست و نوشتن عدد حدسی بدتر از نساختن
   آن است. تعدادشان در گزارش پایان اسکریپت می‌آید.

   ⚠ پیش از هر تغییر، ردیف‌های متأثر در CC_BAK_DTL_MANF_MEGHFIX نگهداری
   می‌شوند تا برگرداندن ممکن بماند.

   ⚠ پس از اجرا باید S07 دوباره اجرا شود تا حواله‌های خروج مواد با مقدار
   اصلاح‌شده بازتولید شوند؛ وگرنه DTL_MANF درست است ولی INVO_LST کهنه.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب فرق
   می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
SET XACT_ABORT ON
GO

BEGIN TRAN;

/* ── ۱) پشتیبان ────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.CC_BAK_DTL_MANF_MEGHFIX','U') IS NOT NULL
    DROP TABLE dbo.CC_BAK_DTL_MANF_MEGHFIX;

SELECT  d.ID, d.FNUMB, d.CODE, d.ANBAR, d.VAHED_K,
        d.MEGH  AS MEGH_Old,
        d.MEGHk AS MEGHk_Old,
        d.PERT, d.SMABL, d.MABLK AS MABLK_Old,
        vv.NESBAT,
        SYSUTCDATETIME() AS BackedUpAtUtc
INTO    dbo.CC_BAK_DTL_MANF_MEGHFIX
FROM    dbo.DTL_MANF d
JOIN    dbo.VAHEDS vv
        ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
       AND vv.VAHED = d.VAHED_K
WHERE   vv.NESBAT <> 0
  AND   ABS(d.MEGHk - d.MEGH * vv.NESBAT) > 1e-9;

DECLARE @affected INT = @@ROWCOUNT;

/* ── ۲) اصلاح ──────────────────────────────────────────────────── */
UPDATE  d
   SET  d.MEGH = d.MEGHk / vv.NESBAT
FROM    dbo.DTL_MANF d
JOIN    dbo.VAHEDS vv
        ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
       AND vv.VAHED = d.VAHED_K
WHERE   vv.NESBAT <> 0
  AND   ABS(d.MEGHk - d.MEGH * vv.NESBAT) > 1e-9;

COMMIT;
GO

/* ── ۳) گزارش ──────────────────────────────────────────────────── */
SELECT  (SELECT COUNT(*) FROM dbo.CC_BAK_DTL_MANF_MEGHFIX) AS اصلاح_شد,

        (SELECT COUNT(*)
         FROM   dbo.DTL_MANF d
         JOIN   dbo.VAHEDS vv
                ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
               AND vv.VAHED = d.VAHED_K
         WHERE  vv.NESBAT <> 0
           AND  ABS(d.MEGHk - d.MEGH * vv.NESBAT) > 1e-9) AS باقيمانده_ناهماهنگ,

        (SELECT COUNT(*)
         FROM   dbo.DTL_MANF d
         LEFT   JOIN dbo.VAHEDS vv
                ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
               AND vv.VAHED = d.VAHED_K
         WHERE  vv.NESBAT IS NULL OR vv.NESBAT = 0) AS بدون_نسبت_واحد;
GO

PRINT N'اسکریپت 23-fix-dtl-manf-megh.sql اجرا شد. پشتیبان: CC_BAK_DTL_MANF_MEGHFIX';
PRINT N'⚠ اکنون S07 را دوباره اجرا کنید تا حواله‌های خروج مواد بازتولید شوند.';
GO
