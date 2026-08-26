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
    @WhatIf         BIT = 1
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

    DECLARE @FromFNUMB INT, @ToFNUMB INT, @FromProdQty FLOAT, @ToProdQty FLOAT;

    SELECT TOP 1 @FromFNUMB = hm.FNUMB
    FROM    dbo.HEAD_MANF hm
    WHERE   TRY_CAST(hm.CODE AS BIGINT) = @FromParentCode AND hm.GHEYMAT = @Month;

    SELECT TOP 1 @ToFNUMB = hm.FNUMB
    FROM    dbo.HEAD_MANF hm
    WHERE   TRY_CAST(hm.CODE AS BIGINT) = @ToParentCode AND hm.GHEYMAT = @Month;

    IF @FromFNUMB IS NULL OR @ToFNUMB IS NULL
    BEGIN
        RAISERROR(N'فرمول مبدأ یا مقصد برای این ماه ثبت نشده است.', 16, 1);
        RETURN;
    END

    SELECT @FromProdQty = ProdQty FROM #Prod WHERE FNUMB = @FromFNUMB;
    SELECT @ToProdQty   = ProdQty FROM #Prod WHERE FNUMB = @ToFNUMB;

    IF ISNULL(@FromProdQty, 0) <= 0 OR ISNULL(@ToProdQty, 0) <= 0
    BEGIN
        RAISERROR(N'یکی از دو فرمول در این بازه سند تولید (رسید تولید) ندارد؛ تبدیل مقدار به ازای واحد ممکن نیست.', 16, 1);
        RETURN;
    END

    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    SELECT  d.FNUMB, d.CODE, d.MEGHk, ISNULL(d.SMABL, 0) AS Rate,
            CASE WHEN d.FNUMB = @FromFNUMB THEN -@Qty / @FromProdQty
                                            ELSE  @Qty / @ToProdQty END AS Delta,
            CASE WHEN d.FNUMB = @FromFNUMB THEN @FromParentCode ELSE @ToParentCode END AS ParentCode,
            CASE WHEN d.FNUMB = @FromFNUMB THEN @FromProdQty ELSE @ToProdQty END AS ProdQty
    INTO    #Rows
    FROM    dbo.DTL_MANF d
    WHERE   d.FNUMB IN (@FromFNUMB, @ToFNUMB)
      AND   TRY_CAST(d.CODE AS BIGINT) = @MaterialCode;

    IF (SELECT COUNT(*) FROM #Rows) < 2
    BEGIN
        RAISERROR(N'این ماده در هر دو فرمول مصرف نشده — ابتدا باید ردیف ماده در هر دو فرمول موجود باشد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Rows GROUP BY FNUMB HAVING COUNT(*) > 1)
    BEGIN
        RAISERROR(N'این ماده در یکی از فرمول‌ها بیش از یک ردیف (چند انبار) دارد؛ این حالت با این ابزار پشتیبانی نمی‌شود — دستی اصلاح کنید.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Rows WHERE MEGHk + Delta < 0)
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

        UPDATE  d
           SET  d.MEGHk = d.MEGHk + r.Delta,
                d.MABLK = ROUND(r.Rate * (d.MEGHk + r.Delta), 0)
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

    SELECT  r.ParentCode                    AS ParentCode,
            s.NAME                          AS ParentName,
            r.MEGHk                         AS MEGHkBefore,
            r.MEGHk + r.Delta               AS MEGHkAfter,
            r.Rate                          AS Rate,
            r.Rate * r.MEGHk                AS CostPerUnitBefore,
            r.Rate * (r.MEGHk + r.Delta)    AS CostPerUnitAfter,
            r.ProdQty                       AS ProdQty
    FROM    #Rows r
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = r.ParentCode
    ORDER BY r.ParentCode;
END
GO

PRINT N'رويه CC_sp_RebalanceMaterialQty ايجاد شد.';
GO
