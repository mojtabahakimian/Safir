/* ============================================================================
   جدول‌های قدیمیِ نرم‌افزار WPF که بخش‌های غیرحقوقیِ Safir به آن‌ها نیاز دارند
   + دادهٔ نمونهٔ حداقلی — فقط برای دیتابیس تست

   ⚠️ این فایل را هرگز روی دیتابیس واقعی اجرا نکنید.

   چرا لازم است
   ------------
   schema.sql و legacy_dependencies.sql فقط آنچه حقوق و دستمزد و CRM لازم دارند
   را می‌سازند. بدون این فایل، بخش‌های فروش ویزیتوری (گروه کالا، سبد، پیش‌فاکتور،
   لیست مشتریان، تعریف مشتری، صورت‌حساب)، کارتابل اتوماسیون (پیام و یادآوری)،
   گزارش تولید، گزارش خطا و تنظیمات سازمان در دیتابیس تست همه ۵۰۰ می‌دهند و
   نمی‌شود آن‌ها را مثل یک کاربر واقعی آزمایش کرد.

   ساختار جدول‌ها از روی کوئری‌های خودِ Server/ (و ScriptSqly.Main.cs برای
   DEFAULTDEP و پاداش فاکتور) بازسازی شده‌اند، نه از روی دیتابیس مشتری؛ پس
   فقط ستون‌هایی را دارند که این مخزن واقعاً می‌خواند یا می‌نویسد. دو تابع
   AK_MOGO_AVL_KOL / AK_MOGO_FR و تابع دفتر QDAFTARTAFZIL2_H نسخهٔ ساده‌شدهٔ
   تابع‌های واقعی‌اند (همان ستون‌ها، منطق حداقلی) — برای آزمون رابط کاربری
   کافی‌اند، نه برای راستی‌آزمایی عددیِ کاردکس.

   ترتیب اجرا: بعد از test_chart_of_accounts.sql (به TOTA_HES/DETA_HES نیاز
   دارد) و قبل از مهاجرت بهای تمام‌شده (MOGHA_ANBAR به ANBGRD_* و BACK_HEAD و
   TAGCOD ارجاع می‌دهد). setup-test-env.sh همین ترتیب را رعایت می‌کند.

   کاربر ساخته‌شده
   ----------------
   | نام کاربری | رمز    | نقش                                                 |
   |------------|--------|-----------------------------------------------------|
   | salesrep   | 444444 | ویزیتور فروش با حساب 310-1-1، مسیر و برنامهٔ ویزیت |
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── ۱. تنظیمات سازمان ─────────────────────────────────────────────────────
   SAZMAN در test_crm_tables.sql فقط با IT1..IT9 ساخته می‌شود. AppSettingsService
   (NAME, YEA, BEDEHKAR, GHAYM) و SmsService و بازسازی اسناد بهای تمام‌شده
   بقیه را می‌خوانند. بدون این‌ها GET /api/appsettings در هر صفحه ۴۰۴ می‌دهد. */
IF COL_LENGTH('dbo.SAZMAN','NAME')         IS NULL ALTER TABLE dbo.SAZMAN ADD NAME NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.SAZMAN','YEA')          IS NULL ALTER TABLE dbo.SAZMAN ADD YEA SMALLINT NULL;
IF COL_LENGTH('dbo.SAZMAN','BEDEHKAR')     IS NULL ALTER TABLE dbo.SAZMAN ADD BEDEHKAR INT NULL;
IF COL_LENGTH('dbo.SAZMAN','GHAYM')        IS NULL ALTER TABLE dbo.SAZMAN ADD GHAYM SMALLINT NULL;
IF COL_LENGTH('dbo.SAZMAN','SMSACT')       IS NULL ALTER TABLE dbo.SAZMAN ADD SMSACT BIT NULL;
IF COL_LENGTH('dbo.SAZMAN','RB_TUBA')      IS NULL ALTER TABLE dbo.SAZMAN ADD RB_TUBA BIT NULL;
IF COL_LENGTH('dbo.SAZMAN','RB_SMSIR')     IS NULL ALTER TABLE dbo.SAZMAN ADD RB_SMSIR BIT NULL;
IF COL_LENGTH('dbo.SAZMAN','SMS_LIBKEY')   IS NULL ALTER TABLE dbo.SAZMAN ADD SMS_LIBKEY NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.SAZMAN','SMS_USERNAME') IS NULL ALTER TABLE dbo.SAZMAN ADD SMS_USERNAME NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.SAZMAN','SMS_PASSWORD') IS NULL ALTER TABLE dbo.SAZMAN ADD SMS_PASSWORD NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.SAZMAN','SMS_TSMSHOST') IS NULL ALTER TABLE dbo.SAZMAN ADD SMS_TSMSHOST NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.SAZMAN','SMS_OWNER')    IS NULL ALTER TABLE dbo.SAZMAN ADD SMS_OWNER NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.SAZMAN','DSMS')         IS NULL ALTER TABLE dbo.SAZMAN ADD DSMS NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.SAZMAN','MOGODIA')      IS NULL ALTER TABLE dbo.SAZMAN ADD MOGODIA FLOAT NULL;
IF COL_LENGTH('dbo.SAZMAN','SNDKH')        IS NULL ALTER TABLE dbo.SAZMAN ADD SNDKH BIT NULL;
IF COL_LENGTH('dbo.SAZMAN','CONKAL')       IS NULL ALTER TABLE dbo.SAZMAN ADD CONKAL FLOAT NULL;
IF COL_LENGTH('dbo.SAZMAN','SANAT')        IS NULL ALTER TABLE dbo.SAZMAN ADD SANAT BIT NULL;
IF COL_LENGTH('dbo.SAZMAN','OPTIONSS')     IS NULL ALTER TABLE dbo.SAZMAN ADD OPTIONSS NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.SAZMAN','TKHF')         IS NULL ALTER TABLE dbo.SAZMAN ADD TKHF INT NULL;
IF COL_LENGTH('dbo.SAZMAN','ARSESH')       IS NULL ALTER TABLE dbo.SAZMAN ADD ARSESH TINYINT NULL;
IF COL_LENGTH('dbo.SAZMAN','tindata')      IS NULL ALTER TABLE dbo.SAZMAN ADD tindata NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.SAZMAN','PHAZ_TOL')     IS NULL ALTER TABLE dbo.SAZMAN ADD PHAZ_TOL FLOAT NULL;
IF COL_LENGTH('dbo.SAZMAN','HAZ_TOL')      IS NULL ALTER TABLE dbo.SAZMAN ADD HAZ_TOL FLOAT NULL;
IF COL_LENGTH('dbo.SAZMAN','AMALKARD')     IS NULL ALTER TABLE dbo.SAZMAN ADD AMALKARD FLOAT NULL;
IF COL_LENGTH('dbo.SAZMAN','FINALS')       IS NULL ALTER TABLE dbo.SAZMAN ADD FINALS BIT NULL;
GO
UPDATE dbo.SAZMAN
   SET NAME = N'شرکت آزمایشی سفیر', YEA = 1405, BEDEHKAR = 103, GHAYM = 1,
       SMSACT = 0;   -- پیامک خاموش: هیچ آزمونی نباید واقعاً پیامک بفرستد
