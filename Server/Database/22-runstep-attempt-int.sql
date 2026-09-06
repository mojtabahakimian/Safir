/* ═══════════════════════════════════════════════════════════════════
   عریض کردن CC_RunStep.Attempt از TINYINT به INT

   ── چه چیزی خراب بود ──
   Attempt با TINYINT تعریف شده بود (سقف ۲۵۵). شماره‌ی تلاش در
   CC_sp_StepStart این‌طور حساب می‌شود:

       MAX(Attempt) برای همان RunId/StepCode  +  ۱

   یعنی شمارنده بین اجراهای مکررِ یک Run انباشته می‌شود و هرگز صفر
   نمی‌شود. حلقه‌ی همگرایی S07A↔S11 در هر اجرا تا ۴۰ دور می‌رود
   (MaxS11Cycles در CloseOrchestrator)، پس چند بار «اجرای مجدد گام‌ها»
   روی یک ماه کافی است تا از ۲۵۵ رد شود.

   روی ران واقعی اردیبهشت ۱۴۰۵ (RunId=6) دقیقاً همین شد: S07A به
   Attempt=255 رسید و دور بعد کل بستن ماه با این خطا متوقف شد:

       Arithmetic overflow error for data type tinyint, value = 256.
       Cannot insert the value NULL into column 'Attempt' ...

   (سرریزِ محاسبه، مقدار را NULL کرد و INSERT روی ستون NOT NULL شکست.)

   ── چرا INT و نه SMALLINT ──
   SMALLINT فقط سقف را به ۳۲٬۷۶۷ می‌برد؛ همان مسئله را دورتر می‌کند نه
   حل. INT با RunStepId هم‌نوع است و عملاً بی‌سقف.

   ⚠ محدودیت UQ_CC_RunStep روی (RunId, StepCode, Attempt) است، پس باید
   قبل از تغییر نوع ستون حذف و بعد دوباره ساخته شود.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* هر سه مرحله جدا و شرطی‌اند تا اسکریپت idempotent باشد و وضعیت
   نیمه‌مهاجرت را هم ترمیم کند (اگر اجرای قبلی وسط کار شکست خورده و
   محدودیت یکتا حذف شده ولی نوع ستون عوض نشده باشد). */

-- ۱) محدودیت یکتا شامل Attempt است، پس باید موقتاً برداشته شود
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UQ_CC_RunStep'
             AND parent_object_id = OBJECT_ID('dbo.CC_RunStep'))
BEGIN
    ALTER TABLE dbo.CC_RunStep DROP CONSTRAINT UQ_CC_RunStep;
    PRINT N'UQ_CC_RunStep موقتاً حذف شد.';
END
GO

-- ۲) تبدیل نوع ستون
--    ⚠ قید DEFAULT هم به ستون وابسته است و ALTER COLUMN را بلاک می‌کند
--    (خطای 5074). نامش خودکار ساخته شده (مثل DF__CC_RunSte__Attem__2CA81010)
--    و در هر نصب فرق می‌کند، پس باید از کاتالوگ خوانده شود نه هاردکد.
IF EXISTS (SELECT 1
           FROM   sys.columns
           WHERE  object_id = OBJECT_ID('dbo.CC_RunStep')
             AND  name      = 'Attempt'
             AND  system_type_id = TYPE_ID('tinyint'))
BEGIN
    PRINT N'در حال تبدیل CC_RunStep.Attempt از TINYINT به INT ...';

    DECLARE @df SYSNAME, @sql NVARCHAR(MAX);

    SELECT @df = dc.name
    FROM   sys.default_constraints dc
    JOIN   sys.columns c ON c.object_id = dc.parent_object_id
                        AND c.column_id = dc.parent_column_id
    WHERE  dc.parent_object_id = OBJECT_ID('dbo.CC_RunStep')
      AND  c.name = 'Attempt';

    IF @df IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.CC_RunStep DROP CONSTRAINT ' + QUOTENAME(@df);
        EXEC sp_executesql @sql;
    END

    ALTER TABLE dbo.CC_RunStep ALTER COLUMN Attempt INT NOT NULL;

    -- این بار با نام ثابت، تا دفعه‌ی بعد لازم نباشد از کاتالوگ پیدایش کنیم
    ALTER TABLE dbo.CC_RunStep
        ADD CONSTRAINT DF_CC_RunStep_Attempt DEFAULT 1 FOR Attempt;

    PRINT N'نوع ستون به INT تبدیل شد.';
END
ELSE
    PRINT N'CC_RunStep.Attempt از قبل TINYINT نیست — تبدیل لازم نبود.';
GO

-- ۳) بازگرداندن محدودیت یکتا
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE name = 'UQ_CC_RunStep'
                 AND parent_object_id = OBJECT_ID('dbo.CC_RunStep'))
BEGIN
    ALTER TABLE dbo.CC_RunStep
        ADD CONSTRAINT UQ_CC_RunStep UNIQUE (RunId, StepCode, Attempt);
    PRINT N'UQ_CC_RunStep بازگردانده شد.';
END
GO

/* CC_sp_StepStart هم متغیر داخلی‌اش TINYINT بود و مستقل از نوعِ ستون
   سرریز می‌کرد؛ نسخه‌ی اصلاح‌شده در 12-procedures-phase1.sql است و باید
   دوباره اجرا شود. برای اینکه این فایل به‌تنهایی هم کامل باشد، همان
   نسخه اینجا تکرار شده است. */
CREATE OR ALTER PROCEDURE dbo.CC_sp_StepStart
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Title    NVARCHAR(120),
    @SeqNo    SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @try INT =
        ISNULL((SELECT MAX(Attempt) FROM dbo.CC_RunStep
                WHERE RunId = @RunId AND StepCode = @StepCode), 0) + 1;

    INSERT dbo.CC_RunStep (RunId, StepCode, StepTitle, SeqNo, Attempt, Status, StartedAtUtc)
    VALUES (@RunId, @StepCode, @Title, @SeqNo, @try, 1, SYSUTCDATETIME());

    UPDATE dbo.CC_Run
       SET Status = 1, StartedAtUtc = ISNULL(StartedAtUtc, SYSUTCDATETIME())
     WHERE RunId = @RunId;
END
GO

PRINT N'اسکریپت 22-runstep-attempt-int.sql اجرا شد.';
GO
