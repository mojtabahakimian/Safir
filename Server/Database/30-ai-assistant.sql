/* ═══════════════════════════════════════════════════════════════════
   دستیار هوش مصنوعی — جدول‌های دسترسی و لاگ

   ── قاعده‌ی بنیادی ──
   دسترسیِ چت هیچ‌وقت از دسترسیِ خودِ کاربر بیشتر نمی‌شود:

       دسترسی مؤثر = مجوز چتِ کاربر  ∩  دسترسی کاربر در برنامه

   لایه‌ی دوم همان Pay2AccessService است و در کد اجرا می‌شود، نه اینجا.
   این جدول فقط لایه‌ی اول است: ادمین می‌گوید چت برای این کاربر چقدر
   باز باشد. اگر ادمین اشتباهاً همه‌چیز را باز بگذارد، باز هم کاربر از
   محدوده‌ی خودش بیرون نمی‌رود.

   ── چرا سطرِ نبودن یعنی «ممنوع» ──
   کاربری که سطر ندارد چت ندارد. پیش‌فرضِ باز، یعنی هر کاربر تازه‌ای که
   فردا تعریف شود بی‌سروصدا به کل داده دسترسی پیدا کند.

   ── چرا فقط دستور دادن به مدل کافی نیست ──
   ستون Mode می‌گوید چت اجازه‌ی نوشتن دارد یا نه، ولی تضمینِ واقعی در
   کد است: اتصالِ ابزارها با کاربرِ db_datareader باز می‌شود، پس حتی
   اگر مدل UPDATE بسازد، خودِ SQL Server ردش می‌کند. این ستون برای
   تصمیمِ رابط کاربری است، نه سدّ آخر.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.AI_UserAccess','U') IS NULL
CREATE TABLE dbo.AI_UserAccess (
    UserCo        INT      NOT NULL PRIMARY KEY,   -- همان USERCO در سیستم دسترسی
    IsEnabled     BIT      NOT NULL DEFAULT 0,
    -- ۰ = فقط خواندن، ۱ = خواندن + پیشنهاد عمل با تأیید کاربر
    Mode          TINYINT  NOT NULL DEFAULT 0,
    -- کوئری آزاد فقط برای کاربر خبره؛ پیش‌فرض بسته است و چت فقط
    -- ابزارهای آماده را دارد
    AllowRawSql   BIT      NOT NULL DEFAULT 0,
    -- سقف سطرِ هر ابزار. جلوی «کل فروش سه سال را بده» را می‌گیرد که هم
    -- سرور را می‌خواباند هم کل داده را به بیرون می‌فرستد
    MaxRows       INT      NOT NULL DEFAULT 500,
    -- سقف پیام روزانه — کنترل هزینه‌ی سرویس ابری
    DailyMessages INT      NOT NULL DEFAULT 100,
    -- فرم‌هایی که حتی با وجود دسترسیِ کاربر، از چت خارج‌اند. لیست با
    -- کاما. پیش‌فرض حقوق و دستمزد است: محتوای آن به سرویس ابری می‌رود
    -- و باید تصمیمِ آگاهانه باشد، نه اتفاقی.
    BlockedForms  NVARCHAR(1000) NULL DEFAULT N'PAY2_PAYROLL,PAY2_EMPLOYEE',
    Note          NVARCHAR(400)  NULL,
    UpdatedBy     NVARCHAR(100)  NULL,
    UpdatedAtUtc  DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

/* ── لاگ ──
   هر پیام، هر ابزاری که صدا زده شد، و اینکه مجاز بود یا رد شد. بدون
   این، روزی که کسی بپرسد «این عدد از کجا آمد» یا «چه کسی این داده را
   دید» جوابی نداریم.

   ToolName خالی یعنی خودِ پیامِ کاربر؛ پرشده یعنی یک فراخوانی ابزار. */
IF OBJECT_ID('dbo.AI_ChatLog','U') IS NULL
CREATE TABLE dbo.AI_ChatLog (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    ConversationId UNIQUEIDENTIFIER NOT NULL,
    UserCo       INT            NOT NULL,
    UserName     NVARCHAR(100)  NULL,
    AtUtc        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    Kind         TINYINT        NOT NULL,   -- 0=پیام کاربر 1=پاسخ مدل 2=ابزار
    ToolName     NVARCHAR(60)   NULL,
    -- ورودی ابزار یا متن پیام. عمداً کامل ذخیره می‌شود: خلاصه‌ی لاگ در
    -- بازرسیِ بعدی بی‌فایده است.
    Payload      NVARCHAR(MAX)  NULL,
    RowsReturned INT            NULL,
    Allowed      BIT            NOT NULL DEFAULT 1,
    DenyReason   NVARCHAR(200)  NULL,
    DurationMs   INT            NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_AI_ChatLog_User' AND object_id = OBJECT_ID('dbo.AI_ChatLog'))
    CREATE INDEX IX_AI_ChatLog_User ON dbo.AI_ChatLog (UserCo, AtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_AI_ChatLog_Conv' AND object_id = OBJECT_ID('dbo.AI_ChatLog'))
    CREATE INDEX IX_AI_ChatLog_Conv ON dbo.AI_ChatLog (ConversationId, Id);
GO


/* شمارش پیام‌های امروزِ کاربر — برای سقف روزانه.
   فقط Kind = 0 شمرده می‌شود: ابزارها پیامِ کاربر نیستند و یک سؤال که
   پنج ابزار صدا می‌زند نباید پنج برابر حساب شود. */
CREATE OR ALTER FUNCTION dbo.AI_fn_TodayMessageCount (@UserCo INT)
RETURNS INT
AS
BEGIN
    RETURN (SELECT COUNT(*)
            FROM   dbo.AI_ChatLog
            WHERE  UserCo = @UserCo
              AND  Kind   = 0
              AND  AtUtc >= CAST(CAST(SYSUTCDATETIME() AS DATE) AS DATETIME2));
END
GO

PRINT N'جدول‌های AI_UserAccess و AI_ChatLog ايجاد شدند.';
GO
