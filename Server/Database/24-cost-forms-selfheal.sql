/* ═══════════════════════════════════════════════════════════════════
   خودترمیمیِ فرم‌ها و دسترسی‌های ماژول بهای تمام‌شده

   ── مسئله‌ای که این اسکریپت حل می‌کند ──
   ۱. هر بار فرم تازه‌ای به Shared/Constants/CostForms.cs اضافه می‌شود، باید
      ردیفش در TFORMS هم ساخته شود. تا امروز این کار با ۲۱ بلوک تکراریِ
      «IF NOT EXISTS … INSERT» در 11-seed-data.sql انجام می‌شد؛ جا افتادنِ
      یکی از آن‌ها هیچ خطایی نمی‌دهد، فقط آن قابلیت بی‌صدا برای همه قفل
      می‌شود.

   ۲. مهم‌تر: ساختنِ فرم در TFORMS به‌تنهایی کافی نیست. کاربری که از قبل به
      ماژول دسترسی داشته، روی فرمِ تازه هیچ ردیفی در SAL_CHEK ندارد و با
      ACL_ENFORCE=1 پاسخِ ۴۰۳ می‌گیرد — بدون اینکه بفهمد چرا. نمونه‌ی
      واقعی: کاربر ۱۱۴ روی YAZDSEPAR1405 به COST_ACT_FIX_DATE و
      COST_ACT_RESOLVE_PERMANENT دسترسی نداشت، چون آن دو فرم بعد از
      تنظیم دسترسی‌های او اضافه شده بودند.

   ── قاعده‌ی اعطای خودکار ──
   COST_DASHBOARD «فرمِ ورودیِ» ماژول است. هر کاربری که روی آن ردیف دارد،
   کاربرِ این ماژول شمرده می‌شود و هر فرمِ COST_ که ردیفش را ندارد با
   *همان* سطح دسترسیِ COST_DASHBOARD برایش ساخته می‌شود — نه بیشتر.
   کاربری که COST_DASHBOARD ندارد اصلاً دست نمی‌خورد، پس این اسکریپت به
   هیچ‌کس دسترسیِ تازه‌ای نمی‌دهد که از قبل نداشته باشد.

   اجرای دوباره بی‌خطر است: هرچه از قبل درست باشد دست‌نخورده می‌ماند.

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

/* ── ۱) فهرست مرجع فرم‌ها — باید با CostForms.cs یکی بماند ────────── */
DECLARE @Forms TABLE (FormName NVARCHAR(100) PRIMARY KEY, Caption NVARCHAR(200));

INSERT INTO @Forms (FormName, Caption) VALUES
    (N'COST_DASHBOARD',             N'داشبورد بستن ماه بهای تمام‌شده'),
    (N'COST_RUN',                   N'پیشرفت اجرای بستن ماه'),
    (N'COST_EXCEPTIONS',            N'مغایرت‌های بستن ماه'),
    (N'COST_VARIANCE',              N'تصمیم انحراف'),
    (N'COST_CONVERSION',            N'هزینه تبدیل'),
    (N'COST_MARGIN',                N'سود و زیان کالا'),
    (N'COST_HISTORY',               N'سوابق اجراها'),
    (N'COST_SETTINGS',              N'تنظیمات بستن ماه'),
    (N'COST_ACT_START',             N'شروع اجرای بستن ماه'),
    (N'COST_ACT_AUTOFIX',           N'اصلاح خودکار داده'),
    (N'COST_ACT_RESOLVE',           N'بستن استثنا'),
    (N'COST_ACT_DECIDE',            N'ثبت تصمیم انحراف'),
    (N'COST_ACT_APPLY_RATE',        N'اعمال ضریب تعدیل'),
    (N'COST_ACT_ROLLUP',            N'اجرای موتور نرخ'),
    (N'COST_ACT_ROLLBACK',          N'بازگردانی از اسنپ‌شات'),
    (N'COST_ACT_APPROVE',           N'تأیید نهایی و قفل ماه'),
    (N'COST_ACT_EXPORT',            N'خروجی اکسل'),
    (N'COST_ACT_REBUILD_DOCS',      N'بازسازی سند حواله خروج مواد'),
    (N'COST_ACT_POST_CORRECTION',   N'سند اصلاحی مغایرت کارت انبار/حسابداری'),
    (N'COST_ACT_RESOLVE_PERMANENT', N'پذیرش دائمی مغایرت'),
    (N'COST_ACT_FIX_DATE',          N'اصلاح تاریخ مغایرِ سند');

