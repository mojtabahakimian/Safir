/* ═══════════════════════════════════════════════════════════════════
   مرحله ۴ — موتور نرخ، نسخه تولیدی

   تفاوت با نسخه آزمون بازگشتی (فایل 03):
     ۱) در DTL_MANF و HEAD_MANF می‌نویسد، نه فقط در CC_ItemCost
     ۲) هر تغییر در CC_FormulaChange ثبت می‌شود
     ۳) @RunId می‌گیرد و به سابقه اجرا وصل است
     ۴) S10 (تراز هزینه تبدیل) هم اینجاست

   ترتیب اجرا: S10 سپس S11
   چون ضریب تعدیل مستقل از نرخ مواد است، یک بار محاسبه کافی است
   و دیگر نیازی به قرار گرفتن داخل حلقه ندارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S11 که در CC_ItemCost (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S10 — تراز هزینه تبدیل به تفکیک واحد تولیدی

   جذب‌شده = Σ (مقدار تولید × نرخ جذب فرمول)
   واقعی   = Σ (مانده سرفصل × ضریب سهم)   طبق CC_UnitAcc
   ضریب    = واقعی ÷ جذب‌شده

   کنترل متقابل: به شرط صفر بودن کار در جریان، جذب باید با
   گردش بستانکار حساب ۷۵۱ با تفصیلی 99999999 برابر باشد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S10_BalanceConversion
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TafDastmozd BIGINT = 99999999;
    DECLARE @UnitId INT, @Dep INT, @SplitMode TINYINT;

    -- Depatman = NULL يعني «همهٔ دپارتمان‌ها» — اگر بيش از يک واحد فعال اين
    -- حالت را داشته باشند، هر دو دقيقاً همان برگه‌هاي توليد را پردازش
    -- مي‌کنند. چون اين حلقه IMBIBE_MANF/IMBIBE_SAR را در HEAD_MANF مستقيماً
    -- ويرايش مي‌کند، واحد دومي که در همان اجرا پردازش مي‌شود ديگر مقدار
    -- اصلي فرمول را نمي‌بيند بلکه مقدارِ از قبل تعديل‌شدهٔ واحد اول را
    -- مي‌خواند و رويش دوباره ضريب مي‌زند — نتيجه فرمول را خراب مي‌کند، نه
    -- فقط عدد کنترلي را. مقدار پيش‌فرض داده اوليه (11-seed-data.sql) دقيقاً
    -- همين ترکيب را دارد؛ تا وقتي نصب‌کننده Depatman هر واحد را با دپارتمان
    -- واقعي‌اش عوض نکند، اجراي واقعي همين‌جا فرمول‌ها را خراب مي‌کرد.
    IF (SELECT COUNT(*) FROM dbo.CC_Unit WHERE IsActive = 1 AND Depatman IS NULL) > 1
    BEGIN
        RAISERROR(N'بيش از يک واحد توليدي فعال بدون دپارتمان مشخص (همه‌شمول) وجود دارد؛ اين باعث پردازش دوباره‌ي همان برگه‌ها و خراب شدن فرمول‌ها مي‌شود. دپارتمان هر واحد را در تنظیمات مشخص کنيد.', 16, 1);
        RETURN;
    END

    DELETE dbo.CC_ConversionCost WHERE RunId = @RunId;

    DECLARE cUnit CURSOR LOCAL FAST_FORWARD FOR
        SELECT UnitId, Depatman, SplitMode
        FROM   dbo.CC_Unit WHERE IsActive = 1 ORDER BY SeqNo;

    OPEN cUnit;
    FETCH NEXT FROM cUnit INTO @UnitId, @Dep, @SplitMode;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        ---- ۱) جذب‌شده از برگه‌هاي توليد اين واحد
        DECLARE @absWage FLOAT, @absOh FLOAT;

        SELECT  @absWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @absOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND  (@Dep IS NULL OR h.DEPATMAN = @Dep);

        DECLARE @absTotal FLOAT = @absWage + @absOh;

        ---- ۲) کنترل متقابل با حساب ۷۵۱ (فقط تفصيلي دستمزد)
        DECLARE @absWip FLOAT;

        SELECT  @absWip = ISNULL(SUM(d.BES) - SUM(d.BED), 0)
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
        WHERE   d.HES_K = 751 AND d.HES_T = @TafDastmozd
          AND   hd.DATE_S BETWEEN @DT1 AND @DT2;

        ---- ۳) واقعي از تراز، طبق نگاشت قابل ويرايش کاربر
        DECLARE @actWage FLOAT, @actOh FLOAT;

        -- CROSS APPLY نه JOIN روي جمعِ از‌قبل‌گروه‌بندی‌شده، چون هر سطر
        -- CC_UnitAcc ممکن است سطح معین/تفصیلی متفاوتی مشخص کرده باشد؛
        -- خالی‌بودن هرکدام یعنی «همهٔ آن سطح» (نگاشت گسترده‌تر، مثل قبل).
        SELECT  @actWage = ISNULL(SUM(CASE WHEN m.CostKind = 1
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0),
                @actOh   = ISNULL(SUM(CASE WHEN m.CostKind = 2
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0)
        FROM    dbo.CC_UnitAcc m
        CROSS   APPLY (
                    SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                    FROM   dbo.DEED_DTL d
                    JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                    WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                      AND  d.HES_K = m.HesKol
                      AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                      AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
                ) t
        WHERE   m.IsActive = 1 AND m.UnitId = @UnitId;

        DECLARE @actTotal FLOAT = @actWage + @actOh;

        ---- ۴) ضريب تعديل
        DECLARE @kWage FLOAT = 1, @kOh FLOAT = 1;

        IF @absTotal <> 0
        BEGIN
            IF @SplitMode = 1                    -- يک ضريب براي کل هزينه تبديل
            BEGIN
                DECLARE @k FLOAT = @actTotal / @absTotal;
                SET @kWage = @k;
                SET @kOh   = @k;
            END
            ELSE                                 -- دو ضريب مجزا
            BEGIN
                SET @kWage = CASE WHEN @absWage <> 0 THEN @actWage / @absWage ELSE 1 END;
                SET @kOh   = CASE WHEN @absOh   <> 0 THEN @actOh   / @absOh   ELSE 1 END;
            END
        END

        ---- ۵) ثبت نتيجه
        INSERT dbo.CC_ConversionCost
            (RunId, UnitId, CostKind, AbsorbedAmount, AbsorbedFromWip,
             ActualAmount, AdjustFactor, ActualDetailJson)
        VALUES
            (@RunId, @UnitId, 0, @absTotal, @absWip, @actTotal,
             CASE WHEN @absTotal <> 0 THEN @actTotal / @absTotal ELSE 1 END,
             (SELECT m.HesKol, m.HesMoin, m.HesTafsili, m.CostKind, m.Ratio
              FROM   dbo.CC_UnitAcc m
              WHERE  m.UnitId = @UnitId AND m.IsActive = 1
              FOR JSON PATH)),
            (@RunId, @UnitId, 1, @absWage, NULL, @actWage, @kWage, NULL),
            (@RunId, @UnitId, 2, @absOh,   NULL, @actOh,   @kOh,   NULL);

        ---- ۶) هشدار اختلاف کنترلي
        IF ABS(@absWip - @absTotal) > 10000000
            INSERT dbo.CC_Exception
                (RunId, StepCode, RuleCode, ExType, Severity, Amount, Description)
            VALUES (@RunId, 'S10', 'CHK-08', 10, 1, @absWip - @absTotal,
                    CONCAT(N'اختلاف جذب: برگه‌هاي توليد ', FORMAT(@absTotal, 'N0'),
                           N' در برابر حساب ۷۵۱ ', FORMAT(@absWip, 'N0')));

        ---- ۷) اعمال ضريب روي فرمول‌هاي کالاهاي توليدشده در اين واحد
        IF @WhatIf = 0 AND (@kWage <> 1 OR @kOh <> 1)
        BEGIN
            BEGIN TRAN;

            UPDATE  hm
               SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF * @kWage,
                    hm.IMBIBE_SAR  = hm.IMBIBE_SAR  * @kOh
            OUTPUT  @RunId, 'S10', inserted.FNUMB,
                    TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
                    deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
                    CONCAT(N'ضريب تعديل هزينه تبديل ', FORMAT(@kWage, 'N5'))
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.HEAD_MANF hm
            WHERE   hm.GHEYMAT = @Month
              AND   EXISTS (
                        SELECT 1
                        FROM   dbo.HEAD_LST h
                        JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                        WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                          AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
                          AND (@Dep IS NULL OR h.DEPATMAN = @Dep));

            INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
            VALUES (@RunId, 'S10', 1,
                    CONCAT(N'واحد ', @UnitId, N': ضريب تعديل ',
                           FORMAT(@kWage, 'N5'), N' روي ', @@ROWCOUNT, N' فرمول'));

            COMMIT;
        END

        FETCH NEXT FROM cUnit INTO @UnitId, @Dep, @SplitMode;
    END

    CLOSE cUnit;
    DEALLOCATE cUnit;

    ---- خلاصه
    SELECT  u.UnitName                          AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل هزينه تبديل'
                            WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END   AS نوع,
            c.AbsorbedAmount                    AS جذب_شده,
            c.AbsorbedFromWip                   AS کنترل_از_751,
            c.ActualAmount                      AS واقعي,
            c.AdjustFactor                      AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S11 — انتشار نرخ، نسخه تولیدی

   سطح‌بندی درخت فرمول، سپس محاسبه از عمیق‌ترین سطح به سطح صفر.
   نتیجه در DTL_MANF نوشته و در CC_FormulaChange ثبت می‌شود.

   یک پاس، قطعی، بدون تکرار.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S11_PropagateRates
    @RunId  INT,
    @Month  TINYINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── ۱) يال‌هاي درخت ─── */
    IF OBJECT_ID('tempdb..#Edge') IS NOT NULL DROP TABLE #Edge;

    SELECT  DISTINCT
            CAST(h.CODE AS BIGINT) AS Parent,
            CAST(d.CODE AS BIGINT) AS Child
    INTO    #Edge
    FROM    dbo.HEAD_MANF h
    JOIN    dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE   h.GHEYMAT = @Month
      AND   h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND   CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);

    CREATE CLUSTERED INDEX IX_Edge ON #Edge(Parent, Child);

    /* ─── ۲) تشخيص حلقه؛ بدون اين، محاسبه بي‌نهايت مي‌شود ─── */
    IF OBJECT_ID('tempdb..#Cycle') IS NOT NULL DROP TABLE #Cycle;
    CREATE TABLE #Cycle (Code BIGINT PRIMARY KEY);

    ;WITH Walk AS (
        SELECT  Parent AS Root, Child, 1 AS Lvl,
                CAST('/' + CAST(Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM    #Edge
        UNION ALL
        SELECT  w.Root, e.Child, w.Lvl + 1,
                CAST(w.Pt + CAST(e.Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM    Walk w JOIN #Edge e ON e.Parent = w.Child
        WHERE   w.Lvl < 20
          AND   w.Pt NOT LIKE '%/' + CAST(e.Child AS VARCHAR(20)) + '/%'
    )
    INSERT #Cycle(Code)
    SELECT DISTINCT Root FROM Walk WHERE Child = Root
    OPTION (MAXRECURSION 0);

    IF EXISTS (SELECT 1 FROM #Cycle)
    BEGIN
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
        SELECT @RunId, 'S11', 'CHK-06', 5, 2, Code,
               N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
        FROM   #Cycle;

        RAISERROR(N'حلقه در ساختار فرمول يافت شد؛ محاسبه متوقف شد.', 16, 1);
        RETURN;
    END

    /* ─── ۳) سطح‌بندي ─── */
    IF OBJECT_ID('tempdb..#C') IS NOT NULL DROP TABLE #C;

    CREATE TABLE #C (
        Code  BIGINT PRIMARY KEY,
        Llc   SMALLINT NOT NULL DEFAULT 0,
        FNUMB INT      NULL,
        Src   TINYINT  NOT NULL DEFAULT 1,
        Mat   FLOAT    NOT NULL DEFAULT 0,
        Wage  FLOAT    NOT NULL DEFAULT 0,
        Oh    FLOAT    NOT NULL DEFAULT 0
    );

    INSERT #C (Code)
    SELECT Parent FROM #Edge UNION SELECT Child FROM #Edge;

    DECLARE @changed INT = 1, @guard INT = 0;

    WHILE @changed > 0 AND @guard < 30
    BEGIN
        UPDATE  c
           SET  c.Llc = x.NewLlc
        FROM    #C c
        JOIN   (SELECT e.Child, MAX(p.Llc) + 1 AS NewLlc
                FROM   #Edge e JOIN #C p ON p.Code = e.Parent
                GROUP BY e.Child) x ON x.Child = c.Code
        WHERE   x.NewLlc > c.Llc;

        SET @changed = @@ROWCOUNT;
        SET @guard  += 1;
    END

    CREATE INDEX IX_C_Llc ON #C(Llc);

    ---- فرمول هر کالا
    UPDATE  c
       SET  c.FNUMB = f.FNUMB,
            c.Src   = 2
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 hm.FNUMB
                   FROM   dbo.HEAD_MANF hm
                   WHERE  CAST(hm.CODE AS BIGINT) = c.Code AND hm.GHEYMAT = @Month
                   ORDER BY hm.DATE_ACTIV DESC, hm.FNUMB DESC) f;

    /* ─── ۴) نرخ مواد خريدني: ميانگين وزني خروج از انبار ─── */
    UPDATE  c
       SET  c.Mat = z.fi, c.Src = 1
    FROM    #C c
    JOIN   (SELECT k.code, SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
            FROM   dbo.KALAS k
            WHERE  k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
            GROUP BY k.code) z ON z.code = c.Code
    WHERE   c.FNUMB IS NULL AND z.fi IS NOT NULL;

    ---- بدون گردش در ماه: آخرين نرخ ميانگين ثبت‌شده
    UPDATE  c
       SET  c.Mat = lp.AVRAGE
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 i.AVRAGE
                   FROM   dbo.INVO_LST i
                   JOIN   dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                   WHERE  CAST(i.CODE AS BIGINT) = c.Code AND i.AVRAGE > 0
                   ORDER BY h.DATE_N DESC, i.NUMBER DESC) lp
    WHERE   c.FNUMB IS NULL AND c.Mat = 0;

    UPDATE #C SET Src = 3 WHERE FNUMB IS NULL AND Mat = 0;

    /* ─── ۵) محاسبه از عميق‌ترين سطح به سطح صفر ───
       چون فرزندها هميشه سطح عميق‌تري از والد دارند، وقتي به والد
       مي‌رسيم بهاي همه اجزايش قبلاً محاسبه شده است. */

    DECLARE @lvl SMALLINT = (SELECT MAX(Llc) FROM #C);
    DECLARE @totalChanges INT = 0;

    WHILE @lvl >= 0
    BEGIN
        IF @WhatIf = 0
        BEGIN
            BEGIN TRAN;

            ---- ۵-الف) نرخ اجزا در فرمول والدهاي اين سطح
            UPDATE  d
               SET  d.SMABL = ch.Mat + ch.Wage + ch.Oh,
                    d.MABLK = ROUND((ch.Mat + ch.Wage + ch.Oh) * d.MEGHk, 0)
            OUTPUT  @RunId, 'S11', inserted.FNUMB,
                    NULL, TRY_CAST(inserted.CODE AS BIGINT), 'SMABL',
                    deleted.SMABL, inserted.SMABL,
                    N'انتشار نرخ — سطح‌بندي BOM'
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.DTL_MANF  d
            JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
            JOIN    #C p  ON p.Code  = CAST(hm.CODE AS BIGINT) AND p.Llc = @lvl
            JOIN    #C ch ON ch.Code = CAST(d.CODE  AS BIGINT)
            WHERE   ABS(ISNULL(d.SMABL, 0) - (ch.Mat + ch.Wage + ch.Oh)) > 0.5;

            SET @totalChanges += @@ROWCOUNT;

            COMMIT;
        END

        ---- ۵-ب) بهاي والد = مجموع اجزا + جذب دستمزد + جذب سربار
        UPDATE  c
           SET  c.Mat  = ISNULL(a.MatCost, 0),
                c.Wage = ISNULL(hm.IMBIBE_MANF, 0),
                c.Oh   = ISNULL(hm.IMBIBE_SAR , 0)
        FROM    #C c
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = c.FNUMB
        CROSS   APPLY (SELECT SUM(d.MEGHk * (ch.Mat + ch.Wage + ch.Oh)) AS MatCost
                       FROM   dbo.DTL_MANF d
                       JOIN   #C ch ON ch.Code = CAST(d.CODE AS BIGINT)
                       WHERE  d.FNUMB = c.FNUMB) a
        WHERE   c.Llc = @lvl AND c.FNUMB IS NOT NULL;

        SET @lvl -= 1;
    END

    /* ─── ۶) ثبت نتيجه در CC_ItemCost ─── */
    DELETE dbo.CC_ItemCost WHERE RunId = @RunId;

    INSERT dbo.CC_ItemCost
        (RunId, PeriodMonth, Code, LowLevelCode, SourceKind, FNUMB,
         MaterialCost, WageCost, OverheadCost)
    SELECT  @RunId, @Month, Code, Llc, Src, FNUMB, Mat, Wage, Oh
    FROM    #C;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S11', 1,
            CONCAT(N'انتشار نرخ: ', @totalChanges, N' نرخ به‌روز شد'),
            (SELECT MAX(Llc) AS maxLevel, COUNT(*) AS items,
                    SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END) AS noSource
             FROM #C FOR JSON PATH));

    /* ─── ۷) آزمون سلامت: CHK-09 بايد صفر شود ─── */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-09';

    ;WITH Khod AS (
        SELECT CAST(hm.CODE AS BIGINT) AS Code,
               SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                      + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha
        FROM   dbo.HEAD_MANF hm JOIN dbo.DTL_MANF d ON d.FNUMB = hm.FNUMB
        WHERE  hm.GHEYMAT = @Month
        GROUP BY CAST(hm.CODE AS BIGINT), hm.FNUMB
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code, AVG(d.SMABL) AS Nerkh
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S11', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            N'نرخ پس از اجراي موتور هنوز منتشر نشده — نياز به بررسي'
    FROM    Khod k JOIN DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > 0.001;

    /* ─── خلاصه ─── */
    SELECT  Llc                                          AS سطح,
            COUNT(*)                                     AS تعداد_کالا,
            SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END)     AS بدون_منبع_نرخ
    FROM    #C
    GROUP BY Llc ORDER BY Llc;

    SELECT  @totalChanges AS تعداد_نرخ_به‌روز_شده,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND RuleCode = 'CHK-09' AND IsResolved = 0)
                          AS نرخ_منتشر_نشده_باقيمانده;
END
GO


PRINT N'موتور نرخ توليدي (S10 و S11) ايجاد شد.';

/* نمونه:
   EXEC dbo.CC_sp_S10_BalanceConversion @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_S11_PropagateRates    @RunId=1, @Month=5, @WhatIf=1;
*/
GO
