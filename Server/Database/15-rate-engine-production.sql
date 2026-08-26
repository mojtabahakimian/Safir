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

    DECLARE @TafDastmozd BIGINT = 99999999;   -- دستمزد (و روي اين پايگاه‌داده: سربار هم همين‌جا)
    DECLARE @TafSarbar   BIGINT = 99999998;   -- سربار، فقط وقتي نصب از دستمزد جدايش کرده باشد
    DECLARE @UnitId INT, @SplitMode TINYINT;

    -- تشخيص واحد از روي دپارتمان کنار گذاشته شد: دپارتمان را اپراتور دستي
    -- روي برگه مي‌زند و اشتباه تايپي رايج است. ملاک مطمئن، انباري است که
    -- محصول توليدشده وارد آن مي‌شود (CC_UnitAnbar.AnbarRole = 3، «محصول»)
    -- — همان چيزي که در تنظيمات واحدها از قبل تعريف شده و کاربر تأييد
    -- کرد بايد ملاک باشد (نه Depatman). CHK-16 (S00) از قبل هر انباري که
    -- برگه توليد دارد ولي به هيچ واحدي وصل نيست را هشدار مي‌دهد.
    --
    -- ريسک مشابهِ حالت قبلي (Depatman=NULL تکراري) اينجا اين است: اگر يک
    -- انبارِ «محصول» به بيش از يک واحد فعال وصل باشد، هر دو دقيقاً همان
    -- برگه‌ها را پردازش مي‌کنند و چون اين حلقه IMBIBE_MANF/IMBIBE_SAR را
    -- مستقيماً در HEAD_MANF ويرايش مي‌کند، واحد دوم رويِ مقدارِ از‌قبل‌
    -- تعديل‌شده‌ي واحد اول دوباره ضريب مي‌زند — فرمول‌ها خراب مي‌شوند.
    IF EXISTS (
        SELECT ua.Anbar
        FROM   dbo.CC_UnitAnbar ua
        JOIN   dbo.CC_Unit      u  ON u.UnitId = ua.UnitId AND u.IsActive = 1
        WHERE  ua.AnbarRole = 3
        GROUP  BY ua.Anbar
        HAVING COUNT(DISTINCT ua.UnitId) > 1
    )
    BEGIN
        RAISERROR(N'يک انبار محصول (نقش «محصول») به بيش از يک واحد توليدي فعال وصل است؛ اين باعث پردازش دوباره‌ي همان برگه‌ها و خراب شدن فرمول‌ها مي‌شود. نگاشت انبار⇄واحد را در تنظیمات اصلاح کنيد.', 16, 1);
        RETURN;
    END

    DELETE dbo.CC_ConversionCost WHERE RunId = @RunId;
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND StepCode = 'S10' AND RuleCode = 'CHK-08';

    DECLARE cUnit CURSOR LOCAL FAST_FORWARD FOR
        SELECT UnitId, SplitMode
        FROM   dbo.CC_Unit WHERE IsActive = 1 ORDER BY SeqNo;

    OPEN cUnit;
    FETCH NEXT FROM cUnit INTO @UnitId, @SplitMode;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        ---- ۱) جذب‌شده از برگه‌هاي توليد اين واحد (بر اساس انبار محصول)
        DECLARE @absWage FLOAT, @absOh FLOAT;

        SELECT  @absWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @absOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND   pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                              WHERE UnitId = @UnitId AND AnbarRole = 3);

        DECLARE @absTotal FLOAT = @absWage + @absOh;

        ---- ۲) کنترل متقابل با حساب ۷۵۱، به تفکيک واحد و به تفکيک دستمزد/سربار
        -- HES_M روي اين رديف‌ها کدِ خودِ کالاي توليدشده است (نه يک معينِ
        -- عمومي) — کاربر تأييد کرد و مستقيماً تست شد: تمام HES_M هاي اين
        -- تفصيلي دقيقاً با STUF_DEF.CODE مطابقت دارند. پس مي‌شود دقيقاً
        -- همان مجموعه کدهايي را که اين واحد در همين بازه توليد کرده
        -- (زيرکوئري پايين، عيناً منطق جذب‌شده در بالا) فيلتر کرد و مانده
        -- ۷۵۱ را per-واحد گرفت، نه فقط جمع کل شرکت.
        --
        -- ⚠️ @TafSarbar (سربار، ۹۹۹۹۹۹۹۸) روي اين پايگاه‌داده استفاده
        -- نمي‌شود — همه‌ي دستمزد و سربار زير همان @TafDastmozd (۹۹۹۹۹۹۹۹)
        -- ثبت مي‌شوند (کاربر تأييد کرد). ولي روي نصب‌هاي ديگر ممکن است
        -- اين دو را جدا کنند؛ اگر اينجا فقط @TafDastmozd را چک مي‌کرديم،
        -- روي چنان پايگاه‌داده‌اي سهمِ سربار از مانده ۷۵۱ اصلاً ديده
        -- نمي‌شد و کنترل CHK-08 دقيقاً به‌اندازه‌ي سربار غلط مي‌شد. پس هر
        -- دو تفصيلي را جدا جمع مي‌زنيم؛ هر کدام که در اين پايگاه‌داده
        -- خالي باشد صفر مي‌ماند و به کنترل کل آسيبي نمي‌زند.
        DECLARE @absWipWage FLOAT, @absWipOh FLOAT, @absWip FLOAT;

        SELECT  @absWipWage = ISNULL(SUM(CASE WHEN d.HES_T = @TafDastmozd THEN d.BES - d.BED ELSE 0 END), 0),
                @absWipOh   = ISNULL(SUM(CASE WHEN d.HES_T = @TafSarbar   THEN d.BES - d.BED ELSE 0 END), 0)
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
        WHERE   d.HES_K = 751 AND d.HES_T IN (@TafDastmozd, @TafSarbar)
          AND   hd.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IN (
                    SELECT DISTINCT TRY_CAST(pl.CODE AS BIGINT)
                    FROM   dbo.HEAD_LST h
                    JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                    WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                      AND  pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                                        WHERE UnitId = @UnitId AND AnbarRole = 3));

        SET @absWip = @absWipWage + @absWipOh;

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
            (@RunId, @UnitId, 1, @absWage, @absWipWage, @actWage, @kWage, NULL),
            (@RunId, @UnitId, 2, @absOh,   @absWipOh,   @actOh,   @kOh,   NULL);

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
                          AND  pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                                            WHERE UnitId = @UnitId AND AnbarRole = 3));

            INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
            VALUES (@RunId, 'S10', 1,
                    CONCAT(N'واحد ', @UnitId, N': ضريب تعديل ',
                           FORMAT(@kWage, 'N5'), N' روي ', @@ROWCOUNT, N' فرمول'));

            COMMIT;
        END

        FETCH NEXT FROM cUnit INTO @UnitId, @SplitMode;
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
   ⚠️ يک کالا مي‌تواند در همان ماه بيش از يک فرمول فعال داشته باشد (مثلاً
   روزهاي مختلف با ترکيب مواد متفاوت توليد شده باشد) — طبق تأييد صاحب
   پروژه اين طبيعي است، نه خطاي داده. نسخه‌ي قبلي فقط يک فرمول را با
   TOP 1 (آخرين DATE_ACTIV/FNUMB) براي محاسبه و انتشار انتخاب مي‌کرد؛
   بهاي «خودِ» کالا حالا ميانگين موزونِ بهاي همه‌ي فرمول‌هاي فعالش است،
   وزن‌دهي‌شده با مقدار واقعيِ توليدشده زيرِ هرکدام در بازه‌ي @DT1..@DT2
   (از HEAD_LST/INVO_LST TAG=9، N_KOL=FNUMB). اگر هيچ‌کدام توليد واقعي
   نداشتند (فرمول تعريف شده ولي هنوز مصرف نشده)، ميانگين ساده جايگزين
   وزن مي‌شود — دقيقاً همان قاعده‌اي که CHK-09 در S00 هم استفاده مي‌کند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S11_PropagateRates
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
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
        Code       BIGINT PRIMARY KEY,
        Llc        SMALLINT NOT NULL DEFAULT 0,
        HasFormula BIT      NOT NULL DEFAULT 0,
        Src        TINYINT  NOT NULL DEFAULT 1,
        Mat        FLOAT    NOT NULL DEFAULT 0,
        Wage       FLOAT    NOT NULL DEFAULT 0,
        Oh         FLOAT    NOT NULL DEFAULT 0
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

    /* ─── ۳ب) فرمول‌هاي هر کالا در اين ماه — ممکن است بيش از يکي باشد ───
       #F جايگزينِ ستون تکيِ #C.FNUMB قبلي است: هر رديف يک فرمول فعال است،
       با مقدار واقعيِ توليدشده زيرش (Qty) که وزنِ ميانگين‌گيري مي‌شود. */
    IF OBJECT_ID('tempdb..#F') IS NOT NULL DROP TABLE #F;

    CREATE TABLE #F (
        FNUMB INT    PRIMARY KEY,
        Code  BIGINT NOT NULL,
        Qty   FLOAT  NOT NULL DEFAULT 0,
        Wage  FLOAT  NOT NULL DEFAULT 0,
        Oh    FLOAT  NOT NULL DEFAULT 0,
        Mat   FLOAT  NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_F_Code ON #F(Code);

    INSERT #F (FNUMB, Code, Qty, Wage, Oh)
    SELECT  hm.FNUMB, CAST(hm.CODE AS BIGINT),
            ISNULL(p.Qty, 0), ISNULL(hm.IMBIBE_MANF, 0), ISNULL(hm.IMBIBE_SAR, 0)
    FROM    dbo.HEAD_MANF hm
    CROSS   APPLY (
                SELECT SUM(pl.MEGHk) AS Qty
                FROM   dbo.HEAD_LST h
                JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                  AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
            ) p
    WHERE   hm.GHEYMAT = @Month AND hm.CODE IS NOT NULL
      AND   EXISTS (SELECT 1 FROM #C c WHERE c.Code = CAST(hm.CODE AS BIGINT));

    UPDATE  c SET c.HasFormula = 1, c.Src = 2
    FROM    #C c
    WHERE   EXISTS (SELECT 1 FROM #F f WHERE f.Code = c.Code);

    /* ─── ۴) نرخ مواد خريدني: ميانگين وزني خروج از انبار ───
       عمداً روي کالاهاي بدون فرمول محدود نيست: نيمه‌ساخته‌اي که خودش هم
       اين ماه از انبار حواله خورده (مثل هر ماده‌ي اوليه‌ي ديگر) بايد
       دقيقاً همان ميانگين واقعيِ انبارش را به‌عنوان نرخِ «خودش وقتي در
       فرمولِ کالاي ديگري مصرف مي‌شود» بگيرد — نه نرخِ تازه‌محاسبه‌شده‌ي
       زنجيره‌ي BOM. کاربر تأييد کرد اين دقيقاً همان چيزي است که مغايرت
       حساب ۷۷۱ را ايجاد مي‌کرد: MaterialIssueRebuildService مبلغ واقعيِ
       حواله (بر مبناي AVRAGE واقعيِ انبار در لحظه‌ي هر تراکنش) را با
       SMABL مقايسه مي‌کند؛ اگر SMABL از ميانگين همان انبار بيايد، دو طرف
       از يک منبع مشتق مي‌شوند و طبيعتاً هم‌خوان مي‌مانند — برخلاف نرخِ
       لحظه‌ايِ بازسازي‌شده‌ي BOM که فقط آخرين قيمتِ اجزا را منعکس مي‌کند،
       نه ميانگينِ واقعيِ کل ماه. نتيجه در ۵-ج پايين‌تر override نمي‌شود
       (شرط Src<>1 آنجا). */
    UPDATE  c
       SET  c.Mat = z.fi, c.Src = 1
    FROM    #C c
    JOIN   (SELECT k.code, SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
            FROM   dbo.KALAS k
            WHERE  k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
            GROUP BY k.code) z ON z.code = c.Code
    WHERE   z.fi IS NOT NULL;

    ---- بدون گردش در ماه: آخرين نرخ ميانگين ثبت‌شده
    UPDATE  c
       SET  c.Mat = lp.AVRAGE
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 i.AVRAGE
                   FROM   dbo.INVO_LST i
                   JOIN   dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                   WHERE  CAST(i.CODE AS BIGINT) = c.Code AND i.AVRAGE > 0
                   ORDER BY h.DATE_N DESC, i.NUMBER DESC) lp
    WHERE   c.HasFormula = 0 AND c.Mat = 0;

    UPDATE #C SET Src = 3 WHERE HasFormula = 0 AND Mat = 0;

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

        ---- ۵-ب) بهاي هر فرمولِ اين سطح = مجموع اجزاي همان فرمول
        UPDATE  f
           SET  f.Mat = ISNULL(a.MatCost, 0)
        FROM    #F f
        JOIN    #C p ON p.Code = f.Code AND p.Llc = @lvl
        CROSS   APPLY (SELECT SUM(d.MEGHk * (ch.Mat + ch.Wage + ch.Oh)) AS MatCost
                       FROM   dbo.DTL_MANF d
                       JOIN   #C ch ON ch.Code = CAST(d.CODE AS BIGINT)
                       WHERE  d.FNUMB = f.FNUMB) a;

        ---- ۵-ج) بهاي «خودِ» کالا = ميانگين موزونِ همه‌ي فرمول‌هايش با
        ---- مقدار واقعيِ توليدشده (Qty)؛ بدون هيچ توليدي، ميانگين ساده.
        ---- وقتي Mat از گام ۴ (ميانگين واقعيِ انبار) تعيين شده، Wage/Oh
        ---- را هم از BOM نمي‌گيرد و صفر مي‌ماند — نه فقط Mat را دست
        ---- نمي‌زند: نرخ انباري از MABL_K واقعيِ ثبت‌شده مي‌آيد که همان
        ---- لحظه‌ي توليد (TAG=9) از قبل دستمزد/سربار را داخلش دارد (نگاه
        ---- کنید AverageRateRebuildService, case 9: produced = IMBIBE_MANF
        ---- + IMBIBE_SAR + SumOfMABLK). اگر اينجا دوباره w.Wage/w.Oh را
        ---- روي همان کد جمع بزنيم، دستمزد/سربار دوبار حساب مي‌شود — دقيقاً
        ---- همان چيزي که مغايرت ۷۷۱ را نصفه رفع کرده بود (Mat درست شد ولي
        ---- Wage هنوز از BOM اضافه مي‌آمد).
        UPDATE  c
           SET  c.Wage = CASE WHEN c.Mat <> 0 THEN 0 ELSE w.Wage END,
                c.Oh   = CASE WHEN c.Mat <> 0 THEN 0 ELSE w.Oh   END,
                c.Mat  = CASE WHEN c.Mat <> 0 THEN c.Mat ELSE w.Mat END
        FROM    #C c
        CROSS   APPLY (
                    SELECT
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Mat  * f.Qty) / SUM(f.Qty) ELSE AVG(f.Mat)  END AS Mat,
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Wage * f.Qty) / SUM(f.Qty) ELSE AVG(f.Wage) END AS Wage,
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Oh   * f.Qty) / SUM(f.Qty) ELSE AVG(f.Oh)   END AS Oh
                    FROM #F f WHERE f.Code = c.Code
                ) w
        WHERE   c.Llc = @lvl AND c.HasFormula = 1;

        SET @lvl -= 1;
    END

    /* ─── ۶) ثبت نتيجه در CC_ItemCost ───
       FNUMB اينجا فقط براي نمايش در گزارش است؛ وقتي کالا چند فرمول همان
       ماه دارد، فرمولي که بيشترين مقدار واقعي زيرش توليد شده به‌عنوان
       نماينده انتخاب مي‌شود (بهاي واقعي همچنان ميانگين موزونِ همه است،
       نه فقط همين يکي). */
    DELETE dbo.CC_ItemCost WHERE RunId = @RunId;

    INSERT dbo.CC_ItemCost
        (RunId, PeriodMonth, Code, LowLevelCode, SourceKind, FNUMB,
         MaterialCost, WageCost, OverheadCost)
    SELECT  @RunId, @Month, c.Code, c.Llc, c.Src, rep.FNUMB, c.Mat, c.Wage, c.Oh
    FROM    #C c
    OUTER   APPLY (SELECT TOP 1 f.FNUMB FROM #F f WHERE f.Code = c.Code
                    ORDER BY f.Qty DESC, f.FNUMB DESC) rep;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S11', 1,
            CONCAT(N'انتشار نرخ: ', @totalChanges, N' نرخ به‌روز شد'),
            (SELECT MAX(Llc) AS maxLevel, COUNT(*) AS items,
                    SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END) AS noSource
             FROM #C FOR JSON PATH));

    /* ─── ۷) آزمون سلامت: CHK-09 بايد صفر شود ───
       Khod اينجا از خودِ #C خوانده مي‌شود (يعني همان بهاي موزوني که تازه
       محاسبه و منتشر شد)، نه دوباره از HEAD_MANF/DTL_MANF به تفکيک FNUMB —
       وگرنه هر فرمولِ «غيرمنتخب» يک کالاي چندفرمولي هميشه کاذب فلگ مي‌شد. */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-09';

    ;WITH DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code, AVG(d.SMABL) AS Nerkh
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S11', 'CHK-09', 14, 2, c.Code, (c.Mat + c.Wage + c.Oh) - v.Nerkh,
            N'نرخ پس از اجراي موتور هنوز منتشر نشده — نياز به بررسي'
    FROM    #C c
    JOIN    DarValed v ON v.Code = c.Code
    WHERE   c.HasFormula = 1
      AND   ABS((c.Mat + c.Wage + c.Oh) - v.Nerkh) / NULLIF((c.Mat + c.Wage + c.Oh), 0) > 0.001;

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
   EXEC dbo.CC_sp_S11_PropagateRates    @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
*/
GO
