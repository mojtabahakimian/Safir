using Dapper;
using Microsoft.Data.SqlClient;
using Prg_SendInvoice.CNNMANAGER;
using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace Prg_UI.Scriptses
{
    public static class ScriptSqly
    {
        /// <summary>
        /// Update Database Via Scripts ...
        /// </summary>
        public static void LetsGo(bool isCustomCall = false, int _type_ = -1)
        {
            using (var db = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
            {
                db.Open();
                SalaryScript(true, db);
                CostCloseScript(db);
            }
        }

        /// <summary>
        /// ماژول «بستن ماه بهای تمام‌شده» (پیشوند CC_).
        ///
        /// هر ده اسکریپت Server/Database/10-schema.sql تا
        /// 19-margin-fix-kalas.sql عیناً اینجا کپی شده‌اند، به همان ترتیب
        /// وابستگی: اول جدول‌های پایه، بعد داده اولیه، بعد رویه‌ها. پس
        /// اجرای این فایل روی یک پایگاه تازه هم کامل بالا می‌آید و
        /// پیش‌نیاز دستی ندارد.
        ///
        /// همه بلوک‌ها idempotent هستند (CREATE OR ALTER برای رویه‌ها،
        /// IF NOT EXISTS برای جدول‌ها و داده اولیه) چون این فایل هر بار
        /// دوباره روی همان پایگاه اجرا می‌شود.
        ///
        /// اگر متن یکی از بلوک‌ها را عوض کردید، همان تغییر را در فایل
        /// .sql متناظرش هم بگذارید؛ این دو باید مو‌به‌مو یکی بمانند.
        /// </summary>
        private static void CostCloseScript(SqlConnection db)
        {
            // ترتیب مهم است: بلوک‌های پایه (۱۰ تا ۱۳) جدول‌ها و رویه‌هایی را
            // می‌سازند که بقیه بلوک‌ها به آن‌ها وابسته‌اند.
            string baseSchema = @"/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۱ از ۳ : ساختار جداول

   هیچ جدول موجودی تغییر نمی‌کند. همه چیز با پیشوند CC_ اضافه می‌شود.
   قابل اجرای مکرر: اگر جدولی از قبل باشد، دست‌نخورده می‌ماند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، CC_ItemCost که ستون محاسباتی PERSISTED دارد (TotalCost)
-- در صورت خاموش بودن QUOTED_IDENTIFIER پیش‌فرض نشست/پایگاه، همان خطای
-- 1934 را که در رویه‌ها دیدیم، هنگام خودِ CREATE TABLE می‌دهد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────────────── اجرا و گام‌ها ───────────────────────── */

IF OBJECT_ID('dbo.CC_Run','U') IS NULL
CREATE TABLE dbo.CC_Run (
    RunId          INT IDENTITY(1,1) PRIMARY KEY,
    FiscalYear     SMALLINT      NOT NULL,
    PeriodMonth    TINYINT       NOT NULL,          -- ۱ تا ۱۲ = HEAD_MANF.GHEYMAT
    DateFrom       BIGINT        NOT NULL,          -- 14050401
    DateTo         BIGINT        NOT NULL,          -- 14050431
    RunNo          SMALLINT      NOT NULL,          -- شماره اجرا در همان ماه
    PrevRunId      INT           NULL,
    IsLatest       BIT           NOT NULL DEFAULT 1,
    RunKind        TINYINT       NOT NULL,          -- 1=آزمایشی 2=قطعی
    Status         TINYINT       NOT NULL,          -- 0=پیش‌نویس 1=درحال‌اجرا 2=متوقف
                                                    -- 3=تکمیل 4=خطا 5=بازگردانی‌شده
    FormulasDirty  BIT           NOT NULL DEFAULT 0,
    StartedAtUtc   DATETIME2     NULL,
    FinishedAtUtc  DATETIME2     NULL,
    StartedByUser  NVARCHAR(50)  NOT NULL,
    ApprovedByUser NVARCHAR(50)  NULL,
    ApprovedAtUtc  DATETIME2     NULL,
    Note           NVARCHAR(500) NULL,
    CreatedAtUtc   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_Run_Period')
    CREATE INDEX IX_CC_Run_Period ON dbo.CC_Run(FiscalYear, PeriodMonth, Status);
GO

IF OBJECT_ID('dbo.CC_RunStep','U') IS NULL
CREATE TABLE dbo.CC_RunStep (
    RunStepId     INT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT           NOT NULL REFERENCES dbo.CC_Run(RunId),
    StepCode      VARCHAR(10)   NOT NULL,
    StepTitle     NVARCHAR(120) NOT NULL,
    SeqNo         SMALLINT      NOT NULL,
    Attempt       TINYINT       NOT NULL DEFAULT 1,
    Status        TINYINT       NOT NULL,           -- 0=درانتظار 1=درحال‌اجرا 2=موفق
                                                    -- 3=هشدار 4=خطا 5=رد‌شده
    StartedAtUtc  DATETIME2     NULL,
    FinishedAtUtc DATETIME2     NULL,
    DurationMs    INT           NULL,
    RowsAffected  INT           NULL,
    ResultJson    NVARCHAR(MAX) NULL,
    ErrorMessage  NVARCHAR(MAX) NULL,
    CONSTRAINT UQ_CC_RunStep UNIQUE (RunId, StepCode, Attempt)
);
GO

IF OBJECT_ID('dbo.CC_RunLog','U') IS NULL
CREATE TABLE dbo.CC_RunLog (
    LogId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId       INT            NULL,
    StepCode    VARCHAR(10)    NULL,
    LoggedAtUtc DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    Severity    TINYINT        NOT NULL,            -- 0=ریز 1=اطلاع 2=هشدار 3=خطا
    Message     NVARCHAR(2000) NOT NULL,
    ContextJson NVARCHAR(MAX)  NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_RunLog_Run')
    CREATE INDEX IX_CC_RunLog_Run ON dbo.CC_RunLog(RunId, LogId);
GO

/* ───────────────────────── اسنپ‌شات و بازگردانی ───────────────────────── */

IF OBJECT_ID('dbo.CC_Snapshot','U') IS NULL
CREATE TABLE dbo.CC_Snapshot (
    SnapshotId    INT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT       NOT NULL,
    StepCode      VARCHAR(10) NOT NULL,
    TableName     SYSNAME   NOT NULL,
    BackupTable   SYSNAME   NOT NULL,
    RowsCopied    INT       NOT NULL,
    TakenAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RestoredAtUtc DATETIME2 NULL
);
GO

/* ───────────────────────── قواعد تشخیص و استثناها ───────────────────────── */

IF OBJECT_ID('dbo.CC_CheckRule','U') IS NULL
CREATE TABLE dbo.CC_CheckRule (
    RuleCode        VARCHAR(12)   NOT NULL PRIMARY KEY,
    RuleName        NVARCHAR(120) NOT NULL,
    StepCode        VARCHAR(10)   NOT NULL,
    ExType          TINYINT       NOT NULL,
    DefaultSeverity TINYINT       NOT NULL,        -- 1=هشدار 2=مسدودکننده
    Threshold       FLOAT         NULL,
    RemedyText      NVARCHAR(600) NOT NULL,
    IsActive        BIT           NOT NULL DEFAULT 1,
    SortOrder       SMALLINT      NOT NULL
);
GO

IF OBJECT_ID('dbo.CC_Exception','U') IS NULL
CREATE TABLE dbo.CC_Exception (
    ExceptionId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId          INT            NULL,
    StepCode       VARCHAR(10)    NOT NULL,
    RuleCode       VARCHAR(12)    NULL,
    ExType         TINYINT        NOT NULL,
    Severity       TINYINT        NOT NULL,
    Anbar          INT            NULL,
    Code           BIGINT         NULL,
    DocNumber      INT            NULL,
    DocTag         INT            NULL,
    DocDate        BIGINT         NULL,
    Amount         FLOAT          NULL,
    Description    NVARCHAR(500)  NOT NULL,
    IsResolved     BIT            NOT NULL DEFAULT 0,
    ResolvedBy     NVARCHAR(50)   NULL,
    ResolvedAtUtc  DATETIME2      NULL,
    ResolutionNote NVARCHAR(500)  NULL,
    CreatedAtUtc   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF COL_LENGTH('dbo.CC_Exception','RuleCode') IS NULL
    ALTER TABLE dbo.CC_Exception ADD RuleCode VARCHAR(12) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_Exception_Run')
    CREATE INDEX IX_CC_Exception_Run
        ON dbo.CC_Exception(RunId, StepCode, IsResolved, Severity);
GO

/* استثناهایی که کاربر یک‌بار پذیرفته و نباید هر ماه تکرار شوند */
IF OBJECT_ID('dbo.CC_AcceptedException','U') IS NULL
CREATE TABLE dbo.CC_AcceptedException (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    RuleCode     VARCHAR(12)   NOT NULL,
    Code         BIGINT        NULL,        -- کالا؛ NULL يعني همه
    FNUMB        INT           NULL,        -- فرمول؛ NULL يعني همه
    Reason       NVARCHAR(400) NOT NULL,
    AcceptedBy   NVARCHAR(50)  NOT NULL,
    AcceptedAtUtc DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME(),
    IsActive     BIT           NOT NULL DEFAULT 1
);
GO

/* ───────────────────────── واحدهای تولیدی ───────────────────────── */

IF OBJECT_ID('dbo.CC_Unit','U') IS NULL
CREATE TABLE dbo.CC_Unit (
    UnitId     INT IDENTITY(1,1) PRIMARY KEY,
    UnitName   NVARCHAR(60) NOT NULL,
    Depatman   INT          NULL,
    SplitMode  TINYINT      NOT NULL DEFAULT 1,   -- 1=يک ضريب 2=دو ضريب
    IsActive   BIT          NOT NULL DEFAULT 1,
    SeqNo      SMALLINT     NOT NULL DEFAULT 1
);
GO

IF OBJECT_ID('dbo.CC_UnitAnbar','U') IS NULL
CREATE TABLE dbo.CC_UnitAnbar (
    UnitId       INT      NOT NULL REFERENCES dbo.CC_Unit(UnitId),
    Anbar        INT      NOT NULL,
    AnbarRole    TINYINT  NOT NULL,   -- 1=مواد مصرفي توليد 2=مواد اوليه
                                      -- 3=محصول 4=ساير
    DoStockCount BIT      NOT NULL DEFAULT 1,
    SeqNo        SMALLINT NOT NULL DEFAULT 1,
    PRIMARY KEY (UnitId, Anbar)
);
GO

IF OBJECT_ID('dbo.CC_UnitAcc','U') IS NULL
CREATE TABLE dbo.CC_UnitAcc (
    Id         INT           IDENTITY(1,1) PRIMARY KEY,
    UnitId     INT           NOT NULL REFERENCES dbo.CC_Unit(UnitId),
    HesKol     INT           NOT NULL,
    HesMoin    INT           NULL,   -- خالی = همه معین‌های این کل
    HesTafsili INT           NULL,   -- خالی = همه تفصیلی‌های همان معین
    CostKind   TINYINT       NOT NULL,          -- 1=دستمزد 2=سربار
    Ratio      DECIMAL(9,6)  NOT NULL DEFAULT 1,
    IsActive   BIT           NOT NULL DEFAULT 1,
    Note       NVARCHAR(200) NULL,
    CONSTRAINT UQ_CC_UnitAcc UNIQUE (UnitId, HesKol, HesMoin, HesTafsili)
);
GO

-- روی نصب‌های قدیمی‌تر که این جدول را بدون سطح معین/تفصیلی دارند
IF COL_LENGTH('dbo.CC_UnitAcc','HesMoin') IS NULL
    ALTER TABLE dbo.CC_UnitAcc ADD HesMoin INT NULL;
GO
IF COL_LENGTH('dbo.CC_UnitAcc','HesTafsili') IS NULL
    ALTER TABLE dbo.CC_UnitAcc ADD HesTafsili INT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UQ_CC_UnitAcc' AND parent_object_id = OBJECT_ID('dbo.CC_UnitAcc'))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.name = 'UQ_CC_UnitAcc' AND c.name = 'HesMoin')
BEGIN
    ALTER TABLE dbo.CC_UnitAcc DROP CONSTRAINT UQ_CC_UnitAcc;
    ALTER TABLE dbo.CC_UnitAcc ADD CONSTRAINT UQ_CC_UnitAcc
        UNIQUE (UnitId, HesKol, HesMoin, HesTafsili);
END
GO

-- نگاشت انبار به حساب موجودی جنسی (کل/معین)، برای CHK-02.
-- TCOD_ANBAR هیچ ستون حسابداری ندارد و این نگاشت شرکت‌به‌شرکت فرق
-- می‌کند (هر انبار زیر یک معین جداگانه در حسابداری ثبت می‌شود، نه یک
-- معین ثابت مشترک) — پس باید از تنظیمات وارد شود، نه هاردکد در کد.
IF OBJECT_ID('dbo.CC_AnbarHes','U') IS NULL
CREATE TABLE dbo.CC_AnbarHes (
    Anbar    INT           NOT NULL PRIMARY KEY,
    HesKol   INT           NOT NULL,
    HesMoin  INT           NOT NULL,
    Note     NVARCHAR(200) NULL
);
GO

/* ───────────────────────── نتایج محاسبه ───────────────────────── */

IF OBJECT_ID('dbo.CC_ItemCost','U') IS NULL
CREATE TABLE dbo.CC_ItemCost (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId        INT      NULL,
    PeriodMonth  TINYINT  NOT NULL,
    Code         BIGINT   NOT NULL,
    LowLevelCode SMALLINT NOT NULL,
    SourceKind   TINYINT  NOT NULL,      -- 1=ميانگين انبار 2=فرمول 3=بدون منبع
    FNUMB        INT      NULL,
    MaterialCost FLOAT    NOT NULL DEFAULT 0,
    WageCost     FLOAT    NOT NULL DEFAULT 0,
    OverheadCost FLOAT    NOT NULL DEFAULT 0,
    TotalCost    AS (MaterialCost + WageCost + OverheadCost) PERSISTED,
    CalculatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_ItemCost_Lookup')
    CREATE INDEX IX_CC_ItemCost_Lookup ON dbo.CC_ItemCost(PeriodMonth, Code, RunId);
GO

IF OBJECT_ID('dbo.CC_FormulaChange','U') IS NULL
CREATE TABLE dbo.CC_FormulaChange (
    ChangeId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId        INT           NOT NULL,
    StepCode     VARCHAR(10)   NOT NULL,
    FNUMB        INT           NOT NULL,
    ParentCode   BIGINT        NULL,
    ChildCode    BIGINT        NULL,
    FieldName    VARCHAR(20)   NOT NULL,   -- SMABL MABLK MEGHK PERT
                                           -- IMBIBE_MANF IMBIBE_SAR
    OldValue     FLOAT         NULL,
    NewValue     FLOAT         NULL,
    Reason       NVARCHAR(200) NULL,
    ChangedAtUtc DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_FormulaChange_Run')
    CREATE INDEX IX_CC_FormulaChange_Run ON dbo.CC_FormulaChange(RunId, StepCode, FNUMB);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_FormulaChange_Code')
    CREATE INDEX IX_CC_FormulaChange_Code ON dbo.CC_FormulaChange(ChildCode, RunId);
GO

/* ───────────────────────── انحراف و تصمیم‌ها ───────────────────────── */

IF OBJECT_ID('dbo.CC_Variance','U') IS NULL
CREATE TABLE dbo.CC_Variance (
    VarianceId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId          INT    NOT NULL,
    Anbar          INT    NOT NULL,
    Code           BIGINT NOT NULL,
    QtyVariance    FLOAT  NOT NULL,
    UnitRate       FLOAT  NULL,
    AmountVariance FLOAT  NULL,
    ConsumedQty    FLOAT  NULL,
    IsKeyItem      BIT    NOT NULL DEFAULT 0,
    CONSTRAINT UQ_CC_Variance UNIQUE (RunId, Anbar, Code)
);
GO

IF OBJECT_ID('dbo.CC_VarianceDecision','U') IS NULL
CREATE TABLE dbo.CC_VarianceDecision (
    DecisionId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId        INT           NOT NULL,
    Code         BIGINT        NOT NULL,
    Mode         TINYINT       NOT NULL,   -- 1=اختصاص 2=تسهيم 3=بدون تخصيص
    TargetCode   BIGINT        NULL,       -- کليد پايدار بين ماه‌ها
    TargetFNUMB  INT           NULL,       -- فرمول ماه جاري، مشتق از TargetCode
    AppliedQty   FLOAT         NULL,
    DecidedBy    NVARCHAR(50)  NOT NULL,
    DecidedAtUtc DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    Note         NVARCHAR(300) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_VarDecision_Code')
    CREATE INDEX IX_CC_VarDecision_Code ON dbo.CC_VarianceDecision(Code, RunId);
GO

/* ───────────────────────── هزینه تبدیل و حاشیه سود ───────────────────────── */

IF OBJECT_ID('dbo.CC_ConversionCost','U') IS NULL
CREATE TABLE dbo.CC_ConversionCost (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    RunId            INT           NOT NULL,
    UnitId           INT           NOT NULL,
    CostKind         TINYINT       NOT NULL,   -- 0=کل 1=دستمزد 2=سربار
    AbsorbedAmount   DECIMAL(19,0) NOT NULL,
    AbsorbedFromWip  DECIMAL(19,0) NULL,
    ActualAmount     DECIMAL(19,0) NOT NULL,
    ActualDetailJson NVARCHAR(MAX) NULL,
    AdjustFactor     DECIMAL(18,8) NOT NULL,
    ApprovedBy       NVARCHAR(50)  NULL,
    CONSTRAINT UQ_CC_ConversionCost UNIQUE (RunId, UnitId, CostKind)
);
GO

IF OBJECT_ID('dbo.CC_MarginTarget','U') IS NULL
CREATE TABLE dbo.CC_MarginTarget (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Code           BIGINT       NOT NULL,
    TargetKind     TINYINT      NOT NULL,   -- 1=سود صفر 2=درصد مشخص 3=آزاد
    TargetPct      DECIMAL(9,4) NULL,
    BalancingCode  BIGINT       NULL,
    BalancingFNUMB INT          NULL,
    IsActive       BIT          NOT NULL DEFAULT 1,
    Note           NVARCHAR(300) NULL
);
GO

PRINT N'ساختار جداول ايجاد شد.';

SELECT  t.name AS جدول,
        (SELECT SUM(p.rows) FROM sys.partitions p
         WHERE p.object_id = t.object_id AND p.index_id IN (0,1)) AS تعداد_سطر
FROM    sys.tables t
WHERE   t.name LIKE 'CC[_]%'
ORDER BY t.name;
GO
";
            TryExecuteCostCloseBatch(db, baseSchema,
                "جدول‌های پایه CC_*",
                "اسکریپت 10-schema.sql را اجرا کنید.");

            string seedData = @"
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
 ('CHK-01', N'کاردکس منفی', 'S05', 1, 2, NULL,
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
  N'مقدار منفی در یک سطر فرمول قابل قبول نیست و باعث می‌شود مانده حساب کالای در جریان ساخت (۷۵۱) هرگز متوازن نشود. با دکمه اصلاح، آن سطر را صفر یا حذف کنید.', 75)
) AS s (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
ON t.RuleCode = s.RuleCode
WHEN MATCHED THEN UPDATE SET
    t.RuleName = s.RuleName, t.StepCode = s.StepCode, t.ExType = s.ExType,
    t.DefaultSeverity = s.DefaultSeverity, t.Threshold = s.Threshold,
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
";
            TryExecuteCostCloseBatch(db, seedData,
                "قواعد تشخیص و واحدهای تولیدی",
                "اسکریپت 11-seed-data.sql را اجرا کنید (به CC_CheckRule و CC_Unit نیاز دارد).");

            string phase1Procs = @"
/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۳ از ۳ : رویه‌ها

   مدیریت اجرا، اسنپ‌شات و بازگردانی، و گام‌های S00 تا S04.
   قابل اجرای مکرر (همه با CREATE OR ALTER).

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، رویه‌هایی که به CC_ItemCost/CC_ItemMargin می‌نویسند
-- (ستون‌های محاسباتی PERSISTED) با خطای 1934 شکست می‌خورند.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════ مدیریت اجرا ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_RunCreate
    @FiscalYear SMALLINT,
    @Month      TINYINT,
    @DateFrom   BIGINT,
    @DateTo     BIGINT,
    @RunKind    TINYINT,          -- 1=آزمايشي 2=قطعي
    @UserName   NVARCHAR(50),
    @Note       NVARCHAR(500) = NULL,
    @RunId      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
                 AND RunKind = 2 AND Status = 3 AND ApprovedAtUtc IS NOT NULL)
    BEGIN
        RAISERROR(N'براي اين ماه يک اجراي قطعي تأييدشده وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month AND Status = 1)
    BEGIN
        RAISERROR(N'يک اجرا براي اين ماه در حال انجام است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @no SMALLINT =
        ISNULL((SELECT MAX(RunNo) FROM dbo.CC_Run
                WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month), 0) + 1;

    DECLARE @prev INT =
        (SELECT TOP 1 RunId FROM dbo.CC_Run
         WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
         ORDER BY RunNo DESC);

    UPDATE dbo.CC_Run SET IsLatest = 0
    WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month;

    INSERT dbo.CC_Run (FiscalYear, PeriodMonth, DateFrom, DateTo, RunNo,
                       PrevRunId, IsLatest, RunKind, Status, StartedByUser, Note)
    VALUES (@FiscalYear, @Month, @DateFrom, @DateTo, @no,
            @prev, 1, @RunKind, 0, @UserName, @Note);

    SET @RunId = SCOPE_IDENTITY();

    INSERT dbo.CC_RunLog (RunId, Severity, Message)
    VALUES (@RunId, 1, CONCAT(N'اجراي شماره ', @no, N' براي دوره ',
                              @FiscalYear, '/', @Month, N' ايجاد شد'));

    COMMIT;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepStart
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Title    NVARCHAR(120),
    @SeqNo    SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @try TINYINT =
        ISNULL((SELECT MAX(Attempt) FROM dbo.CC_RunStep
                WHERE RunId = @RunId AND StepCode = @StepCode), 0) + 1;

    INSERT dbo.CC_RunStep (RunId, StepCode, StepTitle, SeqNo, Attempt, Status, StartedAtUtc)
    VALUES (@RunId, @StepCode, @Title, @SeqNo, @try, 1, SYSUTCDATETIME());

    UPDATE dbo.CC_Run
       SET Status = 1, StartedAtUtc = ISNULL(StartedAtUtc, SYSUTCDATETIME())
     WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepFinish
    @RunId     INT,
    @StepCode  VARCHAR(10),
    @Status    TINYINT,                     -- 2=موفق 3=هشدار 4=خطا 5=رد‌شده
    @Rows      INT           = NULL,
    @Result    NVARCHAR(MAX) = NULL,
    @Error     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE  s
       SET  s.Status        = @Status,
            s.FinishedAtUtc = SYSUTCDATETIME(),
            s.DurationMs    = DATEDIFF(MILLISECOND, s.StartedAtUtc, SYSUTCDATETIME()),
            s.RowsAffected  = @Rows,
            s.ResultJson    = @Result,
            s.ErrorMessage  = @Error
    FROM    dbo.CC_RunStep s
    JOIN   (SELECT RunId, StepCode, MAX(Attempt) AS Attempt
            FROM   dbo.CC_RunStep
            WHERE  RunId = @RunId AND StepCode = @StepCode
            GROUP BY RunId, StepCode) x
           ON x.RunId = s.RunId AND x.StepCode = s.StepCode AND x.Attempt = s.Attempt;

    IF @Status = 4
        UPDATE dbo.CC_Run SET Status = 4 WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_SetFormulasDirty
    @RunId INT, @Dirty BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.CC_Run SET FormulasDirty = @Dirty WHERE RunId = @RunId;
END
GO


/* ═══════════════ اسنپ‌شات و بازگردانی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_Snapshot
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Month    TINYINT,
    @DT1      BIGINT,
    @DT2      BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @bak SYSNAME, @sql NVARCHAR(MAX), @n INT;

    ---- DTL_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_DTL_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT d.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DTL_MANF d
                 JOIN dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DTL_MANF', @bak, @n);

    ---- HEAD_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_HEAD_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT h.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.HEAD_MANF h WHERE h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'HEAD_MANF', @bak, @n);

    ---- DEED_HED : اسنپ‌شات کامل اسناد بازه، به‌همراه اسناد پس از @DT2 هم —
    -- چون شاخهٔ جابه‌جايي CC_sp_S04_SortDeeds مي‌تواند شمارهٔ اسناد بعد از
    -- پايان ماه را هم عوض کند تا با شمارهٔ تازهٔ اسناد اين ماه تلاقي نکند؛
    -- اگر آن اسناد اينجا اسنپ‌شات نشوند، Rollback راهي براي برگرداندن
    -- شماره‌شان ندارد. ستون‌ها هم کامل ذخيره مي‌شوند (نه فقط base/N_S/DATE_S)
    -- تا اگر CC_sp_S03_DeleteEmptyDeeds سندي را کامل حذف کرد، Rollback
    -- بتواند کل سطر را دوباره درج کند، نه فقط شماره‌اش را برگرداند.
    SET @bak = CONCAT('CC_BAK_DEED_HED_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT * INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DEED_HED WHERE DATE_S >= @a';
    EXEC sp_executesql @sql, N'@a BIGINT', @a = @DT1;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DEED_HED', @bak, @n);

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 1, N'اسنپ‌شات گرفته شد');

    SELECT TableName AS جدول, BackupTable AS جدول_پشتيبان, RowsCopied AS تعداد_سطر
    FROM   dbo.CC_Snapshot
    WHERE  RunId = @RunId AND StepCode = @StepCode;
END
GO


/* ═══════════════ S00 — بازبینی ابتدای ماه ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S00_Preflight
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  StepCode = 'S00' AND ISNULL(RunId, -1) = ISNULL(@RunId, -1);

    ---- CHK-03 : فرمول بدون نرخ جذب
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-03', 9, r.DefaultSeverity,
            MIN(CAST(pl.CODE AS BIGINT)), hm.FNUMB, MAX(h.DATE_N), SUM(pl.MEGHK),
            CONCAT(N'فرمول ', hm.FNUMB, N' نرخ جذب هزينه تبديل ندارد')
    FROM    dbo.HEAD_LST  h
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
    CROSS   JOIN dbo.CC_CheckRule r
    WHERE   r.RuleCode = 'CHK-03' AND r.IsActive = 1
      AND   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(hm.IMBIBE_MANF,0) + ISNULL(hm.IMBIBE_SAR,0) = 0
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-03' AND ae.IsActive = 1
                          AND (ae.Code IS NULL
                               OR ae.Code = CAST(pl.CODE AS BIGINT)))
    GROUP BY hm.FNUMB, r.DefaultSeverity;

    ---- CHK-04 : کالاي توليدشده بدون فرمول ماه
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-04', 12, 2, CAST(pl.CODE AS BIGINT),
            N'کالا در اين ماه توليد شده ولي فرمول ماه را ندارد'
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                          AND hm.GHEYMAT = @Month);

    ---- CHK-05 : ماده بدون منبع نرخ
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-05', 4, 1, CAST(d.CODE AS BIGINT),
            N'ماده بدون منبع نرخ — نرخ صفر به کالاهاي بالادست منتقل مي‌شود'
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF p
                        WHERE CAST(p.CODE AS BIGINT) = CAST(d.CODE AS BIGINT)
                          AND p.GHEYMAT = @Month)
      AND   NOT EXISTS (SELECT 1 FROM dbo.KALAS k
                        WHERE k.code = CAST(d.CODE AS BIGINT)
                          AND k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0);

    ---- CHK-15 : فرمول با مقدار منفی
    -- مقدار منفی در فرمول یعنی مانده حساب کالای در جریان ساخت (۷۵۱) هرگز
    -- متوازن نمی‌شود (CHK-07)؛ چون خروج مواد از روی همین عدد بازتولید
    -- می‌شود. کد سطر (DTL_MANF.id) در DocNumber ذخیره می‌شود تا اصلاح
    -- خودکار دقیقاً همان سطر را هدف بگیرد.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-15', 17, 2, CAST(d.CODE AS BIGINT),
            CAST(d.id AS INT), d.MEGH,
            CONCAT(N'فرمول ', h.FNUMB, N' مقدار منفی دارد: ', d.MEGH)
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   d.MEGH < 0 OR d.MEGHk < 0;

    ---- CHK-06 : حلقه در ساختار فرمول
    IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
    SELECT DISTINCT CAST(h.CODE AS BIGINT) AS P, CAST(d.CODE AS BIGINT) AS C
    INTO   #E
    FROM   dbo.HEAD_MANF h
    JOIN   dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE  h.GHEYMAT = @Month AND h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND  CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);
    CREATE CLUSTERED INDEX IX_E ON #E(P, C);

    ;WITH W AS (
        SELECT P AS Root, C, 1 AS L,
               CAST('/' + CAST(P AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM   #E
        UNION ALL
        SELECT w.Root, e.C, w.L + 1,
               CAST(w.Pt + CAST(e.P AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM   W w JOIN #E e ON e.P = w.C
        WHERE  w.L < 20
          AND  w.Pt NOT LIKE '%/' + CAST(e.C AS VARCHAR(20)) + '/%'
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT DISTINCT @RunId, 'S00', 'CHK-06', 5, 2, Root,
           N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
    FROM   W WHERE C = Root
    OPTION (MAXRECURSION 0);

    DROP TABLE #E;

    ---- CHK-07 : مانده نامتوازن مواد در ۷۵۱ (آستانه نسبي)
    DECLARE @th FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-07'), 0.001);

    -- DocNumber عمداً پر نمی‌شود: این قاعده مانده یک کالا را در کل بازه بررسی
    -- می‌کند، نه یک سند مشخص را؛ ستون HES_M (کد معین حسابداری) شمارهٔ برگهٔ
    -- تولید نیست و نمایشش به کاربر گمراه‌کننده بود.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-07', 13,
            CASE WHEN SUM(d.BED) = 0 OR SUM(d.BES) = 0 THEN 2 ELSE 1 END,
            TRY_CAST(d.HES_T AS BIGINT),
            SUM(d.BED) - SUM(d.BES),
            CASE WHEN SUM(d.BED) = 0
                 THEN N'ماده با توليد خارج شده ولي با حواله وارد نشده'
                 WHEN SUM(d.BES) = 0
                 THEN N'ماده با حواله وارد شده ولي با توليد خارج نشده'
                 ELSE N'مانده نامتوازن مواد در حساب کالاي در جريان ساخت' END
    FROM    dbo.DEED_DTL d
    JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
    WHERE   d.HES_K = 751 AND d.HES_T <> 99999999
      AND   hd.DATE_S BETWEEN @DT1 AND @DT2
    GROUP BY d.HES_M, d.HES_T
    HAVING  (SUM(d.BED) = 0 AND SUM(d.BES) <> 0)
         OR (SUM(d.BES) = 0 AND SUM(d.BED) <> 0)
         OR (ABS(SUM(d.BED) - SUM(d.BES))
             / NULLIF((SUM(d.BED) + SUM(d.BES)) / 2.0, 0) > @th);

    ---- CHK-09 : نرخ منتشرنشده نيمه‌ساخته
    DECLARE @th9 FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-09'), 0.001);

    ;WITH Khod AS (
        SELECT CAST(hm.CODE AS BIGINT) AS Code,
               SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                      + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha
        FROM   dbo.HEAD_MANF hm JOIN dbo.DTL_MANF d ON d.FNUMB = hm.FNUMB
        WHERE  hm.GHEYMAT = @Month
        GROUP BY CAST(hm.CODE AS BIGINT), hm.FNUMB
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code,
               AVG(d.SMABL) AS Nerkh, COUNT(DISTINCT d.FNUMB) AS Valedha
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            CONCAT(N'نرخ منتشر نشده — بهاي فرمول ', CAST(ROUND(k.Baha,0) AS BIGINT),
                   N' ولي نرخ در ', v.Valedha, N' فرمول بالادست ',
                   CAST(ROUND(v.Nerkh,0) AS BIGINT))
    FROM    Khod k
    JOIN    DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > @th9;

    ---- خلاصه
    SELECT  e.RuleCode AS قاعده, r.RuleName AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده' ELSE N'هشدار' END AS شدت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.StepCode = 'S00' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO


/* ═══════════════ S03 — حذف اسناد حسابداری خالی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S03_DeleteEmptyDeeds
    @RunId INT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 1                  -- ۱ = فقط گزارش، بدون حذف
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Empty') IS NOT NULL DROP TABLE #Empty;

    -- «خالی» یعنی نه فقط بدون ردیف DEED_DTL، بلکه هیچ جدول دیگری هم به آن
    -- ارجاع ندهد. طبق sys.foreign_keys، هشت جدول به DEED_HED.N_S کلید
    -- خارجی دارند (DEED_DTL, HEAD_LST, ANBGRD_HEAD, CHKREC_H, CHREC_HP,
    -- WORKHEAD, MO_DTL, PGET_HED, HEAD_LST_TMP_WPF). سندی که هنوز از
    -- کاردکس انبار یا هرکدام دیگر ارجاع می‌شود واقعاً خالی نیست، حتی اگر
    -- DEED_DTL نداشته باشد — نباید حذفش کرد، و مطلقاً نباید ارجاع آن
    -- جدول‌ها را NULL کرد تا حذف زور بشود؛ آن ارجاع همان چیزی است که
    -- ردگیری سند حسابداری را از رکورد انبار ممکن می‌کند.
    SELECT h.N_S, h.DATE_S
    INTO   #Empty
    FROM   dbo.DEED_HED h
    WHERE  h.DATE_S BETWEEN @DT1 AND @DT2
      AND  NOT EXISTS (SELECT 1 FROM dbo.DEED_DTL    d WHERE d.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.HEAD_LST    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.ANBGRD_HEAD x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.CHKREC_H    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.CHREC_HP    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.WORKHEAD    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.MO_DTL      x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.PGET_HED    x WHERE x.N_S = h.N_S);

    -- HEAD_LST_TMP_WPF ممکن است روی همهٔ نصب‌ها نباشد؛ اگر هست همان قاعده.
    IF OBJECT_ID('dbo.HEAD_LST_TMP_WPF', 'U') IS NOT NULL
        DELETE e FROM #Empty e
        WHERE EXISTS (SELECT 1 FROM dbo.HEAD_LST_TMP_WPF t WHERE t.N_S = e.N_S);

    DECLARE @n INT = (SELECT COUNT(*) FROM #Empty);

    IF @WhatIf = 1
    BEGIN
        SELECT N_S AS شماره_سند, DATE_S AS تاريخ FROM #Empty ORDER BY DATE_S, N_S;
        SELECT @n AS تعداد_سند_قابل_حذف, N'حالت گزارش — چيزي حذف نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DELETE h FROM dbo.DEED_HED h JOIN #Empty e ON e.N_S = h.N_S;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S03', 1, CONCAT(N'حذف اسناد خالي: ', @n, N' سند'),
            (SELECT N_S, DATE_S FROM #Empty FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S03_DeleteEmptyDeeds).
    -- Dapper روی نام‌مستعار فارسی نگاشت نمی‌کند و بی‌صدا صفر برمی‌گرداند؛
    -- شرح فارسی در CC_RunLog بالا ثبت شده است.
    SELECT @n AS Value;
END
GO


/* ═══════════════ S04 — مرتب‌سازی اسناد ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S04_SortDeeds
    @RunId     INT,
    @DT1       BIGINT,
    @DT2       BIGINT,
    @WholeYear BIT = 0,
    @WhatIf    BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    DECLARE @seed FLOAT =
        CASE WHEN @WholeYear = 1 THEN 0
             ELSE ISNULL((SELECT MAX(N_S) FROM dbo.DEED_HED WHERE DATE_S < @DT1), 0) END;

    -- کل جدول را می‌آوریم (نه فقط بازهٔ ماه) چون برای جلوگیری از تلاقی با
    -- اسناد ماه‌های بعدی باید بدانیم شمارهٔ فعلی‌شان چیست؛ اسناد بیرون بازه
    -- در ستون NewNS همان شمارهٔ فعلی خودشان را می‌گیرند (دست‌نخورده).
    SELECT  base,
            DATE_S,
            N_S AS OldNS,
            CASE WHEN @WholeYear = 1 OR DATE_S BETWEEN @DT1 AND @DT2
                 THEN @seed + ROW_NUMBER() OVER (
                          PARTITION BY CASE WHEN @WholeYear = 1
                                             OR DATE_S BETWEEN @DT1 AND @DT2
                                        THEN 1 ELSE 0 END
                          ORDER BY DATE_S ASC, N_S ASC)
                 ELSE N_S END AS NewNS
    INTO    #Map
    FROM    dbo.DEED_HED;

    -- اگر بازهٔ شمارهٔ جدید ماه جاری با شمارهٔ فعلی اولین سند ماه‌های بعدی
    -- تلاقی کند، همهٔ اسناد بعد از @DT2 را به یک اندازه جلو می‌بریم؛ چون
    -- همه با هم جابه‌جا می‌شوند، ترتیب و فاصلهٔ نسبی‌شان دست‌نخورده می‌ماند
    -- و تلاقی تازه‌ای ایجاد نمی‌شود.
    IF @WholeYear = 0
    BEGIN
        DECLARE @maxNewInMonth FLOAT =
            ISNULL((SELECT MAX(NewNS) FROM #Map WHERE DATE_S BETWEEN @DT1 AND @DT2), @seed);
        DECLARE @minAfterMonth FLOAT =
            ISNULL((SELECT MIN(OldNS) FROM #Map WHERE DATE_S > @DT2), 0);

        IF @minAfterMonth > 0 AND @maxNewInMonth >= @minAfterMonth
        BEGIN
            DECLARE @shift FLOAT = (@maxNewInMonth - @minAfterMonth) + 1;
            UPDATE #Map SET NewNS = OldNS + @shift WHERE DATE_S > @DT2;
        END
    END

    CREATE UNIQUE CLUSTERED INDEX IX_Map ON #Map(base);

    DECLARE @total INT   = (SELECT COUNT(*) FROM #Map);
    DECLARE @changed INT = (SELECT COUNT(*) FROM #Map WHERE OldNS <> NewNS);

    IF @WhatIf = 1
    BEGIN
        SELECT TOP 100 base, OldNS AS شماره_فعلي, NewNS AS شماره_جديد
        FROM   #Map WHERE OldNS <> NewNS ORDER BY NewNS;
        SELECT @total AS کل_اسناد, @changed AS تعداد_تغيير,
               N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- تريگرهاي Audit را فقط براي همين نشست کنار مي‌گذاريم
    EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

    -- ۹ جدول فرزند با ON UPDATE CASCADE خودکار به‌روز مي‌شوند.
    -- دو مرحله‌اي: چون شمارهٔ جدید یک سند می‌تواند برابر شمارهٔ فعلیِ سند
    -- دیگری باشد که هنوز عوض نشده (Shift یا جابه‌جایی داخل ماه)، یک
    -- UPDATE مستقیم وسط کار به PRIMARY KEY تکراری می‌خورد. اول همه را به
    -- یک بازهٔ منفیِ ناهم‌پوشان می‌بریم، بعد به مقدار نهایی.
    UPDATE  h
       SET  h.N_S = -1000000.0 - m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S <> m.NewNS;

    UPDATE  h
       SET  h.N_S = m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S < 0;

    EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S04', 1, N'بازشماره‌گذاري اسناد انجام شد',
            (SELECT @total AS total, @changed AS changed FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S04_SortDeeds).
    -- Value = تعداد اسناد بازشماره‌شده؛ شرح فارسی در CC_RunLog بالا ثبت شد.
    SELECT @changed AS Value, @total AS Total;
END
GO


PRINT N'رويه‌هاي فاز ۱ ايجاد شدند.';

SELECT  name AS رويه, create_date AS تاريخ_ايجاد, modify_date AS آخرين_تغيير
FROM    sys.procedures
WHERE   name LIKE 'CC[_]sp[_]%'
ORDER BY name;
GO
";
            TryExecuteCostCloseBatch(db, phase1Procs,
                "CC_sp_RunCreate، CC_sp_StepStart/Finish، CC_sp_Snapshot، S00/S03/S04",
                "اسکریپت 12-procedures-phase1.sql را اجرا کنید.");

            string chk04AutoFix = @"
/* ═══════════════════════════════════════════════════════════════════
   دو تغییر بر اساس درخواست کاربر

   ۱) CHK-04 حالا شماره برگه‌های تولید را هم می‌دهد، نه فقط کد کالا
   ۲) رویه اصلاح خودکار: فرمول همان ماه را به برگه‌ها نسبت می‌دهد

   قابل اجرای مکرر.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ستون جدید برای نگهداری فهرست برگه‌ها و امکان اصلاح خودکار */
IF COL_LENGTH('dbo.CC_Exception','RefList') IS NULL
    ALTER TABLE dbo.CC_Exception ADD RefList NVARCHAR(2000) NULL;
GO
IF COL_LENGTH('dbo.CC_Exception','CanAutoFix') IS NULL
    ALTER TABLE dbo.CC_Exception ADD CanAutoFix BIT NOT NULL DEFAULT 0;
GO
IF COL_LENGTH('dbo.CC_CheckRule','FixProcName') IS NULL
    ALTER TABLE dbo.CC_CheckRule ADD FixProcName SYSNAME NULL;
GO
IF COL_LENGTH('dbo.CC_CheckRule','FixButtonText') IS NULL
    ALTER TABLE dbo.CC_CheckRule ADD FixButtonText NVARCHAR(60) NULL;
GO

UPDATE dbo.CC_CheckRule
   SET FixProcName   = 'CC_sp_Fix_MissingFormula',
       FixButtonText = N'اصلاح خودکار برگه'
 WHERE RuleCode = 'CHK-04';
GO


/* ═══════════════════════════════════════════════════════════════════
   CHK-04 — نسخه‌ای که شماره برگه می‌دهد

   یک سطر به ازای هر کالا، با فهرست برگه‌های متأثر.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Chk04_MissingFormula
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  RuleCode = 'CHK-04' AND ISNULL(RunId,-1) = ISNULL(@RunId,-1);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocTag,
         DocNumber, DocDate, Amount, RefList, CanAutoFix, Description)
    SELECT  @RunId, 'S00', 'CHK-04', 12, 2,
            CAST(pl.CODE AS BIGINT),
            9,
            MIN(h.NUMBER),                       -- اولين برگه
            MIN(h.DATE_N),
            SUM(pl.MEGHK),                       -- جمع مقدار توليد متأثر
            STRING_AGG(CAST(h.NUMBER AS VARCHAR(12)), ', ')
                WITHIN GROUP (ORDER BY h.NUMBER),
            -- اصلاح خودکار فقط وقتي ممکن است که فرمول ماه واقعاً وجود داشته باشد
            CASE WHEN EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                              WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                                AND hm.GHEYMAT = @Month)
                 THEN 1 ELSE 0 END,
            CONCAT(N'کالا در ', COUNT(DISTINCT h.NUMBER),
                   N' برگه توليد شده ولي فرمول ماه ', @Month, N' به آن نسبت داده نشده')
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (
                SELECT 1 FROM dbo.HEAD_MANF hm
                WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                  AND hm.GHEYMAT = @Month)
    GROUP BY CAST(pl.CODE AS BIGINT);

    SELECT  e.Code       AS کد_کالا,
            s.NAME       AS نام_کالا,
            e.Amount     AS جمع_مقدار_توليد,
            e.RefList    AS برگه_ها,
            CASE e.CanAutoFix WHEN 1 THEN N'بله' ELSE N'خير — فرمول ماه وجود ندارد' END
                         AS اصلاح_خودکار
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = e.Code
    WHERE   e.RuleCode = 'CHK-04' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    ORDER BY e.Amount DESC;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   اصلاح خودکار — دکمه‌ای که کاربر می‌زند

   فرمول همان ماه را پیدا و به برگه‌های تولید نسبت می‌دهد.
   @ExceptionId داده شود  → فقط همان یک کالا
   @ExceptionId خالی      → همه کالاهای قابل اصلاح

   @WhatIf = 1 پیش‌فرض است: فقط نشان می‌دهد چه چیزی تغییر خواهد کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_MissingFormula
    @Month       TINYINT,
    @DT1         BIGINT,
    @DT2         BIGINT,
    @RunId       INT           = NULL,
    @ExceptionId BIGINT        = NULL,
    @UserName    NVARCHAR(50)  = N'system',
    @WhatIf      BIT           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- کالاهاي هدف
    IF OBJECT_ID('tempdb..#Target') IS NOT NULL DROP TABLE #Target;
    CREATE TABLE #Target (Code BIGINT PRIMARY KEY);

    INSERT #Target(Code)
    SELECT DISTINCT e.Code
    FROM   dbo.CC_Exception e
    WHERE  e.RuleCode = 'CHK-04'
      AND  e.IsResolved = 0
      AND  e.CanAutoFix = 1
      AND  ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
      AND  (@ExceptionId IS NULL OR e.ExceptionId = @ExceptionId);

    ---- نگاشت کالا به فرمول ماه
    ---- اگر يک کالا چند فرمول در همان ماه داشته باشد، تازه‌ترين انتخاب مي‌شود
    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    SELECT  t.Code,
            f.FNUMB,
            f.Chand
    INTO    #Map
    FROM    #Target t
    CROSS   APPLY (
                SELECT TOP 1
                       hm.FNUMB,
                       COUNT(*) OVER () AS Chand
                FROM   dbo.HEAD_MANF hm
                WHERE  CAST(hm.CODE AS BIGINT) = t.Code
                  AND  hm.GHEYMAT = @Month
                ORDER BY hm.DATE_ACTIV DESC, hm.FNUMB DESC
            ) f;

    ---- سطرهايي که تغيير خواهند کرد
    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- کليد تطبيق id است نه (NUMBER, RADIF): ستون RADIF در INVO_LST
    -- nullable است و روي داده‌ي واقعي مي‌تواند خالي باشد؛ آن‌وقت شرط
    -- «r.Radif = pl.RADIF» در UPDATE هرگز برقرار نمي‌شود (NULL = NULL
    -- در SQL نادرست است) و اصلاح خودکار بي‌صدا هيچ سطري را عوض
    -- نمي‌کند، درحالي‌که تعداد را گزارش مي‌دهد و استثنا را هم مي‌بندد.
    -- id کليد اصلي جدول است و اين حالت را کاملاً حذف مي‌کند.
    SELECT  pl.id               AS InvoId,
            h.NUMBER            AS ProdNo,
            h.DATE_N            AS ProdDate,
            pl.RADIF            AS Radif,
            CAST(pl.CODE AS BIGINT) AS Code,
            pl.N_KOL            AS OldFnumb,
            m.FNUMB             AS NewFnumb,
            pl.MEGHK            AS Meghdar,
            m.Chand             AS ChandFormul
    INTO    #Rows
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    #Map m ON m.Code = CAST(pl.CODE AS BIGINT)
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                          AND hm.GHEYMAT = @Month);

    ---- هشدار: کالايي که در ماه بيش از يک فرمول دارد نياز به انتخاب کاربر دارد
    IF EXISTS (SELECT 1 FROM #Rows WHERE ChandFormul > 1)
        SELECT DISTINCT
               r.Code AS کد_کالا, s.NAME AS نام_کالا, r.ChandFormul AS تعداد_فرمول_ماه,
               N'اين کالا در اين ماه بيش از يک فرمول دارد؛ تازه‌ترين انتخاب شد' AS هشدار
        FROM   #Rows r LEFT JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = r.Code
        WHERE  r.ChandFormul > 1;

    DECLARE @n INT = (SELECT COUNT(*) FROM #Rows);

    IF @WhatIf = 1
    BEGIN
        SELECT  ProdNo    AS شماره_برگه,
                ProdDate  AS تاريخ,
                Code      AS کد_کالا,
                OldFnumb  AS فرمول_فعلي,
                NewFnumb  AS فرمول_جديد,
                Meghdar   AS مقدار
        FROM    #Rows
        ORDER BY ProdDate, ProdNo;

        SELECT @n AS تعداد_سطر_قابل_اصلاح, N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- کدهايي که واقعاً عوض شدند را نگه مي‌داريم تا فقط استثناي همان‌ها
    -- بسته شود. اگر UPDATE به هر دليلي سطري را نگيرد، نبايد استثنا را
    -- «رفع‌شده» علامت بزنيم و عدد قابل‌اصلاح را به‌عنوان عدد اصلاح‌شده
    -- گزارش کنيم — کاربر بايد ببيند که کاري انجام نشده.
    DECLARE @Applied TABLE (Code BIGINT);

    UPDATE  pl
       SET  pl.N_KOL = r.NewFnumb
    OUTPUT  CAST(inserted.CODE AS BIGINT) INTO @Applied(Code)
    FROM    dbo.INVO_LST pl
    JOIN    #Rows r ON r.InvoId = pl.id
    WHERE   pl.TAG = 9;

    DECLARE @appliedRows INT = @@ROWCOUNT;

    ---- ثبت در سابقه
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S00', CASE WHEN @appliedRows = 0 AND @n > 0 THEN 2 ELSE 1 END,
            CASE WHEN @appliedRows = 0 AND @n > 0
                 THEN CONCAT(N'اصلاح خودکار هيچ سطري را عوض نکرد (', @n,
                             N' سطر نامزد بود) — توسط ', @UserName)
                 ELSE CONCAT(N'اصلاح خودکار فرمول برگه‌هاي توليد: ', @appliedRows,
                             N' سطر توسط ', @UserName) END,
            (SELECT ProdNo, Code, OldFnumb, NewFnumb FROM #Rows FOR JSON PATH);

    ---- استثناها بسته مي‌شوند — فقط براي کدهايي که واقعاً اصلاح شدند
    UPDATE  e
       SET  e.IsResolved     = 1,
            e.ResolvedBy     = @UserName,
            e.ResolvedAtUtc  = SYSUTCDATETIME(),
            e.ResolutionNote = N'اصلاح خودکار — فرمول ماه به برگه‌ها نسبت داده شد'
    FROM    dbo.CC_Exception e
    WHERE   e.RuleCode = 'CHK-04'
      AND   ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
      AND   EXISTS (SELECT 1 FROM @Applied a WHERE a.Code = e.Code);

    ---- خروج مواد بايد بازسازي شود، چون فرمول برگه عوض شد
    IF @RunId IS NOT NULL AND @appliedRows > 0
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT @appliedRows AS تعداد_سطر_اصلاح_شده, @n AS تعداد_سطر_نامزد;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   CHK-15 — اصلاح فرمول با مقدار منفی

   @ExceptionId الزامی است: هر سطر فرمول منفی جدا اصلاح می‌شود، نه گروهی،
   چون هر سطر می‌تواند تصمیم متفاوتی بخواهد (صفر یا حذف). شناسه سطر
   (DTL_MANF.id) در CC_Exception.DocNumber ذخیره شده — نگاه کنید به
   CC_sp_S00_Preflight بخش CHK-15.

   @Action = 'zero'   → مقدار (MEGH/MEGHk/MABLK/SMABL) صفر می‌شود، سطر می‌ماند
   @Action = 'delete' → کل سطر فرمول حذف می‌شود

   @WhatIf = 1 پیش‌فرض است: فقط نشان می‌دهد چه چیزی تغییر خواهد کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_NegativeFormulaQty
    @ExceptionId BIGINT,
    @Action      VARCHAR(10),
    @RunId       INT           = NULL,
    @UserName    NVARCHAR(50)  = N'system',
    @WhatIf      BIT           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Action NOT IN ('zero', 'delete')
    BEGIN
        RAISERROR(N'مقدار @Action باید zero یا delete باشد.', 16, 1);
        RETURN;
    END

    DECLARE @DtlId BIGINT, @Code BIGINT;

    SELECT  @DtlId = e.DocNumber, @Code = e.Code
    FROM    dbo.CC_Exception e
    WHERE   e.ExceptionId = @ExceptionId AND e.RuleCode = 'CHK-15';

    IF @DtlId IS NULL
    BEGIN
        RAISERROR(N'این استثنا یافت نشد یا مربوط به CHK-15 نیست.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.DTL_MANF WHERE id = @DtlId)
    BEGIN
        -- سطر قبلاً حذف يا اصلاح شده — فقط استثنا را ببند
        IF @WhatIf = 0
            UPDATE dbo.CC_Exception
               SET IsResolved = 1, ResolvedBy = @UserName, ResolvedAtUtc = SYSUTCDATETIME(),
                   ResolutionNote = N'سطر فرمول از قبل اصلاح شده بود'
             WHERE ExceptionId = @ExceptionId;

        SELECT 0 AS تغيير_يافت, N'سطر فرمول از قبل اصلاح يا حذف شده بود' AS وضعيت;
        RETURN;
    END

    IF @WhatIf = 1
    BEGIN
        SELECT  d.id AS شناسه_سطر, h.FNUMB AS شماره_فرمول, d.CODE AS کد_ماده,
                d.MEGH AS مقدار_فعلي, d.MEGHk AS مقدار_کوچک_فعلي,
                CASE @Action WHEN 'zero' THEN N'مقدار صفر مي‌شود'
                             ELSE N'کل سطر فرمول حذف مي‌شود' END AS عمليات
        FROM    dbo.DTL_MANF d
        JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB
        WHERE   d.id = @DtlId;
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @Fnumb INT;
    SELECT @Fnumb = FNUMB FROM dbo.DTL_MANF WHERE id = @DtlId;

    IF @Action = 'zero'
        UPDATE dbo.DTL_MANF
           SET MEGH = 0, MEGHk = 0, MABLK = 0, SMABL = 0
         WHERE id = @DtlId;
    ELSE
        DELETE dbo.DTL_MANF WHERE id = @DtlId;

    UPDATE dbo.CC_Exception
       SET IsResolved = 1, ResolvedBy = @UserName, ResolvedAtUtc = SYSUTCDATETIME(),
           ResolutionNote = CASE @Action WHEN 'zero' THEN N'مقدار سطر فرمول صفر شد'
                                          ELSE N'سطر فرمول حذف شد' END
     WHERE ExceptionId = @ExceptionId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S00', 1,
            CONCAT(N'اصلاح فرمول با مقدار منفي — فرمول ', @Fnumb, N', کالا ', @Code,
                   CASE @Action WHEN 'zero' THEN N' — مقدار صفر شد' ELSE N' — سطر حذف شد' END,
                   N' توسط ', @UserName));

    IF @RunId IS NOT NULL
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT 1 AS تغيير_يافت, N'انجام شد' AS وضعيت;
END
GO


PRINT N'CHK-04 و اصلاح خودکار آماده شد.';

/* نمونه:
   EXEC dbo.CC_sp_Chk04_MissingFormula  @Month=5, @DT1=14050501, @DT2=14050531;
   EXEC dbo.CC_sp_Fix_MissingFormula    @Month=5, @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_Fix_MissingFormula    @Month=5, @DT1=14050501, @DT2=14050531, @WhatIf=0,
                                        @UserName=N'مدير مالي';
*/
GO
";
            TryExecuteCostCloseBatch(db, chk04AutoFix,
                "CC_sp_Chk04_MissingFormula و CC_sp_Fix_MissingFormula",
                "اسکریپت 13-chk04-and-autofix.sql را اجرا کنید (به CC_Exception و CC_CheckRule نیاز دارد).");

            string s05Gate = @"/* ═══════════════════════════════════════════════════════════════════
   S05 — دروازه اعتبارسنجی

   دو کنترلی که امروز با دابل‌کلیک روی گزارش موجودی می‌گیرید:
     CHK-01  کاردکس منفی
     CHK-02  مغایرت کارت انبار و حسابداری
     CHK-13  حواله با مقدار صفر

   نتیجه مستقیم در CC_Exception می‌نشیند و صفحه مغایرت‌ها نشانش می‌دهد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند (YAZDSEPAR{YEAR} در تولید، SafirTest* در محیط تست).
   اسکریپت را روی پایگاه هدف اجرا کنید. بقیه اسکریپت‌های
   Server/Database/ هم همین قرارداد را دارند.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_S05_Gate
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  RunId = @RunId AND RuleCode IN ('CHK-01','CHK-02','CHK-13');

    /* ─────────────────────────────────────────────────────────────
       CHK-01 — کاردکس منفی

       موجودی تجمعی هر کالا در هر انبار به ترتیب تاریخ محاسبه و
       هر جا منفی شد علامت می‌خورد. معمولاً یعنی تاریخ رسید بعد از
       تاریخ حواله ثبت شده است.

       فقط اولین نقطه منفی هر کالا/انبار گزارش می‌شود؛ بقیه
       دنباله همان یک مشکل‌اند و فهرست را شلوغ می‌کنند.
       ───────────────────────────────────────────────────────────── */
    ;WITH Harekat AS (
        -- KALAS یک ویو گزارشی است، نه کاردکس خام؛ ستون انبار آن به‌جای
        -- ANBAR، سه ستون ANBARF/ANBARCODE/ANBARAS دارد. با مقایسه با
        -- INVO_LST.ANBAR (که مبنای درست است) روی داده واقعی تأیید شد که
        -- فقط ANBARCODE همیشه پر و همیشه برابر همان مقدار است؛ ANBARF و
        -- ANBARAS اکثراً NULLاند.
        SELECT  k.ANBARCODE AS ANBAR,
                k.code,
                k.DATE_N,
                k.NUMBER,
                k.TAG,
                CASE WHEN k.TAG IN (1, 7, 9, 24) THEN k.MEGHk ELSE -k.MEGHk END AS Meghdar
        FROM    dbo.KALAS k
        WHERE   k.DATE_N <= @DT2
          AND   k.MEGHk <> 0
    ),
    Tajamoi AS (
        SELECT  ANBAR, code, DATE_N, NUMBER, TAG,
                SUM(Meghdar) OVER (
                    PARTITION BY ANBAR, code
                    ORDER BY DATE_N, NUMBER
                    ROWS UNBOUNDED PRECEDING) AS Mande
        FROM    Harekat
    ),
    AvvalinManfi AS (
        SELECT  ANBAR, code, DATE_N, NUMBER, TAG, Mande,
                ROW_NUMBER() OVER (
                    PARTITION BY ANBAR, code
                    ORDER BY DATE_N, NUMBER) AS rn
        FROM    Tajamoi
        WHERE   Mande < -0.0001
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocTag, DocDate, Amount, Description)
    SELECT  @RunId, 'S05', 'CHK-01', 1, 2,
            m.ANBAR, m.code, m.NUMBER, m.TAG, m.DATE_N, m.Mande,
            CONCAT(N'انبار ', m.ANBAR, N' (', ISNULL(a.NAMES, N'نامشخص'), N'): موجودی در تاریخ ',
                   m.DATE_N / 10000, '/',
                   FORMAT(m.DATE_N / 100 % 100, '00'), '/',
                   FORMAT(m.DATE_N % 100, '00'),
                   N' منفی می‌شود')
    FROM    AvvalinManfi m
    LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = m.ANBAR
    WHERE   m.rn = 1
      AND   m.DATE_N BETWEEN @DT1 AND @DT2;

    /* ─────────────────────────────────────────────────────────────
       CHK-02 — مغایرت کارت انبار و حسابداری

       مانده ریالی کارت انبار (KALAS) با مانده حساب موجودی جنسی
       مقایسه می‌شود. اختلاف معمولاً یعنی حواله‌ای که فاکتورش
       صادر نشده، یا تاریخ فاکتور در ماه بعد افتاده.

       هر انبار زیر یک معین جداگانه در حسابداری ثبت می‌شود، نه یک
       معین ثابت مشترک برای همهٔ انبارها (تفصیلی طبق ساختار خودِ
       دیتابیس فقط زیر یک معین مشخص یکتاست: TDETA_HES.PK =
       (N_KOL,NUMBER,TNUMBER)). قبلاً این کنترل فقط با کل=۱۲۱ و
       بدون معین مقایسه می‌کرد، یعنی اگر همان شماره تفصیلی زیر چند
       معین مختلف (چند انبار) تراکنش داشت، همه را با هم جمع می‌زد و
       برای تقریباً هر کالا یک مانده کاملاً غلط و متورم می‌ساخت.
       نگاشت واقعیِ انبار⇄معین از CC_AnbarHes (تنظیمات) خوانده
       می‌شود، نه هاردکد، چون شرکت‌به‌شرکت فرق می‌کند.

       آستانه یک ریال است چون این دو باید دقیقاً یکی باشند.
       ───────────────────────────────────────────────────────────── */
    IF NOT EXISTS (SELECT 1 FROM dbo.CC_AnbarHes)
    BEGIN
        -- بدون نگاشت انبار⇄معین نمی‌توان درست مقایسه کرد؛ به‌جای هزاران
        -- مورد کاذب، یک هشدار واحد می‌گوید چه باید تنظیم شود.
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Description)
        VALUES
            (@RunId, 'S05', 'CHK-02', 2, 1,
             N'نگاشت انبار به حساب موجودی (کل/معین) در تنظیمات ثبت نشده؛ این کنترل غیرفعال است.');
    END
    ELSE
    BEGIN
        ;WITH KartAnbar AS (
            SELECT  k.ANBARCODE AS Anbar, k.code,
                    SUM(CASE WHEN k.TAG IN (1, 7, 9, 24)
                             THEN k.MABL_K ELSE -k.MABL_K END) AS Mande
            FROM    dbo.KALAS k
            WHERE   k.DATE_N <= @DT2
              AND   k.ANBARCODE IN (SELECT Anbar FROM dbo.CC_AnbarHes)
            GROUP BY k.ANBARCODE, k.code
        ),
        Hesabdari AS (
            SELECT  am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code,
                    SUM(d.BED) - SUM(d.BES) AS Mande
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED  h  ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   h.DATE_S <= @DT2
            GROUP BY am.Anbar, TRY_CAST(d.HES_T AS BIGINT)
        )
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
        SELECT  @RunId, 'S05', 'CHK-02', 2, 2,
                ISNULL(k.Anbar, hh.Anbar), ISNULL(k.code, hh.code),
                ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0),
                CONCAT(N'انبار ', ISNULL(k.Anbar, hh.Anbar),
                       N' (', ISNULL(a.NAMES, N'نامشخص'), N'): کارت انبار ',
                       FORMAT(ISNULL(k.Mande, 0), 'N0'),
                       N' در برابر حسابداری ', FORMAT(ISNULL(hh.Mande, 0), 'N0'))
        FROM    KartAnbar k
        FULL    OUTER JOIN Hesabdari hh ON hh.Anbar = k.Anbar AND hh.code = k.code
        LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = ISNULL(k.Anbar, hh.Anbar)
        WHERE   ABS(ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0)) > 1;
    END

    /* ─────────────────────────────────────────────────────────────
       CHK-13 — حواله با مقدار صفر

       ماده‌ای که در فرمول مقدار دارد ولی حواله‌اش صفر است، یعنی
       فرمول پس از صدور حواله ویرایش شده و خروج مواد بازسازی نشده.
       این همان چیزی است که برای کالای ۲۸۴۱ در ماه تیر رخ داد.
       ───────────────────────────────────────────────────────────── */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocDate, Amount, Description)
    SELECT  DISTINCT @RunId, 'S05', 'CHK-13', 16, 2,
            i.ANBAR, CAST(i.CODE AS BIGINT), h.NUMBER, h.DATE_N, 0,
            N'حواله با مقدار صفر برای ماده‌ای که در فرمول ماه مقدار دارد'
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE   h.TAG = 10
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(i.MEGHK, 0) = 0
      AND   EXISTS (
                SELECT 1
                FROM   dbo.DTL_MANF d
                JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
                WHERE  CAST(d.CODE AS BIGINT) = CAST(i.CODE AS BIGINT)
                  AND  d.MEGHk > 0);

    /* ─────────────────────────── خلاصه ─────────────────────────── */
    SELECT  e.RuleCode                                            AS قاعده,
            r.RuleName                                            AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده'
                            ELSE N'هشدار' END                     AS شدت,
            COUNT(*)                                              AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.RunId = @RunId AND e.StepCode = 'S05' AND e.IsResolved = 0
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO

/* رویه آزمایشی قدیمی که جای خود را به CC_sp_S00_Preflight داده است. */
DROP PROCEDURE IF EXISTS dbo.CC_sp_Preflight;
GO

PRINT N'رويه دروازه اعتبارسنجي ايجاد شد.';
GO
";
            TryExecuteCostCloseBatch(db, s05Gate, "CC_sp_S05_Gate",
                "اسکریپت‌های 10-schema.sql تا 13-chk04-and-autofix.sql را اول اجرا کنید.");

            string rateEngine = @"
/* ═══════════════════════════════════════════════════════════════════
   مرحله ۴ — موتور نرخ، نسخه تولیدی

   تفاوت با نسخه آزمون بازگشتی (فایل 03):
     ۱) در DTL_MANF و HEAD_MANF می‌نویسد، نه فقط در CC_ItemCost
     ۲) هر تغییر در CC_FormulaChange ثبت می‌شود
     ۳) @RunId می‌گیرد و به سابقه اجرا وصل است
     ۴) S10 (تراز هزینه تبدیل) هم اینجاست

   ترتیب اجرا: S10 سپس S11
   چون ضریب تعدیل مستقل از نرخ مواد است، یک بار محاسبه کافی است
   و دیگر نیازی به قرار گرفتن داخل حلقه ندارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S11 که در CC_ItemCost (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S10 — تراز هزینه تبدیل به تفکیک واحد تولیدی

   جذب‌شده = Σ (مقدار تولید × نرخ جذب فرمول)
   واقعی   = Σ (مانده سرفصل × ضریب سهم)   طبق CC_UnitAcc
   ضریب    = واقعی ÷ جذب‌شده

   کنترل متقابل: به شرط صفر بودن کار در جریان، جذب باید با
   گردش بستانکار حساب ۷۵۱ با تفصیلی 99999999 برابر باشد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S10_BalanceConversion
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TafDastmozd BIGINT = 99999999;
    DECLARE @UnitId INT, @Dep INT, @SplitMode TINYINT;

    -- Depatman = NULL يعني «همهٔ دپارتمان‌ها» — اگر بيش از يک واحد فعال اين
    -- حالت را داشته باشند، هر دو دقيقاً همان برگه‌هاي توليد را پردازش
    -- مي‌کنند. چون اين حلقه IMBIBE_MANF/IMBIBE_SAR را در HEAD_MANF مستقيماً
    -- ويرايش مي‌کند، واحد دومي که در همان اجرا پردازش مي‌شود ديگر مقدار
    -- اصلي فرمول را نمي‌بيند بلکه مقدارِ از قبل تعديل‌شدهٔ واحد اول را
    -- مي‌خواند و رويش دوباره ضريب مي‌زند — نتيجه فرمول را خراب مي‌کند، نه
    -- فقط عدد کنترلي را. مقدار پيش‌فرض داده اوليه (11-seed-data.sql) دقيقاً
    -- همين ترکيب را دارد؛ تا وقتي نصب‌کننده Depatman هر واحد را با دپارتمان
    -- واقعي‌اش عوض نکند، اجراي واقعي همين‌جا فرمول‌ها را خراب مي‌کرد.
    IF (SELECT COUNT(*) FROM dbo.CC_Unit WHERE IsActive = 1 AND Depatman IS NULL) > 1
    BEGIN
        RAISERROR(N'بيش از يک واحد توليدي فعال بدون دپارتمان مشخص (همه‌شمول) وجود دارد؛ اين باعث پردازش دوباره‌ي همان برگه‌ها و خراب شدن فرمول‌ها مي‌شود. دپارتمان هر واحد را در تنظیمات مشخص کنيد.', 16, 1);
        RETURN;
    END

    DELETE dbo.CC_ConversionCost WHERE RunId = @RunId;

    DECLARE cUnit CURSOR LOCAL FAST_FORWARD FOR
        SELECT UnitId, Depatman, SplitMode
        FROM   dbo.CC_Unit WHERE IsActive = 1 ORDER BY SeqNo;

    OPEN cUnit;
    FETCH NEXT FROM cUnit INTO @UnitId, @Dep, @SplitMode;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        ---- ۱) جذب‌شده از برگه‌هاي توليد اين واحد
        DECLARE @absWage FLOAT, @absOh FLOAT;

        SELECT  @absWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @absOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND  (@Dep IS NULL OR h.DEPATMAN = @Dep);

        DECLARE @absTotal FLOAT = @absWage + @absOh;

        ---- ۲) کنترل متقابل با حساب ۷۵۱ (فقط تفصيلي دستمزد)
        DECLARE @absWip FLOAT;

        SELECT  @absWip = ISNULL(SUM(d.BES) - SUM(d.BED), 0)
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
        WHERE   d.HES_K = 751 AND d.HES_T = @TafDastmozd
          AND   hd.DATE_S BETWEEN @DT1 AND @DT2;

        ---- ۳) واقعي از تراز، طبق نگاشت قابل ويرايش کاربر
        DECLARE @actWage FLOAT, @actOh FLOAT;

        -- CROSS APPLY نه JOIN روي جمعِ از‌قبل‌گروه‌بندی‌شده، چون هر سطر
        -- CC_UnitAcc ممکن است سطح معین/تفصیلی متفاوتی مشخص کرده باشد؛
        -- خالی‌بودن هرکدام یعنی «همهٔ آن سطح» (نگاشت گسترده‌تر، مثل قبل).
        SELECT  @actWage = ISNULL(SUM(CASE WHEN m.CostKind = 1
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0),
                @actOh   = ISNULL(SUM(CASE WHEN m.CostKind = 2
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0)
        FROM    dbo.CC_UnitAcc m
        CROSS   APPLY (
                    SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                    FROM   dbo.DEED_DTL d
                    JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                    WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                      AND  d.HES_K = m.HesKol
                      AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                      AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
                ) t
        WHERE   m.IsActive = 1 AND m.UnitId = @UnitId;

        DECLARE @actTotal FLOAT = @actWage + @actOh;

        ---- ۴) ضريب تعديل
        DECLARE @kWage FLOAT = 1, @kOh FLOAT = 1;

        IF @absTotal <> 0
        BEGIN
            IF @SplitMode = 1                    -- يک ضريب براي کل هزينه تبديل
            BEGIN
                DECLARE @k FLOAT = @actTotal / @absTotal;
                SET @kWage = @k;
                SET @kOh   = @k;
            END
            ELSE                                 -- دو ضريب مجزا
            BEGIN
                SET @kWage = CASE WHEN @absWage <> 0 THEN @actWage / @absWage ELSE 1 END;
                SET @kOh   = CASE WHEN @absOh   <> 0 THEN @actOh   / @absOh   ELSE 1 END;
            END
        END

        ---- ۵) ثبت نتيجه
        INSERT dbo.CC_ConversionCost
            (RunId, UnitId, CostKind, AbsorbedAmount, AbsorbedFromWip,
             ActualAmount, AdjustFactor, ActualDetailJson)
        VALUES
            (@RunId, @UnitId, 0, @absTotal, @absWip, @actTotal,
             CASE WHEN @absTotal <> 0 THEN @actTotal / @absTotal ELSE 1 END,
             (SELECT m.HesKol, m.HesMoin, m.HesTafsili, m.CostKind, m.Ratio
              FROM   dbo.CC_UnitAcc m
              WHERE  m.UnitId = @UnitId AND m.IsActive = 1
              FOR JSON PATH)),
            (@RunId, @UnitId, 1, @absWage, NULL, @actWage, @kWage, NULL),
            (@RunId, @UnitId, 2, @absOh,   NULL, @actOh,   @kOh,   NULL);

        ---- ۶) هشدار اختلاف کنترلي
        IF ABS(@absWip - @absTotal) > 10000000
            INSERT dbo.CC_Exception
                (RunId, StepCode, RuleCode, ExType, Severity, Amount, Description)
            VALUES (@RunId, 'S10', 'CHK-08', 10, 1, @absWip - @absTotal,
                    CONCAT(N'اختلاف جذب: برگه‌هاي توليد ', FORMAT(@absTotal, 'N0'),
                           N' در برابر حساب ۷۵۱ ', FORMAT(@absWip, 'N0')));

        ---- ۷) اعمال ضريب روي فرمول‌هاي کالاهاي توليدشده در اين واحد
        IF @WhatIf = 0 AND (@kWage <> 1 OR @kOh <> 1)
        BEGIN
            BEGIN TRAN;

            UPDATE  hm
               SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF * @kWage,
                    hm.IMBIBE_SAR  = hm.IMBIBE_SAR  * @kOh
            OUTPUT  @RunId, 'S10', inserted.FNUMB,
                    TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
                    deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
                    CONCAT(N'ضريب تعديل هزينه تبديل ', FORMAT(@kWage, 'N5'))
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.HEAD_MANF hm
            WHERE   hm.GHEYMAT = @Month
              AND   EXISTS (
                        SELECT 1
                        FROM   dbo.HEAD_LST h
                        JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                        WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                          AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
                          AND (@Dep IS NULL OR h.DEPATMAN = @Dep));

            INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
            VALUES (@RunId, 'S10', 1,
                    CONCAT(N'واحد ', @UnitId, N': ضريب تعديل ',
                           FORMAT(@kWage, 'N5'), N' روي ', @@ROWCOUNT, N' فرمول'));

            COMMIT;
        END

        FETCH NEXT FROM cUnit INTO @UnitId, @Dep, @SplitMode;
    END

    CLOSE cUnit;
    DEALLOCATE cUnit;

    ---- خلاصه
    SELECT  u.UnitName                          AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل هزينه تبديل'
                            WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END   AS نوع,
            c.AbsorbedAmount                    AS جذب_شده,
            c.AbsorbedFromWip                   AS کنترل_از_751,
            c.ActualAmount                      AS واقعي,
            c.AdjustFactor                      AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S11 — انتشار نرخ، نسخه تولیدی

   سطح‌بندی درخت فرمول، سپس محاسبه از عمیق‌ترین سطح به سطح صفر.
   نتیجه در DTL_MANF نوشته و در CC_FormulaChange ثبت می‌شود.

   یک پاس، قطعی، بدون تکرار.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S11_PropagateRates
    @RunId  INT,
    @Month  TINYINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── ۱) يال‌هاي درخت ─── */
    IF OBJECT_ID('tempdb..#Edge') IS NOT NULL DROP TABLE #Edge;

    SELECT  DISTINCT
            CAST(h.CODE AS BIGINT) AS Parent,
            CAST(d.CODE AS BIGINT) AS Child
    INTO    #Edge
    FROM    dbo.HEAD_MANF h
    JOIN    dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE   h.GHEYMAT = @Month
      AND   h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND   CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);

    CREATE CLUSTERED INDEX IX_Edge ON #Edge(Parent, Child);

    /* ─── ۲) تشخيص حلقه؛ بدون اين، محاسبه بي‌نهايت مي‌شود ─── */
    IF OBJECT_ID('tempdb..#Cycle') IS NOT NULL DROP TABLE #Cycle;
    CREATE TABLE #Cycle (Code BIGINT PRIMARY KEY);

    ;WITH Walk AS (
        SELECT  Parent AS Root, Child, 1 AS Lvl,
                CAST('/' + CAST(Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM    #Edge
        UNION ALL
        SELECT  w.Root, e.Child, w.Lvl + 1,
                CAST(w.Pt + CAST(e.Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM    Walk w JOIN #Edge e ON e.Parent = w.Child
        WHERE   w.Lvl < 20
          AND   w.Pt NOT LIKE '%/' + CAST(e.Child AS VARCHAR(20)) + '/%'
    )
    INSERT #Cycle(Code)
    SELECT DISTINCT Root FROM Walk WHERE Child = Root
    OPTION (MAXRECURSION 0);

    IF EXISTS (SELECT 1 FROM #Cycle)
    BEGIN
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
        SELECT @RunId, 'S11', 'CHK-06', 5, 2, Code,
               N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
        FROM   #Cycle;

        RAISERROR(N'حلقه در ساختار فرمول يافت شد؛ محاسبه متوقف شد.', 16, 1);
        RETURN;
    END

    /* ─── ۳) سطح‌بندي ─── */
    IF OBJECT_ID('tempdb..#C') IS NOT NULL DROP TABLE #C;

    CREATE TABLE #C (
        Code  BIGINT PRIMARY KEY,
        Llc   SMALLINT NOT NULL DEFAULT 0,
        FNUMB INT      NULL,
        Src   TINYINT  NOT NULL DEFAULT 1,
        Mat   FLOAT    NOT NULL DEFAULT 0,
        Wage  FLOAT    NOT NULL DEFAULT 0,
        Oh    FLOAT    NOT NULL DEFAULT 0
    );

    INSERT #C (Code)
    SELECT Parent FROM #Edge UNION SELECT Child FROM #Edge;

    DECLARE @changed INT = 1, @guard INT = 0;

    WHILE @changed > 0 AND @guard < 30
    BEGIN
        UPDATE  c
           SET  c.Llc = x.NewLlc
        FROM    #C c
        JOIN   (SELECT e.Child, MAX(p.Llc) + 1 AS NewLlc
                FROM   #Edge e JOIN #C p ON p.Code = e.Parent
                GROUP BY e.Child) x ON x.Child = c.Code
        WHERE   x.NewLlc > c.Llc;

        SET @changed = @@ROWCOUNT;
        SET @guard  += 1;
    END

    CREATE INDEX IX_C_Llc ON #C(Llc);

    ---- فرمول هر کالا
    UPDATE  c
       SET  c.FNUMB = f.FNUMB,
            c.Src   = 2
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 hm.FNUMB
                   FROM   dbo.HEAD_MANF hm
                   WHERE  CAST(hm.CODE AS BIGINT) = c.Code AND hm.GHEYMAT = @Month
                   ORDER BY hm.DATE_ACTIV DESC, hm.FNUMB DESC) f;

    /* ─── ۴) نرخ مواد خريدني: ميانگين وزني خروج از انبار ─── */
    UPDATE  c
       SET  c.Mat = z.fi, c.Src = 1
    FROM    #C c
    JOIN   (SELECT k.code, SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
            FROM   dbo.KALAS k
            WHERE  k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
            GROUP BY k.code) z ON z.code = c.Code
    WHERE   c.FNUMB IS NULL AND z.fi IS NOT NULL;

    ---- بدون گردش در ماه: آخرين نرخ ميانگين ثبت‌شده
    UPDATE  c
       SET  c.Mat = lp.AVRAGE
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 i.AVRAGE
                   FROM   dbo.INVO_LST i
                   JOIN   dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                   WHERE  CAST(i.CODE AS BIGINT) = c.Code AND i.AVRAGE > 0
                   ORDER BY h.DATE_N DESC, i.NUMBER DESC) lp
    WHERE   c.FNUMB IS NULL AND c.Mat = 0;

    UPDATE #C SET Src = 3 WHERE FNUMB IS NULL AND Mat = 0;

    /* ─── ۵) محاسبه از عميق‌ترين سطح به سطح صفر ───
       چون فرزندها هميشه سطح عميق‌تري از والد دارند، وقتي به والد
       مي‌رسيم بهاي همه اجزايش قبلاً محاسبه شده است. */

    DECLARE @lvl SMALLINT = (SELECT MAX(Llc) FROM #C);
    DECLARE @totalChanges INT = 0;

    WHILE @lvl >= 0
    BEGIN
        IF @WhatIf = 0
        BEGIN
            BEGIN TRAN;

            ---- ۵-الف) نرخ اجزا در فرمول والدهاي اين سطح
            UPDATE  d
               SET  d.SMABL = ch.Mat + ch.Wage + ch.Oh,
                    d.MABLK = ROUND((ch.Mat + ch.Wage + ch.Oh) * d.MEGHk, 0)
            OUTPUT  @RunId, 'S11', inserted.FNUMB,
                    NULL, TRY_CAST(inserted.CODE AS BIGINT), 'SMABL',
                    deleted.SMABL, inserted.SMABL,
                    N'انتشار نرخ — سطح‌بندي BOM'
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.DTL_MANF  d
            JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
            JOIN    #C p  ON p.Code  = CAST(hm.CODE AS BIGINT) AND p.Llc = @lvl
            JOIN    #C ch ON ch.Code = CAST(d.CODE  AS BIGINT)
            WHERE   ABS(ISNULL(d.SMABL, 0) - (ch.Mat + ch.Wage + ch.Oh)) > 0.5;

            SET @totalChanges += @@ROWCOUNT;

            COMMIT;
        END

        ---- ۵-ب) بهاي والد = مجموع اجزا + جذب دستمزد + جذب سربار
        UPDATE  c
           SET  c.Mat  = ISNULL(a.MatCost, 0),
                c.Wage = ISNULL(hm.IMBIBE_MANF, 0),
                c.Oh   = ISNULL(hm.IMBIBE_SAR , 0)
        FROM    #C c
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = c.FNUMB
        CROSS   APPLY (SELECT SUM(d.MEGHk * (ch.Mat + ch.Wage + ch.Oh)) AS MatCost
                       FROM   dbo.DTL_MANF d
                       JOIN   #C ch ON ch.Code = CAST(d.CODE AS BIGINT)
                       WHERE  d.FNUMB = c.FNUMB) a
        WHERE   c.Llc = @lvl AND c.FNUMB IS NOT NULL;

        SET @lvl -= 1;
    END

    /* ─── ۶) ثبت نتيجه در CC_ItemCost ─── */
    DELETE dbo.CC_ItemCost WHERE RunId = @RunId;

    INSERT dbo.CC_ItemCost
        (RunId, PeriodMonth, Code, LowLevelCode, SourceKind, FNUMB,
         MaterialCost, WageCost, OverheadCost)
    SELECT  @RunId, @Month, Code, Llc, Src, FNUMB, Mat, Wage, Oh
    FROM    #C;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S11', 1,
            CONCAT(N'انتشار نرخ: ', @totalChanges, N' نرخ به‌روز شد'),
            (SELECT MAX(Llc) AS maxLevel, COUNT(*) AS items,
                    SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END) AS noSource
             FROM #C FOR JSON PATH));

    /* ─── ۷) آزمون سلامت: CHK-09 بايد صفر شود ─── */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-09';

    ;WITH Khod AS (
        SELECT CAST(hm.CODE AS BIGINT) AS Code,
               SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                      + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha
        FROM   dbo.HEAD_MANF hm JOIN dbo.DTL_MANF d ON d.FNUMB = hm.FNUMB
        WHERE  hm.GHEYMAT = @Month
        GROUP BY CAST(hm.CODE AS BIGINT), hm.FNUMB
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code, AVG(d.SMABL) AS Nerkh
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S11', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            N'نرخ پس از اجراي موتور هنوز منتشر نشده — نياز به بررسي'
    FROM    Khod k JOIN DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > 0.001;

    /* ─── خلاصه ─── */
    SELECT  Llc                                          AS سطح,
            COUNT(*)                                     AS تعداد_کالا,
            SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END)     AS بدون_منبع_نرخ
    FROM    #C
    GROUP BY Llc ORDER BY Llc;

    SELECT  @totalChanges AS تعداد_نرخ_به‌روز_شده,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND RuleCode = 'CHK-09' AND IsResolved = 0)
                          AS نرخ_منتشر_نشده_باقيمانده;
END
GO


PRINT N'موتور نرخ توليدي (S10 و S11) ايجاد شد.';

/* نمونه:
   EXEC dbo.CC_sp_S10_BalanceConversion @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_S11_PropagateRates    @RunId=1, @Month=5, @WhatIf=1;
*/
GO
";
            TryExecuteCostCloseBatch(db, rateEngine, "CC_sp_S10_BalanceConversion و CC_sp_S11_PropagateRates",
                "اسکریپت 15-rate-engine-production.sql را اجرا کنید (به CC_ConversionCost, CC_UnitAcc, CC_ItemCost نیاز دارد).");

            string rollback = @"
/* ═══════════════════════════════════════════════════════════════════
   بازگردانی از اسنپ‌شات

   هر گام نویسنده پیش از اجرا اسنپ‌شات می‌گیرد. این رویه آن را
   برمی‌گرداند و اجرا را به وضعیت «بازگردانی‌شده» می‌برد.

   بدون این، اجرای موتور روی داده واقعی ریسک دارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_Rollback
    @RunId    INT,
    @StepCode VARCHAR(10) = NULL,   -- خالي = بازگرداني کل اجرا (قديمي‌ترين اسنپ‌شات)
    @UserName NVARCHAR(50) = N'system',
    @WhatIf   BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- دورهٔ تأييدشده قفل است
    -- CC_sp_S14_ApproveClose به کاربر مي‌گويد «دوره تأييد و قفل شد»؛ اگر
    -- بازگرداني بتواند بعد از آن اجرا شود، آن قفل واقعي نيست و يک بستنِ
    -- رسميِ تأييدشده بي‌صدا باطل مي‌شود. تأييد بايد اول برداشته شود.
    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE RunId = @RunId AND ApprovedAtUtc IS NOT NULL)
    BEGIN
        RAISERROR(N'اين اجرا تأييد و قفل شده است؛ بازگرداني ممکن نيست.', 16, 1);
        RETURN;
    END

    ---- اسنپ‌شات‌هاي قابل استفاده
    -- MIN و نه MAX: در يک اجراي کامل چند گام اسنپ‌شات مي‌گيرند (S02, S09,
    -- S10, S11) و هرکدام هر سه جدول را نگه مي‌دارند. S03 و S04 که اسناد را
    -- حذف و بازشماره مي‌کنند بين اسنپ‌شات S02 و اسنپ‌شات S09 اجرا مي‌شوند،
    -- پس اسنپ‌شات‌هاي بعدي وضعيتِ «بعد از S03/S04» را در خود دارند. اگر
    -- بازگرداني کل اجرا از آخرين اسنپ‌شات انجام شود، حذف و بازشماره‌گذاري
    -- هرگز برنمي‌گردد — درحالي‌که هم دکمهٔ رابط کاربري و هم نام اين رويه به
    -- کاربر قول «بازگشت به وضعيت پيش از اجرا» را مي‌دهند. قديمي‌ترين
    -- اسنپ‌شات همان وضعيت پيش از اجراست. براي بازگرداني يک گام مشخص هم
    -- درست است، چون هر گام براي هر جدول فقط يک اسنپ‌شات دارد.
    IF OBJECT_ID('tempdb..#Snap') IS NOT NULL DROP TABLE #Snap;

    SELECT  s.SnapshotId, s.TableName, s.BackupTable, s.RowsCopied, s.StepCode
    INTO    #Snap
    FROM    dbo.CC_Snapshot s
    JOIN   (SELECT TableName, MIN(SnapshotId) AS Id
            FROM   dbo.CC_Snapshot
            WHERE  RunId = @RunId
              AND (@StepCode IS NULL OR StepCode = @StepCode)
              AND  RestoredAtUtc IS NULL
            GROUP BY TableName) x ON x.Id = s.SnapshotId;

    IF NOT EXISTS (SELECT 1 FROM #Snap)
    BEGIN
        SELECT N'اسنپ‌شات قابل بازگرداني يافت نشد' AS پيام;
        RETURN;
    END

    ---- بررسي وجود واقعي جداول پشتيبان
    DECLARE @missing NVARCHAR(MAX) = NULL;

    SELECT  @missing = STRING_AGG(BackupTable, ', ')
    FROM    #Snap
    WHERE   OBJECT_ID('dbo.' + BackupTable, 'U') IS NULL;

    IF @missing IS NOT NULL
    BEGIN
        RAISERROR(N'جدول پشتيبان يافت نشد: %s', 16, 1, @missing);
        RETURN;
    END

    IF @WhatIf = 1
    BEGIN
        SELECT  TableName   AS جدول,
                BackupTable AS جدول_پشتيبان,
                RowsCopied  AS تعداد_سطر,
                StepCode    AS گام
        FROM    #Snap ORDER BY TableName;

        SELECT N'حالت گزارش — چيزي بازگردانده نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @tbl SYSNAME, @bak SYSNAME, @sql NVARCHAR(MAX), @n INT = 0, @inserted INT;

    DECLARE cSnap CURSOR LOCAL FAST_FORWARD FOR
        SELECT TableName, BackupTable FROM #Snap;

    OPEN cSnap;
    FETCH NEXT FROM cSnap INTO @tbl, @bak;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @tbl = 'DTL_MANF'
        BEGIN
            SET @sql = N'
                UPDATE  d
                   SET  d.SMABL = b.SMABL,
                        d.MABLK = b.MABLK,
                        d.MEGHk = b.MEGHk,
                        d.PERT  = b.PERT
                FROM    dbo.DTL_MANF d
                JOIN    dbo.' + QUOTENAME(@bak) + N' b
                        ON b.FNUMB = d.FNUMB AND b.CODE = d.CODE';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;
        END
        ELSE IF @tbl = 'HEAD_MANF'
        BEGIN
            SET @sql = N'
                UPDATE  h
                   SET  h.IMBIBE_MANF = b.IMBIBE_MANF,
                        h.IMBIBE_SAR  = b.IMBIBE_SAR
                FROM    dbo.HEAD_MANF h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.FNUMB = h.FNUMB';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;
        END
        ELSE IF @tbl = 'DEED_HED'
        BEGIN
            -- بازگرداني شماره اسناد و اسناد حذف‌شده؛ ۹ جدول فرزند خودکار دنبال
            -- مي‌آيند. سه مرحله، به همان دليلي که CC_sp_S04_SortDeeds دو-مرحله‌اي
            -- است: اگر شمارهٔ اصليِ يک سند برابر شمارهٔ فعليِ سند ديگري باشد که
            -- هنوز به حالت اصلي‌اش برنگشته، UPDATE يا INSERT مستقيم به
            -- PRIMARY KEY تکراري مي‌خورد.
            EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

            -- ۱) هر سندي که شماره‌اش فرق کرده را به يک بازهٔ منفيِ ناهم‌پوشان
            --    مي‌بريم تا شمارهٔ اصلي‌اش براي درج سندهاي حذف‌شده (مرحلهٔ ۲) و
            --    بازگرداني خودش (مرحلهٔ ۳) آزاد و بدون برخورد باشد.
            SET @sql = N'
                UPDATE  h
                   SET  h.N_S = -3000000.0 - h.N_S
                FROM    dbo.DEED_HED h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.base = h.base
                WHERE   h.N_S <> b.N_S';
            EXEC sp_executesql @sql;

            -- ۲) سندهايي که CC_sp_S03_DeleteEmptyDeeds کامل حذف کرده بود را با
            --    همان base و همان مقادير همهٔ ستون‌ها دوباره درج مي‌کنيم. امن
            --    است چون مرحلهٔ ۱ هر شمارهٔ زندهٔ همپوشان را قبلاً کنار زده.
            -- @@ROWCOUNT را بلافاصله بعد از INSERT، داخل همان دستهٔ پویا، در
            -- @inserted می‌ریزیم — چون SET IDENTITY_INSERT OFF که بعدش لازم
            -- است خودش یک دستور SET است و @@ROWCOUNT را در نشستِ فراخوان صفر
            -- می‌کند (رفتار واقعی SQL Server، با آزمایش مستقیم تأیید شد). بدون
            -- این، بازگردانیِ سندی که فقط حذف شده بود (بدون تغییر شماره) به
            -- کاربر «۰ سطر بازگردانده شد» نشان می‌داد، با اینکه سند واقعاً
            -- برگشته بود.
            SET @sql = N'
                SET IDENTITY_INSERT dbo.DEED_HED ON;
                INSERT INTO dbo.DEED_HED
                    (N_S, DATE_S, SHARH_S, NO_S, ANBAR, N_FACTOR, GHATEI, USER_NAME,
                     base, SGN1, SGN2, SGN3, SGN4, OKF, sgn1usid, sgn2usid, sgn3usid,
                     CRT, UID, BAYEG)
                SELECT b.N_S, b.DATE_S, b.SHARH_S, b.NO_S, b.ANBAR, b.N_FACTOR, b.GHATEI,
                       b.USER_NAME, b.base, b.SGN1, b.SGN2, b.SGN3, b.SGN4, b.OKF,
                       b.sgn1usid, b.sgn2usid, b.sgn3usid, b.CRT, b.UID, b.BAYEG
                FROM   dbo.' + QUOTENAME(@bak) + N' b
                WHERE  NOT EXISTS (SELECT 1 FROM dbo.DEED_HED h WHERE h.base = b.base);
                SET @ins = @@ROWCOUNT;
                SET IDENTITY_INSERT dbo.DEED_HED OFF;';
            EXEC sp_executesql @sql, N'@ins INT OUTPUT', @ins = @inserted OUTPUT;
            SET @n += @inserted;

            -- ۳) سندهاي مرحلهٔ ۱ را از بازهٔ منفي به شمارهٔ اصلي‌شان برمي‌گردانيم.
            SET @sql = N'
                UPDATE  h
                   SET  h.N_S = b.N_S
                FROM    dbo.DEED_HED h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.base = h.base
                WHERE   h.N_S < 0';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;

            EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;
        END

        FETCH NEXT FROM cSnap INTO @tbl, @bak;
    END

    CLOSE cSnap;
    DEALLOCATE cSnap;

    ---- علامت‌گذاري اسنپ‌شات‌ها
    -- در بازگرداني کل اجرا، اسنپ‌شات‌هاي مياني (S09/S10/S11) هم مصرف‌شده
    -- حساب مي‌شوند؛ وگرنه بازگرداني دوباره، قديمي‌ترينِ باقيمانده يعني وضعيت
    -- «بعد از S03/S04» را روي داده‌اي که همين الان درست برگشته مي‌نويسد.
    UPDATE  s
       SET  s.RestoredAtUtc = SYSUTCDATETIME()
    FROM    dbo.CC_Snapshot s
    WHERE   s.RunId = @RunId
      AND   s.RestoredAtUtc IS NULL
      AND  (@StepCode IS NULL OR s.StepCode = @StepCode);

    ---- تغييرات ثبت‌شده باطل مي‌شوند
    DELETE dbo.CC_FormulaChange
    WHERE  RunId = @RunId
      AND (@StepCode IS NULL OR StepCode = @StepCode);

    ---- وضعيت اجرا
    UPDATE dbo.CC_Run
       SET Status = 5,                       -- بازگردانی‌شده
           FormulasDirty = 0,
           FinishedAtUtc = SYSUTCDATETIME()
     WHERE RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 2,
            CONCAT(N'بازگرداني توسط ', @UserName, N': ', @n, N' سطر'));

    COMMIT;

    SELECT @n AS تعداد_سطر_بازگردانده_شده;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   پاکسازی اسنپ‌شات‌های قدیمی
   جداول CC_BAK_* بعد از ۹۰ روز حذف می‌شوند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_PurgeSnapshots
    @OlderThanDays INT = 90,
    @WhatIf        BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#Old') IS NOT NULL DROP TABLE #Old;

    SELECT  SnapshotId, BackupTable, TakenAtUtc
    INTO    #Old
    FROM    dbo.CC_Snapshot
    WHERE   TakenAtUtc < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());

    IF @WhatIf = 1
    BEGIN
        SELECT BackupTable AS جدول, TakenAtUtc AS تاريخ FROM #Old;
        SELECT COUNT(*) AS تعداد_قابل_حذف FROM #Old;
        RETURN;
    END

    DECLARE @bak SYSNAME;
    DECLARE cOld CURSOR LOCAL FAST_FORWARD FOR SELECT BackupTable FROM #Old;

    OPEN cOld;
    FETCH NEXT FROM cOld INTO @bak;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
            EXEC('DROP TABLE dbo.' + @bak);

        FETCH NEXT FROM cOld INTO @bak;
    END

    CLOSE cOld;
    DEALLOCATE cOld;

    DELETE s FROM dbo.CC_Snapshot s JOIN #Old o ON o.SnapshotId = s.SnapshotId;

    SELECT COUNT(*) AS تعداد_حذف_شده FROM #Old;
END
GO

PRINT N'رويه‌هاي بازگرداني و پاکسازي ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, rollback, "CC_sp_Rollback و CC_sp_PurgeSnapshots",
                "اسکریپت 16-rollback.sql را اجرا کنید (به CC_Snapshot نیاز دارد).");

            string varianceSteps = @"
/* ═══════════════════════════════════════════════════════════════════
   S07 تا S09 — بازتولید، انحراف، و تخصیص

   S07  بازتولید خروج مواد + انبارگردانی  (بازنویسی مجموعه‌ای)
   S08  محاسبه انحراف مصرف
   S09  تخصیص انحراف با تصمیم کاربر
   S09a تولید پیشنهاد پیش‌فرض از ماه قبل

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S07 — بازتولید خروج مواد و انبارگردانی

   جایگزین اسکریپت فعلی با دو کرسر تودرتو و sp_executesql.
   خروج مواد یک INSERT مجموعه‌ای است؛ انبارگردانی کرسر روز دارد
   چون dbo.MOGUDI تابع جدولی پارامتری است.

   انبارها از CC_UnitAnbar خوانده می‌شوند، نه از کد. با این کار
   باگ تکرار انبار ۸ در اسکریپت فعلی موضوعیت خود را از دست می‌دهد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S07_RebuildIssue
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── بخش يک: خروج مواد ─── */
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  h.NUMBER  AS ProdNo,
            h.NUMBER1 AS IssueNo
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    WHERE   h.TAG = 9
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   EXISTS (SELECT 1 FROM dbo.HEAD_LST x
                    WHERE x.NUMBER = h.NUMBER1 AND x.TAG = 10);

    CREATE CLUSTERED INDEX IX_Prod ON #Prod(IssueNo);

    IF @WhatIf = 1
    BEGIN
        SELECT  COUNT(*) AS تعداد_برگه_توليد,
                (SELECT COUNT(*) FROM dbo.INVO_LST i
                 JOIN #Prod p ON p.IssueNo = i.NUMBER AND i.TAG = 10) AS سطر_فعلي_خروج
        FROM    #Prod;
        RETURN;
    END

    BEGIN TRAN;

    DELETE  i
    FROM    dbo.INVO_LST i
    JOIN    #Prod p ON p.IssueNo = i.NUMBER AND i.TAG = 10;

    DECLARE @deleted INT = @@ROWCOUNT;

    INSERT dbo.INVO_LST
        (NUMBER, TAG, ANBAR, CODE, VAHED_K, MEGH, MEGHK,
         N_RASID, MABL, AVRAGE, MABL_K)
    SELECT  p.IssueNo, 10, dm.ANBAR, dm.CODE, dm.VAHED_K,
            (dm.MEGH  + dm.PERT) * pl.MEGHK,
            (dm.MEGHK + dm.PERT) * pl.MEGHK,
            dm.FNUMB, 1, 1,
            (dm.MEGHK + dm.PERT) * pl.MEGHK
    FROM    #Prod p
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = p.ProdNo AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                            AND hm.GHEYMAT = @Month
    JOIN    dbo.DTL_MANF  dm ON dm.FNUMB  = hm.FNUMB;

    DECLARE @inserted INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S07', 1,
            CONCAT(N'بازتوليد خروج مواد: ', @deleted, N' حذف، ', @inserted, N' درج'),
            (SELECT @deleted AS deleted, @inserted AS inserted FOR JSON PATH));

    COMMIT;

    /* ─── بخش دو: انبارگرداني ─── */
    DECLARE @anb INT, @grdNum INT, @grdDate INT, @countRows INT = 0;

    DECLARE cAnb CURSOR LOCAL FAST_FORWARD FOR
        SELECT   ua.Anbar
        FROM     dbo.CC_UnitAnbar ua
        JOIN     dbo.CC_Unit u ON u.UnitId = ua.UnitId AND u.IsActive = 1
        WHERE    ua.DoStockCount = 1
        GROUP BY ua.Anbar, ua.SeqNo      -- يک انبار در دو واحد = يک بار پردازش
        ORDER BY ua.SeqNo;

    OPEN cAnb;
    FETCH NEXT FROM cAnb INTO @anb;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DELETE  l
        FROM    dbo.ANBGRD_LST l
        JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
        WHERE   h.GRD_ANBAR = @anb AND h.GRD_DATE BETWEEN @DT1 AND @DT2;

        DECLARE cDay CURSOR LOCAL FAST_FORWARD FOR
            SELECT GRD_NUM, GRD_DATE
            FROM   dbo.ANBGRD_HEAD
            WHERE  GRD_ANBAR = @anb AND GRD_DATE BETWEEN @DT1 AND @DT2
            ORDER BY GRD_DATE;

        OPEN cDay;
        FETCH NEXT FROM cDay INTO @grdNum, @grdDate;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            INSERT dbo.ANBGRD_LST (CODE, MOG, GRD_NUM)
            SELECT CODE, MAND, @grdNum FROM dbo.MOGUDI(@grdDate, @anb);

            SET @countRows += @@ROWCOUNT;
            FETCH NEXT FROM cDay INTO @grdNum, @grdDate;
        END

        CLOSE cDay;
        DEALLOCATE cDay;

        FETCH NEXT FROM cAnb INTO @anb;
    END

    CLOSE cAnb;
    DEALLOCATE cAnb;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S07', 1, CONCAT(N'انبارگرداني: ', @countRows, N' سطر'));

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (CostCloseController/S07_RebuildIssue)؛
    -- برای بازبینی دستی در SSMS از دو خلاصه بالا (INSERT به CC_RunLog) استفاده کنید.
    SELECT @deleted AS Deleted, @inserted AS Inserted, @countRows AS StockCount;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S08 — محاسبه انحراف مصرف

   مانده انبار مواد مصرفی تولید = انحراف مصرف
   (به شرط صفر بودن کالای در جریان ساخت)

   همان zanbekht{MM}، ولی با مبلغ ریالی و درصد نسبت به مصرف،
   تا بتوان بر اساس اهمیت مرتب کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S08_CalcVariance
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Variance WHERE RunId = @RunId;

    ---- انبارهاي «مبناي انحراف» هر واحد
    INSERT dbo.CC_Variance
        (RunId, Anbar, Code, QtyVariance, UnitRate, AmountVariance, ConsumedQty)
    SELECT  @RunId,
            h.GRD_ANBAR,
            l.CODE,
            SUM(l.MOG - ISNULL(l.NUM3, 0))                     AS QtyVar,
            MAX(ic.TotalCost)                                  AS Rate,
            SUM(l.MOG - ISNULL(l.NUM3, 0)) * MAX(ic.TotalCost) AS AmtVar,
            MAX(u.Consumed)                                    AS Consumed
    FROM    dbo.ANBGRD_LST  l
    JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
    JOIN    dbo.CC_UnitAnbar ua ON ua.Anbar = h.GRD_ANBAR AND ua.AnbarRole = 1
    LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = l.CODE AND ic.RunId = @RunId
    OUTER   APPLY (
                SELECT SUM(pl.MEGHK * d.MEGHk) AS Consumed
                FROM   dbo.HEAD_LST  hl
                JOIN   dbo.INVO_LST  pl ON pl.NUMBER = hl.NUMBER AND pl.TAG = 9
                JOIN   dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                       AND hm.GHEYMAT = @Month
                JOIN   dbo.DTL_MANF  d  ON d.FNUMB   = hm.FNUMB
                                       AND CAST(d.CODE AS BIGINT) = l.CODE
                WHERE  hl.TAG = 9 AND hl.DATE_N BETWEEN @DT1 AND @DT2
            ) u
    WHERE   h.GRD_DATE BETWEEN @DT1 AND @DT2
    GROUP BY h.GRD_ANBAR, l.CODE
    HAVING  ABS(SUM(l.MOG - ISNULL(l.NUM3, 0))) > 0.0001;

    ---- کالاي کليدي: بالاي يک درصد کل انحراف
    DECLARE @total FLOAT =
        (SELECT SUM(ABS(ISNULL(AmountVariance, 0)))
         FROM dbo.CC_Variance WHERE RunId = @RunId);

    IF @total > 0
        UPDATE dbo.CC_Variance
           SET IsKeyItem = 1
         WHERE RunId = @RunId
           AND ABS(ISNULL(AmountVariance, 0)) > @total * 0.01;

    ---- CHK-11: انحراف روي ماده‌اي که در هيچ فرمولي مصرف نشده
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-11';

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
    SELECT  @RunId, 'S08', 'CHK-11', 11, 1, v.Anbar, v.Code, v.AmountVariance,
            N'انحراف روي ماده‌اي که در هيچ فرمول اين ماه مصرف نشده'
    FROM    dbo.CC_Variance v
    WHERE   v.RunId = @RunId
      AND   ISNULL(v.ConsumedQty, 0) = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S08', 1,
            CONCAT(N'انحراف مصرف: ', COUNT(*), N' کالا، جمع ',
                   FORMAT(SUM(ISNULL(AmountVariance,0)), 'N0'), N' ريال'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN IsKeyItem = 1 THEN 1 ELSE 0 END) AS keyItems,
                    SUM(ISNULL(AmountVariance,0)) AS netAmount
             FROM dbo.CC_Variance WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_Variance WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S08_CalcVariance.VarianceSummary)
    SELECT  COUNT(*)                                        AS Items,
            SUM(CASE WHEN IsKeyItem = 1 THEN 1 ELSE 0 END)  AS KeyItems,
            SUM(ISNULL(AmountVariance, 0))                  AS NetAmount,
            SUM(ABS(ISNULL(AmountVariance, 0)))             AS GrossAmount
    FROM    dbo.CC_Variance WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S09a — تولید پیشنهاد پیش‌فرض

   زنجیره سه‌مرحله‌ای:
     ۱) فرمول مقصد ماه قبل امسال هم هست  → همان تصمیم (Manual)
     ۲) نیست ولی ماده مصرف شده           → تسهیم (Prorata)
     ۳) ماده اصلاً مصرف نشده             → بدون تخصیص (Ignore)

   کلید حمل تصمیم بین ماه‌ها TargetCode است نه TargetFNUMB، چون
   GHEYMAT شماره ماه است و فرمول هر ماه FNUMB جداگانه دارد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S09a_SeedDecisions
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_VarianceDecision WHERE RunId = @RunId;

    ;WITH Prev AS (
        SELECT  d.Code, d.Mode, d.TargetCode,
                ROW_NUMBER() OVER (PARTITION BY d.Code
                                   ORDER BY d.DecisionId DESC) AS rn
        FROM    dbo.CC_VarianceDecision d
        JOIN    dbo.CC_Run r ON r.RunId = d.RunId
        WHERE   r.Status = 3            -- فقط از اجراهاي تکميل‌شده
          AND   d.RunId <> @RunId
    )
    INSERT dbo.CC_VarianceDecision
        (RunId, Code, Mode, TargetCode, TargetFNUMB, DecidedBy, Note)
    SELECT  @RunId,
            v.Code,
            CASE
              WHEN p.Mode = 1 AND hm.FNUMB IS NOT NULL THEN 1   -- ادامه تصميم قبلي
              WHEN ISNULL(v.ConsumedQty, 0) > 0         THEN 2   -- تسهيم
              ELSE 3                                             -- بدون تخصيص
            END,
            CASE WHEN hm.FNUMB IS NOT NULL THEN p.TargetCode END,
            hm.FNUMB,
            N'system',
            CASE
              WHEN p.Mode = 1 AND hm.FNUMB IS NOT NULL
                   THEN N'مثل ماه قبل'
              WHEN p.Mode = 1 AND hm.FNUMB IS NULL
                   THEN N'فرمول مقصد ماه قبل امسال نيست — تسهيم'
              WHEN ISNULL(v.ConsumedQty, 0) = 0
                   THEN N'ماده در هيچ فرمولي مصرف نشده — بررسي شود'
              ELSE N'تصميم جديد'
            END
    FROM    dbo.CC_Variance v
    LEFT    JOIN Prev p ON p.Code = v.Code AND p.rn = 1
    OUTER   APPLY (SELECT TOP 1 h.FNUMB
                   FROM   dbo.HEAD_MANF h
                   WHERE  CAST(h.CODE AS BIGINT) = p.TargetCode
                     AND  h.GHEYMAT = @Month
                   ORDER BY h.FNUMB DESC) hm
    WHERE   v.RunId = @RunId;

    ---- CHK-12: تصميم ماه قبل قابل ادامه نيست
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-12';

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  @RunId, 'S09', 'CHK-12', 15, 1, d.Code,
            N'فرمول مقصد ماه قبل در اين ماه وجود ندارد؛ پيش‌فرض روي تسهيم رفت'
    FROM    dbo.CC_VarianceDecision d
    WHERE   d.RunId = @RunId
      AND   d.Note LIKE N'%امسال نيست%';

    SELECT  CASE Mode WHEN 1 THEN N'اختصاص'
                      WHEN 2 THEN N'تسهيم'
                      ELSE N'بدون تخصيص' END AS حالت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_VarianceDecision
    WHERE   RunId = @RunId
    GROUP BY Mode ORDER BY Mode;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S09 — اعمال تصمیم‌ها

   Manual   کل انحراف کالا به یک فرمول مشخص
   Prorata  تسهیم بین فرمول‌هایی که آن ماده را مصرف کرده‌اند
   Ignore   دست‌نخورده در حساب ۷۷۲ می‌ماند

   تغییر روی MEGHk انجام می‌شود، به ازای یک واحد محصول:
   مقدار افزوده = سهم انحراف ÷ مقدار تولید همان محصول
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S09_ApplyDecisions
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- مقدار توليد هر فرمول در ماه
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB,
            SUM(pl.MEGHK)             AS ProdQty
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
    GROUP BY TRY_CAST(pl.N_KOL AS INT)
    HAVING  SUM(pl.MEGHK) > 0;

    CREATE UNIQUE CLUSTERED INDEX IX_Prod ON #Prod(FNUMB);

    ---- سهم هر فرمول از انحراف هر ماده
    IF OBJECT_ID('tempdb..#Share') IS NOT NULL DROP TABLE #Share;

    ;WITH Usage AS (
        SELECT  d.FNUMB,
                CAST(d.CODE AS BIGINT) AS Code,
                p.ProdQty * d.MEGHk    AS UsedQty
        FROM    dbo.DTL_MANF  d
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        JOIN    #Prod p ON p.FNUMB = d.FNUMB
        WHERE   d.MEGHk > 0
    )
    SELECT  u.FNUMB,
            u.Code,
            v.QtyVariance,
            dc.Mode,
            CASE
              -- اختصاص: کل انحراف به همان يک فرمول
              WHEN dc.Mode = 1 AND u.FNUMB = dc.TargetFNUMB THEN 1.0
              -- تسهيم: به نسبت مصرف
              WHEN dc.Mode = 2
                   THEN u.UsedQty / NULLIF(SUM(u.UsedQty) OVER (PARTITION BY u.Code), 0)
              ELSE 0
            END AS Ratio
    INTO    #Share
    FROM    Usage u
    JOIN    dbo.CC_Variance          v  ON v.Code  = u.Code AND v.RunId  = @RunId
    JOIN    dbo.CC_VarianceDecision  dc ON dc.Code = u.Code AND dc.RunId = @RunId
    WHERE   dc.Mode IN (1, 2);

    DELETE #Share WHERE Ratio IS NULL OR Ratio = 0;

    IF @WhatIf = 1
    BEGIN
        SELECT  s.FNUMB                              AS شماره_فرمول,
                s.Code                               AS کد_ماده,
                st.NAME                              AS نام_ماده,
                CASE s.Mode WHEN 1 THEN N'اختصاص'
                            ELSE N'تسهيم' END        AS حالت,
                s.QtyVariance                        AS کل_انحراف,
                s.Ratio                              AS سهم,
                s.QtyVariance * s.Ratio              AS مقدار_سهم,
                p.ProdQty                            AS مقدار_توليد,
                s.QtyVariance * s.Ratio / p.ProdQty  AS افزايش_در_فرمول
        FROM    #Share s
        JOIN    #Prod  p  ON p.FNUMB = s.FNUMB
        LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = s.Code
        ORDER BY ABS(s.QtyVariance * s.Ratio) DESC;
        RETURN;
    END

    BEGIN TRAN;

    UPDATE  d
       SET  d.MEGHk = d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty),
            d.MABLK = ROUND(ISNULL(d.SMABL, 0) *
                            (d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty)), 0)
    OUTPUT  @RunId, 'S09', inserted.FNUMB,
            NULL, TRY_CAST(inserted.CODE AS BIGINT), 'MEGHk',
            deleted.MEGHk, inserted.MEGHk,
            N'تخصيص انحراف مصرف'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.DTL_MANF d
    JOIN    #Share s ON s.FNUMB = d.FNUMB
                    AND s.Code  = CAST(d.CODE AS BIGINT)
    JOIN    #Prod  p ON p.FNUMB = d.FNUMB;

    DECLARE @n INT = @@ROWCOUNT;

    ---- ثبت مقدار اعمال‌شده در تصميم‌ها
    UPDATE  dc
       SET  dc.AppliedQty = x.Applied
    FROM    dbo.CC_VarianceDecision dc
    JOIN   (SELECT Code, SUM(QtyVariance * Ratio) AS Applied
            FROM   #Share GROUP BY Code) x ON x.Code = dc.Code
    WHERE   dc.RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S09', 1,
            CONCAT(N'تخصيص انحراف: ', @n, N' سطر فرمول به‌روز شد'));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (S09_ApplyDecisions.ApplyResult)
    SELECT @n AS Value;
END
GO


PRINT N'رويه‌هاي S07 تا S09 ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, varianceSteps,
                "CC_sp_S07_RebuildIssue، CC_sp_S08_CalcVariance، CC_sp_S09_ApplyDecisions، CC_sp_S09a_SeedDecisions",
                "اسکریپت 17-variance-steps.sql را اجرا کنید (به CC_Variance, CC_VarianceDecision, CC_UnitAnbar نیاز دارد).");

            string marginReportApprove = @"
/* ═══════════════════════════════════════════════════════════════════
   S12 تا S14 — سود کالا، گزارش هیئت‌مدیره، تأیید نهایی

   S12  سود و زیان به تفکیک کالا + اعمال هدف حاشیه
   S13  داده گزارش اکسل
   S14  تأیید و قفل دوره

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S12 که در CC_ItemMargin (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* جدول نتیجه سود کالا */
IF OBJECT_ID('dbo.CC_ItemMargin','U') IS NULL
CREATE TABLE dbo.CC_ItemMargin (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT      NOT NULL,
    Code          BIGINT   NOT NULL,
    QtySold       FLOAT    NOT NULL DEFAULT 0,
    WeightKg      FLOAT    NULL,
    SalesAmount   FLOAT    NOT NULL DEFAULT 0,   -- مبلغ خالص فروش
    CostAmount    FLOAT    NOT NULL DEFAULT 0,   -- بهاي تمام‌شده کالاي فروش‌رفته
    Profit        AS (SalesAmount - CostAmount) PERSISTED,
    UnitCost      FLOAT    NULL,
    UnitPrice     FLOAT    NULL,
    CalculatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CC_ItemMargin UNIQUE (RunId, Code)
);
GO


/* ═══════════════════════════════════════════════════════════════════
   S12 — محاسبه سود و زیان کالا

   فروش    از فاکتورهای TAG=2
   بها     از حساب قیمت تمام‌شده (GHEYMAT) به تفکیک کالا
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S12_CalcMargin
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMargin WHERE RunId = @RunId;

    ;WITH Forush AS (
        SELECT  CAST(i.CODE AS BIGINT)                       AS Code,
                SUM(i.MEGHk)                                 AS Qty,
                SUM(i.MEGH)                                  AS Weight,
                SUM(i.MABL_K - ISNULL(i.N_MOIN, 0))          AS NetSales
        FROM    dbo.INVO_LST i
        JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
        WHERE   i.TAG = 2 AND h.DATE_N BETWEEN @DT1 AND @DT2
        GROUP BY CAST(i.CODE AS BIGINT)
    ),
    Baha AS (
        -- بهاي تمام‌شده کالاي فروش‌رفته از سند حسابداري
        SELECT  TRY_CAST(d.HES_M AS BIGINT) AS Code,
                SUM(d.BED) - SUM(d.BES)     AS Cost
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.TAG = 13
          AND   h.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(d.HES_M AS BIGINT)
    )
    INSERT dbo.CC_ItemMargin
        (RunId, Code, QtySold, WeightKg, SalesAmount, CostAmount, UnitCost, UnitPrice)
    SELECT  @RunId,
            f.Code,
            f.Qty,
            f.Weight,
            f.NetSales,
            ISNULL(b.Cost, ISNULL(ic.TotalCost, 0) * f.Qty),
            CASE WHEN f.Qty <> 0
                 THEN ISNULL(b.Cost, ISNULL(ic.TotalCost,0) * f.Qty) / f.Qty END,
            CASE WHEN f.Qty <> 0 THEN f.NetSales / f.Qty END
    FROM    Forush f
    LEFT    JOIN Baha b ON b.Code = f.Code
    LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = f.Code AND ic.RunId = @RunId
    WHERE   f.Qty <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا: ', COUNT(*), N' کالا، ',
                   SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END), N' زيان‌ده'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(SalesAmount) AS totalSales,
                    SUM(CostAmount)  AS totalCost,
                    SUM(SalesAmount) - SUM(CostAmount) AS totalProfit
             FROM dbo.CC_ItemMargin WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S12_CalcMargin.MarginSummary)
    SELECT  COUNT(*)                                              AS Items,
            SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END)           AS LossItems,
            SUM(SalesAmount)                                      AS TotalSales,
            SUM(CostAmount)                                       AS TotalCost,
            SUM(SalesAmount) - SUM(CostAmount)                    AS TotalProfit
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S12b — اعمال هدف حاشیه سود

   وقتی زیان یک کالا صفر می‌شود، مبلغ آن از بهای تمام‌شده‌اش کم و
   به کالای متعادل‌کننده اضافه می‌شود، تا جمع کل دست‌نخورده بماند.

   تغییر روی IMBIBE_MANF فرمول انجام می‌گیرد، چون تنها جزئی است
   که مستقل از مواد قابل تنظیم است.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S12b_ApplyMarginTargets
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Adj') IS NOT NULL DROP TABLE #Adj;

    ---- مبلغ تعديل لازم براي هر کالاي هدف‌دار
    SELECT  m.Code,
            t.TargetKind,
            t.TargetPct,
            t.BalancingCode,
            m.SalesAmount,
            m.CostAmount,
            m.QtySold,
            CASE t.TargetKind
                 WHEN 1 THEN m.CostAmount - m.SalesAmount                    -- سود صفر
                 WHEN 2 THEN m.CostAmount - m.SalesAmount * (1 - t.TargetPct/100.0)
                 ELSE 0 END AS AdjustAmount
    INTO    #Adj
    FROM    dbo.CC_ItemMargin m
    JOIN    dbo.CC_MarginTarget t ON t.Code = m.Code AND t.IsActive = 1
    WHERE   m.RunId = @RunId
      AND   t.TargetKind IN (1, 2)
      AND   m.QtySold <> 0;

    DELETE #Adj WHERE ABS(AdjustAmount) < 1;

    ---- هشدار: کالاي متعادل‌کننده زيان‌ده مي‌شود
    IF OBJECT_ID('tempdb..#Warn') IS NOT NULL DROP TABLE #Warn;

    SELECT  a.Code                    AS SourceCode,
            a.BalancingCode,
            a.AdjustAmount,
            bm.Profit                 AS BalancerProfitBefore,
            bm.Profit - a.AdjustAmount AS BalancerProfitAfter
    INTO    #Warn
    FROM    #Adj a
    JOIN    dbo.CC_ItemMargin bm ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
    WHERE   a.BalancingCode IS NOT NULL
      AND   bm.Profit - a.AdjustAmount < 0
      AND   bm.Profit >= 0;

    ---- نگهبان: نرخ جذب منفي
    -- هشدار #Warn بالا فقط سودِ کالاي متعادل‌کننده را مي‌سنجد، نه نرخي که
    -- واقعاً نوشته مي‌شود. اگر مبلغ تعديل از جذب فعلي بزرگ‌تر باشد،
    -- IMBIBE_MANF منفي مي‌شود — نرخ جذب دستمزدِ منفي در بهاي تمام‌شده
    -- بي‌معناست و S11 همان را به کل درخت محصول منتشر مي‌کند. اين حالت
    -- روي داده واقعي ديده شد: کالايي که نرخ کاردکسش صفر بود (CHK-14) با
    -- هدف «سود صفر»، جذب متعادل‌کننده را به عدد منفي برد.
    IF OBJECT_ID('tempdb..#Neg') IS NOT NULL DROP TABLE #Neg;

    SELECT q.Code, q.Naghsh, q.NerkhBefore, q.NerkhAfter
    INTO   #Neg
    FROM (
        SELECT  CAST(hm.CODE AS BIGINT) AS Code,
                N'کالاي هدف' AS Naghsh,
                hm.IMBIBE_MANF AS NerkhBefore,
                hm.IMBIBE_MANF - (a.AdjustAmount / NULLIF(a.QtySold, 0)) AS NerkhAfter
        FROM    dbo.HEAD_MANF hm
        JOIN    #Adj a ON CAST(hm.CODE AS BIGINT) = a.Code
        WHERE   hm.GHEYMAT = @Month
        UNION ALL
        SELECT  CAST(hm.CODE AS BIGINT),
                N'متعادل‌کننده',
                hm.IMBIBE_MANF,
                hm.IMBIBE_MANF + (x.Amount / NULLIF(x.Qty, 0))
        FROM    dbo.HEAD_MANF hm
        JOIN   (SELECT a.BalancingCode AS Code,
                       SUM(a.AdjustAmount) AS Amount,
                       MAX(bm.QtySold) AS Qty
                FROM   #Adj a
                JOIN   dbo.CC_ItemMargin bm
                       ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
                WHERE  a.BalancingCode IS NOT NULL AND bm.QtySold <> 0
                GROUP BY a.BalancingCode) x ON CAST(hm.CODE AS BIGINT) = x.Code
        WHERE   hm.GHEYMAT = @Month
    ) q
    WHERE  q.NerkhAfter < 0;

    IF @WhatIf = 1
    BEGIN
        SELECT  a.Code               AS کد_کالا,
                s.NAME               AS نام_کالا,
                a.SalesAmount        AS فروش,
                a.CostAmount         AS بها,
                a.SalesAmount - a.CostAmount AS سود_فعلي,
                a.AdjustAmount       AS مبلغ_تعديل,
                a.BalancingCode      AS کالاي_متعادل_کننده,
                sb.NAME              AS نام_متعادل_کننده
        FROM    #Adj a
        LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = a.Code
        LEFT    JOIN dbo.STUF_DEF sb ON TRY_CAST(sb.CODE AS BIGINT) = a.BalancingCode
        ORDER BY ABS(a.AdjustAmount) DESC;

        SELECT  w.SourceCode              AS کالاي_مبدا,
                w.BalancingCode           AS متعادل_کننده,
                w.BalancerProfitBefore    AS سود_قبل,
                w.BalancerProfitAfter     AS سود_بعد,
                N'کالاي متعادل‌کننده زيان‌ده مي‌شود' AS هشدار
        FROM    #Warn w;

        SELECT  n.Code        AS کد_کالا,
                n.Naghsh      AS نقش,
                n.NerkhBefore AS نرخ_جذب_فعلي,
                n.NerkhAfter  AS نرخ_جذب_پس_از_اعمال,
                N'نرخ جذب منفي مي‌شود — اعمال نخواهد شد' AS خطا
        FROM    #Neg n;

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Neg)
    BEGIN
        SELECT  n.Code        AS کد_کالا,
                n.Naghsh      AS نقش,
                n.NerkhBefore AS نرخ_جذب_فعلي,
                n.NerkhAfter  AS نرخ_جذب_پس_از_اعمال
        FROM    #Neg n;

        RAISERROR(N'اعمال هدف حاشيه سود، نرخ جذب را منفي مي‌کند و بهاي تمام‌شده را خراب مي‌کند؛ کالاي متعادل‌کننده يا هدف را تغيير دهيد.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    ---- کاهش بهاي کالاي هدف: تعديل نرخ جذب دستمزد فرمول
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF - (a.AdjustAmount / NULLIF(a.QtySold, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN    #Adj a ON CAST(hm.CODE AS BIGINT) = a.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n1 INT = @@ROWCOUNT;

    ---- افزايش بهاي کالاي متعادل‌کننده به همان مبلغ
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF + (x.Amount / NULLIF(x.Qty, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'جذب اثر معکوس هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN   (SELECT a.BalancingCode AS Code,
                   SUM(a.AdjustAmount) AS Amount,
                   MAX(bm.QtySold) AS Qty
            FROM   #Adj a
            JOIN   dbo.CC_ItemMargin bm
                   ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
            WHERE  a.BalancingCode IS NOT NULL AND bm.QtySold <> 0
            GROUP BY a.BalancingCode) x ON CAST(hm.CODE AS BIGINT) = x.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n2 INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S12', 1,
            CONCAT(N'هدف حاشيه سود: ', @n1, N' کالاي هدف، ', @n2, N' متعادل‌کننده'));

    COMMIT;

    SELECT @n1 AS کالاي_هدف, @n2 AS متعادل_کننده;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S13 — داده گزارش هیئت‌مدیره

   شیت‌های موجود گزارش اکسل شما، به‌علاوه شیت جدید «خلاصه اجرا».
   خروجی چند مجموعه است که سمت سرور با ClosedXML به اکسل تبدیل می‌شود.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S13_ReportData
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Month TINYINT, @DT1 BIGINT, @DT2 BIGINT;
    SELECT @Month = PeriodMonth, @DT1 = DateFrom, @DT2 = DateTo
    FROM   dbo.CC_Run WHERE RunId = @RunId;

    ---- ۱) سود کالا به کالا
    SELECT  m.Code                        AS کد_کالا,
            s.NAME                        AS نام_کالا,
            @Month                        AS ماه,
            m.WeightKg                    AS وزن_به_کيلو,
            m.QtySold                     AS مقدار_کل,
            m.SalesAmount                 AS مبلغ_خالص,
            m.CostAmount                  AS مبلغ_ريالي,
            m.Profit                      AS سود,
            CASE WHEN m.SalesAmount <> 0
                 THEN ROUND(m.Profit / m.SalesAmount * 100, 0) END AS درصد
    FROM    dbo.CC_ItemMargin m
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = m.Code
    WHERE   m.RunId = @RunId
    ORDER BY m.Profit;

    ---- ۲) خلاصه اجرا — شيت جديدي که امروز وجود ندارد
    SELECT  r.RunId                       AS شماره_اجرا,
            r.FiscalYear                  AS سال,
            r.PeriodMonth                 AS ماه,
            r.RunNo                       AS نوبت,
            CASE r.RunKind WHEN 2 THEN N'قطعي' ELSE N'آزمايشي' END AS نوع,
            r.StartedByUser               AS کاربر,
            r.ApprovedByUser              AS تأييدکننده,
            (SELECT COUNT(*) FROM dbo.CC_FormulaChange WHERE RunId = @RunId)
                                          AS تعداد_تغيير_فرمول,
            (SELECT SUM(ISNULL(AmountVariance,0)) FROM dbo.CC_Variance WHERE RunId = @RunId)
                                          AS انحراف_مصرف,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND IsResolved = 0)
                                          AS استثناي_باز
    FROM    dbo.CC_Run r WHERE r.RunId = @RunId;

    ---- ۳) هزينه تبديل به تفکيک واحد
    SELECT  u.UnitName                    AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل' WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END AS نوع,
            c.AbsorbedAmount              AS جذب_شده,
            c.ActualAmount                AS واقعي,
            c.AdjustFactor                AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;

    ---- ۴) بيشترين تغيير نرخ — پاسخ به «چرا اين عدد عوض شد؟»
    SELECT  TOP 100
            f.FNUMB                       AS شماره_فرمول,
            sp.NAME                       AS کالاي_توليدي,
            sc.NAME                       AS ماده,
            f.FieldName                   AS فيلد,
            f.OldValue                    AS مقدار_قبل,
            f.NewValue                    AS مقدار_بعد,
            f.Reason                      AS علت
    FROM    dbo.CC_FormulaChange f
    LEFT    JOIN dbo.STUF_DEF sp ON TRY_CAST(sp.CODE AS BIGINT) = f.ParentCode
    LEFT    JOIN dbo.STUF_DEF sc ON TRY_CAST(sc.CODE AS BIGINT) = f.ChildCode
    WHERE   f.RunId = @RunId
    ORDER BY ABS(ISNULL(f.NewValue,0) - ISNULL(f.OldValue,0)) DESC;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S14 — تأیید نهایی و قفل دوره
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S14_Approve
    @RunId    INT,
    @UserName NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @kind TINYINT, @status TINYINT, @year SMALLINT, @month TINYINT;

    SELECT @kind = RunKind, @status = Status,
           @year = FiscalYear, @month = PeriodMonth
    FROM   dbo.CC_Run WHERE RunId = @RunId;

    IF @kind <> 2
    BEGIN
        RAISERROR(N'فقط اجراي قطعي قابل تأييد است.', 16, 1);
        RETURN;
    END

    IF @status <> 3
    BEGIN
        RAISERROR(N'اجرا هنوز تکميل نشده است.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Exception
               WHERE RunId = @RunId AND Severity = 2 AND IsResolved = 0)
    BEGIN
        RAISERROR(N'استثناي مسدودکننده باز وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @year AND PeriodMonth = @month
                 AND RunKind = 2 AND ApprovedAtUtc IS NOT NULL AND RunId <> @RunId)
    BEGIN
        RAISERROR(N'براي اين ماه قبلاً يک اجراي قطعي تأييد شده است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    UPDATE dbo.CC_Run
       SET ApprovedByUser = @UserName,
           ApprovedAtUtc  = SYSUTCDATETIME()
     WHERE RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S14', 1,
            CONCAT(N'تأييد نهايي دوره ', @year, '/', @month, N' توسط ', @UserName));

    COMMIT;

    SELECT N'دوره تأييد و قفل شد' AS وضعيت;
END
GO


PRINT N'رويه‌هاي S12 تا S14 ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, marginReportApprove,
                "CC_sp_S12_CalcMargin، CC_sp_S12b_ApplyMarginTargets، CC_sp_S13_ReportData، CC_sp_S14_Approve",
                "اسکریپت 18-margin-report-approve.sql را اجرا کنید (به CC_ItemMargin, CC_MarginTarget, CC_ConversionCost نیاز دارد).");

            // ⚠ حتماً بعد از marginReportApprove اجرا شود — نسخه CC_sp_S12_CalcMargin
            // را با محاسبه بر مبنای کاردکس (KALAS) جایگزین می‌کند.
            string marginFixKalas = @"
/* ═══════════════════════════════════════════════════════════════════
   اصلاح S12 — محاسبه سود بر مبنای کاردکس

   ── چه چیزی غلط بود ──
   نسخه قبلی بهای تمام‌شده را از سند حسابداری (DEED_DTL با TAG=13)
   می‌گرفت. آن عدد، بهای لحظه صدور سند است.

   ── روش درست ──
   AVRAGE در KALAS میانگین متحرک واقعی کاردکس است و MABRIAL همان
   AVRAGE × MEGHk. برای سنجش سود این درست است، چون بهای واقعی
   موجودی را نشان می‌دهد نه بهای لحظه‌ای.

   سود = مبلغ خالص (KHFR) − مبلغ ریالی (MABRIAL)

   ستون‌های KALAS که استفاده می‌شوند:
     TAGCODE = 2   فاکتور فروش
     KHFR          MABL_K − N_MOIN  (مبلغ خالص پس از تخفیف)
     MABRIAL       AVRAGE × MEGHk   (بهای تمام‌شده از کاردکس)
     MEGHk         مقدار کل
     MEGH          وزن به کیلو

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.

   ⚠ حتماً پس از 18-margin-report-approve.sql اجرا شود — نسخه S12
   آن فایل را جایگزین می‌کند.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S12 که در CC_ItemMargin (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد — دقیقاً همان خطایی که تست واقعی گرفت.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ستون‌های جدید برای تفکیک تخفیف و برگشت */
IF COL_LENGTH('dbo.CC_ItemMargin','GrossSales') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD GrossSales FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','Discount') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD Discount FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','ReturnAmount') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD ReturnAmount FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','ReturnQty') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD ReturnQty FLOAT NULL;
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_S12_CalcMargin
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMargin WHERE RunId = @RunId;

    /* ─── فروش: TAGCODE = 2 ─── */
    ;WITH Forush AS (
        SELECT  k.CODE                       AS Code,
                SUM(k.MEGHk)                 AS Qty,
                SUM(k.MEGH)                  AS Weight,
                SUM(k.MABL_K)                AS Gross,      -- پيش از تخفيف
                SUM(ISNULL(k.N_MOIN, 0))     AS Discount,   -- تخفيف
                SUM(k.KHFR)                  AS NetSales,   -- مبلغ خالص
                SUM(k.MABRIAL)               AS CostRial    -- AVRAGE × MEGHk
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 2
          AND   k.MM = @Month
        GROUP BY k.CODE
    ),
    /* ─── برگشت از فروش: TAGCODE = 4 ─── */
    Bargasht AS (
        SELECT  k.CODE           AS Code,
                SUM(k.MEGHk)     AS Qty,
                SUM(k.KHFR)      AS NetAmount,
                SUM(k.MABRIAL)   AS CostRial
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 4
          AND   k.MM = @Month
        GROUP BY k.CODE
    )
    INSERT dbo.CC_ItemMargin
        (RunId, Code, QtySold, WeightKg, SalesAmount, CostAmount,
         UnitCost, UnitPrice, GrossSales, Discount, ReturnAmount, ReturnQty)
    SELECT  @RunId,
            f.Code,
            f.Qty      - ISNULL(b.Qty, 0),
            f.Weight,
            f.NetSales - ISNULL(b.NetAmount, 0),      -- فروش خالص پس از برگشت
            f.CostRial - ISNULL(b.CostRial, 0),       -- بهاي واقعي از کاردکس
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.CostRial - ISNULL(b.CostRial,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.NetSales - ISNULL(b.NetAmount,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            f.Gross,
            f.Discount,
            ISNULL(b.NetAmount, 0),
            ISNULL(b.Qty, 0)
    FROM    Forush f
    LEFT    JOIN Bargasht b ON b.Code = f.Code
    WHERE   f.Qty <> 0;

    /* ─── هشدار: کالاي فروش‌رفته بدون نرخ کاردکس ───
       اگر MABRIAL صفر باشد يعني AVRAGE در کاردکس صفر است و
       سود آن کالا کاملاً غلط محاسبه مي‌شود. */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-14';

    IF NOT EXISTS (SELECT 1 FROM dbo.CC_CheckRule WHERE RuleCode = 'CHK-14')
        INSERT dbo.CC_CheckRule
            (RuleCode, RuleName, StepCode, ExType, DefaultSeverity,
             RemedyText, SortOrder)
        VALUES ('CHK-14', N'فروش بدون نرخ کاردکس', 'S12', 17, 1,
                N'اين کالا فروخته شده ولي ميانگين نرخ در کاردکس صفر است، پس بهاي تمام‌شده و سودش صفر محاسبه مي‌شود. کاردکس کالا را بررسي کنيد؛ معمولاً يعني رسيد بدون مبلغ ثبت شده.',
                140);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S12', 'CHK-14', 17, 1, m.Code, m.SalesAmount,
            N'کالا فروخته شده ولي نرخ کاردکسش صفر است — سود غيرواقعي'
    FROM    dbo.CC_ItemMargin m
    WHERE   m.RunId = @RunId
      AND   m.CostAmount = 0
      AND   m.SalesAmount <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا: ', COUNT(*), N' کالا، سود کل ',
                   FORMAT(SUM(SalesAmount) - SUM(CostAmount), 'N0'), N' ريال'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(SalesAmount) AS sales,
                    SUM(CostAmount)  AS cost
             FROM dbo.CC_ItemMargin WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S12_CalcMargin.MarginSummary)؛
    -- خلاصه‌ی خواناترِ فارسی (فروش ناخالص/تخفیف/برگشت) در CC_RunLog بالا ثبت شد.
    SELECT  COUNT(*)                                    AS Items,
            SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS LossItems,
            SUM(SalesAmount)                            AS TotalSales,
            SUM(CostAmount)                              AS TotalCost,
            SUM(SalesAmount) - SUM(CostAmount)          AS TotalProfit
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   مقایسه: روش کاردکس در برابر روش سند حسابداری

   برای اطمینان از درستی تغییر. اگر اختلاف بزرگ بود، یعنی سند
   حسابداری با کاردکس نمی‌خواند و خودِ آن یک یافته است.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_CompareMarginMethods
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH AzKardex AS (
        SELECT  k.CODE AS Code,
                SUM(k.KHFR)    AS NetSales,
                SUM(k.MABRIAL) AS Cost
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 2 AND k.MM = @Month
        GROUP BY k.CODE
    ),
    AzSanad AS (
        SELECT  TRY_CAST(d.HES_M AS BIGINT) AS Code,
                SUM(d.BED) - SUM(d.BES)     AS Cost
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.TAG = 13 AND h.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(d.HES_M AS BIGINT)
    )
    SELECT  TOP 50
            k.Code                            AS کد_کالا,
            s.NAME                            AS نام_کالا,
            ROUND(k.NetSales, 0)              AS فروش_خالص,
            ROUND(k.Cost, 0)                  AS بها_از_کاردکس,
            ROUND(ISNULL(sn.Cost, 0), 0)      AS بها_از_سند,
            ROUND(k.Cost - ISNULL(sn.Cost,0), 0) AS اختلاف,
            ROUND(k.NetSales - k.Cost, 0)     AS سود_روش_کاردکس,
            ROUND(k.NetSales - ISNULL(sn.Cost,0), 0) AS سود_روش_سند
    FROM    AzKardex k
    LEFT    JOIN AzSanad sn ON sn.Code = k.Code
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = k.Code
    WHERE   ABS(k.Cost - ISNULL(sn.Cost, 0)) > 1000
    ORDER BY ABS(k.Cost - ISNULL(sn.Cost, 0)) DESC;

    ;WITH AzKardex AS (
        SELECT SUM(k.MABRIAL) AS Cost FROM dbo.KALAS k
        WHERE k.TAGCODE = 2 AND k.MM = @Month
    ),
    AzSanad AS (
        SELECT SUM(d.BED) - SUM(d.BES) AS Cost
        FROM   dbo.DEED_DTL d JOIN dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE  d.TAG = 13 AND h.DATE_S BETWEEN @DT1 AND @DT2
    )
    SELECT  ROUND((SELECT Cost FROM AzKardex), 0) AS جمع_بها_کاردکس,
            ROUND((SELECT Cost FROM AzSanad),  0) AS جمع_بها_سند,
            ROUND((SELECT Cost FROM AzKardex) -
                  (SELECT Cost FROM AzSanad), 0)  AS اختلاف_کل;
END
GO


PRINT N'S12 با منطق کاردکس بازنويسي شد.';

/* نمونه:
   EXEC dbo.CC_sp_CompareMarginMethods @Month=4, @DT1=14050401, @DT2=14050431;
*/
GO
";
            TryExecuteCostCloseBatch(db, marginFixKalas,
                "CC_sp_S12_CalcMargin (نسخه کاردکس)، CC_sp_CompareMarginMethods",
                "اسکریپت 19-margin-fix-kalas.sql را اجرا کنید (به دیدگاه KALAS و ستون‌های KHFR/MABRIAL/TAGCODE/MM نیاز دارد).");
        }

        /// <summary>
        /// یک بلوک SQL از ماژول بستن ماه بهای تمام‌شده را اجرا می‌کند و
        /// اگر جدول‌های پایه CC_* هنوز روی این پایگاه نباشند (اسکریپت‌های
        /// 10 تا 13 اجرا نشده‌اند)، به‌جای متوقف کردن کل LetsGo فقط پیام
        /// راهنما می‌دهد.
        /// </summary>
        private static void TryExecuteCostCloseBatch(
            SqlConnection db, string script, string what, string hint)
        {
            try
            {
                ExecuteBatches(db, script);
                Console.WriteLine($"[CostCloseScript] {what} ایجاد/به‌روزرسانی شد.");
            }
            catch (SqlException ex) when (ex.Message.Contains("Invalid object name 'dbo.CC_"))
            {
                Console.WriteLine($"[CostCloseScript] جدول‌های پایه CC_* پیدا نشدند برای {what} — {hint}");
            }
        }

        private static void SalaryScript(bool isCustomCall, SqlConnection db)
        {
            if (isCustomCall) //
            {
                // ===========================================================
                // 1. DDL — جداول، ویوها، فانکشن‌ها، تریگرها
                //    ایجاد می‌شوند اگر وجود ندارند؛ آپدیت اگر موجودند
                // ===========================================================
                string tablescript = @"
-- ================================================================
-- PAY2 — سیستم حقوق و دستمزد — نسخه v6.0
-- نرم‌افزار مستر کارکت
-- کد: PAY2-DB-006  |  تاریخ: ۱۴۰۴/۱۲/۲۱
-- ================================================================
-- ترتیب ایجاد جداول بر اساس وابستگی‌های FK طراحی شده است.
-- اجرای کامل این اسکریپت ساختار کامل سیستم را می‌سازد.
-- ================================================================

SET NOCOUNT ON;
GO

-- ================================================================
-- گروه A — پیکربندی سیستم
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_CONFIG', N'U') IS NULL
BEGIN

-- ── ۱. PAY2_CONFIG — تنظیمات مرکزی ─────────────────────────────

CREATE TABLE [dbo].[PAY2_CONFIG]
(
    [CFG_KEY]      NVARCHAR(80)   NOT NULL,                                        -- کلید یکتا تنظیم
    [CFG_VALUE]    NVARCHAR(500)  NOT NULL,                                        -- مقدار جاری
    [CFG_OPTIONS]  NVARCHAR(500)  NULL,                                            -- گزینه‌های مجاز با | (مثال '30|REAL')
    [CFG_DEFAULT]  NVARCHAR(500)  NOT NULL,                                        -- مقدار پیش‌فرض کارخانه
    [CFG_SECTION]  NVARCHAR(60)   NOT NULL,                                        -- گروه در UI تنظیمات
    [LABEL_FA]     NVARCHAR(200)  NOT NULL,                                        -- عنوان فارسی
    [DESC_FA]      NVARCHAR(1000) NULL,                                            -- توضیح کامل
    [OPT_LABELS]   NVARCHAR(500)  NULL,                                            -- عنوان فارسی هر گزینه با |
    [DATA_TYPE]    NVARCHAR(20)   NOT NULL CONSTRAINT DF_CFG_DT DEFAULT('TEXT'),   -- TEXT|INT|DECIMAL|BOOL|DATE
    [ACCESS_LEVEL] TINYINT        NOT NULL CONSTRAINT DF_CFG_AL DEFAULT(2),        -- 1=Super Admin | 2=Admin | 3=Payroll Manager
    [CHANGED_AT]   DATETIME       NULL,
    [CHANGED_BY]   INT            NULL,
    [CHANGE_NOTE]  NVARCHAR(300)  NULL,

    CONSTRAINT PK_PAY2_CONFIG PRIMARY KEY ([CFG_KEY])
);
END;
GO

-- ── ۲. PAY2_CONFIG_LOG — لاگ تغییرات تنظیمات ───────────────────
IF OBJECT_ID(N'dbo.PAY2_CONFIG_LOG', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_CONFIG_LOG]
(
    [LOG_ID]     INT           NOT NULL IDENTITY(1,1),
    [CFG_KEY]    NVARCHAR(80)  NOT NULL,
    [OLD_VALUE]  NVARCHAR(500) NULL,
    [NEW_VALUE]  NVARCHAR(500) NOT NULL,
    [CHANGED_BY] INT           NOT NULL,
    [CHANGED_AT] DATETIME      NOT NULL CONSTRAINT DF_CFL_DT DEFAULT(GETDATE()),
    [REASON]     NVARCHAR(300) NULL,

    CONSTRAINT PK_PAY2_CONFIG_LOG PRIMARY KEY ([LOG_ID])
);
END;
GO

-- ── Trigger — لاگ خودکار تغییرات PAY2_CONFIG ───────────────────

CREATE OR ALTER TRIGGER [dbo].[TR_PAY2_CONFIG_LOG]
ON [dbo].[PAY2_CONFIG]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO PAY2_CONFIG_LOG
        (CFG_KEY, OLD_VALUE, NEW_VALUE, CHANGED_BY, CHANGED_AT, REASON)
    SELECT
        i.CFG_KEY,
        d.CFG_VALUE,
        i.CFG_VALUE,
        ISNULL(i.CHANGED_BY, 0),
        GETDATE(),
        i.CHANGE_NOTE
    FROM INSERTED i
    INNER JOIN DELETED d ON i.CFG_KEY = d.CFG_KEY
    WHERE i.CFG_VALUE <> d.CFG_VALUE;
END;
GO

-- ── ۳. PAY2_TAX_BRACKET — جدول مالیات پلکانی ───────────────────
IF OBJECT_ID(N'dbo.PAY2_TAX_BRACKET', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_TAX_BRACKET]
(
    [BRK_ID]     INT           NOT NULL IDENTITY(1,1),
    [TAX_YEAR]   SMALLINT      NOT NULL,                                       -- سال شمسی (مثال: 1403)
    [UPPER_LIMIT] BIGINT       NOT NULL,                                       -- سقف سالانه این پله (ریال) — NULL=پله آخر
    [RATE_PCT]   DECIMAL(5,2)  NOT NULL,                                       -- نرخ این پله (درصد)
    [FIXED_TAX]  BIGINT        NOT NULL CONSTRAINT DF_BRK_FT DEFAULT(0),      -- مالیات ثابت پله‌های قبل (برای سرعت محاسبه)
    [SORT_ORDER] SMALLINT      NOT NULL,

    CONSTRAINT PK_PAY2_TAX_BRACKET PRIMARY KEY ([BRK_ID]),
    CONSTRAINT UQ_BRK UNIQUE ([TAX_YEAR], [SORT_ORDER])
);
END;
GO

-- نمونه داده سال ۱۴۰۳
IF NOT EXISTS (SELECT 1 FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = 1403)
BEGIN
INSERT INTO PAY2_TAX_BRACKET (TAX_YEAR, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER) VALUES
(1403, 1800000000, 10,   0,         1),
(1403, 2700000000, 15,   180000000, 2),
(1403, 3600000000, 20,   315000000, 3),
(1403, 4800000000, 25,   495000000, 4),
(1403, 9999999999, 30,   795000000, 5);
END;
GO

-- ── بارگذاری اولیه PAY2_CONFIG ──────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM PAY2_CONFIG WHERE CFG_KEY = 'MONTH_DAYS_MODE')
BEGIN

INSERT INTO PAY2_CONFIG
    (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION,
     LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
VALUES
-- ─── محاسبه حقوق ───────────────────────────────────────────────
('MONTH_DAYS_MODE',      '30',            '30|REAL',                '30',            N'محاسبه',
 N'مبنای روزهای ماه',        N'30=ثابت ۳۰ روز | REAL=روز واقعی شمسی',
 N'۳۰ روز ثابت|روز واقعی ماه', 'TEXT', 2),

('MID_MONTH_PRORATE',    'CALENDAR',      'CALENDAR|EXACT',         'CALENDAR',      N'محاسبه',
 N'روش تغییر حکم وسط ماه',   N'CALENDAR=روز تقویمی | EXACT=نسبت دقیق',
 N'روز تقویمی|نسبت دقیق',    'TEXT', 2),

('OT_NORMAL_MULT',       '1.40',          NULL,                     '1.40',          N'محاسبه',
 N'ضریب اضافه‌کار عادی',      N'طبق ماده ۵۹ ق.ک ضریب ۱.۴۰ (۴۰٪ اضافه)',
 NULL, 'DECIMAL', 2),

('OT_HOLIDAY_MULT',      '1.40',          NULL,                     '1.40',          N'محاسبه',
 N'ضریب اضافه‌کار تعطیل',     N'طبق ماده ۶۲ ق.ک. برخی کارگاه‌ها بالاتر توافق می‌کنند',
 NULL, 'DECIMAL', 2),

('OT_HOUR_BASE',         '7.33',          NULL,                     '7.33',          N'محاسبه',
 N'ساعت کاری روزانه (مبنای نرخ ساعتی)', N'۷.۳۳ ساعت = ۴۴ ساعت هفتگی ÷ ۶',
 NULL, 'DECIMAL', 2),

('SHIFT_MODE',           'PCT',           'PCT|FIXED',              'PCT',           N'محاسبه',
 N'روش محاسبه حق شیفت',      N'PCT=درصدی از حقوق پایه | FIXED=مبلغ ثابت در حکم',
 N'درصدی|مبلغ ثابت',         'TEXT', 2),

('ROUND_MODE',           '1000',          '1|100|1000|10000',       '1000',          N'محاسبه',
 N'گرد کردن مبالغ (ریال)',    N'مبلغ خالص به نزدیک‌ترین مضرب این عدد گرد می‌شود',
 N'تومان|صدگان|هزارگان|ده‌هزارگان', 'INT', 2),

-- ─── بیمه ──────────────────────────────────────────────────────
('INS_WORKER_RATE',      '7.00',          NULL,                     '7.00',          N'بیمه',
 N'نرخ بیمه کارگر (درصد)',    NULL, NULL, 'DECIMAL', 1),

('INS_EMPLOYER_RATE',    '20.00',         NULL,                     '20.00',         N'بیمه',
 N'نرخ بیمه کارفرما — بدون بیمه بیکاری (درصد)', NULL, NULL, 'DECIMAL', 1),

('INS_UNEMP_RATE',       '3.00',          NULL,                     '3.00',          N'بیمه',
 N'نرخ بیمه بیکاری کارفرما (درصد) — برای غیرمدیران', NULL, NULL, 'DECIMAL', 1),

('INS_CEILING_APPLY',    '1',             '1|0',                    '1',             N'بیمه',
 N'اعمال سقف دستمزد مشمول بیمه', N'1=اعمال (قانونی) | 0=بدون سقف',
 N'اعمال سقف|بدون سقف',      'BOOL', 1),

('INS_CEILING_MONTHLY',  '126000000',     NULL,                     '126000000',     N'بیمه',
 N'سقف ماهیانه دستمزد مشمول بیمه (ریال)',
 N'هر سال با ابلاغ تأمین اجتماعی به‌روز شود', NULL, 'INT', 1),

('INS_EXEMPT_COUNT',     '5',             NULL,                     '5',             N'بیمه',
 N'تعداد کارگران معاف در تبصره ماده ۷',
 N'کارگاه‌هایی با ≤ این تعداد نفر از ۲۰٪ کارفرما معاف‌اند', NULL, 'INT', 1),

('INS_JANBAZ_RATE',      '0.18',          NULL,                     '0.18',          N'بیمه',
 N'نرخ بیمه کارفرما برای جانبازان',
 N'جانباز: ۱۸٪ (بدون ۳٪ بیکاری و بدون ۷٪ کارگر)', NULL, 'DECIMAL', 1),

('INS_TAB56_UNEMP',      '0',             '1|0',                    '0',             N'بیمه',
 N'آیا تبصره ۵۶ مشمول ۳٪ بیکاری هست؟',
 N'0=خیر (پیش‌فرض) | 1=بله',
 N'خیر|بله',                  'BOOL', 1),

-- ─── مالیات ────────────────────────────────────────────────────
('TAX_YEAR',             '1403',          NULL,                     '1403',          N'مالیات',
 N'سال مالیاتی جاری',         N'برای انتخاب ردیف‌های صحیح از PAY2_TAX_BRACKET',
 NULL, 'INT', 1),

('TAX_EXEMPT_MONTHLY',   '84000000',      NULL,                     '84000000',      N'مالیات',
 N'معافیت ماهیانه مالیاتی (ریال)', N'= معافیت سالانه ماده ۸۴ تقسیم بر ۱۲',
 NULL, 'INT', 1),

('TAX_DEDUCT_INS',       '1',             '1|0',                    '1',             N'مالیات',
 N'کسر سهم بیمه کارگر از مبنای مالیات', N'۱=بله (تبصره ۱ ماده ۸۶ ق.م.م)',
 N'بله — قانونی|خیر',         'BOOL', 2),

('TAX_DEPRIVATION_APPLY','1',             '1|0',                    '1',             N'مالیات',
 N'اعمال معافیت مناطق محروم', N'تا ۵۰٪ معافیت برای شاغلان مناطق محروم',
 N'اعمال|عدم اعمال',          'BOOL', 2),

-- ─── مساعده هوشمند ─────────────────────────────────────────────
-- منطق: SUM(BED-BES) از DEED_DTL
-- شرط: HES_K=ADV_HES_K AND HES_M=ADV_HES_M AND HES_T=PCODE
-- AND DEED_HED.N_S < N_S_سند_حقوق_جاری
-- AND ماه سند = ماه حقوق
-- ADV_HES_K و ADV_HES_M به PAY2_WORKSHOP_ACC منتقل شده‌اند (v6 — کارگاه‌محور)
('ADV_ENABLED',          '1',             '1|0',                    '1',             N'مساعده',
 N'آیا مساعده هوشمند فعال باشد؟',
 N'1=مانده حساب معین پرسنل از DEED_DTL محاسبه می‌شود | 0=بدون کسر مساعده',
 N'فعال (هوشمند)|غیرفعال',    'BOOL', 2),

('ADV_SCOPE',            'CURRENT_MONTH', 'CURRENT_MONTH|OPEN_BALANCE', 'CURRENT_MONTH', N'مساعده',
 N'محدوده محاسبه مساعده',
 N'CURRENT_MONTH=فقط اسناد همان ماه | OPEN_BALANCE=کل مانده باز تا سند حقوق',
 N'فقط ماه جاری|کل مانده باز','TEXT', 2),

('ADV_USE_HES_T_FILTER', '1',             '1|0',                    '1',             N'مساعده',
 N'آیا فیلتر HES_T (تفصیلی=کد پرسنل) اعمال شود؟',
 N'1=هر پرسنل فقط مانده حساب خودش (معمول) | 0=جمع کل معین بدون تفکیک تفصیلی',
 N'به تفکیک پرسنل|کل معین',   'BOOL', 2),

('ADV_MIN_POSITIVE',     '1',             '1|0',                    '1',             N'مساعده',
 N'مساعده فقط اگر مانده بدهکار (مثبت) باشد کسر شود',
 N'1=بله — اگر حساب بستانکار بود مساعده صفر در نظر گرفته می‌شود | 0=همیشه کسر',
 N'فقط بدهکار|همیشه',         'BOOL', 2),

-- ─── مرخصی ─────────────────────────────────────────────────────
('LEAVE_ANNUAL_DAYS',    '26',            '24|26|30',               '26',            N'مرخصی',
 N'روزهای مرخصی استحقاقی سالانه', N'ماده ۶۴ ق.ک',
 N'۲۴ روز|۲۶ روز|۳۰ روز',    'INT', 2),

('LEAVE_MINS_PER_DAY',   '440',           NULL,                     '440',           N'مرخصی',
 N'دقیقه معادل یک روز مرخصی',
 N'v6: ۴۴۰ دقیقه (طبق کارکرد سیستم قدیم) — مبنای LEAVE_BAL و تسویه مرخصی',
 NULL, 'INT', 2),

('LEAVE_CARRYOVER_MAX',  '9',             '0|9|999',                '9',             N'مرخصی',
 N'حداکثر روز انتقال مرخصی به سال بعد', N'ماده ۶۶ ق.ک — ۹ روز',
 N'ممنوع|۹ روز (قانون)|نامحدود', 'INT', 2),

('LEAVE_HOURLY_MAX_MINS', '200', NULL, '200', N'مرخصی', N'حداکثر زمان مرخصی ساعتی (دقیقه)', N'حداکثر دقایق مجاز برای ثبت در یک برگ مرخصی ساعتی (مثلاً ۲۰۰ دقیقه = ۳ ساعت و ۲۰ دقیقه)', NULL, 'INT', 2),
-- ─── تسویه حساب ────────────────────────────────────────────────
('BONUS_MODE',           'MIN_WAGE',      'MIN_WAGE|ACTUAL|CUSTOM',  'MIN_WAGE',     N'تسویه',
 N'مبنای محاسبه عیدی',
 N'MIN_WAGE=حداقل مزد ۶۰-۹۰ روز | ACTUAL=حقوق واقعی | CUSTOM=روز سفارشی',
 N'حداقل مزد|حقوق واقعی|سفارشی', 'TEXT', 2),

('BONUS_CUSTOM_DAYS',    '60',            NULL,                     '60',            N'تسویه',
 N'روز عیدی در حالت سفارشی',  NULL, NULL, 'INT', 2),

('MIN_WAGE_DAILY',       '73200',         NULL,                     '73200',         N'تسویه',
 N'حداقل دستمزد روزانه (ریال) طبق قانون کار',
 N'هر سال با ابلاغ شورای عالی کار به‌روز شود.',
 NULL, 'INT', 1),

('MIN_WAGE_MONTHLY',     '2196000',       NULL,                     '2196000',       N'تسویه',
 N'حداقل دستمزد ماهیانه (ریال) طبق قانون کار',
 N'= MIN_WAGE_DAILY × ۳۰. مبنای محاسبه عیدی در حالت MIN_WAGE.',
 NULL, 'INT', 1),

('EIDI_MIN_DAYS',        '60',            NULL,                     '60',            N'تسویه',
 N'حداقل روز برای محاسبه عیدی (قانون: ۶۰ روز)', NULL, NULL, 'INT', 1),

('EIDI_MAX_DAYS',        '90',            NULL,                     '90',            N'تسویه',
 N'حداکثر روز برای محاسبه عیدی (قانون: ۹۰ روز)', NULL, NULL, 'INT', 1),

('SENIORITY_MODE',       'LAST_SAL',      'LAST_SAL|DAILY|FIXED',   'LAST_SAL',      N'تسویه',
 N'مبنای حق سنوات',
 N'LAST_SAL=آخرین ماه×سال | DAILY=نرخ روزانه×۳۰×سال | FIXED=مبلغ ثابت per سال',
 N'آخرین حقوق|نرخ روزانه|مبلغ ثابت', 'TEXT', 2),

('SENIORITY_FIXED_AMT',  '0',             NULL,                     '0',             N'تسویه',
 N'مبلغ ثابت سنوات per سال (ریال) — فقط حالت FIXED', NULL, NULL, 'INT', 2),

-- ─── امنیت ─────────────────────────────────────────────────────
('ITEM_DEF_MIN_ROLE',    'ADMIN',         'SUPER|ADMIN|MGR',        'ADMIN',         N'امنیت',
 N'حداقل نقش برای تعریف آیتم حکم', NULL,
 N'فقط مدیر ارشد|ادمین|مدیر حقوق', 'TEXT', 1),

('CONFIG_MIN_ROLE',      'SUPER',         'SUPER|ADMIN',            'SUPER',         N'امنیت',
 N'حداقل نقش برای تغییر PAY2_CONFIG', NULL,
 N'فقط مدیر ارشد|ادمین',      'TEXT', 1);
END;
GO

-- ── افزودن کلید MONTHLY_ITEM_PRORATE برای دیتابیس‌های موجود (idempotent) ──
IF NOT EXISTS (SELECT 1 FROM PAY2_CONFIG WHERE CFG_KEY = 'MONTHLY_ITEM_PRORATE')
    INSERT INTO PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES ('MONTHLY_ITEM_PRORATE', '0', '1|0', '0', N'محاسبه',
            N'کسر آیتم‌های ماهیانه به‌نسبت غیبت',
            N'1=آیتم‌های ماهانه (حق تأهل/جذب/شرایط محیط کار/سایر ثابت) با غیبت کم می‌شوند | 0=کامل پرداخت می‌شوند',
            N'به‌نسبت کارکرد|کامل', 'BOOL', 2);
GO

-- ================================================================
-- گروه B — سازمان و کارگاه
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_WORKSHOP', N'U') IS NULL
BEGIN

-- ── ۴. PAY2_WORKSHOP — کارگاه‌ها ────────────────────────────────

CREATE TABLE [dbo].[PAY2_WORKSHOP]
(
    [WS_ID]           INT           NOT NULL IDENTITY(1,1),
    [WS_CODE]         NVARCHAR(20)  NOT NULL,                                    -- کد کارگاه (مرجع TAGCOD.CODE در صورت مهاجرت)
    [WS_NAME]         NVARCHAR(100) NOT NULL,
    [SHIFT_MODE]      NVARCHAR(10)  NULL,                                    -- نام کارگاه
    [EMPLOYER_NAME]   NVARCHAR(100) NULL,                                        -- نام کارفرما (فیلد جدید v6)
    [NATIONAL_ID]     NVARCHAR(11)  NULL,                                        -- شناسه ملی کارگاه
    [SOCIAL_INS_CODE] NVARCHAR(20)  NULL,                                        -- کد کارگاه نزد تأمین اجتماعی
    [TAX_CODE]        NVARCHAR(20)  NULL,                                        -- شناسه مالیاتی
    [POSTAL_CODE]     NVARCHAR(20)  NULL,                                        -- کد پستی کارگاه (فیلد جدید v6)
    [ADDRESS]         NVARCHAR(300) NULL,
    [PHONE]           NVARCHAR(30)  NULL,
    [INS_MODE]        TINYINT       NOT NULL CONSTRAINT DF_WS_INS DEFAULT(1),    -- 1=کارگاه معمولی (SANAD) | 2=تبصره ماده ۷ (SANAD10)
    [DEFAULT_DEED_MODE] TINYINT     NOT NULL CONSTRAINT DF_WS_DEED_MODE DEFAULT(1), -- روش پیش‌فرض صدور سند (1=خلاصه، 2=تفکیکی)
    [IS_ACTIVE]       BIT           NOT NULL CONSTRAINT DF_WS_ACT DEFAULT(1),
    [CREATED_AT]      DATETIME      NOT NULL CONSTRAINT DF_WS_CRT DEFAULT(GETDATE()),
    [CREATED_BY]      INT           NULL,

    CONSTRAINT PK_PAY2_WORKSHOP PRIMARY KEY ([WS_ID]),
    CONSTRAINT UQ_WS_CODE UNIQUE ([WS_CODE])
);
END;
GO

-- ── ۵. PAY2_WORKSHOP_ACC — سرفصل‌های حسابداری هر کارگاه ─────────
IF OBJECT_ID(N'dbo.PAY2_WORKSHOP_ACC', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_WORKSHOP_ACC]
(
    [WS_ID]    INT           NOT NULL,
    [ACC_KEY]  NVARCHAR(50)  NOT NULL,   -- SALARY_EXP | INS_EXP | SALARY_PAYABLE | INS_PAYABLE | TAX_PAYABLE | ADV_HES_K | ADV_HES_M
    [ACC_CODE] NVARCHAR(20)  NOT NULL,   -- کد سرفصل در سیستم حسابداری
    [ACC_DESC] NVARCHAR(100) NULL,

    CONSTRAINT PK_PAY2_WS_ACC PRIMARY KEY ([WS_ID], [ACC_KEY]),
    CONSTRAINT FK_WS_ACC FOREIGN KEY ([WS_ID]) REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ================================================================
-- گروه C — پرسنل
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_JOB', N'U') IS NULL
BEGIN

-- ── ۶. PAY2_JOB — جدول مشاغل ────────────────────────────────────

CREATE TABLE [dbo].[PAY2_JOB]
(
    [JOB_ID]    INT           NOT NULL IDENTITY(1,1),
    [JOB_CODE]  NVARCHAR(20)  NOT NULL,                                      -- کد شغل
    [JOB_NAME]  NVARCHAR(100) NOT NULL,                                      -- عنوان شغل (فارسی)
    [JOB_GROUP] NVARCHAR(50)  NULL,                                          -- گروه شغلی
    [IS_ACTIVE] BIT           NOT NULL CONSTRAINT DF_JOB_ACT DEFAULT(1),

    CONSTRAINT PK_PAY2_JOB PRIMARY KEY ([JOB_ID]),
    CONSTRAINT UQ_JOB_CODE UNIQUE ([JOB_CODE])
);
END;
GO

-- ── ۷. PAY2_EMPLOYEE — مشخصات پرسنل ────────────────────────────
IF OBJECT_ID(N'dbo.PAY2_EMPLOYEE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_EMPLOYEE]
(
    [EMP_ID]             INT           NOT NULL IDENTITY(1,1),
    [EMP_CODE]           NVARCHAR(20)  NOT NULL,                             -- کد یکتا (می‌تواند = CODE در PERSONEL قدیم)
    [WS_ID]              INT           NOT NULL,                             -- کارگاه اصلی

    -- مشخصات فردی
    [FIRST_NAME]         NVARCHAR(50)  NOT NULL,
    [LAST_NAME]          NVARCHAR(50)  NOT NULL,
    [FATHER_NAME]        NVARCHAR(50)  NULL,
    [NATIONAL_CODE]      NVARCHAR(10)  NULL,                                 -- v6: NULL مجاز (خارجی/موقت — filtered unique)
    [ID_NUMBER]          NVARCHAR(20)  NULL,                                 -- شماره شناسنامه
    [BIRTH_PLACE]        NVARCHAR(50)  NULL,                                 -- محل صدور شناسنامه
    [BIRTH_DATE]         BIGINT        NULL,                                 -- تاریخ تولد شمسی (YYYYMMDD)
    [GENDER]             TINYINT       NOT NULL CONSTRAINT DF_EMP_GND DEFAULT(1),  -- 1=مذکر، 2=مونث
    [NATIONALITY]        TINYINT       NOT NULL CONSTRAINT DF_EMP_NAT DEFAULT(1),  -- 1=ایرانی، 2=خارجی
    [IS_JANBAZ]          BIT           NOT NULL CONSTRAINT DF_EMP_JAN DEFAULT(0),  -- جانباز

    -- اشتغال
    [HIRE_DATE]          BIGINT        NOT NULL,                             -- تاریخ شروع به کار شمسی
    [FIRE_DATE]          BIGINT        NULL,                                 -- تاریخ ترک کار
    [JOB_ID]             INT           NULL,                                 -- شغل — FK به PAY2_JOB
    [UNIT]               TINYINT       NULL,                                 -- 1=تولید، 2=فروش، 3=خدمات، 4=اداری
    [EDU_LEVEL]          TINYINT       NULL,                                 -- مدرک تحصیلی
    [MARITAL]            TINYINT       NOT NULL CONSTRAINT DF_EMP_MAR DEFAULT(2),  -- 1=متأهل، 2=مجرد
    [IS_MANAGER]         BIT           NOT NULL CONSTRAINT DF_EMP_MGR DEFAULT(0),  -- مدیر — غیرمشمول ۳٪ بیمه بیکاری

    -- بیمه
    [INS_CODE]           NVARCHAR(15)  NULL,                                 -- شماره بیمه تأمین اجتماعی
    [INS_TYPE]           TINYINT       NOT NULL CONSTRAINT DF_EMP_INS DEFAULT(1),  -- 1=معمولی، 2=تبصره‌ای، 3=معاف از بیمه

    -- مالیات
    [TAX_EXEMPT]         BIT           NOT NULL CONSTRAINT DF_EMP_TEX DEFAULT(0),
    [REGION_DEPRIVATION] TINYINT       NOT NULL CONSTRAINT DF_EMP_DEP DEFAULT(0), -- ۰=عادی، یا درصد معافیت منطقه محروم (مثال ۵۰)

    -- ارتباط با حسابداری
    [ACC_T] NVARCHAR(50) NULL,                                 -- کد تفصیلی پرسنل در DEED_DTL.HES_T — مبنای مساعده هوشمند

    -- اطلاعات تماس و پرداخت
    [CARD_NO]            NVARCHAR(20)  NULL,                                 -- شماره کارت ساعت
    [MOBILE]             NVARCHAR(15)  NULL,
    [BANK_ACC]           NVARCHAR(30)  NULL,                                 -- شماره حساب بانکی
    [IBAN]               NVARCHAR(26)  NULL,                                 -- شماره شبا برای پرداخت الکترونیک

    -- وضعیت
    [IS_ACTIVE]          BIT           NOT NULL CONSTRAINT DF_EMP_ACT DEFAULT(1),
    [NOTES]              NVARCHAR(300) NULL,
    [CREATED_AT]         DATETIME      NOT NULL CONSTRAINT DF_EMP_CRT DEFAULT(GETDATE()),
    [CREATED_BY]         INT           NULL,

    CONSTRAINT PK_PAY2_EMPLOYEE  PRIMARY KEY ([EMP_ID]),
    CONSTRAINT UQ_EMP_CODE       UNIQUE ([EMP_CODE]),
    CONSTRAINT FK_EMP_WS         FOREIGN KEY ([WS_ID])  REFERENCES [PAY2_WORKSHOP]([WS_ID]),
    CONSTRAINT FK_EMP_JOB        FOREIGN KEY ([JOB_ID]) REFERENCES [PAY2_JOB]([JOB_ID])
);
END;
GO

-- v6: Filtered Unique Index برای کد ملی (NULL مجاز — برای خارجی‌ها)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_EMP_NATCODE' AND object_id = OBJECT_ID(N'dbo.PAY2_EMPLOYEE'))
CREATE UNIQUE INDEX UX_EMP_NATCODE
    ON PAY2_EMPLOYEE([NATIONAL_CODE])
    WHERE [NATIONAL_CODE] IS NOT NULL AND [NATIONAL_CODE] <> N'';
GO

-- ── ۸. PAY2_CONTRACT — قراردادها ────────────────────────────────
IF OBJECT_ID(N'dbo.PAY2_CONTRACT', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_CONTRACT]
(
    [CON_ID]       INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]       INT           NOT NULL,
    [CON_TYPE]     TINYINT       NOT NULL,                                   -- 1=دائم، 2=موقت، 3=پیمانی، 4=ساعتی
    [START_DATE]   BIGINT        NOT NULL,                                   -- شمسی
    [END_DATE]     BIGINT        NULL,                                       -- NULL=نامحدود
    [TRIAL_END]    BIGINT        NULL,                                       -- پایان دوره آزمایشی
    [WEEKLY_HOURS] DECIMAL(5,2)  NOT NULL CONSTRAINT DF_CON_WH DEFAULT(44),
    [NOTES]        NVARCHAR(200) NULL,
    [CREATED_AT]   DATETIME      NOT NULL CONSTRAINT DF_CON_CRT DEFAULT(GETDATE()),

    CONSTRAINT PK_PAY2_CONTRACT PRIMARY KEY ([CON_ID]),
    CONSTRAINT FK_CON_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ── ۹. PAY2_LEAVE_BAL — مانده مرخصی به دقیقه ───────────────────
IF OBJECT_ID(N'dbo.PAY2_LEAVE_BAL', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_LEAVE_BAL]
(
    [EMP_ID]           INT       NOT NULL,
    [YEAR]             SMALLINT  NOT NULL,                                   -- سال شمسی
    [ENTITLEMENT_MIN]  INT       NOT NULL CONSTRAINT DF_LB_ENT DEFAULT(11440), -- استحقاق سالانه (دقیقه) — ۲۶روز × ۴۴۰دق
    [USED_MIN]         INT       NOT NULL CONSTRAINT DF_LB_USD DEFAULT(0),    -- مجموع مرخصی‌های استفاده‌شده (دقیقه)
    [CARRIED_IN_MIN]   INT       NOT NULL CONSTRAINT DF_LB_CIN DEFAULT(0),    -- انتقالی از سال قبل — MAX: 9روز=3960دق
    [CARRIED_OUT_MIN]  INT       NOT NULL CONSTRAINT DF_LB_COU DEFAULT(0),    -- منتقل‌شده به سال بعد (دقیقه)

    -- ستون‌های محاسبه‌شده (نمایشی)
    [BALANCE_MIN]  AS ([ENTITLEMENT_MIN] + [CARRIED_IN_MIN] - [USED_MIN]),                        -- مانده کل به دقیقه
    [BALANCE_DAYS] AS (([ENTITLEMENT_MIN] + [CARRIED_IN_MIN] - [USED_MIN]) / 440),                -- مانده به روز (تقریبی)

    [UPDATED_AT] DATETIME NULL,

    CONSTRAINT PK_PAY2_LEAVE_BAL PRIMARY KEY ([EMP_ID], [YEAR]),
    CONSTRAINT FK_LB_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ================================================================
-- گروه D — تعریف آیتم‌های حقوق
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_ITEM_DEF', N'U') IS NULL
BEGIN

-- ── ۱۰. PAY2_ITEM_DEF — آیتم‌های پویا ──────────────────────────

CREATE TABLE [dbo].[PAY2_ITEM_DEF]
(
    [ITEM_ID]       INT           NOT NULL IDENTITY(1,1),
    [ITEM_CODE]     NVARCHAR(30)  NOT NULL,                                  -- کد یکتا: 'BASE_SAL','HOME','CHILDREN'...
    [ITEM_NAME]     NVARCHAR(100) NOT NULL,                                  -- نام فارسی برای فیش
    [ITEM_TYPE]     TINYINT       NOT NULL,
    -- 1=پرداختی ثابت، 2=پرداختی متغیر/کارکردی، 3=کسر ثابت، 4=کسر متغیر، 5=آگاهی(نمایش)
    [CALC_BASIS]    TINYINT       NOT NULL CONSTRAINT DF_ID_CB  DEFAULT(2),  -- 1=روزانه، 2=ماهیانه
    [INS_SUBJECT]   BIT           NOT NULL CONSTRAINT DF_ID_INS DEFAULT(1),  -- مشمول بیمه
    [TAX_SUBJECT]   BIT           NOT NULL CONSTRAINT DF_ID_TAX DEFAULT(1),  -- مشمول مالیات
    [INS_BASE_DAYS] TINYINT       NOT NULL CONSTRAINT DF_ID_IBD DEFAULT(1),  -- 1=DAYS (کارکرد اسمی) | 2=DAYSB (کارکرد رسمی)
    [PAY_BASE_DAYS] TINYINT       NOT NULL CONSTRAINT DF_ID_PBD DEFAULT(2),  -- 1=DAYS | 2=DAYSB — پرداخت همیشه رسمی
    [IS_SYSTEM]     BIT           NOT NULL CONSTRAINT DF_ID_SYS DEFAULT(0),  -- سیستمی؟ (حذف ممنوع)
    [SHOW_IN_SLIP]  BIT           NOT NULL CONSTRAINT DF_ID_SLP DEFAULT(1),
    -- شمارهٔ تفصیلیِ حسابِ هزینه در سند تفصیلی کامل (DEED_MODE=3). به ریشهٔ
    -- حسابِ مرکز هزینه چسبانده می‌شود: «711-1» + «2» → «711-1-2» (اضافه‌کار تولید).
    -- NULL یعنی این قلم هزینه‌ای تولید نمی‌کند (کسورات). پیش‌فرضِ اقلام جدید ۹=سایر است.
    [EXP_TAFSILI]   SMALLINT      NULL,
    [SORT_ORDER]    SMALLINT      NOT NULL CONSTRAINT DF_ID_SRT DEFAULT(100),
    [IS_ACTIVE]     BIT           NOT NULL CONSTRAINT DF_ID_ACT DEFAULT(1),
    [NOTES]         NVARCHAR(200) NULL,
    [CREATED_AT]    DATETIME      NOT NULL CONSTRAINT DF_ID_CRT DEFAULT(GETDATE()),
    [CREATED_BY]    INT           NULL,

    CONSTRAINT PK_PAY2_ITEM_DEF  PRIMARY KEY ([ITEM_ID]),
    CONSTRAINT UQ_ITEM_CODE      UNIQUE ([ITEM_CODE]),
    CONSTRAINT CK_ITEM_TYPE      CHECK ([ITEM_TYPE]   BETWEEN 1 AND 5),
    CONSTRAINT CK_CALC_BASIS     CHECK ([CALC_BASIS]  IN (1,2)),
    CONSTRAINT CK_INS_BASE_DAYS  CHECK ([INS_BASE_DAYS] IN (1,2)),
    CONSTRAINT CK_PAY_BASE_DAYS  CHECK ([PAY_BASE_DAYS] IN (1,2))
);
END;
GO

-- آیتم‌های سیستمی پیش‌فرض (IS_SYSTEM=1)
IF NOT EXISTS (SELECT 1 FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'BASE_SAL')
BEGIN
INSERT INTO PAY2_ITEM_DEF
    (ITEM_CODE, ITEM_NAME, ITEM_TYPE, CALC_BASIS, INS_SUBJECT, TAX_SUBJECT, INS_BASE_DAYS, PAY_BASE_DAYS, IS_SYSTEM, SORT_ORDER)
VALUES
-- عمداً EXP_TAFSILI اینجا نیست: این batch روی دیتابیسی هم اجرا می‌شود که جدول
-- را از قبل دارد ولی ستون را نه (ستون پایین‌تر با ALTER اضافه می‌شود). چون
-- SQL Server کل batch را پیش از اجرا کامپایل می‌کند، صرفِ نامِ آن ستون کافی
-- بود تا کل اسکریپت با «Invalid column name» رد شود — حتی با وجود IF NOT
-- EXISTS که جلوی اجرای INSERT را می‌گرفت. مقداردهی‌اش پایین‌تر و داخل EXEC است.
('BASE_SAL_B',  N'حقوق روزانه رسمی',        1, 1, 1, 1, 1, 2, 1, 1),   -- SALARY_DAYLYB
('BASE_SAL',    N'حقوق روزانه اسمی',         1, 1, 1, 1, 1, 2, 1, 2),   -- SALARY_DAYLY
('HOME',        N'خواربار و مسکن',           1, 1, 1, 1, 1, 2, 1, 3),   -- قانون ۲۸ روز
('CHILDREN',    N'حق اولاد',                 1, 1, 0, 1, 1, 2, 1, 4),   -- معاف بیمه، مشمول مالیات
('FAMILY_ALLOW',N'حق تأهل',                  1, 2, 1, 1, 1, 2, 1, 5),   -- ماهیانه
('ATTRACT',     N'حق جذب',                   1, 2, 1, 1, 1, 2, 1, 6),   -- ماهیانه
('GROCERY',     N'بن کارگری',                1, 1, 1, 0, 1, 2, 1, 7),   -- مشمول بیمه، معاف مالیات
('HARD_COND',   N'شرایط محیط کار',           1, 2, 1, 1, 1, 2, 1, 8),
('NAHAR',       N'حق نهار',                  1, 2, 0, 0, 2, 2, 1, 9),   -- معاف بیمه/مالیات
('SHIFT',       N'حق شیفت/نوبت/شب‌کاری',    1, 1, 0, 1, 1, 2, 1, 10),  -- درصد از BASE_SAL_B
('OTHER_FIX',   N'سایر ثابت',               1, 2, 1, 1, 1, 2, 1, 11),
('OT_NORMAL',   N'اضافه‌کار عادی',           2, 1, 1, 1, 1, 2, 1, 12),
('OT_HOLIDAY',  N'اضافه‌کار تعطیل',          2, 1, 1, 1, 1, 2, 1, 13),
('OT_ADMIN',    N'اضافه‌کار اداری',           2, 1, 1, 1, 1, 2, 1, 14),
('PERF_BONUS',  N'پاداش/راندمان',            2, 2, 1, 1, 1, 2, 1, 15),
('TRANSP',      N'حق ناقل/ایاب‌ذهاب',        2, 2, 0, 0, 1, 2, 1, 16),  -- معاف بیمه/مالیات
('INS_DED',     N'کسر بیمه کارگر',           4, 1, 0, 0, 1, 2, 1, 17),  -- خودکار
('TAX_DED',     N'کسر مالیات',              4, 1, 0, 0, 1, 2, 1, 18),  -- خودکار
('LOAN_DED',    N'قسط وام',                  3, 2, 0, 0, 1, 2, 1, 19),  -- از PAY2_LOAN_SCHED
('ADVANCE_DED', N'مساعده',                   4, 2, 0, 0, 1, 2, 1, 20),  -- هوشمند از DEED_DTL
('OTHER_DED',   N'سایر کسورات',             3, 2, 0, 0, 1, 2, 1, 21);
END;
GO

-- ── ۱۱. PAY2_ITEM_TEMPLATE — قالب‌های حکم ───────────────────────
IF OBJECT_ID(N'dbo.PAY2_ITEM_TEMPLATE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ITEM_TEMPLATE]
(
    [TMPL_ID]   INT           NOT NULL IDENTITY(1,1),
    [TMPL_CODE] NVARCHAR(30)  NOT NULL,
    [TMPL_NAME] NVARCHAR(100) NOT NULL,                                      -- مثال: 'کارگر تولید پایه'
    [WS_ID]     INT           NULL,                                          -- NULL=برای همه کارگاه‌ها
    [IS_ACTIVE] BIT           NOT NULL CONSTRAINT DF_TMPL_ACT DEFAULT(1),
    [NOTES]     NVARCHAR(200) NULL,

    CONSTRAINT PK_PAY2_TMPL    PRIMARY KEY ([TMPL_ID]),
    CONSTRAINT UQ_TMPL_CODE    UNIQUE ([TMPL_CODE]),
    CONSTRAINT FK_TMPL_WS      FOREIGN KEY ([WS_ID]) REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ── ۱۲. PAY2_ITEM_TMPL_LINE — آیتم‌های هر قالب ─────────────────
IF OBJECT_ID(N'dbo.PAY2_ITEM_TMPL_LINE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ITEM_TMPL_LINE]
(
    [TMPL_ID]   INT      NOT NULL,
    [ITEM_ID]   INT      NOT NULL,
    [DEF_AMOUNT] DECIMAL(18,2) NOT NULL CONSTRAINT DF_TL_AMT DEFAULT(0),
    [INS_OV]    BIT      NULL,                                               -- NULL=از تعریف آیتم
    [TAX_OV]    BIT      NULL,
    [BASIS_OV]  TINYINT  NULL,
    [SHIFT_MODE_OV] NVARCHAR(10) NULL,

    CONSTRAINT PK_PAY2_TMPL_LINE PRIMARY KEY ([TMPL_ID], [ITEM_ID]),
    CONSTRAINT FK_TL_TMPL FOREIGN KEY ([TMPL_ID]) REFERENCES [PAY2_ITEM_TEMPLATE]([TMPL_ID]) ON DELETE CASCADE,
    CONSTRAINT FK_TL_ITEM FOREIGN KEY ([ITEM_ID])  REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ================================================================
-- گروه E — احکام کارگزینی
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_DECREE', N'U') IS NULL
BEGIN

-- ── ۱۳. PAY2_DECREE — هدر احکام ────────────────────────────────

CREATE TABLE [dbo].[PAY2_DECREE]
(
    [DEC_ID]       INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]       INT           NOT NULL,
    [WS_ID]        INT           NOT NULL,
    [ISSUED_DATE]  BIGINT        NOT NULL,
    [SHIFT_MODE]   NVARCHAR(10)  NULL,                                   -- تاریخ صدور شمسی
    [EFF_FROM]     BIGINT        NOT NULL,                                   -- تاریخ شروع اجرا (شمسی)
    [EFF_TO]       BIGINT        NULL,                                       -- پایان اجرا (NULL=تا حکم بعدی)
    [EDU_LEVEL]    TINYINT       NULL,
    [MARITAL]      TINYINT       NULL,                                       -- تأهل در زمان این حکم
    [IS_MANAGER]   BIT           NULL,                                       -- مدیر در این حکم
    [TMPL_ID]      INT           NULL,                                       -- قالب استفاده‌شده
    [IS_CONFIRMED] BIT           NOT NULL CONSTRAINT DF_DEC_CON DEFAULT(0), -- تأیید نهایی؟
    [CONFIRMED_BY] INT           NULL,
    [CONFIRMED_AT] DATETIME      NULL,
    [NOTES]        NVARCHAR(300) NULL,
    [CREATED_AT]   DATETIME      NOT NULL CONSTRAINT DF_DEC_CRT DEFAULT(GETDATE()),
    [CREATED_BY]   INT           NULL,

    CONSTRAINT PK_PAY2_DECREE   PRIMARY KEY ([DEC_ID]),
    CONSTRAINT FK_DEC_EMP        FOREIGN KEY ([EMP_ID])  REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_DEC_WS         FOREIGN KEY ([WS_ID])   REFERENCES [PAY2_WORKSHOP]([WS_ID]),
    CONSTRAINT FK_DEC_TMPL       FOREIGN KEY ([TMPL_ID]) REFERENCES [PAY2_ITEM_TEMPLATE]([TMPL_ID])
);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_DEC_EMP_DATE' AND object_id = OBJECT_ID(N'dbo.PAY2_DECREE'))
CREATE NONCLUSTERED INDEX IX_DEC_EMP_DATE
    ON PAY2_DECREE ([EMP_ID], [EFF_FROM], [EFF_TO]);
GO

-- ── ۱۴. PAY2_DECREE_LINE — آیتم‌های هر حکم ─────────────────────
IF OBJECT_ID(N'dbo.PAY2_DECREE_LINE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_DECREE_LINE]
(
    [DEC_ID]   INT      NOT NULL,
    [ITEM_ID]  INT      NOT NULL,
    [AMOUNT]   DECIMAL(18,2) NOT NULL CONSTRAINT DF_DL_AMT DEFAULT(0),
    [NOMINAL_AMOUNT_OV] DECIMAL(18,2) NULL, -- فقط برای اقلام دو ریلی مانند SANOVAT_PAYE
    [OFFICIAL_AMOUNT_OV] DECIMAL(18,2) NULL,
    [INS_OV]   BIT      NULL,                                                -- NULL=از PAY2_ITEM_DEF
    [TAX_OV]   BIT      NULL,
    [BASIS_OV] TINYINT  NULL,
    [SHIFT_MODE_OV] NVARCHAR(10) NULL,

    CONSTRAINT PK_PAY2_DECREE_LINE PRIMARY KEY ([DEC_ID], [ITEM_ID]),
    CONSTRAINT FK_DL_DEC  FOREIGN KEY ([DEC_ID])  REFERENCES [PAY2_DECREE]([DEC_ID]) ON DELETE CASCADE,
    CONSTRAINT FK_DL_ITEM FOREIGN KEY ([ITEM_ID]) REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ── ۱۵. PAY2_OVERRIDE — استثناهای مشمولیت per پرسنل per آیتم ──
IF OBJECT_ID(N'dbo.PAY2_OVERRIDE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_OVERRIDE]
(
    [EMP_ID]     INT           NOT NULL,
    [ITEM_ID]    INT           NOT NULL,
    [INS_OV]     BIT           NULL,
    [TAX_OV]     BIT           NULL,
    [BASIS_OV]   TINYINT       NULL,
    [VALID_FROM] BIGINT        NOT NULL,
    [VALID_TO]   BIGINT        NULL,
    [REASON]     NVARCHAR(200) NULL,
    [CREATED_AT] DATETIME      NOT NULL CONSTRAINT DF_OV_CRT DEFAULT(GETDATE()),
    [CREATED_BY] INT           NULL,

    CONSTRAINT PK_PAY2_OVERRIDE PRIMARY KEY ([EMP_ID], [ITEM_ID], [VALID_FROM]),
    CONSTRAINT FK_OV_EMP  FOREIGN KEY ([EMP_ID])  REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_OV_ITEM FOREIGN KEY ([ITEM_ID]) REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ================================================================
-- گروه F — کارکرد ماهیانه
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_PERIOD', N'U') IS NULL
BEGIN

-- ── ۱۶. PAY2_PERIOD — دوره ماهیانه ─────────────────────────────

CREATE TABLE [dbo].[PAY2_PERIOD]
(
    [PER_ID]       INT           NOT NULL IDENTITY(1,1),
    [WS_ID]        INT           NOT NULL,
    [PERIOD_DATE]  BIGINT        NOT NULL,                                   -- تاریخ ماه شمسی (YYYYMM00)
    [HOLIDAY_DAYS] TINYINT       NOT NULL CONSTRAINT DF_PER_HD DEFAULT(0),  -- تعداد روزهای تعطیل رسمی این ماه
    [TENDAR_APPLY] BIT           NOT NULL CONSTRAINT DF_PER_TEN DEFAULT(0), -- ده‌درصدی: کسر ۱۰٪ این ماه؟
    [DEED_N_S_PAY] FLOAT         NULL,                                       -- شماره سند پرداخت صادرشده (از DEED_HED)
    [STATUS]       TINYINT       NOT NULL CONSTRAINT DF_PER_ST DEFAULT(1),  -- 1=باز، 2=بسته، 3=محاسبه‌شده، 4=سند صادر شده
    [OPENED_AT]    DATETIME      NOT NULL CONSTRAINT DF_PER_OA DEFAULT(GETDATE()),
    [CLOSED_AT]    DATETIME      NULL,
    [NOTES]        NVARCHAR(200) NULL,

    CONSTRAINT PK_PAY2_PERIOD PRIMARY KEY ([PER_ID]),
    CONSTRAINT UQ_PERIOD       UNIQUE ([WS_ID], [PERIOD_DATE]),
    CONSTRAINT FK_PER_WS       FOREIGN KEY ([WS_ID]) REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ── ۱۷. PAY2_ATTENDANCE — کارکرد هر پرسنل در هر دوره ──────────
IF OBJECT_ID(N'dbo.PAY2_ATTENDANCE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ATTENDANCE]
(
    [PER_ID]         INT           NOT NULL,
    [EMP_ID]         INT           NOT NULL,

    -- روزهای کارکرد با تفکیک واحد
    [WORK_DAYS]      DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_WD  DEFAULT(0),   -- کل روزهای کارکرد
    [DAYS_TOLID]     DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DTL DEFAULT(0),   -- روز تولید
    [DAYS_EDARI]     DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DED DEFAULT(0),   -- روز اداری
    [DAYS_KHADAMAT]  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DKH DEFAULT(0),   -- روز خدمات
    [DAYS_FOROSH]    DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DFR DEFAULT(0),   -- روز فروش

    -- اضافه‌کار
    [OT_NORMAL_H]    DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_OTN DEFAULT(0),   -- ساعت اضافه‌کار عادی
    [OT_HOLIDAY_H]   DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_OTH DEFAULT(0),   -- ساعت اضافه‌کار تعطیل
    [OT_ADMIN_H]     DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_OTA DEFAULT(0),   -- ساعت اضافه‌کار اداری
    [SHORTAGE_H]     DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_SHRT DEFAULT(0),  -- ساعت کسر کار (کسر با ضریب ۱ از نرخ ساعتی)

    -- غیبت و مرخصی
    [LEAVE_DAYS]     DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_LD  DEFAULT(0),   -- روز مرخصی
    [ABSENT_DAYS]    DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_AD  DEFAULT(0),   -- روز غیبت
    [MISSION_DAYS]   DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_MD  DEFAULT(0),   -- روز مأموریت

    -- کارکرد اسمی / رسمی (v6)
    [DAYS]           DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DAYS  DEFAULT(0), -- کارکرد اسمی: مبنای محاسبه پایه بیمه
    [DAYSB]          DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DAYSB DEFAULT(0), -- کارکرد رسمی: مبنای پرداخت آیتم‌های روزانه
    [FRID_COUNT]     TINYINT       NOT NULL CONSTRAINT DF_ATT_FRID  DEFAULT(0), -- تعداد جمعه‌های ماه (برای نهار)
    [TDAYS]          DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_TDAYS DEFAULT(0), -- تعطیلات رسمی قابل جبران

    -- آیتم‌های ثابت کارکردی
    [PERF_AMOUNT]    BIGINT        NOT NULL CONSTRAINT DF_ATT_PF DEFAULT(0),    -- راندمان/پاداش عملکرد (ریال)
    [TRANSP_AMOUNT]  BIGINT        NOT NULL CONSTRAINT DF_ATT_TR DEFAULT(0),    -- حق ناقل/ایاب و ذهاب (ریال)
    [KASR_OTHER]     BIGINT        NOT NULL CONSTRAINT DF_ATT_KO DEFAULT(0),    -- سایر کسورات (ریال)

    -- وضعیت ورود
    [SOURCE]         TINYINT       NOT NULL CONSTRAINT DF_ATT_SRC DEFAULT(1),   -- 1=دستی، 2=ورود دستگاه، 3=اکسل
    [LOCKED]         BIT           NOT NULL CONSTRAINT DF_ATT_LCK DEFAULT(0),
    [CREATED_AT]     DATETIME      NOT NULL CONSTRAINT DF_ATT_CRT DEFAULT(GETDATE()),
    [CREATED_BY]     INT           NULL,

    CONSTRAINT PK_PAY2_ATT     PRIMARY KEY ([PER_ID], [EMP_ID]),
    CONSTRAINT FK_ATT_PER      FOREIGN KEY ([PER_ID]) REFERENCES [PAY2_PERIOD]([PER_ID]),
    CONSTRAINT FK_ATT_EMP      FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT CK_ATT_DAYS     CHECK ([DAYS_TOLID]+[DAYS_EDARI]+[DAYS_KHADAMAT]+[DAYS_FOROSH] <= [WORK_DAYS] + 0.01),
    CONSTRAINT CK_ATT_DAYSB    CHECK ([DAYSB] <= [WORK_DAYS] + 0.01)
);
END;
GO

-- ── ۱۸. PAY2_ATT_VALUE — مقادیر متغیر اضافی per آیتم ───────────
IF OBJECT_ID(N'dbo.PAY2_ATT_VALUE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ATT_VALUE]
(
    [PER_ID]  INT    NOT NULL,
    [EMP_ID]  INT    NOT NULL,
    [ITEM_ID] INT    NOT NULL,
    [VALUE]   BIGINT NOT NULL CONSTRAINT DF_AV_VAL DEFAULT(0),

    CONSTRAINT PK_PAY2_ATT_VAL PRIMARY KEY ([PER_ID], [EMP_ID], [ITEM_ID]),
    CONSTRAINT FK_AV_ATT  FOREIGN KEY ([PER_ID], [EMP_ID]) REFERENCES [PAY2_ATTENDANCE]([PER_ID],[EMP_ID]) ON DELETE CASCADE,
    CONSTRAINT FK_AV_ITEM FOREIGN KEY ([ITEM_ID]) REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ── ۱۹. PAY2_LEAVE — ثبت مرخصی ─────────────────────────────────
IF OBJECT_ID(N'dbo.PAY2_LEAVE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_LEAVE]
(
    [LEV_ID]       INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]       INT           NOT NULL,
    [LEV_TYPE]     TINYINT       NOT NULL,                                   -- 1=استحقاقی، 2=استعلاجی، 3=بدون حقوق، 4=زایمان، 5=مأموریت
    [REQUEST_DATE] BIGINT        NOT NULL,                                   -- تاریخ درخواست
    [START_DATE]   BIGINT        NOT NULL,                                   -- تاریخ شروع
    [END_DATE]     BIGINT        NOT NULL,                                   -- تاریخ پایان

    -- مقدار مرخصی (روز+ساعت+دقیقه)
    [REQ_DAYS]     SMALLINT      NOT NULL CONSTRAINT DF_LEV_RD DEFAULT(0),
    [REQ_HOURS]    TINYINT       NOT NULL CONSTRAINT DF_LEV_RH DEFAULT(0),
    [REQ_MINUTES]  TINYINT       NOT NULL CONSTRAINT DF_LEV_RM DEFAULT(0),
    [TOTAL_MINUTES] AS ([REQ_DAYS]*440 + [REQ_HOURS]*60 + [REQ_MINUTES]),   -- ۱ روز = ۴۴۰ دقیقه
    [BAL_BEFORE]   INT           NULL,                                       -- مانده مرخصی قبل از این برگه (دقیقه)

    [DESCRIPTION]  NVARCHAR(300) NULL,                                       -- توضیحات (ساعت ورود-خروج)

    -- ارجاع و تأیید
    [REFER_TO]     INT           NULL,                                       -- ارجاع به (کد پرسنل مدیر)
    [STATUS]       TINYINT       NOT NULL CONSTRAINT DF_LEV_ST DEFAULT(1),  -- 1=ثبت، 2=تأیید درخواست‌کننده، 3=تأیید مدیر واحد، 4=تأیید مدیرعامل
    [APV1_BY]      INT           NULL, [APV1_AT] DATETIME NULL,             -- درخواست‌کننده
    [APV2_BY]      INT           NULL, [APV2_AT] DATETIME NULL,             -- مدیر واحد
    [APV3_BY]      INT           NULL, [APV3_AT] DATETIME NULL,             -- مدیر عامل
    [CREATED_AT]   DATETIME      NOT NULL CONSTRAINT DF_LEV_CRT DEFAULT(GETDATE()),
    [CREATED_BY]   INT           NULL,

    CONSTRAINT PK_PAY2_LEAVE   PRIMARY KEY ([LEV_ID]),
    CONSTRAINT FK_LEV_EMP      FOREIGN KEY ([EMP_ID])   REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_LEV_REFER    FOREIGN KEY ([REFER_TO]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ================================================================
-- گروه G — وام پرسنل
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_LOAN', N'U') IS NULL
BEGIN

-- ── ۲۰. PAY2_LOAN — وام ─────────────────────────────────────────

CREATE TABLE [dbo].[PAY2_LOAN]
(
    [LOAN_ID]    INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]     INT           NOT NULL,
    [WS_ID]      INT           NOT NULL,
    [LOAN_TYPE]  TINYINT       NOT NULL CONSTRAINT DF_LN_TYP DEFAULT(1),    -- 1=قرض‌الحسنه، 2=رفاهی، 3=ضروری، 4=مسکن، 5=سایر
    [LOAN_DATE]  BIGINT        NOT NULL,                                     -- تاریخ اعطا شمسی
    [AMOUNT]     BIGINT        NOT NULL,                                     -- مبلغ کل وام (ریال)
    [INSTALLMENT] BIGINT       NOT NULL,                                     -- مبلغ هر قسط
    [TOTAL_INST] SMALLINT      NOT NULL,                                     -- تعداد کل اقساط
    [PAID_INST]  SMALLINT      NOT NULL CONSTRAINT DF_LN_PI DEFAULT(0),
    [FIRST_PAY]  BIGINT        NOT NULL,                                     -- ماه اولین بازپرداخت شمسی (YYYYMM00)
    [PURPOSE]    NVARCHAR(200) NULL,
    [IS_ACTIVE]  BIT           NOT NULL CONSTRAINT DF_LN_ACT DEFAULT(1),
    [CREATED_AT] DATETIME      NOT NULL CONSTRAINT DF_LN_CRT DEFAULT(GETDATE()),
    [CREATED_BY] INT           NULL,

    CONSTRAINT PK_PAY2_LOAN PRIMARY KEY ([LOAN_ID]),
    CONSTRAINT FK_LN_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_LN_WS  FOREIGN KEY ([WS_ID])  REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ── ۲۱. PAY2_LOAN_SCHED — جدول اقساط ──────────────────────────
IF OBJECT_ID(N'dbo.PAY2_LOAN_SCHED', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_LOAN_SCHED]
(
    [SCHED_ID]  INT      NOT NULL IDENTITY(1,1),
    [LOAN_ID]   INT      NOT NULL,
    [INST_NUM]  SMALLINT NOT NULL,                                           -- شماره قسط
    [DUE_PERIOD] BIGINT  NOT NULL,                                           -- ماه سررسید شمسی (YYYYMM00)
    [AMOUNT]    BIGINT   NOT NULL,                                           -- مبلغ این قسط
    [RUN_ID]    INT      NULL,                                               -- شناسه اجرای حقوقی که کسر شد
    [PAID_AT]   DATETIME NULL,                                               -- تاریخ پرداخت واقعی

    CONSTRAINT PK_PAY2_LOAN_SCHED PRIMARY KEY ([SCHED_ID]),
    CONSTRAINT UQ_LOAN_INST       UNIQUE ([LOAN_ID], [INST_NUM]),
    CONSTRAINT FK_LS_LOAN         FOREIGN KEY ([LOAN_ID]) REFERENCES [PAY2_LOAN]([LOAN_ID])
);
END;
GO

-- View مانده وام هر پرسنل
CREATE OR ALTER VIEW [dbo].[V_PAY2_LOAN_BALANCE] AS
SELECT
    L.EMP_ID,
    L.LOAN_ID,
    L.AMOUNT                            AS TOTAL_AMOUNT,
    L.PAID_INST * L.INSTALLMENT         AS TOTAL_PAID,
    L.AMOUNT - L.PAID_INST*L.INSTALLMENT AS BALANCE,
    L.INSTALLMENT                       AS NEXT_INSTALLMENT,
    (L.TOTAL_INST - L.PAID_INST)        AS REMAINING_INST
FROM PAY2_LOAN L
WHERE L.IS_ACTIVE = 1 AND L.PAID_INST < L.TOTAL_INST;
GO

-- ================================================================
-- گروه H — مساعده هوشمند از حسابداری
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_ADVANCE_EXCL', N'U') IS NULL
BEGIN

-- ── ۲۲. PAY2_ADVANCE_EXCL — استثناهای دستی مساعده ──────────────

CREATE TABLE [dbo].[PAY2_ADVANCE_EXCL]
(
    [EXCL_ID]     INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]      INT           NOT NULL,
    [PERIOD_DATE] BIGINT        NOT NULL,                                    -- ماه شمسی اعمال استثنا (YYYYMM00)
    [EXCL_AMOUNT] BIGINT        NOT NULL,                                    -- مبلغ کسر از مانده (ریال)
    [REASON]      NVARCHAR(300) NOT NULL,
    [DEED_N_S]    FLOAT         NULL,                                        -- شماره سند مرجع در DEED_HED
    [CREATED_AT]  DATETIME      NOT NULL CONSTRAINT DF_AE_CRT DEFAULT(GETDATE()),
    [CREATED_BY]  INT           NULL,
    [APPROVED_BY] INT           NULL,

    CONSTRAINT PK_PAY2_ADV_EXCL PRIMARY KEY ([EXCL_ID]),
    CONSTRAINT FK_AE_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ── تابع کمکی: تبدیل تاریخ شمسی به ماه (مشابه Umonth سیستم قدیم) ─

CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_MONTH](@DATE BIGINT)
RETURNS INT
AS
BEGIN
    RETURN @DATE / 100  -- YYYYMM
END;
GO

IF OBJECT_ID(N'dbo.PAY2_RUN', N'U') IS NULL
BEGIN

-- ================================================================
-- گروه I — نتایج محاسبه حقوق
-- ================================================================

-- ── ۲۳. PAY2_RUN — هدر اجرا ─────────────────────────────────────

CREATE TABLE [dbo].[PAY2_RUN]
(
    [RUN_ID]     INT           NOT NULL IDENTITY(1,1),
    [PER_ID]     INT           NOT NULL,                                     -- دوره ماهیانه
    [RUN_NO]     SMALLINT      NOT NULL CONSTRAINT DF_RUN_NO DEFAULT(1),     -- شماره ترتیبی نسخه — v6
    [IS_LATEST]  BIT           NOT NULL CONSTRAINT DF_RUN_IL DEFAULT(1),     -- ۱=آخرین نسخه این دوره — v6
    [CALC_AT]    DATETIME      NOT NULL CONSTRAINT DF_RUN_CA DEFAULT(GETDATE()),
    [CALC_BY]    INT           NULL,
    [STATUS]     TINYINT       NOT NULL CONSTRAINT DF_RUN_ST DEFAULT(1),     -- 1=پیش‌نویس، 2=نهایی، 3=سند صادرشده
    [PREV_RUN_ID] INT          NULL,                                         -- ارجاع به نسخه قبلی — v6
    [PAYROLL_ENGINE_VERSION] SMALLINT NULL,                                  -- 3 = Snapshot کامل اتمی مبلغ/پرسنل/عنوان آیتم
    [WS_ID_SNAP] INT NULL,                                                   -- کارگاه مؤثر همین Run
    [DEED_ID_SAL] INT          NULL,                                         -- شماره سند حقوق در حسابداری
    [DEED_ID_INS] INT          NULL,                                         -- شماره سند بیمه
    [DEED_MODE]              TINYINT  NULL,                                  -- روش صدور سند این اجرا (1=خلاصه، 2=تفکیکی)
    [DEED_GENERATOR_VERSION] SMALLINT NULL,                                  -- نسخه موتور تولید سند
    [NOTES]      NVARCHAR(300) NULL,

    CONSTRAINT PK_PAY2_RUN          PRIMARY KEY ([RUN_ID]),
    CONSTRAINT UQ_RUN_PERIOD_NO     UNIQUE ([PER_ID], [RUN_NO]),             -- v6: UNIQUE روی (PER_ID, RUN_NO)
    CONSTRAINT FK_RUN_PER           FOREIGN KEY ([PER_ID])     REFERENCES [PAY2_PERIOD]([PER_ID]),
    CONSTRAINT FK_RUN_PREV          FOREIGN KEY ([PREV_RUN_ID]) REFERENCES [PAY2_RUN]([RUN_ID])
);
END;
GO

-- ── ۲۴. PAY2_RUN_LINE — نتیجه per پرسنل ─────────────────────────
IF OBJECT_ID(N'dbo.PAY2_RUN_LINE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_RUN_LINE]
(
    [RUN_ID]               INT           NOT NULL,
    [EMP_ID]               INT           NOT NULL,
    [DEC_ID]               INT           NULL,                               -- حکم استفاده‌شده
    [WORK_DAYS]            DECIMAL(5,2)  NOT NULL,

    -- پرداختی‌ها
    [GROSS_PAY]            BIGINT        NOT NULL,                           -- ناخالص (کل پرداختی قبل از کسر)

    -- بیمه
    [INS_BASE]             BIGINT        NOT NULL,                           -- مبنای بیمه
    [INS_WORKER]           BIGINT        NOT NULL,                           -- کسر بیمه کارگر (۷٪)
    [INS_EMPLOYER]         BIGINT        NOT NULL,                           -- بیمه کارفرما (۲۳٪)

    -- مالیات
    [TAX_BASE]             BIGINT        NOT NULL,                           -- مبنای مالیات
    [TAX_AMOUNT]           BIGINT        NOT NULL,

    -- کسورات دیگر
    [LOAN_DED]             BIGINT        NOT NULL CONSTRAINT DF_RL_LD DEFAULT(0),
    [ADVANCE_DED]          BIGINT        NOT NULL CONSTRAINT DF_RL_AD DEFAULT(0), -- مساعده هوشمند
    [OTHER_DED]            BIGINT        NOT NULL CONSTRAINT DF_RL_OD DEFAULT(0),
    [TOTAL_DED]            BIGINT        NOT NULL,

    -- خالص
    [NET_PAY]              BIGINT        NOT NULL,                           -- خالص پرداختی

    -- اطلاعات فیش
    [LEAVE_BAL_DAYS]       DECIMAL(5,2)  NULL,                              -- مانده مرخصی (روز)
    [LOAN_BALANCE]         BIGINT        NULL,                              -- مانده وام
    [ADVANCE_BALANCE_SNAP] BIGINT        NULL,                              -- عکس مانده مساعده در لحظه محاسبه

    CONSTRAINT PK_PAY2_RUN_LINE PRIMARY KEY ([RUN_ID], [EMP_ID]),
    CONSTRAINT FK_RL_RUN FOREIGN KEY ([RUN_ID]) REFERENCES [PAY2_RUN]([RUN_ID]),
    CONSTRAINT FK_RL_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- مشخصات غیرقابل‌تغییر پرسنل و شغل در لحظه محاسبه Run
IF OBJECT_ID(N'dbo.PAY2_RUN_EMP_SNAPSHOT', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[PAY2_RUN_EMP_SNAPSHOT]
(
    [RUN_ID] INT NOT NULL, [EMP_ID] INT NOT NULL,
    [EMP_CODE] NVARCHAR(50) NULL, [FIRST_NAME] NVARCHAR(100) NULL, [LAST_NAME] NVARCHAR(100) NULL,
    [FATHER_NAME] NVARCHAR(100) NULL, [NATIONAL_CODE] NVARCHAR(20) NULL, [INS_CODE] NVARCHAR(30) NULL,
    [ID_NUMBER] NVARCHAR(30) NULL, [BIRTH_PLACE] NVARCHAR(100) NULL, [BIRTH_DATE] BIGINT NULL,
    [GENDER] TINYINT NULL, [INS_TYPE_SNAP] TINYINT NOT NULL, [TAX_EXEMPT_SNAP] BIT NOT NULL,
    [MARITAL_SNAP] TINYINT NULL, [NATIONALITY_SNAP] TINYINT NULL, [MOBILE] NVARCHAR(30) NULL,
    [JOB_CODE_SNAP] NVARCHAR(50) NULL, [JOB_NAME_SNAP] NVARCHAR(200) NULL,
    [HIRE_DATE_SNAP] BIGINT NULL, [FIRE_DATE_SNAP] BIGINT NULL,
    [IS_MANAGER_SNAP] BIT NULL, [IS_JANBAZ_SNAP] BIT NULL,
    [REGION_DEPRIVATION_SNAP] TINYINT NULL, [ACC_T_SNAP] NVARCHAR(50) NULL,
    CONSTRAINT PK_PAY2_RUN_EMP_SNAPSHOT PRIMARY KEY ([RUN_ID],[EMP_ID]),
    CONSTRAINT FK_PAY2_RES_LINE FOREIGN KEY ([RUN_ID],[EMP_ID])
        REFERENCES [dbo].[PAY2_RUN_LINE]([RUN_ID],[EMP_ID]) ON DELETE CASCADE
);
END;
GO

-- ── ۲۵. PAY2_RUN_DETAIL — ریز آیتمی (فیش تفصیلی و حسابرسی) ────
IF OBJECT_ID(N'dbo.PAY2_RUN_DETAIL', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_RUN_DETAIL]
(
    [RUN_ID]      INT    NOT NULL,
    [EMP_ID]      INT    NOT NULL,
    [ITEM_ID]     INT    NOT NULL,
    [AMOUNT]      BIGINT NOT NULL,                                           -- مبلغ رسمی پرداختی
    [NOMINAL_AMOUNT] BIGINT NULL,                                             -- مبلغ اسمی Snapshot برای بیمه/مالیات
    [ITEM_CODE_SNAP] NVARCHAR(30) NULL,
    [ITEM_NAME_SNAP] NVARCHAR(200) NULL,
    [CALC_BASIS_SNAP] TINYINT NULL,
    [ITEM_TYPE_SNAP] TINYINT NULL,
    [INS_SUBJECT_AMOUNT] BIGINT NULL,                                      -- بخش مبلغ اسمی که در همان Run مشمول بیمه بوده
    [TAX_SUBJECT_AMOUNT] BIGINT NULL,                                      -- بخش مبلغ اسمی که در همان Run مشمول مالیات بوده
    [INS_SUBJECT] BIT    NOT NULL,                                           -- مشمول بیمه بود؟
    [TAX_SUBJECT] BIT    NOT NULL,                                           -- مشمول مالیات بود؟

    CONSTRAINT PK_PAY2_RUN_DETAIL PRIMARY KEY ([RUN_ID], [EMP_ID], [ITEM_ID]),
    CONSTRAINT FK_RD_LINE FOREIGN KEY ([RUN_ID], [EMP_ID])
        REFERENCES [PAY2_RUN_LINE]([RUN_ID], [EMP_ID]) ON DELETE CASCADE
);
END;
GO

-- View لیست بیمه
CREATE OR ALTER VIEW [dbo].[V_PAY2_BIMEH] AS
SELECT
    P.PERIOD_DATE,
    W.SOCIAL_INS_CODE,
    W.WS_NAME,
    W.EMPLOYER_NAME, -- اضافه شده به خروجی لیست بیمه
    W.POSTAL_CODE,   -- اضافه شده به خروجی لیست بیمه
    E.INS_CODE,
    E.NATIONAL_CODE,
    E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
    RL.WORK_DAYS,
    RL.INS_BASE,
    RL.INS_WORKER,
    RL.INS_EMPLOYER,
    RL.INS_BASE * 0.30 AS TOTAL_BIMEH,
    E.INS_TYPE
FROM PAY2_RUN_LINE RL
INNER JOIN PAY2_RUN      R  ON RL.RUN_ID = R.RUN_ID
INNER JOIN PAY2_PERIOD   P  ON R.PER_ID  = P.PER_ID
INNER JOIN PAY2_WORKSHOP W  ON P.WS_ID   = W.WS_ID
INNER JOIN PAY2_EMPLOYEE E  ON RL.EMP_ID = E.EMP_ID;
GO

-- تابع محاسبه مالیات پلکانی
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_CALC_TAX]
    (@ANNUAL_BASE BIGINT, @TAX_YEAR SMALLINT)
RETURNS BIGINT
AS
BEGIN
    DECLARE @TAX        BIGINT      = 0;
    DECLARE @PREV_LIMIT BIGINT      = 0;
    DECLARE @RATE       DECIMAL(5,2);
    DECLARE @LIMIT      BIGINT;
    DECLARE @FIXED      BIGINT;

    DECLARE cur CURSOR FOR
        SELECT UPPER_LIMIT, RATE_PCT, FIXED_TAX
        FROM PAY2_TAX_BRACKET
        WHERE TAX_YEAR = @TAX_YEAR
        ORDER BY SORT_ORDER;

    OPEN cur;
    FETCH NEXT FROM cur INTO @LIMIT, @RATE, @FIXED;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @ANNUAL_BASE <= @LIMIT
        BEGIN
            SET @TAX = @FIXED + CAST((@ANNUAL_BASE - @PREV_LIMIT) * @RATE / 100 AS BIGINT);
            BREAK;
        END;
        SET @PREV_LIMIT = @LIMIT;
        FETCH NEXT FROM cur INTO @LIMIT, @RATE, @FIXED;
    END;

    -- اگر از همه پله‌ها بیشتر بود: پله آخر اعمال شود
    IF @@FETCH_STATUS <> 0 AND @TAX = 0
        SET @TAX = @FIXED + CAST((@ANNUAL_BASE - @PREV_LIMIT) * @RATE / 100 AS BIGINT);

    CLOSE cur;
    DEALLOCATE cur;

    RETURN @TAX;  -- مالیات سالانه — موتور ÷12 می‌کند
END;
GO

-- ================================================================
-- گروه J — تسویه حساب پرسنل
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_SETTLEMENT', N'U') IS NULL
BEGIN

-- ── ۲۶. PAY2_SETTLEMENT — تسویه حساب ───────────────────────────

CREATE TABLE [dbo].[PAY2_SETTLEMENT]
(
    [SET_ID]           INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]           INT           NOT NULL,
    [WS_ID]            INT           NOT NULL,

    -- اطلاعات زمانی
    [SETTLE_DATE]      BIGINT        NOT NULL,                               -- تاریخ تسویه شمسی
    [HIRE_DATE]        BIGINT        NOT NULL,                               -- تاریخ شروع به کار
    [END_DATE]         BIGINT        NOT NULL,                               -- تاریخ پایان کار
    [SENIORITY_DAYS]   INT           NOT NULL,                               -- سابقه خدمت (روز) — محاسبه‌شده
    [SENIORITY_YEARS]  DECIMAL(6,2)  NOT NULL,                               -- سابقه (سال — برای نمایش)

    -- مبنای محاسبه
    [LAST_SALARY]      BIGINT        NOT NULL,                               -- دستمزد مبنا (آخرین حکم)
    [LAST_DAILY]       BIGINT        NOT NULL,                               -- نرخ روزانه (برای محاسبه مرخصی)

    -- سابقه تسویه قبلی
    [PREV_SET_ID]         INT        NULL,                                   -- FK به تسویه قبلی
    [PREV_SENIORITY_DAYS] INT        NOT NULL CONSTRAINT DF_SET_PSD DEFAULT(0), -- سابقه حساب‌شده در تسویه قبلی

    -- مانده مرخصی
    [LEAVE_BAL_MIN]    INT           NOT NULL CONSTRAINT DF_SET_LBM DEFAULT(0),   -- دقیقه‌های مانده مرخصی (مبنای ۴۴۰ دق/روز) — v6
    [LEAVE_BAL_DAYS]   DECIMAL(5,2)  NOT NULL CONSTRAINT DF_SET_LBD DEFAULT(0),   -- مانده مرخصی (روز — برای نمایش)

    -- ستون‌های درآمد تسویه
    [EIDI]             BIGINT        NOT NULL CONSTRAINT DF_SET_EID DEFAULT(0),   -- عیدی (بر اساس BONUS_MODE)
    [BON]              BIGINT        NOT NULL CONSTRAINT DF_SET_BON DEFAULT(0),   -- بن کارگری
    [LEAVE_PAY]        BIGINT        NOT NULL CONSTRAINT DF_SET_LPY DEFAULT(0),   -- مانده مرخصی به ریال
    [SANAVAT]          BIGINT        NOT NULL CONSTRAINT DF_SET_SAN DEFAULT(0),   -- حق سنوات
    [PREV_CREDIT]      BIGINT        NOT NULL CONSTRAINT DF_SET_PCR DEFAULT(0),   -- بستانکاری قبلی
    [OTHER_INCOME]     BIGINT        NOT NULL CONSTRAINT DF_SET_OIN DEFAULT(0),   -- سایر درآمدها
    [TOTAL_INCOME] AS (EIDI + BON + LEAVE_PAY + SANAVAT + PREV_CREDIT + OTHER_INCOME), -- جمع درآمدها

    -- ستون‌های کسورات تسویه
    [PREV_DEBIT]       BIGINT        NOT NULL CONSTRAINT DF_SET_PDB DEFAULT(0),   -- بدهکاری قبلی
    [EIDI_TAX]         BIGINT        NOT NULL CONSTRAINT DF_SET_ETX DEFAULT(0),   -- مالیات عیدی
    [LOAN_BALANCE]     BIGINT        NOT NULL CONSTRAINT DF_SET_LBL DEFAULT(0),   -- مانده وام قابل کسر
    [OTHER_DED]        BIGINT        NOT NULL CONSTRAINT DF_SET_ODE DEFAULT(0),   -- سایر کسورات
    [TOTAL_DED]    AS (PREV_DEBIT + EIDI_TAX + LOAN_BALANCE + OTHER_DED),         -- جمع کسورات

    -- نتیجه
    [NET_SETTLE]   AS (EIDI + BON + LEAVE_PAY + SANAVAT + PREV_CREDIT + OTHER_INCOME
                       - PREV_DEBIT - EIDI_TAX - LOAN_BALANCE - OTHER_DED),      -- خالص تسویه

    -- وضعیت و اسناد
    [STATUS]           TINYINT       NOT NULL CONSTRAINT DF_SET_ST DEFAULT(1),    -- 1=پیش‌نویس، 2=نهایی، 3=سند صادر شده
    [DEED_N_S]         FLOAT         NULL,                                         -- شماره سند حسابداری تسویه
    [CALC_METHOD]      NVARCHAR(200) NULL,                                         -- روش محاسبه (برای حسابرسی — JSON)
    [NOTES]            NVARCHAR(300) NULL,
    [CREATED_AT]       DATETIME      NOT NULL CONSTRAINT DF_SET_CRT DEFAULT(GETDATE()),
    [CREATED_BY]       INT           NULL,
    [APPROVED_BY]      INT           NULL,
    [APPROVED_AT]      DATETIME      NULL,

    CONSTRAINT PK_PAY2_SETTLEMENT  PRIMARY KEY ([SET_ID]),
    CONSTRAINT FK_SET_EMP          FOREIGN KEY ([EMP_ID])      REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_SET_WS           FOREIGN KEY ([WS_ID])       REFERENCES [PAY2_WORKSHOP]([WS_ID]),
    CONSTRAINT FK_SET_PREV         FOREIGN KEY ([PREV_SET_ID]) REFERENCES [PAY2_SETTLEMENT]([SET_ID])
);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_SET_EMP' AND object_id = OBJECT_ID(N'dbo.PAY2_SETTLEMENT'))
CREATE NONCLUSTERED INDEX IX_SET_EMP
    ON PAY2_SETTLEMENT ([EMP_ID], [SETTLE_DATE]);
GO

-- ================================================================
-- پایان اسکریپت
-- ================================================================
-- خلاصه اشیاء ایجاد شده:
--
--  گروه A : PAY2_CONFIG, PAY2_CONFIG_LOG, TR_PAY2_CONFIG_LOG,
--            PAY2_TAX_BRACKET
--            + INSERT داده‌های اولیه PAY2_CONFIG و PAY2_TAX_BRACKET
--
--  گروه B : PAY2_WORKSHOP, PAY2_WORKSHOP_ACC
--
--  گروه C : PAY2_JOB, PAY2_EMPLOYEE (+ Filtered Index),
--            PAY2_CONTRACT, PAY2_LEAVE_BAL
--
--  گروه D : PAY2_ITEM_DEF (+ INSERT آیتم‌های سیستمی),
--            PAY2_ITEM_TEMPLATE, PAY2_ITEM_TMPL_LINE
--
--  گروه E : PAY2_DECREE (+ Index IX_DEC_EMP_DATE),
--            PAY2_DECREE_LINE, PAY2_OVERRIDE
--
--  گروه F : PAY2_PERIOD, PAY2_ATTENDANCE, PAY2_ATT_VALUE, PAY2_LEAVE
--
--  گروه G : PAY2_LOAN, PAY2_LOAN_SCHED, V_PAY2_LOAN_BALANCE
--
--  گروه H : PAY2_ADVANCE_EXCL, FN_PAY2_MONTH, SP_PAY2_GET_ADVANCES
--
--  گروه I : PAY2_RUN, PAY2_RUN_LINE, PAY2_RUN_DETAIL,
--            V_PAY2_BIMEH, FN_PAY2_CALC_TAX
--
--  گروه J : PAY2_SETTLEMENT (+ Index IX_SET_EMP)
--
-- جمع: ۲۱ جدول، ۲ View، ۲ Function، ۱ Stored Procedure، ۱ Trigger
-- ================================================================

-- Migration 010: Fix configurations for Shift Allowance
GO
";
                ExecuteBatches(db, tablescript);

                // ===========================================================
                // 2. Schema Updates (Idempotent)
                // ===========================================================
                string schemaUpdates = @"
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'POSTAL_CODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [POSTAL_CODE] NVARCHAR(20) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'EMPLOYER_NAME') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [EMPLOYER_NAME] NVARCHAR(100) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'PROVINCE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [PROVINCE] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'CITY') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [CITY] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'REGISTRATION_NUMBER') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [REGISTRATION_NUMBER] NVARCHAR(20) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'SSO_BRANCH') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [SSO_BRANCH] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'FINANCIAL_MANAGER') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [FINANCIAL_MANAGER] NVARCHAR(100) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'ADMIN_MANAGER') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [ADMIN_MANAGER] NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PAY2_JOB_PERFORMANCE')
                    CREATE NONCLUSTERED INDEX IX_PAY2_JOB_PERFORMANCE ON [dbo].[PAY2_JOB] ([IS_ACTIVE], [JOB_NAME]) INCLUDE ([JOB_ID]);

                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_WS_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));
                IF COL_LENGTH('dbo.PAY2_DECREE', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_DEC_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));
                IF COL_LENGTH('dbo.PAY2_DECREE_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_DL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));
                IF COL_LENGTH('dbo.PAY2_ITEM_TMPL_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_TL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));

                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'DEFAULT_DEED_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [DEFAULT_DEED_MODE] TINYINT NOT NULL CONSTRAINT DF_WS_DEED_MODE DEFAULT(1);
                IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_MODE] TINYINT NULL;
                IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_GENERATOR_VERSION') IS NULL
                    ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_GENERATOR_VERSION] SMALLINT NULL;

                -- رهاسازی محدودیت Basis=3
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CALC_BASIS' AND parent_object_id = OBJECT_ID(N'dbo.PAY2_ITEM_DEF') AND definition NOT LIKE '%(3)%')
                BEGIN
                    ALTER TABLE dbo.PAY2_ITEM_DEF DROP CONSTRAINT CK_CALC_BASIS;
                    ALTER TABLE dbo.PAY2_ITEM_DEF ADD CONSTRAINT CK_CALC_BASIS CHECK ([CALC_BASIS] IN (1,2,3));
                END;

                -- رهاسازی محدودیت 6 برای LEV_TYPE
                DECLARE @sql NVARCHAR(MAX) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(10)
                FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) = 'PAY2_LEAVE' AND definition LIKE '%LEV_TYPE%' AND definition NOT LIKE '%(6)%';
                IF LEN(@sql) > 0 EXEC sp_executesql @sql;

                -- رهاسازی محدودیت Basis=3 روی Overrides
                SET @sql = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(10)
                FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN ('PAY2_DECREE_LINE', 'PAY2_OVERRIDE', 'PAY2_ITEM_TMPL_LINE') AND definition LIKE '%BASIS_OV%' AND definition NOT LIKE '%(3)%';
                IF LEN(@sql) > 0 EXEC sp_executesql @sql;
                ";
                db.Execute(schemaUpdates);

                // نصب زیرساخت Preview/Apply سنوات؛ بدون هیچ اعمال خودکار روی احکام.
                ExecuteBatchesTransactional(db, @"SET XACT_ABORT ON;

IF COL_LENGTH('dbo.PAY2_DECREE_LINE','NOMINAL_AMOUNT_OV') IS NULL ALTER TABLE dbo.PAY2_DECREE_LINE ADD NOMINAL_AMOUNT_OV DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.PAY2_DECREE_LINE','OFFICIAL_AMOUNT_OV') IS NULL ALTER TABLE dbo.PAY2_DECREE_LINE ADD OFFICIAL_AMOUNT_OV DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','NOMINAL_AMOUNT') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD NOMINAL_AMOUNT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','ITEM_CODE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD ITEM_CODE_SNAP NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','ITEM_NAME_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD ITEM_NAME_SNAP NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','CALC_BASIS_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD CALC_BASIS_SNAP TINYINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','ITEM_TYPE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD ITEM_TYPE_SNAP TINYINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','INS_SUBJECT_AMOUNT') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD INS_SUBJECT_AMOUNT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','TAX_SUBJECT_AMOUNT') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD TAX_SUBJECT_AMOUNT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_ATTENDANCE','SHORTAGE_H') IS NULL ALTER TABLE dbo.PAY2_ATTENDANCE ADD SHORTAGE_H DECIMAL(6,2) NOT NULL CONSTRAINT DF_ATT_SHRT DEFAULT(0);

-- نگاشت «قلم حکم ← شمارهٔ تفصیلیِ حساب هزینه» برای سند تفصیلی کامل (DEED_MODE=3).
IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NULL
    ALTER TABLE dbo.PAY2_ITEM_DEF ADD EXP_TAFSILI SMALLINT NULL;
GO

-- مقداردهی جدا از ALTER است و داخل EXEC: در یک batch نمی‌شود ستونی را که همان‌جا
-- ساخته شده ارجاع داد، چون SQL Server کلِ batch را پیش از اجرا کامپایل می‌کند و
-- کلِ بلوک — با تمام ALTERهای دیگرش — رد می‌شود.
--
-- شرطِ «فقط وقتی ستون تازه ساخته شد» نیست، چون روی دیتابیس تازه ستون از همان
-- CREATE TABLE وجود دارد و آن شرط هرگز برقرار نمی‌شد؛ نتیجه‌اش این بود که نگاشت
-- خالی می‌مانْد و همه‌ی هزینه‌ها روی یک تفصیلی («سایر») جمع می‌شد.
-- فیلترِ IS NULL تضمین می‌کند ویرایش‌های کاربر با اجرای دوباره بازنویسی نشود.
IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NOT NULL
EXEC(N'
    UPDATE I SET I.EXP_TAFSILI = V.T
    FROM dbo.PAY2_ITEM_DEF I
    INNER JOIN (VALUES
        (''BASE_SAL_B'',1),(''BASE_SAL'',1),(''SANOVAT_PAYE'',1),
        (''OT_NORMAL'',2),(''OT_HOLIDAY'',2),(''OT_ADMIN'',2),
        (''PERF_BONUS'',3),(''CHILDREN'',4),(''HOME'',5),
        (''ATTRACT'',9),(''HARD_COND'',9),(''NAHAR'',9),(''OTHER_FIX'',9),(''TRANSP'',9),
        (''SHIFT'',11),(''GROCERY'',12),(''FAMILY_ALLOW'',13)
    ) AS V(C,T) ON V.C = I.ITEM_CODE
    WHERE I.EXP_TAFSILI IS NULL;

    -- کسورات حساب هزینه ندارند.
    UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI = NULL
    WHERE ITEM_TYPE IN (3,4,5) AND EXP_TAFSILI IS NOT NULL;

    -- اقلامِ تعریف‌شده توسط کاربر روی «سایر» می‌نشینند تا بدون تنظیم اضافه کار کنند.
    UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI = 9
    WHERE ITEM_TYPE IN (1,2) AND EXP_TAFSILI IS NULL;');
IF COL_LENGTH('dbo.PAY2_RUN','PAYROLL_ENGINE_VERSION') IS NULL ALTER TABLE dbo.PAY2_RUN ADD PAYROLL_ENGINE_VERSION SMALLINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN','WS_ID_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN ADD WS_ID_SNAP INT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','NOMINAL_GROSS') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD NOMINAL_GROSS BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','NOMINAL_DAYS') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD NOMINAL_DAYS DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','INS_EMPLOYER_BASE') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD INS_EMPLOYER_BASE BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','INS_UNEMPLOYMENT') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD INS_UNEMPLOYMENT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','ROUNDING_ADJ') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD ROUNDING_ADJ BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','HIRE_DATE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD HIRE_DATE_SNAP BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','FIRE_DATE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD FIRE_DATE_SNAP BIGINT NULL;
IF OBJECT_ID(N'dbo.PAY2_RUN_EMP_SNAPSHOT',N'U') IS NULL
 CREATE TABLE dbo.PAY2_RUN_EMP_SNAPSHOT
 (
  RUN_ID INT NOT NULL, EMP_ID INT NOT NULL,
  EMP_CODE NVARCHAR(50) NULL, FIRST_NAME NVARCHAR(100) NULL, LAST_NAME NVARCHAR(100) NULL,
  FATHER_NAME NVARCHAR(100) NULL, NATIONAL_CODE NVARCHAR(20) NULL, INS_CODE NVARCHAR(30) NULL,
  ID_NUMBER NVARCHAR(30) NULL, BIRTH_PLACE NVARCHAR(100) NULL, BIRTH_DATE BIGINT NULL,
  GENDER TINYINT NULL, INS_TYPE_SNAP TINYINT NOT NULL, TAX_EXEMPT_SNAP BIT NOT NULL,
  MARITAL_SNAP TINYINT NULL, NATIONALITY_SNAP TINYINT NULL, MOBILE NVARCHAR(30) NULL,
  JOB_CODE_SNAP NVARCHAR(50) NULL, JOB_NAME_SNAP NVARCHAR(200) NULL,
  HIRE_DATE_SNAP BIGINT NULL, FIRE_DATE_SNAP BIGINT NULL,
  IS_MANAGER_SNAP BIT NULL, IS_JANBAZ_SNAP BIT NULL,
  REGION_DEPRIVATION_SNAP TINYINT NULL, ACC_T_SNAP NVARCHAR(50) NULL,
  CONSTRAINT PK_PAY2_RUN_EMP_SNAPSHOT PRIMARY KEY(RUN_ID,EMP_ID),
  CONSTRAINT FK_PAY2_RES_LINE FOREIGN KEY(RUN_ID,EMP_ID) REFERENCES dbo.PAY2_RUN_LINE(RUN_ID,EMP_ID) ON DELETE CASCADE
 );
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','IS_MANAGER_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD IS_MANAGER_SNAP BIT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','IS_JANBAZ_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD IS_JANBAZ_SNAP BIT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','REGION_DEPRIVATION_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD REGION_DEPRIVATION_SNAP TINYINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','ACC_T_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD ACC_T_SNAP NVARCHAR(50) NULL;
IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='SANOVAT_PAYE')
 INSERT dbo.PAY2_ITEM_DEF(ITEM_CODE,ITEM_NAME,ITEM_TYPE,CALC_BASIS,INS_SUBJECT,TAX_SUBJECT,INS_BASE_DAYS,PAY_BASE_DAYS,IS_SYSTEM,SHOW_IN_SLIP,SORT_ORDER,IS_ACTIVE,NOTES)
 VALUES('SANOVAT_PAYE',N'پایه سنوات روزانه',1,1,1,1,1,2,1,1,3,1,N'مبلغ روزانه دو ریلی؛ مشمولیت از Snapshot مؤثر Run خوانده می‌شود');

-- سنوات هزینه‌ی «حقوق» است (تفصیلی ۱). این آیتم پایین‌تر از بلوکِ نگاشت اولیه
-- ساخته می‌شود، پس آنجا هنوز وجود ندارد و باید صریحاً اینجا مقدار بگیرد.
--
-- شرط هم NULL را می‌گیرد و هم ۹: روی دیتابیس تازه، سنوات همین‌جا و بدون مقدار
-- درج می‌شود (ستون DEFAULT ندارد) و اگر فقط دنبال ۹ می‌گشتیم مقدارش NULL
-- می‌مانْد و هزینه‌ی سنوات به‌جای «حقوق» روی «سایر» می‌افتاد. اگر کاربر عمداً
-- تفصیلی دیگری گذاشته باشد، دست‌نخورده می‌مانَد.
IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NOT NULL
    EXEC(N'UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI=1
           WHERE ITEM_CODE=''SANOVAT_PAYE'' AND (EXP_TAFSILI=9 OR EXP_TAFSILI IS NULL);');

IF OBJECT_ID(N'dbo.PAY2_SANOVAT_MIGRATION_INPUT',N'U') IS NULL
 CREATE TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT
 (
   DEC_ID INT NOT NULL CONSTRAINT PK_PAY2_SANOVAT_MIGRATION_INPUT PRIMARY KEY,
   SOURCE_RAIL NVARCHAR(10) NULL,
   NOMINAL_SENIORITY_DAILY DECIMAL(18,2) NULL,
   OFFICIAL_SENIORITY_DAILY DECIMAL(18,2) NULL,
   IS_APPROVED BIT NOT NULL CONSTRAINT DF_PAY2_SMI_APPROVED DEFAULT(0),
   SOURCE_NOTE NVARCHAR(300) NOT NULL,
   ENTERED_BY INT NULL,
   ENTERED_AT DATETIME2 NOT NULL CONSTRAINT DF_PAY2_SMI_AT DEFAULT(SYSDATETIME()),
   CONSTRAINT FK_PAY2_SMI_DEC FOREIGN KEY(DEC_ID) REFERENCES dbo.PAY2_DECREE(DEC_ID)
 );
ELSE
BEGIN
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','SENIORITY_DAILY_AMOUNT') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ALTER COLUMN SENIORITY_DAILY_AMOUNT DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','SOURCE_RAIL') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ADD SOURCE_RAIL NVARCHAR(10) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','NOMINAL_SENIORITY_DAILY') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ADD NOMINAL_SENIORITY_DAILY DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','OFFICIAL_SENIORITY_DAILY') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ADD OFFICIAL_SENIORITY_DAILY DECIMAL(18,2) NULL;
END;

IF OBJECT_ID(N'dbo.PAY2_SANOVAT_MIGRATION_LOG',N'U') IS NULL
 CREATE TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG
 (
   DEC_ID INT NOT NULL CONSTRAINT PK_PAY2_SANOVAT_MIGRATION_LOG PRIMARY KEY,
   NEW_DEC_ID INT NULL, EFFECTIVE_FROM BIGINT NULL,
   SOURCE_RAIL NVARCHAR(10) NOT NULL,
   NOMINAL_BASE_BEFORE DECIMAL(18,2) NULL, NOMINAL_SENIORITY DECIMAL(18,2) NOT NULL, NOMINAL_BASE_AFTER DECIMAL(18,2) NULL,
   OFFICIAL_BASE_BEFORE DECIMAL(18,2) NULL, OFFICIAL_SENIORITY DECIMAL(18,2) NOT NULL, OFFICIAL_BASE_AFTER DECIMAL(18,2) NULL,
   APPLIED_BY INT NULL, APPLIED_AT DATETIME2 NOT NULL CONSTRAINT DF_PAY2_SML_AT DEFAULT(SYSDATETIME()),
   CONSTRAINT FK_PAY2_SML_DEC FOREIGN KEY(DEC_ID) REFERENCES dbo.PAY2_DECREE(DEC_ID)
 );
ELSE
BEGIN
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','BASE_BEFORE') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ALTER COLUMN BASE_BEFORE DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','SENIORITY_AMOUNT') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ALTER COLUMN SENIORITY_AMOUNT DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','BASE_AFTER') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ALTER COLUMN BASE_AFTER DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','NEW_DEC_ID') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD NEW_DEC_ID INT NULL, EFFECTIVE_FROM BIGINT NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','SOURCE_RAIL') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD SOURCE_RAIL NVARCHAR(10) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','NOMINAL_BASE_BEFORE') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD NOMINAL_BASE_BEFORE DECIMAL(18,2) NULL, NOMINAL_SENIORITY DECIMAL(18,2) NULL, NOMINAL_BASE_AFTER DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','OFFICIAL_BASE_BEFORE') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD OFFICIAL_BASE_BEFORE DECIMAL(18,2) NULL, OFFICIAL_SENIORITY DECIMAL(18,2) NULL, OFFICIAL_BASE_AFTER DECIMAL(18,2) NULL;
END;

IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'INS_NON_SUBJECT_OPT_IN')
 INSERT dbo.PAY2_CONFIG(CFG_KEY,CFG_VALUE,CFG_OPTIONS,CFG_DEFAULT,CFG_SECTION,LABEL_FA,DESC_FA,OPT_LABELS,DATA_TYPE,ACCESS_LEVEL)
 VALUES(N'INS_NON_SUBJECT_OPT_IN',N'DISABLED',NULL,N'DISABLED',N'INSURANCE',N'مجوز تغییر مشمولیت شیفت و اضافه‌کار',
        N'فقط برای دیتابیس هدف، مقدار باید به APPROVED:<DatabaseName> تغییر کند؛ Updater هرگز آن را خودکار فعال نمی‌کند.',NULL,N'TEXT',1);
IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM')
 INSERT dbo.PAY2_CONFIG(CFG_KEY,CFG_VALUE,CFG_OPTIONS,CFG_DEFAULT,CFG_SECTION,LABEL_FA,DESC_FA,OPT_LABELS,DATA_TYPE,ACCESS_LEVEL)
 VALUES(N'INS_NON_SUBJECT_EFFECTIVE_FROM',N'0',NULL,N'0',N'INSURANCE',N'تاریخ اثر عدم مشمولیت شیفت و اضافه‌کار',
        N'تاریخ شمسی روز اول ماه؛ فقط Procedure تأییدشده Opt-in آن را مقداردهی می‌کند.',NULL,N'DATE',1);

IF OBJECT_ID(N'dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG',N'U') IS NULL
 CREATE TABLE dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG
 (
   LOG_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PAY2_INS_NON_SUBJECT_OPTIN_LOG PRIMARY KEY,
   DATABASE_NAME SYSNAME NOT NULL,
   ITEM_CODE NVARCHAR(30) NOT NULL,
   EFFECTIVE_FROM BIGINT NOT NULL,
   OLD_INS_SUBJECT BIT NOT NULL,
   NEW_INS_SUBJECT BIT NOT NULL,
   TAX_SUBJECT_SNAPSHOT BIT NOT NULL,
   APPLIED_BY INT NOT NULL,
   APPLIED_AT DATETIME2 NOT NULL CONSTRAINT DF_PAY2_INS_OPTIN_AT DEFAULT(SYSDATETIME())
 );
ELSE IF COL_LENGTH('dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG','EFFECTIVE_FROM') IS NULL
 ALTER TABLE dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG ADD EFFECTIVE_FROM BIGINT NOT NULL CONSTRAINT DF_PAY2_INS_OPTIN_EFF DEFAULT(0) WITH VALUES;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_PREVIEW_SANOVAT_MIGRATION @EFFECTIVE_FROM BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12
  THROW 51010,N'تاریخ اثر باید تاریخ شمسی معتبر در روز اول ماه (سال 1300 تا 1600 و ماه 1 تا 12) باشد.',1;
 DECLARE @ItemId INT=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='SANOVAT_PAYE');
 SELECT D.DEC_ID,D.EMP_ID,E.EMP_CODE,E.LAST_NAME+N' '+E.FIRST_NAME FULL_NAME,D.EFF_FROM,D.EFF_TO,@EFFECTIVE_FROM NEW_EFFECTIVE_FROM,
   I.SOURCE_RAIL,I.NOMINAL_SENIORITY_DAILY,I.OFFICIAL_SENIORITY_DAILY,I.IS_APPROVED,I.SOURCE_NOTE,
   BN.AMOUNT NOMINAL_BASE_BEFORE,
   CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN BN.AMOUNT-I.NOMINAL_SENIORITY_DAILY ELSE BN.AMOUNT END NOMINAL_BASE_AFTER,
   CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN BN.AMOUNT-I.NOMINAL_SENIORITY_DAILY+I.NOMINAL_SENIORITY_DAILY ELSE BN.AMOUNT END NOMINAL_TOTAL_AFTER,
   BO.AMOUNT OFFICIAL_BASE_BEFORE,
   CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN BO.AMOUNT-I.OFFICIAL_SENIORITY_DAILY ELSE BO.AMOUNT END OFFICIAL_BASE_AFTER,
   CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN BO.AMOUNT-I.OFFICIAL_SENIORITY_DAILY+I.OFFICIAL_SENIORITY_DAILY ELSE BO.AMOUNT END OFFICIAL_TOTAL_AFTER,
   CASE
    WHEN I.DEC_ID IS NULL THEN N'مبلغ واقعی سنوات و ریل منبع ثبت نشده؛ اعمال ممنوع'
    WHEN I.SOURCE_RAIL IS NULL OR I.SOURCE_RAIL NOT IN('NOMINAL','OFFICIAL','BOTH') THEN N'ریل منبع باید NOMINAL، OFFICIAL یا BOTH باشد'
    WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') AND (BN.AMOUNT IS NULL OR I.NOMINAL_SENIORITY_DAILY IS NULL) THEN N'پایه یا سنوات اسمی ناقص است'
    WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') AND (BO.AMOUNT IS NULL OR I.OFFICIAL_SENIORITY_DAILY IS NULL) THEN N'پایه یا سنوات رسمی ناقص است'
    WHEN ISNULL(I.NOMINAL_SENIORITY_DAILY,0)<0 OR ISNULL(I.OFFICIAL_SENIORITY_DAILY,0)<0 THEN N'سنوات منفی مجاز نیست'
    WHEN ISNULL(I.NOMINAL_SENIORITY_DAILY,0)>ISNULL(BN.AMOUNT,0) OR ISNULL(I.OFFICIAL_SENIORITY_DAILY,0)>ISNULL(BO.AMOUNT,0) THEN N'سنوات از پایه ریل مربوط بیشتر است'
    WHEN EXISTS(SELECT 1 FROM dbo.PAY2_RUN R JOIN dbo.PAY2_PERIOD P ON P.PER_ID=R.PER_ID JOIN dbo.PAY2_RUN_LINE RL ON RL.RUN_ID=R.RUN_ID WHERE RL.EMP_ID=D.EMP_ID AND R.STATUS IN(2,3) AND P.PERIOD_DATE/100>=@EFFECTIVE_FROM/100) THEN N'Run قطعی/سندشده در یا پس از تاریخ اثر وجود دارد'
    WHEN EXISTS(SELECT 1 FROM dbo.PAY2_DECREE NX WHERE NX.EMP_ID=D.EMP_ID AND NX.DEC_ID<>D.DEC_ID AND NX.EFF_FROM>=@EFFECTIVE_FROM) THEN N'حکم جدیدتر از تاریخ اثر وجود دارد'
    WHEN D.EFF_FROM=@EFFECTIVE_FROM THEN N'تاریخ اثر با شروع حکم جاری برابر است؛ اعمال ممنوع'
    WHEN I.IS_APPROVED=0 THEN N'در انتظار تأیید'
    WHEN L.DEC_ID IS NOT NULL THEN N'قبلاً اعمال شده'
    WHEN S.ITEM_ID IS NOT NULL THEN N'پایه سنوات از قبل در حکم وجود دارد'
    ELSE N'آماده ایجاد حکم جدید' END MIGRATION_STATUS
 FROM dbo.PAY2_DECREE D
 JOIN dbo.PAY2_EMPLOYEE E ON E.EMP_ID=D.EMP_ID
 LEFT JOIN dbo.PAY2_SANOVAT_MIGRATION_INPUT I ON I.DEC_ID=D.DEC_ID
 LEFT JOIN dbo.PAY2_DECREE_LINE BN ON BN.DEC_ID=D.DEC_ID AND BN.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL')
 LEFT JOIN dbo.PAY2_DECREE_LINE BO ON BO.DEC_ID=D.DEC_ID AND BO.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL_B')
 LEFT JOIN dbo.PAY2_DECREE_LINE S ON S.DEC_ID=D.DEC_ID AND S.ITEM_ID=@ItemId
 LEFT JOIN dbo.PAY2_SANOVAT_MIGRATION_LOG L ON L.DEC_ID=D.DEC_ID
 WHERE D.IS_CONFIRMED=1 AND D.EFF_FROM<=@EFFECTIVE_FROM AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
 ORDER BY E.LAST_NAME,E.FIRST_NAME,D.EFF_FROM;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_APPLY_SANOVAT_MIGRATION @EFFECTIVE_FROM BIGINT,@APPLIED_BY INT=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12 THROW 51010,N'تاریخ اثر باید تاریخ شمسی معتبر در روز اول ماه (سال 1300 تا 1600 و ماه 1 تا 12) باشد.',1;

 IF @APPLIED_BY IS NULL THROW 51012,N'شناسه کاربر اعمال‌کننده برای ثبت حسابرسی الزامی است.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @LockResult INT;
  EXEC @LockResult=sys.sp_getapplock @Resource=N'PAY2_SANOVAT_MIGRATION',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 51011,N'Migration سنوات هم‌اکنون در اجرای دیگری فعال است.',1;
  DECLARE @ItemId INT=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WITH(UPDLOCK,HOLDLOCK) WHERE ITEM_CODE='SANOVAT_PAYE');
  IF @ItemId IS NULL THROW 51000,N'آیتم SANOVAT_PAYE نصب نشده است.',1;
  IF EXISTS(SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I WITH(UPDLOCK,HOLDLOCK) JOIN dbo.PAY2_DECREE D WITH(UPDLOCK,HOLDLOCK) ON D.DEC_ID=I.DEC_ID WHERE I.IS_APPROVED=1 AND D.EFF_FROM=@EFFECTIVE_FROM) THROW 51005,N'تاریخ اثر با تاریخ شروع حکم جاری برابر است؛ برای جلوگیری از بازه نامعتبر اعمال متوقف شد.',1;
  SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_LOG WITH(UPDLOCK,HOLDLOCK) WHERE 1=0;
  IF EXISTS(
    SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I
    JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID AND D.IS_CONFIRMED=1 AND D.EFF_FROM<=@EFFECTIVE_FROM AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
    LEFT JOIN dbo.PAY2_DECREE_LINE BN ON BN.DEC_ID=I.DEC_ID AND BN.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL')
    LEFT JOIN dbo.PAY2_DECREE_LINE BO ON BO.DEC_ID=I.DEC_ID AND BO.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL_B')
    WHERE I.IS_APPROVED=1 AND (I.SOURCE_RAIL IS NULL OR I.SOURCE_RAIL NOT IN('NOMINAL','OFFICIAL','BOTH')
      OR (I.SOURCE_RAIL IN('NOMINAL','BOTH') AND (BN.AMOUNT IS NULL OR I.NOMINAL_SENIORITY_DAILY IS NULL OR I.NOMINAL_SENIORITY_DAILY<0 OR I.NOMINAL_SENIORITY_DAILY>BN.AMOUNT))
      OR (I.SOURCE_RAIL IN('OFFICIAL','BOTH') AND (BO.AMOUNT IS NULL OR I.OFFICIAL_SENIORITY_DAILY IS NULL OR I.OFFICIAL_SENIORITY_DAILY<0 OR I.OFFICIAL_SENIORITY_DAILY>BO.AMOUNT))))
   THROW 51001,N'ریل منبع یا مبلغ سنوات کامل/معتبر نیست؛ ابتدا Preview را بررسی کنید.',1;
  IF EXISTS(SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID JOIN dbo.PAY2_RUN_LINE RL ON RL.EMP_ID=D.EMP_ID JOIN dbo.PAY2_RUN R ON R.RUN_ID=RL.RUN_ID JOIN dbo.PAY2_PERIOD P ON P.PER_ID=R.PER_ID WHERE I.IS_APPROVED=1 AND R.STATUS IN(2,3) AND P.PERIOD_DATE/100>=@EFFECTIVE_FROM/100)
   THROW 51003,N'Run قطعی یا سندشده در/پس از تاریخ اثر وجود دارد؛ Migration مجاز نیست.',1;
  IF EXISTS(SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID JOIN dbo.PAY2_DECREE NX ON NX.EMP_ID=D.EMP_ID AND NX.DEC_ID<>D.DEC_ID AND NX.EFF_FROM>=@EFFECTIVE_FROM WHERE I.IS_APPROVED=1)
   THROW 51004,N'برای حداقل یک پرسنل حکم جدیدتر از تاریخ اثر وجود دارد.',1;

  DECLARE @PrevDate BIGINT,@Y INT=@EFFECTIVE_FROM/10000,@M INT=(@EFFECTIVE_FROM/100)%100;
  SET @PrevDate=CASE WHEN @M=1 THEN (@Y-1)*10000+1200+CASE WHEN ((25*(@Y-1)+11)%33)<8 THEN 30 ELSE 29 END WHEN @M<=7 THEN @Y*10000+(@M-1)*100+31 ELSE @Y*10000+(@M-1)*100+30 END;
  DECLARE @W TABLE(DEC_ID INT PRIMARY KEY,SOURCE_RAIL NVARCHAR(10),NB DECIMAL(18,2),NS DECIMAL(18,2),NA DECIMAL(18,2),OB DECIMAL(18,2),OS DECIMAL(18,2),OA DECIMAL(18,2));
  INSERT @W SELECT I.DEC_ID,I.SOURCE_RAIL,BN.AMOUNT,CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN I.NOMINAL_SENIORITY_DAILY ELSE 0 END,CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN BN.AMOUNT-I.NOMINAL_SENIORITY_DAILY ELSE BN.AMOUNT END,BO.AMOUNT,CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN I.OFFICIAL_SENIORITY_DAILY ELSE 0 END,CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN BO.AMOUNT-I.OFFICIAL_SENIORITY_DAILY ELSE BO.AMOUNT END
  FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID AND D.IS_CONFIRMED=1 AND D.EFF_FROM<=@EFFECTIVE_FROM AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
  LEFT JOIN dbo.PAY2_DECREE_LINE BN ON BN.DEC_ID=I.DEC_ID AND BN.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL') LEFT JOIN dbo.PAY2_DECREE_LINE BO ON BO.DEC_ID=I.DEC_ID AND BO.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL_B') LEFT JOIN dbo.PAY2_DECREE_LINE S ON S.DEC_ID=I.DEC_ID AND S.ITEM_ID=@ItemId LEFT JOIN dbo.PAY2_SANOVAT_MIGRATION_LOG L ON L.DEC_ID=I.DEC_ID WHERE I.IS_APPROVED=1 AND S.ITEM_ID IS NULL AND L.DEC_ID IS NULL;
  IF EXISTS(SELECT 1 FROM @W WHERE (NB IS NOT NULL AND NB<>NA+NS) OR (OB IS NOT NULL AND OB<>OA+OS)) THROW 51002,N'کنترل مستقل تساوی ریل اسمی/رسمی ناموفق بود.',1;

  DECLARE @Old INT,@New INT,@Rail NVARCHAR(10),@NB DECIMAL(18,2),@NS DECIMAL(18,2),@NA DECIMAL(18,2),@OB DECIMAL(18,2),@OS DECIMAL(18,2),@OA DECIMAL(18,2);
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT DEC_ID,SOURCE_RAIL,NB,NS,NA,OB,OS,OA FROM @W; OPEN C; FETCH NEXT FROM C INTO @Old,@Rail,@NB,@NS,@NA,@OB,@OS,@OA;
  WHILE @@FETCH_STATUS=0
  BEGIN
   INSERT dbo.PAY2_DECREE(EMP_ID,WS_ID,ISSUED_DATE,EFF_FROM,EFF_TO,EDU_LEVEL,MARITAL,IS_MANAGER,TMPL_ID,IS_CONFIRMED,CONFIRMED_BY,CONFIRMED_AT,CREATED_AT,CREATED_BY,NOTES,SHIFT_MODE)
    SELECT EMP_ID,WS_ID,@EFFECTIVE_FROM,@EFFECTIVE_FROM,EFF_TO,EDU_LEVEL,MARITAL,IS_MANAGER,TMPL_ID,1,@APPLIED_BY,SYSDATETIME(),SYSDATETIME(),@APPLIED_BY,CONCAT(ISNULL(NOTES,N''),N' | تفکیک پایه سنوات از حکم ',DEC_ID),SHIFT_MODE FROM dbo.PAY2_DECREE WHERE DEC_ID=@Old;
   SET @New=SCOPE_IDENTITY();
   INSERT dbo.PAY2_DECREE_LINE(DEC_ID,ITEM_ID,AMOUNT,INS_OV,TAX_OV,BASIS_OV,SHIFT_MODE_OV,NOMINAL_AMOUNT_OV,OFFICIAL_AMOUNT_OV)
    SELECT @New,ITEM_ID,AMOUNT,INS_OV,TAX_OV,BASIS_OV,SHIFT_MODE_OV,NOMINAL_AMOUNT_OV,OFFICIAL_AMOUNT_OV FROM dbo.PAY2_DECREE_LINE WHERE DEC_ID=@Old;
   UPDATE L SET AMOUNT=@NA FROM dbo.PAY2_DECREE_LINE L JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=L.ITEM_ID WHERE L.DEC_ID=@New AND I.ITEM_CODE='BASE_SAL' AND @Rail IN('NOMINAL','BOTH');
   UPDATE L SET AMOUNT=@OA FROM dbo.PAY2_DECREE_LINE L JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=L.ITEM_ID WHERE L.DEC_ID=@New AND I.ITEM_CODE='BASE_SAL_B' AND @Rail IN('OFFICIAL','BOTH');
   INSERT dbo.PAY2_DECREE_LINE(DEC_ID,ITEM_ID,AMOUNT,NOMINAL_AMOUNT_OV,OFFICIAL_AMOUNT_OV) VALUES(@New,@ItemId,@OS,@NS,@OS);
   UPDATE dbo.PAY2_DECREE SET EFF_TO=@PrevDate WHERE DEC_ID=@Old AND (EFF_TO IS NULL OR EFF_TO>=@EFFECTIVE_FROM);
   INSERT dbo.PAY2_SANOVAT_MIGRATION_LOG(DEC_ID,NEW_DEC_ID,EFFECTIVE_FROM,SOURCE_RAIL,NOMINAL_BASE_BEFORE,NOMINAL_SENIORITY,NOMINAL_BASE_AFTER,OFFICIAL_BASE_BEFORE,OFFICIAL_SENIORITY,OFFICIAL_BASE_AFTER,APPLIED_BY) VALUES(@Old,@New,@EFFECTIVE_FROM,@Rail,@NB,@NS,@NA,@OB,@OS,@OA,@APPLIED_BY);
   FETCH NEXT FROM C INTO @Old,@Rail,@NB,@NS,@NA,@OB,@OS,@OA;
  END; CLOSE C; DEALLOCATE C;
  COMMIT; SELECT COUNT(*) APPLIED_COUNT FROM @W;
 END TRY BEGIN CATCH IF CURSOR_STATUS('local','C')>=0 CLOSE C; IF CURSOR_STATUS('local','C')>-3 DEALLOCATE C; IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_PREVIEW_INS_NON_SUBJECT_OPTIN @EFFECTIVE_FROM BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12
  THROW 51105,N'تاریخ اثر باید روز اول یک ماه شمسی معتبر باشد.',1;
 SELECT DB_NAME() DATABASE_NAME,C.CFG_VALUE OPT_IN_VALUE,
        CASE WHEN C.CFG_VALUE=N'APPROVED:'+DB_NAME() THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END IS_TARGET_APPROVED,
        @EFFECTIVE_FROM EFFECTIVE_FROM,
        TRY_CAST((SELECT CFG_VALUE FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM') AS BIGINT) CURRENT_RULE_EFFECTIVE_FROM
 FROM dbo.PAY2_CONFIG C WHERE C.CFG_KEY=N'INS_NON_SUBJECT_OPT_IN';

 SELECT I.ITEM_ID,I.ITEM_CODE,I.ITEM_NAME,I.INS_SUBJECT CURRENT_INS_SUBJECT,I.TAX_SUBJECT UNCHANGED_TAX_SUBJECT
 FROM dbo.PAY2_ITEM_DEF I WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') ORDER BY I.ITEM_CODE;

 -- همه Overrideها نمایش داده می‌شوند؛ IS_BLOCKING فقط جاری/آینده را مشخص می‌کند.
 SELECT N'DECREE' OVERRIDE_SCOPE,D.EMP_ID,DL.DEC_ID,CAST(DL.DEC_ID AS NVARCHAR(80)) REF_KEY,I.ITEM_CODE,DL.INS_OV,
        D.EFF_FROM VALID_FROM,D.EFF_TO VALID_TO,
        CAST(CASE WHEN D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM THEN 1 ELSE 0 END AS bit) IS_BLOCKING
 FROM dbo.PAY2_DECREE_LINE DL JOIN dbo.PAY2_DECREE D ON D.DEC_ID=DL.DEC_ID JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=DL.ITEM_ID
 WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND DL.INS_OV IS NOT NULL
 UNION ALL
 SELECT N'EMPLOYEE',O.EMP_ID,NULL,CONCAT(O.EMP_ID,N':',O.ITEM_ID,N':',O.VALID_FROM),I.ITEM_CODE,O.INS_OV,
        O.VALID_FROM,O.VALID_TO,CAST(CASE WHEN O.VALID_TO IS NULL OR O.VALID_TO>=@EFFECTIVE_FROM THEN 1 ELSE 0 END AS bit)
 FROM dbo.PAY2_OVERRIDE O JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=O.ITEM_ID
 WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND O.INS_OV IS NOT NULL
 UNION ALL
 SELECT N'TEMPLATE',NULL,NULL,CONCAT(TL.TMPL_ID,N':',TL.ITEM_ID),I.ITEM_CODE,TL.INS_OV,
        NULL,NULL,CAST(T.IS_ACTIVE AS bit)
 FROM dbo.PAY2_ITEM_TMPL_LINE TL JOIN dbo.PAY2_ITEM_TEMPLATE T ON T.TMPL_ID=TL.TMPL_ID JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=TL.ITEM_ID
 WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND TL.INS_OV IS NOT NULL
 ORDER BY OVERRIDE_SCOPE,ITEM_CODE,REF_KEY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_APPLY_INS_NON_SUBJECT_OPTIN
 @CONFIRM_DATABASE SYSNAME,@EFFECTIVE_FROM BIGINT,@APPLIED_BY INT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @LockResult INT;
  EXEC @LockResult=sys.sp_getapplock @Resource=N'PAY2_INS_NON_SUBJECT_OPTIN',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 51104,N'اعمال Opt-in هم‌اکنون در اجرای دیگری فعال است.',1;

  -- تمام Validationهای قابل تغییر پس از قفل و داخل همان Transaction انجام می‌شوند.
  IF @APPLIED_BY IS NULL THROW 51100,N'شناسه کاربر اعمال‌کننده الزامی است.',1;
  IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12
   THROW 51105,N'تاریخ اثر باید روز اول یک ماه شمسی معتبر باشد.',1;
  IF @CONFIRM_DATABASE IS NULL OR @CONFIRM_DATABASE<>DB_NAME() THROW 51101,N'نام دیتابیس تأییدشده با دیتابیس جاری یکسان نیست.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WITH(UPDLOCK,HOLDLOCK) WHERE CFG_KEY=N'INS_NON_SUBJECT_OPT_IN' AND CFG_VALUE=N'APPROVED:'+DB_NAME())
   THROW 51102,N'Opt-in این دیتابیس فعال نیست؛ ابتدا Preview و سپس مقدار APPROVED:<DatabaseName> را با تأیید صریح ثبت کنید.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WITH(UPDLOCK,HOLDLOCK) WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM')
   THROW 51106,N'تنظیم تاریخ اثر نصب نشده است؛ ابتدا Updater را کامل اجرا کنید.',1;
  IF (SELECT COUNT(*) FROM dbo.PAY2_ITEM_DEF WITH(UPDLOCK,HOLDLOCK) WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN'))<>4
   THROW 51107,N'هر چهار آیتم SHIFT، OT_NORMAL، OT_HOLIDAY و OT_ADMIN باید پیش از Apply موجود باشند.',1;

  IF EXISTS
  (
   SELECT 1 FROM dbo.PAY2_DECREE_LINE DL WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.PAY2_DECREE D WITH(UPDLOCK,HOLDLOCK) ON D.DEC_ID=DL.DEC_ID
   JOIN dbo.PAY2_ITEM_DEF I WITH(UPDLOCK,HOLDLOCK) ON I.ITEM_ID=DL.ITEM_ID
   WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND DL.INS_OV=1
     AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
   UNION ALL
   SELECT 1 FROM dbo.PAY2_OVERRIDE O WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.PAY2_ITEM_DEF I WITH(UPDLOCK,HOLDLOCK) ON I.ITEM_ID=O.ITEM_ID
   WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND O.INS_OV=1
     AND (O.VALID_TO IS NULL OR O.VALID_TO>=@EFFECTIVE_FROM)
   UNION ALL
   SELECT 1 FROM dbo.PAY2_ITEM_TMPL_LINE TL WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.PAY2_ITEM_TEMPLATE T WITH(UPDLOCK,HOLDLOCK) ON T.TMPL_ID=TL.TMPL_ID
   JOIN dbo.PAY2_ITEM_DEF I WITH(UPDLOCK,HOLDLOCK) ON I.ITEM_ID=TL.ITEM_ID
   WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND TL.INS_OV=1 AND T.IS_ACTIVE=1
  ) THROW 51103,N'Override مشمول بیمه جاری/آینده وجود دارد؛ Preview را بررسی و Overrideها را صریحاً تعیین تکلیف کنید.',1;

  INSERT dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG(DATABASE_NAME,ITEM_CODE,EFFECTIVE_FROM,OLD_INS_SUBJECT,NEW_INS_SUBJECT,TAX_SUBJECT_SNAPSHOT,APPLIED_BY)
  SELECT DB_NAME(),ITEM_CODE,@EFFECTIVE_FROM,INS_SUBJECT,0,TAX_SUBJECT,@APPLIED_BY FROM dbo.PAY2_ITEM_DEF WITH(UPDLOCK,HOLDLOCK)
  WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN');

  UPDATE dbo.PAY2_CONFIG
  SET CFG_VALUE=CAST(@EFFECTIVE_FROM AS NVARCHAR(20)),CHANGED_BY=@APPLIED_BY,CHANGED_AT=GETDATE(),
      CHANGE_NOTE=CONCAT(N'قاعده عدم مشمولیت SHIFT/OT از ',@EFFECTIVE_FROM,N' برای دیتابیس ',DB_NAME(),N' زمان‌بندی شد.')
  WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM';

  -- مجوز یک‌بارمصرف است و در همان تراکنش مصرف می‌شود؛ TAX_SUBJECT و تاریخچه Run دست‌نخورده می‌مانند.
  UPDATE dbo.PAY2_CONFIG
  SET CFG_VALUE=N'DISABLED',CHANGED_BY=@APPLIED_BY,CHANGED_AT=GETDATE(),
      CHANGE_NOTE=CONCAT(N'Opt-in بیمه برای SHIFT/OT در تاریخ اثر ',@EFFECTIVE_FROM,N' روی دیتابیس ',DB_NAME(),N' اعمال و مجوز مصرف شد.')
  WHERE CFG_KEY=N'INS_NON_SUBJECT_OPT_IN';

  COMMIT;
  SELECT ITEM_CODE,INS_SUBJECT BASE_INS_SUBJECT,TAX_SUBJECT,@EFFECTIVE_FROM EFFECTIVE_FROM,CAST(0 AS bit) EFFECTIVE_INS_SUBJECT
  FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') ORDER BY ITEM_CODE;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
");

                // ===========================================================
                // 3. Stored Procedures â CREATE OR ALTER (همیشه آخرین نسخه)
                // ===========================================================
                string procScript = @"
-- ================================================================
-- PAY2 — Stored Procedures & Business Logic — v6.0
-- نرم‌افزار مستر کارکت | کد: PAY2-DB-006
-- ================================================================
-- این فایل باید پس از PAY2_DDL_v6.sql اجرا شود.
--
-- محتوا:
--   1. SP_PAY2_CALC_RUN   — موتور محاسبه حقوق ماهیانه (۱۲ گام)
--   2. SP_PAY2_GEN_DEED   — تولید سند حسابداری حقوق و بیمه
--   3. SP_PAY2_CALC_SETTLE — محاسبه تسویه حساب پرسنل
--   4. SP_PAY2_GEN_DEED_SETTLE — تولید سند حسابداری تسویه
--   5. SP_PAY2_CLOSE_PERIOD   — بستن دوره و کنترل نهایی
--   6. SP_PAY2_REVERT_RUN     — برگشت محاسبه (bak به پیش‌نویس)
-- ================================================================

SET NOCOUNT ON;
GO

-- ================================================================
-- پارامترها:
--   @WS_ID        : شناسه کارگاه
--   @PER_ID       : شناسه دوره (از PAY2_PERIOD)
--   @PAYROLL_N_S  : شماره سند حقوق در DEED_HED (برای مساعده)
--   @CALC_BY      : کد کاربر محاسبه‌گر
--   @IS_RERUN     : 0=اول بار | 1=بازمحاسبه (RUN_NO جدید ایجاد می‌کند)
-- خروجی:
--   @NEW_RUN_ID   OUTPUT — شناسه PAY2_RUN ایجادشده
-- ================================================================
-- ================================================================
-- ۱. SP_PAY2_CALC_RUN — موتور محاسبه حقوق ماهیانه
-- موتور قطعی دو ریل مستقل؛ بدون Fallback خاموش بین اسمی و رسمی
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CALC_RUN]
    @WS_ID       INT,
    @PER_ID      INT,
    @PAYROLL_N_S FLOAT,
    @CALC_BY     INT          = NULL,
    @IS_RERUN    BIT          = 0,
    @NEW_RUN_ID  INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET ANSI_WARNINGS OFF;

    -- 🚀 گام صفر: اعلان (DECLARE) تمامی متغیرها در سطح Batch برای جلوگیری از نشت اسکوپ در T-SQL
    DECLARE
        @MONTH_DAYS_MODE NVARCHAR(10), @MONTH_DAYS TINYINT,
        @OT_NORMAL_MULT DECIMAL(6,4), @OT_HOLIDAY_MULT DECIMAL(6,4),
        @OT_HOUR_BASE DECIMAL(6,4), @SHIFT_MODE NVARCHAR(10),
        @ROUND_MODE INT, @INS_WORKER_RATE DECIMAL(6,4),
        @INS_EMPLOYER_RATE DECIMAL(6,4), @INS_UNEMP_RATE DECIMAL(6,4),
        @INS_CEILING_APPLY BIT, @INS_CEILING BIGINT,
        @TAX_YEAR SMALLINT, @TAX_EXEMPT BIGINT,
        @TAX_DEDUCT_INS BIT, @TAX_DEP_APPLY BIT,
        @ADV_ENABLED BIT, @PERIOD_DATE BIGINT, @INS_NON_SUBJECT_EFFECTIVE_FROM BIGINT,
        @PERIOD_MONTH INT, @PERIOD_YEAR INT,
        @MONTHLY_PRORATE BIT;

    DECLARE @INS_DED_ID INT, @TAX_DED_ID INT, @LOAN_DED_ID INT, @ADV_DED_ID INT;
    DECLARE @PREV_RUN_ID INT, @PREV_STATUS TINYINT, @NEXT_RUN_NO SMALLINT = 1;
    DECLARE @IS_LEAP_YEAR BIT;

    -- متغیرهای حلقه پرسنل
    DECLARE @PER_START BIGINT, @PER_END BIGINT;
    DECLARE @WS_SHIFT_MODE NVARCHAR(10);
    DECLARE @EMP_ID INT, @IS_MANAGER BIT, @INS_TYPE TINYINT, @TAX_EXEMPT_FLAG BIT, @REGION_DEP TINYINT, @ACC_T NVARCHAR(50);
    -- برچسب خوانا برای پیام‌های خطا (کد پرسنلی و نام) و تشخیص دلیل نبود حکم معتبر
    DECLARE @EMP_LABEL NVARCHAR(300), @DEC_REASON NVARCHAR(400);
    DECLARE @DEC_ANY_COUNT INT, @DEC_UNCONFIRMED_COUNT INT, @DEC_CONFIRMED_NO_LINE_COUNT INT;

    DECLARE @WORK_DAYS DECIMAL(5,2), @DAYS DECIMAL(5,2), @DAYSB DECIMAL(5,2),
            @FRID_COUNT TINYINT, @TDAYS DECIMAL(5,2), @OT_NORMAL_H DECIMAL(6,2),
            @OT_HOLIDAY_H DECIMAL(6,2), @OT_ADMIN_H DECIMAL(6,2), @SHORTAGE_H DECIMAL(6,2), @LEAVE_DAYS DECIMAL(5,2),
            @PERF_AMOUNT BIGINT, @TRANSP_AMOUNT BIGINT, @KASR_OTHER BIGINT;

    -- متغیرهای حلقه‌های احکام و اقلام
    DECLARE @DEC_ID INT, @DEC_FROM BIGINT, @DEC_TO BIGINT, @DEC_SHIFT_MODE NVARCHAR(10);
    DECLARE @DEC_ACTUAL_START BIGINT, @DEC_ACTUAL_END BIGINT, @DEC_ACTIVE_DAYS INT, @PRORATE_FACTOR DECIMAL(18,6);

    DECLARE @HAS_BOTH_SAL BIT, @HAS_NOMINAL_RATE BIT, @HAS_OFFICIAL_RATE BIT, @DAILY_NOMINAL DECIMAL(18,2), @DAILY_OFFICIAL DECIMAL(18,2), @DAILY_SEN_NOMINAL DECIMAL(18,2), @DAILY_SEN_OFFICIAL DECIMAL(18,2);
    DECLARE @INS_OFFICIAL_VALID BIT, @TAX_OFFICIAL_VALID BIT, @INS_DROP_SAL NVARCHAR(30), @TAX_DROP_SAL NVARCHAR(30);

    DECLARE @ITEM_ID INT, @ITEM_CODE NVARCHAR(30), @ITEM_TYPE TINYINT, @ITEM_AMOUNT DECIMAL(18,2),
            @ITEM_BASIS TINYINT, @ITEM_INS BIT, @ITEM_TAX BIT, @ITEM_PBD TINYINT, @ITEM_IBD TINYINT, @DL_SHIFT_MODE_OV NVARCHAR(10),
            @DL_NOMINAL_AMOUNT_OV DECIMAL(18,2), @DL_OFFICIAL_AMOUNT_OV DECIMAL(18,2);
    DECLARE @OV_INS BIT, @OV_TAX BIT, @OV_BASIS TINYINT;
    DECLARE @CALC_AMOUNT BIGINT, @INS_CALC_AMOUNT BIGINT;
    DECLARE @PAY_DAYS DECIMAL(18,6), @BASE_DAYS_RAW DECIMAL(5,2), @INS_DAYS DECIMAL(18,6), @INS_DAYS_RAW DECIMAL(5,2);
    DECLARE @FULL_MONTH BIGINT, @FULL_MONTH_INS BIGINT, @NAHAR_DAYS DECIMAL(18,6), @EFF_SHIFT_MODE NVARCHAR(10);

    -- متغیرهای محاسباتی نهایی
    DECLARE @TOTAL_NOMINAL_BASE BIGINT, @TOTAL_OFFICIAL_BASE BIGINT;
    DECLARE @EFFECTIVE_HOURLY DECIMAL(18,2), @OFFICIAL_HOURLY DECIMAL(18,2);

    DECLARE @GROSS_PAY BIGINT, @NOMINAL_GROSS BIGINT, @INS_BASE BIGINT, @INS_WORKER BIGINT, @INS_EMPLOYER BIGINT, @INS_EMPLOYER_BASE BIGINT, @INS_UNEMPLOYMENT BIGINT;
    DECLARE @EFFECTIVE_INS_CEILING BIGINT, @EMP_IS_JANBAZ BIT, @JANBAZ_RATE DECIMAL(6,4);
    DECLARE @TAX_BASE BIGINT, @TAX_AMOUNT BIGINT;
    DECLARE @ADVANCE_DED BIGINT, @LOAN_DED BIGINT, @OTHER_DED BIGINT, @SHORTAGE_DED BIGINT, @TOTAL_DED BIGINT, @NET_PAY BIGINT;
    DECLARE @LEAVE_BAL_DAYS DECIMAL(5,2), @LOAN_BAL BIGINT, @LEAVE_MIN_USED INT;

    DECLARE @ItemCalc TABLE (
        ITEM_ID INT, ITEM_CODE NVARCHAR(30), ITEM_TYPE TINYINT,
        AMOUNT BIGINT, INS_AMOUNT BIGINT, INS_SUBJECT BIT, TAX_SUBJECT BIT
    );

    -- گام ۱ — بارگذاری تنظیمات
    SELECT
        @MONTH_DAYS_MODE   = ISNULL(MAX(CASE WHEN CFG_KEY='MONTH_DAYS_MODE'    THEN CFG_VALUE END), '30'),
        @OT_NORMAL_MULT    = ISNULL(MAX(CASE WHEN CFG_KEY='OT_NORMAL_MULT'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOLIDAY_MULT   = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOLIDAY_MULT'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOUR_BASE      = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOUR_BASE'       THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 7.33),
        @SHIFT_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='SHIFT_MODE'         THEN CFG_VALUE END), 'PCT'),
        @ROUND_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='ROUND_MODE'         THEN CAST(CFG_VALUE AS INT) END), 1),
        @INS_WORKER_RATE   = ISNULL(MAX(CASE WHEN CFG_KEY='INS_WORKER_RATE'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.07),
        @INS_EMPLOYER_RATE = ISNULL(MAX(CASE WHEN CFG_KEY='INS_EMPLOYER_RATE'  THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.20),
        @INS_UNEMP_RATE    = ISNULL(MAX(CASE WHEN CFG_KEY='INS_UNEMP_RATE'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.03),
        @INS_CEILING       = ISNULL(MAX(CASE WHEN CFG_KEY='INS_CEILING_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 999999999),
        @TAX_YEAR          = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'           THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT        = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @INS_CEILING_APPLY = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='INS_CEILING_APPLY'  THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEDUCT_INS    = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEDUCT_INS'     THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEP_APPLY     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEPRIVATION_APPLY' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @ADV_ENABLED       = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='ADV_ENABLED'        THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @MONTHLY_PRORATE   = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='MONTHLY_ITEM_PRORATE' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @INS_NON_SUBJECT_EFFECTIVE_FROM = ISNULL(MAX(CASE WHEN CFG_KEY='INS_NON_SUBJECT_EFFECTIVE_FROM' THEN TRY_CAST(CFG_VALUE AS BIGINT) END),0)
    FROM PAY2_CONFIG;

    SELECT @PERIOD_DATE = PERIOD_DATE
    FROM PAY2_PERIOD WITH (UPDLOCK, HOLDLOCK)
    WHERE PER_ID = @PER_ID AND WS_ID = @WS_ID;
    IF @PERIOD_DATE IS NULL
    BEGIN
        RAISERROR(N'دوره %d به کارگاه %d تعلق ندارد یا یافت نشد.', 16, 1, @PER_ID, @WS_ID);
        RETURN;
    END;

    SET @PERIOD_MONTH = (@PERIOD_DATE / 100) % 100;
    SET @PERIOD_YEAR  = @PERIOD_DATE / 10000;
    SET @IS_LEAP_YEAR = CASE WHEN ((25 * @PERIOD_YEAR + 11) % 33) < 8 THEN 1 ELSE 0 END;

    SET @MONTH_DAYS = CASE
        WHEN @MONTH_DAYS_MODE = '30' THEN 30
        WHEN @PERIOD_MONTH <= 6 THEN 31
        WHEN @PERIOD_MONTH BETWEEN 7 AND 11 THEN 30
        WHEN @PERIOD_MONTH = 12 AND @IS_LEAP_YEAR = 1 THEN 30
        ELSE 29
    END;

    -- محاسبه و Snapshot از یک تصویر ثابت از Masterها استفاده می‌کنند.
    SELECT I.ITEM_ID,I.ITEM_CODE,I.ITEM_NAME,I.ITEM_TYPE,I.CALC_BASIS,I.INS_SUBJECT,I.TAX_SUBJECT,
           I.PAY_BASE_DAYS,I.INS_BASE_DAYS,I.IS_ACTIVE,I.SORT_ORDER
    INTO #ItemDefSource
    FROM PAY2_ITEM_DEF I;

    SELECT E.EMP_ID,E.EMP_CODE,E.FIRST_NAME,E.LAST_NAME,E.FATHER_NAME,E.NATIONAL_CODE,E.INS_CODE,
           E.ID_NUMBER,E.BIRTH_PLACE,E.BIRTH_DATE,E.GENDER,ISNULL(E.INS_TYPE,1) INS_TYPE,
           ISNULL(E.TAX_EXEMPT,0) TAX_EXEMPT,E.MARITAL,E.NATIONALITY,E.MOBILE,E.HIRE_DATE,E.FIRE_DATE,
           E.IS_MANAGER,ISNULL(E.IS_JANBAZ,0) IS_JANBAZ,E.REGION_DEPRIVATION,E.ACC_T,
           J.JOB_CODE,J.JOB_NAME
    INTO #EmployeeSource
    FROM PAY2_EMPLOYEE E
    LEFT JOIN PAY2_JOB J ON J.JOB_ID=E.JOB_ID
    WHERE E.WS_ID=@WS_ID AND E.IS_ACTIVE=1
      AND EXISTS(SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID=@PER_ID AND A.EMP_ID=E.EMP_ID);

    SET @INS_DED_ID  = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='INS_DED');
    SET @TAX_DED_ID  = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='TAX_DED');
    SET @LOAN_DED_ID = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='LOAN_DED');
    SET @ADV_DED_ID  = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='ADVANCE_DED');

    -- گام ۲ — ایجاد هدر PAY2_RUN
    IF @IS_RERUN = 1
    BEGIN
        SELECT TOP 1 @PREV_RUN_ID = RUN_ID, @NEXT_RUN_NO = RUN_NO + 1, @PREV_STATUS = STATUS
        FROM PAY2_RUN WHERE PER_ID = @PER_ID AND IS_LATEST = 1 ORDER BY RUN_NO DESC;

        IF @PREV_STATUS >= 2
        BEGIN
            RAISERROR(N'اجرای قبلی تأیید نهایی شده است. دیتابیس اجازه بازمحاسبه را نمی‌دهد.', 16, 1);
            RETURN;
        END

        IF @PREV_RUN_ID IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM PAY2_RUN WHERE RUN_ID = @PREV_RUN_ID AND STATUS = 1)
               AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @PREV_RUN_ID)
            BEGIN
                EXEC SP_PAY2_REVERT_RUN @RUN_ID = @PREV_RUN_ID, @REVERT_BY = @CALC_BY;
            END
        END

        UPDATE PAY2_RUN SET IS_LATEST = 0 WHERE PER_ID = @PER_ID;
    END;

    INSERT INTO PAY2_RUN (PER_ID, RUN_NO, IS_LATEST, CALC_AT, CALC_BY, STATUS, PREV_RUN_ID, PAYROLL_ENGINE_VERSION, WS_ID_SNAP)
    VALUES (@PER_ID, @NEXT_RUN_NO, 1, GETDATE(), @CALC_BY, 1, @PREV_RUN_ID, 3, @WS_ID);

    SET @NEW_RUN_ID = SCOPE_IDENTITY();

    CREATE TABLE #AdvResult (EMP_ID INT, PCODE NVARCHAR(50), FULL_NAME NVARCHAR(150), RAW_BALANCE BIGINT, MANUAL_EXCL BIGINT, ADVANCE_DEDUCTION BIGINT);
    IF @ADV_ENABLED = 1
    BEGIN
        INSERT INTO #AdvResult (EMP_ID, PCODE, FULL_NAME, RAW_BALANCE, MANUAL_EXCL, ADVANCE_DEDUCTION)
        EXEC SP_PAY2_GET_ADVANCES @PERIOD_DATE = @PERIOD_DATE, @PAYROLL_N_S = @PAYROLL_N_S, @WS_ID = @WS_ID;
    END;

    SELECT @WS_SHIFT_MODE = NULLIF(SHIFT_MODE, N'') FROM PAY2_WORKSHOP WHERE WS_ID = @WS_ID;

    DECLARE cur_emp CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
        SELECT E.EMP_ID, E.INS_TYPE, E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.ACC_T
        FROM #EmployeeSource E;

    OPEN cur_emp;
    FETCH NEXT FROM cur_emp INTO @EMP_ID, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;

    -- گام ۳ — حلقه روی پرسنل فعال کارگاه
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DELETE FROM @ItemCalc;

        -- 🚀 ریست صریح مقادیر در هر چرخش حلقه پرسنل
        SET @HAS_BOTH_SAL = 0; SET @HAS_NOMINAL_RATE = 0; SET @HAS_OFFICIAL_RATE = 0;
        SET @DAILY_NOMINAL = 0; SET @DAILY_OFFICIAL = 0; SET @DAILY_SEN_NOMINAL = 0; SET @DAILY_SEN_OFFICIAL = 0;
        SET @TOTAL_NOMINAL_BASE = 0; SET @TOTAL_OFFICIAL_BASE = 0;
        SET @EFFECTIVE_HOURLY = 0; SET @OFFICIAL_HOURLY = 0;

        SELECT
            @WORK_DAYS = ISNULL(WORK_DAYS,0), @DAYS = ISNULL(DAYS,0), @DAYSB = ISNULL(DAYSB,0),
            @FRID_COUNT = ISNULL(FRID_COUNT,0), @TDAYS = ISNULL(TDAYS,0), @OT_NORMAL_H = ISNULL(OT_NORMAL_H,0),
            @OT_HOLIDAY_H = ISNULL(OT_HOLIDAY_H,0), @OT_ADMIN_H = ISNULL(OT_ADMIN_H,0), @SHORTAGE_H = ISNULL(SHORTAGE_H,0), @LEAVE_DAYS = ISNULL(LEAVE_DAYS,0),
            @PERF_AMOUNT = ISNULL(PERF_AMOUNT,0), @TRANSP_AMOUNT = ISNULL(TRANSP_AMOUNT,0), @KASR_OTHER = ISNULL(KASR_OTHER,0)
        FROM PAY2_ATTENDANCE WHERE PER_ID = @PER_ID AND EMP_ID = @EMP_ID;

        SET @PER_START = @PERIOD_DATE + 1;
        SET @PER_END   = @PERIOD_DATE + @MONTH_DAYS;

        SET @EMP_LABEL = NULL;
        SELECT @EMP_LABEL = N'کد پرسنلی ' + ISNULL(E.EMP_CODE, N'-') + N' (' + LTRIM(RTRIM(ISNULL(E.FIRST_NAME, N'') + N' ' + ISNULL(E.LAST_NAME, N''))) + N')'
        FROM #EmployeeSource E WHERE E.EMP_ID = @EMP_ID;
        SET @EMP_LABEL = ISNULL(@EMP_LABEL, N'EMP_ID=' + CAST(@EMP_ID AS NVARCHAR(20)));

        -- وضعیت مدیر از آخرین حکم تأییدشده و مؤثر همین ماه خوانده می‌شود؛ همان مقدار برای بیمه بیکاری و Snapshot استفاده می‌شود.
        SET @IS_MANAGER = 0;
        SELECT TOP 1 @IS_MANAGER=ISNULL(D.IS_MANAGER,0)
        FROM PAY2_DECREE D
        WHERE D.EMP_ID=@EMP_ID AND D.IS_CONFIRMED=1
          AND D.EFF_FROM<=@PER_END AND (D.EFF_TO IS NULL OR D.EFF_TO>=@PER_START)
        ORDER BY D.EFF_FROM DESC,D.DEC_ID DESC;

        DECLARE cur_dec CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
            SELECT DEC_ID, EFF_FROM, ISNULL(EFF_TO, 99991231), NULLIF(SHIFT_MODE, N'')
            FROM PAY2_DECREE
            WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1
              AND EFF_FROM <= @PER_END
              AND (EFF_TO IS NULL OR EFF_TO >= @PER_START)
            ORDER BY EFF_FROM;

        OPEN cur_dec;
        FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @DEC_ACTUAL_START = CASE WHEN @DEC_FROM > @PER_START THEN @DEC_FROM ELSE @PER_START END;
            SET @DEC_ACTUAL_END   = CASE WHEN @DEC_TO < @PER_END THEN @DEC_TO ELSE @PER_END END;
            SET @DEC_ACTIVE_DAYS = 0;

            IF @DEC_ACTUAL_START <= @DEC_ACTUAL_END
                SET @DEC_ACTIVE_DAYS = (@DEC_ACTUAL_END % 100) - (@DEC_ACTUAL_START % 100) + 1;

            IF @DEC_ACTIVE_DAYS > 0
            BEGIN
                SET @PRORATE_FACTOR = CAST(@DEC_ACTIVE_DAYS AS DECIMAL(18,6)) / CAST(@MONTH_DAYS AS DECIMAL(18,6));

                SET @DAILY_NOMINAL=0; SET @DAILY_OFFICIAL=0; SET @DAILY_SEN_NOMINAL=0; SET @DAILY_SEN_OFFICIAL=0;
                SELECT
                    @DAILY_NOMINAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE = 'BASE_SAL' THEN DL.AMOUNT END),0),
                    @DAILY_OFFICIAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE = 'BASE_SAL_B' THEN DL.AMOUNT END),0),
                    @DAILY_SEN_NOMINAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE='SANOVAT_PAYE' THEN ISNULL(DL.NOMINAL_AMOUNT_OV,DL.AMOUNT) END),0),
                    @DAILY_SEN_OFFICIAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE='SANOVAT_PAYE' THEN ISNULL(DL.OFFICIAL_AMOUNT_OV,DL.AMOUNT) END),0)
                FROM PAY2_DECREE_LINE DL INNER JOIN #ItemDefSource ID ON DL.ITEM_ID = ID.ITEM_ID
                WHERE DL.DEC_ID = @DEC_ID;

                IF @DAILY_NOMINAL <= 0 OR @DAILY_OFFICIAL <= 0
                BEGIN
                    DECLARE @MissingDecreeRailMsg NVARCHAR(1000)=N'حکم پرسنل '+@EMP_LABEL+N' ناقص است: مبلغ حقوق پایه در حکم ثبت نشده. لطفاً آیتم‌های «حقوق پایه اسمی (BASE_SAL)» و «حقوق پایه رسمی (BASE_SAL_B)» را در حکم پر و حکم را تأیید کنید. (DEC_ID='+CAST(@DEC_ID AS NVARCHAR(20))+N'، بازه محاسباتی='+CAST(@DEC_ACTUAL_START AS NVARCHAR(20))+N' تا '+CAST(@DEC_ACTUAL_END AS NVARCHAR(20))+N'، BASE_SAL='+CAST(@DAILY_NOMINAL AS NVARCHAR(40))+N'، BASE_SAL_B='+CAST(@DAILY_OFFICIAL AS NVARCHAR(40))+N')';
                    RAISERROR(@MissingDecreeRailMsg,16,1);
                    RETURN;
                END;

                IF @DAILY_NOMINAL > 0 SET @HAS_NOMINAL_RATE = 1;
                IF @DAILY_OFFICIAL > 0 SET @HAS_OFFICIAL_RATE = 1;

                -- ریل‌ها عمداً مستقل‌اند؛ نبود هیچ ریل با دیگری جبران نمی‌شود.
                DECLARE cur_line CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
                    SELECT DL.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, ISNULL(DL.AMOUNT, 0),
                        DL.SHIFT_MODE_OV, DL.NOMINAL_AMOUNT_OV, DL.OFFICIAL_AMOUNT_OV,
                        ISNULL(DL.BASIS_OV, ID.CALC_BASIS), ISNULL(DL.INS_OV, ID.INS_SUBJECT), ISNULL(DL.TAX_OV, ID.TAX_SUBJECT), ID.PAY_BASE_DAYS, ID.INS_BASE_DAYS
                    FROM PAY2_DECREE_LINE DL INNER JOIN #ItemDefSource ID ON DL.ITEM_ID = ID.ITEM_ID
                    WHERE DL.DEC_ID = @DEC_ID AND ID.IS_ACTIVE = 1 AND ID.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                    ORDER BY ID.SORT_ORDER;

                OPEN cur_line;
                FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @DL_NOMINAL_AMOUNT_OV, @DL_OFFICIAL_AMOUNT_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @OV_INS = NULL; SET @OV_TAX = NULL; SET @OV_BASIS = NULL;
                    SELECT TOP 1 @OV_INS = INS_OV, @OV_TAX = TAX_OV, @OV_BASIS = BASIS_OV
                    FROM PAY2_OVERRIDE WHERE EMP_ID = @EMP_ID AND ITEM_ID = @ITEM_ID
                      AND VALID_FROM/100 <= @PERIOD_DATE/100
                      AND (VALID_TO IS NULL OR VALID_TO/100 >= @PERIOD_DATE/100)
                    ORDER BY VALID_FROM DESC;

                    IF @OV_INS IS NOT NULL SET @ITEM_INS = @OV_INS;
                    IF @OV_TAX IS NOT NULL SET @ITEM_TAX = @OV_TAX;
                    IF @OV_BASIS IS NOT NULL SET @ITEM_BASIS = @OV_BASIS;

                    SET @PAY_DAYS      = (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @PRORATE_FACTOR;
                    SET @BASE_DAYS_RAW = (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END);
                    SET @INS_DAYS      = (CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @PRORATE_FACTOR;
                    SET @INS_DAYS_RAW  = (CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END);

                    IF @ITEM_CODE = 'SANOVAT_PAYE'
                    BEGIN
                        SET @CALC_AMOUNT = CAST(ISNULL(@DL_OFFICIAL_AMOUNT_OV, @ITEM_AMOUNT) * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(ISNULL(@DL_NOMINAL_AMOUNT_OV, @ITEM_AMOUNT) * @INS_DAYS AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE IN ('BASE_SAL', 'BASE_SAL_B')
                    BEGIN
                        SET @CALC_AMOUNT     = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE IN ('HOME','CHILDREN','GROCERY')
                    BEGIN
                        SET @FULL_MONTH     = CASE WHEN @BASE_DAYS_RAW >= 28 THEN CAST(@ITEM_AMOUNT AS BIGINT) ELSE CAST(@ITEM_AMOUNT * (@BASE_DAYS_RAW / 30.0) AS BIGINT) END;
                        SET @FULL_MONTH_INS = CASE WHEN @INS_DAYS_RAW  >= 28 THEN CAST(@ITEM_AMOUNT AS BIGINT) ELSE CAST(@ITEM_AMOUNT * (@INS_DAYS_RAW  / 30.0) AS BIGINT) END;
                        SET @CALC_AMOUNT     = CAST(@FULL_MONTH     * @PRORATE_FACTOR AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@FULL_MONTH_INS * @PRORATE_FACTOR AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE = 'NAHAR'
                    BEGIN
                        SET @NAHAR_DAYS = (@DAYSB - @FRID_COUNT - @LEAVE_DAYS + @TDAYS) * @PRORATE_FACTOR;
                        SET @CALC_AMOUNT = CASE WHEN @NAHAR_DAYS > 0 THEN CAST(@ITEM_AMOUNT * @NAHAR_DAYS AS BIGINT) ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT) END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_CODE = 'SHIFT'
                    BEGIN
                        SET @EFF_SHIFT_MODE = COALESCE(NULLIF(@DL_SHIFT_MODE_OV, N''), @DEC_SHIFT_MODE, @WS_SHIFT_MODE, @SHIFT_MODE, 'PCT');
                        IF @EFF_SHIFT_MODE = 'FIXED'
                        BEGIN
                            SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                            SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@INS_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                        END
                        ELSE
                        BEGIN
                            -- حق شیفت پرداختی از رسمی، حق شیفت بیمه/مالیات از اسمی
                            SET @CALC_AMOUNT = CAST(ROUND(((@DAILY_OFFICIAL + @DAILY_SEN_OFFICIAL) * @PAY_DAYS * @ITEM_AMOUNT / 100.0), 0) AS BIGINT);
                            SET @INS_CALC_AMOUNT = CAST(ROUND(((@DAILY_NOMINAL + @DAILY_SEN_NOMINAL) * @INS_DAYS * @ITEM_AMOUNT / 100.0), 0) AS BIGINT);
                        END
                    END
                    ELSE IF @ITEM_BASIS = 3
                    BEGIN
                        SET @CALC_AMOUNT =
                            CASE @ITEM_CODE
                                WHEN 'OT_NORMAL'  THEN CAST(@ITEM_AMOUNT * @OT_NORMAL_H  AS BIGINT)
                                WHEN 'OT_HOLIDAY' THEN CAST(@ITEM_AMOUNT * @OT_HOLIDAY_H AS BIGINT)
                                WHEN 'OT_ADMIN'   THEN CAST(@ITEM_AMOUNT * @OT_ADMIN_H   AS BIGINT)
                                ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS * @OT_HOUR_BASE AS BIGINT)
                            END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_BASIS = 2
                    BEGIN
                        SET @CALC_AMOUNT = CASE
                            WHEN @MONTHLY_PRORATE = 1
                                THEN CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT)
                            ELSE CAST(@ITEM_AMOUNT * @PRORATE_FACTOR AS BIGINT)
                        END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_BASIS = 1
                    BEGIN
                        SET @CALC_AMOUNT     = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END
                    ELSE
                    BEGIN
                        SET @CALC_AMOUNT = ISNULL(@ITEM_AMOUNT, 0);
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END

                    INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
                    VALUES (@ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @CALC_AMOUNT, @INS_CALC_AMOUNT, @ITEM_INS, @ITEM_TAX);

                    FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @DL_NOMINAL_AMOUNT_OV, @DL_OFFICIAL_AMOUNT_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;
                END;
                CLOSE cur_line; DEALLOCATE cur_line;
            END;

            FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;
        END;
        CLOSE cur_dec; DEALLOCATE cur_dec;

        -- هیچ Fallback خاموشی بین BASE_SAL و BASE_SAL_B مجاز نیست.

        IF @HAS_NOMINAL_RATE=1 AND @HAS_OFFICIAL_RATE=1
            SET @HAS_BOTH_SAL = 1;

        IF @HAS_NOMINAL_RATE=0
        BEGIN
            SET @DEC_ANY_COUNT = (SELECT COUNT(*) FROM PAY2_DECREE WHERE EMP_ID=@EMP_ID AND EFF_FROM<=@PER_END AND (EFF_TO IS NULL OR EFF_TO>=@PER_START));
            SET @DEC_UNCONFIRMED_COUNT = (SELECT COUNT(*) FROM PAY2_DECREE WHERE EMP_ID=@EMP_ID AND ISNULL(IS_CONFIRMED,0)=0 AND EFF_FROM<=@PER_END AND (EFF_TO IS NULL OR EFF_TO>=@PER_START));
            SET @DEC_CONFIRMED_NO_LINE_COUNT = (SELECT COUNT(*) FROM PAY2_DECREE D WHERE D.EMP_ID=@EMP_ID AND D.IS_CONFIRMED=1 AND D.EFF_FROM<=@PER_END AND (D.EFF_TO IS NULL OR D.EFF_TO>=@PER_START) AND NOT EXISTS(SELECT 1 FROM PAY2_DECREE_LINE DL WHERE DL.DEC_ID=D.DEC_ID));

            SET @DEC_REASON =
                CASE
                    WHEN @DEC_UNCONFIRMED_COUNT > 0 THEN N'حکم این ماه ثبت شده ولی «تأیید» نشده است؛ حکم را تأیید کنید.'
                    WHEN @DEC_CONFIRMED_NO_LINE_COUNT > 0 THEN N'حکم این ماه هیچ آیتم حقوقی ندارد؛ آیتم‌های حکم (مثلاً از قالب حکم) را ثبت کنید.'
                    WHEN @DEC_ANY_COUNT = 0 THEN N'هیچ حکمی برای این ماه وجود ندارد (حکم قبلی تمام شده است)؛ حکم جدید ثبت و تأیید کنید.'
                    ELSE N'حکم تأییدشده‌ای با بازه معتبر در این ماه یافت نشد؛ تاریخ شروع/پایان حکم را بررسی کنید.'
                END;

            DECLARE @MissingNominalMsg NVARCHAR(1000) = N'محاسبه انجام نشد: برای پرسنل ' + @EMP_LABEL + N' حکم تأییدشده با حقوق پایه اسمی (BASE_SAL) در این ماه وجود ندارد. ' + @DEC_REASON;
            RAISERROR(@MissingNominalMsg,16,1);
            RETURN;
        END;
        IF @HAS_OFFICIAL_RATE=0
        BEGIN
            DECLARE @MissingOfficialMsg NVARCHAR(1000)=N'محاسبه انجام نشد: برای پرسنل '+@EMP_LABEL+N' حقوق پایه رسمی (BASE_SAL_B) در حکم این ماه ثبت نشده یا صفر است؛ حکم را کامل و تأیید کنید.';
            RAISERROR(@MissingOfficialMsg,16,1);
            RETURN;
        END;

        -- گام ۶ — افزودن آیتم‌های متغیر
        SET @TOTAL_NOMINAL_BASE = ISNULL((
            SELECT SUM(INS_AMOUNT) FROM @ItemCalc
            WHERE ITEM_CODE IN ('BASE_SAL','SANOVAT_PAYE')
        ), 0);

        SET @TOTAL_OFFICIAL_BASE = ISNULL((
            SELECT SUM(AMOUNT) FROM @ItemCalc
            WHERE ITEM_CODE IN ('BASE_SAL_B','SANOVAT_PAYE')
        ), 0);

        -- ریل رسمی (برای پرداختی اضافه‌کار)
        IF @DAYSB > 0 AND @OT_HOUR_BASE > 0
        BEGIN
            SET @EFFECTIVE_HOURLY = ISNULL((CAST(@TOTAL_OFFICIAL_BASE AS DECIMAL(18,2)) / @DAYSB) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END


        -- ریل اسمی (برای بیمه و مالیات اضافه‌کار)
        IF @DAYS > 0 AND @OT_HOUR_BASE > 0
        BEGIN
            SET @OFFICIAL_HOURLY = ISNULL((CAST(@TOTAL_NOMINAL_BASE AS DECIMAL(18,2)) / @DAYS) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END


        IF @OT_NORMAL_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_NORMAL')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_NORMAL', 2, CAST(@EFFECTIVE_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'OT_NORMAL';

        IF @OT_HOLIDAY_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_HOLIDAY')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_HOLIDAY', 2, CAST(@EFFECTIVE_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'OT_HOLIDAY';

        IF @OT_ADMIN_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_ADMIN')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_ADMIN', 2, CAST(@EFFECTIVE_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'OT_ADMIN';

        IF @PERF_AMOUNT > 0
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'PERF_BONUS', 2, @PERF_AMOUNT, @PERF_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'PERF_BONUS';

        IF @TRANSP_AMOUNT > 0
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'TRANSP', 2, @TRANSP_AMOUNT, @TRANSP_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'TRANSP';

        INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT AV.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, AV.VALUE, AV.VALUE, ID.INS_SUBJECT, ID.TAX_SUBJECT
        FROM PAY2_ATT_VALUE AV INNER JOIN #ItemDefSource ID ON AV.ITEM_ID = ID.ITEM_ID
        WHERE AV.PER_ID = @PER_ID AND AV.EMP_ID = @EMP_ID AND AV.VALUE <> 0
          AND NOT EXISTS (SELECT 1 FROM @ItemCalc X WHERE X.ITEM_ID = AV.ITEM_ID);

        -- برای دوره‌های قبل از تاریخ اثر، قاعده اختصاصی مشتری اعمال نمی‌شود.
        -- Runهای نهایی نیز طبق کنترل موتور قابل بازمحاسبه نیستند.
        IF @INS_NON_SUBJECT_EFFECTIVE_FROM>0 AND @PERIOD_DATE/100>=@INS_NON_SUBJECT_EFFECTIVE_FROM/100
            UPDATE @ItemCalc SET INS_SUBJECT=0
            WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN');

        -- گام ۷ — محاسبه بیمه
        SET @GROSS_PAY = 0; SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0; SET @INS_EMPLOYER_BASE = 0; SET @INS_UNEMPLOYMENT = 0;

        -- ناخالص پرداختی بر اساس حقوق رسمی (با جلوگیری از دوبارشماری)
        SELECT @GROSS_PAY = ISNULL(SUM(AMOUNT), 0)
        FROM @ItemCalc
        WHERE ITEM_TYPE IN (1, 2) AND (@HAS_BOTH_SAL = 0 OR ITEM_CODE <> 'BASE_SAL');

        SELECT @NOMINAL_GROSS = ISNULL(SUM(INS_AMOUNT), 0)
        FROM @ItemCalc
        WHERE ITEM_TYPE IN (1, 2) AND (@HAS_BOTH_SAL = 0 OR ITEM_CODE <> 'BASE_SAL_B');

        -- مبنای بیمه بر اساس حقوق اسمی (با استفاده از INS_AMOUNT)
        SET @INS_OFFICIAL_VALID = 0; SET @INS_DROP_SAL = NULL;
        IF @HAS_BOTH_SAL = 1
        BEGIN
            IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B' AND INS_SUBJECT = 1 AND INS_AMOUNT <> 0)
                SET @INS_OFFICIAL_VALID = 1;
            SET @INS_DROP_SAL = 'BASE_SAL_B';
        END;

        SELECT @INS_BASE = ISNULL(SUM(INS_AMOUNT), 0)
        FROM @ItemCalc
        WHERE INS_SUBJECT = 1 AND ITEM_TYPE IN (1, 2) AND (@INS_DROP_SAL IS NULL OR ITEM_CODE <> @INS_DROP_SAL);

        SET @EFFECTIVE_INS_CEILING = CAST((@INS_CEILING / 30.0) * @DAYS AS BIGINT);
        IF @INS_CEILING_APPLY = 1 AND @INS_TYPE <> 3
            SET @INS_BASE = CASE WHEN @INS_BASE > @EFFECTIVE_INS_CEILING THEN @EFFECTIVE_INS_CEILING ELSE @INS_BASE END;

        IF @INS_TYPE = 3
        BEGIN
            SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0; SET @INS_EMPLOYER_BASE = 0; SET @INS_UNEMPLOYMENT = 0;
        END;
        ELSE
        BEGIN
            SET @INS_WORKER = ISNULL(CAST(@INS_BASE * @INS_WORKER_RATE AS BIGINT), 0);
            SET @EMP_IS_JANBAZ = ISNULL((SELECT IS_JANBAZ FROM #EmployeeSource WHERE EMP_ID = @EMP_ID), 0);
            SET @JANBAZ_RATE = ISNULL(CAST((SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY='INS_JANBAZ_RATE') AS DECIMAL(6,4)), 0.18);

            IF @EMP_IS_JANBAZ = 1
                SET @INS_EMPLOYER_BASE = ISNULL(CAST(@INS_BASE * @JANBAZ_RATE AS BIGINT), 0);
            ELSE
            BEGIN
                SET @INS_EMPLOYER_BASE = ISNULL(CAST(@INS_BASE * @INS_EMPLOYER_RATE AS BIGINT), 0);
                SET @INS_UNEMPLOYMENT = CASE WHEN ISNULL(@IS_MANAGER,0)=0 THEN ISNULL(CAST(@INS_BASE * @INS_UNEMP_RATE AS BIGINT),0) ELSE 0 END;
            END;
            SET @INS_EMPLOYER = @INS_EMPLOYER_BASE + @INS_UNEMPLOYMENT;
        END;

        -- گام ۸ — محاسبه مالیات
        SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        IF @TAX_EXEMPT_FLAG = 1
        BEGIN
            SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        END;
        ELSE
        BEGIN
            -- مالیات کاملاً بر اساس حقوق اسمی و مقادیر INS_AMOUNT محاسبه می‌شود
            SET @TAX_OFFICIAL_VALID = 0; SET @TAX_DROP_SAL = NULL;
            IF @HAS_BOTH_SAL = 1
            BEGIN
                IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B' AND TAX_SUBJECT = 1 AND INS_AMOUNT <> 0)
                    SET @TAX_OFFICIAL_VALID = 1;
                SET @TAX_DROP_SAL = 'BASE_SAL_B';
            END;

            SELECT @TAX_BASE = ISNULL(SUM(INS_AMOUNT), 0)
            FROM @ItemCalc
            WHERE TAX_SUBJECT = 1 AND ITEM_TYPE IN (1, 2) AND (@TAX_DROP_SAL IS NULL OR ITEM_CODE <> @TAX_DROP_SAL);

            IF @TAX_DEDUCT_INS = 1 SET @TAX_BASE = @TAX_BASE - @INS_WORKER;
            SET @TAX_BASE = CASE WHEN @TAX_BASE > @TAX_EXEMPT THEN @TAX_BASE - @TAX_EXEMPT ELSE 0 END;
            IF @TAX_DEP_APPLY = 1 AND @REGION_DEP > 0 SET @TAX_BASE = CAST(@TAX_BASE * (1.0 - @REGION_DEP / 100.0) AS BIGINT);
            SET @TAX_AMOUNT = ISNULL([dbo].[FN_PAY2_CALC_TAX](@TAX_BASE * 12, @TAX_YEAR) / 12, 0);
            IF @TAX_AMOUNT < 0 SET @TAX_AMOUNT = 0;
        END;

        SET @ADVANCE_DED = 0;
        IF @ADV_ENABLED = 1 SELECT @ADVANCE_DED = ISNULL(ADVANCE_DEDUCTION, 0) FROM #AdvResult WHERE EMP_ID = @EMP_ID;

        SET @LOAN_DED = 0;
        SELECT @LOAN_DED = ISNULL(SUM(LS.AMOUNT), 0) FROM PAY2_LOAN_SCHED LS INNER JOIN PAY2_LOAN L ON LS.LOAN_ID = L.LOAN_ID
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1 AND LS.DUE_PERIOD = @PERIOD_DATE AND LS.RUN_ID IS NULL;

        -- کسر کار: هر ساعت دقیقاً معادل «نرخ یک ساعت کار عادی» (ضریب ۱، بدون ضریب اضافه‌کار)
        SET @SHORTAGE_DED = 0;
        IF @SHORTAGE_H > 0 AND @EFFECTIVE_HOURLY > 0
            SET @SHORTAGE_DED = ISNULL(CAST(@EFFECTIVE_HOURLY * @SHORTAGE_H AS BIGINT), 0);

        SET @OTHER_DED = ISNULL(@KASR_OTHER, 0) + @SHORTAGE_DED;
        SET @TOTAL_DED = @INS_WORKER + @TAX_AMOUNT + @LOAN_DED + @ADVANCE_DED + @OTHER_DED;

        -- فرمول تراز: پیدا کردن اختلاف گرد کردن و اعمال آن روی ناخالص پرداختی
        DECLARE @RAW_NET BIGINT = @GROSS_PAY - @TOTAL_DED;
        SET @NET_PAY = @RAW_NET;

        IF @ROUND_MODE > 1
            SET @NET_PAY = ISNULL(ROUND(CAST(@RAW_NET AS FLOAT) / @ROUND_MODE, 0) * @ROUND_MODE, 0);

        -- اختلافی که بخاطر گرد کردن ایجاد شده را به ناخالص اضافه/کم میکنیم تا معادله تراز بماند
        DECLARE @ROUNDING_DIFF BIGINT = @NET_PAY - @RAW_NET;
        SET @GROSS_PAY = @GROSS_PAY + @ROUNDING_DIFF;

        SET @LEAVE_BAL_DAYS = NULL;
        SELECT @LEAVE_BAL_DAYS = CAST(BALANCE_MIN AS DECIMAL(10,2)) / 440.0 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;

        SET @LOAN_BAL = NULL;
        SELECT @LOAN_BAL = ISNULL(SUM(BALANCE), 0) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID;

        INSERT INTO PAY2_RUN_LINE (
            RUN_ID, EMP_ID, WORK_DAYS, GROSS_PAY, INS_BASE, INS_WORKER, INS_EMPLOYER, TAX_BASE, TAX_AMOUNT,
            LOAN_DED, ADVANCE_DED, OTHER_DED, TOTAL_DED, NET_PAY, LEAVE_BAL_DAYS, LOAN_BALANCE, ADVANCE_BALANCE_SNAP,
            NOMINAL_GROSS, NOMINAL_DAYS, INS_EMPLOYER_BASE, INS_UNEMPLOYMENT, ROUNDING_ADJ, HIRE_DATE_SNAP, FIRE_DATE_SNAP
        ) VALUES (
            @NEW_RUN_ID, @EMP_ID, @DAYSB, @GROSS_PAY, @INS_BASE, @INS_WORKER, @INS_EMPLOYER, @TAX_BASE, @TAX_AMOUNT,
            @LOAN_DED, @ADVANCE_DED, @OTHER_DED, @TOTAL_DED, @NET_PAY, @LEAVE_BAL_DAYS, @LOAN_BAL, @ADVANCE_DED,
            @NOMINAL_GROSS, @DAYS, @INS_EMPLOYER_BASE, @INS_UNEMPLOYMENT, @ROUNDING_DIFF,
            (SELECT HIRE_DATE FROM #EmployeeSource WHERE EMP_ID=@EMP_ID),
            (SELECT FIRE_DATE FROM #EmployeeSource WHERE EMP_ID=@EMP_ID)
        );

        INSERT INTO PAY2_RUN_EMP_SNAPSHOT
        (
            RUN_ID,EMP_ID,EMP_CODE,FIRST_NAME,LAST_NAME,FATHER_NAME,NATIONAL_CODE,INS_CODE,
            ID_NUMBER,BIRTH_PLACE,BIRTH_DATE,GENDER,INS_TYPE_SNAP,TAX_EXEMPT_SNAP,
            MARITAL_SNAP,NATIONALITY_SNAP,MOBILE,JOB_CODE_SNAP,JOB_NAME_SNAP,HIRE_DATE_SNAP,FIRE_DATE_SNAP,
            IS_MANAGER_SNAP,IS_JANBAZ_SNAP,REGION_DEPRIVATION_SNAP,ACC_T_SNAP
        )
        SELECT @NEW_RUN_ID,E.EMP_ID,E.EMP_CODE,E.FIRST_NAME,E.LAST_NAME,E.FATHER_NAME,E.NATIONAL_CODE,E.INS_CODE,
               E.ID_NUMBER,E.BIRTH_PLACE,E.BIRTH_DATE,E.GENDER,ISNULL(E.INS_TYPE,1),ISNULL(E.TAX_EXEMPT,0),
               E.MARITAL,E.NATIONALITY,E.MOBILE,E.JOB_CODE,E.JOB_NAME,E.HIRE_DATE,E.FIRE_DATE,
               @IS_MANAGER,E.IS_JANBAZ,E.REGION_DEPRIVATION,E.ACC_T
        FROM #EmployeeSource E
        WHERE E.EMP_ID=@EMP_ID;

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, NOMINAL_AMOUNT, ITEM_CODE_SNAP, ITEM_NAME_SNAP, CALC_BASIS_SNAP, ITEM_TYPE_SNAP, INS_SUBJECT_AMOUNT, TAX_SUBJECT_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT @NEW_RUN_ID, @EMP_ID, C.ITEM_ID, SUM(C.AMOUNT), SUM(C.INS_AMOUNT), MAX(C.ITEM_CODE), MAX(I.ITEM_NAME), MAX(I.CALC_BASIS), MAX(C.ITEM_TYPE),
               SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.INS_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END),
               SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.TAX_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END),
               CASE WHEN SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.INS_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END)<>0 THEN 1 ELSE 0 END,
               CASE WHEN SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.TAX_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END)<>0 THEN 1 ELSE 0 END
        FROM @ItemCalc C INNER JOIN #ItemDefSource I ON I.ITEM_ID=C.ITEM_ID
        GROUP BY C.ITEM_ID
        HAVING SUM(C.AMOUNT) <> 0 OR SUM(C.INS_AMOUNT) <> 0 OR MAX(C.ITEM_CODE) IN ('BASE_SAL','BASE_SAL_B','SANOVAT_PAYE');

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID,EMP_ID,ITEM_ID,AMOUNT,NOMINAL_AMOUNT,ITEM_CODE_SNAP,ITEM_NAME_SNAP,CALC_BASIS_SNAP,ITEM_TYPE_SNAP,INS_SUBJECT_AMOUNT,TAX_SUBJECT_AMOUNT,INS_SUBJECT,TAX_SUBJECT)
        SELECT @NEW_RUN_ID,@EMP_ID,I.ITEM_ID,V.AMOUNT,0,I.ITEM_CODE,I.ITEM_NAME,I.CALC_BASIS,I.ITEM_TYPE,0,0,0,0
        FROM #ItemDefSource I
        CROSS APPLY (VALUES(CASE I.ITEM_CODE WHEN 'INS_DED' THEN @INS_WORKER WHEN 'TAX_DED' THEN @TAX_AMOUNT WHEN 'LOAN_DED' THEN @LOAN_DED WHEN 'ADVANCE_DED' THEN @ADVANCE_DED ELSE 0 END)) V(AMOUNT)
        WHERE I.ITEM_ID IN(@INS_DED_ID,@TAX_DED_ID,@LOAN_DED_ID,@ADV_DED_ID) AND V.AMOUNT>0;

        UPDATE PAY2_LOAN_SCHED SET RUN_ID = @NEW_RUN_ID, PAID_AT = GETDATE()
        WHERE DUE_PERIOD = @PERIOD_DATE AND RUN_ID IS NULL AND LOAN_ID IN (SELECT LOAN_ID FROM PAY2_LOAN WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1);

        UPDATE L
        SET L.PAID_INST = L.PAID_INST + (
            SELECT COUNT(1) FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @NEW_RUN_ID
        )
        FROM PAY2_LOAN L
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1
          AND EXISTS (SELECT 1 FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @NEW_RUN_ID);

        SET @LEAVE_MIN_USED = CAST(@LEAVE_DAYS * 440 AS INT);
        IF @LEAVE_MIN_USED > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000)
            BEGIN
                UPDATE PAY2_LEAVE_BAL SET USED_MIN = USED_MIN + @LEAVE_MIN_USED, UPDATED_AT = GETDATE()
                WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;
            END
            ELSE
            BEGIN
                INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN, CARRIED_OUT_MIN, UPDATED_AT)
                VALUES (@EMP_ID, @PERIOD_DATE / 10000, 11440, @LEAVE_MIN_USED, 0, 0, GETDATE());
            END
        END;

        FETCH NEXT FROM cur_emp INTO @EMP_ID, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;
    END;

    CLOSE cur_emp; DEALLOCATE cur_emp;
    DROP TABLE #AdvResult;

    UPDATE PAY2_PERIOD SET STATUS = 3 WHERE PER_ID = @PER_ID;

END;
GO

-- ================================================================
-- ۳. SP_PAY2_CALC_SETTLE — محاسبه تسویه حساب پرسنل
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CALC_SETTLE]
    @EMP_ID        INT,
    @WS_ID         INT,
    @SETTLE_DATE   BIGINT,
    @END_DATE      BIGINT,
    @PREV_CREDIT   BIGINT = 0,
    @OTHER_INCOME  BIGINT = 0,
    @OTHER_DED     BIGINT = 0,
    @CALC_BY       INT    = NULL,
    @NEW_SET_ID    INT    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @BONUS_MODE          NVARCHAR(20),
        @BONUS_CUSTOM_DAYS   INT,
        @MIN_WAGE_DAILY      BIGINT,
        @MIN_WAGE_MONTHLY    BIGINT,
        @EIDI_MIN_DAYS       INT,
        @EIDI_MAX_DAYS       INT,
        @SENIORITY_MODE      NVARCHAR(20),
        @SENIORITY_FIXED_AMT BIGINT,
        @TAX_YEAR            SMALLINT,
        @TAX_EXEMPT_MONTHLY  BIGINT,
        @LEAVE_MINS_PER_DAY  INT;

    SELECT
        @BONUS_MODE          = ISNULL(MAX(CASE WHEN CFG_KEY='BONUS_MODE'          THEN CFG_VALUE END), 'MIN_WAGE'),
        @BONUS_CUSTOM_DAYS   = ISNULL(MAX(CASE WHEN CFG_KEY='BONUS_CUSTOM_DAYS'   THEN CAST(CFG_VALUE AS INT) END), 60),
        @MIN_WAGE_DAILY      = ISNULL(MAX(CASE WHEN CFG_KEY='MIN_WAGE_DAILY'      THEN CAST(CFG_VALUE AS BIGINT) END), 73200),
        @MIN_WAGE_MONTHLY    = ISNULL(MAX(CASE WHEN CFG_KEY='MIN_WAGE_MONTHLY'    THEN CAST(CFG_VALUE AS BIGINT) END), 2196000),
        @EIDI_MIN_DAYS       = ISNULL(MAX(CASE WHEN CFG_KEY='EIDI_MIN_DAYS'       THEN CAST(CFG_VALUE AS INT) END), 60),
        @EIDI_MAX_DAYS       = ISNULL(MAX(CASE WHEN CFG_KEY='EIDI_MAX_DAYS'       THEN CAST(CFG_VALUE AS INT) END), 90),
        @SENIORITY_MODE      = ISNULL(MAX(CASE WHEN CFG_KEY='SENIORITY_MODE'      THEN CFG_VALUE END), 'LAST_SAL'),
        @SENIORITY_FIXED_AMT = ISNULL(MAX(CASE WHEN CFG_KEY='SENIORITY_FIXED_AMT' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @TAX_YEAR            = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'            THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT_MONTHLY  = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY'  THEN CAST(CFG_VALUE AS BIGINT) END), 84000000),
        @LEAVE_MINS_PER_DAY  = ISNULL(MAX(CASE WHEN CFG_KEY='LEAVE_MINS_PER_DAY'  THEN CAST(CFG_VALUE AS INT) END), 440)
    FROM PAY2_CONFIG
    WHERE CFG_KEY IN ('BONUS_MODE','BONUS_CUSTOM_DAYS','MIN_WAGE_DAILY','MIN_WAGE_MONTHLY','EIDI_MIN_DAYS','EIDI_MAX_DAYS','SENIORITY_MODE','SENIORITY_FIXED_AMT','TAX_YEAR','TAX_EXEMPT_MONTHLY','LEAVE_MINS_PER_DAY');

    DECLARE @HIRE_DATE BIGINT, @EMP_FIRST_NAME NVARCHAR(50), @EMP_LAST_NAME NVARCHAR(50);
    SELECT @HIRE_DATE = HIRE_DATE, @EMP_FIRST_NAME = FIRST_NAME, @EMP_LAST_NAME = LAST_NAME FROM PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID;

    IF @HIRE_DATE IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_CALC_SETTLE: پرسنل %d یافت نشد.', 16, 1, @EMP_ID);
        RETURN;
    END;

    DECLARE @PREV_SET_ID INT = NULL, @PREV_SEN_DAYS INT = 0, @PREV_SETTLE_DATE BIGINT = NULL;
    SELECT TOP 1 @PREV_SET_ID = SET_ID, @PREV_SEN_DAYS = SENIORITY_DAYS + PREV_SENIORITY_DAYS, @PREV_SETTLE_DATE = SETTLE_DATE
    FROM PAY2_SETTLEMENT WHERE EMP_ID = @EMP_ID AND STATUS >= 2 ORDER BY SETTLE_DATE DESC;

   -- سابقه کل بر اساس استاندارد ۳۶۵ روزه (جایگزین خط اشتباه قبلی)
    DECLARE @START_Y INT = @HIRE_DATE / 10000;
    DECLARE @START_M INT = (@HIRE_DATE / 100) % 100;
    DECLARE @START_D INT = @HIRE_DATE % 100;

    DECLARE @END_Y INT = @END_DATE / 10000;
    DECLARE @END_M INT = (@END_DATE / 100) % 100;
    DECLARE @END_D INT = @END_DATE % 100;

    DECLARE @DAYS_START INT = CASE WHEN @START_M <= 6 THEN (@START_M - 1) * 31 + @START_D ELSE (6 * 31) + (@START_M - 7) * 30 + @START_D END;
    DECLARE @DAYS_END   INT = CASE WHEN @END_M <= 6 THEN (@END_M - 1) * 31 + @END_D ELSE (6 * 31) + (@END_M - 7) * 30 + @END_D END;

    -- محاسبه دقیق روزهای بین دو تاریخ شمسی با احتساب سال‌های ۳۶۵ روزه
    DECLARE @SENIORITY_DAYS INT = ((@END_Y - @START_Y) * 365) + @DAYS_END - @DAYS_START - @PREV_SEN_DAYS;
    IF @SENIORITY_DAYS < 0 SET @SENIORITY_DAYS = 0;

    DECLARE @SENIORITY_YEARS  DECIMAL(6,2) = CAST(@SENIORITY_DAYS AS DECIMAL(10,2)) / 365.0;
    DECLARE @SENIORITY_FULL   INT           = @SENIORITY_DAYS / 365;
    DECLARE @SENIORITY_REMAIN INT           = @SENIORITY_DAYS % 365;

    DECLARE @LAST_DEC_ID  INT;
    SELECT TOP 1 @LAST_DEC_ID = DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1 AND EFF_FROM <= @SETTLE_DATE AND (EFF_TO IS NULL OR EFF_TO >= @SETTLE_DATE) ORDER BY EFF_FROM DESC;

    DECLARE @LAST_DAILY_ONLY BIGINT = ISNULL((SELECT SUM(DL.AMOUNT) FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_TYPE = 1 AND ID.INS_SUBJECT = 1 AND ID.CALC_BASIS = 1), 0);
    DECLARE @LAST_MONTHLY_ONLY BIGINT = ISNULL((SELECT SUM(DL.AMOUNT) FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_TYPE = 1 AND ID.INS_SUBJECT = 1 AND ID.CALC_BASIS = 2), 0);
    DECLARE @LAST_DAILY BIGINT = @LAST_DAILY_ONLY + CAST(@LAST_MONTHLY_ONLY / 30.0 AS BIGINT);
    IF @LAST_DAILY < @MIN_WAGE_DAILY SET @LAST_DAILY = @MIN_WAGE_DAILY;
    DECLARE @LAST_SALARY BIGINT = @LAST_DAILY * 30;

    -- محاسبه روزهای عیدی محدود به سال جاری تقویمی / پس از آخرین تسویه
    DECLARE @EIDI BIGINT = 0;

    DECLARE @START_OF_YEAR BIGINT = (@END_DATE / 10000) * 10000 + 101;
    DECLARE @EIDI_START_DATE BIGINT = @HIRE_DATE;

    IF @START_OF_YEAR > @EIDI_START_DATE SET @EIDI_START_DATE = @START_OF_YEAR;
    IF @PREV_SETTLE_DATE IS NOT NULL AND @PREV_SETTLE_DATE > @EIDI_START_DATE SET @EIDI_START_DATE = @PREV_SETTLE_DATE;

    DECLARE @END_M_EIDI INT = (@END_DATE / 100) % 100;
    DECLARE @END_D_EIDI INT = @END_DATE % 100;

    DECLARE @START_M_EIDI INT = (@EIDI_START_DATE / 100) % 100;
    DECLARE @START_D_EIDI INT = @EIDI_START_DATE % 100;

    DECLARE @DAYS_SINCE_YEAR_START_END INT =
        CASE
            WHEN @END_M_EIDI <= 6 THEN (@END_M_EIDI - 1) * 31 + @END_D_EIDI
            ELSE (6 * 31) + (@END_M_EIDI - 7) * 30 + @END_D_EIDI
        END;

    DECLARE @DAYS_SINCE_YEAR_START_START INT =
        CASE
            WHEN @START_M_EIDI <= 6 THEN (@START_M_EIDI - 1) * 31 + @START_D_EIDI
            ELSE (6 * 31) + (@START_M_EIDI - 7) * 30 + @START_D_EIDI
        END;

    DECLARE @WORKED_DAYS_FOR_EIDI INT = @DAYS_SINCE_YEAR_START_END - @DAYS_SINCE_YEAR_START_START + 1;

    IF @WORKED_DAYS_FOR_EIDI < 0 SET @WORKED_DAYS_FOR_EIDI = 0;
    IF @WORKED_DAYS_FOR_EIDI > 365 SET @WORKED_DAYS_FOR_EIDI = 365;

    IF @WORKED_DAYS_FOR_EIDI > 0
    BEGIN
        DECLARE @EIDI_BASE_DAILY BIGINT = @LAST_DAILY;

        IF @BONUS_MODE = 'MIN_WAGE'
            SET @EIDI_BASE_DAILY = CASE WHEN @LAST_SALARY < @MIN_WAGE_MONTHLY THEN @LAST_DAILY ELSE (@MIN_WAGE_MONTHLY / 30) END;

        IF @BONUS_MODE = 'CUSTOM'
        BEGIN
            SET @EIDI = @LAST_DAILY * ISNULL(@BONUS_CUSTOM_DAYS, 60);
        END
        ELSE
        BEGIN
            DECLARE @CALC_EIDI BIGINT = CAST((@EIDI_BASE_DAILY * @EIDI_MIN_DAYS * CAST(@WORKED_DAYS_FOR_EIDI AS FLOAT)) / 365.0 AS BIGINT);
            DECLARE @MAX_EIDI BIGINT  = CAST((@EIDI_BASE_DAILY * @EIDI_MAX_DAYS * CAST(@WORKED_DAYS_FOR_EIDI AS FLOAT)) / 365.0 AS BIGINT);

            IF @CALC_EIDI > @MAX_EIDI SET @EIDI = @MAX_EIDI;
            ELSE SET @EIDI = @CALC_EIDI;
        END
    END;

    -- معافیت مالیات عیدی طبق قانون: معادل «یک ماه» معافیت کامل، بدون پروریت بر حسب روزهای کارکرد
    DECLARE @EIDI_TAX BIGINT = 0;
    IF @EIDI > @TAX_EXEMPT_MONTHLY
    BEGIN
        SET @EIDI_TAX = [dbo].[FN_PAY2_CALC_TAX]((@EIDI - @TAX_EXEMPT_MONTHLY) * 12, @TAX_YEAR) / 12;
    END

    DECLARE @SANAVAT BIGINT = CASE
        WHEN @SENIORITY_MODE = 'LAST_SAL' THEN @LAST_SALARY * @SENIORITY_FULL + CAST(@LAST_SALARY * @SENIORITY_REMAIN / 365.0 AS BIGINT)
        WHEN @SENIORITY_MODE = 'DAILY' THEN @LAST_DAILY * 30 * @SENIORITY_FULL + CAST(@LAST_DAILY * @SENIORITY_REMAIN AS BIGINT)
        ELSE ISNULL(@SENIORITY_FIXED_AMT, 0) * @SENIORITY_FULL END;

    DECLARE @LEAVE_BAL_MIN  INT = ISNULL((SELECT SUM(BALANCE_MIN) FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID), 0);
    IF @LEAVE_BAL_MIN < 0 SET @LEAVE_BAL_MIN = 0;

    DECLARE @LEAVE_BAL_DAYS_CALC DECIMAL(5,2) = CAST(@LEAVE_BAL_MIN AS DECIMAL(10,2)) / ISNULL(NULLIF(@LEAVE_MINS_PER_DAY, 0), 440);
    DECLARE @LEAVE_PAY BIGINT = CAST(@LEAVE_BAL_DAYS_CALC * @LAST_DAILY AS BIGINT);

    DECLARE @BON_SETTLE BIGINT = ISNULL((SELECT TOP 1 DL.AMOUNT * @SENIORITY_FULL FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_CODE = 'GROCERY'), 0);
    DECLARE @LOAN_BALANCE_TOT BIGINT = ISNULL((SELECT SUM(BALANCE) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID), 0);

    INSERT INTO PAY2_SETTLEMENT (EMP_ID, WS_ID, SETTLE_DATE, HIRE_DATE, END_DATE, SENIORITY_DAYS, SENIORITY_YEARS, LAST_SALARY, LAST_DAILY, PREV_SET_ID, PREV_SENIORITY_DAYS, LEAVE_BAL_MIN, LEAVE_BAL_DAYS, EIDI, BON, LEAVE_PAY, SANAVAT, PREV_CREDIT, OTHER_INCOME, PREV_DEBIT, EIDI_TAX, LOAN_BALANCE, OTHER_DED, STATUS, CALC_METHOD, CREATED_BY)
    VALUES (@EMP_ID, @WS_ID, @SETTLE_DATE, @HIRE_DATE, @END_DATE, @SENIORITY_DAYS, @SENIORITY_YEARS, @LAST_SALARY, @LAST_DAILY, @PREV_SET_ID, @PREV_SEN_DAYS, @LEAVE_BAL_MIN, @LEAVE_BAL_DAYS_CALC, @EIDI, @BON_SETTLE, @LEAVE_PAY, @SANAVAT, @PREV_CREDIT, @OTHER_INCOME, 0, @EIDI_TAX, @LOAN_BALANCE_TOT, @OTHER_DED, 1,
        N'{""bonus_mode"":""' + @BONUS_MODE + N'"",""seniority_mode"":""' + @SENIORITY_MODE + N'"",""tax_year"":' + CAST(@TAX_YEAR AS NVARCHAR) + N'}', @CALC_BY);

    SET @NEW_SET_ID = SCOPE_IDENTITY();

END;
GO

-- ================================================================
-- ۴. SP_PAY2_GEN_DEED_SETTLE — تولید آرتیکل‌های سند تسویه
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GEN_DEED_SETTLE]
    @SET_ID  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT;
    DECLARE @WS_ID  INT;
    DECLARE @EMP_ID INT;
    DECLARE @EMP_NAME NVARCHAR(100);

    SELECT @STATUS = S.STATUS, @WS_ID = S.WS_ID, @EMP_ID = S.EMP_ID,
           @EMP_NAME = E.LAST_NAME + N' ' + E.FIRST_NAME
    FROM PAY2_SETTLEMENT S
    INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID
    WHERE S.SET_ID = @SET_ID;
    IF @STATUS IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_GEN_DEED_SETTLE: تسویه‌ای با شناسه %d یافت نشد.', 16, 1, @SET_ID);
        RETURN;
    END;
IF @STATUS <> 2
    BEGIN
        RAISERROR(N'SP_PAY2_GEN_DEED_SETTLE: تسویه %d باید نهایی (STATUS=2) شود.', 16, 1, @SET_ID);
        RETURN;
    END;

    DECLARE @ACC_SALARY_PAY  NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50), @ACC_TAX_PAYABLE NVARCHAR(50);
    DECLARE @ACC_COST_EIDI   NVARCHAR(50), @ACC_COST_SANAVAT NVARCHAR(50), @ACC_COST_LEAVE NVARCHAR(50);
    DECLARE @ACC_LOAN_HES    NVARCHAR(50), @ACC_ADV_HES NVARCHAR(50);

    SELECT
        @ACC_SALARY_PAY   = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE' THEN ACC_CODE END),
        @ACC_INS_PAYABLE  = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'    THEN ACC_CODE END),
        @ACC_TAX_PAYABLE  = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'    THEN ACC_CODE END),
        @ACC_COST_EIDI    = MAX(CASE WHEN ACC_KEY='COST_EIDI'      THEN ACC_CODE END),
        @ACC_COST_SANAVAT = MAX(CASE WHEN ACC_KEY='COST_SANAVAT'   THEN ACC_CODE END),
        @ACC_COST_LEAVE   = MAX(CASE WHEN ACC_KEY='COST_LEAVE'     THEN ACC_CODE END),
        @ACC_LOAN_HES     = MAX(CASE WHEN ACC_KEY='LOAN_HES'       THEN ACC_CODE END),
        @ACC_ADV_HES      = MAX(CASE WHEN ACC_KEY='ADV_HES'        THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    SELECT @ACC_COST_EIDI AS HES_CODE, N'هزینه عیدی' AS SHARH, EIDI AS BED, 0 AS BES, 'COST_EIDI' AS ACC_KEY, NULL AS EMP_ID
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI > 0
    UNION ALL
    SELECT @ACC_COST_SANAVAT, N'هزینه حق سنوات', SANAVAT, 0, 'COST_SANAVAT', NULL
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND SANAVAT > 0
    UNION ALL
    SELECT @ACC_COST_LEAVE, N'هزینه بازخرید مرخصی', LEAVE_PAY, 0, 'COST_LEAVE', NULL
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND LEAVE_PAY > 0
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_SALARY_PAY), N'پرداختنی تسویه حساب: ' + @EMP_NAME, 0, CAST(EIDI+BON+LEAVE_PAY+SANAVAT+PREV_CREDIT+OTHER_INCOME-PREV_DEBIT-EIDI_TAX-LOAN_BALANCE-OTHER_DED AS BIGINT), 'SETTLE_PAYABLE', S.EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_LOAN_HES), N'وصول مانده وام از تسویه: ' + @EMP_NAME, 0, LOAN_BALANCE, 'LOAN_COLLECT', @EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID AND LOAN_BALANCE > 0
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_ADV_HES), N'وصول بدهکاری (مساعده): ' + @EMP_NAME, 0, PREV_DEBIT, 'ADV_COLLECT', @EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID AND PREV_DEBIT > 0
    UNION ALL
    SELECT @ACC_TAX_PAYABLE, N'مالیات عیدی', 0, EIDI_TAX, 'TAX_PAYABLE', NULL
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI_TAX > 0;
END;
GO

-- ================================================================
-- ۵. SP_PAY2_CLOSE_PERIOD — بستن دوره و کنترل نهایی
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CLOSE_PERIOD]
    @PER_ID  INT,
    @CLOSE_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WS_ID INT;
    DECLARE @STATUS TINYINT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT @WS_ID = WS_ID, @STATUS = STATUS, @PERIOD_DATE = PERIOD_DATE
    FROM PAY2_PERIOD WHERE PER_ID = @PER_ID;
    IF @STATUS IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_CLOSE_PERIOD: دوره‌ای با شناسه %d یافت نشد.', 16, 1, @PER_ID);
        RETURN;
    END;
IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_CLOSE_PERIOD: دوره %d در وضعیت %d است. فقط دوره باز (1) قابل بستن است.', 16, 1, @PER_ID, @STATUS);
        RETURN;
    END;

    DECLARE @EMP_NO_ATT INT;
    SELECT @EMP_NO_ATT = COUNT(*)
    FROM PAY2_EMPLOYEE E
    WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
      AND NOT EXISTS (
          SELECT 1 FROM PAY2_ATTENDANCE A
          WHERE A.PER_ID = @PER_ID AND A.EMP_ID = E.EMP_ID
      );

    IF @EMP_NO_ATT > 0
        PRINT N'هشدار: ' + CAST(@EMP_NO_ATT AS NVARCHAR) + N' پرسنل فاقد ورودی کارکرد در این دوره هستند.';

    UPDATE PAY2_PERIOD SET STATUS = 2, CLOSED_AT = GETDATE() WHERE PER_ID = @PER_ID;

    PRINT N'دوره ' + CAST(@PER_ID AS NVARCHAR) + N' (ماه ' + CAST(@PERIOD_DATE AS NVARCHAR) + N') بسته شد.';
END;
GO

-- ================================================================
-- ۶. SP_PAY2_REVERT_RUN — برگشت محاسبه (بازگشت به حالت قابل ویرایش)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_REVERT_RUN]
    @RUN_ID   INT,
    @REVERT_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- این پروسیجر همیشه داخل تراکنشِ لایه‌ی C# (ExecuteInTransactionAsync) اجرا می‌شود،
    -- چه مستقیم و چه از طریق SP_PAY2_CALC_RUN. پس تراکنش داخلی نمی‌گذاریم تا تداخلِ
    -- تراکنش تو‌در‌تو (ROLLBACK داخلی که تراکنش بیرونی را می‌کشد) رخ ندهد. XACT_ABORT
    -- تضمین می‌کند در صورت خطا، تراکنش بیرونی doomed و توسط C# رول‌بک شود.
    SET XACT_ABORT ON;

    DECLARE @STATUS TINYINT;
    DECLARE @PER_ID INT;
    DECLARE @IS_LATEST BIT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT @STATUS = R.STATUS, @PER_ID = R.PER_ID, @IS_LATEST = R.IS_LATEST, @PERIOD_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE R.RUN_ID = @RUN_ID;

    IF @PER_ID IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: محاسبه‌ای با این شناسه یافت نشد.', 16, 1);
        RETURN;
    END;

    IF @STATUS >= 3
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: سند حسابداری صادر شده — برگشت ممکن نیست.', 16, 1);
        RETURN;
    END;

    IF @IS_LATEST = 0
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: فقط آخرین نسخه (IS_LATEST=1) قابل برگشت است.', 16, 1);
        RETURN;
    END;

    -- گارد Idempotency: جلوگیری از برگشت دوباره مرخصی یا اقساط پس از حذف خروجی‌های RUN.
    IF NOT EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID)
    BEGIN
        RETURN;
    END;

    -- 1. بازگرداندن دقیق تعداد اقساط کسر شده در این RUN (فقط وام‌های درگیر همین RUN)
    UPDATE L SET L.PAID_INST = L.PAID_INST - (
        SELECT COUNT(1) FROM PAY2_LOAN_SCHED LS
        WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @RUN_ID
    )
    FROM PAY2_LOAN L
    WHERE EXISTS (SELECT 1 FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @RUN_ID);

    UPDATE PAY2_LOAN_SCHED
    SET RUN_ID = NULL, PAID_AT = NULL
    WHERE RUN_ID = @RUN_ID;

    -- 2. بازگرداندن دقیقه‌های مرخصی کسر شده (محافظت در برابر اعداد منفی)
    UPDATE LB
    SET LB.USED_MIN = CASE
                        WHEN LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT) < 0 THEN 0
                        ELSE LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT)
                      END,
        LB.UPDATED_AT = GETDATE()
    FROM PAY2_LEAVE_BAL LB
    INNER JOIN PAY2_ATTENDANCE A ON LB.EMP_ID = A.EMP_ID
    WHERE A.PER_ID = @PER_ID AND LB.YEAR = (@PERIOD_DATE / 10000)
      AND A.LEAVE_DAYS > 0;

    -- 3. حذف فیش‌ها
    DELETE FROM PAY2_RUN_DETAIL WHERE RUN_ID = @RUN_ID;
    DELETE FROM PAY2_RUN_LINE    WHERE RUN_ID = @RUN_ID;

    -- 4. باز کردن دوره و ثبت لاگ
    UPDATE PAY2_RUN
    SET STATUS = 1,
        NOTES = SUBSTRING(ISNULL(NOTES,'') + N' | Reverted by ' + CAST(ISNULL(@REVERT_BY,0) AS NVARCHAR), 1, 300)
    WHERE RUN_ID = @RUN_ID;

    UPDATE PAY2_PERIOD SET STATUS = 2 WHERE PER_ID = @PER_ID;

END;
GO

-- ================================================================
-- ۷. SP_PAY2_FINALIZE_RUN — نهایی‌کردن محاسبه (STATUS 1→2)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_FINALIZE_RUN]
    @RUN_ID   INT,
    @FINAL_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT;
    SELECT @STATUS = STATUS FROM PAY2_RUN WHERE RUN_ID = @RUN_ID;

    IF @STATUS IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: اجرای %d یافت نشد.', 16, 1, @RUN_ID);
        RETURN;
    END;

    IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: اجرا %d باید در وضعیت پیش‌نویس (1) باشد.', 16, 1, @RUN_ID);
        RETURN;
    END;

    DECLARE @PER_ID INT;
    DECLARE @WS_ID  INT;
    SELECT @PER_ID = R.PER_ID, @WS_ID = P.WS_ID
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID=P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    DECLARE @MISSING INT;
    SELECT @MISSING = COUNT(*)
    FROM PAY2_EMPLOYEE E
    WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
      AND EXISTS (SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID=@PER_ID AND A.EMP_ID=E.EMP_ID)
      AND NOT EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL WHERE RL.RUN_ID=@RUN_ID AND RL.EMP_ID=E.EMP_ID);

    IF @MISSING > 0
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: %d پرسنل هنوز محاسبه نشده‌اند.', 16, 1, @MISSING);
        RETURN;
    END;

    UPDATE PAY2_RUN
    SET STATUS = 2, NOTES = ISNULL(NOTES,'') + N' | Finalized by ' + CAST(ISNULL(@FINAL_BY,0) AS NVARCHAR)
    WHERE RUN_ID = @RUN_ID;

    PRINT N'SP_PAY2_FINALIZE_RUN — RUN_ID ' + CAST(@RUN_ID AS NVARCHAR) + N' نهایی شد.';
END;
GO

-- ================================================================
-- ۸. SP_PAY2_FINALIZE_SETTLE — نهایی‌کردن تسویه (STATUS 1→2)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_FINALIZE_SETTLE]
    @SET_ID     INT,
    @APPROVED_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @STATUS TINYINT;
        DECLARE @EMP_ID INT;
        DECLARE @END_DATE BIGINT;
        DECLARE @LOAN_BALANCE BIGINT;

        -- قفلِ واقعی (UPDLOCK) تا زمان COMMIT/ROLLBACK روی این سطر باقی می‌ماند
        SELECT @STATUS = STATUS, @EMP_ID = EMP_ID, @END_DATE = END_DATE, @LOAN_BALANCE = LOAN_BALANCE
        FROM PAY2_SETTLEMENT WITH (UPDLOCK)
        WHERE SET_ID = @SET_ID;

        IF @STATUS IS NULL
            RAISERROR(N'تسویه حسابی با این شناسه یافت نشد.', 16, 1);

        IF @STATUS <> 1
            RAISERROR(N'تسویه در وضعیت پیش‌نویس (1) نیست یا قبلاً تأیید شده است.', 16, 1);

        UPDATE PAY2_SETTLEMENT
        SET STATUS = 2, APPROVED_BY = @APPROVED_BY, APPROVED_AT = GETDATE()
        WHERE SET_ID = @SET_ID;

        -- پایان همکاری و غیرفعال شدن پرسنل
        UPDATE PAY2_EMPLOYEE
        SET FIRE_DATE = @END_DATE, IS_ACTIVE = 0
        WHERE EMP_ID = @EMP_ID AND IS_ACTIVE = 1;

        -- بستن قطعی وام‌های فعالِ تسویه‌شده
        IF @LOAN_BALANCE > 0
        BEGIN
            UPDATE PAY2_LOAN
            SET IS_ACTIVE = 0,
                PURPOSE = SUBSTRING(ISNULL(PURPOSE, '') + N' (بسته‌شده در تسویه)', 1, 200)
            WHERE EMP_ID = @EMP_ID AND IS_ACTIVE = 1 AND PAID_INST < TOTAL_INST;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        DECLARE @ERR_MSG NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ERR_MSG, 16, 1);
    END CATCH;
END;
GO

-- ================================================================
-- ۹. SP_PAY2_LOAN_GEN_SCHED — تولید خودکار جدول اقساط وام
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_LOAN_GEN_SCHED]
    @LOAN_ID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @TOTAL_INST  SMALLINT,
        @INSTALLMENT BIGINT,
        @FIRST_PAY   BIGINT,
        @EMP_ID      INT;

    SELECT
        @TOTAL_INST  = TOTAL_INST,
        @INSTALLMENT = INSTALLMENT,
        @FIRST_PAY   = FIRST_PAY,
        @EMP_ID      = EMP_ID
    FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID;

    IF @TOTAL_INST IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_LOAN_GEN_SCHED: وام %d یافت نشد.', 16, 1, @LOAN_ID);
        RETURN;
    END;

    DELETE FROM PAY2_LOAN_SCHED WHERE LOAN_ID = @LOAN_ID AND PAID_AT IS NULL;

    DECLARE @I SMALLINT = 1;
    DECLARE @DUE BIGINT = @FIRST_PAY;

    DECLARE @DUE_YEAR  INT = @FIRST_PAY / 10000;
    DECLARE @DUE_MONTH INT = (@FIRST_PAY % 10000) / 100;

    WHILE @I <= @TOTAL_INST
    BEGIN
        DECLARE @THIS_AMT BIGINT =
            CASE WHEN @I = @TOTAL_INST
                 THEN (
                    SELECT CASE
                             WHEN AMOUNT - (@INSTALLMENT * (@TOTAL_INST - 1)) < 0 THEN 0
                             ELSE AMOUNT - (@INSTALLMENT * (@TOTAL_INST - 1))
                           END
                    FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID
                 )
                 ELSE @INSTALLMENT
            END;

        INSERT INTO PAY2_LOAN_SCHED (LOAN_ID, INST_NUM, DUE_PERIOD, AMOUNT)
        VALUES (@LOAN_ID, @I, @DUE_YEAR * 10000 + @DUE_MONTH * 100, @THIS_AMT);

        SET @DUE_MONTH = @DUE_MONTH + 1;
        IF @DUE_MONTH > 12
        BEGIN
            SET @DUE_MONTH = 1;
            SET @DUE_YEAR  = @DUE_YEAR + 1;
        END;

        SET @I = @I + 1;
    END;

    PRINT N'SP_PAY2_LOAN_GEN_SCHED — ' + CAST(@TOTAL_INST AS NVARCHAR) + N' قسط برای وام ' + CAST(@LOAN_ID AS NVARCHAR) + N' ایجاد شد.';
END;
GO

-- ================================================================
-- ۱۰. SP_PAY2_CARRYOVER_LEAVE — انتقال مانده مرخصی به سال بعد
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CARRYOVER_LEAVE]
    @FROM_YEAR INT,
    @TO_YEAR   INT,
    @WS_ID     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CARRYOVER_MAX INT;
    SELECT @CARRYOVER_MAX = CAST(CFG_VALUE AS INT)
    FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_CARRYOVER_MAX';

    DECLARE @LEAVE_MINS_PER_DAY INT;
    SELECT @LEAVE_MINS_PER_DAY = CAST(CFG_VALUE AS INT)
    FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_MINS_PER_DAY';

    DECLARE @MAX_CARRY_MIN INT = @CARRYOVER_MAX * @LEAVE_MINS_PER_DAY;

    UPDATE PAY2_LEAVE_BAL
    SET CARRIED_OUT_MIN = CASE
        WHEN BALANCE_MIN > @MAX_CARRY_MIN THEN @MAX_CARRY_MIN
        WHEN BALANCE_MIN < 0 THEN 0
        ELSE BALANCE_MIN
    END,
    UPDATED_AT = GETDATE()
    WHERE YEAR = @FROM_YEAR
      AND (@WS_ID IS NULL OR EMP_ID IN (
          SELECT EMP_ID FROM PAY2_EMPLOYEE WHERE WS_ID = @WS_ID
      ));

    DECLARE @ANNUAL_DAYS INT;
    SELECT @ANNUAL_DAYS = CAST(CFG_VALUE AS INT) FROM PAY2_CONFIG WHERE CFG_KEY='LEAVE_ANNUAL_DAYS';
    DECLARE @ENTITLEMENT INT = @ANNUAL_DAYS * @LEAVE_MINS_PER_DAY;

    INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN)
    SELECT
        LB.EMP_ID, @TO_YEAR, @ENTITLEMENT, 0, LB.CARRIED_OUT_MIN
    FROM PAY2_LEAVE_BAL LB
    WHERE LB.YEAR = @FROM_YEAR
      AND (@WS_ID IS NULL OR LB.EMP_ID IN (SELECT EMP_ID FROM PAY2_EMPLOYEE WHERE WS_ID = @WS_ID))
      AND NOT EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL X WHERE X.EMP_ID = LB.EMP_ID AND X.YEAR = @TO_YEAR);

    PRINT N'SP_PAY2_CARRYOVER_LEAVE — انتقال از ' + CAST(@FROM_YEAR AS NVARCHAR) + N' به ' + CAST(@TO_YEAR AS NVARCHAR) + N' انجام شد.';
END;
GO

-- ================================================================
-- ۱۱. SP_PAY2_NEW_PERIOD — ایجاد دوره ماهیانه جدید
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_NEW_PERIOD]
    @WS_ID        INT,
    @PERIOD_DATE  BIGINT,
    @HOLIDAY_DAYS TINYINT = 0,
    @OPENED_BY    INT     = NULL,
    @NEW_PER_ID   INT     OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM PAY2_PERIOD WHERE WS_ID=@WS_ID AND PERIOD_DATE=@PERIOD_DATE)
    BEGIN
        RAISERROR(N'SP_PAY2_NEW_PERIOD: دوره %I64d برای کارگاه %d قبلاً ایجاد شده است.', 16, 1, @PERIOD_DATE, @WS_ID);
        RETURN;
    END;

    INSERT INTO PAY2_PERIOD (WS_ID, PERIOD_DATE, HOLIDAY_DAYS, STATUS, OPENED_AT)
    VALUES (@WS_ID, @PERIOD_DATE, @HOLIDAY_DAYS, 1, GETDATE());

    SET @NEW_PER_ID = SCOPE_IDENTITY();

    PRINT N'SP_PAY2_NEW_PERIOD — دوره ' + CAST(@PERIOD_DATE AS NVARCHAR) + N' با PER_ID=' + CAST(@NEW_PER_ID AS NVARCHAR) + N' ایجاد شد.';
END;
GO
";
                ExecuteBatches(db, procScript);

                // ===========================================================
                // 3. Modify â تغییرات ساختاری و بازنویسی Procedureهای خاص
                // ===========================================================
                // بخش اصلاح شده و کامل modify1
                string modify1 = @"
-- ================================================================
-- ۱. اصلاح ساختار ستون ACC_T در صورت قدیمی بودن دیتابیس
-- ================================================================
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.PAY2_EMPLOYEE')
      AND name = 'ACC_T'
      AND system_type_id = TYPE_ID('int')
)
BEGIN
    PRINT N'در حال تغییر نوع ستون ACC_T از INT به NVARCHAR...';
    ALTER TABLE PAY2_EMPLOYEE ALTER COLUMN ACC_T NVARCHAR(50) NULL;
END;
GO

-- ================================================================
-- ۲. SP_PAY2_GET_ADVANCES — محاسبه مساعده هوشمند (نسخه نهایی — JSON_VALUE)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GET_ADVANCES]
    @PERIOD_DATE  BIGINT,
    @PAYROLL_N_S  FLOAT,
    @WS_ID        INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. خواندن کد کامل حساب مساعده
    DECLARE @FULL_HES NVARCHAR(100);
    SELECT @FULL_HES = ACC_CODE
    FROM PAY2_WORKSHOP_ACC WITH (NOLOCK)
    WHERE WS_ID = @WS_ID AND ACC_KEY = 'ADV_HES';

    IF @FULL_HES IS NULL
    BEGIN
        RAISERROR(N'حساب مساعده (ADV_HES) برای این کارگاه تنظیم نشده است.', 16, 1);
        RETURN;
    END;

    -- 2. پارس کردن کد ترکیبی با استفاده از JSON_VALUE
    DECLARE @JsonArr NVARCHAR(250) = N'[""' + REPLACE(@FULL_HES, '-', '"",""') + N'""]';

    DECLARE @HES_K  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[0]'), '') AS INT);
    DECLARE @HES_M  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[1]'), '') AS INT);
    DECLARE @HES_T  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[2]'), '') AS INT);
    DECLARE @HES_T2 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[3]'), '') AS INT);
    DECLARE @HES_T3 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[4]'), '') AS INT);
    DECLARE @HES_T4 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[5]'), '') AS INT);

    -- بررسی امنیتی حساب
    IF @HES_K IS NULL OR @HES_M IS NULL
    BEGIN
        RAISERROR(N'فرمت حساب مساعده نادرست است. باید حداقل شامل کل و معین باشد (مثال: 112-1).', 16, 1);
        RETURN;
    END;

    -- 3. تعیین سطح اعمال فیلتر کد پرسنل (ACC_T)
    DECLARE @EMP_FILTER_LEVEL TINYINT =
        CASE
            WHEN @HES_T  IS NULL THEN 3
            WHEN @HES_T2 IS NULL THEN 4
            WHEN @HES_T3 IS NULL THEN 5
            ELSE                     6
        END;

    -- 4. خواندن تنظیمات اضافی به صورت ایمن
    DECLARE @USE_T BIT = 1, @MIN_POS BIT = 1, @ADV_SCOPE NVARCHAR(20) = 'CURRENT_MONTH';

    SELECT
        @USE_T     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY = 'ADV_USE_HES_T_FILTER' THEN TRY_CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @MIN_POS   = ISNULL(CAST(MAX(CASE WHEN CFG_KEY = 'ADV_MIN_POSITIVE'   THEN TRY_CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @ADV_SCOPE = ISNULL(MAX(CASE WHEN CFG_KEY = 'ADV_SCOPE' THEN CFG_VALUE END), 'CURRENT_MONTH')
    FROM PAY2_CONFIG WITH (NOLOCK)
    WHERE CFG_KEY IN ('ADV_USE_HES_T_FILTER', 'ADV_MIN_POSITIVE', 'ADV_SCOPE');

    -- 5. محاسبه بازه تاریخ به صورت امن و بدون تقسیم خطرناک
    -- تبدیل 14030700 به بازه 14030700 تا 14030799
    DECLARE @MONTH_START BIGINT = (@PERIOD_DATE / 100) * 100;
    DECLARE @MONTH_END   BIGINT = @MONTH_START + 99;

    -- 6. اجرای کوئری نهایی مالی
    ;WITH AdvBase AS
    (
        SELECT
            E.EMP_ID,
            E.ACC_T                            AS PCODE,
            E.LAST_NAME + N' ' + E.FIRST_NAME  AS FULL_NAME,

            -- مانده خام از حسابداری
            ISNULL((
                SELECT CAST(SUM(D.BED - D.BES) AS BIGINT)
                FROM DEED_HED H
                INNER JOIN DEED_DTL D ON H.N_S = D.N_S
                WHERE
                    D.HES_K = @HES_K
                    AND D.HES_M = @HES_M
                    -- 🚀 فیلتر دقیق سطوح بالادستی (باید دقیقاً برابر با مقدار کانفیگ باشند)
                    AND (@EMP_FILTER_LEVEL <= 3 OR D.HES_T  = @HES_T)
                    AND (@EMP_FILTER_LEVEL <= 4 OR D.HES_T2 = @HES_T2)
                    AND (@EMP_FILTER_LEVEL <= 5 OR D.HES_T3 = @HES_T3)
                    AND (@EMP_FILTER_LEVEL <= 6 OR D.HES_T4 = @HES_T4)

                    -- 🚀 فیلتر سطح پرسنل (یا فعال نیست، یا باید دقیقاً برابر با کد پرسنل باشد)
                    AND (
                        @USE_T = 0
                        OR TRY_CAST(NULLIF(TRIM(E.ACC_T), '') AS INT) =
                           CASE @EMP_FILTER_LEVEL
                                WHEN 3 THEN D.HES_T
                                WHEN 4 THEN D.HES_T2
                                WHEN 5 THEN D.HES_T3
                                WHEN 6 THEN D.HES_T4
                           END
                    )

                    -- 🚀 جلوگیری از نشت داده (سطوح پایین‌تر از پرسنل باید خالی یا صفر باشند)
                    AND (@EMP_FILTER_LEVEL >= 4 OR ISNULL(D.HES_T2, 0) = 0)
                    AND (@EMP_FILTER_LEVEL >= 5 OR ISNULL(D.HES_T3, 0) = 0)
                    AND (@EMP_FILTER_LEVEL >= 6 OR ISNULL(D.HES_T4, 0) = 0)

                    AND H.N_S < ISNULL(@PAYROLL_N_S, 999999999)
                    AND H.OKF = 1
                    AND (
                        @ADV_SCOPE = 'OPEN_BALANCE'
                        OR (H.DATE_S BETWEEN @MONTH_START AND @MONTH_END)
                    )
            ), 0) AS RAW_BALANCE,

            -- استثناهای دستی مساعده
            ISNULL((
                SELECT SUM(EXCL_AMOUNT)
                FROM PAY2_ADVANCE_EXCL WITH (NOLOCK)
                WHERE EMP_ID = E.EMP_ID
                  AND PERIOD_DATE BETWEEN @MONTH_START AND @MONTH_END
            ), 0) AS MANUAL_EXCL

        FROM PAY2_EMPLOYEE E WITH (NOLOCK)
        INNER JOIN PAY2_PERIOD P WITH (NOLOCK)
            ON P.WS_ID = E.WS_ID
            AND P.PERIOD_DATE = @PERIOD_DATE
        WHERE E.WS_ID     = @WS_ID
          AND E.IS_ACTIVE = 1
          AND E.ACC_T IS NOT NULL
    )
    SELECT
        EMP_ID,
        PCODE,
        FULL_NAME,
        RAW_BALANCE,
        MANUAL_EXCL,
        CASE
            WHEN @MIN_POS = 1 AND (RAW_BALANCE - MANUAL_EXCL) <= 0
                THEN 0
            ELSE CASE
                    WHEN (RAW_BALANCE - MANUAL_EXCL) < 0 THEN 0
                    ELSE RAW_BALANCE - MANUAL_EXCL
                 END
        END AS ADVANCE_DEDUCTION
    FROM AdvBase
    OPTION (RECOMPILE);

END;
GO

-- ================================================================
-- ۳. باز کردن قید CK_CALC_BASIS برای مقدار 3 (ساعتی)
-- ================================================================
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_CALC_BASIS'
             AND parent_object_id = OBJECT_ID(N'dbo.PAY2_ITEM_DEF')
             AND definition NOT LIKE '%(3)%')
BEGIN
    ALTER TABLE dbo.PAY2_ITEM_DEF DROP CONSTRAINT CK_CALC_BASIS;
    ALTER TABLE dbo.PAY2_ITEM_DEF ADD CONSTRAINT CK_CALC_BASIS CHECK ([CALC_BASIS] IN (1,2,3));
END;
GO

-- ================================================================
-- ۴. سایر قیدهای احتمالی روی BASIS_OV که مقدار 3 را مجاز نمی‌دانند
-- ================================================================
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) IN ('PAY2_DECREE_LINE', 'PAY2_OVERRIDE', 'PAY2_ITEM_TMPL_LINE')
  AND cc.definition LIKE '%BASIS_OV%'
  AND cc.definition NOT LIKE '%(3)%';
IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

-- ================================================================
-- ۵. پشتیبانی از نوع مرخصی «ساعتی» (مقدار 6) در جدول PAY2_LEAVE
--
-- انواع مرخصی:
--   1=استحقاقی  2=استعلاجی  3=بدون حقوق  4=زایمان  5=مأموریت  6=ساعتی (جدید)
--
-- اگر CHECK CONSTRAINT روی LEV_TYPE مقدار 6 را مجاز نمی‌داند، حذف می‌شود.
-- سقف مرخصی ساعتی (3 ساعت و 20 دقیقه) در سمت سرور (Pay2EmployeesController)
-- و سمت کلاینت اعتبارسنجی می‌شود.
-- ================================================================
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) = 'PAY2_LEAVE'
  AND cc.definition LIKE '%LEV_TYPE%'
  AND cc.definition NOT LIKE '%(6)%';
IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

";
                ExecuteBatches(db, modify1);

                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'POSTAL_CODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [POSTAL_CODE] NVARCHAR(20) NULL;");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'EMPLOYER_NAME') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [EMPLOYER_NAME] NVARCHAR(100) NULL;");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'PROVINCE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD
                        [PROVINCE] NVARCHAR(50) NULL,
                        [CITY] NVARCHAR(50) NULL,
                        [REGISTRATION_NUMBER] NVARCHAR(20) NULL,
                        [SSO_BRANCH] NVARCHAR(50) NULL,
                        [FINANCIAL_MANAGER] NVARCHAR(100) NULL,
                        [ADMIN_MANAGER] NVARCHAR(100) NULL;");

                //-- ساخت ایندکس ترکیبی برای حذف عملیات سورت و اسکن جدول شغل‌ها
                db.Execute(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PAY2_JOB_PERFORMANCE')
                    CREATE NONCLUSTERED INDEX IX_PAY2_JOB_PERFORMANCE ON [dbo].[PAY2_JOB] ([IS_ACTIVE], [JOB_NAME]) INCLUDE ([JOB_ID]);");

                // Migration 009: افزودن تنظیمات حق شیفت به تفکیک کارگاه و پرسنل
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_WS_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_DECREE', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_DEC_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_DECREE_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_DL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_ITEM_TMPL_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_TL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));");

                // Migration 010: افزودن فیلدهای مربوط به روش صدور سند (Dual Deed Modes)
                try
                {
                    db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'DEFAULT_DEED_MODE') IS NULL
                        ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [DEFAULT_DEED_MODE] TINYINT NOT NULL CONSTRAINT DF_WS_DEED_MODE DEFAULT(1);");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در Migration دیتابیس (PAY2_WORKSHOP.DEFAULT_DEED_MODE). آپدیت متوقف شد: {ex.Message}", ex);
                }

                try
                {
                    db.Execute(@"
                    IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_MODE') IS NULL
                        ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_MODE] TINYINT NULL;

                    IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_GENERATOR_VERSION') IS NULL
                        ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_GENERATOR_VERSION] SMALLINT NULL;
                    ");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در Migration دیتابیس (PAY2_RUN.DEED_MODE). آپدیت متوقف شد: {ex.Message}", ex);
                }

                try
                {
                    //-- ================================================================
                    //-- ۱.۵ FN_PAY2_ACC_PARENT — برداشتن آخرین سطح یک کد حساب
                    //-- ================================================================
                    db.Execute(@"
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_ACC_PARENT](@CODE NVARCHAR(50))
RETURNS NVARCHAR(50)
AS
BEGIN
    -- «711-1-1» ← «711-1». اگر کد کمتر از دو سطح داشته باشد NULL برمی‌گردد تا
    -- فراخوان مجبور شود ریشه را صریح تنظیم کند و ما حسابِ ناقص نسازیم.
    SET @CODE = NULLIF(LTRIM(RTRIM(@CODE)), '');
    IF @CODE IS NULL OR CHARINDEX('-', @CODE) = 0 RETURN NULL;
    RETURN NULLIF(LEFT(@CODE, LEN(@CODE) - CHARINDEX('-', REVERSE(@CODE))), '');
END;");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در دیتابیس (FN_PAY2_ACC_PARENT). آپدیت متوقف شد: {ex.Message}", ex);
                }

                try
                {
                    //-- ================================================================
                    //-- ۲. SP_PAY2_GEN_DEED — تولید سند حسابداری حقوق و بیمه
                    //-- ================================================================
                    db.Execute(@"
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GEN_DEED]
    @RUN_ID  INT,
    @CALC_BY INT = NULL,
    @DEED_MODE TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#SalarySplit') IS NOT NULL DROP TABLE #SalarySplit;
    IF OBJECT_ID('tempdb..#FinalArticles') IS NOT NULL DROP TABLE #FinalArticles;
    IF OBJECT_ID('tempdb..#UniqueAccounts') IS NOT NULL DROP TABLE #UniqueAccounts;

    DECLARE @PER_ID INT, @WS_ID INT, @PER_DATE BIGINT;

    SELECT @PER_ID = R.PER_ID, @WS_ID = P.WS_ID, @PER_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    DECLARE @MonthNum INT = (@PER_DATE / 100) % 100;
    DECLARE @MonthName NVARCHAR(10) = CASE @MonthNum
        WHEN 1 THEN N'فروردین' WHEN 2 THEN N'اردیبهشت' WHEN 3 THEN N'خرداد'
        WHEN 4 THEN N'تیر'     WHEN 5 THEN N'مرداد'    WHEN 6 THEN N'شهریور'
        WHEN 7 THEN N'مهر'     WHEN 8 THEN N'آبان'     WHEN 9 THEN N'آذر'
        WHEN 10 THEN N'دی'     WHEN 11 THEN N'بهمن'    WHEN 12 THEN N'اسفند' ELSE N'نامشخص' END;
    DECLARE @ML NVARCHAR(20) = RIGHT('0' + CAST(@MonthNum AS NVARCHAR(2)), 2) + N'-' + @MonthName;

    DECLARE
        @ACC_SALARY_TOLID NVARCHAR(50), @ACC_SALARY_EDARI NVARCHAR(50),
        @ACC_SALARY_FOROSH NVARCHAR(50), @ACC_SALARY_KHADAMAT NVARCHAR(50),
        @ACC_SALARY_PAY NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50),
        @ACC_TAX_PAYABLE NVARCHAR(50), @ACC_INS_EXP NVARCHAR(50),
        @ACC_ADV_HES NVARCHAR(50), @ACC_LOAN_HES NVARCHAR(50),
        @ACC_OTHER_DED_HES NVARCHAR(50);

    -- شاخه‌ی حساب هزینه‌ی هر مرکز (کل-معین) برای سند تفصیلی کامل؛ شماره‌ی
    -- تفصیلی از روی نوع قلم به آن اضافه می‌شود. مستقیماً از همان حساب هزینه‌ی
    -- تنظیم‌شده‌ی کارگاه ساخته می‌شود تا کارگاه‌ها تنظیم تازه‌ای لازم نداشته باشند.
    DECLARE
        @ROOT_TOLID NVARCHAR(50), @ROOT_EDARI NVARCHAR(50),
        @ROOT_FOROSH NVARCHAR(50), @ROOT_KHADAMAT NVARCHAR(50);

    SELECT
        @ACC_SALARY_TOLID   = MAX(CASE WHEN ACC_KEY='SALARY_EXP_TOLID'    THEN ACC_CODE END),
        @ACC_SALARY_EDARI   = MAX(CASE WHEN ACC_KEY='SALARY_EXP_EDARI'    THEN ACC_CODE END),
        @ACC_SALARY_FOROSH  = MAX(CASE WHEN ACC_KEY='SALARY_EXP_FOROSH'   THEN ACC_CODE END),
        @ACC_SALARY_KHADAMAT= MAX(CASE WHEN ACC_KEY='SALARY_EXP_KHADAMAT' THEN ACC_CODE END),
        @ACC_SALARY_PAY     = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE'      THEN ACC_CODE END),
        @ACC_INS_PAYABLE    = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'         THEN ACC_CODE END),
        @ACC_TAX_PAYABLE    = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'         THEN ACC_CODE END),
        @ACC_INS_EXP        = MAX(CASE WHEN ACC_KEY='INS_EXP'             THEN ACC_CODE END),
        @ACC_ADV_HES        = MAX(CASE WHEN ACC_KEY='ADV_HES'             THEN ACC_CODE END),
        @ACC_LOAN_HES       = MAX(CASE WHEN ACC_KEY='LOAN_HES'            THEN ACC_CODE END),
        @ACC_OTHER_DED_HES  = MAX(CASE WHEN ACC_KEY='OTHER_DED_HES'       THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    -- شاخه از خودِ حساب هزینه‌ی همان مرکز ساخته می‌شود: آخرین سطح برداشته
    -- می‌شود («711-1-1» ← «711-1»). پس کارگاه‌های موجود بدون هیچ تنظیم تازه‌ای
    -- سند تفصیلی می‌گیرند.
    SET @ROOT_TOLID    = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_TOLID))),    '');
    SET @ROOT_EDARI    = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_EDARI))),    '');
    SET @ROOT_FOROSH   = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_FOROSH))),   '');
    SET @ROOT_KHADAMAT = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_KHADAMAT))), '');

    IF @DEED_MODE IS NULL
    BEGIN
        SELECT @DEED_MODE = CASE
            WHEN R.DEED_MODE IS NOT NULL THEN R.DEED_MODE
            WHEN R.STATUS >= 2 THEN 1
            ELSE W.DEFAULT_DEED_MODE
        END
        FROM PAY2_RUN R
        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
        INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
        WHERE R.RUN_ID = @RUN_ID;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- گاردهای امنیتی (جلوگیری از منفی شدن خالص و کمبود حساب‌ها)
    -- ─────────────────────────────────────────────────────────────────
    -- خالص منفی برای پرسنلِ فقط‌بیمه (کارکرد رسمی صفر و بدون کسور وام/مساعده/سایر) مجاز است؛
    -- این افراد حقوق نمی‌گیرند و فقط بابت بیمه و مالیات بدهکار می‌شوند.
    DECLARE @NegEmpId INT, @NegEmpName NVARCHAR(100), @NegAmount BIGINT;
    SELECT TOP 1 @NegEmpId = RL.EMP_ID, @NegEmpName = E.LAST_NAME + N' ' + E.FIRST_NAME, @NegAmount = RL.NET_PAY
    FROM PAY2_RUN_LINE RL INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
    WHERE RL.RUN_ID = @RUN_ID AND RL.NET_PAY < 0
      AND NOT (RL.WORK_DAYS = 0 AND RL.LOAN_DED = 0 AND RL.ADVANCE_DED = 0 AND RL.OTHER_DED = 0);

    IF @NegEmpId IS NOT NULL
    BEGIN
        DECLARE @Err1 NVARCHAR(500) = N'صدور سند متوقف شد: خالص پرداختی پرسنل منفی است. کد: ' + CAST(@NegEmpId AS NVARCHAR) + N' | نام: ' + @NegEmpName + N' | مبلغ بدهی: ' + CAST(ABS(@NegAmount) AS NVARCHAR) + N' ریال.';
        RAISERROR(@Err1, 16, 1);
        RETURN;
    END

    IF @ACC_SALARY_PAY IS NULL
    BEGIN
        RAISERROR(N'حساب پرداختنی حقوق (SALARY_PAYABLE) برای کارگاه تنظیم نشده است.', 16, 1);
        RETURN;
    END

    DECLARE @MissingAcc NVARCHAR(MAX) = N'';
    -- در سند تفصیلی کامل، بیمه‌ی کارفرما به حساب هزینه‌ی مرکزِ خودِ پرسنل
    -- (ریشه + تفصیلی ۱۰) می‌رود، نه به حساب تکیِ INS_EXP.
    IF @DEED_MODE <> 3 AND @ACC_INS_EXP IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND INS_EMPLOYER > 0) SET @MissingAcc += N'هزینه بیمه کارفرما، ';
    IF @ACC_INS_PAYABLE IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND (INS_WORKER + INS_EMPLOYER) > 0) SET @MissingAcc += N'اداره بیمه، ';
    IF @ACC_TAX_PAYABLE IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND TAX_AMOUNT > 0) SET @MissingAcc += N'اداره مالیات، ';
    IF @ACC_LOAN_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND LOAN_DED > 0) SET @MissingAcc += N'صندوق وام، ';
    IF @ACC_ADV_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND ADVANCE_DED > 0) SET @MissingAcc += N'حساب مساعده، ';
    -- OTHER_DED دو جزء دارد: «سایر کسورات» دستی (KASR_OTHER) و «کسر کار».
    -- در سند تفصیلی کامل فقط جزء اول به حساب سایر کسورات می‌رود؛ کسر کار
    -- حسابِ مقصد ندارد و هزینه‌ی حقوق را کم می‌کند. پس اگر ماهی فقط کسر کار
    -- داشته باشد، نباید حسابی را مطالبه کنیم که هیچ آرتیکلی به آن نمی‌خورد.
    IF @ACC_OTHER_DED_HES IS NULL AND EXISTS (
        SELECT 1 FROM PAY2_RUN_LINE RL
        LEFT JOIN PAY2_ATTENDANCE A ON A.EMP_ID = RL.EMP_ID AND A.PER_ID = @PER_ID
        WHERE RL.RUN_ID = @RUN_ID
          AND (CASE WHEN @DEED_MODE = 3 THEN ISNULL(A.KASR_OTHER, 0) ELSE RL.OTHER_DED END) > 0
    ) SET @MissingAcc += N'سایر کسورات، ';

    -- حالت ۳ به‌جای این چهار حساب، «ریشه»ی هر مرکز را می‌خواهد (پایین‌تر بررسی می‌شود).
    IF @DEED_MODE <> 3
    BEGIN
        IF @ACC_SALARY_TOLID IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_TOLID > 0) SET @MissingAcc += N'هزینه تولید، ';
        IF @ACC_SALARY_EDARI IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_EDARI > 0) SET @MissingAcc += N'هزینه اداری، ';
        IF @ACC_SALARY_FOROSH IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_FOROSH > 0) SET @MissingAcc += N'هزینه فروش، ';
        IF @ACC_SALARY_KHADAMAT IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_KHADAMAT > 0) SET @MissingAcc += N'هزینه خدمات، ';
    END

    -- حساب هزینه در این حالت «شاخه‌ی حساب مرکز + شماره‌ی تفصیلیِ قلم» است، پس
    -- برای هر مرکزی که کارکرد دارد باید حساب هزینه‌ی آن مرکز تنظیم شده باشد و
    -- دست‌کم دو سطح داشته باشد (وگرنه شاخه‌ای برای ساختن نمی‌ماند).
    IF @DEED_MODE = 3
    BEGIN
        IF @ROOT_TOLID IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_TOLID > 0) SET @MissingAcc += N'هزینه تولید، ';
        IF @ROOT_EDARI IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_EDARI > 0) SET @MissingAcc += N'هزینه اداری، ';
        IF @ROOT_FOROSH IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_FOROSH > 0) SET @MissingAcc += N'هزینه فروش، ';
        IF @ROOT_KHADAMAT IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_KHADAMAT > 0) SET @MissingAcc += N'هزینه خدمات، ';
    END

    -- ریشه‌ی مشترک بین دو مرکزِ فعال یعنی تفکیک مرکز هزینه از بین می‌رود: سند
    -- تراز می‌مانَد ولی هزینه‌ی تولید و اداری روی یک حساب می‌نشیند. این وقتی رخ
    -- می‌دهد که در دفتر حساب، خودِ مرکز هزینه در سطح تفصیلی تعریف شده باشد
    -- (مثل 71-1-1 تا 71-1-4) و ریشه‌ی خودکار برای همه «71-1» شود.
    IF @DEED_MODE = 3
    BEGIN
        DECLARE @DupRoot NVARCHAR(50);
        SELECT TOP 1 @DupRoot = ROOT
        FROM (VALUES (@ROOT_TOLID, 1), (@ROOT_EDARI, 2), (@ROOT_FOROSH, 3), (@ROOT_KHADAMAT, 4)) R(ROOT, CENTER)
        WHERE R.ROOT IS NOT NULL
          AND EXISTS (
              SELECT 1 FROM PAY2_RUN_LINE RL
              INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID
              WHERE RL.RUN_ID = @RUN_ID
                AND ((R.CENTER = 1 AND A.DAYS_TOLID > 0) OR (R.CENTER = 2 AND A.DAYS_EDARI > 0)
                  OR (R.CENTER = 3 AND A.DAYS_FOROSH > 0) OR (R.CENTER = 4 AND A.DAYS_KHADAMAT > 0)))
        GROUP BY ROOT
        HAVING COUNT(*) > 1;

        IF @DupRoot IS NOT NULL
        BEGIN
            DECLARE @ErrDup NVARCHAR(500) = N'صدور سند متوقف شد: بیش از یک مرکز هزینه به شاخه‌ی حساب «'
                + @DupRoot + N'» می‌رسد و هزینه‌ی مراکز روی هم می‌افتد. '
                + N'در «سند تفصیلی کامل»، حساب هزینه‌ی هر مرکز باید شاخه‌ی مستقل خودش را داشته باشد '
                + N'(مثلاً 711-1-1 برای تولید و 712-1-1 برای اداری، نه 71-1-1 و 71-1-2). '
                + N'سرفصل‌های هزینه‌ی کارگاه را مطابق این ساختار تنظیم کنید یا از روش «نیمه‌تفصیلی» استفاده کنید.';
            RAISERROR(@ErrDup, 16, 1);
            RETURN;
        END
    END

    IF LEN(@MissingAcc) > 0
    BEGIN
        -- LEN در T-SQL فاصله‌ی انتهایی را نمی‌شمارد، پس LEN-2 علاوه بر جداکننده یک
        -- کاراکترِ واقعی را هم می‌بُرید («سایر کسورات» → «سایر کسورا»). فقط «،» حذف شود.
        DECLARE @Err2 NVARCHAR(MAX) = N'صدور سند متوقف شد: حساب‌های زیر در تنظیمات کارگاه خالی هستند: ' + SUBSTRING(@MissingAcc, 1, LEN(@MissingAcc)-1);
        RAISERROR(@Err2, 16, 1);
        RETURN;
    END

    DECLARE @BadEmpName NVARCHAR(100), @BadAccT NVARCHAR(50);
    SELECT TOP 1 @BadEmpName = E.LAST_NAME + N' ' + E.FIRST_NAME, @BadAccT = ISNULL(E.ACC_T, N'خالی')
    FROM PAY2_RUN_LINE RL
    INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
    WHERE RL.RUN_ID = @RUN_ID
      AND (
           (@DEED_MODE IN (2, 3))
           OR
           (@DEED_MODE = 1 AND (RL.LOAN_DED > 0 OR RL.ADVANCE_DED > 0 OR RL.OTHER_DED > 0))
      )
      AND (
           NULLIF(TRIM(E.ACC_T), '') IS NULL
           OR TRIM(E.ACC_T) = @ACC_SALARY_PAY
      );

    IF @BadEmpName IS NOT NULL
    BEGIN
        DECLARE @Err4 NVARCHAR(500) = N'صدور سند متوقف شد: کد تفصیلی (ACC_T) برای پرسنل نامعتبر است. حساب پرسنل نمی‌تواند خالی یا برابر با ریشه کل باشد. نام پرسنل: ' + @BadEmpName + N' (' + @BadAccT + N')';
        RAISERROR(@Err4, 16, 1);
        RETURN;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- جدول موقت محاسبات و ایجاد ردیف‌های خام (Summary vs Traceable)
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #SalarySplit (
        EMP_ID INT PRIMARY KEY,
        FULL_NAME NVARCHAR(150),
        ACC_T NVARCHAR(50),
        EXP_TOLID BIGINT,
        EXP_EDARI BIGINT,
        EXP_FOROSH BIGINT,
        EXP_KHADAMAT BIGINT,
        NET_PAY BIGINT,
        PERSONAL_DEBT BIGINT,
        INS_WORKER BIGINT,
        INS_EMPLOYER BIGINT,
        TAX_AMOUNT BIGINT,
        LOAN_DED BIGINT,
        ADVANCE_DED BIGINT,
        OTHER_DED BIGINT
    );

    ;WITH EmpAcc AS (
        SELECT
            E.EMP_ID, E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
            -- ACC_T is a complete account path and may contain all six supported
            -- levels (for example 115-1-25-3-4-6). It must never be appended to
            -- another account hierarchy or reduced to a supposedly portable suffix.
            NULLIF(TRIM(E.ACC_T), '') AS ACC_T
        FROM PAY2_EMPLOYEE E
        INNER JOIN PAY2_RUN_LINE RL ON E.EMP_ID = RL.EMP_ID
        WHERE RL.RUN_ID = @RUN_ID
    ),
    SplitBase AS (
        SELECT
            RL.EMP_ID, RL.GROSS_PAY, A.DAYS_TOLID, A.DAYS_EDARI, A.DAYS_FOROSH, A.DAYS_KHADAMAT,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_TOLID)    / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_T,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_EDARI)    / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_E,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_FOROSH)   / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_F,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_KHADAMAT) / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_K,
            EB.EXP_BASE,
            RL.NET_PAY, RL.INS_WORKER, RL.INS_EMPLOYER, RL.TAX_AMOUNT, RL.LOAN_DED, RL.ADVANCE_DED, RL.OTHER_DED
        FROM PAY2_RUN_LINE RL
        INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID
        -- مبنای هزینه «خالص + کسورات» است، نه مستقیماً GROSS_PAY.
        --
        -- سند، خالص را بستانکار و کسورات را بستانکار می‌کند؛ پس طرف بدهکار
        -- باید دقیقاً جمع همان‌ها باشد. در موتور امروز GROSS_PAY همین است و
        -- این تغییر بی‌اثر می‌مانَد، ولی در اجراهایی که با نسخه‌های قدیمی‌تر
        -- محاسبه شده‌اند خالص جداگانه رُند شده و GROSS_PAY همراهش تغییر نکرده.
        -- آنجا تفاوتِ رُند در هیچ طرف سند نمی‌نشست و سند به همان اندازه ناتراز
        -- می‌شد — یعنی کاربر پیام «سند تراز نیست» می‌گرفت و بازصدور سند آن
        -- ماه‌ها اصلاً ممکن نبود.
        CROSS APPLY (VALUES (RL.NET_PAY + RL.TOTAL_DED)) EB(EXP_BASE)
        WHERE RL.RUN_ID = @RUN_ID
    )
    INSERT INTO #SalarySplit (
        EMP_ID, FULL_NAME, ACC_T, EXP_TOLID, EXP_EDARI, EXP_FOROSH, EXP_KHADAMAT,
        NET_PAY, PERSONAL_DEBT, INS_WORKER, INS_EMPLOYER, TAX_AMOUNT, LOAN_DED, ADVANCE_DED, OTHER_DED
    )
    SELECT
        B.EMP_ID, E.FULL_NAME, E.ACC_T,
        CASE WHEN B.DAYS_TOLID > 0 THEN B.R_T + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_T END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI > 0 THEN B.R_E + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_E END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI = 0 AND B.DAYS_FOROSH > 0 THEN B.R_F + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_F END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI = 0 AND B.DAYS_FOROSH = 0 THEN B.R_K + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_K END,
        B.NET_PAY,
        CASE WHEN B.NET_PAY < 0 THEN -B.NET_PAY ELSE 0 END,
        B.INS_WORKER, B.INS_EMPLOYER, B.TAX_AMOUNT, B.LOAN_DED, B.ADVANCE_DED, B.OTHER_DED
    FROM SplitBase B
    INNER JOIN EmpAcc E ON B.EMP_ID = E.EMP_ID;

    -- ─────────────────────────────────────────────────────────────────
    -- جمع‌آوری مقادیر نهایی در جدول برای ولیدیشن حساب‌ها
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #FinalArticles (
        HES_CODE NVARCHAR(100) COLLATE database_default,
        SHARH NVARCHAR(500),
        BED BIGINT,
        BES BIGINT,
        ACC_KEY NVARCHAR(50),
        EMP_ID INT NULL,
        EmployeeName NVARCHAR(150),
        SortOrder INT
    );

    IF @DEED_MODE = 1
    BEGIN
        INSERT INTO #FinalArticles
        SELECT CAST(@ACC_SALARY_TOLID AS NVARCHAR(100)), CAST(N'هزینه حقوق تولید ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_TOLID) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_TOLID' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 1
        FROM #SalarySplit HAVING SUM(EXP_TOLID) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_EDARI AS NVARCHAR(100)), CAST(N'هزینه حقوق اداری ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_EDARI) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_EDARI' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 2
        FROM #SalarySplit HAVING SUM(EXP_EDARI) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_FOROSH AS NVARCHAR(100)), CAST(N'هزینه حقوق فروش ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_FOROSH) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_FOROSH' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 3
        FROM #SalarySplit HAVING SUM(EXP_FOROSH) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_KHADAMAT AS NVARCHAR(100)), CAST(N'هزینه حقوق خدمات ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_KHADAMAT) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_KHADAMAT' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 4
        FROM #SalarySplit HAVING SUM(EXP_KHADAMAT) > 0
        UNION ALL
        SELECT CAST(@ACC_INS_EXP AS NVARCHAR(100)), CAST(N'هزینه بیمه کارفرما ' + @ML AS NVARCHAR(500)), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST(0 AS BIGINT), CAST('INS_EXP' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_PAY AS NVARCHAR(100)), CAST(N'حقوق پرداختنی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(CASE WHEN NET_PAY > 0 THEN NET_PAY ELSE 0 END) AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 6
        FROM #SalarySplit HAVING SUM(CASE WHEN NET_PAY > 0 THEN NET_PAY ELSE 0 END) > 0
        UNION ALL
        -- بدهی پرسنل فقط از خالص منفی می‌آید؛ سهم کارفرما در ردیف INS_EXP ثبت شده است.
        SELECT CAST(@ACC_SALARY_PAY AS NVARCHAR(100)), CAST(N'بدهی بیمه و مالیات پرسنل ' + @ML AS NVARCHAR(500)), CAST(SUM(PERSONAL_DEBT) AS BIGINT), CAST(0 AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 6
        FROM #SalarySplit HAVING SUM(PERSONAL_DEBT) > 0
        UNION ALL
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه تأمین اجتماعی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_WORKER + INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 7
        FROM #SalarySplit HAVING SUM(INS_WORKER + INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(TAX_AMOUNT) AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 8
        FROM #SalarySplit HAVING SUM(TAX_AMOUNT) > 0
        UNION ALL
        SELECT CAST(@ACC_LOAN_HES AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 9
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL
        SELECT CAST(@ACC_ADV_HES AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 10
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL
        SELECT CAST(@ACC_OTHER_DED_HES AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(OTHER_DED AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 11
        FROM #SalarySplit WHERE OTHER_DED > 0;
    END
    ELSE IF @DEED_MODE = 2
    BEGIN
        INSERT INTO #FinalArticles
        SELECT CAST(@ACC_SALARY_TOLID AS NVARCHAR(100)), CAST(N'هزینه حقوق تولید ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_TOLID AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_TOLID' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 1
        FROM #SalarySplit WHERE EXP_TOLID > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_EDARI AS NVARCHAR(100)), CAST(N'هزینه حقوق اداری ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_EDARI AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_EDARI' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 2
        FROM #SalarySplit WHERE EXP_EDARI > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_FOROSH AS NVARCHAR(100)), CAST(N'هزینه حقوق فروش ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_FOROSH AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_FOROSH' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 3
        FROM #SalarySplit WHERE EXP_FOROSH > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_KHADAMAT AS NVARCHAR(100)), CAST(N'هزینه حقوق خدمات ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_KHADAMAT AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_KHADAMAT' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 4
        FROM #SalarySplit WHERE EXP_KHADAMAT > 0
        UNION ALL
        SELECT CAST(@ACC_INS_EXP AS NVARCHAR(100)), CAST(N'هزینه بیمه کارفرما ' + @ML AS NVARCHAR(500)), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST(0 AS BIGINT), CAST('INS_EXP' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'حقوق پرداختنی: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(NET_PAY AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE NET_PAY > 0
        UNION ALL
        -- برای پرسنل فقط‌بیمه نیز حساب شخص صرفاً به اندازه خالص منفی بدهکار می‌شود.
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'بدهی بیمه و مالیات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(PERSONAL_DEBT AS BIGINT), CAST(0 AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE PERSONAL_DEBT > 0
        UNION ALL
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه سهم کارگر ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(INS_WORKER AS BIGINT), CAST('INS_PAYABLE_W' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 7
        FROM #SalarySplit WHERE INS_WORKER > 0
        UNION ALL
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه سهم کارفرما ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE_E' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 8
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(TAX_AMOUNT AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 9
        FROM #SalarySplit WHERE TAX_AMOUNT > 0
        UNION ALL
        SELECT CAST(@ACC_LOAN_HES AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 10
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL
        SELECT CAST(@ACC_ADV_HES AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 11
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL
        SELECT CAST(@ACC_OTHER_DED_HES AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(OTHER_DED AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 12
        FROM #SalarySplit WHERE OTHER_DED > 0;
    END
    ELSE IF @DEED_MODE = 3
    BEGIN
        -- ═════════════════════════════════════════════════════════════
        -- سند تفصیلی کامل
        --   بدهکار : هزینه به تفکیک «مرکز هزینه × نوع قلم» + کسورات هر پرسنل
        --   بستانکار: اقلام حکم هر پرسنل + حساب‌های مقصد کسورات
        -- مانده‌ی حساب تفصیلی هر پرسنل دقیقاً «خالص پرداختی» او می‌شود.
        -- ═════════════════════════════════════════════════════════════

        -- ── ۱) اقلام پرداختیِ هر پرسنل ────────────────────────────────
        -- مبنا همان چیزی است که موتور برای GROSS_PAY استفاده می‌کند:
        -- ریلِ «رسمی». وقتی هر دو ریل در حکم هستند BASE_SAL (اسمی) کنار
        -- گذاشته می‌شود، وگرنه حقوق پایه دوبار شمرده می‌شود.
        CREATE TABLE #EmpItem (
            EMP_ID    INT,
            ITEM_ID   INT,
            ITEM_NAME NVARCHAR(200),
            TAFSILI   SMALLINT,
            AMOUNT    BIGINT,
            SORT      SMALLINT
        );

        -- کدام ریل «پرداختی» است از روی خودِ GROSS_PAY تشخیص داده می‌شود، نه با
        -- قاعده‌ی ثابت. موتورِ امروز ریل رسمی را می‌پردازد (BASE_SAL کنار می‌رود)
        -- ولی اجراهایی که با نسخه‌های قدیمی‌تر محاسبه شده‌اند ریل اسمی را
        -- پرداخته‌اند. با قاعده‌ی ثابت، سندِ آن ماه‌ها به اندازه‌ی فاصله‌ی دو ریل
        -- غلط می‌شد — اختلافی که هم‌جنسِ گِردکردن نیست و نباید جذب شود.
        -- جدول موقت در tempdb ساخته می‌شود و در نصب‌هایی که collation دیتابیس
        -- حسابداری با tempdb فرق دارد، مقایسه DROP_CODE با ITEM_CODE_SNAP بدون
        -- COLLATE صریح با خطای 468 متوقف می‌شود.
        CREATE TABLE #Rail (
            EMP_ID INT PRIMARY KEY,
            DROP_CODE NVARCHAR(30) COLLATE database_default NULL
        );

        ;WITH Sums AS (
            SELECT D.EMP_ID,
                   SUM(D.AMOUNT) AS S_ALL,
                   SUM(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL'   THEN D.AMOUNT ELSE 0 END) AS S_NOM,
                   SUM(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL_B' THEN D.AMOUNT ELSE 0 END) AS S_OFF,
                   MAX(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL'   THEN 1 ELSE 0 END) AS HAS_NOM,
                   MAX(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL_B' THEN 1 ELSE 0 END) AS HAS_OFF
            FROM PAY2_RUN_DETAIL D
            LEFT JOIN PAY2_ITEM_DEF I ON I.ITEM_ID = D.ITEM_ID
            WHERE D.RUN_ID = @RUN_ID
              AND ISNULL(D.ITEM_TYPE_SNAP, I.ITEM_TYPE) IN (1, 2)
              AND D.AMOUNT <> 0
            GROUP BY D.EMP_ID
        )
        -- سه رفتار تاریخی دیده شده است: کنار گذاشتن ریل اسمی (موتور امروز)،
        -- کنار گذاشتن ریل رسمی، و اصلاً کنار نگذاشتن هیچ‌کدام. به‌جای تطبیق
        -- دقیق، گزینه‌ای انتخاب می‌شود که جمعش کمترین فاصله را با هدف دارد؛
        -- این‌طور اختلافِ گِردکردن انتخاب را خراب نمی‌کند.
        INSERT INTO #Rail (EMP_ID, DROP_CODE)
        SELECT S.EMP_ID, X.DROP_CODE
        FROM Sums S
        INNER JOIN PAY2_RUN_LINE RL ON RL.RUN_ID = @RUN_ID AND RL.EMP_ID = S.EMP_ID
        -- OUTER (نه CROSS): پرسنلی که فقط یک ریل دارند باید بمانند و هیچ قلمی
        -- از آن‌ها کنار گذاشته نشود، وگرنه کلاً از سند حذف می‌شدند.
        OUTER APPLY (
            SELECT TOP 1 C.DROP_CODE
            FROM (VALUES
                    (CAST('BASE_SAL'   AS NVARCHAR(30)), S.S_ALL - S.S_NOM, 1),
                    (CAST('BASE_SAL_B' AS NVARCHAR(30)), S.S_ALL - S.S_OFF, 2),
                    (CAST(NULL         AS NVARCHAR(30)), S.S_ALL,           3)
                 ) C(DROP_CODE, TOTAL, PREF)
            WHERE S.HAS_NOM = 1 AND S.HAS_OFF = 1
            -- تساوی: اولویت با قاعده‌ی موتور امروز (کنار گذاشتن ریل اسمی)
            ORDER BY ABS(C.TOTAL - (RL.NET_PAY + RL.TOTAL_DED)), C.PREF
        ) X;

        INSERT INTO #EmpItem (EMP_ID, ITEM_ID, ITEM_NAME, TAFSILI, AMOUNT, SORT)
        SELECT D.EMP_ID, D.ITEM_ID,
               ISNULL(ISNULL(D.ITEM_NAME_SNAP, I.ITEM_NAME), N'قلم حکم'),
               ISNULL(I.EXP_TAFSILI, 9),
               D.AMOUNT,
               ISNULL(I.SORT_ORDER, 999)
        FROM PAY2_RUN_DETAIL D
        LEFT JOIN PAY2_ITEM_DEF I ON I.ITEM_ID = D.ITEM_ID
        INNER JOIN #Rail R ON R.EMP_ID = D.EMP_ID
        WHERE D.RUN_ID = @RUN_ID
          AND ISNULL(D.ITEM_TYPE_SNAP, I.ITEM_TYPE) IN (1, 2)
          AND D.AMOUNT <> 0
          AND ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) COLLATE database_default
              <> ISNULL(R.DROP_CODE, N'') COLLATE database_default;

        -- تعدیلِ گِردکردنِ خالص جزو هیچ قلمی نیست. ستون ROUNDING_ADJ مبنا نیست
        -- چون در اجراهای قدیمی‌تر NULL است؛ ضمناً بسته به نسخه‌ی موتور، گِردکردن
        -- گاهی داخل GROSS_PAY تا شده و گاهی بین GROSS_PAY و NET_PAY نشسته است.
        -- تنها چیزی که در همه‌ی نسخه‌ها معتبر است این تساوی است:
        --     جمع بستانکارِ اقلام = NET_PAY + TOTAL_DED
        -- پس هدف را از روی همان می‌سازیم تا مانده‌ی حساب پرسنل دقیقاً خالص شود.
        --
        -- فقط اختلافِ هم‌اندازه‌ی گِردکردن جذب می‌شود؛ اگر اختلاف بزرگ بود یعنی
        -- ایرادِ ساختاری است (مثلاً ریل حقوق اشتباه انتخاب شده) و باید گاردِ
        -- پایین آن را با پیام روشن بگیرد، نه اینکه بی‌صدا روی حقوق تَه‌نشین شود.
        DECLARE @ROUND_TOLERANCE BIGINT = 10000;

        -- پرسنلِ فقط‌بیمه هیچ قلم پرداختی ندارند، پس تعدیل جایی برای نشستن
        -- نداشت. یک ردیفِ صفرِ حامل ساخته می‌شود تا مسیرِ تعدیل برای همه یکسان
        -- بماند؛ اگر تعدیلی نگیرد، با فیلترِ AMOUNT > 0 آرتیکلی تولید نمی‌کند.
        INSERT INTO #EmpItem (EMP_ID, ITEM_ID, ITEM_NAME, TAFSILI, AMOUNT, SORT)
        SELECT SS.EMP_ID, -1, N'تعدیل گِردکردن خالص', 1, 0, 0
        FROM #SalarySplit SS
        WHERE NOT EXISTS (SELECT 1 FROM #EmpItem EI WHERE EI.EMP_ID = SS.EMP_ID);

        ;WITH Target AS (
            SELECT EI.EMP_ID, EI.ITEM_ID,
                   ROW_NUMBER() OVER (PARTITION BY EI.EMP_ID
                                      ORDER BY CASE WHEN EI.TAFSILI = 1 THEN 0 ELSE 1 END,
                                               EI.SORT, EI.ITEM_ID) AS RN
            FROM #EmpItem EI
        ),
        Diff AS (
            SELECT RL.EMP_ID, (RL.NET_PAY + RL.TOTAL_DED) - SUM(EI.AMOUNT) AS ADJ
            FROM PAY2_RUN_LINE RL
            INNER JOIN #EmpItem EI ON EI.EMP_ID = RL.EMP_ID
            WHERE RL.RUN_ID = @RUN_ID
            GROUP BY RL.EMP_ID, RL.NET_PAY, RL.TOTAL_DED
            HAVING ABS((RL.NET_PAY + RL.TOTAL_DED) - SUM(EI.AMOUNT)) BETWEEN 1 AND @ROUND_TOLERANCE
        )
        UPDATE EI
        SET EI.AMOUNT = EI.AMOUNT + D.ADJ
        FROM #EmpItem EI
        INNER JOIN Target T ON T.EMP_ID = EI.EMP_ID AND T.ITEM_ID = EI.ITEM_ID AND T.RN = 1
        INNER JOIN Diff   D ON D.EMP_ID = EI.EMP_ID;

        -- ── ۲) سهم هر مرکز هزینه از کارکرد هر پرسنل ───────────────────
        -- IS_PRIMARY همان اولویتی است که بقیه‌ی رویه هم دارد (تولید ← اداری
        -- ← فروش ← خدمات) و باقیمانده‌ی گِردکردن روی همان مرکز می‌نشیند.
        CREATE TABLE #EmpCenter (
            EMP_ID     INT,
            CENTER     TINYINT,
            DAYS       DECIMAL(9,2),
            TOT_DAYS   DECIMAL(9,2),
            IS_PRIMARY BIT
        );

        INSERT INTO #EmpCenter (EMP_ID, CENTER, DAYS, TOT_DAYS, IS_PRIMARY)
        SELECT X.EMP_ID, X.CENTER, X.DAYS, X.TOT,
               CASE WHEN X.CENTER = (SELECT MIN(Y.CENTER) FROM (
                        SELECT A2.EMP_ID, V2.CENTER, V2.DAYS
                        FROM PAY2_ATTENDANCE A2
                        CROSS APPLY (VALUES (1, A2.DAYS_TOLID), (2, A2.DAYS_EDARI),
                                            (3, A2.DAYS_FOROSH), (4, A2.DAYS_KHADAMAT)) V2(CENTER, DAYS)
                        WHERE A2.PER_ID = @PER_ID AND V2.DAYS > 0
                    ) Y WHERE Y.EMP_ID = X.EMP_ID) THEN 1 ELSE 0 END
        FROM (
            SELECT A.EMP_ID, V.CENTER, V.DAYS,
                   (A.DAYS_TOLID + A.DAYS_EDARI + A.DAYS_FOROSH + A.DAYS_KHADAMAT) AS TOT
            FROM PAY2_ATTENDANCE A
            INNER JOIN PAY2_RUN_LINE RL ON RL.EMP_ID = A.EMP_ID AND RL.RUN_ID = @RUN_ID
            CROSS APPLY (VALUES (1, A.DAYS_TOLID), (2, A.DAYS_EDARI),
                                (3, A.DAYS_FOROSH), (4, A.DAYS_KHADAMAT)) V(CENTER, DAYS)
            WHERE A.PER_ID = @PER_ID AND V.DAYS > 0
        ) X;

        -- پرسنلِ «فقط‌بیمه» کارکردی در هیچ مرکزی ندارند ولی بیمه‌ی سهم کارفرما
        -- دارند. بدون این ردیف، آن بیمه به هیچ حساب هزینه‌ای نمی‌رفت و سند
        -- ناتراز می‌شد. اولین مرکزِ دارای ریشه به عنوان مقصد پیش‌فرض برمی‌دارد.
        INSERT INTO #EmpCenter (EMP_ID, CENTER, DAYS, TOT_DAYS, IS_PRIMARY)
        SELECT SS.EMP_ID,
               CASE WHEN @ROOT_TOLID IS NOT NULL THEN 1
                    WHEN @ROOT_EDARI IS NOT NULL THEN 2
                    WHEN @ROOT_FOROSH IS NOT NULL THEN 3
                    ELSE 4 END,
               1, 1, 1
        FROM #SalarySplit SS
        WHERE NOT EXISTS (SELECT 1 FROM #EmpCenter EC WHERE EC.EMP_ID = SS.EMP_ID)
          AND COALESCE(@ROOT_TOLID, @ROOT_EDARI, @ROOT_FOROSH, @ROOT_KHADAMAT) IS NOT NULL;

        -- ── ۳) پخش هر قلم روی مراکز هزینه ─────────────────────────────
        CREATE TABLE #ExpAlloc (
            CENTER  TINYINT,
            TAFSILI SMALLINT,
            AMOUNT  BIGINT
        );

        ;WITH Part AS (
            SELECT EI.EMP_ID, EI.ITEM_ID, EI.TAFSILI, EC.CENTER, EC.IS_PRIMARY, EI.AMOUNT,
                   CAST(ROUND(EI.AMOUNT * EC.DAYS / NULLIF(EC.TOT_DAYS, 0), 0) AS BIGINT) AS PART
            FROM #EmpItem EI
            INNER JOIN #EmpCenter EC ON EC.EMP_ID = EI.EMP_ID
        ),
        Fixed AS (
            SELECT *, SUM(PART) OVER (PARTITION BY EMP_ID, ITEM_ID) AS SUM_PART FROM Part
        )
        INSERT INTO #ExpAlloc (CENTER, TAFSILI, AMOUNT)
        SELECT CENTER, TAFSILI,
               SUM(PART + CASE WHEN IS_PRIMARY = 1 THEN AMOUNT - SUM_PART ELSE 0 END)
        FROM Fixed
        GROUP BY CENTER, TAFSILI;

        -- بیمه‌ی سهم کارفرما هزینه‌ی همان مرکز است (تفصیلی ۱۰).
        ;WITH Part AS (
            SELECT SS.EMP_ID, EC.CENTER, EC.IS_PRIMARY, SS.INS_EMPLOYER AS AMOUNT,
                   CAST(ROUND(SS.INS_EMPLOYER * EC.DAYS / NULLIF(EC.TOT_DAYS, 0), 0) AS BIGINT) AS PART
            FROM #SalarySplit SS
            INNER JOIN #EmpCenter EC ON EC.EMP_ID = SS.EMP_ID
            WHERE SS.INS_EMPLOYER > 0
        ),
        Fixed AS (
            SELECT *, SUM(PART) OVER (PARTITION BY EMP_ID) AS SUM_PART FROM Part
        )
        INSERT INTO #ExpAlloc (CENTER, TAFSILI, AMOUNT)
        SELECT CENTER, 10, SUM(PART + CASE WHEN IS_PRIMARY = 1 THEN AMOUNT - SUM_PART ELSE 0 END)
        FROM Fixed
        GROUP BY CENTER;

        -- «کسر کار» حسابِ مقصد ندارد: بدهکارِ حساب پرسنل می‌شود و در مقابل،
        -- هزینه‌ی حقوقِ همان مرکز را کم می‌کند (دقیقاً رفتار سند قدیمی).
        INSERT INTO #ExpAlloc (CENTER, TAFSILI, AMOUNT)
        SELECT EC.CENTER, 1, -SUM(SH.SHORTAGE_DED)
        FROM (
            SELECT SS.EMP_ID, SS.OTHER_DED - ISNULL(A.KASR_OTHER, 0) AS SHORTAGE_DED
            FROM #SalarySplit SS
            INNER JOIN PAY2_ATTENDANCE A ON A.EMP_ID = SS.EMP_ID AND A.PER_ID = @PER_ID
        ) SH
        INNER JOIN #EmpCenter EC ON EC.EMP_ID = SH.EMP_ID AND EC.IS_PRIMARY = 1
        WHERE SH.SHORTAGE_DED > 0
        GROUP BY EC.CENTER;

        -- ── ۴) آرتیکل‌ها ──────────────────────────────────────────────
        INSERT INTO #FinalArticles
        -- (الف) هزینه: مرکز هزینه × نوع قلم
        -- شرح، نوع قلم را هم می‌گوید: بدون آن همه‌ی خطوط یک مرکز شرح یکسان
        -- می‌گرفتند و در دفتر حسابداری «711-1-2» از «711-1-11» قابل تشخیص نبود.
        SELECT CAST(R.ROOT + N'-' + CAST(X.TAFSILI AS NVARCHAR(10)) AS NVARCHAR(100)),
               CAST(N'هزینه ' + R.CNAME + N' — '
                    + ISNULL(TL.LBL, N'قلم ' + CAST(X.TAFSILI AS NVARCHAR(10)))
                    + N' ' + @ML AS NVARCHAR(500)),
               CAST(SUM(X.AMOUNT) AS BIGINT), CAST(0 AS BIGINT),
               CAST('EXP_' + R.CKEY AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 1
        FROM #ExpAlloc X
        INNER JOIN (VALUES (1, @ROOT_TOLID,    N'تولید',  'TOLID'),
                           (2, @ROOT_EDARI,    N'اداری',  'EDARI'),
                           (3, @ROOT_FOROSH,   N'فروش',   'FOROSH'),
                           (4, @ROOT_KHADAMAT, N'خدمات',  'KHADAMAT')) R(CENTER, ROOT, CNAME, CKEY)
             ON R.CENTER = X.CENTER
        -- برچسب‌ها ثابت‌اند چون یک تفصیلی چند قلم را می‌پوشاند (مثلاً ۱ هم
        -- حقوق پایه است هم سنوات) و نامِ یکی از آن‌ها گمراه‌کننده می‌شد.
        LEFT JOIN (VALUES (1, N'حقوق'), (2, N'اضافه‌کار'), (3, N'راندمان'),
                          (4, N'حق اولاد'), (5, N'خواربار و مسکن'), (9, N'سایر'),
                          (10, N'بیمه سهم کارفرما'), (11, N'حق شیفت'),
                          (12, N'بن کارگری'), (13, N'حق تأهل و سنوات')) TL(T, LBL)
             ON TL.T = X.TAFSILI
        GROUP BY R.ROOT, R.CNAME, R.CKEY, X.TAFSILI, TL.LBL
        HAVING SUM(X.AMOUNT) <> 0

        UNION ALL
        -- (ب) اقلام حکم هر پرسنل روی حساب تفصیلی خودش
        SELECT CAST(SS.ACC_T AS NVARCHAR(100)),
               CAST(EI.ITEM_NAME + N' ' + @ML + N' | ' + SS.FULL_NAME AS NVARCHAR(500)),
               CAST(0 AS BIGINT), CAST(EI.AMOUNT AS BIGINT),
               CAST('EMP_ITEM' AS NVARCHAR(50)), CAST(EI.EMP_ID AS INT), CAST(SS.FULL_NAME AS NVARCHAR(150)), 2
        FROM #EmpItem EI
        INNER JOIN #SalarySplit SS ON SS.EMP_ID = EI.EMP_ID
        WHERE EI.AMOUNT > 0

        UNION ALL
        -- (ج) کسورات هر پرسنل: بدهکارِ حساب خودش
        SELECT CAST(SS.ACC_T AS NVARCHAR(100)),
               CAST(D.LABEL + N' ' + @ML + N' | ' + SS.FULL_NAME AS NVARCHAR(500)),
               CAST(D.AMOUNT AS BIGINT), CAST(0 AS BIGINT),
               CAST('EMP_DED' AS NVARCHAR(50)), CAST(SS.EMP_ID AS INT), CAST(SS.FULL_NAME AS NVARCHAR(150)), 3
        FROM #SalarySplit SS
        INNER JOIN PAY2_ATTENDANCE A ON A.EMP_ID = SS.EMP_ID AND A.PER_ID = @PER_ID
        CROSS APPLY (VALUES
            (N'کسر بیمه سهم کارگر', SS.INS_WORKER),
            (N'کسر مالیات',         SS.TAX_AMOUNT),
            (N'کسر قسط وام',        SS.LOAN_DED),
            (N'تصفیه مساعده',       SS.ADVANCE_DED),
            (N'سایر کسورات',        ISNULL(A.KASR_OTHER, 0)),
            (N'کسر کار',            SS.OTHER_DED - ISNULL(A.KASR_OTHER, 0))
        ) D(LABEL, AMOUNT)
        WHERE D.AMOUNT > 0

        UNION ALL
        -- (د) حساب‌های مقصد کسورات («کسر کار» مقصد ندارد؛ هزینه را کم کرده است)
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه تأمین اجتماعی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_WORKER + INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 4
        FROM #SalarySplit HAVING SUM(INS_WORKER + INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(TAX_AMOUNT) AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(TAX_AMOUNT) > 0
        UNION ALL
        SELECT CAST(@ACC_LOAN_HES AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL
        SELECT CAST(@ACC_ADV_HES AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 7
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL
        SELECT CAST(@ACC_OTHER_DED_HES AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + SS.FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(A.KASR_OTHER AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(SS.EMP_ID AS INT), CAST(SS.FULL_NAME AS NVARCHAR(150)), 8
        FROM #SalarySplit SS
        INNER JOIN PAY2_ATTENDANCE A ON A.EMP_ID = SS.EMP_ID AND A.PER_ID = @PER_ID
        WHERE ISNULL(A.KASR_OTHER, 0) > 0;

        DROP TABLE #EmpItem;
        DROP TABLE #EmpCenter;
        DROP TABLE #ExpAlloc;
        DROP TABLE #Rail;

        -- ── ۵) گاردِ آشتی ─────────────────────────────────────────────
        -- مانده‌ی حساب هر پرسنل باید دقیقاً خالص پرداختی او باشد. اگر قلمی از
        -- RUN_DETAIL جا افتاده باشد یا ریلِ حقوق اشتباه انتخاب شده باشد، سند
        -- ممکن است در جمع کل تراز بمانَد ولی حساب اشخاص غلط باشد؛ این گارد
        -- دقیقاً همان حالت را می‌گیرد.
        DECLARE @MismatchName NVARCHAR(150), @MismatchExp BIGINT, @MismatchAct BIGINT;
        SELECT TOP 1
            @MismatchName = SS.FULL_NAME,
            @MismatchExp  = SS.NET_PAY,
            @MismatchAct  = ISNULL(F.BES, 0) - ISNULL(F.BED, 0)
        FROM #SalarySplit SS
        OUTER APPLY (
            SELECT SUM(FA.BES) AS BES, SUM(FA.BED) AS BED
            FROM #FinalArticles FA
            WHERE FA.EMP_ID = SS.EMP_ID AND FA.ACC_KEY IN ('EMP_ITEM', 'EMP_DED')
        ) F
        WHERE ISNULL(F.BES, 0) - ISNULL(F.BED, 0) <> SS.NET_PAY;

        IF @MismatchName IS NOT NULL
        BEGIN
            DECLARE @ErrRec NVARCHAR(500) = N'صدور سند متوقف شد: مانده‌ی حساب پرسنل با خالص پرداختی نمی‌خواند. نام: '
                + @MismatchName + N' | خالص پرداختی: ' + CAST(@MismatchExp AS NVARCHAR(30))
                + N' | مانده‌ی آرتیکل‌ها: ' + CAST(@MismatchAct AS NVARCHAR(30))
                + N'. لطفاً محاسبه‌ی این ماه را دوباره اجرا کنید.';
            RAISERROR(@ErrRec, 16, 1);
            RETURN;
        END
    END

    -- ─────────────────────────────────────────────────────────────────
    -- 🚨 اعتبارسنجی Set-Based سطح دیتابیس (جلوگیری از ساخت دیتای یتیم)
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #UniqueAccounts (
        HES_CODE NVARCHAR(100) COLLATE database_default
    );

    INSERT INTO #UniqueAccounts (HES_CODE)
    SELECT DISTINCT HES_CODE FROM #FinalArticles;

    DECLARE @MissingAccounts NVARCHAR(MAX) = N'';

    ;WITH Parsed AS (
        SELECT
            HES_CODE,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[0]') AS INT) AS K,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[1]') AS INT) AS M,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[2]') AS INT) AS T1,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[3]') AS INT) AS T2,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[4]') AS INT) AS T3,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[5]') AS INT) AS T4
        FROM #UniqueAccounts
    ),
    Leveled AS (
        SELECT *,
            CASE
                WHEN T4 IS NOT NULL THEN 6
                WHEN T3 IS NOT NULL THEN 5
                WHEN T2 IS NOT NULL THEN 4
                WHEN T1 IS NOT NULL THEN 3
                WHEN M IS NOT NULL THEN 2
                ELSE 1
            END AS Lvl
        FROM Parsed
    )
    SELECT @MissingAccounts = @MissingAccounts + U.HES_CODE + N', '
    FROM Leveled U
    LEFT JOIN TOTA_HES K ON U.K = K.NUMBER AND U.Lvl = 1
    LEFT JOIN DETA_HES M ON U.K = M.N_KOL AND U.M = M.NUMBER AND U.Lvl = 2
    LEFT JOIN TDETA_HES T1 ON U.K = T1.N_KOL AND U.M = T1.NUMBER AND U.T1 = T1.TNUMBER AND U.Lvl = 3
    LEFT JOIN TDETA_HES2 T2 ON U.K = T2.N_KOL AND U.M = T2.NUMBER AND U.T1 = T2.TNUMBER AND U.T2 = T2.TNUMBER2 AND U.Lvl = 4
    LEFT JOIN TDETA_HES3 T3 ON U.K = T3.N_KOL AND U.M = T3.NUMBER AND U.T1 = T3.TNUMBER AND U.T2 = T3.TNUMBER2 AND U.T3 = T3.TNUMBER3 AND U.Lvl = 5
    LEFT JOIN TDETA_HES4 T4 ON U.K = T4.N_KOL AND U.M = T4.NUMBER AND U.T1 = T4.TNUMBER AND U.T2 = T4.TNUMBER2 AND U.T3 = T4.TNUMBER3 AND U.T4 = T4.TNUMBER4 AND U.Lvl = 6
    WHERE
        (U.Lvl = 1 AND K.NUMBER IS NULL) OR
        (U.Lvl = 2 AND M.NUMBER IS NULL) OR
        (U.Lvl = 3 AND T1.TNUMBER IS NULL) OR
        (U.Lvl = 4 AND T2.TNUMBER2 IS NULL) OR
        (U.Lvl = 5 AND T3.TNUMBER3 IS NULL) OR
        (U.Lvl = 6 AND T4.TNUMBER4 IS NULL) OR
        U.Lvl > 6 OR
        U.T1 IS NULL; -- 🚀 تغییر حیاتی: U.M به U.T1 تغییر یافت تا حساب‌های کمتر از ۳ سطح بلوکه شوند

    IF LEN(@MissingAccounts) > 0
    BEGIN
        -- LEN در T-SQL فاصله‌ی انتهایی را نمی‌شمارد، پس LEN-2 علاوه بر جداکننده یک
        -- کاراکترِ واقعی را هم می‌بُرید («سایر کسورات» → «سایر کسورا»). فقط «،» حذف شود.
        DECLARE @ErrAcc NVARCHAR(MAX) = N'صدور سند متوقف شد. حساب‌های زیر نامعتبرند یا فاقد حداقل ۳ سطح (کل-معین-تفصیلی) می‌باشند: ' + SUBSTRING(@MissingAccounts, 1, LEN(@MissingAccounts)-1);
        RAISERROR(@ErrAcc, 16, 1);
        RETURN;
    END

    SELECT HES_CODE, SHARH, BED, BES, ACC_KEY, EMP_ID, EmployeeName
    FROM #FinalArticles
    ORDER BY SortOrder, EmployeeName;

    DROP TABLE #SalarySplit;
    DROP TABLE #FinalArticles;
    DROP TABLE #UniqueAccounts;
END;");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در دیتابیس (SP_PAY2_GEN_DEED). آپدیت متوقف شد: {ex.Message}", ex);
                }

                // ===========================================================
                // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
                // ===========================================================
                string aclScript = @"
IF OBJECT_ID(N'dbo.PAY2_USER_WS', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAY2_USER_WS]
    (
        [USERCO] INT      NOT NULL,
        [WS_ID]  INT      NOT NULL,
        [CRT]    DATETIME NULL CONSTRAINT DF_PUW_CRT DEFAULT(GETDATE()),
        [UID]    INT      NULL,
        CONSTRAINT PK_PAY2_USER_WS PRIMARY KEY ([USERCO],[WS_ID]),
        CONSTRAINT FK_PUW_WS FOREIGN KEY ([WS_ID])
            REFERENCES [dbo].[PAY2_WORKSHOP]([WS_ID]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_PAY2_USER_WS_USER ON [dbo].[PAY2_USER_WS]([USERCO]);
END;
GO

IF OBJECT_ID(N'dbo.PAY2_SEC_AUDIT', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAY2_SEC_AUDIT]
    (
        [AUDIT_ID]  BIGINT        NOT NULL IDENTITY(1,1),
        [USERCO]    INT           NOT NULL,
        [USER_NAME] NVARCHAR(50)  NULL,
        [FORM_NAME] NVARCHAR(50)  NULL,
        [PERM_FLAG] NVARCHAR(10)  NULL,
        [WS_ID]     INT           NULL,
        [ENTITY_KEY] NVARCHAR(50) NULL,
        [ALLOWED]   BIT           NOT NULL,
        [HTTP_METHOD] NVARCHAR(10) NULL,
        [PATH]      NVARCHAR(300) NULL,
        [IP]        NVARCHAR(45)  NULL,
        [DETAILS]   NVARCHAR(MAX) NULL,
        [CRT]       DATETIME      NOT NULL CONSTRAINT DF_PSA_CRT DEFAULT(GETDATE()),
        CONSTRAINT PK_PAY2_SEC_AUDIT PRIMARY KEY ([AUDIT_ID])
    );
    CREATE NONCLUSTERED INDEX IX_PAY2_SEC_AUDIT_USER_DATE
        ON [dbo].[PAY2_SEC_AUDIT]([USERCO],[CRT] DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_DASHBOARD')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_DASHBOARD', N'داشبورد حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_WORKSHOP')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_WORKSHOP', N'کارگاه‌ها و سرفصل‌های حسابداری', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_EMPLOYEE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_EMPLOYEE', N'پرسنل، قرارداد و مرخصی', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_DECREE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_DECREE', N'احکام کارگزینی', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ATTENDANCE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ATTENDANCE', N'کارکرد ماهیانه', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ADVANCE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ADVANCE', N'مساعده', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_LOAN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_LOAN', N'وام پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_RUN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_RUN', N'محاسبه حقوق ماهیانه', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_SETTLEMENT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_SETTLEMENT', N'تسویه حساب پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ITEM_DEF')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ITEM_DEF', N'تعریف آیتم‌های حکم و قالب‌ها', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_SETTINGS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_SETTINGS', N'تنظیمات حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_REPORTS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_REPORTS', N'گزارش‌های حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_CALC')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_CALC', N'اجرای محاسبه حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_FINALIZE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_FINALIZE', N'نهایی‌کردن محاسبه حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_REVERT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_REVERT', N'برگشت محاسبه حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_DEED')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_DEED', N'صدور سند حسابداری حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_DEED_UNDO')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_DEED_UNDO', N'ابطال سند حسابداری حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_PERIOD_CLOSE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_PERIOD_CLOSE', N'بستن دوره کارکرد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_PERIOD_REOPEN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_PERIOD_REOPEN', N'بازگشایی/حذف دوره کارکرد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_DECREE_CONFIRM')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_DECREE_CONFIRM', N'تأیید نهایی حکم کارگزینی', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_SETTLE_FINALIZE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_SETTLE_FINALIZE', N'نهایی‌کردن و برگشت تسویه حساب', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_VIEW_AMOUNTS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_VIEW_AMOUNTS', N'مشاهده مبالغ حقوق سایر پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_CONFIG_CRITICAL')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_CONFIG_CRITICAL', N'تغییر تنظیمات حساس (نرخ بیمه/مالیات/سقف)', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_ADV_EXCL')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_ADV_EXCL', N'ثبت استثنای دستی مساعده', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_OVERRIDE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_OVERRIDE', N'تغییر مشمولیت بیمه/مالیات پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_EXPORT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_EXPORT', N'خروجی اکسل/PDF و دیسکت بیمه و مالیات', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ADMIN_ACL')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ADMIN_ACL', N'مدیریت دسترسی‌های حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO


IF COL_LENGTH(N'dbo.PAY2_EMPLOYEE', N'USERCO') IS NULL
BEGIN
    ALTER TABLE dbo.PAY2_EMPLOYEE ADD [USERCO] INT NULL;
    EXEC('CREATE UNIQUE NONCLUSTERED INDEX UX_EMP_USERCO ON dbo.PAY2_EMPLOYEE([USERCO]) WHERE [USERCO] IS NOT NULL');
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_SELF_PAYSLIP')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_SELF_PAYSLIP', N'فیش حقوقی من', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_ENFORCE')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_ENFORCE', N'0', N'1|0', N'0', N'امنیت', N'فعال‌سازی کنترل دسترسی حقوق و دستمزد', N'۰ = خاموش (وضعیت فعلی). ۱ = کنترل دسترسی‌ها کاملا فعال و اعمال می‌شود.', NULL, N'BOOL', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_WS_SCOPE_ENFORCE')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_WS_SCOPE_ENFORCE', N'1', N'1|0', N'1', N'امنیت', N'محدودکردن کاربر به کارگاه‌های مجاز', N'۱ = کارگاه‌های مجاز هر فرد کنترل می‌شود', NULL, N'BOOL', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_CACHE_SECONDS')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_CACHE_SECONDS', N'120', NULL, N'120', N'امنیت', N'مدت نگهداری کش دسترسی‌ها (ثانیه)', NULL, NULL, N'INT', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_AUDIT_DENIED')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_AUDIT_DENIED', N'1', N'1|0', N'1', N'امنیت', N'ثبت لاگ تلاش‌های ناموفق دسترسی', NULL, NULL, N'BOOL', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_AUDIT_SENSITIVE')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_AUDIT_SENSITIVE', N'1', N'1|0', N'1', N'امنیت', N'ثبت لاگ عملیات حساس', NULL, NULL, N'BOOL', 1);
GO

-- این چهار کلید موقع اضافه شدن، OPT_LABELS نداشتند؛ صفحه‌ی تنظیمات به‌جای
-- برچسب فارسی، خودِ عدد خام «۰»/«۱» را نشان می‌داد. روی نصب‌های قبلی هم
-- (که این کلیدها را از قبل INSERT کرده‌اند) این UPDATE لازم است، چون
-- IF NOT EXISTS بالا برای آن‌ها دیگر اجرا نمی‌شود.
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'روشن — دسترسی‌ها اعمال می‌شود|خاموش — همه به همه‌چیز دسترسی دارند' WHERE CFG_KEY = N'ACL_ENFORCE';
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'محدود به کارگاه‌های مجاز|بدون محدودیت کارگاه' WHERE CFG_KEY = N'ACL_WS_SCOPE_ENFORCE';
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'ثبت می‌شود|ثبت نمی‌شود' WHERE CFG_KEY = N'ACL_AUDIT_DENIED';
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'ثبت می‌شود|ثبت نمی‌شود' WHERE CFG_KEY = N'ACL_AUDIT_SENSITIVE';
GO

UPDATE dbo.PAY2_CONFIG
SET DESC_FA = N'منسوخ — جایگزین: کنترل دسترسی مبتنی بر SAL_CHEK'
WHERE CFG_KEY IN (N'CONFIG_MIN_ROLE', N'ITEM_DEF_MIN_ROLE');
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK WHERE [OBJECT] IN (SELECT IDH FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ADMIN_ACL'))
BEGIN
    DECLARE @Users TABLE (IDD INT);
    INSERT INTO @Users SELECT USERCO FROM dbo.SAL_CHEK WHERE [OBJECT] = (SELECT IDH FROM dbo.TFORMS WHERE FORMNAME = N'DEED_HEAD') AND RUN = 1
    AND USERCO IN (SELECT IDD FROM dbo.SALA_DTL WHERE ENABL = 1);

    IF NOT EXISTS (SELECT 1 FROM @Users)
    BEGIN
        INSERT INTO @Users SELECT TOP 1 IDD FROM dbo.SALA_DTL WHERE ENABL = 1 ORDER BY IDD ASC;
    END

    INSERT INTO dbo.SAL_CHEK ([USERCO], [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
    SELECT U.IDD, F.IDH, 1, 1, 1, 1, 1, GETDATE()
    FROM @Users U
    CROSS JOIN dbo.TFORMS F
    WHERE F.FORMNAME LIKE N'PAY2!_%' ESCAPE N'!'
      AND NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK SC WHERE SC.[USERCO] = U.IDD AND SC.[OBJECT] = F.IDH);

    INSERT INTO dbo.PAY2_USER_WS ([USERCO], [WS_ID], [CRT])
    SELECT U.IDD, W.WS_ID, GETDATE()
    FROM @Users U
    CROSS JOIN dbo.PAY2_WORKSHOP W
    WHERE W.IS_ACTIVE = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.PAY2_USER_WS UW WHERE UW.[USERCO] = U.IDD AND UW.[WS_ID] = W.WS_ID);

    PRINT N'مجوز اولیه (Bootstrap) برای کاربران صادر شد.';
END;
GO
";
                ExecuteBatches(db, aclScript);
                LoadJobData(db);

                // ===========================================================
                // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
                // ===========================================================

                return;
            }

                // ===========================================================
                // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
                // ===========================================================



                // ===========================================================
                // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
                // ===========================================================


            }

        private static void ExecuteBatchesTransactional(SqlConnection db, string script)
        {
            using var transaction = db.BeginTransaction();
            try
            {
                ExecuteBatches(db, script, transaction);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (InvalidOperationException) { /* SQL Server may already have rolled back after XACT_ABORT. */ }
                throw;
            }
        }
        private static void ExecuteBatches(SqlConnection db, string script, SqlTransaction? transaction = null)
        {
            // Safely split the script ONLY when "GO" is on its own line
            var commands = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var cmdText in commands)
            {
                if (!string.IsNullOrWhiteSpace(cmdText))
                {
                    try
                    {
                        db.Execute(cmdText, transaction: transaction);
                    }
                    catch (SqlException ex)
                    {
                        // Logging the exact error and query batch that failed so you can actually debug it
                        Console.WriteLine($"SQL Execution Error:\n{ex.Message}\nFailed Batch:\n{cmdText}\n");
                        // If a critical procedure fails to create, you might want to throw the error here
                        throw;
                    }
                }
            }
        }
        private static void LoadJobData(SqlConnection db)
        {
            const string JobFilePath = @"C:\CORRECT\joby.sql";

            if (!File.Exists(JobFilePath))
            {
                Console.WriteLine($"[LoadJobData] فایل پیدا نشد: {JobFilePath}");
                return;
            }

            // ── بررسی وجود داده قبلی ─────────────────────────────
            var existingCount = db.ExecuteScalar<int>("SELECT COUNT(*) FROM [dbo].[PAY2_JOB]");
            if (existingCount > 0)
            {
                Console.WriteLine($"[LoadJobData] PAY2_JOB از قبل {existingCount} رکورد دارد — رد شد.");
                return;
            }

            Console.WriteLine("[LoadJobData] در حال خواندن joby.sql ...");

            // فایل UTF-16LE است
            string[] lines = File.ReadAllLines(JobFilePath, System.Text.Encoding.Unicode);

            // ── Parse سطرهای INSERT با Regex ─────────────────────
            // نمونه: INSERT [dbo].[PAY2_JOB] ([JOB_ID],...) VALUES (1, N'1', N'2', N'3', 1)
            var insertRx = new Regex(
                @"VALUES\s*\(\s*(\d+)\s*,\s*N'((?:[^']|'')*)'\s*,\s*N'((?:[^']|'')*)'\s*,\s*(?:N'((?:[^']|'')*)'|NULL)\s*,\s*(\d+)\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var table = new DataTable();
            table.Columns.Add("JOB_ID", typeof(int));
            table.Columns.Add("JOB_CODE", typeof(string));
            table.Columns.Add("JOB_NAME", typeof(string));
            table.Columns.Add("JOB_GROUP", typeof(string));
            table.Columns.Add("IS_ACTIVE", typeof(bool));

            int parsed = 0;
            foreach (string line in lines)
            {
                var m = insertRx.Match(line);
                if (!m.Success) continue;

                table.Rows.Add(
                    int.Parse(m.Groups[1].Value),           // JOB_ID
                    m.Groups[2].Value.Replace("''", "'"),   // JOB_CODE
                    m.Groups[3].Value.Replace("''", "'"),   // JOB_NAME
                    m.Groups[4].Success && m.Groups[4].Value.Length > 0
                        ? (object)m.Groups[4].Value.Replace("''", "'")
                        : DBNull.Value,                     // JOB_GROUP (nullable)
                    m.Groups[5].Value == "1"                // IS_ACTIVE
                );
                parsed++;
            }

            if (parsed == 0)
            {
                Console.WriteLine("[LoadJobData] هیچ سطر INSERT ای parse نشد.");
                return;
            }

            Console.WriteLine($"[LoadJobData] {parsed} رکورد parse شد — در حال BulkCopy ...");

            // ── SqlBulkCopy ────────────────
            using var tx = db.BeginTransaction();
            try
            {
                using var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.KeepIdentity, tx)
                {
                    DestinationTableName = "[dbo].[PAY2_JOB]",
                    BatchSize = 1000,
                    BulkCopyTimeout = 600
                };
                bulk.ColumnMappings.Add("JOB_ID", "JOB_ID");
                bulk.ColumnMappings.Add("JOB_CODE", "JOB_CODE");
                bulk.ColumnMappings.Add("JOB_NAME", "JOB_NAME");
                bulk.ColumnMappings.Add("JOB_GROUP", "JOB_GROUP");
                bulk.ColumnMappings.Add("IS_ACTIVE", "IS_ACTIVE");

                bulk.WriteToServer(table);
                tx.Commit();
                Console.WriteLine($"[LoadJobData] {parsed} رکورد با موفقیت در PAY2_JOB درج شد.");
            }
            catch (Exception ex)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    Console.WriteLine($"[LoadJobData] خطا در Rollback: {rollbackEx.Message}");
                }

                Console.WriteLine($"[LoadJobData] خطا در BulkCopy: {ex.Message}");
                throw;
            }
        }
    }


}
