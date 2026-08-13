/* ═══════════════════════════════════════════════════════════════════
   اصلاح S12 — محاسبه سود بر مبنای کاردکس

   ── چه چیزی غلط بود ──
   نسخه قبلی بهای تمام‌شده را از سند حسابداری (DEED_DTL با TAG=13)
   می‌گرفت. آن عدد، بهای لحظه صدور سند است.

   ── روش درست ──
   AVRAGE در KALAS میانگین متحرک واقعی کاردکس است و MABRIAL همان
   AVRAGE × MEGHk. برای سنجش سود این درست است، چون بهای واقعی
   موجودی را نشان می‌دهد نه بهای لحظه‌ای.

   سود = مبلغ خالص (KHFR) − مبلغ ریالی (MABRIAL)

   ستون‌های KALAS که استفاده می‌شوند:
     TAGCODE = 2   فاکتور فروش
     KHFR          MABL_K − N_MOIN  (مبلغ خالص پس از تخفیف)
     MABRIAL       AVRAGE × MEGHk   (بهای تمام‌شده از کاردکس)
     MEGHk         مقدار کل
     MEGH          وزن به کیلو
   ═══════════════════════════════════════════════════════════════════ */

-- این اسکریپت باید روی دیتابیس هدف شما اجرا شود (USE ثابت حذف شد؛
-- در پکیج اصلی این خط به دیتابیس مشتری YAZDSEPAR1405 اشاره داشت).