GO

/* ── ۲. کاربر ویزیتور و تنظیمات پیش‌فرض کاربر ─────────────────────────────── */
IF COL_LENGTH('dbo.SALA_DTL','DEFAULT_NAHVA') IS NULL
    ALTER TABLE dbo.SALA_DTL ADD DEFAULT_NAHVA INT NULL;
GO

-- salesrep / 444444 — همان کدگذاری CL_METHODS: نام کاربری −۲۰، رمز −۱۰ و ۳ نویسهٔ پرکننده در هر طرف
MERGE dbo.SALA_DTL AS T
USING (VALUES (9004, N'_MXQ_^Q\', N'WWW******WWW', 1, 0, N'310-1-1')) AS S (IDD, SAL_NAME, PSAL_NAME, GRSAL, ENABL, HES)
    ON T.IDD = S.IDD
WHEN MATCHED THEN UPDATE SET T.SAL_NAME = S.SAL_NAME, T.PSAL_NAME = S.PSAL_NAME,
                             T.GRSAL = S.GRSAL, T.ENABL = S.ENABL, T.HES = S.HES
WHEN NOT MATCHED THEN INSERT (IDD, SAL_NAME, PSAL_NAME, GRSAL, ENABL, HES)
                      VALUES (S.IDD, S.SAL_NAME, S.PSAL_NAME, S.GRSAL, S.ENABL, S.HES);
GO

-- ساختار از ScriptSqly.Main.cs
IF OBJECT_ID(N'dbo.DEFAULTDEP', N'U') IS NULL
CREATE TABLE dbo.DEFAULTDEP (
    TFSAZMAN INT NULL,
    SHIFT    INT NULL,
    USERID   INT NOT NULL CONSTRAINT PK_DEFAULTDEP PRIMARY KEY,
    CRT      DATETIME NULL DEFAULT (GETDATE()),
    UID      INT NULL
);
GO
-- ساختار از ScriptSqly.Blazor.cs
IF OBJECT_ID(N'dbo.UserState', N'U') IS NULL
CREATE TABLE dbo.UserState (
    UserId    INT           NOT NULL PRIMARY KEY,
    StateJson NVARCHAR(MAX) NOT NULL
);
GO

/* ── ۳. کدینگ پایهٔ فروش ─────────────────────────────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM dbo.DEPART)        INSERT dbo.DEPART (DEPATMAN, DEPNAME) VALUES (1, N'فروش');
IF NOT EXISTS (SELECT 1 FROM dbo.SHIFT)         INSERT dbo.SHIFT (SHIFT_ID, SHNAME) VALUES (1, N'صبح');
IF NOT EXISTS (SELECT 1 FROM dbo.CUSTKIND)      INSERT dbo.CUSTKIND (CUST_COD, CUSTKNAME) VALUES (1, N'عمده'), (2, N'خرده');
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_VAHEDS)   INSERT dbo.TCOD_VAHEDS (CODE, NAMES) VALUES (1, N'عدد'), (2, N'کارتن');
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_ANBAR_KIND) INSERT dbo.TCOD_ANBAR_KIND (CODE, ANB_KIND) VALUES (1, N'محصول');
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_ANBAR)    INSERT dbo.TCOD_ANBAR (CODE, NAMES, KIND) VALUES (1, N'انبار محصول', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_STUFGROUP) INSERT dbo.TCOD_STUFGROUP (CODE, NAMES) VALUES (1, N'محصولات');
IF NOT EXISTS (SELECT 1 FROM dbo.PRICE_PAYNO)
    INSERT dbo.PRICE_PAYNO (PPID, PPAME, TR_DATE, USERNAME, MODAT)
    VALUES (0, N'آزاد', GETDATE(), N'System', 0), (1, N'نقدی', GETDATE(), N'System', 0),
           (2, N'چک ۳۰ روزه', GETDATE(), N'System', 30);
IF NOT EXISTS (SELECT 1 FROM dbo.PRICE_ELAMIE)
    INSERT dbo.PRICE_ELAMIE (PEPID, PEPNAME, PEPDATE, TR_DATE, USERNAME, PEPDEPART)
    VALUES (1, N'اعلامیه قیمت ۱۴۰۵', 14050101, GETDATE(), N'System', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.PRICE_ELAMIETF)
    INSERT dbo.PRICE_ELAMIETF (PEID, PENAME, PEDATE, TR_DATE, USERNAME, PEPDEPART)
    VALUES (1, N'تخفیف پایه ۱۴۰۵', 14050101, GETDATE(), N'System', 1);
GO

IF OBJECT_ID(N'dbo.TCOD_OSTAN', N'U') IS NULL
CREATE TABLE dbo.TCOD_OSTAN (OSCODE INT NOT NULL PRIMARY KEY, OSNAME NVARCHAR(100) NOT NULL);
IF OBJECT_ID(N'dbo.TCOD_CITY', N'U') IS NULL
CREATE TABLE dbo.TCOD_CITY (CITYCODE INT NOT NULL PRIMARY KEY, CITYNAME NVARCHAR(100) NOT NULL, OSCODE INT NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_OSTAN)
    INSERT dbo.TCOD_OSTAN VALUES (1, N'تهران'), (2, N'یزد');
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_CITY)
    INSERT dbo.TCOD_CITY VALUES (11, N'تهران', 1), (12, N'شهریار', 1), (21, N'یزد', 2), (22, N'میبد', 2);
GO

/* ── ۴. حساب‌ها: بدهکاران تجاری (مشتریان) و ویزیتورها ──────────────────────── */
IF NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER = 103) INSERT dbo.TOTA_HES (NUMBER, NAME) VALUES (103, N'حساب‌های دریافتنی تجاری');
IF NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER = 310) INSERT dbo.TOTA_HES (NUMBER, NAME) VALUES (310, N'ویزیتورها');
IF NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER = 610) INSERT dbo.TOTA_HES (NUMBER, NAME) VALUES (610, N'فروش');
IF NOT EXISTS (SELECT 1 FROM dbo.DETA_HES WHERE N_KOL = 103 AND NUMBER = 1) INSERT dbo.DETA_HES (N_KOL, NUMBER, NAME) VALUES (103, 1, N'مشتریان');
IF NOT EXISTS (SELECT 1 FROM dbo.DETA_HES WHERE N_KOL = 310 AND NUMBER = 1) INSERT dbo.DETA_HES (N_KOL, NUMBER, NAME) VALUES (310, 1, N'ویزیتورهای فروش');
IF NOT EXISTS (SELECT 1 FROM dbo.DETA_HES WHERE N_KOL = 610 AND NUMBER = 1) INSERT dbo.DETA_HES (N_KOL, NUMBER, NAME) VALUES (610, 1, N'فروش محصولات');
GO
MERGE dbo.TDETA_HES AS T
USING (VALUES
    (103, 1, 1, N'فروشگاه آزمایشی الف', N'تهران، خیابان آزادی، پلاک ۱', N'02100000001', N'09120000001', 1, 1, 11, 35.70, 51.40),
    (103, 1, 2, N'فروشگاه آزمایشی ب',   N'تهران، میدان ونک، پلاک ۲',   N'02100000002', N'09120000002', 1, 1, 11, 35.75, 51.41),
    (103, 1, 3, N'پخش آزمایشی ج',       N'یزد، بلوار جمهوری، پلاک ۳',  N'03500000003', N'09130000003', 1, 2, 21, 31.89, 54.36),
    (103, 1, 4, N'سوپرمارکت آزمایشی د', N'میبد، خیابان امام، پلاک ۴',  N'03500000004', N'09130000004', 2, 2, 22, 32.24, 54.01),
    (103, 1, 5, N'مشتری مسدود آزمایشی', N'تهران، شهریار، پلاک ۵',      N'02100000005', N'09120000005', 1, 1, 12, 35.66, 51.06),
    (310, 1, 1, N'ویزیتور آزمایشی',      N'تهران',                      N'02100000100', N'09120000100', NULL, 1, 11, NULL, NULL),
    (610, 1, 1, N'فروش داخلی',           NULL,                           NULL,           NULL,           NULL, NULL, NULL, NULL, NULL)
) AS S (N_KOL, NUMBER, TNUMBER, NAME, ADDRESS, TEL, MOBILE, CUST_COD, OSTANID, SHAHRID, Latitude, Longitude)
    ON T.N_KOL = S.N_KOL AND T.NUMBER = S.NUMBER AND T.TNUMBER = S.TNUMBER
