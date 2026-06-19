using Dapper;
using Microsoft.Data.SqlClient;
using Prg_SendInvoice.CNNMANAGER;
using System;
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
            CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
            using (var db = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
            {
                db.Open();
                SalaryScript(true, db);
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
 N'per پرسنل|کل معین',        'BOOL', 2),

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
    [WS_NAME]         NVARCHAR(100) NOT NULL,                                    -- نام کارگاه
    [EMPLOYER_NAME]   NVARCHAR(100) NULL,                                        -- نام کارفرما (فیلد جدید v6)
    [NATIONAL_ID]     NVARCHAR(11)  NULL,                                        -- شناسه ملی کارگاه
    [SOCIAL_INS_CODE] NVARCHAR(20)  NULL,                                        -- کد کارگاه نزد تأمین اجتماعی
    [TAX_CODE]        NVARCHAR(20)  NULL,                                        -- شناسه مالیاتی
    [POSTAL_CODE]     NVARCHAR(20)  NULL,                                        -- کد پستی کارگاه (فیلد جدید v6)
    [ADDRESS]         NVARCHAR(300) NULL,
    [PHONE]           NVARCHAR(30)  NULL,
    [INS_MODE]        TINYINT       NOT NULL CONSTRAINT DF_WS_INS DEFAULT(1),    -- 1=کارگاه معمولی (SANAD) | 2=تبصره ماده ۷ (SANAD10)
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
('BASE_SAL_B',  N'حقوق روزانه رسمی',        1, 1, 1, 1, 1, 2, 1, 1),   -- SALARY_DAYLYB
('BASE_SAL',    N'حقوق روزانه اسمی',         1, 1, 1, 1, 1, 2, 1, 2),   -- SALARY_DAYLY
('HOME',        N'خواربار و مسکن',           1, 1, 1, 1, 1, 2, 1, 3),   -- قانون ۲۸ روز
('CHILDREN',    N'حق اولاد',                 1, 1, 1, 0, 1, 2, 1, 4),   -- معاف مالیات
('FAMILY_ALLOW',N'حق تأهل',                  1, 2, 1, 1, 1, 2, 1, 5),   -- ماهیانه
('ATTRACT',     N'حق جذب',                   1, 2, 1, 1, 1, 2, 1, 6),
('GROCERY',     N'بن کارگری',                1, 1, 0, 0, 1, 2, 1, 7),   -- معاف بیمه/مالیات
('HARD_COND',   N'شرایط محیط کار',           1, 2, 1, 1, 1, 2, 1, 8),
('NAHAR',       N'حق نهار',                  1, 2, 0, 0, 2, 2, 1, 9),   -- معاف بیمه/مالیات
('SHIFT',       N'حق شیفت/نوبت/شب‌کاری',    1, 1, 1, 1, 1, 2, 1, 10),  -- درصد از BASE_SAL_B
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
    [DEF_AMOUNT] DECIMAL(18,2)  NOT NULL CONSTRAINT DF_TL_AMT DEFAULT(0),
    [INS_OV]    BIT      NULL,                                               -- NULL=از تعریف آیتم
    [TAX_OV]    BIT      NULL,
    [BASIS_OV]  TINYINT  NULL,

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
    [ISSUED_DATE]  BIGINT        NOT NULL,                                   -- تاریخ صدور شمسی
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
    [AMOUNT]   DECIMAL(18,2)   NOT NULL CONSTRAINT DF_DL_AMT DEFAULT(0),
    [INS_OV]   BIT      NULL,                                                -- NULL=از PAY2_ITEM_DEF
    [TAX_OV]   BIT      NULL,
    [BASIS_OV] TINYINT  NULL,

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
    [DEED_ID_SAL] INT          NULL,                                         -- شماره سند حقوق در حسابداری
    [DEED_ID_INS] INT          NULL,                                         -- شماره سند بیمه
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

-- ── ۲۵. PAY2_RUN_DETAIL — ریز آیتمی (فیش تفصیلی و حسابرسی) ────
IF OBJECT_ID(N'dbo.PAY2_RUN_DETAIL', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_RUN_DETAIL]
(
    [RUN_ID]      INT    NOT NULL,
    [EMP_ID]      INT    NOT NULL,
    [ITEM_ID]     INT    NOT NULL,
    [AMOUNT]      BIGINT NOT NULL,
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
GO
";
                ExecuteBatches(db, tablescript);

                // ===========================================================
                // 2. Stored Procedures â CREATE OR ALTER (همیشه آخرین نسخه)
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
-- ۱. SP_PAY2_CALC_RUN — موتور محاسبه حقوق ماهیانه
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

    -- گام ۱ — بارگذاری تنظیمات
    DECLARE
        @MONTH_DAYS_MODE   NVARCHAR(10), @MONTH_DAYS        TINYINT,
        @OT_NORMAL_MULT    DECIMAL(6,4), @OT_HOLIDAY_MULT   DECIMAL(6,4),
        @OT_HOUR_BASE      DECIMAL(6,4), @SHIFT_MODE        NVARCHAR(10),
        @ROUND_MODE        INT,          @INS_WORKER_RATE   DECIMAL(6,4),
        @INS_EMPLOYER_RATE DECIMAL(6,4), @INS_UNEMP_RATE    DECIMAL(6,4),
        @INS_CEILING_APPLY BIT,          @INS_CEILING       BIGINT,
        @TAX_YEAR          SMALLINT,     @TAX_EXEMPT        BIGINT,
        @TAX_DEDUCT_INS    BIT,          @TAX_DEP_APPLY     BIT,
        @ADV_ENABLED       BIT,          @PERIOD_DATE       BIGINT,
        @PERIOD_MONTH      INT;

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
        @ADV_ENABLED       = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='ADV_ENABLED'        THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0)
    FROM PAY2_CONFIG
    WHERE CFG_KEY IN (
        'MONTH_DAYS_MODE','OT_NORMAL_MULT','OT_HOLIDAY_MULT','OT_HOUR_BASE',
        'SHIFT_MODE','ROUND_MODE','INS_WORKER_RATE','INS_EMPLOYER_RATE',
        'INS_UNEMP_RATE','INS_CEILING_APPLY','INS_CEILING_MONTHLY',
        'TAX_YEAR','TAX_EXEMPT_MONTHLY','TAX_DEDUCT_INS',
        'TAX_DEPRIVATION_APPLY','ADV_ENABLED'
    );

    SELECT @PERIOD_DATE = PERIOD_DATE FROM PAY2_PERIOD WHERE PER_ID = @PER_ID;
    IF @PERIOD_DATE IS NULL
    BEGIN
        RAISERROR(N'دوره %d یافت نشد.', 16, 1, @PER_ID);
        RETURN;
    END;

    SET @MONTH_DAYS = CASE WHEN @MONTH_DAYS_MODE = '30' THEN 30 ELSE 30 END;
    SET @PERIOD_MONTH = @PERIOD_DATE / 100;  

    -- گام ۲ — ایجاد هدر PAY2_RUN
    DECLARE @PREV_RUN_ID INT = NULL;
    DECLARE @NEXT_RUN_NO SMALLINT = 1;

    IF @IS_RERUN = 1
    BEGIN
        SELECT TOP 1
            @PREV_RUN_ID = RUN_ID,
            @NEXT_RUN_NO = RUN_NO + 1
        FROM PAY2_RUN
        WHERE PER_ID = @PER_ID AND IS_LATEST = 1
        ORDER BY RUN_NO DESC;

        UPDATE PAY2_RUN SET IS_LATEST = 0 WHERE PER_ID = @PER_ID;
    END;

    INSERT INTO PAY2_RUN (PER_ID, RUN_NO, IS_LATEST, CALC_AT, CALC_BY, STATUS, PREV_RUN_ID)
    VALUES (@PER_ID, @NEXT_RUN_NO, 1, GETDATE(), @CALC_BY, 1, @PREV_RUN_ID);

    SET @NEW_RUN_ID = SCOPE_IDENTITY();

    CREATE TABLE #AdvResult (
        EMP_ID             INT, PCODE NVARCHAR(50), FULL_NAME NVARCHAR(150),
        RAW_BALANCE        BIGINT, MANUAL_EXCL BIGINT, ADVANCE_DEDUCTION  BIGINT
    );

    IF @ADV_ENABLED = 1
    BEGIN
        INSERT INTO #AdvResult (EMP_ID, PCODE, FULL_NAME, RAW_BALANCE, MANUAL_EXCL, ADVANCE_DEDUCTION)
        EXEC SP_PAY2_GET_ADVANCES
            @PERIOD_DATE = @PERIOD_DATE,
            @PAYROLL_N_S = @PAYROLL_N_S,
            @WS_ID       = @WS_ID;
    END;

    -- گام ۳ — حلقه روی پرسنل فعال کارگاه
    DECLARE
        @EMP_ID INT, @IS_MANAGER BIT, @INS_TYPE TINYINT,
        @TAX_EXEMPT_FLAG BIT, @REGION_DEP TINYINT, @ACC_T NVARCHAR(50); 

    DECLARE cur_emp CURSOR LOCAL FAST_FORWARD FOR
        SELECT E.EMP_ID, E.IS_MANAGER, E.INS_TYPE, E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.ACC_T
        FROM PAY2_EMPLOYEE E
        WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
          AND EXISTS (SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID = @PER_ID AND A.EMP_ID = E.EMP_ID);

    OPEN cur_emp;
    FETCH NEXT FROM cur_emp INTO @EMP_ID, @IS_MANAGER, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE
            @WORK_DAYS DECIMAL(5,2)=0, @DAYS DECIMAL(5,2)=0, @DAYSB DECIMAL(5,2)=0,
            @FRID_COUNT TINYINT=0, @TDAYS DECIMAL(5,2)=0, @OT_NORMAL_H DECIMAL(6,2)=0,
            @OT_HOLIDAY_H DECIMAL(6,2)=0, @OT_ADMIN_H DECIMAL(6,2)=0, @LEAVE_DAYS DECIMAL(5,2)=0,
            @PERF_AMOUNT BIGINT=0, @TRANSP_AMOUNT BIGINT=0, @KASR_OTHER BIGINT=0;

        SELECT
            @WORK_DAYS = ISNULL(WORK_DAYS,0), @DAYS = ISNULL(DAYS,0), @DAYSB = ISNULL(DAYSB,0),
            @FRID_COUNT = ISNULL(FRID_COUNT,0), @TDAYS = ISNULL(TDAYS,0), @OT_NORMAL_H = ISNULL(OT_NORMAL_H,0),
            @OT_HOLIDAY_H = ISNULL(OT_HOLIDAY_H,0), @OT_ADMIN_H = ISNULL(OT_ADMIN_H,0), @LEAVE_DAYS = ISNULL(LEAVE_DAYS,0),
            @PERF_AMOUNT = ISNULL(PERF_AMOUNT,0), @TRANSP_AMOUNT = ISNULL(TRANSP_AMOUNT,0), @KASR_OTHER = ISNULL(KASR_OTHER,0)
        FROM PAY2_ATTENDANCE WHERE PER_ID = @PER_ID AND EMP_ID = @EMP_ID;

        CREATE TABLE #ItemCalc (
            ITEM_ID INT, ITEM_CODE NVARCHAR(30), ITEM_TYPE TINYINT,
            AMOUNT BIGINT, INS_SUBJECT BIT, TAX_SUBJECT BIT
        );

        -- گام ۴ — یافتن حکم معتبر
        DECLARE @DEC_ID INT, @DEC_FROM BIGINT, @DEC_TO BIGINT;

        DECLARE cur_dec CURSOR LOCAL FAST_FORWARD FOR
            SELECT DEC_ID, EFF_FROM, ISNULL(EFF_TO, 99991231)
            FROM PAY2_DECREE
            WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1
              AND EFF_FROM <= @PERIOD_DATE + 30   
              AND (EFF_TO IS NULL OR EFF_TO >= @PERIOD_DATE)
            ORDER BY EFF_FROM;

        OPEN cur_dec;
        FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- گام ۵ — محاسبه هر آیتم حکم
            DECLARE
                @ITEM_ID INT, @ITEM_CODE NVARCHAR(30), @ITEM_TYPE TINYINT, @ITEM_AMOUNT DECIMAL(18,2),
                @ITEM_BASIS TINYINT, @ITEM_INS BIT, @ITEM_TAX BIT, @ITEM_PBD TINYINT,  
                @OV_INS BIT, @OV_TAX BIT, @OV_BASIS TINYINT, @CALC_AMOUNT BIGINT = 0;

            DECLARE cur_line CURSOR LOCAL FAST_FORWARD FOR
                SELECT DL.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, ISNULL(DL.AMOUNT, 0),
                    ISNULL(DL.BASIS_OV, ID.CALC_BASIS), ISNULL(DL.INS_OV, ID.INS_SUBJECT), ISNULL(DL.TAX_OV, ID.TAX_SUBJECT), ID.PAY_BASE_DAYS
                FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
                WHERE DL.DEC_ID = @DEC_ID AND ID.IS_ACTIVE = 1 AND ID.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                ORDER BY ID.SORT_ORDER;

            OPEN cur_line;
            FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                -- v6.1: ریست متغیرها قبل از خواندن استثنا — اگر SELECT ردیفی برنگرداند،
                -- مقادیر استثنای آیتم قبلی نباید به این آیتم نشت کند (باگ موروثی v6.0)
                SELECT @OV_INS = NULL, @OV_TAX = NULL, @OV_BASIS = NULL;

                SELECT TOP 1 @OV_INS = INS_OV, @OV_TAX = TAX_OV, @OV_BASIS = BASIS_OV
                FROM PAY2_OVERRIDE WHERE EMP_ID = @EMP_ID AND ITEM_ID = @ITEM_ID
                  AND VALID_FROM <= @PERIOD_DATE AND (VALID_TO IS NULL OR VALID_TO >= @PERIOD_DATE)
                ORDER BY VALID_FROM DESC;

                IF @OV_INS IS NOT NULL SET @ITEM_INS = @OV_INS;
                IF @OV_TAX IS NOT NULL SET @ITEM_TAX = @OV_TAX;
                IF @OV_BASIS IS NOT NULL SET @ITEM_BASIS = @OV_BASIS;

                -- v6.1: مبنای ساعتی (3) — مبلغ ثبت‌شده در حکم، «نرخ هر ساعت» است
                IF @ITEM_BASIS = 3
                BEGIN
                    SET @CALC_AMOUNT =
                        CASE @ITEM_CODE
                            WHEN 'OT_NORMAL'  THEN CAST(@ITEM_AMOUNT * @OT_NORMAL_H  AS BIGINT)
                            WHEN 'OT_HOLIDAY' THEN CAST(@ITEM_AMOUNT * @OT_HOLIDAY_H AS BIGINT)
                            WHEN 'OT_ADMIN'   THEN CAST(@ITEM_AMOUNT * @OT_ADMIN_H   AS BIGINT)
                            -- سایر آیتم‌های ساعتی: نرخ ساعتی × (روز کارکرد × ساعت کاری روزانه)
                            ELSE CAST(@ITEM_AMOUNT * (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @OT_HOUR_BASE AS BIGINT)
                        END;
                END;
                ELSE IF @ITEM_BASIS = 1 
                BEGIN
                    DECLARE @PAY_DAYS DECIMAL(5,2) = CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END;

                    IF @ITEM_CODE IN ('HOME','CHILDREN','GROCERY')
                        SET @CALC_AMOUNT = CASE WHEN @PAY_DAYS >= 28 THEN @ITEM_AMOUNT ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS / 30.0 AS BIGINT) END;
                    ELSE IF @ITEM_CODE = 'NAHAR'
                    BEGIN
                        DECLARE @NAHAR_DAYS DECIMAL(5,2) = @DAYSB - @FRID_COUNT - @LEAVE_DAYS + @TDAYS;
                        SET @CALC_AMOUNT = CASE WHEN @NAHAR_DAYS > 0 THEN CAST(@ITEM_AMOUNT * @NAHAR_DAYS AS BIGINT) ELSE CAST(@ITEM_AMOUNT * @DAYSB AS BIGINT) END;
                    END;
                    ELSE IF @ITEM_CODE = 'SHIFT'
                    BEGIN
                        IF @SHIFT_MODE = 'FIXED'
                            -- حالت مبلغ ثابت: مانند سایر آیتم‌های روزانه با تناسب روز کارکرد
                            SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * @PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2)) AS BIGINT);
                        ELSE
                        BEGIN
                            -- v6.1: درصدی از حقوق پایه «ماهیانه» (نرخ روزانه × 30) با تناسب روز کارکرد
                            -- @BASE_SAL_B محاسبه‌شده = نرخ روزانه × DAYSB ÷ 30  →  ضرب در @MONTH_DAYS = نرخ روزانه × DAYSB
                            DECLARE @BASE_SAL_B BIGINT = ISNULL((SELECT TOP 1 AMOUNT FROM #ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B'), 0);
                            SET @CALC_AMOUNT = CAST(@BASE_SAL_B * @MONTH_DAYS * @ITEM_AMOUNT / 100.0 AS BIGINT);
                        END;
                    END;
                    ELSE
                        SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * @PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2)) AS BIGINT);
                END;
                ELSE 
                    SET @CALC_AMOUNT = ISNULL(@ITEM_AMOUNT, 0);

                INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
                VALUES (@ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @CALC_AMOUNT, @ITEM_INS, @ITEM_TAX);

                FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD;
            END;
            CLOSE cur_line; DEALLOCATE cur_line;

            FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO;
        END;
        CLOSE cur_dec; DEALLOCATE cur_dec;

        -- گام ۶ — افزودن آیتم‌های متغیر
        -- v6.1: اگر آیتم اضافه‌کار به صورت صریح در حکم تعریف شده باشد (مثلاً با نرخ ساعتی)،
        -- محاسبه خودکار آن از روی حقوق پایه انجام نمی‌شود تا دوبار منظور نگردد
        DECLARE @BASE_SAL_DAILY BIGINT = ISNULL((SELECT TOP 1 AMOUNT FROM #ItemCalc WHERE ITEM_CODE = 'BASE_SAL' OR ITEM_CODE = 'BASE_SAL_B'), 0);

        IF @OT_NORMAL_H > 0 AND NOT EXISTS (SELECT 1 FROM #ItemCalc WHERE ITEM_CODE = 'OT_NORMAL')
        BEGIN
            DECLARE @OT_NORMAL_AMT BIGINT = ISNULL(CAST(@BASE_SAL_DAILY / (@MONTH_DAYS * @OT_HOUR_BASE) * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), 0);
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_NORMAL', 2, @OT_NORMAL_AMT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_NORMAL';
        END;

        IF @OT_HOLIDAY_H > 0 AND NOT EXISTS (SELECT 1 FROM #ItemCalc WHERE ITEM_CODE = 'OT_HOLIDAY')
        BEGIN
            DECLARE @OT_HOLIDAY_AMT BIGINT = ISNULL(CAST(@BASE_SAL_DAILY / (@MONTH_DAYS * @OT_HOUR_BASE) * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), 0);
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_HOLIDAY', 2, @OT_HOLIDAY_AMT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_HOLIDAY';
        END;

        IF @OT_ADMIN_H > 0 AND NOT EXISTS (SELECT 1 FROM #ItemCalc WHERE ITEM_CODE = 'OT_ADMIN')
        BEGIN
            DECLARE @OT_ADMIN_AMT BIGINT = ISNULL(CAST(@BASE_SAL_DAILY / (@MONTH_DAYS * @OT_HOUR_BASE) * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), 0);
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_ADMIN', 2, @OT_ADMIN_AMT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_ADMIN';
        END;

        IF @PERF_AMOUNT > 0
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'PERF_BONUS', 2, @PERF_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'PERF_BONUS';

        IF @TRANSP_AMOUNT > 0
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'TRANSP', 2, @TRANSP_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'TRANSP';

        INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT AV.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, AV.VALUE, ID.INS_SUBJECT, ID.TAX_SUBJECT
        FROM PAY2_ATT_VALUE AV INNER JOIN PAY2_ITEM_DEF ID ON AV.ITEM_ID = ID.ITEM_ID
        WHERE AV.PER_ID = @PER_ID AND AV.EMP_ID = @EMP_ID AND AV.VALUE <> 0
          AND NOT EXISTS (SELECT 1 FROM #ItemCalc X WHERE X.ITEM_ID = AV.ITEM_ID);

        -- گام ۷ — محاسبه بیمه
        DECLARE @GROSS_PAY BIGINT=0, @INS_BASE BIGINT=0, @INS_WORKER BIGINT=0, @INS_EMPLOYER BIGINT=0;

        SELECT @GROSS_PAY = ISNULL(SUM(AMOUNT), 0) FROM #ItemCalc WHERE ITEM_TYPE IN (1, 2);
        SELECT @INS_BASE = ISNULL(SUM(AMOUNT), 0) FROM #ItemCalc WHERE INS_SUBJECT = 1 AND ITEM_TYPE IN (1, 2);

        IF @INS_CEILING_APPLY = 1 AND @INS_TYPE <> 3
            SET @INS_BASE = CASE WHEN @INS_BASE > @INS_CEILING THEN @INS_CEILING ELSE @INS_BASE END;

        IF @INS_TYPE = 3 
        BEGIN
            SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0;
        END;
        ELSE
        BEGIN
            SET @INS_WORKER = ISNULL(CAST(@INS_BASE * @INS_WORKER_RATE AS BIGINT), 0);
            
            DECLARE @EMP_IS_JANBAZ BIT = ISNULL((SELECT IS_JANBAZ FROM PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID), 0);
            DECLARE @JANBAZ_RATE   DECIMAL(6,4) = ISNULL(CAST((SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY='INS_JANBAZ_RATE') AS DECIMAL(6,4)), 0.18);

            IF @EMP_IS_JANBAZ = 1
                SET @INS_EMPLOYER = ISNULL(CAST(@INS_BASE * @JANBAZ_RATE AS BIGINT), 0); 
            ELSE
                SET @INS_EMPLOYER = ISNULL(CAST(@INS_BASE * (@INS_EMPLOYER_RATE + CASE WHEN ISNULL(@IS_MANAGER,0)=0 THEN @INS_UNEMP_RATE ELSE 0 END) AS BIGINT), 0);
        END;

        -- گام ۸ — محاسبه مالیات
        DECLARE @TAX_BASE BIGINT=0, @TAX_AMOUNT BIGINT=0;

        IF @TAX_EXEMPT_FLAG = 1
        BEGIN
            SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        END;
        ELSE
        BEGIN
            SELECT @TAX_BASE = ISNULL(SUM(AMOUNT), 0) FROM #ItemCalc WHERE TAX_SUBJECT = 1 AND ITEM_TYPE IN (1, 2);
            IF @TAX_DEDUCT_INS = 1 SET @TAX_BASE = @TAX_BASE - @INS_WORKER;
            SET @TAX_BASE = CASE WHEN @TAX_BASE > @TAX_EXEMPT THEN @TAX_BASE - @TAX_EXEMPT ELSE 0 END;
            IF @TAX_DEP_APPLY = 1 AND @REGION_DEP > 0 SET @TAX_BASE = CAST(@TAX_BASE * (1.0 - @REGION_DEP / 100.0) AS BIGINT);
            SET @TAX_AMOUNT = ISNULL([dbo].[FN_PAY2_CALC_TAX](@TAX_BASE * 12, @TAX_YEAR) / 12, 0);
            IF @TAX_AMOUNT < 0 SET @TAX_AMOUNT = 0;
        END;

        -- گام ۹ — مساعده هوشمند
        DECLARE @ADVANCE_DED BIGINT = 0;
        IF @ADV_ENABLED = 1
            SELECT @ADVANCE_DED = ISNULL(ADVANCE_DEDUCTION, 0) FROM #AdvResult WHERE EMP_ID = @EMP_ID;

        -- گام ۱۰ — کسر وام خودکار
        DECLARE @LOAN_DED BIGINT = 0;
        SELECT @LOAN_DED = ISNULL(SUM(LS.AMOUNT), 0) FROM PAY2_LOAN_SCHED LS INNER JOIN PAY2_LOAN L ON LS.LOAN_ID = L.LOAN_ID
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1 AND LS.DUE_PERIOD = @PERIOD_DATE AND LS.RUN_ID IS NULL;

        DECLARE @OTHER_DED BIGINT = ISNULL(@KASR_OTHER, 0);

        -- گام ۱۱ — محاسبه خالص
        DECLARE @TOTAL_DED BIGINT = @INS_WORKER + @TAX_AMOUNT + @LOAN_DED + @ADVANCE_DED + @OTHER_DED;
        DECLARE @NET_PAY BIGINT = @GROSS_PAY - @TOTAL_DED;

        IF @ROUND_MODE > 1
            SET @NET_PAY = ISNULL(ROUND(@NET_PAY / CAST(@ROUND_MODE AS FLOAT), 0) * @ROUND_MODE, 0);

        -- گام ۱۲ — ذخیره نتایج
        DECLARE @LEAVE_BAL_DAYS DECIMAL(5,2) = NULL;
        SELECT @LEAVE_BAL_DAYS = CAST(BALANCE_MIN AS DECIMAL(10,2)) / 440.0 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;

        DECLARE @LOAN_BAL BIGINT = NULL;
        SELECT @LOAN_BAL = ISNULL(SUM(BALANCE), 0) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID;

        INSERT INTO PAY2_RUN_LINE (
            RUN_ID, EMP_ID, WORK_DAYS, GROSS_PAY, INS_BASE, INS_WORKER, INS_EMPLOYER, TAX_BASE, TAX_AMOUNT,
            LOAN_DED, ADVANCE_DED, OTHER_DED, TOTAL_DED, NET_PAY, LEAVE_BAL_DAYS, LOAN_BALANCE, ADVANCE_BALANCE_SNAP
        ) VALUES (
            @NEW_RUN_ID, @EMP_ID, @DAYSB, @GROSS_PAY, @INS_BASE, @INS_WORKER, @INS_EMPLOYER, @TAX_BASE, @TAX_AMOUNT,
            @LOAN_DED, @ADVANCE_DED, @OTHER_DED, @TOTAL_DED, @NET_PAY, @LEAVE_BAL_DAYS, @LOAN_BAL, @ADVANCE_DED
        );

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT @NEW_RUN_ID, @EMP_ID, ITEM_ID, ISNULL(AMOUNT,0), ISNULL(INS_SUBJECT,0), ISNULL(TAX_SUBJECT,0) FROM #ItemCalc;

        DECLARE @INS_DED_ID  INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='INS_DED');
        DECLARE @TAX_DED_ID  INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='TAX_DED');
        DECLARE @LOAN_DED_ID INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='LOAN_DED');
        DECLARE @ADV_DED_ID  INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='ADVANCE_DED');

        -- 🚀 رفع باگ T-SQL 213: افزودن نام ستون‌ها در INSERT VALUES 🚀
        IF @INS_WORKER  > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@INS_DED_ID, @INS_WORKER, 0,0);
        IF @TAX_AMOUNT  > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@TAX_DED_ID, @TAX_AMOUNT, 0,0);
        IF @LOAN_DED    > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@LOAN_DED_ID,@LOAN_DED,   0,0);
        IF @ADVANCE_DED > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@ADV_DED_ID, @ADVANCE_DED,0,0);

        UPDATE PAY2_LOAN_SCHED SET RUN_ID = @NEW_RUN_ID, PAID_AT = GETDATE()
        WHERE DUE_PERIOD = @PERIOD_DATE AND RUN_ID IS NULL AND LOAN_ID IN (SELECT LOAN_ID FROM PAY2_LOAN WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1);

        UPDATE PAY2_LOAN SET PAID_INST = PAID_INST + (SELECT COUNT(*) FROM PAY2_LOAN_SCHED WHERE LOAN_ID=PAY2_LOAN.LOAN_ID AND DUE_PERIOD=@PERIOD_DATE AND RUN_ID=@NEW_RUN_ID)
        WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1;

        DECLARE @LEAVE_MIN_USED INT = CAST(@LEAVE_DAYS * 440 AS INT);
        IF @LEAVE_MIN_USED > 0
        BEGIN
            UPDATE PAY2_LEAVE_BAL SET USED_MIN = USED_MIN + @LEAVE_MIN_USED, UPDATED_AT = GETDATE()
            WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;
        END;

        DROP TABLE #ItemCalc;

        FETCH NEXT FROM cur_emp INTO @EMP_ID, @IS_MANAGER, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;
    END;

    CLOSE cur_emp; DEALLOCATE cur_emp;
    DROP TABLE #AdvResult;

    UPDATE PAY2_PERIOD SET STATUS = 3 WHERE PER_ID = @PER_ID;

