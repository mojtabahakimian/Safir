/*
    PAY2 dual deed mode Go/No-Go verification
    ------------------------------------------------------------
    Purpose: run this read-only script on a restored copy of the customer database
    before enabling DEFAULT_DEED_MODE = 2 in production.

    It does not modify permanent data. It reports the three financial blockers:
      1) ACC_T shape and person-account resolvability
      2) accounting deed final/editable status patterns
      3) Golden Master comparison candidates for CURRENT_SUMMARY
*/

SET NOCOUNT ON;

SELECT
    @@VERSION AS FullVersion,
    SERVERPROPERTY('ProductVersion') AS ProductVersion,
    SERVERPROPERTY('ProductMajorVersion') AS ProductMajorVersion,
    DATABASEPROPERTYEX(DB_NAME(), 'CompatibilityLevel') AS CompatibilityLevel;

PRINT N'1) ACC_T classification';
;WITH Emp AS
(
    SELECT
        E.WS_ID,
        E.EMP_ID,
        E.EMP_CODE,
        E.FIRST_NAME,
        E.LAST_NAME,
        LTRIM(RTRIM(ISNULL(E.ACC_T, N''))) AS ACC_T,
        A.ACC_CODE AS SALARY_PAYABLE
    FROM dbo.PAY2_EMPLOYEE E
    LEFT JOIN dbo.PAY2_WORKSHOP_ACC A
           ON A.WS_ID = E.WS_ID
          AND A.ACC_KEY = N'SALARY_PAYABLE'
), Classified AS
(
    SELECT *,
        CASE
            WHEN ACC_T = N'' THEN N'NULL_OR_EMPTY'
            WHEN ACC_T LIKE N'%--%' OR ACC_T LIKE N'-%' OR ACC_T LIKE N'%-' THEN N'EMPTY_SEGMENT'
            WHEN ACC_T LIKE N'%[^0-9-]%' THEN N'NON_NUMERIC_OR_UNNORMALIZED'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 0 THEN N'LEAF_ONLY'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 1 THEN N'TWO_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 2 THEN N'THREE_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 3 THEN N'FOUR_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 4 THEN N'FIVE_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 5 THEN N'SIX_LEVEL'
            ELSE N'MORE_THAN_SIX_LEVEL'
        END AS ACC_T_CLASS
    FROM Emp
)
SELECT ACC_T_CLASS, COUNT(*) AS Cnt
FROM Classified
GROUP BY ACC_T_CLASS
ORDER BY ACC_T_CLASS;

;WITH Emp AS
(
    SELECT
        E.WS_ID,
        E.EMP_ID,
        E.EMP_CODE,
        E.FIRST_NAME,
        E.LAST_NAME,
        LTRIM(RTRIM(ISNULL(E.ACC_T, N''))) AS ACC_T,
        A.ACC_CODE AS SALARY_PAYABLE
    FROM dbo.PAY2_EMPLOYEE E
    LEFT JOIN dbo.PAY2_WORKSHOP_ACC A
           ON A.WS_ID = E.WS_ID
          AND A.ACC_KEY = N'SALARY_PAYABLE'
), Classified AS
(
    SELECT *,
        CASE
            WHEN ACC_T = N'' THEN N'NULL_OR_EMPTY'
            WHEN ACC_T LIKE N'%--%' OR ACC_T LIKE N'-%' OR ACC_T LIKE N'%-' THEN N'EMPTY_SEGMENT'
            WHEN ACC_T LIKE N'%[^0-9-]%' THEN N'NON_NUMERIC_OR_UNNORMALIZED'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 0 THEN N'LEAF_ONLY'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 1 THEN N'TWO_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 2 THEN N'THREE_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 3 THEN N'FOUR_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 4 THEN N'FIVE_LEVEL'
            WHEN LEN(ACC_T) - LEN(REPLACE(ACC_T, N'-', N'')) = 5 THEN N'SIX_LEVEL'
            ELSE N'MORE_THAN_SIX_LEVEL'
        END AS ACC_T_CLASS
    FROM Emp
)
SELECT TOP (100)
    ACC_T_CLASS, WS_ID, EMP_ID, EMP_CODE, FIRST_NAME, LAST_NAME, ACC_T, SALARY_PAYABLE,
    CASE
        WHEN ACC_T_CLASS = N'LEAF_ONLY' THEN N'Legacy leaf: application will append this suffix to the target base and then validate existence.'
        WHEN ACC_T <> N'' AND SALARY_PAYABLE IS NOT NULL AND ACC_T NOT LIKE SALARY_PAYABLE + N'-%' THEN N'BLOCKER: full ACC_T is outside SALARY_PAYABLE base.'
        WHEN ACC_T = N'' THEN N'BLOCKER: person-oriented article cannot be resolved.'
        ELSE N'Candidate OK; verify final account existence.'
    END AS GoNoGoNote
