-- ================================================================
-- ۱. تابع مشترک تولید رشته استاندارد حساب
-- ================================================================
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_BUILD_ACC_CODE]
(
    @HES_K INT,
    @HES_M INT,
    @HES_T INT = NULL,
    @HES_T2 INT = NULL,
    @HES_T3 INT = NULL,
    @HES_T4 INT = NULL
)
RETURNS NVARCHAR(100)
AS
BEGIN
    IF @HES_K IS NULL OR @HES_M IS NULL
        RETURN NULL;

    DECLARE @Result NVARCHAR(100) = CAST(@HES_K AS NVARCHAR) + N'-' + CAST(@HES_M AS NVARCHAR);

    IF @HES_T IS NOT NULL
        SET @Result = @Result + N'-' + CAST(@HES_T AS NVARCHAR);

    IF @HES_T2 IS NOT NULL
        SET @Result = @Result + N'-' + CAST(@HES_T2 AS NVARCHAR);

    IF @HES_T3 IS NOT NULL
        SET @Result = @Result + N'-' + CAST(@HES_T3 AS NVARCHAR);

    IF @HES_T4 IS NOT NULL
        SET @Result = @Result + N'-' + CAST(@HES_T4 AS NVARCHAR);

    RETURN @Result;
END;
GO

-- ================================================================
-- ۲. Resolver مسیر کامل حساب (ACC_T_FULL_PATH)
-- ================================================================
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_RESOLVE_ACCOUNT]
(
    @TargetBase NVARCHAR(50),      -- مثال: 213-1
    @EmployeeAccount NVARCHAR(50)  -- مثال: 112-1-386 یا فقط 386 (حالت Mixed fallback)
)
RETURNS NVARCHAR(100)
AS
BEGIN
    IF @TargetBase IS NULL OR LTRIM(RTRIM(@TargetBase)) = N'' RETURN NULL;
    IF @EmployeeAccount IS NULL OR LTRIM(RTRIM(@EmployeeAccount)) = N'' RETURN NULL;

    -- Normalize characters (Persian/Arabic digits, Unicode hyphens, remove spaces)
    DECLARE @NormEmp NVARCHAR(100) = @EmployeeAccount;
    SET @NormEmp = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@NormEmp, N' ', N''), N'ـ', N'-'), N'−', N'-'), N'–', N'-'), N'—', N'-');
    SET @NormEmp = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@NormEmp, N'۰', N'0'), N'۱', N'1'), N'۲', N'2'), N'۳', N'3'), N'۴', N'4'), N'۵', N'5'), N'۶', N'6'), N'۷', N'7'), N'۸', N'8'), N'۹', N'9');
    SET @NormEmp = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@NormEmp, N'٠', N'0'), N'١', N'1'), N'٢', N'2'), N'٣', N'3'), N'٤', N'4'), N'٥', N'5'), N'٦', N'6'), N'٧', N'7'), N'٨', N'8'), N'٩', N'9');

    DECLARE @NormBase NVARCHAR(50) = @TargetBase;
    SET @NormBase = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@NormBase, N' ', N''), N'ـ', N'-'), N'−', N'-'), N'–', N'-'), N'—', N'-');
    SET @NormBase = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@NormBase, N'۰', N'0'), N'۱', N'1'), N'۲', N'2'), N'۳', N'3'), N'۴', N'4'), N'۵', N'5'), N'۶', N'6'), N'۷', N'7'), N'۸', N'8'), N'۹', N'9');
    SET @NormBase = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@NormBase, N'٠', N'0'), N'١', N'1'), N'٢', N'2'), N'٣', N'3'), N'٤', N'4'), N'٥', N'5'), N'٦', N'6'), N'٧', N'7'), N'٨', N'8'), N'٩', N'9');

    -- Anti-pattern checks
    IF @NormEmp LIKE N'%--%' OR @NormEmp LIKE N'-%' OR @NormEmp LIKE N'%-' RETURN NULL;
    IF @NormBase LIKE N'%--%' OR @NormBase LIKE N'-%' OR @NormBase LIKE N'%-' RETURN NULL;
    IF @NormEmp LIKE N'%[^0-9-]%' RETURN NULL;
    IF @NormBase LIKE N'%[^0-9-]%' RETURN NULL;

    DECLARE @TargetPartsCount INT = LEN(@NormBase) - LEN(REPLACE(@NormBase, '-', '')) + 1;
    DECLARE @EmpPartsCount INT = LEN(@NormEmp) - LEN(REPLACE(@NormEmp, '-', '')) + 1;

    IF @TargetPartsCount > 6 OR @EmpPartsCount > 6 RETURN NULL;

    DECLARE @Result NVARCHAR(100);

    -- حالت A: ACC_T یک Full Path است (مثلاً 213-1-386)
    IF @EmpPartsCount > 1
    BEGIN
        -- استخراج بخش تفصیلی (حذف پایه کل-معین از شخص)
        DECLARE @FirstDash INT = CHARINDEX('-', @NormEmp);
        DECLARE @SecondDash INT = CHARINDEX('-', @NormEmp, @FirstDash + 1);

        IF @SecondDash = 0 RETURN NULL; -- فقط 2 سطح (کل-معین) دارد، تفصیلی برای ترکیب ندارد

        DECLARE @PersonDetails NVARCHAR(50) = SUBSTRING(@NormEmp, @SecondDash + 1, LEN(@NormEmp));
        SET @Result = @NormBase + '-' + @PersonDetails;
    END
    -- حالت B: ACC_T فقط Leaf است (مثلاً 386) - Legacy Fallback
    ELSE
    BEGIN
        SET @Result = @NormBase + '-' + @NormEmp;
    END

    -- Final Validation
    DECLARE @FinalPartsCount INT = LEN(@Result) - LEN(REPLACE(@Result, '-', '')) + 1;
    IF @FinalPartsCount > 6 OR @FinalPartsCount < 2 RETURN NULL;

    RETURN @Result;
