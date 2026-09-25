/* ═══════════════════════════════════════════════════════════════════
   دستیار هوش مصنوعی: دانش کسب‌وکار (یادداشت‌های حسابدار)

   چیزهایی که در ساختار پایگاه نیست و مدل نباید حدسشان بزند — «مشتری‌ها زیر
   کل ۱۱۵اند»، «حساب ۱۲۸ جاری شرکاست و در بدهکاران نمی‌آید»، «گزارش فروش
   ویزیتور از ویو X درمی‌آید». حسابدارِ همین شرکت از صفحه‌ی تنظیمات دستیار
   می‌نویسد، پس برای هر مشتری جداست.

   AlwaysInPrompt = 1 : در پرامپتِ هر گفتگو می‌آید (قاعده‌های کوتاه و مهم).
   AlwaysInPrompt = 0 : فقط با ابزار business_notes جست‌وجو می‌شود.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID('dbo.AI_Knowledge','U') IS NULL
CREATE TABLE dbo.AI_Knowledge (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AI_Knowledge PRIMARY KEY,
    Title          NVARCHAR(200)  NOT NULL,
    Body           NVARCHAR(4000) NOT NULL,
    AlwaysInPrompt BIT            NOT NULL CONSTRAINT DF_AI_Knowledge_Always DEFAULT 0,
    IsActive       BIT            NOT NULL CONSTRAINT DF_AI_Knowledge_Active DEFAULT 1,
    UpdatedBy      NVARCHAR(100)  NULL,
    UpdatedAtUtc   DATETIME2      NOT NULL CONSTRAINT DF_AI_Knowledge_At DEFAULT SYSUTCDATETIME()
);
GO

PRINT N'جدول AI_Knowledge ايجاد شد.';
GO
