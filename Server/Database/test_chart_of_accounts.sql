/* =============================================================================
   یک دفتر حساب حداقلی برای دیتابیس تست
   =============================================================================
   چرا لازم است: SP_PAY2_GEN_DEED پیش از ساختن سند، تک‌تک حساب‌های به‌کاررفته
   را در دفتر حساب‌های حسابداری (TOTA_HES / DETA_HES / TDETA_HES) جست‌وجو
   می‌کند و اگر حسابی نباشد — یا کمتر از سه سطح (کل-معین-تفصیلی) داشته باشد —
   صدور را متوقف می‌کند.

   نه schema.sql و نه pay2_seed.sql هیچ ردیفی در این سه جدول نمی‌گذارند، پس
   بدون این فایل، مرحله‌ی «سند حسابداری» در آزمون سرتاسری اصلاً قابل رسیدن
   نیست و زنجیره نیمه‌کاره می‌ماند.

   کدهای پرسنلی (213-1-<EMP_CODE>) دقیقاً با ستون ACC_T در PAY2_EMPLOYEE که
   pay2_seed.sql می‌سازد هم‌خوان است.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── جدول‌های کد کمکی که TOTA_HES با کلید خارجی به آن‌ها وابسته است ────────
   ستون‌های NO_HES / M_D / GROUP در TOTA_HES پیش‌فرض ۰ دارند و هر سه به این
   سه جدول FK می‌خورند؛ تا وقتی کد ۰ در آن‌ها نباشد، هیچ حسابِ کلی درج نمی‌شود. */
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_HESKIND  WHERE CODE = 0)
    INSERT dbo.TCOD_HESKIND  (CODE, NAMES, CRT) VALUES (0, N'نامشخص (آزمایشی)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_HESVAZ   WHERE CODE = 0)
    INSERT dbo.TCOD_HESVAZ   (CODE, NAMES, CRT) VALUES (0, N'نامشخص (آزمایشی)', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.TCOD_HESGROUP WHERE CODE = 0)
    INSERT dbo.TCOD_HESGROUP (CODE, NAMES, CRT) VALUES (0, N'نامشخص (آزمایشی)', GETDATE());
GO

/* ── سطح کل ───────────────────────────────────────────────────────────────── */
MERGE dbo.TOTA_HES AS t
USING (VALUES
    (71,  N'هزینه حقوق و دستمزد'),
    (112, N'موجودی نقد و بانک'),
    (213, N'پرداختنی‌های پرسنلی'),
    (218, N'سازمان‌های بیمه و مالیات'),
    -- سند تفصیلی کامل حساب هزینه را «ریشه + شماره‌ی تفصیلیِ نوع قلم» می‌سازد،
    -- پس هر مرکز هزینه باید شاخه‌ی مستقل خودش را داشته باشد. با ساختار قبلی
    -- (71-1-1 تا 71-1-4) ریشه‌ی هر چهار مرکز یکسان می‌شد و روی هم می‌افتادند.
    (711, N'هزینه حقوق — تولید'),
    (712, N'هزینه حقوق — اداری'),
    (713, N'هزینه حقوق — فروش'),
    (714, N'هزینه حقوق — خدمات')
) AS s(NUMBER, NAME) ON t.NUMBER = s.NUMBER
WHEN NOT MATCHED THEN INSERT (NUMBER, NAME, CRT) VALUES (s.NUMBER, s.NAME, GETDATE());
GO

/* ── سطح معین ─────────────────────────────────────────────────────────────── */
MERGE dbo.DETA_HES AS t
USING (VALUES
    (71,  1, N'حقوق و دستمزد'),
    (112, 1, N'بانک‌ها'),
    (213, 1, N'حقوق پرسنل (تفصیلی به تفکیک نفر)'),
    (213, 2, N'کسورات و پرداختنی‌های حقوق'),
    (218, 1, N'بیمه و مالیات پرداختنی'),
    (711, 1, N'هزینه حقوق تولید'),
    (712, 1, N'هزینه حقوق اداری'),
    (713, 1, N'هزینه حقوق فروش'),
    (714, 1, N'هزینه حقوق خدمات')
) AS s(N_KOL, NUMBER, NAME) ON t.N_KOL = s.N_KOL AND t.NUMBER = s.NUMBER
WHEN NOT MATCHED THEN INSERT (N_KOL, NUMBER, NAME, CRT) VALUES (s.N_KOL, s.NUMBER, s.NAME, GETDATE());
GO

/* ── سطح تفصیلی: حساب‌های کارگاه ──────────────────────────────────────────── */
MERGE dbo.TDETA_HES AS t
USING (VALUES
    (71,  1, 1, N'هزینه حقوق — تولید'),
    (71,  1, 2, N'هزینه حقوق — اداری'),
    (71,  1, 3, N'هزینه حقوق — فروش'),
    (71,  1, 4, N'هزینه حقوق — خدمات'),
    (71,  1, 5, N'هزینه بیمه سهم کارفرما'),
    (112, 1, 1, N'بانک پرداخت حقوق'),
    (213, 2, 1, N'حقوق پرداختنی'),
    (213, 2, 2, N'صندوق وام کارکنان'),
    (213, 2, 3, N'مساعده کارکنان'),
    (213, 2, 4, N'سایر کسورات حقوق'),
    (218, 1, 1, N'سازمان تأمین اجتماعی'),
    (218, 1, 2, N'اداره امور مالیاتی')
) AS s(N_KOL, NUMBER, TNUMBER, NAME) ON t.N_KOL = s.N_KOL AND t.NUMBER = s.NUMBER AND t.TNUMBER = s.TNUMBER
WHEN NOT MATCHED THEN INSERT (N_KOL, NUMBER, TNUMBER, NAME, CRT) VALUES (s.N_KOL, s.NUMBER, s.TNUMBER, s.NAME, GETDATE());
GO

/* ── سطح تفصیلی: نوع قلم، برای هر مرکز هزینه ──────────────────────────────
   شماره‌ها همان EXP_TAFSILI در PAY2_ITEM_DEF است: ۱=حقوق، ۲=اضافه‌کار،
   ۳=راندمان، ۴=اولاد، ۵=خواربار، ۹=سایر، ۱۰=بیمه کارفرما، ۱۱=شیفت، ۱۲=بن،
   ۱۳=تأهل/سنوات. */
INSERT INTO dbo.TDETA_HES (N_KOL, NUMBER, TNUMBER, NAME, CRT)
SELECT K.N_KOL, 1, T.TNUMBER, CONCAT(K.CNAME, N' — ', T.TNAME), GETDATE()
FROM (VALUES (711, N'تولید'), (712, N'اداری'), (713, N'فروش'), (714, N'خدمات')) K(N_KOL, CNAME)
CROSS JOIN (VALUES
    (1, N'حقوق'), (2, N'اضافه‌کار'), (3, N'راندمان'), (4, N'حق اولاد'),
    (5, N'خواربار و مسکن'), (9, N'سایر'), (10, N'بیمه سهم کارفرما'),
    (11, N'حق شیفت'), (12, N'بن کارگری'), (13, N'حق تأهل/سنوات')
) T(TNUMBER, TNAME)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TDETA_HES X
                  WHERE X.N_KOL = K.N_KOL AND X.NUMBER = 1 AND X.TNUMBER = T.TNUMBER);