END;
GO

-- ================================================================
-- ۲. SP_PAY2_GEN_DEED — تولید سند حسابداری حقوق و بیمه
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GEN_DEED]
    @RUN_ID  INT,
    @CALC_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT, @PER_ID INT, @WS_ID INT, @PER_DATE BIGINT;

    SELECT @STATUS = R.STATUS, @PER_ID = R.PER_ID, @WS_ID = P.WS_ID, @PER_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    IF @STATUS <> 2
    BEGIN
        RAISERROR(N'اجرای %d باید ابتدا نهایی شود.', 16, 1, @RUN_ID);
        RETURN;
    END;

    -- متغیرهای حساب‌ها
    DECLARE 
        @ACC_SALARY_TOLID NVARCHAR(50), @ACC_SALARY_EDARI NVARCHAR(50), 
        @ACC_SALARY_FOROSH NVARCHAR(50), @ACC_SALARY_KHADAMAT NVARCHAR(50),
        @ACC_SALARY_PAY NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50),
        @ACC_TAX_PAYABLE NVARCHAR(50), @ACC_INS_EXP NVARCHAR(50),
        @ACC_ADV_HES NVARCHAR(50), @ACC_LOAN_HES NVARCHAR(50);

    -- خواندن کدهای حسابداری از تنظیمات کارگاه
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
        @ACC_LOAN_HES       = MAX(CASE WHEN ACC_KEY='LOAN_HES'            THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    -- ایجاد یک CTE برای محاسبه سهم هزینه‌ها بر اساس روزهای کارکرد
    ;WITH SalarySplit AS (
        SELECT 
            RL.EMP_ID, RL.GROSS_PAY,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN RL.GROSS_PAY * (A.DAYS_TOLID / A.WORK_DAYS) ELSE 0 END AS BIGINT) AS EXP_TOLID,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN RL.GROSS_PAY * (A.DAYS_EDARI / A.WORK_DAYS) ELSE 0 END AS BIGINT) AS EXP_EDARI,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN RL.GROSS_PAY * (A.DAYS_FOROSH / A.WORK_DAYS) ELSE 0 END AS BIGINT) AS EXP_FOROSH,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN RL.GROSS_PAY * (A.DAYS_KHADAMAT / A.WORK_DAYS) ELSE 0 END AS BIGINT) AS EXP_KHADAMAT
        FROM PAY2_RUN_LINE RL
        INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID
        WHERE RL.RUN_ID = @RUN_ID
    )
    -- خروجی نهایی برای صدور سند
    SELECT @ACC_SALARY_TOLID AS HES_CODE, N'هزینه حقوق تولید' AS SHARH, SUM(EXP_TOLID) AS BED, 0 AS BES, 'EXP_TOLID' AS ACC_KEY, NULL AS EMP_ID
    FROM SalarySplit HAVING SUM(EXP_TOLID) > 0
    UNION ALL 
    SELECT @ACC_SALARY_EDARI, N'هزینه حقوق اداری', SUM(EXP_EDARI), 0, 'EXP_EDARI', NULL
    FROM SalarySplit HAVING SUM(EXP_EDARI) > 0
    UNION ALL 
    SELECT @ACC_SALARY_FOROSH, N'هزینه حقوق فروش', SUM(EXP_FOROSH), 0, 'EXP_FOROSH', NULL
    FROM SalarySplit HAVING SUM(EXP_FOROSH) > 0
    UNION ALL 
    SELECT @ACC_SALARY_KHADAMAT, N'هزینه حقوق خدمات', SUM(EXP_KHADAMAT), 0, 'EXP_KHADAMAT', NULL
    FROM SalarySplit HAVING SUM(EXP_KHADAMAT) > 0
    UNION ALL 
    SELECT @ACC_INS_EXP, N'هزینه بیمه کارفرما', SUM(INS_EMPLOYER), 0, 'INS_EXP', NULL
    FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID HAVING SUM(INS_EMPLOYER) > 0
    UNION ALL 
    SELECT @ACC_SALARY_PAY, N'پرداختنی حقوق خالص', 0, SUM(NET_PAY), 'SALARY_PAYABLE', NULL
    FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID HAVING SUM(NET_PAY) > 0
    UNION ALL 
    SELECT @ACC_INS_PAYABLE, N'بیمه تأمین اجتماعی', 0, SUM(INS_WORKER + INS_EMPLOYER), 'INS_PAYABLE', NULL
    FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID HAVING SUM(INS_WORKER + INS_EMPLOYER) > 0
    UNION ALL 
    SELECT @ACC_TAX_PAYABLE, N'مالیات حقوق', 0, SUM(TAX_AMOUNT), 'TAX_PAYABLE', NULL
    FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID HAVING SUM(TAX_AMOUNT) > 0
    UNION ALL 
    SELECT @ACC_LOAN_HES, N'کسر اقساط وام', 0, SUM(LOAN_DED), 'LOAN_HES', NULL
    FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID HAVING SUM(LOAN_DED) > 0
    UNION ALL 
    SELECT @ACC_ADV_HES, N'تصفیه مساعده: ' + E.LAST_NAME + N' ' + E.FIRST_NAME, 0, RL.ADVANCE_DED, 'ADVANCE_SETTLE', E.EMP_ID
    FROM PAY2_RUN_LINE RL INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
    WHERE RL.RUN_ID = @RUN_ID AND RL.ADVANCE_DED > 0;
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
        @LEAVE_MINS_PER_DAY  INT;

    SELECT
        @BONUS_MODE          = MAX(CASE WHEN CFG_KEY='BONUS_MODE'          THEN CFG_VALUE END),
        @BONUS_CUSTOM_DAYS   = MAX(CASE WHEN CFG_KEY='BONUS_CUSTOM_DAYS'   THEN CAST(CFG_VALUE AS INT) END),
        @MIN_WAGE_DAILY      = MAX(CASE WHEN CFG_KEY='MIN_WAGE_DAILY'      THEN CAST(CFG_VALUE AS BIGINT) END),
        @MIN_WAGE_MONTHLY    = MAX(CASE WHEN CFG_KEY='MIN_WAGE_MONTHLY'    THEN CAST(CFG_VALUE AS BIGINT) END),
        @EIDI_MIN_DAYS       = MAX(CASE WHEN CFG_KEY='EIDI_MIN_DAYS'       THEN CAST(CFG_VALUE AS INT) END),
        @EIDI_MAX_DAYS       = MAX(CASE WHEN CFG_KEY='EIDI_MAX_DAYS'       THEN CAST(CFG_VALUE AS INT) END),
        @SENIORITY_MODE      = MAX(CASE WHEN CFG_KEY='SENIORITY_MODE'      THEN CFG_VALUE END),
        @SENIORITY_FIXED_AMT = MAX(CASE WHEN CFG_KEY='SENIORITY_FIXED_AMT' THEN CAST(CFG_VALUE AS BIGINT) END),
        @TAX_YEAR            = MAX(CASE WHEN CFG_KEY='TAX_YEAR'            THEN CAST(CFG_VALUE AS SMALLINT) END),
        @LEAVE_MINS_PER_DAY  = MAX(CASE WHEN CFG_KEY='LEAVE_MINS_PER_DAY' THEN CAST(CFG_VALUE AS INT) END)
    FROM PAY2_CONFIG
    WHERE CFG_KEY IN (
        'BONUS_MODE','BONUS_CUSTOM_DAYS','MIN_WAGE_DAILY','MIN_WAGE_MONTHLY',
        'EIDI_MIN_DAYS','EIDI_MAX_DAYS','SENIORITY_MODE','SENIORITY_FIXED_AMT',
        'TAX_YEAR','LEAVE_MINS_PER_DAY'
    );

    DECLARE
        @HIRE_DATE           BIGINT,
        @EMP_FIRST_NAME      NVARCHAR(50),
        @EMP_LAST_NAME       NVARCHAR(50);

    SELECT
        @HIRE_DATE      = HIRE_DATE,
        @EMP_FIRST_NAME = FIRST_NAME,
        @EMP_LAST_NAME  = LAST_NAME
    FROM PAY2_EMPLOYEE
    WHERE EMP_ID = @EMP_ID;

    IF @HIRE_DATE IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_CALC_SETTLE: پرسنل %d یافت نشد.', 16, 1, @EMP_ID);
        RETURN;
    END;

    DECLARE @PREV_SET_ID INT = NULL;
    DECLARE @PREV_SEN_DAYS INT = 0;

    SELECT TOP 1
        @PREV_SET_ID   = SET_ID,
        @PREV_SEN_DAYS = SENIORITY_DAYS + PREV_SENIORITY_DAYS
    FROM PAY2_SETTLEMENT
    WHERE EMP_ID = @EMP_ID AND STATUS >= 2
    ORDER BY SETTLE_DATE DESC;

    DECLARE @HIRE_YEAR   INT = @HIRE_DATE / 10000;
    DECLARE @HIRE_MONTH  INT = (@HIRE_DATE % 10000) / 100;
    DECLARE @HIRE_DAY    INT = @HIRE_DATE % 100;
    DECLARE @END_YEAR    INT = @END_DATE / 10000;
    DECLARE @END_MONTH   INT = (@END_DATE % 10000) / 100;
    DECLARE @END_DAY     INT = @END_DATE % 100;

    DECLARE @SENIORITY_DAYS INT =
        (@END_YEAR * 365) + (@END_MONTH * 30) + @END_DAY
        - (@HIRE_YEAR * 365) - (@HIRE_MONTH * 30) - @HIRE_DAY
        - @PREV_SEN_DAYS;

    IF @SENIORITY_DAYS < 0 SET @SENIORITY_DAYS = 0;

    DECLARE @SENIORITY_YEARS  DECIMAL(6,2) = CAST(@SENIORITY_DAYS AS DECIMAL(10,2)) / 365.0;
    DECLARE @SENIORITY_FULL   INT           = @SENIORITY_DAYS / 365;
    DECLARE @SENIORITY_REMAIN INT           = @SENIORITY_DAYS % 365;

    DECLARE @LAST_DEC_ID  INT;
    SELECT TOP 1 @LAST_DEC_ID = DEC_ID
    FROM PAY2_DECREE
    WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1
      AND EFF_FROM <= @SETTLE_DATE
      AND (EFF_TO IS NULL OR EFF_TO >= @SETTLE_DATE)
    ORDER BY EFF_FROM DESC;

    DECLARE @LAST_SALARY BIGINT = 0;
    SELECT @LAST_SALARY = ISNULL(SUM(DL.AMOUNT), 0)
    FROM PAY2_DECREE_LINE DL
    INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
    WHERE DL.DEC_ID = @LAST_DEC_ID
      AND ID.ITEM_TYPE = 1
      AND ID.INS_SUBJECT = 1;

    DECLARE @LAST_DAILY BIGINT = @LAST_SALARY / 30;
    IF @LAST_DAILY < @MIN_WAGE_DAILY SET @LAST_DAILY = @MIN_WAGE_DAILY;

    DECLARE @EIDI BIGINT = 0;
    DECLARE @EIDI_DAYS INT;
    DECLARE @DAYS_THIS_YEAR INT = CASE WHEN @SENIORITY_DAYS >= 365 THEN 365 ELSE @SENIORITY_DAYS END;

    IF @DAYS_THIS_YEAR < @EIDI_MIN_DAYS
        SET @EIDI = 0;
    ELSE
    BEGIN
        SET @EIDI_DAYS = CASE
            WHEN @DAYS_THIS_YEAR >= @EIDI_MAX_DAYS THEN @EIDI_MAX_DAYS
            ELSE @EIDI_MIN_DAYS
        END;

        IF @BONUS_MODE = 'MIN_WAGE'
        BEGIN
            DECLARE @EIDI_BASE BIGINT = CASE WHEN @LAST_SALARY < @MIN_WAGE_MONTHLY THEN @LAST_SALARY ELSE @MIN_WAGE_MONTHLY END;
            SET @EIDI = @EIDI_BASE / 30 * @EIDI_DAYS;
        END;
        ELSE IF @BONUS_MODE = 'ACTUAL'
            SET @EIDI = @LAST_DAILY * @EIDI_DAYS;
        ELSE IF @BONUS_MODE = 'CUSTOM'
        BEGIN
            SET @EIDI_DAYS = ISNULL(@BONUS_CUSTOM_DAYS, 60);
            SET @EIDI = @LAST_DAILY * @EIDI_DAYS;
        END;
    END;

    DECLARE @EIDI_TAX BIGINT = 0;
    DECLARE @EIDI_MIN_AMT BIGINT = @MIN_WAGE_DAILY * @EIDI_MIN_DAYS;
    IF @EIDI > @EIDI_MIN_AMT
    BEGIN
        DECLARE @EIDI_TAXABLE BIGINT = (@EIDI - @EIDI_MIN_AMT) * 12;
        SET @EIDI_TAX = [dbo].[FN_PAY2_CALC_TAX](@EIDI_TAXABLE, @TAX_YEAR) / 12;
    END;

    DECLARE @SANAVAT BIGINT = 0;
    IF @SENIORITY_MODE = 'LAST_SAL'
    BEGIN
        SET @SANAVAT = @LAST_SALARY * @SENIORITY_FULL + CAST(@LAST_SALARY * @SENIORITY_REMAIN / 365.0 AS BIGINT);
    END;
    ELSE IF @SENIORITY_MODE = 'DAILY'
        SET @SANAVAT = @LAST_DAILY * 30 * @SENIORITY_FULL + CAST(@LAST_DAILY * @SENIORITY_REMAIN AS BIGINT);
    ELSE IF @SENIORITY_MODE = 'FIXED'
        SET @SANAVAT = ISNULL(@SENIORITY_FIXED_AMT, 0) * @SENIORITY_FULL;

    DECLARE @LEAVE_BAL_MIN  INT = 0;
    DECLARE @LEAVE_BAL_DAYS_CALC DECIMAL(5,2) = 0;
    SELECT @LEAVE_BAL_MIN = ISNULL(SUM(BALANCE_MIN), 0)
    FROM PAY2_LEAVE_BAL
    WHERE EMP_ID = @EMP_ID;

    IF @LEAVE_BAL_MIN < 0 SET @LEAVE_BAL_MIN = 0;
    SET @LEAVE_BAL_DAYS_CALC = CAST(@LEAVE_BAL_MIN AS DECIMAL(10,2)) / @LEAVE_MINS_PER_DAY;

    DECLARE @LEAVE_DAILY BIGINT = @LAST_SALARY / 26;
    DECLARE @LEAVE_PAY   BIGINT = CAST(@LEAVE_BAL_DAYS_CALC * @LEAVE_DAILY AS BIGINT);

    DECLARE @BON_SETTLE BIGINT = 0;
    SELECT @BON_SETTLE = ISNULL(
        (SELECT TOP 1 DL.AMOUNT * @SENIORITY_FULL
         FROM PAY2_DECREE_LINE DL
         INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
         WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_CODE = 'GROCERY')
    , 0);

    DECLARE @LOAN_BALANCE_TOT BIGINT = 0;
    SELECT @LOAN_BALANCE_TOT = ISNULL(SUM(BALANCE), 0)
    FROM V_PAY2_LOAN_BALANCE
    WHERE EMP_ID = @EMP_ID;

    DECLARE @PREV_DEBIT BIGINT = 0;

    INSERT INTO PAY2_SETTLEMENT (
        EMP_ID, WS_ID, SETTLE_DATE, HIRE_DATE, END_DATE,
        SENIORITY_DAYS, SENIORITY_YEARS, LAST_SALARY, LAST_DAILY,
        PREV_SET_ID, PREV_SENIORITY_DAYS, LEAVE_BAL_MIN, LEAVE_BAL_DAYS,
        EIDI, BON, LEAVE_PAY, SANAVAT, PREV_CREDIT, OTHER_INCOME,
        PREV_DEBIT, EIDI_TAX, LOAN_BALANCE, OTHER_DED,
        STATUS, CALC_METHOD, CREATED_BY
    )
    VALUES (
        @EMP_ID, @WS_ID, @SETTLE_DATE, @HIRE_DATE, @END_DATE,
        @SENIORITY_DAYS, @SENIORITY_YEARS, @LAST_SALARY, @LAST_DAILY,
        @PREV_SET_ID, @PREV_SEN_DAYS, @LEAVE_BAL_MIN, @LEAVE_BAL_DAYS_CALC,
        @EIDI, @BON_SETTLE, @LEAVE_PAY, @SANAVAT, @PREV_CREDIT, @OTHER_INCOME,
        @PREV_DEBIT, @EIDI_TAX, @LOAN_BALANCE_TOT, @OTHER_DED,
        1,
        (
            N'{""bonus_mode"":""' + @BONUS_MODE + N'""'
            + N',""seniority_mode"":""' + @SENIORITY_MODE + N'""'
            + N',""seniority_days"":' + CAST(@SENIORITY_DAYS AS NVARCHAR)
            + N',""leave_bal_min"":' + CAST(@LEAVE_BAL_MIN AS NVARCHAR)
            + N',""tax_year"":' + CAST(@TAX_YEAR AS NVARCHAR) + N'}'
        ),
        @CALC_BY
    );

    SET @NEW_SET_ID = SCOPE_IDENTITY();

    UPDATE PAY2_EMPLOYEE SET FIRE_DATE = @END_DATE, IS_ACTIVE = 0
    WHERE EMP_ID = @EMP_ID AND FIRE_DATE IS NULL;

    PRINT N'SP_PAY2_CALC_SETTLE — موفق. SET_ID = ' + CAST(@NEW_SET_ID AS NVARCHAR);
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

    SELECT @STATUS = STATUS, @WS_ID = WS_ID, @EMP_ID = EMP_ID
    FROM PAY2_SETTLEMENT WHERE SET_ID = @SET_ID;

    IF @STATUS <> 2
    BEGIN
        RAISERROR(N'SP_PAY2_GEN_DEED_SETTLE: تسویه %d باید نهایی (STATUS=2) شود.', 16, 1, @SET_ID);
        RETURN;
    END;

    DECLARE @ACC_SALARY_PAY  NVARCHAR(20);
    DECLARE @ACC_INS_PAYABLE NVARCHAR(20);
    DECLARE @ACC_TAX_PAYABLE NVARCHAR(20);
    SELECT
        @ACC_SALARY_PAY  = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE' THEN ACC_CODE END),
        @ACC_INS_PAYABLE = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'    THEN ACC_CODE END),
        @ACC_TAX_PAYABLE = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'    THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    SELECT N'هزینه عیدی'        AS SHARH, EIDI     AS BED, 0        AS BES, 'COST_EIDI'    AS ACC_KEY FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI     > 0
    UNION ALL
    SELECT N'هزینه حق سنوات', SANAVAT,  0, 'COST_SANAVAT'  FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND SANAVAT   > 0
    UNION ALL
    SELECT N'هزینه مانده مرخصی', LEAVE_PAY, 0, 'COST_LEAVE'  FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND LEAVE_PAY  > 0
    UNION ALL
    SELECT N'پرداختنی تسویه حساب',  0, CAST(EIDI+BON+LEAVE_PAY+SANAVAT+PREV_CREDIT+OTHER_INCOME-PREV_DEBIT-EIDI_TAX-LOAN_BALANCE-OTHER_DED AS BIGINT), 'SETTLE_PAYABLE' FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID
    UNION ALL
    SELECT N'وصول مانده وام',        0, LOAN_BALANCE, 'LOAN_COLLECT'  FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND LOAN_BALANCE > 0
    UNION ALL
    SELECT N'وصول بدهکاری (مساعده)', 0, PREV_DEBIT,   'ADV_COLLECT'   FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND PREV_DEBIT   > 0
    UNION ALL
    SELECT N'مالیات عیدی',           0, EIDI_TAX,     @ACC_TAX_PAYABLE FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI_TAX    > 0;
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

    DECLARE @STATUS TINYINT;
    DECLARE @PER_ID INT;
    DECLARE @IS_LATEST BIT;

    SELECT @STATUS = STATUS, @PER_ID = PER_ID, @IS_LATEST = IS_LATEST
    FROM PAY2_RUN WHERE RUN_ID = @RUN_ID;

    IF @STATUS = 3
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: سند حسابداری صادر شده — برگشت ممکن نیست.', 16, 1);
        RETURN;
    END;

    IF @IS_LATEST = 0
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: فقط آخرین نسخه (IS_LATEST=1) قابل برگشت است.', 16, 1);
        RETURN;
    END;

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE PAY2_LOAN_SCHED
        SET RUN_ID = NULL, PAID_AT = NULL
        WHERE RUN_ID = @RUN_ID;

        UPDATE L SET L.PAID_INST = L.PAID_INST - (
            SELECT COUNT(*) FROM PAY2_LOAN_SCHED LS
            WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID IS NULL
              AND 1 = 0
        )
        FROM PAY2_LOAN L WHERE L.IS_ACTIVE = 1;

        DELETE FROM PAY2_RUN_DETAIL WHERE RUN_ID = @RUN_ID;
        DELETE FROM PAY2_RUN_LINE    WHERE RUN_ID = @RUN_ID;

        UPDATE PAY2_RUN SET STATUS = 1, NOTES = ISNULL(NOTES,'') + N' | Reverted by ' + CAST(ISNULL(@REVERT_BY,0) AS NVARCHAR)
        WHERE RUN_ID = @RUN_ID;

        UPDATE PAY2_PERIOD SET STATUS = 2 WHERE PER_ID = @PER_ID;

        COMMIT TRANSACTION;
        PRINT N'SP_PAY2_REVERT_RUN — موفق. RUN_ID ' + CAST(@RUN_ID AS NVARCHAR) + N' برگشت داده شد.';
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        -- رفع خطای استفاده مستقیم از ERROR_MESSAGE در تابع RAISERROR
        DECLARE @ERR_MSG NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(N'SP_PAY2_REVERT_RUN خطا: %s', 16, 1, @ERR_MSG);
    END CATCH;
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

    DECLARE @STATUS TINYINT;
    SELECT @STATUS = STATUS FROM PAY2_SETTLEMENT WHERE SET_ID = @SET_ID;

    IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_SETTLE: تسویه %d در وضعیت پیش‌نویس (1) نیست.', 16, 1, @SET_ID);
        RETURN;
    END;

    UPDATE PAY2_SETTLEMENT
    SET STATUS = 2, APPROVED_BY = @APPROVED_BY, APPROVED_AT = GETDATE()
    WHERE SET_ID = @SET_ID;

    PRINT N'SP_PAY2_FINALIZE_SETTLE — SET_ID ' + CAST(@SET_ID AS NVARCHAR) + N' نهایی شد.';
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
                 THEN (SELECT AMOUNT - @INSTALLMENT*(@TOTAL_INST-1) FROM PAY2_LOAN WHERE LOAN_ID=@LOAN_ID)
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
                FROM DEED_HED H WITH (NOLOCK)
                INNER JOIN DEED_DTL D WITH (NOLOCK) ON H.N_S = D.N_S
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

                //try { db.Execute($@""); } catch { }
                try { db.Execute($@"ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [POSTAL_CODE] NVARCHAR(20) NULL;"); } catch { }
                try { db.Execute($@"ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [EMPLOYER_NAME] NVARCHAR(100) NULL;"); } catch { }
                try { db.Execute($@"ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD 
    [PROVINCE] NVARCHAR(50) NULL,             -- نام استان
    [CITY] NVARCHAR(50) NULL,                 -- نام شهر
    [REGISTRATION_NUMBER] NVARCHAR(20) NULL,  -- شماره ثبت
    [SSO_BRANCH] NVARCHAR(50) NULL,           -- شعبه تامین اجتماعی
    [FINANCIAL_MANAGER] NVARCHAR(100) NULL,   -- مدیر مالی
    [ADMIN_MANAGER] NVARCHAR(100) NULL;       -- معاون اداری مالی"); } catch { }

                //-- ساخت ایندکس ترکیبی برای حذف عملیات سورت و اسکن جدول شغل‌ها
                try { db.Execute($@"CREATE NONCLUSTERED INDEX IX_PAY2_JOB_PERFORMANCE 
ON [dbo].[PAY2_JOB] ([IS_ACTIVE], [JOB_NAME]) 
INCLUDE ([JOB_ID]);"); } catch { }

                LoadJobData(db);   // <-- این خط اضافه شود
            }
        }
        private static void ExecuteBatches(SqlConnection db, string script)
        {
            // Safely split the script ONLY when "GO" is on its own line
            var commands = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var cmdText in commands)
            {
                if (!string.IsNullOrWhiteSpace(cmdText))
                {
                    try
                    {
                        db.Execute(cmdText);
                    }
                    catch (SqlException ex)
                    {
                        // Logging the exact error and query batch that failed so you can actually debug it
                        Console.WriteLine($"SQL Execution Error:\n{ex.Message}\nFailed Batch:\n{cmdText}\n");
                        // If a critical procedure fails to create, you might want to throw the error here
                        // throw; 
                    }
                }
            }
        }
    }
}
