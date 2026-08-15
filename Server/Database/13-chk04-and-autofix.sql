/* ═══════════════════════════════════════════════════════════════════
   دو تغییر بر اساس درخواست کاربر

   ۱) CHK-04 حالا شماره برگه‌های تولید را هم می‌دهد، نه فقط کد کالا
   ۲) رویه اصلاح خودکار: فرمول همان ماه را به برگه‌ها نسبت می‌دهد

   قابل اجرای مکرر.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ستون جدید برای نگهداری فهرست برگه‌ها و امکان اصلاح خودکار */
IF COL_LENGTH('dbo.CC_Exception','RefList') IS NULL
    ALTER TABLE dbo.CC_Exception ADD RefList NVARCHAR(2000) NULL;
GO
IF COL_LENGTH('dbo.CC_Exception','CanAutoFix') IS NULL
    ALTER TABLE dbo.CC_Exception ADD CanAutoFix BIT NOT NULL DEFAULT 0;
GO
IF COL_LENGTH('dbo.CC_CheckRule','FixProcName') IS NULL
    ALTER TABLE dbo.CC_CheckRule ADD FixProcName SYSNAME NULL;
GO
IF COL_LENGTH('dbo.CC_CheckRule','FixButtonText') IS NULL
    ALTER TABLE dbo.CC_CheckRule ADD FixButtonText NVARCHAR(60) NULL;
GO

UPDATE dbo.CC_CheckRule
   SET FixProcName   = 'CC_sp_Fix_MissingFormula',
       FixButtonText = N'اصلاح خودکار برگه'
 WHERE RuleCode = 'CHK-04';
GO