WHEN NOT MATCHED THEN INSERT (N_KOL, NUMBER, TNUMBER, NAME, ADDRESS, TEL, MOBILE, CUST_COD, OSTANID, SHAHRID, Latitude, Longitude, ROUTE_NAME)
    VALUES (S.N_KOL, S.NUMBER, S.TNUMBER, S.NAME, S.ADDRESS, S.TEL, S.MOBILE, S.CUST_COD, S.OSTANID, S.SHAHRID, S.Latitude, S.Longitude,
            CASE WHEN S.N_KOL = 103 THEN N'مسیر آزمایشی' END);
GO

-- گردش حساب مشتری الف برای صورت‌حساب: یک فروش و یک دریافت
IF NOT EXISTS (SELECT 1 FROM dbo.DEED_HED WHERE N_S = 90001)
BEGIN
    INSERT dbo.DEED_HED (N_S, DATE_S, SHARH_S, NO_S, USER_NAME) VALUES
        (90001, 14050610, N'فروش آزمایشی', 1, N'System'),
        (90002, 14050620, N'دریافت آزمایشی', 1, N'System');
    INSERT dbo.DEED_DTL (N_S, RADIF, HES_K, HES_M, HES_T, SHARH, BED, BES, HES) VALUES
        (90001, 1, 103, 1, 1, N'فاکتور فروش ۱۰۰', 50000000, 0, N'103-1-1'),
        (90001, 2, 610, 1, 1, N'فاکتور فروش ۱۰۰', 0, 50000000, N'610-1-1'),
        (90002, 1, 112, 1, 1, N'دریافت وجه', 20000000, 0, N'112-1-1'),
        (90002, 2, 103, 1, 1, N'دریافت وجه', 0, 20000000, N'103-1-1');