GO

/* ── سطح تفصیلی: یک حساب به ازای هر پرسنل، دقیقاً مطابق ACC_T ─────────────── */
INSERT INTO dbo.TDETA_HES (N_KOL, NUMBER, TNUMBER, NAME, CRT)
SELECT 213, 1, TRY_CAST(E.EMP_CODE AS INT), E.LAST_NAME + N' ' + E.FIRST_NAME, GETDATE()
FROM dbo.PAY2_EMPLOYEE E
WHERE TRY_CAST(E.EMP_CODE AS INT) IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.TDETA_HES T
                  WHERE T.N_KOL = 213 AND T.NUMBER = 1 AND T.TNUMBER = TRY_CAST(E.EMP_CODE AS INT));
GO

/* ── محدوده‌ی رزروشده برای پرسنلی که خودِ آزمون سرتاسری می‌سازد ────────────
   آزمون 03-payroll-chain یک پرسنل تازه درج می‌کند و بلافاصله سند حسابداری
   می‌گیرد؛ اگر کد تفصیلی‌اش از قبل در دفتر حساب نباشد، صدور سند رد می‌شود.
   کدهای 99000..99049 از پیش ساخته می‌شوند و آزمون اولین کد آزاد را برمی‌دارد. */
;WITH N AS (
    SELECT TOP (50) 99000 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS CODE
    FROM sys.all_objects
)
INSERT INTO dbo.TDETA_HES (N_KOL, NUMBER, TNUMBER, NAME, CRT)
SELECT 213, 1, N.CODE, CONCAT(N'پرسنل آزمون سرتاسری ', N.CODE), GETDATE()
FROM N
WHERE NOT EXISTS (SELECT 1 FROM dbo.TDETA_HES T
                  WHERE T.N_KOL = 213 AND T.NUMBER = 1 AND T.TNUMBER = N.CODE);
