/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۱ از ۳ : ساختار جداول

   هیچ جدول موجودی تغییر نمی‌کند. همه چیز با پیشوند CC_ اضافه می‌شود.
   قابل اجرای مکرر: اگر جدولی از قبل باشد، دست‌نخورده می‌ماند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

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
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    UnitId   INT           NOT NULL REFERENCES dbo.CC_Unit(UnitId),
    HesKol   INT           NOT NULL,
    CostKind TINYINT       NOT NULL,          -- 1=دستمزد 2=سربار
    Ratio    DECIMAL(9,6)  NOT NULL DEFAULT 1,
    IsActive BIT           NOT NULL DEFAULT 1,
    Note     NVARCHAR(200) NULL,
    CONSTRAINT UQ_CC_UnitAcc UNIQUE (UnitId, HesKol)
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