END;
GO

/* ── ۵. کالا، واحد، موجودی و قیمت ─────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.TCODE_MENUITEM', N'U') IS NULL
CREATE TABLE dbo.TCODE_MENUITEM (
    CODE  FLOAT NOT NULL PRIMARY KEY,
    NAMES NVARCHAR(100) NULL,
    ANBAR INT NULL,
    ID    INT NULL
);
IF OBJECT_ID(N'dbo.MODULE_D', N'U') IS NULL
CREATE TABLE dbo.MODULE_D (CODE NVARCHAR(30) NOT NULL, VAHED INT NOT NULL, NESBAT FLOAT NOT NULL,
                           ID BIGINT IDENTITY(1,1) NOT NULL);
IF OBJECT_ID(N'dbo.VAHEDS', N'U') IS NULL
CREATE TABLE dbo.VAHEDS (CODE NVARCHAR(30) NOT NULL, VAHED INT NOT NULL, NESBAT FLOAT NOT NULL);
IF OBJECT_ID(N'dbo.PRICE_ELAMIE_DTL', N'U') IS NULL
CREATE TABLE dbo.PRICE_ELAMIE_DTL (PEPID INT NOT NULL, PGID INT NOT NULL, PRICE1 FLOAT NULL);
IF OBJECT_ID(N'dbo.PRICE_ELAMIETF_DTL', N'U') IS NULL
CREATE TABLE dbo.PRICE_ELAMIETF_DTL (PEID INT NOT NULL, CUSTCODE INT NOT NULL, PPID INT NOT NULL,
                                     TF1 FLOAT NULL, TF2 FLOAT NULL);
IF OBJECT_ID(N'dbo.OPANBACCESS', N'U') IS NULL
CREATE TABLE dbo.OPANBACCESS (USERCO INT NOT NULL, ANBCO INT NOT NULL);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TCODE_MENUITEM)
    INSERT dbo.TCODE_MENUITEM (CODE, NAMES, ANBAR, ID) VALUES (1, N'لبنیات', 1, 1), (2, N'پودرها', 1, 2);
IF NOT EXISTS (SELECT 1 FROM dbo.STUF_DEF WHERE CODE = N'1001')
    INSERT dbo.STUF_DEF (CODE, NAME, VAHED, RADAH, MABL_F, B_SEF, MAX_M, MENUIT, PGID, OKF, CMBAA, vra, TOZIH) VALUES
        (N'1001', N'شیر پاستوریزه ۱ لیتری', 1, 1, 450000,  500000,  550000,  1, 1, 1, 1, 10, N'آزمایشی'),
        (N'1002', N'ماست ۲ کیلویی',         1, 1, 900000,  1000000, 1100000, 1, 2, 1, 1, 10, N'آزمایشی'),
        (N'2001', N'پودر شیر ۲۵ کیلویی',    1, 1, 30000000, 32000000, 35000000, 2, 3, 1, 0, 0, N'آزمایشی'),
        (N'2002', N'کالای غیرفعال',          1, 1, 100000,  100000,  100000,  2, 4, 0, 0, 0, N'نباید در لیست بیاید');
IF NOT EXISTS (SELECT 1 FROM dbo.STUF_FSK)
    INSERT dbo.STUF_FSK (CODE, ANBAR, MOGODI_A, FI_A, MABL_A, MANDAH_A, MIN_M, MAX_M) VALUES
        (N'1001', 1, 500, 400000, 200000000, 500, 50, 0),
        (N'1002', 1, 200, 800000, 160000000, 200, 20, 0),
        (N'2001', 1, 10,  28000000, 280000000, 10, 2, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.MODULE_D)
    INSERT dbo.MODULE_D (CODE, VAHED, NESBAT) VALUES (N'1001', 2, 12), (N'1002', 2, 6);
IF NOT EXISTS (SELECT 1 FROM dbo.VAHEDS)
    INSERT dbo.VAHEDS (CODE, VAHED, NESBAT) VALUES (N'1001', 2, 12), (N'1002', 2, 6);
IF NOT EXISTS (SELECT 1 FROM dbo.PRICE_ELAMIE_DTL)
    INSERT dbo.PRICE_ELAMIE_DTL (PEPID, PGID, PRICE1) VALUES (1, 1, 480000), (1, 2, 950000), (1, 3, 31000000);
IF NOT EXISTS (SELECT 1 FROM dbo.PRICE_ELAMIETF_DTL)
    INSERT dbo.PRICE_ELAMIETF_DTL (PEID, CUSTCODE, PPID, TF1, TF2) VALUES (1, 1, 0, 5, 0), (1, 1, 1, 7, 0), (1, 2, 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.OPANBACCESS)
    INSERT dbo.OPANBACCESS (USERCO, ANBCO) VALUES (9001, 1), (9004, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.DEFAULTDEP)
    INSERT dbo.DEFAULTDEP (TFSAZMAN, SHIFT, USERID) VALUES (1, 1, 9001), (1, 1, 9004);
GO

/* موجودی = اول دوره + ورودی‌ها − خروجی‌ها. نسخهٔ ساده‌شدهٔ توابع واقعی؛
   ستون‌ها همان‌هایی‌اند که ItemsController و DatabaseService می‌خوانند. */
