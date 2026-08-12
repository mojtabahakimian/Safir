/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۳ از ۳ : رویه‌ها

   مدیریت اجرا، اسنپ‌شات و بازگردانی، و گام‌های S00 تا S04.
   قابل اجرای مکرر (همه با CREATE OR ALTER).

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

/* ═══════════════ مدیریت اجرا ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_RunCreate
    @FiscalYear SMALLINT,
    @Month      TINYINT,
    @DateFrom   BIGINT,
    @DateTo     BIGINT,
    @RunKind    TINYINT,          -- 1=آزمايشي 2=قطعي
    @UserName   NVARCHAR(50),
    @Note       NVARCHAR(500) = NULL,
    @RunId      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
                 AND RunKind = 2 AND Status = 3 AND ApprovedAtUtc IS NOT NULL)
    BEGIN
        RAISERROR(N'براي اين ماه يک اجراي قطعي تأييدشده وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month AND Status = 1)
    BEGIN
        RAISERROR(N'يک اجرا براي اين ماه در حال انجام است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @no SMALLINT =
        ISNULL((SELECT MAX(RunNo) FROM dbo.CC_Run
                WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month), 0) + 1;

    DECLARE @prev INT =
        (SELECT TOP 1 RunId FROM dbo.CC_Run
         WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
         ORDER BY RunNo DESC);

    UPDATE dbo.CC_Run SET IsLatest = 0
    WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month;

    INSERT dbo.CC_Run (FiscalYear, PeriodMonth, DateFrom, DateTo, RunNo,
                       PrevRunId, IsLatest, RunKind, Status, StartedByUser, Note)
    VALUES (@FiscalYear, @Month, @DateFrom, @DateTo, @no,
            @prev, 1, @RunKind, 0, @UserName, @Note);

    SET @RunId = SCOPE_IDENTITY();

    INSERT dbo.CC_RunLog (RunId, Severity, Message)
    VALUES (@RunId, 1, CONCAT(N'اجراي شماره ', @no, N' براي دوره ',
                              @FiscalYear, '/', @Month, N' ايجاد شد'));

    COMMIT;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepStart
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Title    NVARCHAR(120),
    @SeqNo    SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @try TINYINT =
        ISNULL((SELECT MAX(Attempt) FROM dbo.CC_RunStep
                WHERE RunId = @RunId AND StepCode = @StepCode), 0) + 1;

    INSERT dbo.CC_RunStep (RunId, StepCode, StepTitle, SeqNo, Attempt, Status, StartedAtUtc)
    VALUES (@RunId, @StepCode, @Title, @SeqNo, @try, 1, SYSUTCDATETIME());

    UPDATE dbo.CC_Run
       SET Status = 1, StartedAtUtc = ISNULL(StartedAtUtc, SYSUTCDATETIME())
     WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepFinish
    @RunId     INT,
    @StepCode  VARCHAR(10),
    @Status    TINYINT,                     -- 2=موفق 3=هشدار 4=خطا 5=رد‌شده
    @Rows      INT           = NULL,
    @Result    NVARCHAR(MAX) = NULL,
    @Error     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE  s
       SET  s.Status        = @Status,
            s.FinishedAtUtc = SYSUTCDATETIME(),
            s.DurationMs    = DATEDIFF(MILLISECOND, s.StartedAtUtc, SYSUTCDATETIME()),
            s.RowsAffected  = @Rows,
            s.ResultJson    = @Result,
            s.ErrorMessage  = @Error
    FROM    dbo.CC_RunStep s
    JOIN   (SELECT RunId, StepCode, MAX(Attempt) AS Attempt
            FROM   dbo.CC_RunStep
            WHERE  RunId = @RunId AND StepCode = @StepCode
            GROUP BY RunId, StepCode) x
           ON x.RunId = s.RunId AND x.StepCode = s.StepCode AND x.Attempt = s.Attempt;

    IF @Status = 4
        UPDATE dbo.CC_Run SET Status = 4 WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_SetFormulasDirty
    @RunId INT, @Dirty BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.CC_Run SET FormulasDirty = @Dirty WHERE RunId = @RunId;
END
GO


/* ═══════════════ اسنپ‌شات و بازگردانی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_Snapshot
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Month    TINYINT,
    @DT1      BIGINT,
    @DT2      BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @bak SYSNAME, @sql NVARCHAR(MAX), @n INT;

    ---- DTL_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_DTL_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT d.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DTL_MANF d
                 JOIN dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DTL_MANF', @bak, @n);

    ---- HEAD_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_HEAD_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT h.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.HEAD_MANF h WHERE h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'HEAD_MANF', @bak, @n);

    ---- DEED_HED : نگاشت شماره اسناد بازه
    SET @bak = CONCAT('CC_BAK_DEED_HED_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT base, N_S, DATE_S INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DEED_HED WHERE DATE_S BETWEEN @a AND @b';
    EXEC sp_executesql @sql, N'@a BIGINT, @b BIGINT', @a = @DT1, @b = @DT2;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DEED_HED', @bak, @n);

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 1, N'اسنپ‌شات گرفته شد');

    SELECT TableName AS جدول, BackupTable AS جدول_پشتيبان, RowsCopied AS تعداد_سطر
    FROM   dbo.CC_Snapshot
    WHERE  RunId = @RunId AND StepCode = @StepCode;
END
GO


/* ═══════════════ S00 — بازبینی ابتدای ماه ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S00_Preflight
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  StepCode = 'S00' AND ISNULL(RunId, -1) = ISNULL(@RunId, -1);

    ---- CHK-03 : فرمول بدون نرخ جذب
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-03', 9, r.DefaultSeverity,
            MIN(CAST(pl.CODE AS BIGINT)), hm.FNUMB, MAX(h.DATE_N), SUM(pl.MEGHK),
            CONCAT(N'فرمول ', hm.FNUMB, N' نرخ جذب هزينه تبديل ندارد')
    FROM    dbo.HEAD_LST  h
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
    CROSS   JOIN dbo.CC_CheckRule r
    WHERE   r.RuleCode = 'CHK-03' AND r.IsActive = 1
      AND   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(hm.IMBIBE_MANF,0) + ISNULL(hm.IMBIBE_SAR,0) = 0
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-03' AND ae.IsActive = 1
                          AND (ae.Code IS NULL
                               OR ae.Code = CAST(pl.CODE AS BIGINT)))
    GROUP BY hm.FNUMB, r.DefaultSeverity;

    ---- CHK-04 : کالاي توليدشده بدون فرمول ماه
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-04', 12, 2, CAST(pl.CODE AS BIGINT),
            N'کالا در اين ماه توليد شده ولي فرمول ماه را ندارد'
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                          AND hm.GHEYMAT = @Month);

    ---- CHK-05 : ماده بدون منبع نرخ
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-05', 4, 1, CAST(d.CODE AS BIGINT),
            N'ماده بدون منبع نرخ — نرخ صفر به کالاهاي بالادست منتقل مي‌شود'
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF p
                        WHERE CAST(p.CODE AS BIGINT) = CAST(d.CODE AS BIGINT)
                          AND p.GHEYMAT = @Month)
      AND   NOT EXISTS (SELECT 1 FROM dbo.KALAS k
                        WHERE k.code = CAST(d.CODE AS BIGINT)
                          AND k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0);

    ---- CHK-06 : حلقه در ساختار فرمول
    IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
    SELECT DISTINCT CAST(h.CODE AS BIGINT) AS P, CAST(d.CODE AS BIGINT) AS C
    INTO   #E
    FROM   dbo.HEAD_MANF h
    JOIN   dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE  h.GHEYMAT = @Month AND h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND  CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);
    CREATE CLUSTERED INDEX IX_E ON #E(P, C);

    ;WITH W AS (
        SELECT P AS Root, C, 1 AS L,
               CAST('/' + CAST(P AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM   #E
        UNION ALL
        SELECT w.Root, e.C, w.L + 1,
               CAST(w.Pt + CAST(e.P AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM   W w JOIN #E e ON e.P = w.C
        WHERE  w.L < 20
          AND  w.Pt NOT LIKE '%/' + CAST(e.C AS VARCHAR(20)) + '/%'
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT DISTINCT @RunId, 'S00', 'CHK-06', 5, 2, Root,
           N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
    FROM   W WHERE C = Root
    OPTION (MAXRECURSION 0);

    DROP TABLE #E;

    ---- CHK-07 : مانده نامتوازن مواد در ۷۵۱ (آستانه نسبي)
    DECLARE @th FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-07'), 0.001);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-07', 13,
            CASE WHEN SUM(d.BED) = 0 OR SUM(d.BES) = 0 THEN 2 ELSE 1 END,
            TRY_CAST(d.HES_T AS BIGINT), TRY_CAST(d.HES_M AS INT),
            SUM(d.BED) - SUM(d.BES),
            CASE WHEN SUM(d.BED) = 0
                 THEN N'ماده با توليد خارج شده ولي با حواله وارد نشده'
                 WHEN SUM(d.BES) = 0
                 THEN N'ماده با حواله وارد شده ولي با توليد خارج نشده'
                 ELSE N'مانده نامتوازن مواد در حساب کالاي در جريان ساخت' END
    FROM    dbo.DEED_DTL d
    JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
    WHERE   d.HES_K = 751 AND d.HES_T <> 99999999
      AND   hd.DATE_S BETWEEN @DT1 AND @DT2
    GROUP BY d.HES_M, d.HES_T
    HAVING  (SUM(d.BED) = 0 AND SUM(d.BES) <> 0)
         OR (SUM(d.BES) = 0 AND SUM(d.BED) <> 0)
         OR (ABS(SUM(d.BED) - SUM(d.BES))
             / NULLIF((SUM(d.BED) + SUM(d.BES)) / 2.0, 0) > @th);

    ---- CHK-09 : نرخ منتشرنشده نيمه‌ساخته
    DECLARE @th9 FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-09'), 0.001);

    ;WITH Khod AS (
        SELECT CAST(hm.CODE AS BIGINT) AS Code,
               SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                      + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha
        FROM   dbo.HEAD_MANF hm JOIN dbo.DTL_MANF d ON d.FNUMB = hm.FNUMB
        WHERE  hm.GHEYMAT = @Month
        GROUP BY CAST(hm.CODE AS BIGINT), hm.FNUMB
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code,
               AVG(d.SMABL) AS Nerkh, COUNT(DISTINCT d.FNUMB) AS Valedha
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            CONCAT(N'نرخ منتشر نشده — بهاي فرمول ', CAST(ROUND(k.Baha,0) AS BIGINT),
                   N' ولي نرخ در ', v.Valedha, N' فرمول بالادست ',
                   CAST(ROUND(v.Nerkh,0) AS BIGINT))
    FROM    Khod k
    JOIN    DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > @th9;

    ---- خلاصه
    SELECT  e.RuleCode AS قاعده, r.RuleName AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده' ELSE N'هشدار' END AS شدت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.StepCode = 'S00' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO


/* ═══════════════ S03 — حذف اسناد حسابداری خالی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S03_DeleteEmptyDeeds
    @RunId INT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 1                  -- ۱ = فقط گزارش، بدون حذف
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Empty') IS NOT NULL DROP TABLE #Empty;

    SELECT h.N_S, h.DATE_S
    INTO   #Empty
    FROM   dbo.DEED_HED h
    WHERE  h.DATE_S BETWEEN @DT1 AND @DT2
      AND  NOT EXISTS (SELECT 1 FROM dbo.DEED_DTL d WHERE d.N_S = h.N_S);

    DECLARE @n INT = (SELECT COUNT(*) FROM #Empty);

    IF @WhatIf = 1
    BEGIN
        SELECT N_S AS شماره_سند, DATE_S AS تاريخ FROM #Empty ORDER BY DATE_S, N_S;
        SELECT @n AS تعداد_سند_قابل_حذف, N'حالت گزارش — چيزي حذف نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DELETE h FROM dbo.DEED_HED h JOIN #Empty e ON e.N_S = h.N_S;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S03', 1, CONCAT(N'حذف اسناد خالي: ', @n, N' سند'),
            (SELECT N_S, DATE_S FROM #Empty FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S03_DeleteEmptyDeeds).
    -- Dapper روی نام‌مستعار فارسی نگاشت نمی‌کند و بی‌صدا صفر برمی‌گرداند؛
    -- شرح فارسی در CC_RunLog بالا ثبت شده است.
    SELECT @n AS Value;
END
GO


/* ═══════════════ S04 — مرتب‌سازی اسناد ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S04_SortDeeds
    @RunId     INT,
    @DT1       BIGINT,
    @DT2       BIGINT,
    @WholeYear BIT = 0,
    @WhatIf    BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    DECLARE @seed FLOAT =
        CASE WHEN @WholeYear = 1 THEN 0
             ELSE ISNULL((SELECT MAX(N_S) FROM dbo.DEED_HED WHERE DATE_S < @DT1), 0) END;

    SELECT  base,
            N_S AS OldNS,
            @seed + ROW_NUMBER() OVER (ORDER BY DATE_S ASC, N_S ASC) AS NewNS
    INTO    #Map
    FROM    dbo.DEED_HED
    WHERE   @WholeYear = 1 OR DATE_S BETWEEN @DT1 AND @DT2;

    CREATE UNIQUE CLUSTERED INDEX IX_Map ON #Map(base);

    DECLARE @total INT   = (SELECT COUNT(*) FROM #Map);
    DECLARE @changed INT = (SELECT COUNT(*) FROM #Map WHERE OldNS <> NewNS);

    IF @WhatIf = 1
    BEGIN
        SELECT TOP 100 base, OldNS AS شماره_فعلي, NewNS AS شماره_جديد
        FROM   #Map WHERE OldNS <> NewNS ORDER BY NewNS;
        SELECT @total AS کل_اسناد, @changed AS تعداد_تغيير,
               N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- تريگرهاي Audit را فقط براي همين نشست کنار مي‌گذاريم
    EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

    -- ۹ جدول فرزند با ON UPDATE CASCADE خودکار به‌روز مي‌شوند
    UPDATE  h
       SET  h.N_S = m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S <> m.NewNS;

    EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S04', 1, N'بازشماره‌گذاري اسناد انجام شد',
            (SELECT @total AS total, @changed AS changed FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S04_SortDeeds).
    -- Value = تعداد اسناد بازشماره‌شده؛ شرح فارسی در CC_RunLog بالا ثبت شد.
    SELECT @changed AS Value, @total AS Total;
END
GO


PRINT N'رويه‌هاي فاز ۱ ايجاد شدند.';

SELECT  name AS رويه, create_date AS تاريخ_ايجاد, modify_date AS آخرين_تغيير
FROM    sys.procedures
WHERE   name LIKE 'CC[_]sp[_]%'
ORDER BY name;
GO