/* ═══════════════════════════════════════════════════════════════════
   CHK-04 — نسخه‌ای که شماره برگه می‌دهد

   یک سطر به ازای هر کالا، با فهرست برگه‌های متأثر.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Chk04_MissingFormula
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  RuleCode = 'CHK-04' AND ISNULL(RunId,-1) = ISNULL(@RunId,-1);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocTag,
         DocNumber, DocDate, Amount, RefList, CanAutoFix, Description)
    SELECT  @RunId, 'S00', 'CHK-04', 12, 2,
            CAST(pl.CODE AS BIGINT),
            9,
            MIN(h.NUMBER),                       -- اولين برگه
            MIN(h.DATE_N),
            SUM(pl.MEGHK),                       -- جمع مقدار توليد متأثر
            STRING_AGG(CAST(h.NUMBER AS VARCHAR(12)), ', ')
                WITHIN GROUP (ORDER BY h.NUMBER),
            -- اصلاح خودکار فقط وقتي ممکن است که فرمول ماه واقعاً وجود داشته باشد
            CASE WHEN EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                              WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                                AND hm.GHEYMAT = @Month)
                 THEN 1 ELSE 0 END,
            CONCAT(N'کالا در ', COUNT(DISTINCT h.NUMBER),
                   N' برگه توليد شده ولي فرمول ماه ', @Month, N' به آن نسبت داده نشده')
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (
                SELECT 1 FROM dbo.HEAD_MANF hm
                WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                  AND hm.GHEYMAT = @Month)
    GROUP BY CAST(pl.CODE AS BIGINT);

    SELECT  e.Code       AS کد_کالا,
            s.NAME       AS نام_کالا,
            e.Amount     AS جمع_مقدار_توليد,
            e.RefList    AS برگه_ها,
            CASE e.CanAutoFix WHEN 1 THEN N'بله' ELSE N'خير — فرمول ماه وجود ندارد' END
                         AS اصلاح_خودکار
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = e.Code
    WHERE   e.RuleCode = 'CHK-04' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    ORDER BY e.Amount DESC;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   اصلاح خودکار — دکمه‌ای که کاربر می‌زند

   فرمول همان ماه را پیدا و به برگه‌های تولید نسبت می‌دهد.
   @ExceptionId داده شود  → فقط همان یک کالا
   @ExceptionId خالی      → همه کالاهای قابل اصلاح

   @WhatIf = 1 پیش‌فرض است: فقط نشان می‌دهد چه چیزی تغییر خواهد کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_MissingFormula
    @Month       TINYINT,
    @DT1         BIGINT,
    @DT2         BIGINT,
    @RunId       INT           = NULL,
    @ExceptionId BIGINT        = NULL,
    @UserName    NVARCHAR(50)  = N'system',
    @WhatIf      BIT           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- کالاهاي هدف
    IF OBJECT_ID('tempdb..#Target') IS NOT NULL DROP TABLE #Target;
    CREATE TABLE #Target (Code BIGINT PRIMARY KEY);

    INSERT #Target(Code)
    SELECT DISTINCT e.Code
    FROM   dbo.CC_Exception e
    WHERE  e.RuleCode = 'CHK-04'
      AND  e.IsResolved = 0
      AND  e.CanAutoFix = 1
      AND  ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
      AND  (@ExceptionId IS NULL OR e.ExceptionId = @ExceptionId);

    ---- نگاشت کالا به فرمول ماه
    ---- اگر يک کالا چند فرمول در همان ماه داشته باشد، تازه‌ترين انتخاب مي‌شود
    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    SELECT  t.Code,
            f.FNUMB,
            f.Chand
    INTO    #Map
    FROM    #Target t
    CROSS   APPLY (
                SELECT TOP 1
                       hm.FNUMB,
                       COUNT(*) OVER () AS Chand
                FROM   dbo.HEAD_MANF hm
                WHERE  CAST(hm.CODE AS BIGINT) = t.Code
                  AND  hm.GHEYMAT = @Month
                ORDER BY hm.DATE_ACTIV DESC, hm.FNUMB DESC
            ) f;

    ---- سطرهايي که تغيير خواهند کرد
    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- کليد تطبيق id است نه (NUMBER, RADIF): ستون RADIF در INVO_LST
    -- nullable است و روي داده‌ي واقعي مي‌تواند خالي باشد؛ آن‌وقت شرط
    -- «r.Radif = pl.RADIF» در UPDATE هرگز برقرار نمي‌شود (NULL = NULL
    -- در SQL نادرست است) و اصلاح خودکار بي‌صدا هيچ سطري را عوض
    -- نمي‌کند، درحالي‌که تعداد را گزارش مي‌دهد و استثنا را هم مي‌بندد.
    -- id کليد اصلي جدول است و اين حالت را کاملاً حذف مي‌کند.
    SELECT  pl.id               AS InvoId,
            h.NUMBER            AS ProdNo,
            h.DATE_N            AS ProdDate,
            pl.RADIF            AS Radif,
            CAST(pl.CODE AS BIGINT) AS Code,
            pl.N_KOL            AS OldFnumb,
            m.FNUMB             AS NewFnumb,
            pl.MEGHK            AS Meghdar,
            m.Chand             AS ChandFormul
    INTO    #Rows
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    #Map m ON m.Code = CAST(pl.CODE AS BIGINT)
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                          AND hm.GHEYMAT = @Month);

    ---- هشدار: کالايي که در ماه بيش از يک فرمول دارد نياز به انتخاب کاربر دارد
    IF EXISTS (SELECT 1 FROM #Rows WHERE ChandFormul > 1)
        SELECT DISTINCT
               r.Code AS کد_کالا, s.NAME AS نام_کالا, r.ChandFormul AS تعداد_فرمول_ماه,
               N'اين کالا در اين ماه بيش از يک فرمول دارد؛ تازه‌ترين انتخاب شد' AS هشدار
        FROM   #Rows r LEFT JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = r.Code
        WHERE  r.ChandFormul > 1;

    DECLARE @n INT = (SELECT COUNT(*) FROM #Rows);

    IF @WhatIf = 1
    BEGIN
        SELECT  ProdNo    AS شماره_برگه,
                ProdDate  AS تاريخ,
                Code      AS کد_کالا,
                OldFnumb  AS فرمول_فعلي,
                NewFnumb  AS فرمول_جديد,
                Meghdar   AS مقدار
        FROM    #Rows
        ORDER BY ProdDate, ProdNo;

        SELECT @n AS تعداد_سطر_قابل_اصلاح, N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- کدهايي که واقعاً عوض شدند را نگه مي‌داريم تا فقط استثناي همان‌ها
    -- بسته شود. اگر UPDATE به هر دليلي سطري را نگيرد، نبايد استثنا را
    -- «رفع‌شده» علامت بزنيم و عدد قابل‌اصلاح را به‌عنوان عدد اصلاح‌شده
    -- گزارش کنيم — کاربر بايد ببيند که کاري انجام نشده.
    DECLARE @Applied TABLE (Code BIGINT);

    UPDATE  pl
       SET  pl.N_KOL = r.NewFnumb
    OUTPUT  CAST(inserted.CODE AS BIGINT) INTO @Applied(Code)
    FROM    dbo.INVO_LST pl
    JOIN    #Rows r ON r.InvoId = pl.id
    WHERE   pl.TAG = 9;

    DECLARE @appliedRows INT = @@ROWCOUNT;

    ---- ثبت در سابقه
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S00', CASE WHEN @appliedRows = 0 AND @n > 0 THEN 2 ELSE 1 END,
            CASE WHEN @appliedRows = 0 AND @n > 0
                 THEN CONCAT(N'اصلاح خودکار هيچ سطري را عوض نکرد (', @n,
                             N' سطر نامزد بود) — توسط ', @UserName)
                 ELSE CONCAT(N'اصلاح خودکار فرمول برگه‌هاي توليد: ', @appliedRows,
                             N' سطر توسط ', @UserName) END,
            (SELECT ProdNo, Code, OldFnumb, NewFnumb FROM #Rows FOR JSON PATH);

    ---- استثناها بسته مي‌شوند — فقط براي کدهايي که واقعاً اصلاح شدند
    UPDATE  e
       SET  e.IsResolved     = 1,
            e.ResolvedBy     = @UserName,
            e.ResolvedAtUtc  = SYSUTCDATETIME(),
            e.ResolutionNote = N'اصلاح خودکار — فرمول ماه به برگه‌ها نسبت داده شد'
    FROM    dbo.CC_Exception e
    WHERE   e.RuleCode = 'CHK-04'
      AND   ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
      AND   EXISTS (SELECT 1 FROM @Applied a WHERE a.Code = e.Code);

    ---- خروج مواد بايد بازسازي شود، چون فرمول برگه عوض شد
    IF @RunId IS NOT NULL AND @appliedRows > 0
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT @appliedRows AS تعداد_سطر_اصلاح_شده, @n AS تعداد_سطر_نامزد;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   CHK-15 — اصلاح فرمول با مقدار منفی

   @ExceptionId الزامی است: هر سطر فرمول منفی جدا اصلاح می‌شود، نه گروهی،
   چون هر سطر می‌تواند تصمیم متفاوتی بخواهد (صفر یا حذف). شناسه سطر
   (DTL_MANF.id) در CC_Exception.DocNumber ذخیره شده — نگاه کنید به
   CC_sp_S00_Preflight بخش CHK-15.

   @Action = 'zero'   → مقدار (MEGH/MEGHk/MABLK/SMABL) صفر می‌شود، سطر می‌ماند
   @Action = 'delete' → کل سطر فرمول حذف می‌شود

   @WhatIf = 1 پیش‌فرض است: فقط نشان می‌دهد چه چیزی تغییر خواهد کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_NegativeFormulaQty
    @ExceptionId BIGINT,
    @Action      VARCHAR(10),
    @RunId       INT           = NULL,
    @UserName    NVARCHAR(50)  = N'system',
    @WhatIf      BIT           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Action NOT IN ('zero', 'delete')
    BEGIN
        RAISERROR(N'مقدار @Action باید zero یا delete باشد.', 16, 1);
        RETURN;
    END

    DECLARE @DtlId BIGINT, @Code BIGINT;

    SELECT  @DtlId = e.DocNumber, @Code = e.Code
    FROM    dbo.CC_Exception e
    WHERE   e.ExceptionId = @ExceptionId AND e.RuleCode = 'CHK-15';

    IF @DtlId IS NULL
    BEGIN
        RAISERROR(N'این استثنا یافت نشد یا مربوط به CHK-15 نیست.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.DTL_MANF WHERE id = @DtlId)
    BEGIN
        -- سطر قبلاً حذف يا اصلاح شده — فقط استثنا را ببند
        IF @WhatIf = 0
            UPDATE dbo.CC_Exception
               SET IsResolved = 1, ResolvedBy = @UserName, ResolvedAtUtc = SYSUTCDATETIME(),
                   ResolutionNote = N'سطر فرمول از قبل اصلاح شده بود'
             WHERE ExceptionId = @ExceptionId;

        SELECT 0 AS تغيير_يافت, N'سطر فرمول از قبل اصلاح يا حذف شده بود' AS وضعيت;
        RETURN;
    END

    IF @WhatIf = 1
    BEGIN
        SELECT  d.id AS شناسه_سطر, h.FNUMB AS شماره_فرمول, d.CODE AS کد_ماده,
                d.MEGH AS مقدار_فعلي, d.MEGHk AS مقدار_کوچک_فعلي,
                CASE @Action WHEN 'zero' THEN N'مقدار صفر مي‌شود'
                             ELSE N'کل سطر فرمول حذف مي‌شود' END AS عمليات
        FROM    dbo.DTL_MANF d
        JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB
        WHERE   d.id = @DtlId;
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @Fnumb INT;
    SELECT @Fnumb = FNUMB FROM dbo.DTL_MANF WHERE id = @DtlId;

    IF @Action = 'zero'
        UPDATE dbo.DTL_MANF
           SET MEGH = 0, MEGHk = 0, MABLK = 0, SMABL = 0
         WHERE id = @DtlId;
    ELSE
        DELETE dbo.DTL_MANF WHERE id = @DtlId;

    UPDATE dbo.CC_Exception
       SET IsResolved = 1, ResolvedBy = @UserName, ResolvedAtUtc = SYSUTCDATETIME(),
           ResolutionNote = CASE @Action WHEN 'zero' THEN N'مقدار سطر فرمول صفر شد'
                                          ELSE N'سطر فرمول حذف شد' END
     WHERE ExceptionId = @ExceptionId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S00', 1,
            CONCAT(N'اصلاح فرمول با مقدار منفي — فرمول ', @Fnumb, N', کالا ', @Code,
                   CASE @Action WHEN 'zero' THEN N' — مقدار صفر شد' ELSE N' — سطر حذف شد' END,
                   N' توسط ', @UserName));

    IF @RunId IS NOT NULL
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT 1 AS تغيير_يافت, N'انجام شد' AS وضعيت;
END
GO


PRINT N'CHK-04 و اصلاح خودکار آماده شد.';

/* نمونه:
   EXEC dbo.CC_sp_Chk04_MissingFormula  @Month=5, @DT1=14050501, @DT2=14050531;
   EXEC dbo.CC_sp_Fix_MissingFormula    @Month=5, @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_Fix_MissingFormula    @Month=5, @DT1=14050501, @DT2=14050531, @WhatIf=0,
                                        @UserName=N'مدير مالي';
*/
GO