CREATE OR ALTER FUNCTION dbo.AK_MOGO_AVL_KOL (@dt2 BIGINT, @ANBAR INT)
RETURNS TABLE AS RETURN (
    SELECT x.CODE, x.ANBAR, SUM(x.MEG) AS SMEGH
    FROM (
        SELECT CODE, ANBAR, MOGODI_A AS MEG FROM dbo.STUF_FSK WHERE ANBAR = @ANBAR
        UNION ALL
        SELECT i.CODE, i.ANBAR, i.MEGHk
        FROM dbo.INVO_LST i JOIN dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
        WHERE i.TAG IN (1, 7, 9, 24) AND h.DATE_N <= @dt2 AND i.ANBAR = @ANBAR
    ) x
    GROUP BY x.CODE, x.ANBAR
);
GO
CREATE OR ALTER FUNCTION dbo.AK_MOGO_FR (@dt2 BIGINT, @ANBAR INT)
RETURNS TABLE AS RETURN (
    SELECT i.CODE, i.ANBAR, SUM(i.MEGHk) AS MEG
    FROM dbo.INVO_LST i JOIN dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE i.TAG IN (2, 5, 8, 10, 11, 26) AND h.DATE_N <= @dt2 AND i.ANBAR = @ANBAR
    GROUP BY i.CODE, i.ANBAR
);
GO

/* دفتر تفصیلی یک حساب در بازهٔ تاریخ — ستون‌ها همان‌هایی‌اند که صورت‌حساب مشتری
   (CustomersController و PdfGenerator) می‌خواند. */
CREATE OR ALTER FUNCTION dbo.QDAFTARTAFZIL2_H (@StartDate BIGINT, @EndDate BIGINT, @HesabCode NVARCHAR(80))
RETURNS TABLE AS RETURN (
    SELECT d.HES_K, d.HES_M, t.NAME AS TAFZILN, d.HES, d.SHARH, d.BED, d.BES, d.N_S, h.DATE_S,
           CAST(0 AS FLOAT) AS MAND, d.id
    FROM dbo.DEED_DTL d
    JOIN dbo.DEED_HED h ON h.N_S = d.N_S
    LEFT JOIN dbo.CUST_HESAB t ON t.hes = d.HES
    WHERE d.HES = @HesabCode AND h.DATE_S BETWEEN @StartDate AND @EndDate
);
GO

/* ── ۶. ویزیتور: پورسانت، مسیر، برنامهٔ روز ────────────────────────────────── */
IF OBJECT_ID(N'dbo.VISITORS_PORSANT', N'U') IS NULL
CREATE TABLE dbo.VISITORS_PORSANT (PORID INT NOT NULL PRIMARY KEY, HES NVARCHAR(80) NOT NULL, NAME NVARCHAR(100) NULL);
IF OBJECT_ID(N'dbo.VISITORS_PORSANT_KALA', N'U') IS NULL
CREATE TABLE dbo.VISITORS_PORSANT_KALA (PORID INT NOT NULL, CODE NVARCHAR(30) NOT NULL, PORSANT FLOAT NULL);
IF OBJECT_ID(N'dbo.Visit_route', N'U') IS NULL
CREATE TABLE dbo.Visit_route (ROUTE_NAME NVARCHAR(100) NOT NULL PRIMARY KEY, HES NVARCHAR(80) NOT NULL, RACTIVE BIT NOT NULL DEFAULT (1));
IF OBJECT_ID(N'dbo.Visit_route_dtl', N'U') IS NULL
CREATE TABLE dbo.Visit_route_dtl (IDR INT IDENTITY(1,1) PRIMARY KEY, ROUTE_NAME NVARCHAR(100) NOT NULL,
                                  COUST_NO NVARCHAR(80) NOT NULL, RACTIVE BIT NOT NULL DEFAULT (1));
