/* ============================================================================
   جدول‌های پایه‌ی ورود و دسترسی — فقط برای دیتابیس تست

   ⚠️ روی دیتابیس واقعی لازم نیست؛ آنجا این جدول‌ها از قبل وجود دارند.

   چرا جدا از test_auth_and_acl_users.sql؟
   ----------------------------------------
   pay2_acl_migration.sql در بخش Bootstrap خودش به dbo.SALA_DTL و
   dbo.SAL_CHEK ارجاع می‌دهد. پس این دو جدول باید **قبل از** مهاجرت
   وجود داشته باشند، در حالی که درج کاربران باید **بعد از** مهاجرت انجام
   شود (چون به ردیف‌های PAY2_% در TFORMS نیاز دارد که مهاجرت می‌سازد).

   ترتیب درست:
       1) legacy_dependencies.sql
       2) schema.sql
       3) این فایل                     ← ساخت جدول‌های خالی
       4) pay2_runtime_procedures.sql
       5) pay2_acl_migration.sql
       6) pay2_seed.sql
       7) test_auth_and_acl_users.sql  ← درج کاربران و دسترسی‌ها
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── کاربران سیستم ────────────────────────────────────────────────────── */
-- نکته: ENABL = 0 یعنی کاربر «فعال» است. قرارداد وارونه به‌نظر می‌رسد ولی
-- کوئری ورود در UserService و هر دو کوئری LookupController همین است.
IF OBJECT_ID(N'dbo.SALA_DTL', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SALA_DTL]
    (
        [IDD]       [int]          NOT NULL,
        [SAL_NAME]  [nvarchar](50) NULL,   -- نام کاربری کدشده (CL_METHODS.DECODEUN)
        [PSAL_NAME] [nvarchar](50) NULL,   -- رمز کدشده (CL_METHODS.DECODEPS)
        [GRSAL]     [smallint]     NULL,   -- گروه کاربری
        [HES]       [nvarchar](50) NULL,
        [PORID]     [int]          NULL,
        [erjabe]    [int]          NULL,
        [ENABL]     [smallint]     NULL,   -- ۰ = فعال
        CONSTRAINT [PK_SALA_DTL] PRIMARY KEY CLUSTERED ([IDD])
    );
END
GO

/* ── دسترسی فرم‌ها ────────────────────────────────────────────────────── */
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

/* ── چارت سازمانی ────────────────────────────────────────────────────── */
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

IF OBJECT_ID(N'dbo.SALA_DTL', N'U') IS NULL OR OBJECT_ID(N'dbo.SAL_CHEK', N'U') IS NULL
    THROW 54100, N'جدول‌های ورود ساخته نشدند.', 1;

PRINT N'✅ جدول‌های ورود و دسترسی آماده‌اند.';
GO
