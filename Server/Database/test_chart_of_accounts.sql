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
    (218, N'سازمان‌های بیمه و مالیات')
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
    (218, 1, N'بیمه و مالیات پرداختنی')
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
