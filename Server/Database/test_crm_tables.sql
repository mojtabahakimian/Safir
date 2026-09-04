/* ============================================================================
   جدول‌های CRM و داده‌ی آزمایشی — فقط برای دیتابیس تست

   ⚠️ روی دیتابیس واقعی لازم نیست؛ آنجا این جدول‌ها از قبل وجود دارند.

   چرا لازم است؟ مثل SALA_DTL و SAL_CHEK، هیچ‌کدام از جدول‌های CRM در
   schema.sql و legacy_dependencies.sql نیستند. دیتابیسی که فقط از آن‌ها
   ساخته شود اصلاً جدول COPMANES ندارد و صفحه‌ی CRM بالا نمی‌آید.

   ساختار جدول‌ها عیناً از دیتابیس واقعی مشتری گرفته شده تا تست با چیزی که
   در تولید هست بخواند — از جمله دو ناهماهنگیِ عمدی که در تولید هم هست:
     • Notes.Ndate از نوع nchar(10) است، نه int
     • Notes.Ntime از نوع datetime است، نه رشته

   باید **بعد از** crm_acl_migration.sql اجرا شود، چون به فرم CRMALL
   ارجاع می‌دهد.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── ۱. جدول‌های CRM ─────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.COPMANES', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[COPMANES]
    (
        [id]           [int] IDENTITY(1,1) NOT NULL,
        [COMPANY_NAME] [nvarchar](100) NULL,
        [CITY]         [nvarchar](50)  NULL,
        [MANAGER]      [nvarchar](50)  NULL,
        [FACT_TEL]     [nvarchar](50)  NULL,
        [MOBILE]       [nvarchar](50)  NULL,
        [PERNUM]       [int]           NULL,
        [STATUS_FACT]  [nvarchar](100) NULL,
        [PRODUCTS]     [nvarchar](100) NULL,
        [ADDR]         [nvarchar](250) NULL,
        [ACCOUNTANT]   [nvarchar](50)  NULL,
        [SOFTWARE]     [nvarchar](50)  NULL,
        [ESP_PERSON]   [nvarchar](100) NULL,
        [REAGENT]      [nvarchar](50)  NULL,
        [STATUS]       [int]           NULL,
        [COMMENT]      [nvarchar](255) NULL,
        [date_sabt]    [datetime]      NULL,
        [USER_NAME]    [nvarchar](50)  NULL,
        [pic]          [image]         NULL,
        [dt]           [int]           NULL,
        [userid]       [int]           NULL,
        [Longitude]    [float]         NULL,
        [Latitude]     [float]         NULL,
        [OSTANID]      [int]           NULL,
        [SHAHRID]      [int]           NULL,
        [CRT]          [datetime]      NULL CONSTRAINT [DF_COPMANES_CRT] DEFAULT (GETDATE()),
        [UID]          [int]           NULL,
        CONSTRAINT [PK_COPMANES] PRIMARY KEY CLUSTERED ([id])
    );
END
GO

IF OBJECT_ID(N'dbo.CRMEVENTS', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CRMEVENTS]
    (
        [idde]         [int] IDENTITY(1,1) NOT NULL,
        [COMPANY_NAME] [nvarchar](100)  NULL,
        [INFO_DATE]    [int]            NULL,
        [INFO_TIME]    [int]            NULL,
        [SALER]        [nvarchar](50)   NULL,
        [BUYER]        [nvarchar](50)   NULL,
        [COMMENT]      [nvarchar](2000) NULL,
        [NEXT_DATE]    [int]            NULL,
        [NEXT_TIME]    [int]            NULL,
        [STATUS]       [int]            NULL,
        [pic]          [image]          NULL,
        [idc]          [int]            NULL,
        [PAYAM]        [nvarchar](500)  NULL,
        [miting]       [int]            NULL CONSTRAINT [DF_CRMEVENTS_miting] DEFAULT ((0)),
        [USERID]       [int]            NULL,
        [CDATETI]      [datetime]       NULL,
        [CRT]          [datetime]       NULL CONSTRAINT [DF_CRMEVENTS_CRT] DEFAULT (GETDATE()),
        [UID]          [int]            NULL,
        CONSTRAINT [PK_CRMEVENTS] PRIMARY KEY CLUSTERED ([idde])
    );

    -- در تولید WITH NOCHECK است، چون رکوردهای قدیمی اعتبارسنجی نشده‌اند.
    ALTER TABLE [dbo].[CRMEVENTS] WITH NOCHECK
        ADD CONSTRAINT [FK_CRMEVENTS_COPMANES]
        FOREIGN KEY ([idc]) REFERENCES [dbo].[COPMANES] ([id]) ON UPDATE CASCADE;
END
GO

IF OBJECT_ID(N'dbo.Notes', N'U') IS NULL
BEGIN
    -- ⚠️ نوع Ndate و Ntime عمداً با CrmNoteDto نمی‌خواند — در تولید هم همین است.
    CREATE TABLE [dbo].[Notes]
    (
        [idd]    [int] IDENTITY(1,1) NOT NULL,
        [Note]   [nvarchar](250) NULL,
        [Ndate]  [nchar](10)     NULL,
        [Ntime]  [datetime]      NULL,
        [userid] [int]           NOT NULL,
        [Ndone]  [bit]           NOT NULL CONSTRAINT [DF_Notes_Ndone] DEFAULT ((0)),
        [CRT]    [datetime]      NULL CONSTRAINT [DF_Notes_CRT] DEFAULT (GETDATE()),
        [UID]    [int]           NULL,
        CONSTRAINT [PK_Notes] PRIMARY KEY CLUSTERED ([idd])
    );
END
GO

/* عنوان نُه وضعیت CRM از SAZMAN خوانده می‌شود */
IF OBJECT_ID(N'dbo.SAZMAN', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAZMAN]
    (
        [IT1] [nvarchar](50) NULL, [IT2] [nvarchar](50) NULL, [IT3] [nvarchar](50) NULL,
        [IT4] [nvarchar](50) NULL, [IT5] [nvarchar](50) NULL, [IT6] [nvarchar](50) NULL,
        [IT7] [nvarchar](50) NULL, [IT8] [nvarchar](50) NULL, [IT9] [nvarchar](50) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SAZMAN)
    INSERT INTO dbo.SAZMAN (IT1, IT2, IT3, IT4, IT5, IT6, IT7, IT8, IT9)
    VALUES (N'در انتظار خرید', N'تماس اول', N'ارسال نمونه', N'عدم پاسخ',
            N'برگزاری جلسه', N'در حال مذاکره', N'مشتری', N'نمونه تایید نشد', N'غیر مربوط');
