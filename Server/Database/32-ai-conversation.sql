/* ═══════════════════════════════════════════════════════════════════
   تاریخچه‌ی گفتگوهای دستیار

   ── چرا جدول تازه و نه خواندن از AI_ChatLog ──
   خودِ پیام‌ها از قبل در AI_ChatLog هستند و دوباره ذخیره نمی‌شوند؛
   این جدول فقط «سرِ» گفتگوست: عنوان، مالک، و زمان آخرین پیام. بدون آن
   برای ساختن فهرست باید هر بار روی کلِ لاگ گروه‌بندی می‌شد، و عنوانی
   هم که کاربر تغییر داده جایی برای نشستن نداشت.

   ── مالکیت ──
   UserCo روی سرِ گفتگو ثبت می‌شود تا هر کاربر فقط گفتگوهای خودش را
   ببیند. این کنترل در کد هم هست؛ ستون اینجا برای همان است، نه تزئین.

   ── حذف ──
   حذف واقعی است، هم سرِ گفتگو و هم پیام‌هایش. «حذف نرم» اینجا معنی
   نداشت: کاربری که گفتگو را پاک می‌کند انتظار دارد متنش برود، و
   نگه‌داشتنِ پنهانی‌اش با همان دلیلی که لاگ را ساختیم جور درنمی‌آید —
   بازرسی باید عمدی باشد، نه از راه چیزی که کاربر فکر می‌کند پاک شده.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.AI_Conversation','U') IS NULL
CREATE TABLE dbo.AI_Conversation (
    ConversationId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    UserCo         INT            NOT NULL,
    UserName       NVARCHAR(100)  NULL,

    -- عنوان از اولین سؤال ساخته می‌شود و کاربر می‌تواند عوضش کند.
    Title          NVARCHAR(200)  NOT NULL,

    CreatedAtUtc   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    -- زمان آخرین پیام؛ فهرست بر همین مرتب می‌شود، نه بر زمان ساخت.
    UpdatedAtUtc   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_AI_Conversation_User'
                 AND object_id = OBJECT_ID('dbo.AI_Conversation'))
    CREATE INDEX IX_AI_Conversation_User
        ON dbo.AI_Conversation (UserCo, UpdatedAtUtc DESC);
GO


/* سرِ گفتگو را می‌سازد اگر نباشد، و در هر حال زمانش را جلو می‌برد.
   عنوان فقط بار اول از متن سؤال گرفته می‌شود؛ اگر کاربر بعداً عوضش
   کرده باشد، پیام بعدی نباید دوباره رویش بنویسد. */
CREATE OR ALTER PROCEDURE dbo.AI_sp_TouchConversation
    @ConversationId UNIQUEIDENTIFIER,
    @UserCo         INT,
    @UserName       NVARCHAR(100) = NULL,
    @FirstQuestion  NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.AI_Conversation WHERE ConversationId = @ConversationId)
    BEGIN
        UPDATE dbo.AI_Conversation
        SET    UpdatedAtUtc = SYSUTCDATETIME()
        WHERE  ConversationId = @ConversationId;

        RETURN;
    END

    -- عنوانِ خودکار: خطِ اول سؤال، کوتاه‌شده. سؤال چندخطی در فهرست
    -- ردیف‌ها را به هم می‌ریخت.
    DECLARE @title NVARCHAR(200) =
        LTRIM(RTRIM(LEFT(REPLACE(REPLACE(ISNULL(@FirstQuestion, N''), CHAR(13), N' '),
                                 CHAR(10), N' '), 120)));

    IF @title = N'' SET @title = N'گفتگوی بدون عنوان';

    INSERT dbo.AI_Conversation (ConversationId, UserCo, UserName, Title)
    VALUES (@ConversationId, @UserCo, @UserName, @title);
END
GO

PRINT N'جدول AI_Conversation و رويه AI_sp_TouchConversation ايجاد شدند.';
GO