GO

/* ── سرفصل‌های کارگاه برای کارگاه‌هایی که در seed تنظیم نشده‌اند ────────────
   داده‌ی seed چند اجرای قدیمی دارد که برای سنجش «سند تراز» به کار می‌آیند،
   ولی کارگاهشان هیچ سرفصلی ندارد و صدور سند پیش از رسیدن به محاسبات متوقف
   می‌شود. اینجا فقط کارگاه‌های بدون تنظیم پر می‌شوند تا تنظیمات موجود دست
   نخورد. */
INSERT INTO dbo.PAY2_WORKSHOP_ACC (WS_ID, ACC_KEY, ACC_CODE)
SELECT W.WS_ID, A.ACC_KEY, A.ACC_CODE
FROM dbo.PAY2_WORKSHOP W
CROSS JOIN (VALUES
    ('SALARY_EXP_TOLID',    '711-1-1'),
    ('SALARY_EXP_EDARI',    '712-1-1'),
    ('SALARY_EXP_FOROSH',   '713-1-1'),
    ('SALARY_EXP_KHADAMAT', '714-1-1'),
    ('SALARY_PAYABLE',      '213-2-1'),
    ('INS_PAYABLE',         '218-1-1'),
    ('TAX_PAYABLE',         '218-1-2'),
    ('INS_EXP',             '71-1-5'),
    ('ADV_HES',             '213-2-3'),
    ('LOAN_HES',            '213-2-2'),
    ('OTHER_DED_HES',       '213-2-4'),
    ('BANK_PAY_HES',        '112-1-1')
) AS A(ACC_KEY, ACC_CODE)
WHERE NOT EXISTS (SELECT 1 FROM dbo.PAY2_WORKSHOP_ACC X WHERE X.WS_ID = W.WS_ID);
GO

/* ── بررسی: هر ACC_T پرسنلِ فعال باید در دفتر حساب پیدا شود ───────────────── */
IF EXISTS (
    SELECT 1
    FROM dbo.PAY2_EMPLOYEE E
    WHERE E.IS_ACTIVE = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TDETA_HES T
          WHERE E.ACC_T = CONCAT(T.N_KOL, '-', T.NUMBER, '-', T.TNUMBER))
)
    THROW 54100, N'کد تفصیلی بعضی پرسنل در دفتر حساب ساخته نشد — صدور سند در آزمون شکست خواهد خورد.', 1;
GO

PRINT N'✅ دفتر حساب آزمایشی آماده است.';
GO
