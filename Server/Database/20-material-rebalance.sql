/* ═══════════════════════════════════════════════════════════════════
   جابه‌جایی مقدار مصرف ماده بین دو فرمول (اصلاح روی مواد، نه هزینه تبدیل)

   کاربرد: وقتی یک کالای فروش‌رفته (مثلاً پنیر اولیه) زیان‌ده است چون
   مصرف یک ماده‌ی کلیدی (مثلاً شیر اسکیم) در فرمولش بالاست، به‌جای
   دست‌کاری نرخ جذب دستمزد (IMBIBE_MANF در S12b که برای این حالت لور
   درستی نیست)، مقدار فیزیکی مصرف آن ماده از فرمول کالای فروش‌رفته کم
   و به فرمول کالای هم‌خانواده‌ای که در تولید مصرف می‌شود (نه فروخته
   می‌شود) اضافه می‌شود — جمع کل مصرف فیزیکی آن ماده در ماه ثابت
   می‌ماند، پس S08/S09 (انحراف مصرف) چیزی نمی‌بیند.

   دقیقاً همان الگوی محاسبه‌ی «مقدار تولید هر فرمول» را که
   CC_sp_S09_ApplyDecisions استفاده می‌کند به کار می‌بریم، تا مقدار
   فیزیکیِ ورودی کاربر (کیلو/لیتر ماده) به دلتای MEGHk هر فرمول تبدیل
   شود؛ چون MEGHk (نه MEGH) همان فیلدی است که S11 برای محاسبه‌ی بهای
   تمام‌شده واقعاً می‌خواند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_RebalanceMaterialQty
    @RunId          INT,
    @Month          TINYINT,
    @DT1            BIGINT,
    @DT2            BIGINT,
    @MaterialCode   BIGINT,
    @FromParentCode BIGINT,
    @ToParentCode   BIGINT,
    @Qty            FLOAT,      -- مقدار فیزیکی ماده که جابه‌جا می‌شود (واحد کاردکس ماده)
    @WhatIf         BIT = 1,
    -- فهرست FNUMB فرمول‌هایی که کاربر تیک زده (با کاما). NULL یعنی همه‌ی
    -- فرمول‌های هر دو کالا که این ماده را مصرف می‌کنند و سند تولید دارند.
    @SelectedFNUMBs NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Qty IS NULL OR @Qty <= 0
    BEGIN
        RAISERROR(N'مقدار جابه‌جایی باید عددی مثبت باشد.', 16, 1);
        RETURN;
    END

    IF @FromParentCode = @ToParentCode
    BEGIN
        RAISERROR(N'فرمول مبدأ و مقصد نمی‌توانند یکی باشند.', 16, 1);
        RETURN;
    END

    ---- مقدار توليد هر فرمول در اين ماه — عيناً منطق CC_sp_S09_ApplyDecisions
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB,
            SUM(pl.MEGHK)             AS ProdQty
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
    GROUP BY TRY_CAST(pl.N_KOL AS INT)
    HAVING  SUM(pl.MEGHK) > 0;

    ---- فرمول‌های دو طرف
    -- ⚠ یک کالا می‌تواند در یک ماه بیش از یک فرمول داشته باشد. نمونه‌ی واقعی:
    -- کد ۳۷۳ «شیر اسکیم» در اردیبهشت ۱۴۰۵ دو فرمول دارد (FNUMB ۲۳۷۳ و
    -- ۸۲۶۰۳۱۴۸۲). نسخه‌ی قبلی اینجا «SELECT TOP 1 ... » بدون ORDER BY داشت،
    -- یعنی خودسرانه و غیرقطعی یکی را برمی‌داشت و کسر می‌توانست از فرمول
    -- اشتباه برداشته شود — بدون اینکه کاربر بفهمد کدام انتخاب شده.
    --
    -- منطق درست: همه‌ی فرمول‌های آن کالا با هم و «به یک میزان» تغییر کنند،
    -- یعنی دلتای MEGHk یکسان روی هرکدام. چون
    --     جمع مقدار جابه‌جاشده = دلتا × Σ(مقدار تولید) = @Qty
    -- کل مصرف فیزیکی ماده در ماه ثابت می‌ماند و S08/S09 (انحراف مصرف)
    -- چیزی نمی‌بیند — همان تضمینی که این ابزار از ابتدا می‌داد، ولی حالا
    -- برای حالت چندفرمولی هم برقرار است.
    --
    -- فرمولی که در این بازه سند تولید ندارد کنار گذاشته می‌شود، نه اینکه
    -- کل عملیات را رد کند: بدون تولید، تغییر MEGHk آن هیچ مصرف فیزیکی‌ای
    -- را در این ماه جابه‌جا نمی‌کند.
    IF OBJECT_ID('tempdb..#Sel') IS NOT NULL DROP TABLE #Sel;

    SELECT  d.FNUMB,
            d.CODE,
            TRY_CAST(hm.CODE AS BIGINT) AS ParentCode,
            CASE WHEN TRY_CAST(hm.CODE AS BIGINT) = @FromParentCode
                 THEN -1 ELSE 1 END     AS Dir,
            d.MEGH,
            d.MEGHk,
            ISNULL(d.PERT, 0)           AS Pert,
            -- نسبتِ واحدِ ردیف به واحد اصلیِ کالا. مرجعش VAHEDS است — دقیقاً
            -- همان چیزی که فرم فرمولِ نرم‌افزار قدیمی می‌خواند:
            --     Me.MEGHk = Me.MEGH * VAHEDS.NESBAT
            -- (نه VAH_SUB؛ آن دو در ۱۵ ردیف با هم اختلاف دارند.)
            vv.NESBAT                   AS UnitRatio,
            ISNULL(d.SMABL, 0)          AS Rate,
            p.ProdQty
    INTO    #Sel
    FROM    dbo.DTL_MANF d
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
    JOIN    #Prod p ON p.FNUMB = d.FNUMB
    LEFT    JOIN dbo.VAHEDS vv
            ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
           AND vv.VAHED = d.VAHED_K
    WHERE   TRY_CAST(d.CODE AS BIGINT) = @MaterialCode
      AND   TRY_CAST(hm.CODE AS BIGINT) IN (@FromParentCode, @ToParentCode)
      AND   p.ProdQty > 0
      AND   (@SelectedFNUMBs IS NULL
             OR d.FNUMB IN (SELECT TRY_CAST(value AS INT)
                            FROM   STRING_SPLIT(@SelectedFNUMBs, ',')
                            WHERE  TRY_CAST(value AS INT) IS NOT NULL));

    IF NOT EXISTS (SELECT 1 FROM #Sel WHERE Dir = -1)
       OR NOT EXISTS (SELECT 1 FROM #Sel WHERE Dir = 1)
    BEGIN
        RAISERROR(N'برای یکی از دو کالا هیچ فرمولی پیدا نشد که هم این ماده را مصرف کند و هم در این بازه سند تولید داشته باشد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Sel GROUP BY FNUMB HAVING COUNT(*) > 1)
    BEGIN
        RAISERROR(N'این ماده در یکی از فرمول‌ها بیش از یک ردیف (چند انبار) دارد؛ این حالت با این ابزار پشتیبانی نمی‌شود — دستی اصلاح کنید.', 16, 1);
        RETURN;
    END

    -- همان بررسی‌ای که فرم فرمولِ نرم‌افزار قدیمی هم دارد: بدون نسبتِ واحد
    -- نمی‌شود «مقدار» را از «مقدار کل» به دست آورد. سکوت کردن اینجا یعنی
    -- نوشتنِ یک عدد حدسی در فرمول.
    IF EXISTS (SELECT 1 FROM #Sel WHERE UnitRatio IS NULL OR UnitRatio = 0)
    BEGIN
        RAISERROR(N'واحد تعریف‌شده ناقص است و نسبت آن مشخص نگردیده — در بخش تعریف کالا آن را اصلاح کنید.', 16, 1);
        RETURN;
    END

    DECLARE @FromProdQty FLOAT, @ToProdQty FLOAT;

    -- جدا، نه با CASE داخل یک SUM: آن شکل برای هر سطرِ طرف مقابل یک NULL
    -- می‌سازد و SQL Server هشدار «Null value is eliminated by an aggregate»
    -- می‌دهد — بی‌ضرر ولی در لاگ‌ها گمراه‌کننده.
    SELECT @FromProdQty = SUM(ProdQty) FROM #Sel WHERE Dir = -1;
    SELECT @ToProdQty   = SUM(ProdQty) FROM #Sel WHERE Dir =  1;

    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- دلتای «مقدار» = دلتای «مقدار کل» ÷ نسبت واحد. @Qty در واحد کاردکس
    -- (واحد اصلی) است، پس مستقیماً روی MEGHk می‌نشیند و برای MEGH باید به
    -- واحد خودِ ردیف برگردانده شود — عکسِ همان MEGHk = MEGH × NESBAT.
    SELECT  s.FNUMB, s.CODE, s.MEGH, s.MEGHk, s.Pert, s.Rate,
            s.ParentCode, s.ProdQty, s.UnitRatio,
            d.Delta,
            d.Delta / s.UnitRatio AS MeghDelta
    INTO    #Rows
    FROM    #Sel s
    CROSS   APPLY (SELECT s.Dir * @Qty / CASE WHEN s.Dir = -1 THEN @FromProdQty
                                                              ELSE @ToProdQty END) AS d(Delta);

    IF EXISTS (SELECT 1 FROM #Rows WHERE MEGHk + Delta < 0 OR MEGH + MeghDelta < 0)
    BEGIN
        RAISERROR(N'این مقدار بیشتر از مصرف فعلیِ فرمول مبدأ است — عدد کوچک‌تری وارد کنید.', 16, 1);
        RETURN;
    END

    -- خروجی — چه پیش‌نمایش (WhatIf=1) چه بعد از اعمال (WhatIf=0)، از روی همین
    -- #Rows محاسبه می‌شود (مقادیر پیش از UPDATE در آن ثابت مانده)، تا کلاینت
    -- (Dapper → RebalancePreviewDto) یک شکل واحد ببیند. نام ستون‌ها انگلیسی‌اند
    -- چون قرار است روی یک DTO تایپ‌شده map شوند، نه فقط برای نمایش خام.
    IF @WhatIf = 0
    BEGIN
        BEGIN TRAN;

        -- ⚠ هر سه ستون با هم، طبق همان قراردادی که فرم فرمولِ نرم‌افزار
        -- قدیمی رعایت می‌کند:
        --     MEGHk = MEGH * VAHEDS.NESBAT
        --     MABLK = (PERT + MEGHk) * SMABL
        --
        -- نسخه‌ی قبلی فقط MEGHk را جابه‌جا می‌کرد (چون S11 برای بهای
        -- تمام‌شده همان را می‌خواند) و «مقدار» را دست‌نخورده می‌گذاشت، پس هر
        -- بار اجرا این دو ستون را از هم دورتر می‌کرد. MABLK هم PERT را جا
        -- انداخته بود؛ روی ردیف‌هایی با ضایعاتِ غیرصفر مبلغ را کم می‌داد.
        --
        -- سمت راستِ SET همیشه مقدارِ *پیش از* به‌روزرسانی را می‌خواند، پس
        -- هر سه از روی مقادیر قدیمی + دلتا حساب می‌شوند.
        UPDATE  d
           SET  d.MEGH  = d.MEGH  + r.MeghDelta,
                d.MEGHk = d.MEGHk + r.Delta,
                d.MABLK = ROUND((ISNULL(d.PERT, 0) + d.MEGHk + r.Delta) * r.Rate, 0)
        OUTPUT  @RunId, 'MANUAL', inserted.FNUMB,
                r.ParentCode, TRY_CAST(inserted.CODE AS BIGINT), 'MEGHk',
                deleted.MEGHk, inserted.MEGHk,
                N'جابه‌جایی مصرف ماده بین فرمول‌ها'
          INTO  dbo.CC_FormulaChange
                (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                 FieldName, OldValue, NewValue, Reason)
        FROM    dbo.DTL_MANF d
        JOIN    #Rows r ON r.FNUMB = d.FNUMB AND r.CODE = d.CODE;

        COMMIT;
    END

    -- FNUMB هم برمی‌گردد چون یک کالا می‌تواند چند فرمول داشته باشد و بدون آن
    -- دو سطرِ خروجی با نام یکسان تفکیک‌ناپذیر می‌شوند.
    SELECT  r.FNUMB                         AS FNUMB,
            r.ParentCode                    AS ParentCode,
            s.NAME                          AS ParentName,
            r.MEGH                          AS MEGHBefore,
            r.MEGH + r.MeghDelta            AS MEGHAfter,
            r.MEGHk                         AS MEGHkBefore,
            r.MEGHk + r.Delta               AS MEGHkAfter,
            r.Rate                          AS Rate,
            r.Rate * r.MEGHk                AS CostPerUnitBefore,
            r.Rate * (r.MEGHk + r.Delta)    AS CostPerUnitAfter,
            r.ProdQty                       AS ProdQty
    FROM    #Rows r
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = r.ParentCode
    ORDER BY r.ParentCode, r.FNUMB;
END
GO

PRINT N'رويه CC_sp_RebalanceMaterialQty ايجاد شد.';
GO