IF OBJECT_ID(N'dbo.VISITORS_DAY', N'U') IS NULL
CREATE TABLE dbo.VISITORS_DAY (HES NVARCHAR(80) NOT NULL, VDATE BIGINT NOT NULL, OKF BIT NOT NULL DEFAULT (0),
                               CONSTRAINT PK_VISITORS_DAY PRIMARY KEY (HES, VDATE));
IF OBJECT_ID(N'dbo.VISITORS_DAY_DTL', N'U') IS NULL
CREATE TABLE dbo.VISITORS_DAY_DTL (ID BIGINT IDENTITY(1,1) PRIMARY KEY, HES NVARCHAR(80) NOT NULL, VDATE BIGINT NOT NULL,
                                   COUST_NO NVARCHAR(80) NOT NULL, CDATE DATETIME NULL, RACTIVE BIT NULL,
                                   CLASS INT NULL, TOPLACE INT NULL, CRT DATETIME NULL, UID INT NULL);
IF OBJECT_ID(N'dbo.VISITOR_DTL', N'U') IS NULL
CREATE TABLE dbo.VISITOR_DTL (ID BIGINT IDENTITY(1,1) PRIMARY KEY, NUMBER FLOAT NOT NULL, TAG FLOAT NOT NULL,
                              CUST_NO NVARCHAR(80) NULL, DARSAD FLOAT NULL, PURSANT FLOAT NULL, TOZIH NVARCHAR(200) NULL,
                              STAT INT NULL, PORID INT NULL, LOG NVARCHAR(500) NULL);
IF OBJECT_ID(N'dbo.BLOCK_CUSTOMER', N'U') IS NULL
CREATE TABLE dbo.BLOCK_CUSTOMER (HES NVARCHAR(80) NOT NULL, ENDBLK BIT NULL);
IF OBJECT_ID(N'dbo.BLOCK_HES', N'U') IS NULL
CREATE TABLE dbo.BLOCK_HES (HES NVARCHAR(80) NOT NULL, USERCO INT NOT NULL);
IF OBJECT_ID(N'dbo.AZAE', N'U') IS NULL
CREATE TABLE dbo.AZAE (HES NVARCHAR(80) NOT NULL, TOPETEB FLOAT NULL, ID BIGINT IDENTITY(1,1) NOT NULL);
IF OBJECT_ID(N'dbo.last_generate', N'U') IS NULL
CREATE TABLE dbo.last_generate (hes NVARCHAR(80) NOT NULL, lastdt NVARCHAR(20) NULL);
GO
CREATE OR ALTER VIEW dbo.Q_BEDEHBESTANH_MAIN AS
    SELECT HES, SUM(BED) AS BEDM, SUM(BES) AS BESM FROM dbo.DEED_DTL GROUP BY HES;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.VISITORS_PORSANT)
    INSERT dbo.VISITORS_PORSANT (PORID, HES, NAME) VALUES (1, N'310-1-1', N'پورسانت ویزیتور آزمایشی');
IF NOT EXISTS (SELECT 1 FROM dbo.VISITORS_PORSANT_KALA)
    INSERT dbo.VISITORS_PORSANT_KALA (PORID, CODE, PORSANT) VALUES (1, N'1001', 2), (1, N'1002', 2), (1, N'2001', 1), (1, N'2002', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Visit_route)
    INSERT dbo.Visit_route (ROUTE_NAME, HES, RACTIVE) VALUES (N'مسیر آزمایشی', N'310-1-1', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Visit_route_dtl)
    INSERT dbo.Visit_route_dtl (ROUTE_NAME, COUST_NO, RACTIVE)
    VALUES (N'مسیر آزمایشی', N'103-1-1', 1), (N'مسیر آزمایشی', N'103-1-2', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.VISITORS_DAY)
    INSERT dbo.VISITORS_DAY (HES, VDATE, OKF) VALUES (N'310-1-1', 14050701, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.VISITORS_DAY_DTL)
    INSERT dbo.VISITORS_DAY_DTL (HES, VDATE, COUST_NO, CDATE, RACTIVE, CRT, UID)
    VALUES (N'310-1-1', 14050701, N'103-1-1', GETDATE(), 1, GETDATE(), 9004),
           (N'310-1-1', 14050701, N'103-1-2', GETDATE(), 1, GETDATE(), 9004);
IF NOT EXISTS (SELECT 1 FROM dbo.BLOCK_CUSTOMER)
    INSERT dbo.BLOCK_CUSTOMER (HES, ENDBLK) VALUES (N'103-1-5', 0);   -- ENDBLK=0 یعنی هنوز مسدود است
IF NOT EXISTS (SELECT 1 FROM dbo.AZAE)
    INSERT dbo.AZAE (HES, TOPETEB) VALUES (N'103-1-1', 100000000);
GO

/* ── ۷. پاداش فاکتور (ساختار از ScriptSqly.Main.cs) ──────────────────────── */
IF OBJECT_ID(N'dbo.RewardRules', N'U') IS NULL
CREATE TABLE dbo.RewardRules (
    RuleID INT IDENTITY(1,1) PRIMARY KEY, ProductID_Target NVARCHAR(15) NOT NULL, Quantity_Threshold INT NOT NULL,
    Reward_Type NVARCHAR(50) NOT NULL, Reward_ProductID NVARCHAR(15) NOT NULL, Reward_Quantity INT NULL,
    Reward_Discount_Percentage DECIMAL(5,2) NULL, IsActive BIT NOT NULL, StartDate BIGINT NULL, EndDate BIGINT NULL,
    Description NVARCHAR(200) NULL, CRT DATETIME NULL, UID INT NULL);