/* ── ۲) فرم‌های جاافتاده را به TFORMS اضافه کن ───────────────────────
   IDH با ROW_NUMBER تخصیص می‌یابد، نه با MAX(IDH)+1 داخل یک INSERT
   چندسطری — آن شکل به همه‌ی سطرها یک شناسه‌ی یکسان می‌دهد. */
DECLARE @maxIdh INT = (SELECT ISNULL(MAX(IDH), 0) FROM dbo.TFORMS);

INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
SELECT  f.FormName, f.Caption, 3, 10,
        @maxIdh + ROW_NUMBER() OVER (ORDER BY f.FormName),
        GETDATE()
FROM    @Forms f
WHERE   NOT EXISTS (SELECT 1 FROM dbo.TFORMS t WHERE t.FORMNAME = f.FormName);

DECLARE @formsAdded INT = @@ROWCOUNT;

/* ── ۳) عنوانِ فرم‌های موجود را با فهرست مرجع هم‌راستا کن ──────────── */
UPDATE  t
   SET  t.CAPTION = f.Caption
FROM    dbo.TFORMS t
JOIN    @Forms f ON f.FormName = t.FORMNAME
WHERE   ISNULL(t.CAPTION, N'') <> f.Caption;

DECLARE @captionsFixed INT = @@ROWCOUNT;

/* ── ۴) دسترسی‌های جاافتاده را برای کاربرانِ همین ماژول بساز ────────
   سطح دسترسی از COST_DASHBOARD همان کاربر کپی می‌شود. */
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], CRT)
SELECT  d.USERCO, t.IDH, d.[RUN], d.[SEE], d.[INP], d.[UPD], d.[DEL], GETDATE()
FROM    dbo.SAL_CHEK d
JOIN    dbo.TFORMS dash ON dash.IDH = d.[OBJECT]
                       AND dash.FORMNAME = N'COST_DASHBOARD'
CROSS   JOIN dbo.TFORMS t
JOIN    @Forms f ON f.FormName = t.FORMNAME
WHERE   NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK sc
                    WHERE sc.USERCO = d.USERCO AND sc.[OBJECT] = t.IDH);

DECLARE @permsAdded INT = @@ROWCOUNT;

COMMIT;
GO

/* ── ۵) گزارش ──────────────────────────────────────────────────── */
SELECT  (SELECT COUNT(*) FROM dbo.TFORMS WHERE FORMNAME LIKE 'COST[_]%') AS فرم_موجود,

        (SELECT COUNT(*)
         FROM   dbo.SAL_CHEK sc
         JOIN   dbo.TFORMS t ON t.IDH = sc.[OBJECT]
         WHERE  t.FORMNAME = N'COST_DASHBOARD')                          AS کاربر_ماژول,

        (SELECT COUNT(*)
         FROM   dbo.SAL_CHEK d
         JOIN   dbo.TFORMS dash ON dash.IDH = d.[OBJECT]
                               AND dash.FORMNAME = N'COST_DASHBOARD'
         CROSS  JOIN dbo.TFORMS t
         WHERE  t.FORMNAME LIKE 'COST[_]%'
           AND  NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK sc
                            WHERE sc.USERCO = d.USERCO
                              AND sc.[OBJECT] = t.IDH))                  AS دسترسي_جاافتاده;
GO

PRINT N'اسکریپت 24-cost-forms-selfheal.sql اجرا شد.';
GO