FROM Classified
WHERE ACC_T_CLASS IN (N'NULL_OR_EMPTY', N'EMPTY_SEGMENT', N'NON_NUMERIC_OR_UNNORMALIZED', N'MORE_THAN_SIX_LEVEL')
   OR (ACC_T <> N'' AND SALARY_PAYABLE IS NOT NULL AND ACC_T NOT LIKE SALARY_PAYABLE + N'-%' AND ACC_T LIKE N'%-%')
ORDER BY ACC_T_CLASS, WS_ID, EMP_ID;

PRINT N'2) DEED_HED final/editable status patterns';
SELECT
    ISNULL(CAST(OKF AS NVARCHAR(20)), N'<NULL>') AS OKF,
    ISNULL(CAST(GHATEI AS NVARCHAR(20)), N'<NULL>') AS GHATEI,
    ISNULL(CAST(SGN1 AS NVARCHAR(20)), N'<NULL>') AS SGN1,
    ISNULL(CAST(SGN2 AS NVARCHAR(20)), N'<NULL>') AS SGN2,
    ISNULL(CAST(SGN3 AS NVARCHAR(20)), N'<NULL>') AS SGN3,
    ISNULL(CAST(SGN4 AS NVARCHAR(20)), N'<NULL>') AS SGN4,
    COUNT(*) AS Cnt
FROM dbo.DEED_HED
GROUP BY OKF, GHATEI, SGN1, SGN2, SGN3, SGN4
ORDER BY Cnt DESC;

PRINT N'3) Golden Master candidate runs';
SELECT TOP (20)
    R.RUN_ID,
    P.WS_ID,
    P.PERIOD_DATE,
    COUNT(*) AS EmployeeCount,
    SUM(CASE WHEN ISNULL(RL.INS_WORKER,0) + ISNULL(RL.INS_EMPLOYER,0) > 0 THEN 1 ELSE 0 END) AS HasInsuranceRows,
    SUM(CASE WHEN ISNULL(RL.TAX_AMOUNT,0) > 0 THEN 1 ELSE 0 END) AS HasTaxRows,
    SUM(CASE WHEN ISNULL(RL.ADVANCE_DED,0) > 0 THEN 1 ELSE 0 END) AS HasAdvanceRows,
    SUM(CASE WHEN ISNULL(RL.LOAN_DED,0) > 0 THEN 1 ELSE 0 END) AS HasLoanRows,
    SUM(CASE WHEN ISNULL(RL.OTHER_DED,0) > 0 THEN 1 ELSE 0 END) AS HasOtherDedRows,
    SUM(ISNULL(RL.GROSS_PAY,0)) AS GrossPay,
    SUM(ISNULL(RL.NET_PAY,0)) AS NetPay
FROM dbo.PAY2_RUN R
INNER JOIN dbo.PAY2_PERIOD P ON P.PER_ID = R.PER_ID
INNER JOIN dbo.PAY2_RUN_LINE RL ON RL.RUN_ID = R.RUN_ID
WHERE R.STATUS IN (2,3)
GROUP BY R.RUN_ID, P.WS_ID, P.PERIOD_DATE
ORDER BY
    CASE WHEN SUM(CASE WHEN ISNULL(RL.OTHER_DED,0) > 0 THEN 1 ELSE 0 END) > 0 THEN 0 ELSE 1 END,
    R.RUN_ID DESC;

PRINT N'4) Capture Golden Master manually';
PRINT N'Set @RunId below on a database copy, then execute SP_PAY2_GEN_DEED with @DEED_MODE = 1 and compare article count, accounts, descriptions, BED/BES and totals with the legacy installed procedure output.';
DECLARE @RunId INT = NULL;
IF @RunId IS NOT NULL
BEGIN
    EXEC dbo.SP_PAY2_GEN_DEED @RUN_ID = @RunId, @DEED_MODE = 1, @CALC_BY = NULL;
END;
