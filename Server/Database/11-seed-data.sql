/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۲ از ۳ : داده اولیه

   قواعد تشخیص، واحدهای تولیدی، و استثناهای پذیرفته‌شده.
   قابل اجرای مکرر.

   ⚠ بخش واحدهای تولیدی را بر اساس واقعیت کارخانه ویرایش کنید.
     مقادیر فعلی نمونه‌اند و از گزارش موجودی خودتان استخراج شده‌اند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

/* ───────────────────────── قواعد تشخیص ───────────────────────── */

MERGE dbo.CC_CheckRule AS t
USING (VALUES
 ('CHK-01', N'کاردکس منفی', 'S05', 1, 2, -0.001,
  N'تاریخ رسید یا حواله را جابه‌جا کنید تا موجودی در هیچ لحظه‌ای منفی نشود.', 10),

 ('CHK-02', N'مغایرت کارت انبار و حسابداری', 'S05', 2, 2, NULL,
  N'معمولاً حواله‌ای است که فاکتورش صادر نشده، یا تاریخ فاکتور در ماه بعد افتاده. تاریخ‌ها را یکسان کنید.', 20),

 ('CHK-03', N'فرمول بدون نرخ جذب هزینه تبدیل', 'S00', 9, 1, NULL,
  N'در فرمول، «جذب هزینه دستمزد» را پر کنید. اگر عمداً صفر است (محصول فرعی مانند آب پنیر خالص)، آن را در فهرست استثناهای پذیرفته‌شده ثبت کنید تا دیگر هشدار ندهد.', 30),

 ('CHK-04', N'کالای تولیدشده بدون فرمول ماه', 'S00', 12, 2, NULL,
  N'نسخه ماه جاری فرمول ساخته نشده است. با «کپی فرمول» نسخه ماه را بسازید.', 40),

 ('CHK-05', N'ماده بدون منبع نرخ', 'S00', 4, 1, NULL,
  N'این ماده نه فرمول دارد و نه گردش خروج در ماه، بنابراین نرخش صفر می‌ماند و صفر را به همه کالاهای بالادست منتقل می‌کند. یک نرخ برایش تعیین کنید.', 50),

 ('CHK-06', N'حلقه در ساختار فرمول', 'S00', 5, 2, NULL,
  N'کالا مستقیم یا غیرمستقیم خودش را مصرف می‌کند. تا این حلقه شکسته نشود، محاسبه نرخ ممکن نیست.', 60),

 ('CHK-07', N'مانده نامتوازن مواد در حساب ۷۵۱', 'S00', 13, 1, 0.001,
  N'اگر یک طرف صفر باشد، حواله جا افتاده است. آستانه یک در هزار است؛ کمتر از آن گِردکردن طبیعی است و نیاز به اقدام ندارد.', 70),

 ('CHK-08', N'اختلاف جذب برگه تولید با سند', 'S10', 10, 1, NULL,
  N'سند حسابداری وقتی صادر شده که فرمول نرخ دیگری داشته است. برگه تولید را بازسازی کنید.', 80),

 ('CHK-09', N'نرخ منتشرنشده نیمه‌ساخته', 'S11', 14, 2, 0.001,
  N'بهای خودِ فرمول این کالا با نرخی که در فرمول کالاهای بالادست دارد نمی‌خواند؛ یعنی انتشار نرخ کامل نشده. پس از اجرای کامل محاسبه نرخ، این قاعده باید صفر شود.', 90),

 ('CHK-10', N'مانده حساب کالای در جریان ساخت', 'S10', 8, 1, 10000000,
  N'فرض «کالای در جریان ساخت صفر» نقض شده است. آستانه ده میلیون ریال تنظیم شده تا باقیمانده گِردکردن هشدار کاذب ندهد.', 100),

 ('CHK-11', N'انحراف روی ماده مصرف‌نشده', 'S09', 11, 1, NULL,
  N'این ماده در هیچ فرمولی مصرف نشده ولی انحراف دارد. برگه انتقال یا انبارِ انبارگردانی را بررسی کنید.', 110),

 ('CHK-12', N'فرمول مقصد ماه قبل موجود نیست', 'S09', 15, 1, NULL,
  N'تصمیم ماه قبل قابل ادامه نیست چون کالای مقصد امسال فرمول ندارد. پیش‌فرض روی تسهیم به نسبت مصرف قرار گرفت.', 120),

 ('CHK-13', N'حواله با مقدار صفر', 'S07', 16, 2, NULL,
  N'ماده در فرمول مقدار دارد ولی حواله‌اش با مقدار صفر صادر شده؛ یعنی فرمول پس از صدور حواله ویرایش شده است. خروج مواد باید بازسازی شود.', 130),

 ('CHK-15', N'فرمول با مقدار منفی', 'S00', 17, 2, NULL,
  N'مقدار منفی در یک سطر فرمول قابل قبول نیست و باعث می‌شود مانده حساب کالای در جریان ساخت (۷۵۱) هرگز متوازن نشود. با دکمه اصلاح، آن سطر را صفر یا حذف کنید.', 75),

 ('CHK-16', N'برگه تولید به انبار بدون واحد تعریف‌شده', 'S00', 18, 1, NULL,
  N'این انبار را در تنظیمات، به تعریف واحدهای تولیدی (نقش «محصول») اضافه کنید — وگرنه هزینه تبدیل این برگه‌ها در هیچ واحدی جذب نمی‌شود و مانده حساب ۷۵۱ کاذب می‌شود.', 45)
) AS s (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
ON t.RuleCode = s.RuleCode
-- ⚠️ Threshold عمداً از WHEN MATCHED بیرون است: کاربر می‌تواند از تنظیمات
-- برنامه آستانه‌ی هر قاعده را عوض کند (مثلاً CHK-01)؛ اگر این Seed دوباره
-- اجرا شود، نباید آن تنظیم دستی را با مقدار پیش‌فرض پاک کند. Threshold
-- فقط در INSERT اولیه (ردیف جدید) از مقدار پیش‌فرض بالا پر می‌شود.
WHEN MATCHED THEN UPDATE SET
    t.RuleName = s.RuleName, t.StepCode = s.StepCode, t.ExType = s.ExType,
    t.DefaultSeverity = s.DefaultSeverity,
    t.RemedyText = s.RemedyText, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT
    (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
    VALUES (s.RuleCode, s.RuleName, s.StepCode, s.ExType, s.DefaultSeverity,
            s.Threshold, s.RemedyText, s.SortOrder);
GO


/* ───────────────────────── استثنای پذیرفته‌شده ─────────────────────────
   آب پنیر خالص محصول فرعی است و عمداً هزینه تبدیل جذب نمی‌کند.
   تأیید شده توسط کاربر.
   ─────────────────────────────────────────────────────────────────────── */

IF NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException
               WHERE RuleCode = 'CHK-03' AND Code = 1787)
INSERT dbo.CC_AcceptedException (RuleCode, Code, FNUMB, Reason, AcceptedBy)
VALUES ('CHK-03', 1787, NULL,
        N'آب پنیر خالص محصول فرعی است و عمداً هزینه تبدیل جذب نمی‌کند.',
        N'مدیر مالی');
GO


/* ───────────────────────── واحدهای تولیدی ─────────────────────────
   ⚠ این بخش نمونه است. انبارها را با واقعیت کارخانه تطبیق دهید.
     نقش ۱ (مواد مصرفی تولید) مبنای محاسبه انحراف است و باید
     برای هر واحد دقیقاً یک انبار داشته باشد.
   ─────────────────────────────────────────────────────────────────── */

IF NOT EXISTS (SELECT 1 FROM dbo.CC_Unit)
BEGIN
    INSERT dbo.CC_Unit (UnitName, Depatman, SplitMode, IsActive, SeqNo)
    VALUES (N'واحد اصلی', NULL, 1, 1, 1),
           (N'واحد یزد',  NULL, 1, 1, 2);

    DECLARE @u1 INT = (SELECT UnitId FROM dbo.CC_Unit WHERE UnitName = N'واحد اصلی');
    DECLARE @u2 INT = (SELECT UnitId FROM dbo.CC_Unit WHERE UnitName = N'واحد یزد');

    INSERT dbo.CC_UnitAnbar (UnitId, Anbar, AnbarRole, DoStockCount, SeqNo)
    VALUES (@u1,   7, 1, 1, 1),      -- مواد مصرفي توليد ← مبناي انحراف
           (@u1,   1, 2, 1, 2),      -- مواد اوليه
           (@u1,   2, 3, 1, 3),      -- کالاي ساخته شده
           (@u1,   8, 4, 1, 4),
           (@u2, 810, 1, 1, 1),      -- مواد مصرفي توليد يزد
           (@u2, 811, 2, 1, 2),      -- مواد اوليه يزد
           (@u2, 807, 3, 1, 3);      -- محصول يزد

    -- نگاشت سرفصل‌هاي هزينه تبديل واقعي، بر اساس تراز خودتان
    INSERT dbo.CC_UnitAcc (UnitId, HesKol, CostKind, Ratio, Note)
    VALUES (@u1, 711, 1, 1.000, N'هزينه دستمزد توليد'),
           (@u1, 712, 1, 0.700, N'هزينه دستمزد خدمات — سهم توليدي'),
           (@u1, 713, 1, 0.600, N'هزينه دستمزد اداري — سهم توليدي'),
           (@u1, 721, 2, 1.000, N'ساير هزينه‌هاي توليد'),
           (@u1, 723, 2, 0.400, N'ساير هزينه‌هاي اداري — سهم توليدي'),
           (@u1, 745, 2, 0.250, N'مرکز هزينه ضايعات و ساير'),
           (@u2, 743, 2, 1.000, N'هزينه‌هاي واحد يزد');
END
GO

/* ───────────────────────── ثبت فرم‌ها در TFORMS ─────────────────────────
   نام‌ها دقیقاً باید با Shared/Constants/CostForms.cs یکی باشند — همان
   جدولی که Pay2AccessService/Pay2Authorize برای دسترسی می‌خواند. الگو
   عیناً از pay2_acl_migration.sql گرفته شده (GRP=10 برای این ماژول، تا
   با گروه ۹ که PAY2 استفاده می‌کند تداخل نکند).

   بدون این بخش، صفحهٔ مدیریت دسترسی هیچ ردیفی برای این ماژول نشان
   نمی‌دهد و وقتی AclEnforced روشن باشد هیچ‌کس نمی‌تواند به آن دسترسی
   بگیرد.
   ─────────────────────────────────────────────────────────────────────── */

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_DASHBOARD')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_DASHBOARD', N'داشبورد بستن ماه بهای تمام‌شده', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_RUN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_RUN', N'پیشرفت اجرای بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_EXCEPTIONS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_EXCEPTIONS', N'مغایرت‌های بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_VARIANCE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_VARIANCE', N'تصمیم انحراف', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_CONVERSION')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_CONVERSION', N'هزینه تبدیل', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_MARGIN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_MARGIN', N'سود و زیان کالا', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_HISTORY')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_HISTORY', N'سوابق اجراها', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_SETTINGS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_SETTINGS', N'تنظیمات بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_START')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_START', N'شروع اجرای بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_AUTOFIX')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_AUTOFIX', N'اصلاح خودکار داده', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_RESOLVE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_RESOLVE', N'بستن استثنا', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_DECIDE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_DECIDE', N'ثبت تصمیم انحراف', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_APPLY_RATE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_APPLY_RATE', N'اعمال ضریب تعدیل', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_ROLLUP')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_ROLLUP', N'اجرای موتور نرخ', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_ROLLBACK')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_ROLLBACK', N'بازگردانی از اسنپ‌شات', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_APPROVE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_APPROVE', N'تأیید نهایی و قفل ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_EXPORT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_EXPORT', N'خروجی اکسل', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_REBUILD_DOCS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_REBUILD_DOCS', N'بازسازی سند حواله خروج مواد', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_POST_CORRECTION')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_POST_CORRECTION', N'سند اصلاحی مغایرت کارت انبار/حسابداری', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

PRINT N'فرم‌های ماژول بستن ماه بهای تمام‌شده در TFORMS ثبت شدند.';
GO

PRINT N'داده اوليه ثبت شد.';

SELECT RuleCode AS کد, RuleName AS قاعده, StepCode AS گام,
       CASE DefaultSeverity WHEN 2 THEN N'مسدودکننده' ELSE N'هشدار' END AS شدت
FROM   dbo.CC_CheckRule ORDER BY SortOrder;

SELECT u.UnitName AS واحد, a.Anbar AS انبار,
       CASE a.AnbarRole WHEN 1 THEN N'مبناي انحراف' WHEN 2 THEN N'مواد اوليه'
                        WHEN 3 THEN N'محصول' ELSE N'ساير' END AS نقش,
       CASE a.DoStockCount WHEN 1 THEN N'بله' ELSE N'خير' END AS انبارگرداني
FROM   dbo.CC_Unit u JOIN dbo.CC_UnitAnbar a ON a.UnitId = u.UnitId
ORDER BY u.SeqNo, a.SeqNo;
GO
