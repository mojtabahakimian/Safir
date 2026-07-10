/*
    PAY2 Dual Payroll Deed Mode - Phase 0 Pre-Deploy Audit
    ------------------------------------------------------
    Purpose:
      Read-only audit script for preparing the dual payroll deed generation work.

    Safety:
      - This script does not create, alter, update, insert into permanent tables, or delete data.
      - Golden Master output is stored only in local temp tables for the current session.
      - Review the result sets with the payroll/accounting owner before implementation.

    How to use:
      1) Run the whole script against the target customer database.
      2) Inspect result sets in order.
      3) For Golden Master capture, set @GoldenRunId1..3 after selecting candidates.
*/

SET NOCOUNT ON;

PRINT N'PAY2 Dual Payroll Deed Mode - Phase 0 Pre-Deploy Audit';
PRINT N'ExecutedAt: ' + CONVERT(NVARCHAR(30), GETDATE(), 121);
PRINT N'Database: ' + DB_NAME();

/* ============================================================
   A) SQL Server version and database compatibility
   ============================================================ */
SELECT
    @@VERSION AS FullVersion,
    SERVERPROPERTY('ProductVersion') AS ProductVersion,
    SERVERPROPERTY('ProductMajorVersion') AS ProductMajorVersion,
    DATABASEPROPERTYEX(DB_NAME(), 'CompatibilityLevel') AS CompatibilityLevel;

/* ============================================================
   B) Installed PAY2 stored procedure definitions
   ============================================================ */
SELECT
    o.name AS ObjectName,
    o.modify_date,
    OBJECT_DEFINITION(o.object_id) AS ObjectDefinition
FROM sys.objects o
WHERE o.object_id IN
(
    OBJECT_ID(N'dbo.SP_PAY2_GEN_DEED'),
    OBJECT_ID(N'dbo.SP_PAY2_GET_ADVANCES'),
    OBJECT_ID(N'dbo.SP_PAY2_GEN_DEED_SETTLE')
)
ORDER BY o.name;

/* ============================================================
   C) Accounting and PAY2 table column audit
   ============================================================ */
SELECT
    c.TABLE_NAME,
    c.ORDINAL_POSITION,
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE,
    c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN
(
    N'TOTA_HES',
    N'DETA_HES',
    N'TDETA_HES',
    N'TDETA_HES2',
    N'TDETA_HES3',
    N'TDETA_HES4',
    N'DEED_HED',
    N'DEED_DTL',
    N'PAY2_EMPLOYEE',
    N'PAY2_WORKSHOP',
    N'PAY2_WORKSHOP_ACC',
    N'PAY2_RUN',
    N'PAY2_PERIOD',
    N'PAY2_RUN_LINE'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

/* ============================================================
   D) Current workshop account settings, including absence/presence
      of OTHER_DED_ACCOUNT
   ============================================================ */
SELECT
    W.WS_ID,
    W.WS_CODE,
    W.WS_NAME,
    A.ACC_KEY,
    A.ACC_CODE,
    A.ACC_DESC
FROM PAY2_WORKSHOP W
LEFT JOIN PAY2_WORKSHOP_ACC A ON A.WS_ID = W.WS_ID
ORDER BY W.WS_ID, A.ACC_KEY;

SELECT
    W.WS_ID,
    W.WS_CODE,
    W.WS_NAME,
    MAX(CASE WHEN A.ACC_KEY = N'SALARY_PAYABLE' THEN A.ACC_CODE END) AS SALARY_PAYABLE,
    MAX(CASE WHEN A.ACC_KEY = N'ADV_HES' THEN A.ACC_CODE END) AS ADV_HES,
    MAX(CASE WHEN A.ACC_KEY = N'LOAN_HES' THEN A.ACC_CODE END) AS LOAN_HES,
    MAX(CASE WHEN A.ACC_KEY = N'OTHER_DED_ACCOUNT' THEN A.ACC_CODE END) AS OTHER_DED_ACCOUNT
FROM PAY2_WORKSHOP W
LEFT JOIN PAY2_WORKSHOP_ACC A ON A.WS_ID = W.WS_ID
GROUP BY W.WS_ID, W.WS_CODE, W.WS_NAME
ORDER BY W.WS_ID;

/* ============================================================
   E) ACC_T audit - source values and basic grouping
   ============================================================ */
SELECT
    E.WS_ID,
    E.EMP_ID,
    E.EMP_CODE,
    E.FIRST_NAME,
    E.LAST_NAME,
    E.ACC_T,
    A.ACC_CODE AS SALARY_PAYABLE
FROM PAY2_EMPLOYEE E
LEFT JOIN PAY2_WORKSHOP_ACC A
       ON A.WS_ID = E.WS_ID
      AND A.ACC_KEY = N'SALARY_PAYABLE'
ORDER BY E.WS_ID, E.EMP_ID;

;WITH EmpAcc AS
(
    SELECT
        E.WS_ID,
        E.EMP_ID,
        E.EMP_CODE,
        E.FIRST_NAME,
        E.LAST_NAME,
        E.ACC_T,
        A.ACC_CODE AS SALARY_PAYABLE,
        LTRIM(RTRIM(ISNULL(E.ACC_T, N''))) AS ACC_T_TRIM,
        CASE
            WHEN E.ACC_T IS NULL OR LTRIM(RTRIM(E.ACC_T)) = N'' THEN 0
            ELSE LEN(LTRIM(RTRIM(E.ACC_T))) - LEN(REPLACE(LTRIM(RTRIM(E.ACC_T)), N'-', N'')) + 1
        END AS SegmentCount
    FROM PAY2_EMPLOYEE E
    LEFT JOIN PAY2_WORKSHOP_ACC A
           ON A.WS_ID = E.WS_ID
          AND A.ACC_KEY = N'SALARY_PAYABLE'
), Classified AS
(
    SELECT
        *,
        CASE
            WHEN ACC_T_TRIM = N'' THEN N'NULL_OR_EMPTY'
            WHEN ACC_T_TRIM LIKE N'%-' OR ACC_T_TRIM LIKE N'-%' THEN N'LEADING_OR_TRAILING_DASH'
            WHEN ACC_T_TRIM LIKE N'%--%' THEN N'DUPLICATE_DASH'
            WHEN ACC_T_TRIM LIKE N'%[^0-9۰-۹٠-٩-]%' THEN N'NON_NUMERIC_CHAR'
            WHEN SegmentCount = 1 THEN N'ONE_SEGMENT_LEGACY_CANDIDATE'
            WHEN SegmentCount = 2 THEN N'TWO_LEVEL'
            WHEN SegmentCount = 3 THEN N'THREE_LEVEL'
            WHEN SegmentCount = 4 THEN N'FOUR_LEVEL'
            WHEN SegmentCount = 5 THEN N'FIVE_LEVEL'
            WHEN SegmentCount = 6 THEN N'SIX_LEVEL'
            WHEN SegmentCount > 6 THEN N'MORE_THAN_SIX'
            ELSE N'OTHER'
        END AS ACC_T_GROUP
    FROM EmpAcc
)
SELECT
    ACC_T_GROUP,
    COUNT(*) AS RowCount
FROM Classified
GROUP BY ACC_T_GROUP
ORDER BY ACC_T_GROUP;

;WITH EmpAcc AS
(
    SELECT
        E.WS_ID,
        E.EMP_ID,
        E.EMP_CODE,
        E.FIRST_NAME,
        E.LAST_NAME,
        E.ACC_T,
        A.ACC_CODE AS SALARY_PAYABLE,
        LTRIM(RTRIM(ISNULL(E.ACC_T, N''))) AS ACC_T_TRIM,
        CASE
            WHEN E.ACC_T IS NULL OR LTRIM(RTRIM(E.ACC_T)) = N'' THEN 0
            ELSE LEN(LTRIM(RTRIM(E.ACC_T))) - LEN(REPLACE(LTRIM(RTRIM(E.ACC_T)), N'-', N'')) + 1
        END AS SegmentCount
    FROM PAY2_EMPLOYEE E
    LEFT JOIN PAY2_WORKSHOP_ACC A
           ON A.WS_ID = E.WS_ID
          AND A.ACC_KEY = N'SALARY_PAYABLE'
), Classified AS
(
    SELECT
        *,
        CASE
            WHEN ACC_T_TRIM = N'' THEN N'NULL_OR_EMPTY'
            WHEN ACC_T_TRIM LIKE N'%-' OR ACC_T_TRIM LIKE N'-%' THEN N'LEADING_OR_TRAILING_DASH'
            WHEN ACC_T_TRIM LIKE N'%--%' THEN N'DUPLICATE_DASH'
            WHEN ACC_T_TRIM LIKE N'%[^0-9۰-۹٠-٩-]%' THEN N'NON_NUMERIC_CHAR'
            WHEN SegmentCount = 1 THEN N'ONE_SEGMENT_LEGACY_CANDIDATE'
            WHEN SegmentCount = 2 THEN N'TWO_LEVEL'
            WHEN SegmentCount = 3 THEN N'THREE_LEVEL'
            WHEN SegmentCount = 4 THEN N'FOUR_LEVEL'
            WHEN SegmentCount = 5 THEN N'FIVE_LEVEL'
            WHEN SegmentCount = 6 THEN N'SIX_LEVEL'
            WHEN SegmentCount > 6 THEN N'MORE_THAN_SIX'
            ELSE N'OTHER'
        END AS ACC_T_GROUP
    FROM EmpAcc
), Numbered AS
(
    SELECT
        *,
        ROW_NUMBER() OVER (PARTITION BY ACC_T_GROUP ORDER BY WS_ID, EMP_ID) AS rn
    FROM Classified
)
SELECT
    ACC_T_GROUP,
    WS_ID,
    EMP_ID,
    EMP_CODE,
    FIRST_NAME,
    LAST_NAME,
    ACC_T,
    SALARY_PAYABLE
FROM Numbered
WHERE rn <= 10
ORDER BY ACC_T_GROUP, WS_ID, EMP_ID;

/* ============================================================
   F) Current accounting deed status sample - requires finance owner review
   ============================================================ */
SELECT TOP (100)
    H.N_S,
    H.DATE_S,
    H.SHARH_S,
    H.OKF,
    H.GHATEI,
    H.SGN1,
    H.SGN2,
    H.SGN3,
    H.SGN4
FROM DEED_HED H
ORDER BY H.N_S DESC;

SELECT
    H.OKF,
    H.GHATEI,
    H.SGN1,
    H.SGN2,
    H.SGN3,
    H.SGN4,
    COUNT(*) AS DeedCount
FROM DEED_HED H
GROUP BY H.OKF, H.GHATEI, H.SGN1, H.SGN2, H.SGN3, H.SGN4
ORDER BY DeedCount DESC;

/* ============================================================
   G) Candidate RUN_ID selection for Golden Master
   ============================================================ */
;WITH RunAgg AS
(
    SELECT
        R.RUN_ID,
        R.PER_ID,
        P.WS_ID,
        P.PERIOD_DATE,
        R.STATUS,
        R.DEED_ID_SAL,
        P.DEED_N_S_PAY,
        COUNT(*) AS EmployeeCount,
        SUM(RL.GROSS_PAY) AS GrossPay,
        SUM(RL.INS_WORKER) AS InsWorker,
        SUM(RL.INS_EMPLOYER) AS InsEmployer,
        SUM(RL.TAX_AMOUNT) AS TaxAmount,
        SUM(RL.ADVANCE_DED) AS AdvanceDed,
        SUM(RL.LOAN_DED) AS LoanDed,
        SUM(RL.OTHER_DED) AS OtherDed,
        SUM(RL.NET_PAY) AS NetPay
    FROM PAY2_RUN R
    INNER JOIN PAY2_PERIOD P ON P.PER_ID = R.PER_ID
    INNER JOIN PAY2_RUN_LINE RL ON RL.RUN_ID = R.RUN_ID
    WHERE R.STATUS IN (2, 3)
    GROUP BY R.RUN_ID, R.PER_ID, P.WS_ID, P.PERIOD_DATE, R.STATUS, R.DEED_ID_SAL, P.DEED_N_S_PAY
)
SELECT TOP (10)
    N'SIMPLE_NO_ADVANCE_NO_LOAN_NO_OTHER_DED' AS CandidateType,
    *
FROM RunAgg
WHERE ISNULL(AdvanceDed, 0) = 0
  AND ISNULL(LoanDed, 0) = 0
  AND ISNULL(OtherDed, 0) = 0
ORDER BY RUN_ID DESC;

;WITH RunAgg AS
(
    SELECT
        R.RUN_ID,
        R.PER_ID,
        P.WS_ID,
        P.PERIOD_DATE,
        R.STATUS,
        R.DEED_ID_SAL,
        P.DEED_N_S_PAY,
        COUNT(*) AS EmployeeCount,
        SUM(RL.GROSS_PAY) AS GrossPay,
        SUM(RL.INS_WORKER) AS InsWorker,
        SUM(RL.INS_EMPLOYER) AS InsEmployer,
        SUM(RL.TAX_AMOUNT) AS TaxAmount,
        SUM(RL.ADVANCE_DED) AS AdvanceDed,
        SUM(RL.LOAN_DED) AS LoanDed,
        SUM(RL.OTHER_DED) AS OtherDed,
        SUM(RL.NET_PAY) AS NetPay
    FROM PAY2_RUN R
    INNER JOIN PAY2_PERIOD P ON P.PER_ID = R.PER_ID
    INNER JOIN PAY2_RUN_LINE RL ON RL.RUN_ID = R.RUN_ID
    WHERE R.STATUS IN (2, 3)
    GROUP BY R.RUN_ID, R.PER_ID, P.WS_ID, P.PERIOD_DATE, R.STATUS, R.DEED_ID_SAL, P.DEED_N_S_PAY
)
SELECT TOP (10)
    N'WITH_INS_TAX_ADVANCE_LOAN' AS CandidateType,
    *
FROM RunAgg
WHERE ISNULL(InsWorker, 0) > 0
  AND ISNULL(TaxAmount, 0) > 0
  AND ISNULL(AdvanceDed, 0) > 0
  AND ISNULL(LoanDed, 0) > 0
ORDER BY RUN_ID DESC;

;WITH RunAgg AS
(
    SELECT
        R.RUN_ID,
        R.PER_ID,
        P.WS_ID,
        P.PERIOD_DATE,
        R.STATUS,
        R.DEED_ID_SAL,
        P.DEED_N_S_PAY,
        COUNT(*) AS EmployeeCount,
        SUM(RL.GROSS_PAY) AS GrossPay,
        SUM(RL.INS_WORKER) AS InsWorker,
        SUM(RL.INS_EMPLOYER) AS InsEmployer,
        SUM(RL.TAX_AMOUNT) AS TaxAmount,
        SUM(RL.ADVANCE_DED) AS AdvanceDed,
        SUM(RL.LOAN_DED) AS LoanDed,
        SUM(RL.OTHER_DED) AS OtherDed,
        SUM(RL.NET_PAY) AS NetPay
    FROM PAY2_RUN R
    INNER JOIN PAY2_PERIOD P ON P.PER_ID = R.PER_ID
    INNER JOIN PAY2_RUN_LINE RL ON RL.RUN_ID = R.RUN_ID
    WHERE R.STATUS IN (2, 3)
    GROUP BY R.RUN_ID, R.PER_ID, P.WS_ID, P.PERIOD_DATE, R.STATUS, R.DEED_ID_SAL, P.DEED_N_S_PAY
)
SELECT TOP (10)
    N'WITH_OTHER_DED' AS CandidateType,
    *
FROM RunAgg
WHERE ISNULL(OtherDed, 0) > 0
ORDER BY RUN_ID DESC;

/* ============================================================
   H) Golden Master capture
      Set these variables after reviewing candidate result sets.
   ============================================================ */
DECLARE @GoldenRunId1 INT = NULL; -- simple run
DECLARE @GoldenRunId2 INT = NULL; -- run with insurance/tax/advance/loan
DECLARE @GoldenRunId3 INT = NULL; -- run with OTHER_DED

IF OBJECT_ID('tempdb..#GoldenRuns') IS NOT NULL DROP TABLE #GoldenRuns;
CREATE TABLE #GoldenRuns
(
    RunLabel NVARCHAR(100) NOT NULL,
    RUN_ID INT NOT NULL
);

IF @GoldenRunId1 IS NOT NULL INSERT INTO #GoldenRuns (RunLabel, RUN_ID) VALUES (N'SIMPLE', @GoldenRunId1);
IF @GoldenRunId2 IS NOT NULL INSERT INTO #GoldenRuns (RunLabel, RUN_ID) VALUES (N'INS_TAX_ADV_LOAN', @GoldenRunId2);
IF @GoldenRunId3 IS NOT NULL INSERT INTO #GoldenRuns (RunLabel, RUN_ID) VALUES (N'OTHER_DED', @GoldenRunId3);

IF OBJECT_ID('tempdb..#GoldenMaster') IS NOT NULL DROP TABLE #GoldenMaster;
CREATE TABLE #GoldenMaster
(
    RunLabel NVARCHAR(100) NULL,
    RUN_ID INT NULL,
    HES_CODE NVARCHAR(100) NULL,
    SHARH NVARCHAR(500) NULL,
    BED BIGINT NULL,
    BES BIGINT NULL,
    ACC_KEY NVARCHAR(50) NULL,
    EMP_ID INT NULL
);

DECLARE @RunLabel NVARCHAR(100), @RunId INT;
DECLARE gm_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT RunLabel, RUN_ID FROM #GoldenRuns ORDER BY RunLabel;

OPEN gm_cur;
FETCH NEXT FROM gm_cur INTO @RunLabel, @RunId;

WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO #GoldenMaster (HES_CODE, SHARH, BED, BES, ACC_KEY, EMP_ID)
    EXEC dbo.SP_PAY2_GEN_DEED @RUN_ID = @RunId, @CALC_BY = NULL;

    UPDATE #GoldenMaster
       SET RunLabel = @RunLabel,
           RUN_ID = @RunId
     WHERE RunLabel IS NULL
       AND RUN_ID IS NULL;

    FETCH NEXT FROM gm_cur INTO @RunLabel, @RunId;
END;

CLOSE gm_cur;
DEALLOCATE gm_cur;

SELECT
    RunLabel,
    RUN_ID,
    COUNT(*) AS ArticleCount,
    SUM(ISNULL(BED, 0)) AS TotalBed,
    SUM(ISNULL(BES, 0)) AS TotalBes,
    SUM(ISNULL(BED, 0)) - SUM(ISNULL(BES, 0)) AS Difference
FROM #GoldenMaster
GROUP BY RunLabel, RUN_ID
ORDER BY RunLabel, RUN_ID;

SELECT
    RunLabel,
    RUN_ID,
    HES_CODE,
    SHARH,
    BED,
    BES,
    ACC_KEY,
    EMP_ID
FROM #GoldenMaster
ORDER BY RunLabel, RUN_ID, ACC_KEY, EMP_ID, HES_CODE, SHARH;

/* ============================================================
   I) Current SP output risk scan for selected Golden Master rows
   ============================================================ */
SELECT
    RunLabel,
    RUN_ID,
    HES_CODE,
    SHARH,
    BED,
    BES,
    ACC_KEY,
    EMP_ID,
    CASE
        WHEN HES_CODE IS NULL OR LTRIM(RTRIM(HES_CODE)) = N'' THEN N'EMPTY_HES_CODE'
        WHEN SHARH IS NULL OR LTRIM(RTRIM(SHARH)) = N'' THEN N'EMPTY_SHARH'
        WHEN ISNULL(BED, 0) < 0 OR ISNULL(BES, 0) < 0 THEN N'NEGATIVE_AMOUNT'
        WHEN ISNULL(BED, 0) > 0 AND ISNULL(BES, 0) > 0 THEN N'BOTH_BED_BES'
        WHEN ISNULL(BED, 0) = 0 AND ISNULL(BES, 0) = 0 THEN N'ZERO_ARTICLE'
        ELSE N'OK_FORMAT_ONLY'
    END AS RiskStatus
FROM #GoldenMaster
ORDER BY RunLabel, RUN_ID, RiskStatus, ACC_KEY;

PRINT N'Phase 0 audit script completed. No permanent data was changed by this script.';