END;
GO

-- ================================================================
-- ۳. تابع بررسی موجودیت حساب در دیتابیس (Inline TVF برای پرفورمنس)
-- ================================================================
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_VALIDATE_ACC_EXISTS]
(
    @AccountCode NVARCHAR(100)
)
RETURNS TABLE
AS
RETURN
(
    WITH Parsed AS (
        -- استفاده از تکنیک JSON برای پارس سریع بدون STRING_SPLIT (سازگار با SQL قدیمی)
        SELECT
            @AccountCode AS FullCode,
            CAST(JSON_VALUE('["' + REPLACE(@AccountCode, '-', '","') + '"]', '$[0]') AS INT) AS HES_K,
            CAST(JSON_VALUE('["' + REPLACE(@AccountCode, '-', '","') + '"]', '$[1]') AS INT) AS HES_M,
            CAST(JSON_VALUE('["' + REPLACE(@AccountCode, '-', '","') + '"]', '$[2]') AS INT) AS HES_T,
            CAST(JSON_VALUE('["' + REPLACE(@AccountCode, '-', '","') + '"]', '$[3]') AS INT) AS HES_T2,
            CAST(JSON_VALUE('["' + REPLACE(@AccountCode, '-', '","') + '"]', '$[4]') AS INT) AS HES_T3,
            CAST(JSON_VALUE('["' + REPLACE(@AccountCode, '-', '","') + '"]', '$[5]') AS INT) AS HES_T4,
            LEN(@AccountCode) - LEN(REPLACE(@AccountCode, '-', '')) + 1 AS LevelCount
    )
    SELECT
        FullCode,
        CASE
            WHEN LevelCount = 2 THEN
                CASE WHEN EXISTS (SELECT 1 FROM DETA_HES WHERE N_KOL = HES_K AND NUMBER = HES_M) THEN 1 ELSE 0 END
            WHEN LevelCount = 3 THEN
                CASE WHEN EXISTS (SELECT 1 FROM TDETA_HES WHERE N_KOL = HES_K AND NUMBER = HES_M AND TNUMBER = HES_T) THEN 1 ELSE 0 END
            WHEN LevelCount = 4 THEN
                CASE WHEN EXISTS (SELECT 1 FROM TDETA_HES2 WHERE N_KOL = HES_K AND NUMBER = HES_M AND TNUMBER = HES_T AND TNUMBER2 = HES_T2) THEN 1 ELSE 0 END
            WHEN LevelCount = 5 THEN
                CASE WHEN EXISTS (SELECT 1 FROM TDETA_HES3 WHERE N_KOL = HES_K AND NUMBER = HES_M AND TNUMBER = HES_T AND TNUMBER2 = HES_T2 AND TNUMBER3 = HES_T3) THEN 1 ELSE 0 END
            WHEN LevelCount = 6 THEN
                CASE WHEN EXISTS (SELECT 1 FROM TDETA_HES4 WHERE N_KOL = HES_K AND NUMBER = HES_M AND TNUMBER = HES_T AND TNUMBER2 = HES_T2 AND TNUMBER3 = HES_T3 AND TNUMBER4 = HES_T4) THEN 1 ELSE 0 END
            ELSE 0
        END AS IsValid
    FROM Parsed
    WHERE LevelCount BETWEEN 2 AND 6
      AND HES_K IS NOT NULL AND HES_M IS NOT NULL
);
GO
