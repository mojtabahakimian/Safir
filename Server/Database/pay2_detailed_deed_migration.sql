/* =============================================================================
   سند حسابداری تفصیلی کامل (DEED_MODE = 3) — مهاجرت مستقل
   =============================================================================
   منبع حقیقت Server/Info/ScriptSqly.cs است؛ این فایل همان تغییرها را جدا
   اجرا می‌کند تا بشود روی یک دیتابیسِ موجود (تست یا مشتری) اعمالش کرد بدون
   اجرای برنامه‌ی به‌روزرسانی — همان قراردادی که pay2_acl_migration.sql و
   pay2_schema_catchup.sql دارند.

   محتوا:
     ۱) مقداردهی نگاشت «قلم حکم ← شماره‌ی تفصیلیِ حساب هزینه»
        (خودِ ستون در pay2_schema_catchup.sql ساخته می‌شود)
     ۲) FN_PAY2_ACC_PARENT — برداشتن آخرین سطح یک کد حساب
     ۳) SP_PAY2_GEN_DEED — نسخه‌ی دارای حالت ۳

   ترتیب اجرا: بعد از seed، چون مقداردهی روی ردیف‌های PAY2_ITEM_DEF کار
   می‌کند و پیش از seed آن ردیف‌ها وجود ندارند.

   idempotent است: مقداردهی فقط ردیف‌هایی را می‌گیرد که هنوز مقدار نگرفته‌اند،
   پس ویرایش‌های بعدیِ کاربر با اجرای دوباره بازنویسی نمی‌شود.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NULL
    ALTER TABLE dbo.PAY2_ITEM_DEF ADD EXP_TAFSILI SMALLINT NULL;
GO

/* ── ۱) نگاشت قلم به تفصیلیِ حساب هزینه ─────────────────────────────────── */
UPDATE I SET I.EXP_TAFSILI = V.T
FROM dbo.PAY2_ITEM_DEF I
INNER JOIN (VALUES
    ('BASE_SAL_B',1),('BASE_SAL',1),('SANOVAT_PAYE',1),
    ('OT_NORMAL',2),('OT_HOLIDAY',2),('OT_ADMIN',2),
    ('PERF_BONUS',3),('CHILDREN',4),('HOME',5),
    ('ATTRACT',9),('HARD_COND',9),('NAHAR',9),('OTHER_FIX',9),('TRANSP',9),
    ('SHIFT',11),('GROCERY',12),('FAMILY_ALLOW',13)
) AS V(C,T) ON V.C = I.ITEM_CODE
WHERE I.EXP_TAFSILI IS NULL OR I.EXP_TAFSILI = 9;
GO

/* کسورات هیچ حساب هزینه‌ای ندارند. */
UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI = NULL
WHERE ITEM_TYPE IN (3,4,5) AND EXP_TAFSILI IS NOT NULL;
GO

/* اقلامی که کاربر خودش تعریف کرده روی «سایر» می‌نشینند تا بدون تنظیم اضافه
   کار کنند. */
UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI = 9
WHERE ITEM_TYPE IN (1,2) AND EXP_TAFSILI IS NULL;
GO

/* ── ۲) تابع کمکی ────────────────────────────────────────────────────────── */
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_ACC_PARENT](@CODE NVARCHAR(50))
RETURNS NVARCHAR(50)
AS
BEGIN
    -- «711-1-1» ← «711-1». اگر کد کمتر از دو سطح داشته باشد NULL برمی‌گردد تا
    -- فراخوان مجبور شود ریشه را صریح تنظیم کند و ما حسابِ ناقص نسازیم.
    SET @CODE = NULLIF(LTRIM(RTRIM(@CODE)), '');
    IF @CODE IS NULL OR CHARINDEX('-', @CODE) = 0 RETURN NULL;
    RETURN NULLIF(LEFT(@CODE, LEN(@CODE) - CHARINDEX('-', REVERSE(@CODE))), '');
END;
GO

/* ── ۳) رویه‌ی تولید آرتیکل‌های سند ──────────────────────────────────────── */
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
        CREATE TABLE #Rail (EMP_ID INT PRIMARY KEY, DROP_CODE NVARCHAR(30) NULL);

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
          AND ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) <> ISNULL(R.DROP_CODE, N'');

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
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[0]') AS INT) AS K,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[1]') AS INT) AS M,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[2]') AS INT) AS T1,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[3]') AS INT) AS T2,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[4]') AS INT) AS T3,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[5]') AS INT) AS T4
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
END;
GO

PRINT N'✅ سند تفصیلی کامل (حالت ۳) نصب شد.';
GO