IF OBJECT_ID(N'dbo.InvoiceRewards', N'U') IS NULL
CREATE TABLE dbo.InvoiceRewards (
    InvoiceRewardID BIGINT IDENTITY(1,1) PRIMARY KEY, InvoiceNumber FLOAT NOT NULL, InvoiceTag FLOAT NOT NULL,
    CustomerID NVARCHAR(40) NULL, RewardRuleID INT NOT NULL, ProductCode_Earned NVARCHAR(15) NOT NULL,
    Quantity_Earned INT NOT NULL, Reward_Given_Type NVARCHAR(50) NOT NULL, Reward_Given_ProductCode NVARCHAR(15) NULL,
    Reward_Given_Quantity INT NULL, Reward_Given_Discount_Amount FLOAT NULL, RewardDate BIGINT NULL,
    RecordedBy_UserID INT NULL, CRT DATETIME NULL DEFAULT (GETDATE()), UID INT NULL);
GO

/* ── ۸. کارتابل اتوماسیون، پیامک، گزارش تولید ──────────────────────────────── */
IF OBJECT_ID(N'dbo.MESAGEP', N'U') IS NULL
CREATE TABLE dbo.MESAGEP (
    IDNUM INT IDENTITY(1,1) PRIMARY KEY, PERSONEL INT NOT NULL, COMP_COD NVARCHAR(80) NULL,
    PAYAM NVARCHAR(MAX) NULL, STATUS INT NOT NULL DEFAULT (1), STDATE BIGINT NULL, STTIME INT NULL,
    USERNAME NVARCHAR(100) NULL, CRT DATETIME NULL, UID INT NULL);
IF OBJECT_ID(N'dbo.REMAINDER', N'U') IS NULL
CREATE TABLE dbo.REMAINDER (
    IDNUM INT IDENTITY(1,1) PRIMARY KEY, PERSONEL INT NOT NULL, COMP_COD NVARCHAR(80) NULL,
    PAYAM NVARCHAR(MAX) NULL, STATUS INT NOT NULL DEFAULT (1), STDATE BIGINT NULL, STTIME INT NULL,
    USERNAME NVARCHAR(100) NULL, CTDATE BIGINT NULL, CTTIME INT NULL, CRT DATETIME NULL, UID INT NULL);
IF OBJECT_ID(N'dbo.SMS_SENDS', N'U') IS NULL
CREATE TABLE dbo.SMS_SENDS (
    ID BIGINT IDENTITY(1,1) PRIMARY KEY, SM_DT BIGINT NULL, SM_TT NVARCHAR(10) NULL, SM_DTQ BIGINT NULL, SM_TTQ NVARCHAR(10) NULL,
    SM_AMobiles NVARCHAR(MAX) NULL, AMSG NVARCHAR(MAX) NULL, NUMBER FLOAT NULL, TAGS FLOAT NULL, id_sms NVARCHAR(100) NULL,
    CUST_NO NVARCHAR(80) NULL, USERNAME NVARCHAR(100) NULL, STATUSSMS NVARCHAR(100) NULL, CRT DATETIME NULL);
IF OBJECT_ID(N'dbo.PRD_ProductionReports', N'U') IS NULL
CREATE TABLE dbo.PRD_ProductionReports (
    Id INT IDENTITY(1,1) PRIMARY KEY, UserId INT NOT NULL, ReportDate DATETIME NULL, WheyCompany NVARCHAR(200) NULL,
    ConcentrationStart TIME NULL, ConcentrationEnd TIME NULL, ConcentrationWheyQty DECIMAL(18,2) NULL,
    SprayStart TIME NULL, SprayEnd TIME NULL, SprayPowderQty DECIMAL(18,2) NULL, DryMatterQty NVARCHAR(100) NULL,
    ProductType NVARCHAR(100) NULL, CounterNumber NVARCHAR(100) NULL, Description NVARCHAR(MAX) NULL,
    CreatedAt DATETIME NOT NULL DEFAULT (GETDATE()));