/* ستون‌های جدید برای تفکیک تخفیف و برگشت */
IF COL_LENGTH('dbo.CC_ItemMargin','GrossSales') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD GrossSales FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','Discount') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD Discount FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','ReturnAmount') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD ReturnAmount FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','ReturnQty') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD ReturnQty FLOAT NULL;
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_S12_CalcMargin
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMargin WHERE RunId = @RunId;

    /* ─── فروش: TAGCODE = 2 ─── */
    ;WITH Forush AS (
        SELECT  k.CODE                       AS Code,
                SUM(k.MEGHk)                 AS Qty,
                SUM(k.MEGH)                  AS Weight,
                SUM(k.MABL_K)                AS Gross,      -- پيش از تخفيف
                SUM(ISNULL(k.N_MOIN, 0))     AS Discount,   -- تخفيف
                SUM(k.KHFR)                  AS NetSales,   -- مبلغ خالص
                SUM(k.MABRIAL)               AS CostRial    -- AVRAGE × MEGHk
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 2
          AND   k.MM = @Month
        GROUP BY k.CODE
    ),
    /* ─── برگشت از فروش: TAGCODE = 4 ─── */
    Bargasht AS (
        SELECT  k.CODE           AS Code,
                SUM(k.MEGHk)     AS Qty,
                SUM(k.KHFR)      AS NetAmount,
                SUM(k.MABRIAL)   AS CostRial
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 4
          AND   k.MM = @Month
        GROUP BY k.CODE
    )
    INSERT dbo.CC_ItemMargin
        (RunId, Code, QtySold, WeightKg, SalesAmount, CostAmount,
         UnitCost, UnitPrice, GrossSales, Discount, ReturnAmount, ReturnQty)
    SELECT  @RunId,
            f.Code,
            f.Qty      - ISNULL(b.Qty, 0),
            f.Weight,
            f.NetSales - ISNULL(b.NetAmount, 0),      -- فروش خالص پس از برگشت
            f.CostRial - ISNULL(b.CostRial, 0),       -- بهاي واقعي از کاردکس
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
    WHERE   f.Qty <> 0;

    /* ─── هشدار: کالاي فروش‌رفته بدون نرخ کاردکس ───
       اگر MABRIAL صفر باشد يعني AVRAGE در کاردکس صفر است و
       سود آن کالا کاملاً غلط محاسبه مي‌شود. */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-14';

    IF NOT EXISTS (SELECT 1 FROM dbo.CC_CheckRule WHERE RuleCode = 'CHK-14')
        INSERT dbo.CC_CheckRule
            (RuleCode, RuleName, StepCode, ExType, DefaultSeverity,
             RemedyText, SortOrder)
        VALUES ('CHK-14', N'فروش بدون نرخ کاردکس', 'S12', 17, 1,
                N'اين کالا فروخته شده ولي ميانگين نرخ در کاردکس صفر است، پس بهاي تمام‌شده و سودش صفر محاسبه مي‌شود. کاردکس کالا را بررسي کنيد؛ معمولاً يعني رسيد بدون مبلغ ثبت شده.',
                140);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S12', 'CHK-14', 17, 1, m.Code, m.SalesAmount,
            N'کالا فروخته شده ولي نرخ کاردکسش صفر است — سود غيرواقعي'
    FROM    dbo.CC_ItemMargin m
    WHERE   m.RunId = @RunId
      AND   m.CostAmount = 0
      AND   m.SalesAmount <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا: ', COUNT(*), N' کالا، سود کل ',
                   FORMAT(SUM(SalesAmount) - SUM(CostAmount), 'N0'), N' ريال'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(SalesAmount) AS sales,
                    SUM(CostAmount)  AS cost
             FROM dbo.CC_ItemMargin WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;

    SELECT  COUNT(*)                                    AS تعداد_کالا,
            SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS زيان_ده,
            SUM(GrossSales)                             AS فروش_ناخالص,
            SUM(Discount)                               AS تخفيف,
            SUM(ReturnAmount)                           AS برگشت_از_فروش,
            SUM(SalesAmount)                            AS فروش_خالص,
            SUM(CostAmount)                             AS مبلغ_ريالي,
            SUM(SalesAmount) - SUM(CostAmount)          AS سود_کل
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   مقایسه: روش کاردکس در برابر روش سند حسابداری

   برای اطمینان از درستی تغییر. اگر اختلاف بزرگ بود، یعنی سند
   حسابداری با کاردکس نمی‌خواند و خودِ آن یک یافته است.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_CompareMarginMethods
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH AzKardex AS (
        SELECT  k.CODE AS Code,
                SUM(k.KHFR)    AS NetSales,
                SUM(k.MABRIAL) AS Cost
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 2 AND k.MM = @Month
        GROUP BY k.CODE
    ),
    AzSanad AS (
        SELECT  TRY_CAST(d.HES_M AS BIGINT) AS Code,
                SUM(d.BED) - SUM(d.BES)     AS Cost
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.TAG = 13 AND h.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(d.HES_M AS BIGINT)
    )
    SELECT  TOP 50
            k.Code                            AS کد_کالا,
            s.NAME                            AS نام_کالا,
            ROUND(k.NetSales, 0)              AS فروش_خالص,
            ROUND(k.Cost, 0)                  AS بها_از_کاردکس,
            ROUND(ISNULL(sn.Cost, 0), 0)      AS بها_از_سند,
            ROUND(k.Cost - ISNULL(sn.Cost,0), 0) AS اختلاف,
            ROUND(k.NetSales - k.Cost, 0)     AS سود_روش_کاردکس,
            ROUND(k.NetSales - ISNULL(sn.Cost,0), 0) AS سود_روش_سند
    FROM    AzKardex k
    LEFT    JOIN AzSanad sn ON sn.Code = k.Code
    LEFT    JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = k.Code
    WHERE   ABS(k.Cost - ISNULL(sn.Cost, 0)) > 1000
    ORDER BY ABS(k.Cost - ISNULL(sn.Cost, 0)) DESC;

    ;WITH AzKardex AS (
        SELECT SUM(k.MABRIAL) AS Cost FROM dbo.KALAS k
        WHERE k.TAGCODE = 2 AND k.MM = @Month
    ),
    AzSanad AS (
        SELECT SUM(d.BED) - SUM(d.BES) AS Cost
        FROM   dbo.DEED_DTL d JOIN dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE  d.TAG = 13 AND h.DATE_S BETWEEN @DT1 AND @DT2
    )
    SELECT  ROUND((SELECT Cost FROM AzKardex), 0) AS جمع_بها_کاردکس,
            ROUND((SELECT Cost FROM AzSanad),  0) AS جمع_بها_سند,
            ROUND((SELECT Cost FROM AzKardex) -
                  (SELECT Cost FROM AzSanad), 0)  AS اختلاف_کل;
END
GO


PRINT N'S12 با منطق کاردکس بازنويسي شد.';

/* نمونه:
   EXEC dbo.CC_sp_CompareMarginMethods @Month=4, @DT1=14050401, @DT2=14050431;
*/
GO
