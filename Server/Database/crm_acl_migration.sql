/* ============================================================================
   مهاجرت کنترل دسترسی CRM

   هدف: یک قابلیت و فقط یک قابلیت — هر کاربر یا داده‌های همه را می‌بیند، یا
   فقط داده‌های خودش را.

   ⚠️ این اسکریپت به‌تنهایی هیچ رفتاری را عوض نمی‌کند. کلید CRM_ACL_ENFORCE با
   مقدار '0' (خاموش) ساخته می‌شود، پس بعد از اجرا همه‌چیز دقیقاً مثل قبل کار
   می‌کند. برای فعال‌سازی، دستور انتهای همین فایل را جدا اجرا کنید.

   نسخه‌ی معادل این اسکریپت در ScriptSqly.Core/ScriptSqly.Blazor.cs است
   (متد CrmAclScript). اگر یکی را عوض کردید، دیگری هم باید عوض شود.

   همه‌ی بلوک‌ها idempotent هستند و چند بار اجرا شدن اشکالی ندارد.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── ۱) فرم مجازی «مشاهده CRM همه کاربران» ────────────────────────────────
   این ردیف یک فرم واقعی نیست؛ یک کلید روشن/خاموش per-user است که از
   نرم‌افزار WPF (همان صفحه‌ای که دسترسی فرم‌ها را تعیین می‌کند) تنظیم می‌شود.
   دقیقاً همان الگوی CUSTEN («نوع مشتری برای این کاربر فعال باشد») و AZADPAY.

   هر کاربری که RUN این فرم را داشته باشد، CRM همه‌ی کاربران را می‌بیند.
   پیش‌فرض به هیچ‌کس داده نمی‌شود — یعنی وقتی کلید اصلی روشن شود، همه به
   داده‌ی خودشان محدودند تا وقتی صریحاً به کسی این مجوز داده شود.

   GRP = 16 همان گروهی است که CRMMAIN (فرم اصلی CRM) در آن قرار دارد.
   ──────────────────────────────────────────────────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'CRMALL')
BEGIN
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'CRMALL',
            N'مشاهده CRM همه کاربران',
            3,
            ISNULL((SELECT TOP 1 GRP FROM dbo.TFORMS WHERE FORMNAME = N'CRMMAIN'), 16),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM dbo.TFORMS),
            GETDATE());
END
GO

/* ── ۲) کلید فعال‌سازی ────────────────────────────────────────────────────
   هم‌خانواده‌ی ACL_ENFORCE ماژول حقوق و دستمزد و در همان بخش «امنیت» صفحه‌ی
   تنظیمات ظاهر می‌شود.

   اگر PAY2_CONFIG وجود نداشته باشد (دیتابیسی که ماژول حقوق روی آن نصب نیست)
   این بلوک رد می‌شود و سرور کلید را «خاموش» می‌خواند — یعنی رفتار قبلی.
   ──────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.PAY2_CONFIG', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY = N'CRM_ACL_ENFORCE')
BEGIN
    INSERT INTO dbo.PAY2_CONFIG
        (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION,
         LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL, CRT)
    VALUES
        (N'CRM_ACL_ENFORCE',
         N'0',
         N'1|0',
         N'0',
         N'امنیت',
         N'محدودکردن CRM به داده‌های خودِ کاربر',
         N'۰ = خاموش (پیش‌فرض): هر کاربر CRM همه را می‌بیند، مثل قبل. ' +
         N'۱ = روشن: هر کاربر فقط شرکت‌ها، پیگیری‌ها و یادداشت‌های خودش را ' +
         N'می‌بیند، مگر مجوز فرم CRMALL («مشاهده CRM همه کاربران») را داشته باشد.',
         N'روشن — هر کاربر فقط داده‌ی خودش|خاموش — همه همه‌چیز را می‌بینند',
         N'BOOL',
         1,
         GETDATE());
END
GO

/* ── ۳) ایندکس‌های پشتیبان ───────────────────────────────────────────────
   با روشن شدن محدودیت، شرط WHERE همه‌ی کوئری‌های CRM عوض می‌شود.
   هر بلوک وجود جدولش را جدا چک می‌کند: روی دیتابیسی که ماژول CRM اصلاً
   رویش نصب نیست، مهاجرت باید بی‌صدا رد شود نه اینکه بشکند.
   این ایندکس‌ها روی بعضی دیتابیس‌ها از قبل دستی ساخته شده‌اند؛ بلوک‌های زیر
   idempotent هستند و آنجا کاری نمی‌کنند.
   ──────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.COPMANES', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_COPMANES_USERID_STATUS'
                     AND object_id = OBJECT_ID(N'dbo.COPMANES'))
    CREATE NONCLUSTERED INDEX [IX_COPMANES_USERID_STATUS]
        ON [dbo].[COPMANES] ([userid], [STATUS]);
GO

IF OBJECT_ID(N'dbo.CRMEVENTS', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_CRMEVENTS_USERID'
                     AND object_id = OBJECT_ID(N'dbo.CRMEVENTS'))
    CREATE NONCLUSTERED INDEX [IX_CRMEVENTS_USERID]
        ON [dbo].[CRMEVENTS] ([USERID])
        INCLUDE ([idc], [NEXT_DATE], [miting], [STATUS]);
GO

IF OBJECT_ID(N'dbo.CRMEVENTS', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_CRMEVENTS_IDC'
                     AND object_id = OBJECT_ID(N'dbo.CRMEVENTS'))
    CREATE NONCLUSTERED INDEX [IX_CRMEVENTS_IDC]
        ON [dbo].[CRMEVENTS] ([idc])
        INCLUDE ([STATUS], [NEXT_DATE], [NEXT_TIME], [miting]);
GO

IF OBJECT_ID(N'dbo.CRMEVENTS', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_CRMEVENTS_NEXTDATE'
                     AND object_id = OBJECT_ID(N'dbo.CRMEVENTS'))
    CREATE NONCLUSTERED INDEX [IX_CRMEVENTS_NEXTDATE]
        ON [dbo].[CRMEVENTS] ([NEXT_DATE])
        INCLUDE ([idc], [miting], [STATUS], [USERID]);
GO

IF OBJECT_ID(N'dbo.Notes', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_Notes_USERID_NDONE'
                     AND object_id = OBJECT_ID(N'dbo.Notes'))
    CREATE NONCLUSTERED INDEX [IX_Notes_USERID_NDONE]
        ON [dbo].[Notes] ([userid], [Ndone]);
GO

PRINT N'✅ مهاجرت کنترل دسترسی CRM انجام شد — محدودیت هنوز خاموش است.';
GO

/* ============================================================================
   فعال‌سازی — این بخش عمداً جدا است و خودکار اجرا نمی‌شود

   ۱) اول به مدیر (یا هر کسی که باید همه را ببیند) مجوز CRMALL بدهید.
      از صفحه‌ی مدیریت دسترسی‌های نرم‌افزار WPF، یا با این دستور:

          DECLARE @UserId INT = 105;   -- کد کاربر از SALA_DTL.IDD

          INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], CRT)
          SELECT @UserId, F.IDH, 1, 1, 1, 1, 1, GETDATE()
          FROM dbo.TFORMS F
          WHERE F.FORMNAME = N'CRMALL'
            AND NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK SC
                            WHERE SC.USERCO = @UserId AND SC.[OBJECT] = F.IDH);

   ۲) بعد محدودیت را روشن کنید:

          UPDATE dbo.PAY2_CONFIG SET CFG_VALUE = N'1' WHERE CFG_KEY = N'CRM_ACL_ENFORCE';

   برگرداندن همه‌چیز به حالت قبل، با همین یک دستور:

          UPDATE dbo.PAY2_CONFIG SET CFG_VALUE = N'0' WHERE CFG_KEY = N'CRM_ACL_ENFORCE';

   تغییر حداکثر ۳۰ ثانیه بعد اثر می‌کند (کشِ تنظیمات در CrmAccessService).
   ============================================================================ */