-- فرم عمومی شکایت مشتری (ComplaintsController)
IF OBJECT_ID(N'dbo.CustomerComplaints', N'U') IS NULL
CREATE TABLE dbo.CustomerComplaints (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CustomerFirstName NVARCHAR(100) NOT NULL, CustomerLastName NVARCHAR(100) NOT NULL, CustomerMobile NVARCHAR(20) NOT NULL,
    CustomerEmail NVARCHAR(200) NULL, CustomerAddress NVARCHAR(500) NULL, ProductTypeComplaint NVARCHAR(200) NULL,
    PizzaType NVARCHAR(100) NULL, ProductWeight NVARCHAR(50) NULL, ProductionDate DATETIME NULL, ExpiryDate DATETIME NULL,
    ProductCode NVARCHAR(100) NULL, OtherDairyProductName NVARCHAR(200) NULL, PurchaseLocation NVARCHAR(200) NULL,
    PurchaseDate DATETIME NULL, BatchNumber NVARCHAR(100) NULL, ComplaintRegisteredDate DATETIME NULL,
    IsComplaintType_TasteSmell BIT NOT NULL, IsComplaintType_Packaging BIT NOT NULL, IsComplaintType_WrongExpiryDate BIT NOT NULL,
    IsComplaintType_NonConformity BIT NOT NULL, IsComplaintType_ForeignObject BIT NOT NULL, IsComplaintType_AbnormalTexture BIT NOT NULL,
    IsComplaintType_Mold BIT NOT NULL, IsComplaintType_Other BIT NOT NULL, ComplaintType_OtherDescription NVARCHAR(500) NULL,
    ComplaintDescription NVARCHAR(MAX) NOT NULL, CustomerActionTaken BIT NOT NULL, CustomerActionDescription NVARCHAR(MAX) NULL,
    RequestedResolution_Refund BIT NOT NULL, RequestedResolution_Replacement BIT NOT NULL,
    RequestedResolution_FurtherInvestigation BIT NOT NULL, RequestedResolution_Explanation NVARCHAR(MAX) NULL,
    InformationConfirmed BIT NOT NULL, CreatedAt DATETIME NOT NULL DEFAULT (GETDATE()));
GO

/* ── ۹. پیش‌نیازهای قدیمیِ مهاجرت بهای تمام‌شده ─────────────────────────────
   تابع MOGHA_ANBAR (ScriptSqly.CostClose.cs / 21-mogha-anbar-tiebreak-fix.sql)
   این جدول‌ها را می‌خواند و بدون آن‌ها ساخته نمی‌شود — و چون شکستش گرفته
   نمی‌شود، بقیهٔ مهاجرت (از جمله جدول‌های دستیار هوش مصنوعی) هم اجرا نمی‌شود. */
IF OBJECT_ID(N'dbo.TAGCOD', N'U') IS NULL
CREATE TABLE dbo.TAGCOD (CODE INT NOT NULL PRIMARY KEY, BARGAH NVARCHAR(60) NULL, tartib INT NULL);
IF OBJECT_ID(N'dbo.ANBGRD_HEAD', N'U') IS NULL
CREATE TABLE dbo.ANBGRD_HEAD (GRD_NUM INT NOT NULL PRIMARY KEY, GRD_DATE BIGINT NULL, GRD_ANBAR INT NULL,
                              GRD_HES NVARCHAR(80) NULL, N_S FLOAT NULL, USER_NAME NVARCHAR(100) NULL);
IF OBJECT_ID(N'dbo.ANBGRD_LST', N'U') IS NULL
CREATE TABLE dbo.ANBGRD_LST (GRD_NUM INT NOT NULL, CODE NVARCHAR(30) NOT NULL, MOG FLOAT NULL, NUM1 FLOAT NULL,
                             NUM2 FLOAT NULL, NUM3 FLOAT NULL, MABL FLOAT NULL, EKH FLOAT NULL);
IF OBJECT_ID(N'dbo.BACK_HEAD', N'U') IS NULL
CREATE TABLE dbo.BACK_HEAD (NUMBER FLOAT NOT NULL, NUMBER1 FLOAT NULL, ta FLOAT NULL, DATE_N BIGINT NULL);
IF OBJECT_ID(N'dbo.HEAD_MANF', N'U') IS NULL
CREATE TABLE dbo.HEAD_MANF (FNUMB INT NOT NULL PRIMARY KEY, CODE NVARCHAR(30) NULL, NAMES NVARCHAR(200) NULL,
                            NUMBER FLOAT NULL, TAG FLOAT NULL, DATE_N BIGINT NULL, DATE_ACTIV BIGINT NULL, GHEYMAT INT NULL,
                            N_KOL INT NULL, TNUMBER INT NULL, N_S FLOAT NULL, BASE FLOAT NULL, IMBIBE_MANF FLOAT NULL,
                            IMBIBE_SAR FLOAT NULL, SA_HOUR FLOAT NULL, SA_NHOU FLOAT NULL,
                            TOZIH NVARCHAR(200) NULL, CRT DATETIME NULL, UID INT NULL);
IF OBJECT_ID(N'dbo.DTL_MANF', N'U') IS NULL
CREATE TABLE dbo.DTL_MANF (ID BIGINT IDENTITY(1,1) PRIMARY KEY, FNUMB INT NOT NULL, CODE NVARCHAR(30) NULL, ANBAR INT NULL,
                           MEGH FLOAT NULL, MEGHK FLOAT NULL, VAHED_K INT NULL, MABLK FLOAT NULL, SMABL FLOAT NULL,
                           PERT FLOAT NULL, DELTA FLOAT NULL, TOZIH NVARCHAR(200) NULL, NOTE NVARCHAR(200) NULL,
                           CRT DATETIME NULL, UID INT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TAGCOD)
    INSERT dbo.TAGCOD (CODE, BARGAH) VALUES
        (0, N'موجودی اول دوره'), (1, N'رسید خرید'), (2, N'فاکتور فروش'), (5, N'انتقالی - خروج'),
        (7, N'تولید-ورود'), (9, N'حواله ورود'), (10, N'حواله خروج'), (20, N'پیش فاکتور'), (22, N'برگشت فروش');
GO

PRINT N'✅ جدول‌های قدیمی و دادهٔ نمونهٔ فروش آماده‌اند (کاربر salesrep / 444444).';
GO
