-- =========================================================================
-- تست تراز بودن (Balancing Test) اسناد تولیدی
-- =========================================================================
DECLARE @RUN_ID INT = 1; -- (Replace with actual RUN_ID)

-- تست Mode 1: SUMMARY
CREATE TABLE #TempDeedSummary (
    HES_CODE NVARCHAR(100), SHARH NVARCHAR(500), BED BIGINT, BES BIGINT, ACC_KEY NVARCHAR(50), EMP_ID INT NULL
);
INSERT INTO #TempDeedSummary
EXEC SP_PAY2_GEN_DEED_SUMMARY @RUN_ID = @RUN_ID, @CALC_BY = 1;

SELECT
    'SUMMARY_MODE' AS Mode,
    SUM(BED) AS TotalBed,
    SUM(BES) AS TotalBes,
    SUM(BED) - SUM(BES) AS Difference
FROM #TempDeedSummary;

-- تست Mode 2: PERSON_TRACEABLE
CREATE TABLE #TempDeedPerson (
    HES_CODE NVARCHAR(100), SHARH NVARCHAR(500), BED BIGINT, BES BIGINT, ACC_KEY NVARCHAR(50), EMP_ID INT NULL
);
INSERT INTO #TempDeedPerson
EXEC SP_PAY2_GEN_DEED_PERSON @RUN_ID = @RUN_ID, @CALC_BY = 1;

SELECT
    'PERSON_MODE' AS Mode,
    SUM(BED) AS TotalBed,
    SUM(BES) AS TotalBes,
    SUM(BED) - SUM(BES) AS Difference
FROM #TempDeedPerson;

-- Clean up
DROP TABLE #TempDeedSummary;
DROP TABLE #TempDeedPerson;

-- =========================================================================
-- مقایسه خروجی SUMMARY با Golden Master
-- =========================================================================
-- (Run `EXEC SP_PAY2_GEN_DEED_SUMMARY @RUN_ID=X` and visually compare with
-- the baseline you captured in Phase 0. The only expected difference is the
-- appearance of the OTHER_DED_ACCOUNT row in the BES column if OTHER_DED > 0.)
