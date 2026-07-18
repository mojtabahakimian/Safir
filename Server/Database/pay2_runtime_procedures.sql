SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/*
  PAY2 runtime procedures omitted from Server/Database/schema.sql.
  Source aligned with Server/Info/ScriptSqly.cs.
*/

CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_REVERT_RUN]
    @RUN_ID    INT,
    @REVERT_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @STATUS      TINYINT;
    DECLARE @PER_ID      INT;
    DECLARE @IS_LATEST   BIT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT
        @STATUS      = R.STATUS,
        @PER_ID      = R.PER_ID,
        @IS_LATEST   = R.IS_LATEST,
        @PERIOD_DATE = P.PERIOD_DATE
    FROM dbo.PAY2_RUN AS R
    INNER JOIN dbo.PAY2_PERIOD AS P
        ON R.PER_ID = P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    IF @PER_ID IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: محاسبه‌ای با این شناسه یافت نشد.', 16, 1);
        RETURN;
    END;

    IF @STATUS >= 3
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: سند حسابداری صادر شده — برگشت ممکن نیست.', 16, 1);
        RETURN;
    END;

    IF @IS_LATEST = 0
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: فقط آخرین نسخه (IS_LATEST=1) قابل برگشت است.', 16, 1);
        RETURN;
    END;

    -- Idempotency guard: once run lines are gone, do not restore leave or loan state again.
    IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID)
    BEGIN
        RETURN;
    END;

    -- Restore the number of loan installments consumed by this run.
    UPDATE L
    SET L.PAID_INST = L.PAID_INST -
    (
        SELECT COUNT(1)
        FROM dbo.PAY2_LOAN_SCHED AS LS
        WHERE LS.LOAN_ID = L.LOAN_ID
          AND LS.RUN_ID = @RUN_ID
    )
    FROM dbo.PAY2_LOAN AS L
    WHERE EXISTS
    (
        SELECT 1
        FROM dbo.PAY2_LOAN_SCHED AS LS
        WHERE LS.LOAN_ID = L.LOAN_ID
          AND LS.RUN_ID = @RUN_ID
    );

    UPDATE dbo.PAY2_LOAN_SCHED
    SET RUN_ID = NULL,
        PAID_AT = NULL
    WHERE RUN_ID = @RUN_ID;

    -- Restore leave minutes, protected against negative USED_MIN.
    UPDATE LB
    SET LB.USED_MIN =
        CASE
            WHEN LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT) < 0 THEN 0
            ELSE LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT)
        END,
        LB.UPDATED_AT = GETDATE()
    FROM dbo.PAY2_LEAVE_BAL AS LB
    INNER JOIN dbo.PAY2_ATTENDANCE AS A
        ON LB.EMP_ID = A.EMP_ID
    WHERE A.PER_ID = @PER_ID
      AND LB.[YEAR] = (@PERIOD_DATE / 10000)
      AND A.LEAVE_DAYS > 0;

    DELETE FROM dbo.PAY2_RUN_DETAIL
    WHERE RUN_ID = @RUN_ID;

    DELETE FROM dbo.PAY2_RUN_LINE
    WHERE RUN_ID = @RUN_ID;

    UPDATE dbo.PAY2_RUN
    SET STATUS = 1,
        NOTES = SUBSTRING
        (
            ISNULL(NOTES, N'') +
            N' | Reverted by ' +
            CAST(ISNULL(@REVERT_BY, 0) AS NVARCHAR(20)),
            1,
            300
        )
    WHERE RUN_ID = @RUN_ID;

    UPDATE dbo.PAY2_PERIOD
    SET STATUS = 2
    WHERE PER_ID = @PER_ID;
END;
GO

IF OBJECT_ID(N'dbo.SP_PAY2_REVERT_RUN', N'P') IS NULL
    THROW 52200, 'SP_PAY2_REVERT_RUN was not created.', 1;
GO

PRINT N'PAY2 runtime procedures created successfully.';
GO