ELSE
    -- legacy_dependencies.sql ردیف SAZMAN را با مقادیر تهی می‌سازد؛
    -- بدون این، عنوان وضعیت‌ها در UI «وضعیت ۱..۹» می‌شود.
    UPDATE dbo.SAZMAN SET
        IT1 = ISNULL(IT1, N'در انتظار خرید'), IT2 = ISNULL(IT2, N'تماس اول'),
        IT3 = ISNULL(IT3, N'ارسال نمونه'),    IT4 = ISNULL(IT4, N'عدم پاسخ'),
        IT5 = ISNULL(IT5, N'برگزاری جلسه'),   IT6 = ISNULL(IT6, N'در حال مذاکره'),
        IT7 = ISNULL(IT7, N'مشتری'),          IT8 = ISNULL(IT8, N'نمونه تایید نشد'),
        IT9 = ISNULL(IT9, N'غیر مربوط');
GO

/* دفترچه تلفن از CUST_HESAB می‌خواند. در دیتابیس تست (و در تولید) این یک
   View است که legacy_dependencies.sql می‌سازد — پس نه ساخته می‌شود و نه
   داده در آن درج می‌شود.

   ⚠️ تله: نوشتن INSERT روی این نام حتی داخل بلوکی که هرگز اجرا نمی‌شود،
   کل batch را در زمان کامپایل می‌شکند («contains a derived or constant
   field»)، چون SQL Server نامِ موجود را همان‌جا resolve می‌کند. */
IF OBJECT_ID(N'dbo.CUST_HESAB') IS NULL
    THROW 52202, 'View CUST_HESAB وجود ندارد — legacy_dependencies.sql اجرا نشده.', 1;
GO

