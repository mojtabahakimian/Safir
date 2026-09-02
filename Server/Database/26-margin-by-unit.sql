/* ═══════════════════════════════════════════════════════════════════
   سود و زیان کالا به تفکیک واحد تولید

   ── چرا جدول جدا و نه تغییر CC_ItemMargin ──
   گرین (grain) جدول CC_ItemMargin «یک سطر به‌ازای هر کالا در هر اجرا»
   است و کلی کد به همین شکل تکیه دارد: S12b، تابلوی سود و زیان،
   CC_MarginTarget، CHK-14، و گزارش هیئت‌مدیره. اگر گرین را به
   (کالا × واحد) تغییر بدهیم همه‌ی آن‌ها بی‌صدا دو‌برابر می‌شمارند.
   پس گزارش تفکیکی در جدول خودش می‌نشیند و گزارش فعلی دست‌نخورده
   می‌ماند — «علاوه بر»، نه «به‌جای».

   ── دیمنشن واحد از کجا می‌آید ──
   KALAS.DEPATMAN وسوسه‌انگیز است ولی *کد بخش فروش* است نه واحد تولید
   (روی داده‌ی واقعی مقادیری مثل ۲۰، ۲۱، ۸۰۲۰۳۰۹ دارد، درحالی‌که
   CC_Unit.Depatman فقط ۱ و ۲ است). دیمنشن درست KALAS.ANBARCODE است که
   از CC_UnitAnbar به واحد نگاشت می‌شود:

       واحد ۱ (کارخانه یزدسپار) → انبارهای ۷,۸,۱,۲,۳,۱۰,۱۴,۱۵
       واحد ۲ (یزد)             → انبارهای ۸۱۰,۸۱۱,۸۰۷,۸۰۸

   فروشی که از انباری بیاید که به هیچ واحدی نگاشت ندارد با UnitId=NULL
   ثبت می‌شود تا بی‌صدا گم نشود — جمعِ تفکیکی باید با جمع کل بخواند.

   ── هم‌خوانی با گزارش کل ──
   منطق محاسبه عیناً همان CC_sp_S12_CalcMargin است (فروش TAGCODE=2،
   برگشت TAGCODE=4، بها از MABRIAL کاردکس)، فقط با یک کلید گروه‌بندی
   بیشتر. پس جمعِ سطرهای هر کالا روی همه‌ی واحدها باید با سطر همان کالا
   در CC_ItemMargin برابر باشد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.CC_ItemMarginUnit','U') IS NULL
CREATE TABLE dbo.CC_ItemMarginUnit (
    RunId        INT    NOT NULL,
    UnitId       INT    NULL,          -- NULL = انبارِ بدون نگاشت واحد
    Code         BIGINT NOT NULL,
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

-- ⚠ کلید یکتا، نه PRIMARY KEY: ستون UnitId عمداً NULL می‌پذیرد (فروش از
-- انباری که به هیچ واحدی نگاشت ندارد) و SQL Server ستون NULLable را در
-- PRIMARY KEY قبول نمی‌کند. در UNIQUE INDEX، مقادیر NULL با هم برابر
-- شمرده می‌شوند — که دقیقاً همان چیزی است که می‌خواهیم: به‌ازای هر
-- (اجرا، کالا) حداکثر یک سطرِ «بدون واحد».
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_CC_ItemMarginUnit'
                 AND object_id = OBJECT_ID('dbo.CC_ItemMarginUnit'))
    CREATE UNIQUE INDEX UX_CC_ItemMarginUnit
        ON dbo.CC_ItemMarginUnit (RunId, Code, UnitId);
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_S12u_MarginByUnit
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMarginUnit WHERE RunId = @RunId;

    /* نگاشت انبار → واحد. یک انبار نباید به دو واحد بخورد؛ اگر خورد،
       کوچک‌ترین UnitId برداشته می‌شود تا سطر دوباره‌شماری نشود. */
    ;WITH AnbarUnit AS (
        SELECT Anbar, MIN(UnitId) AS UnitId
        FROM   dbo.CC_UnitAnbar
        GROUP BY Anbar
    ),
    /* ─── فروش: TAGCODE = 2 ─── */
    Forush AS (
        SELECT  k.CODE                       AS Code,
                au.UnitId                    AS UnitId,
                SUM(k.MEGHk)                 AS Qty,
                SUM(k.MEGH)                  AS Weight,
                SUM(k.MABL_K)                AS Gross,
                SUM(ISNULL(k.N_MOIN, 0))     AS Discount,
                SUM(k.KHFR)                  AS NetSales,
                SUM(k.MABRIAL)               AS CostRial
        FROM    dbo.KALAS k
        LEFT    JOIN AnbarUnit au ON au.Anbar = k.ANBARCODE
        WHERE   k.TAGCODE = 2
          AND   k.MM = @Month
        GROUP BY k.CODE, au.UnitId
    ),
    /* ─── برگشت از فروش: TAGCODE = 4 ─── */
    Bargasht AS (
        SELECT  k.CODE           AS Code,
                au.UnitId        AS UnitId,
                SUM(k.MEGHk)     AS Qty,
                SUM(k.KHFR)      AS NetAmount,
                SUM(k.MABRIAL)   AS CostRial
        FROM    dbo.KALAS k
        LEFT    JOIN AnbarUnit au ON au.Anbar = k.ANBARCODE
        WHERE   k.TAGCODE = 4
          AND   k.MM = @Month
        GROUP BY k.CODE, au.UnitId
    )
    INSERT dbo.CC_ItemMarginUnit
        (RunId, UnitId, Code, QtySold, WeightKg, SalesAmount, CostAmount,
         UnitCost, UnitPrice, GrossSales, Discount, ReturnAmount, ReturnQty)
    SELECT  @RunId,
            f.UnitId,
            f.Code,
            f.Qty      - ISNULL(b.Qty, 0),
            f.Weight,
            f.NetSales - ISNULL(b.NetAmount, 0),
            f.CostRial - ISNULL(b.CostRial, 0),
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.CostRial - ISNULL(b.CostRial,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.NetSales - ISNULL(b.NetAmount,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            f.Gross,
            f.Discount,
            ISNULL(b.NetAmount, 0),
            ISNULL(b.Qty, 0)
    FROM    Forush f
    LEFT    JOIN Bargasht b ON b.Code = f.Code
                           AND ISNULL(b.UnitId, -1) = ISNULL(f.UnitId, -1)
    WHERE   f.Qty <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا به تفکیک واحد: ', COUNT(DISTINCT ISNULL(UnitId,-1)),
                   N' واحد، ', COUNT(*), N' سطر'),
            (SELECT ISNULL(u.UnitId,-1) AS unitId,
                    MAX(ISNULL(cu.UnitName, N'بدون واحد')) AS unitName,
                    COUNT(*) AS items,
                    SUM(CASE WHEN u.Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(u.SalesAmount) AS sales,
                    SUM(u.CostAmount)  AS cost,
                    SUM(u.Profit)      AS profit
             FROM   dbo.CC_ItemMarginUnit u
             LEFT   JOIN dbo.CC_Unit cu ON cu.UnitId = u.UnitId
             WHERE  u.RunId = @RunId
             GROUP  BY ISNULL(u.UnitId,-1)
             FOR JSON PATH)
    FROM    dbo.CC_ItemMarginUnit WHERE RunId = @RunId;
END
GO

PRINT N'جدول CC_ItemMarginUnit و رويه CC_sp_S12u_MarginByUnit ايجاد شدند.';
GO
