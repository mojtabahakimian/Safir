/* ============================================================================
   جدول‌های ورود و دسترسی + سه کاربر آزمایشی — فقط برای دیتابیس تست

   ⚠️ این فایل را هرگز روی دیتابیس واقعی اجرا نکنید. رمزها ساده و عمومی‌اند.

   چرا لازم است؟
   ---------------
   schema.sql سه جدول پایه‌ی ورود و دسترسی را ندارد:
       SALA_DTL      — کاربران سیستم (نام کاربری و رمز کدشده)
       SAL_CHEK      — دسترسی هر کاربر به هر فرم
       CHARTSAZMANI  — چارت سازمانی (برای دیدن کارهای زیرمجموعه)
   بدون این‌ها هیچ‌کس نمی‌تواند وارد دیتابیس تست شود، پس کنترل دسترسی
   حقوق و دستمزد اصلاً قابل آزمایش نیست.

   ترتیب اجرا
   ------------
       1) schema.sql
       2) legacy_dependencies.sql
       3) pay2_runtime_procedures.sql
       4) pay2_acl_migration.sql      ← ردیف‌های TFORMS مربوط به PAY2 را می‌سازد
       5) pay2_seed.sql
       6) این فایل                     ← باید بعد از ۴ اجرا شود

   کاربران ساخته‌شده
   ------------------
   | نام کاربری  | رمز    | نقش                                              |
   |-------------|--------|--------------------------------------------------|
   | payadmin    | 111111 | دسترسی کامل به همه فرم‌های PAY2 و همه کارگاه‌ها    |
   | payviewer   | 222222 | فقط RUN+SEE (بدون درج/ویرایش/حذف)، همه کارگاه‌ها  |
   | payscoped   | 333333 | دسترسی کامل ولی فقط محدود به کارگاه شماره ۱      |

   نام کاربری و رمز در SALA_DTL به‌صورت کدشده ذخیره می‌شوند
   (CL_METHODS.DECODEUN / DECODEPS). مقادیر زیر با اجرای واقعی همان توابع
   راستی‌آزمایی شده‌اند.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── ۱. جدول کاربران ──────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.SALA_DTL', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SALA_DTL]
    (
        [IDD]       [int]            NOT NULL,
        [SAL_NAME]  [nvarchar](50)   NULL,   -- نام کاربری کدشده
        [PSAL_NAME] [nvarchar](50)   NULL,   -- رمز کدشده
        [GRSAL]     [smallint]       NULL,   -- گروه کاربری
        [HES]       [nvarchar](50)   NULL,
        [PORID]     [int]            NULL,
        [erjabe]    [int]            NULL,
        [ENABL]     [smallint]       NULL,   -- ۰ = فعال (کوئری ورود ENABL = 0 می‌خواهد)
        CONSTRAINT [PK_SALA_DTL] PRIMARY KEY CLUSTERED ([IDD])
    );
END
GO

/* ── ۲. جدول دسترسی فرم‌ها ────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.SAL_CHEK', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAL_CHEK]
    (
        [USERCO] [int]      NOT NULL,   -- = SALA_DTL.IDD
        [OBJECT] [int]      NOT NULL,   -- = TFORMS.IDH
        [RUN]    [smallint] NULL,       -- اجرای فرم
        [SEE]    [smallint] NULL,       -- مشاهده
        [INP]    [smallint] NULL,       -- درج
        [UPD]    [smallint] NULL,       -- ویرایش
        [DEL]    [smallint] NULL,       -- حذف
        [CRT]    [datetime] NULL,
        [UID]    [int]      NULL,
        CONSTRAINT [PK_SAL_CHEK] PRIMARY KEY CLUSTERED ([USERCO], [OBJECT])
    );
END
GO

/* ── ۳. چارت سازمانی ─────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.CHARTSAZMANI', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CHARTSAZMANI]
    (
        [ID]       [int] IDENTITY(1,1) NOT NULL,
        [USERCO]   [int] NOT NULL,
        [PARENTCO] [int] NULL,
        [CRT]      [datetime] NULL,
        CONSTRAINT [PK_CHARTSAZMANI] PRIMARY KEY CLUSTERED ([ID])
    );
END
GO

/* ── ۴. سه کاربر آزمایشی ─────────────────────────────────────────────── */
-- مقادیر کدشده با اجرای واقعی CL_METHODS.DECODEUN/DECODEPS بررسی شده‌اند.
MERGE dbo.SALA_DTL AS T
USING (VALUES
    (9001, N'\MeMPYUZ',  N'WWW''''''WWW', 1, 0),   -- payadmin  / 111111
    (9002, N'\MebUQcQ^', N'WWW((((((WWW', 1, 0),   -- payviewer / 222222
    (9003, N'\Me_O[\QP', N'WWW))))))WWW', 1, 0)    -- payscoped / 333333
) AS S ([IDD], [SAL_NAME], [PSAL_NAME], [GRSAL], [ENABL])
    ON T.[IDD] = S.[IDD]
WHEN MATCHED THEN UPDATE SET
    T.[SAL_NAME] = S.[SAL_NAME], T.[PSAL_NAME] = S.[PSAL_NAME],
    T.[GRSAL] = S.[GRSAL], T.[ENABL] = S.[ENABL]
WHEN NOT MATCHED THEN INSERT ([IDD],[SAL_NAME],[PSAL_NAME],[GRSAL],[ENABL])
    VALUES (S.[IDD], S.[SAL_NAME], S.[PSAL_NAME], S.[GRSAL], S.[ENABL]);
GO

/* راستی‌آزمایی: اگر pay2_acl_migration.sql اجرا نشده باشد هیچ فرم PAY2ای
   وجود ندارد و دسترسی‌ها ساخته نمی‌شوند — پس همین‌جا متوقف شو. */
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%')
    THROW 54000, N'ابتدا pay2_acl_migration.sql را اجرا کنید — هیچ فرم PAY2 در TFORMS نیست.', 1;
GO

/* ── ۵. دسترسی فرم‌ها ─────────────────────────────────────────────────── */
DELETE FROM dbo.SAL_CHEK WHERE USERCO IN (9001, 9002, 9003);

-- payadmin — همه‌ی پنج مجوز روی همه‌ی فرم‌های PAY2
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9001, IDH, 1, 1, 1, 1, 1, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%';

-- payviewer — فقط باز کردن و دیدن؛ هیچ تغییری مجاز نیست
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9002, IDH, 1, 1, 0, 0, 0, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%';

-- payscoped — دسترسی کامل به فرم‌ها، ولی در گام بعد فقط به یک کارگاه وصل می‌شود
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9003, IDH, 1, 1, 1, 1, 1, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%';
GO

/* ── ۶. محدوده‌ی کارگاه ──────────────────────────────────────────────── */
DELETE FROM dbo.PAY2_USER_WS WHERE USERCO IN (9001, 9002, 9003);

-- payadmin و payviewer به همه‌ی کارگاه‌ها
INSERT INTO dbo.PAY2_USER_WS (USERCO, WS_ID, CRT)
SELECT U.USERCO, W.WS_ID, GETDATE()
FROM (VALUES (9001), (9002)) AS U(USERCO)
CROSS JOIN dbo.PAY2_WORKSHOP W;

-- payscoped فقط به کارگاه شماره ۱ — اینجاست که محدودسازی سطر آزمایش می‌شود
INSERT INTO dbo.PAY2_USER_WS (USERCO, WS_ID, CRT)
SELECT 9003, W.WS_ID, GETDATE()
FROM dbo.PAY2_WORKSHOP W WHERE W.WS_ID = 1;
GO

/* ── ۷. کنترل دسترسی را روشن کن ──────────────────────────────────────── */
-- در دیتابیس واقعی پیش‌فرض ۰ (خاموش) است تا چیزی ناگهان قطع نشود؛
-- ولی در دیتابیس تست باید روشن باشد وگرنه اصلاً چیزی آزمایش نمی‌شود.
UPDATE dbo.PAY2_CONFIG SET CFG_VALUE = N'1' WHERE CFG_KEY = N'ACL_ENFORCE';
UPDATE dbo.PAY2_CONFIG SET CFG_VALUE = N'1' WHERE CFG_KEY = N'ACL_WS_SCOPE_ENFORCE';
GO

/* ── ۸. گزارش نهایی ──────────────────────────────────────────────────── */
IF (SELECT COUNT(*) FROM dbo.SALA_DTL WHERE IDD IN (9001,9002,9003)) <> 3
    THROW 54001, N'کاربران آزمایشی ساخته نشدند.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK WHERE USERCO = 9002 AND [INP] = 0)
    THROW 54002, N'دسترسی محدود payviewer درست ثبت نشد.', 1;

SELECT  D.IDD                                            AS UserId,
        CASE D.IDD WHEN 9001 THEN N'payadmin'
                   WHEN 9002 THEN N'payviewer'
                   ELSE           N'payscoped' END       AS UserName,
        COUNT(DISTINCT C.[OBJECT])                       AS Pay2Forms,
        SUM(CAST(C.[INP] AS INT))                        AS FormsWithInsert,
        (SELECT COUNT(*) FROM dbo.PAY2_USER_WS W
          WHERE W.USERCO = D.IDD)                        AS AllowedWorkshops
FROM dbo.SALA_DTL D
LEFT JOIN dbo.SAL_CHEK C ON C.USERCO = D.IDD
WHERE D.IDD IN (9001, 9002, 9003)
GROUP BY D.IDD
ORDER BY D.IDD;

PRINT N'✅ کاربران آزمایشی و دسترسی‌هایشان ساخته شدند. ACL_ENFORCE = 1';
GO
