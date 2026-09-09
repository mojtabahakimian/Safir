/* ═══════════════════════════════════════════════════════════════════
   تنظیمات سرویس هوش مصنوعی

   ── چرا در پایگاه و نه فقط appsettings ──
   روی نصبِ مشتری کسی به فایل تنظیمات یا متغیر محیطیِ سرور دسترسی ندارد.
   عوض کردن آدرس درگاه یا کلید نباید به حضور برنامه‌نویس نیاز داشته باشد.

   ── ترتیب اولویت ──
   ۱) این جدول  ۲) متغیر محیطی  ۳) appsettings
   متغیر محیطی عمداً بالاتر از appsettings مانده: جایی که تیم فنی ترجیح
   می‌دهد کلید اصلاً وارد پایگاه نشود، همان مسیر قبلی کار می‌کند.

   ── کلید ──
   ⚠ ApiKey فقط نوشتنی است. هیچ اندپوینتی آن را برنمی‌گرداند؛ صفحه‌ی
   تنظیمات تنها می‌گوید کلیدی ثبت شده و چهار نویسه‌ی آخرش چیست. کسی که
   دسترسی ویرایش دارد می‌تواند کلید تازه بگذارد ولی کلید فعلی را
   نمی‌بیند — دیدنِ آن هیچ کاربردی در رابط کاربری ندارد و فقط راهی برای
   بیرون رفتنش می‌سازد.

   ⚠ رمزنگاری نشده ذخیره می‌شود. هرکس به خودِ پایگاه دسترسی مستقیم دارد
   می‌تواند بخواندش. اگر این پذیرفتنی نیست، کلید را در متغیر محیطی سرور
   بگذارید و این ستون را خالی رها کنید — همان اولویت بالاتر.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.AI_Config','U') IS NULL
CREATE TABLE dbo.AI_Config (
    -- تک‌سطری. CHECK جلوی سطر دوم را می‌گیرد، وگرنه معلوم نبود کدام
    -- تنظیمات معتبر است و دو نفر می‌توانستند دو سطر بسازند.
    Id             TINYINT      NOT NULL PRIMARY KEY DEFAULT 1
                   CONSTRAINT CK_AI_Config_Single CHECK (Id = 1),

    IsEnabled      BIT           NOT NULL DEFAULT 0,
    Provider       NVARCHAR(20)  NOT NULL DEFAULT N'openai',   -- openai | anthropic
    BaseUrl        NVARCHAR(300) NULL,
    Model          NVARCHAR(120) NULL,
    ApiKey         NVARCHAR(400) NULL,
    TimeoutSeconds INT           NOT NULL DEFAULT 120,
    MaxToolLoops   INT           NOT NULL DEFAULT 4,

    UpdatedBy      NVARCHAR(100) NULL,
    UpdatedAtUtc   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- سطر پیش‌فرض، غیرفعال. تا وقتی ادمین آدرس و کلید را ندهد، دستیار با
-- همان مقادیر appsettings کار می‌کند و این جدول در مسیر نمی‌آید.
IF NOT EXISTS (SELECT 1 FROM dbo.AI_Config WHERE Id = 1)
    INSERT dbo.AI_Config (Id, IsEnabled) VALUES (1, 0);
GO

PRINT N'جدول AI_Config ايجاد شد.';
GO
