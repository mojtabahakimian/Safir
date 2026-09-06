/* ═══════════════════════════════════════════════════════════════════
   سود و زیان کالا به تفکیک «واحدِ تولیدکننده»

   ── چرا این جدا از CC_ItemMarginUnit است ──
   جدول CC_ItemMarginUnit کالا را به انبارِ *فروش* نسبت می‌دهد
   (KALAS.ANBARCODE). این برای سؤال «هر واحد چقدر فروخت» درست است،
   ولی جواب سؤال «هر واحد چه چیزی تولید کرد و آن تولید چقدر سود داد»
   نیست — کالایی که در یزدسپار تولید و از انبار یزد فروخته می‌شود،
   آنجا کامل به یزد نسبت داده می‌شود.

   روی اجرای ۸ (خرداد ۱۴۰۵) این تفاوت واقعی است:

       تولید واحد ۱ → فروش از انبار واحد ۱ : ۳۸ کالا
       تولید واحد ۱ → فروش از انبار واحد ۲ : ۱۶ کالا، ۳۶٫۶ میلیارد
       تولید واحد ۲ → فروش از انبار واحد ۲ : ۳۲ کالا
       بدون تولید در این ماه               : ۳۳ کالا، ۹۸٫۷ میلیارد

   آن سطر دوم همان چیزی است که در گزارشِ انبارِ فروش زیر نام یزد
   می‌نشیند در حالی که یزد تولیدش نکرده.

   ── واحدِ تولید از کجا می‌آید ──
   از انبارِ خودِ رسید تولید: HEAD_LST.TAG = 9 و INVO_LST.TAG = 9،
   ستون INVO_LST.ANBAR، که با همان CC_UnitAnbar به واحد نگاشت می‌شود.
   عیناً همان تعریفی که S09/S11 و موتور پیشنهاد تعادل از تولید دارند،
   تا این گزارش با بقیه‌ی زنجیره یک زبان داشته باشد.

   ── کالایی که در دو واحد تولید شده ──
   خرداد ۱۴۰۵: ۱۰۰ کالا از ۱۰۱ کالا فقط در یک واحد تولید شده‌اند و
   یکی در هر دو. آن یکی حذف یا به یک واحد چسبانده نمی‌شود — سود و
   فروشش به نسبتِ *مقدار تولید* بین دو واحد تقسیم می‌شود. هر انتخاب
   دیگری یا کالا را دوبار می‌شمرد یا یک واحد را بی‌دلیل بدهکار می‌کرد.

   ── کالایی که این ماه تولید نشده ──
   کالای بازرگانی یا فروش از موجودی ماه‌های قبل با ProdUnitId = NULL
   ثبت می‌شود، نه اینکه کنار گذاشته شود. دلیلش همان دلیلِ «بدون واحد»
   در CC_ItemMarginUnit است: جمعِ تفکیکی باید مو‌به‌مو با CC_ItemMargin
   بخواند، وگرنه گزارش بی‌صدا بخشی از فروش را گم می‌کند.

   ── مبلغ‌ها از کجا می‌آیند ──
   از خودِ CC_ItemMargin، نه محاسبه‌ی دوباره از KALAS. یعنی «فروشِ کل
   این کالا» بین واحدهای تولیدکننده‌اش تقسیم می‌شود. اگر اینجا هم مثل
   CC_ItemMarginUnit از انبار فروش شروع می‌کردیم، دو دیمنشن قاطی
   می‌شدند و هیچ‌کدام از دو سؤال جواب درست نمی‌گرفت.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.CC_ItemMarginProdUnit','U') IS NULL
CREATE TABLE dbo.CC_ItemMarginProdUnit (
    RunId        INT    NOT NULL,
    UnitId       INT    NULL,          -- NULL = این ماه تولیدی نداشته
    Code         BIGINT NOT NULL,
    ProdQty      FLOAT  NULL,          -- مقدار تولیدِ همین واحد از این کالا
    ProdShare    FLOAT  NULL,          -- سهم همین واحد از کل تولید (۰ تا ۱)
    QtySold      FLOAT  NULL,
    WeightKg     FLOAT  NULL,
    SalesAmount  FLOAT  NULL,
    CostAmount   FLOAT  NULL,
    UnitCost     FLOAT  NULL,
    UnitPrice    FLOAT  NULL,
    GrossSales   FLOAT  NULL,
    Discount     FLOAT  NULL,
    ReturnAmount FLOAT  NULL,
    ReturnQty    FLOAT  NULL,
    Profit AS (ISNULL(SalesAmount,0) - ISNULL(CostAmount,0)) PERSISTED
);
GO

-- کلید یکتا و نه PRIMARY KEY، دقیقاً به همان دلیلِ CC_ItemMarginUnit:
-- UnitId عمداً NULL می‌پذیرد و SQL Server ستون NULLable را در
-- PRIMARY KEY نمی‌پذیرد.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_CC_ItemMarginProdUnit'
                 AND object_id = OBJECT_ID('dbo.CC_ItemMarginProdUnit'))
    CREATE UNIQUE INDEX UX_CC_ItemMarginProdUnit
        ON dbo.CC_ItemMarginProdUnit (RunId, Code, UnitId);
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_S12p_MarginByProdUnit
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMarginProdUnit WHERE RunId = @RunId;

    /* نگاشت انبار → واحد. یک انبار نباید به دو واحد بخورد؛ اگر خورد،
       کوچک‌ترین UnitId برداشته می‌شود تا سطر دوباره‌شماری نشود. */
    ;WITH AnbarUnit AS (
        SELECT Anbar, MIN(UnitId) AS UnitId
        FROM   dbo.CC_UnitAnbar
        GROUP BY Anbar
    ),
    /* ─── تولید ماه، به تفکیک کالا و واحدِ انبارِ رسید ───
       مقدار منفی (اصلاحیه) با هم جمع می‌شود ولی سطرِ خالص صفر یا منفی
       کنار می‌رود: سهم منفی از تولید، سود را وارونه پخش می‌کرد. */
    Prod AS (
        SELECT  TRY_CAST(pl.CODE AS BIGINT) AS Code,
                au.UnitId                   AS UnitId,
                SUM(pl.MEGHK)               AS ProdQty
        FROM    dbo.HEAD_LST h
        JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        LEFT    JOIN AnbarUnit au ON au.Anbar = pl.ANBAR
        WHERE   h.TAG = 9
          AND   h.DATE_N BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(pl.CODE AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(pl.CODE AS BIGINT), au.UnitId
        HAVING  SUM(pl.MEGHK) > 0
    ),
    Share AS (
        SELECT  p.Code,
                p.UnitId,
                p.ProdQty,
                p.ProdQty / SUM(p.ProdQty) OVER (PARTITION BY p.Code) AS ProdShare
        FROM    Prod p
    )
    INSERT dbo.CC_ItemMarginProdUnit
        (RunId, UnitId, Code, ProdQty, ProdShare,
         QtySold, WeightKg, SalesAmount, CostAmount, UnitCost, UnitPrice,
         GrossSales, Discount, ReturnAmount, ReturnQty)
    SELECT  @RunId,
            sh.UnitId,
            m.Code,
            sh.ProdQty,
            sh.ProdShare,
            m.QtySold      * ISNULL(sh.ProdShare, 1),
            m.WeightKg     * ISNULL(sh.ProdShare, 1),
            m.SalesAmount  * ISNULL(sh.ProdShare, 1),
            m.CostAmount   * ISNULL(sh.ProdShare, 1),
            -- نرخ واحد نسبت است، نه مبلغ: با تقسیم سهم عوض نمی‌شود و
            -- عمداً همان عدد کالا می‌ماند تا با گزارش کل یکی بخواند.
            m.UnitCost,
            m.UnitPrice,
            m.GrossSales   * ISNULL(sh.ProdShare, 1),
            m.Discount     * ISNULL(sh.ProdShare, 1),
            m.ReturnAmount * ISNULL(sh.ProdShare, 1),
            m.ReturnQty    * ISNULL(sh.ProdShare, 1)
    FROM    dbo.CC_ItemMargin m
    LEFT    JOIN Share sh ON sh.Code = m.Code
    WHERE   m.RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا به تفکیک واحد تولید: ',
                   COUNT(DISTINCT ISNULL(UnitId,-1)), N' واحد، ',
                   COUNT(*), N' سطر'),
            (SELECT ISNULL(u.UnitId,-1) AS unitId,
                    MAX(ISNULL(cu.UnitName, N'بدون تولید در این ماه')) AS unitName,
                    COUNT(*) AS items,
                    SUM(CASE WHEN u.Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(u.SalesAmount) AS sales,
                    SUM(u.CostAmount)  AS cost,
                    SUM(u.Profit)      AS profit
             FROM   dbo.CC_ItemMarginProdUnit u
             LEFT   JOIN dbo.CC_Unit cu ON cu.UnitId = u.UnitId
             WHERE  u.RunId = @RunId
             GROUP  BY ISNULL(u.UnitId,-1)
             FOR JSON PATH)
    FROM    dbo.CC_ItemMarginProdUnit WHERE RunId = @RunId;
END
GO

PRINT N'جدول CC_ItemMarginProdUnit و رويه CC_sp_S12p_MarginByProdUnit ايجاد شدند.';
GO
