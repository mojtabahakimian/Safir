/* ═══════════════════════════════════════════════════════════════════
   دو تغییر بر اساس درخواست کاربر

   ۱) CHK-04 حالا شماره برگه‌های تولید را هم می‌دهد، نه فقط کد کالا
   ۲) رویه اصلاح خودکار: فرمول همان ماه را به برگه‌ها نسبت می‌دهد

   قابل اجرای مکرر.
   ═══════════════════════════════════════════════════════════════════ */

-- این اسکریپت باید روی دیتابیس هدف شما اجرا شود (USE ثابت حذف شد؛
-- در پکیج اصلی این خط به دیتابیس مشتری YAZDSEPAR1405 اشاره داشت).

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

    SELECT  h.NUMBER            AS ProdNo,
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

    UPDATE  pl
       SET  pl.N_KOL = r.NewFnumb
    FROM    dbo.INVO_LST pl
    JOIN    #Rows r ON r.ProdNo = pl.NUMBER AND r.Radif = pl.RADIF
    WHERE   pl.TAG = 9;

    ---- ثبت در سابقه
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S00', 1,
            CONCAT(N'اصلاح خودکار فرمول برگه‌هاي توليد: ', @n, N' سطر توسط ', @UserName),
            (SELECT ProdNo, Code, OldFnumb, NewFnumb FROM #Rows FOR JSON PATH);

    ---- استثناها بسته مي‌شوند
    UPDATE  e
       SET  e.IsResolved     = 1,
            e.ResolvedBy     = @UserName,
            e.ResolvedAtUtc  = SYSUTCDATETIME(),
            e.ResolutionNote = N'اصلاح خودکار — فرمول ماه به برگه‌ها نسبت داده شد'
    FROM    dbo.CC_Exception e
    JOIN    #Target t ON t.Code = e.Code
    WHERE   e.RuleCode = 'CHK-04'
      AND   ISNULL(e.RunId,-1) = ISNULL(@RunId,-1);

    ---- خروج مواد بايد بازسازي شود، چون فرمول برگه عوض شد
    IF @RunId IS NOT NULL
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT @n AS تعداد_سطر_اصلاح_شده;
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