IF OBJECT_ID(N'dbo.cust_hesab_dtl', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[cust_hesab_dtl]
    (
        [NAME]  [nvarchar](200) NULL,
        [TNAME] [nvarchar](200) NULL
    );
END
GO

/* ── ۲. داده‌ی آزمایشی ───────────────────────────────────────────────────
   الگوی داده عمداً همان چیزی است که روی دیتابیس واقعی مشتری دیده شد:
   چند مالک واقعی، و چند رکورد «بی‌مالک» که userid ندارند ولی USER_NAME
   دارند (ساخته‌ی نرم‌افزار WPF).

   کاربران از test_auth_and_acl_users.sql:
     9001 payadmin  → در ادامه مجوز CRMALL می‌گیرد (همه را می‌بیند)
     9002 payviewer → فقط داده‌ی خودش
     9003 payscoped → فقط داده‌ی خودش
   ──────────────────────────────────────────────────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM dbo.COPMANES)
BEGIN
    INSERT INTO dbo.COPMANES (COMPANY_NAME, CITY, MANAGER, FACT_TEL, MOBILE, STATUS, COMMENT, date_sabt, USER_NAME, dt, userid)
    VALUES
        (N'شرکت آلفا — مال payadmin',  N'یزد',   N'مدیر آلفا',  N'03531000001', N'09131000001', 1, N'مالک: payadmin',  GETDATE(), N'payadmin',  14040101, 9001),
        (N'شرکت بتا — مال payadmin',   N'یزد',   N'مدیر بتا',   N'03531000002', N'09131000002', 2, N'مالک: payadmin',  GETDATE(), N'payadmin',  14040102, 9001),
        (N'شرکت گاما — مال payviewer', N'تهران', N'مدیر گاما',  N'02131000003', N'09131000003', 3, N'مالک: payviewer', GETDATE(), N'payviewer', 14040103, 9002),
        (N'شرکت دلتا — مال payviewer', N'تهران', N'مدیر دلتا',  N'02131000004', N'09131000004', 6, N'مالک: payviewer', GETDATE(), N'payviewer', 14040104, 9002),
        (N'شرکت اپسیلون — مال payscoped', N'شیراز', N'مدیر اپسیلون', N'07131000005', N'09131000005', 1, N'مالک: payscoped', GETDATE(), N'payscoped', 14040105, 9003),
        -- رکورد سبک WPF: userid ندارد، فقط USER_NAME
        (N'شرکت زتا — بی‌مالک سبک WPF', N'اصفهان', N'مدیر زتا', N'03131000006', N'09131000006', 1, N'userid ندارد، USER_NAME = payviewer', GETDATE(), N'payviewer', 14040106, NULL);

    INSERT INTO dbo.CRMEVENTS (COMPANY_NAME, INFO_DATE, INFO_TIME, SALER, COMMENT, NEXT_DATE, NEXT_TIME, STATUS, idc, miting, USERID, CDATETI)
    SELECT C.COMPANY_NAME, 14040110, 1000, C.USER_NAME,
           N'پیگیری آزمایشی برای ' + C.COMPANY_NAME,
           14049999, 1100, C.STATUS, C.id, 0, C.userid, GETDATE()
    FROM dbo.COPMANES C;

    INSERT INTO dbo.Notes (Note, Ndate, Ntime, userid, Ndone)
    VALUES (N'یادداشت payadmin',  N'14040110', GETDATE(), 9001, 0),
           (N'یادداشت payviewer', N'14040110', GETDATE(), 9002, 0);
END
GO

/* ── ۳. مجوز CRM برای کاربران آزمایشی ───────────────────────────────────
   هر سه کاربر CRMMAIN را کامل می‌گیرند (تا صفحه برایشان باز شود)، ولی
   فقط payadmin مجوز CRMALL می‌گیرد — یعنی فقط او همه را می‌بیند.
   ──────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.TFORMS', N'U') IS NOT NULL
BEGIN
    -- CRMMAIN روی دیتابیس تست وجود ندارد چون در schema.sql نیست؛ ساخته می‌شود.
    IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'CRMMAIN')
        INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'CRMMAIN', N'CRM', 3, 16, (SELECT ISNULL(MAX(IDH), 0) + 1 FROM dbo.TFORMS), GETDATE());

    MERGE dbo.SAL_CHEK AS T
    USING (
        SELECT U.USERCO, F.IDH AS [OBJECT]
        FROM (VALUES (9001), (9002), (9003)) AS U(USERCO)
        CROSS JOIN (SELECT IDH FROM dbo.TFORMS WHERE FORMNAME = N'CRMMAIN') AS F
        UNION ALL
        -- فقط payadmin: «مشاهده CRM همه کاربران»
        SELECT 9001, F.IDH FROM dbo.TFORMS F WHERE F.FORMNAME = N'CRMALL'
    ) AS S (USERCO, [OBJECT])
        ON T.USERCO = S.USERCO AND T.[OBJECT] = S.[OBJECT]
    WHEN MATCHED THEN UPDATE SET
        T.[RUN] = 1, T.[SEE] = 1, T.[INP] = 1, T.[UPD] = 1, T.[DEL] = 1
    WHEN NOT MATCHED THEN
        INSERT (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], CRT)
        VALUES (S.USERCO, S.[OBJECT], 1, 1, 1, 1, 1, GETDATE());
END
GO

/* ── ۴. راستی‌آزمایی ─────────────────────────────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'CRMALL')
    THROW 52200, 'فرم CRMALL وجود ندارد — crm_acl_migration.sql قبل از این فایل اجرا نشده.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY = N'CRM_ACL_ENFORCE')
    THROW 52201, 'کلید CRM_ACL_ENFORCE وجود ندارد — crm_acl_migration.sql بعد از pay2_seed.sql اجرا نشده.', 1;

SELECT (SELECT COUNT(*) FROM dbo.COPMANES)  AS Companies,
       (SELECT COUNT(*) FROM dbo.CRMEVENTS) AS Events,
       (SELECT COUNT(*) FROM dbo.Notes)     AS Notes,
       (SELECT CFG_VALUE FROM dbo.PAY2_CONFIG WHERE CFG_KEY = N'CRM_ACL_ENFORCE') AS CrmAclEnforce,
       (SELECT COUNT(*) FROM dbo.SAL_CHEK SC
        INNER JOIN dbo.TFORMS F ON F.IDH = SC.[OBJECT]
        WHERE F.FORMNAME = N'CRMALL' AND SC.[RUN] = 1) AS UsersWithSeeAll;
GO
