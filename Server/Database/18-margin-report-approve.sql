/* ═══════════════════════════════════════════════════════════════════
   S12 تا S14 — سود کالا، گزارش هیئت‌مدیره، تأیید نهایی

   S12  سود و زیان به تفکیک کالا + اعمال هدف حاشیه
   S13  داده گزارش اکسل
   S14  تأیید و قفل دوره

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S12 که در CC_ItemMargin (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* جدول نتیجه سود کالا */
IF OBJECT_ID('dbo.CC_ItemMargin','U') IS NULL
CREATE TABLE dbo.CC_ItemMargin (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT      NOT NULL,
    Code          BIGINT   NOT NULL,
    QtySold       FLOAT    NOT NULL DEFAULT 0,
    WeightKg      FLOAT    NULL,
    SalesAmount   FLOAT    NOT NULL DEFAULT 0,   -- مبلغ خالص فروش
    CostAmount    FLOAT    NOT NULL DEFAULT 0,   -- بهاي تمام‌شده کالاي فروش‌رفته
    Profit        AS (SalesAmount - CostAmount) PERSISTED,
    UnitCost      FLOAT    NULL,
    UnitPrice     FLOAT    NULL,
    CalculatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CC_ItemMargin UNIQUE (RunId, Code)
);
GO


/* ═══════════════════════════════════════════════════════════════════
   S12 — محاسبه سود و زیان کالا

   فروش    از فاکتورهای TAG=2
   بها     از حساب قیمت تمام‌شده (GHEYMAT) به تفکیک کالا
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S12_CalcMargin
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMargin WHERE RunId = @RunId;

    ;WITH Forush AS (
        SELECT  CAST(i.CODE AS BIGINT)                       AS Code,
                SUM(i.MEGHk)                                 AS Qty,
                SUM(i.MEGH)                                  AS Weight,
                SUM(i.MABL_K - ISNULL(i.N_MOIN, 0))          AS NetSales
        FROM    dbo.INVO_LST i
        JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
        WHERE   i.TAG = 2 AND h.DATE_N BETWEEN @DT1 AND @DT2
        GROUP BY CAST(i.CODE AS BIGINT)
    ),
    Baha AS (
        -- بهاي تمام‌شده کالاي فروش‌رفته از سند حسابداري
        SELECT  TRY_CAST(d.HES_M AS BIGINT) AS Code,
                SUM(d.BED) - SUM(d.BES)     AS Cost
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.TAG = 13
          AND   h.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(d.HES_M AS BIGINT)
    )
    INSERT dbo.CC_ItemMargin
        (RunId, Code, QtySold, WeightKg, SalesAmount, CostAmount, UnitCost, UnitPrice)
    SELECT  @RunId,
            f.Code,
            f.Qty,
            f.Weight,
            f.NetSales,
            ISNULL(b.Cost, ISNULL(ic.TotalCost, 0) * f.Qty),
            CASE WHEN f.Qty <> 0
                 THEN ISNULL(b.Cost, ISNULL(ic.TotalCost,0) * f.Qty) / f.Qty END,
            CASE WHEN f.Qty <> 0 THEN f.NetSales / f.Qty END
    FROM    Forush f
    LEFT    JOIN Baha b ON b.Code = f.Code
    LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = f.Code AND ic.RunId = @RunId
    WHERE   f.Qty <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا: ', COUNT(*), N' کالا، ',
                   SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END), N' زيان‌ده'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(SalesAmount) AS totalSales,
                    SUM(CostAmount)  AS totalCost,
                    SUM(SalesAmount) - SUM(CostAmount) AS totalProfit
             FROM dbo.CC_ItemMargin WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S12_CalcMargin.MarginSummary)
    SELECT  COUNT(*)                                              AS Items,
            SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END)           AS LossItems,
            SUM(SalesAmount)                                      AS TotalSales,
            SUM(CostAmount)                                       AS TotalCost,
            SUM(SalesAmount) - SUM(CostAmount)                    AS TotalProfit
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S12b — اعمال هدف حاشیه سود

   وقتی زیان یک کالا صفر می‌شود، مبلغ آن از بهای تمام‌شده‌اش کم می‌شود.
   این مبلغ یا (الف) به یک کالاي متعادل‌کننده‌ي دستيِ واحد اضافه مي‌شود
   (TargetKind=1/2 با BalancingCode مشخص)، يا (ب) با «پخش خودکار»
   (TargetKind=4) متناسب با سود، بين همه‌ي کالاهاي سودده و بدون هدفِ
   موجود در آن اجرا پخش مي‌شود — چون يک کالاي زيان‌ده معمولاً از ظرفيتِ
   يک کالاي سودده‌ي تنها بيشتر است.

   تغییر روی IMBIBE_MANF فرمول انجام می‌گیرد، چون تنها جزئی است
   که مستقل از مواد قابل تنظیم است.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S12b_ApplyMarginTargets
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Adj') IS NOT NULL DROP TABLE #Adj;

    ---- مبلغ تعديل لازم براي هر کالاي هدف‌دار (دستي يا خودکار)
    SELECT  m.Code,
            t.TargetKind,
            t.TargetPct,
            t.BalancingCode,
            m.SalesAmount,
            m.CostAmount,
            m.QtySold,
            CASE t.TargetKind
                 WHEN 1 THEN m.CostAmount - m.SalesAmount                    -- سود صفر
                 WHEN 2 THEN m.CostAmount - m.SalesAmount * (1 - t.TargetPct/100.0)
                 WHEN 4 THEN m.CostAmount - m.SalesAmount                    -- سود صفر + پخش خودکار
                 ELSE 0 END AS AdjustAmount
    INTO    #Adj
    FROM    dbo.CC_ItemMargin m
    JOIN    dbo.CC_MarginTarget t ON t.Code = m.Code AND t.IsActive = 1
    WHERE   m.RunId = @RunId
      AND   t.TargetKind IN (1, 2, 4)
      AND   m.QtySold <> 0;

    DELETE #Adj WHERE ABS(AdjustAmount) < 1;

    ---- استخر پخش خودکار: کالاهاي سودده‌اي که خودشان هدف يا
    -- متعادل‌کننده‌ي دستيِ کسي نيستند (تا تعارض با تخصيص دستي پيش نيايد)
    IF OBJECT_ID('tempdb..#Pool') IS NOT NULL DROP TABLE #Pool;

    SELECT  m.Code, m.Profit, m.QtySold
    INTO    #Pool
    FROM    dbo.CC_ItemMargin m
    WHERE   m.RunId = @RunId
      AND   m.Profit > 0
      AND   m.QtySold <> 0
      AND   m.Code NOT IN (SELECT Code FROM #Adj)
      AND   m.Code NOT IN (SELECT BalancingCode FROM #Adj WHERE BalancingCode IS NOT NULL);

    DECLARE @TotalAutoAdjust FLOAT = (SELECT ISNULL(SUM(AdjustAmount), 0) FROM #Adj WHERE TargetKind = 4);
    DECLARE @TotalPoolProfit FLOAT = (SELECT ISNULL(SUM(Profit), 0) FROM #Pool);

    IF @TotalAutoAdjust > @TotalPoolProfit
    BEGIN
        SELECT  @TotalAutoAdjust AS مجموع_زيان_پخش_خودکار,
                @TotalPoolProfit AS مجموع_سود_استخر;

        RAISERROR(N'مجموع زيان کالاهاي «پخش خودکار» از مجموع سود کالاهاي سودده‌ي موجود (استخر) بيشتر است؛ بدون منفي‌شدن نرخ جذب امکان پخش کامل نيست — يک يا چند کالا را از حالت «پخش خودکار» خارج کنيد يا اهداف دستي را کاهش دهيد.', 16, 1);
        RETURN;
    END

    IF OBJECT_ID('tempdb..#AutoShare') IS NOT NULL DROP TABLE #AutoShare;

    SELECT  p.Code,
            p.QtySold AS Qty,
            (p.Profit / NULLIF(@TotalPoolProfit, 0)) * @TotalAutoAdjust AS Amount
    INTO    #AutoShare
    FROM    #Pool p
    WHERE   @TotalAutoAdjust <> 0;

    ---- تجميع مبلغ افزايشيِ هر متعادل‌کننده — دستي و سهم پخش خودکار با هم
    IF OBJECT_ID('tempdb..#BalancerAgg') IS NOT NULL DROP TABLE #BalancerAgg;

    SELECT  Code, SUM(Amount) AS Amount, MAX(Qty) AS Qty
    INTO    #BalancerAgg
    FROM (
        SELECT  a.BalancingCode AS Code, a.AdjustAmount AS Amount, bm.QtySold AS Qty
        FROM    #Adj a
        JOIN    dbo.CC_ItemMargin bm ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
        WHERE   a.BalancingCode IS NOT NULL AND bm.QtySold <> 0
        UNION ALL
        SELECT  Code, Amount, Qty FROM #AutoShare
    ) u
    GROUP BY Code;

    ---- هشدار: کالاي متعادل‌کننده زيان‌ده مي‌شود
    IF OBJECT_ID('tempdb..#Warn') IS NOT NULL DROP TABLE #Warn;

    SELECT  ba.Code                    AS BalancingCode,
            ba.Amount                  AS AdjustAmount,
            bm.Profit                  AS BalancerProfitBefore,
            bm.Profit - ba.Amount      AS BalancerProfitAfter
    INTO    #Warn
    FROM    #BalancerAgg ba
    JOIN    dbo.CC_ItemMargin bm ON bm.Code = ba.Code AND bm.RunId = @RunId
    WHERE   bm.Profit - ba.Amount < 0
      AND   bm.Profit >= 0;

    ---- نگهبان: نرخ جذب منفي
    -- هشدار #Warn بالا فقط سودِ کالاي متعادل‌کننده را مي‌سنجد، نه نرخي که
    -- واقعاً نوشته مي‌شود. اگر مبلغ تعديل از جذب فعلي بزرگ‌تر باشد،
    -- IMBIBE_MANF منفي مي‌شود — نرخ جذب دستمزدِ منفي در بهاي تمام‌شده
    -- بي‌معناست و S11 همان را به کل درخت محصول منتشر مي‌کند. اين حالت
    -- روي داده واقعي ديده شد: کالايي که نرخ کاردکسش صفر بود (CHK-14) با
    -- هدف «سود صفر»، جذب متعادل‌کننده را به عدد منفي برد.
    IF OBJECT_ID('tempdb..#Neg') IS NOT NULL DROP TABLE #Neg;

    SELECT q.Code, q.Naghsh, q.NerkhBefore, q.NerkhAfter
    INTO   #Neg
    FROM (
        SELECT  CAST(hm.CODE AS BIGINT) AS Code,
                N'کالاي هدف' AS Naghsh,
                hm.IMBIBE_MANF AS NerkhBefore,
                hm.IMBIBE_MANF - (a.AdjustAmount / NULLIF(a.QtySold, 0)) AS NerkhAfter
        FROM    dbo.HEAD_MANF hm
        JOIN    #Adj a ON CAST(hm.CODE AS BIGINT) = a.Code
        WHERE   hm.GHEYMAT = @Month
        UNION ALL
        SELECT  CAST(hm.CODE AS BIGINT),
                N'متعادل‌کننده',
                hm.IMBIBE_MANF,
                hm.IMBIBE_MANF + (ba.Amount / NULLIF(ba.Qty, 0))
        FROM    dbo.HEAD_MANF hm
        JOIN    #BalancerAgg ba ON CAST(hm.CODE AS BIGINT) = ba.Code
        WHERE   hm.GHEYMAT = @Month
    ) q
    WHERE  q.NerkhAfter < 0;

    IF @WhatIf = 1
    BEGIN
        SELECT  a.Code               AS کد_کالا,
                s.NAME               AS نام_کالا,
                a.SalesAmount        AS فروش,
                a.CostAmount         AS بها,
                a.SalesAmount - a.CostAmount AS سود_فعلي,
                a.AdjustAmount       AS مبلغ_تعديل,
                a.TargetKind         AS نوع_هدف,
                a.BalancingCode      AS کالاي_متعادل_کننده,
                sb.NAME              AS نام_متعادل_کننده
        FROM    #Adj a
        LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = a.Code
        LEFT    JOIN dbo.STUF_DEF sb ON TRY_CAST(sb.CODE AS BIGINT) = a.BalancingCode
        ORDER BY ABS(a.AdjustAmount) DESC;

        ---- سهم هر کالا از پخش خودکار — براي پيش‌نمايش
        SELECT  au.Code            AS کد_کالا,
                s.NAME             AS نام_کالا,
                au.Amount          AS سهم_از_پخش_خودکار,
                pm.Profit          AS سود_قبل,
                pm.Profit - au.Amount AS سود_بعد
        FROM    #AutoShare au
        JOIN    dbo.CC_ItemMargin pm ON pm.Code = au.Code AND pm.RunId = @RunId
        LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = au.Code
        ORDER BY au.Amount DESC;

        SELECT  w.BalancingCode           AS متعادل_کننده,
                w.BalancerProfitBefore    AS سود_قبل,
                w.BalancerProfitAfter     AS سود_بعد,
                N'کالاي متعادل‌کننده زيان‌ده مي‌شود' AS هشدار
        FROM    #Warn w;

        SELECT  n.Code        AS کد_کالا,
                n.Naghsh      AS نقش,
                n.NerkhBefore AS نرخ_جذب_فعلي,
                n.NerkhAfter  AS نرخ_جذب_پس_از_اعمال,
                N'نرخ جذب منفي مي‌شود — اعمال نخواهد شد' AS خطا
        FROM    #Neg n;

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Neg)
    BEGIN
        SELECT  n.Code        AS کد_کالا,
                n.Naghsh      AS نقش,
                n.NerkhBefore AS نرخ_جذب_فعلي,
                n.NerkhAfter  AS نرخ_جذب_پس_از_اعمال
        FROM    #Neg n;

        RAISERROR(N'اعمال هدف حاشيه سود، نرخ جذب را منفي مي‌کند و بهاي تمام‌شده را خراب مي‌کند؛ کالاي متعادل‌کننده يا هدف را تغيير دهيد.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    ---- کاهش بهاي کالاي هدف: تعديل نرخ جذب دستمزد فرمول
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF - (a.AdjustAmount / NULLIF(a.QtySold, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN    #Adj a ON CAST(hm.CODE AS BIGINT) = a.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n1 INT = @@ROWCOUNT;

    ---- افزايش بهاي کالاي متعادل‌کننده (دستي يا خودکار) به همان مبلغ
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF + (ba.Amount / NULLIF(ba.Qty, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'جذب اثر معکوس هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN    #BalancerAgg ba ON CAST(hm.CODE AS BIGINT) = ba.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n2 INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S12', 1,
            CONCAT(N'هدف حاشيه سود: ', @n1, N' کالاي هدف، ', @n2, N' متعادل‌کننده (دستي+پخش خودکار)'));

    COMMIT;

    SELECT @n1 AS کالاي_هدف, @n2 AS متعادل_کننده;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S13 — داده گزارش هیئت‌مدیره

   شیت‌های موجود گزارش اکسل شما، به‌علاوه شیت جدید «خلاصه اجرا».
   خروجی چند مجموعه است که سمت سرور با ClosedXML به اکسل تبدیل می‌شود.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S13_ReportData
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Month TINYINT, @DT1 BIGINT, @DT2 BIGINT;
    SELECT @Month = PeriodMonth, @DT1 = DateFrom, @DT2 = DateTo
    FROM   dbo.CC_Run WHERE RunId = @RunId;

    ---- ۱) سود کالا به کالا
    SELECT  m.Code                        AS کد_کالا,
            s.NAME                        AS نام_کالا,
            @Month                        AS ماه,
            m.WeightKg                    AS وزن_به_کيلو,
            m.QtySold                     AS مقدار_کل,
            m.SalesAmount                 AS مبلغ_خالص,
            m.CostAmount                  AS مبلغ_ريالي,
            m.Profit                      AS سود,
            CASE WHEN m.SalesAmount <> 0
                 THEN ROUND(m.Profit / m.SalesAmount * 100, 0) END AS درصد
    FROM    dbo.CC_ItemMargin m
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = m.Code
    WHERE   m.RunId = @RunId
    ORDER BY m.Profit;

    ---- ۲) خلاصه اجرا — شيت جديدي که امروز وجود ندارد
    SELECT  r.RunId                       AS شماره_اجرا,
            r.FiscalYear                  AS سال,
            r.PeriodMonth                 AS ماه,
            r.RunNo                       AS نوبت,
            CASE r.RunKind WHEN 2 THEN N'قطعي' ELSE N'آزمايشي' END AS نوع,
            r.StartedByUser               AS کاربر,
            r.ApprovedByUser              AS تأييدکننده,
            (SELECT COUNT(*) FROM dbo.CC_FormulaChange WHERE RunId = @RunId)
                                          AS تعداد_تغيير_فرمول,
            (SELECT SUM(ISNULL(AmountVariance,0)) FROM dbo.CC_Variance WHERE RunId = @RunId)
                                          AS انحراف_مصرف,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND IsResolved = 0)
                                          AS استثناي_باز
    FROM    dbo.CC_Run r WHERE r.RunId = @RunId;

    ---- ۳) هزينه تبديل به تفکيک واحد
    SELECT  u.UnitName                    AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل' WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END AS نوع,
            c.AbsorbedAmount              AS جذب_شده,
            c.ActualAmount                AS واقعي,
            c.AdjustFactor                AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;

    ---- ۴) بيشترين تغيير نرخ — پاسخ به «چرا اين عدد عوض شد؟»
    SELECT  TOP 100
            f.FNUMB                       AS شماره_فرمول,
            sp.NAME                       AS کالاي_توليدي,
            sc.NAME                       AS ماده,
            f.FieldName                   AS فيلد,
            f.OldValue                    AS مقدار_قبل,
            f.NewValue                    AS مقدار_بعد,
            f.Reason                      AS علت
    FROM    dbo.CC_FormulaChange f
    LEFT    JOIN dbo.STUF_DEF sp ON TRY_CAST(sp.CODE AS BIGINT) = f.ParentCode
    LEFT    JOIN dbo.STUF_DEF sc ON TRY_CAST(sc.CODE AS BIGINT) = f.ChildCode
    WHERE   f.RunId = @RunId
    ORDER BY ABS(ISNULL(f.NewValue,0) - ISNULL(f.OldValue,0)) DESC;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S14 — تأیید نهایی و قفل دوره
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S14_Approve
    @RunId    INT,
    @UserName NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @kind TINYINT, @status TINYINT, @year SMALLINT, @month TINYINT;

    SELECT @kind = RunKind, @status = Status,
           @year = FiscalYear, @month = PeriodMonth
    FROM   dbo.CC_Run WHERE RunId = @RunId;

    IF @kind <> 2
    BEGIN
        RAISERROR(N'فقط اجراي قطعي قابل تأييد است.', 16, 1);
        RETURN;
    END

    IF @status <> 3
    BEGIN
        RAISERROR(N'اجرا هنوز تکميل نشده است.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Exception
               WHERE RunId = @RunId AND Severity = 2 AND IsResolved = 0)
    BEGIN
        RAISERROR(N'استثناي مسدودکننده باز وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @year AND PeriodMonth = @month
                 AND RunKind = 2 AND ApprovedAtUtc IS NOT NULL AND RunId <> @RunId)
    BEGIN
        RAISERROR(N'براي اين ماه قبلاً يک اجراي قطعي تأييد شده است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    UPDATE dbo.CC_Run
       SET ApprovedByUser = @UserName,
           ApprovedAtUtc  = SYSUTCDATETIME()
     WHERE RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S14', 1,
            CONCAT(N'تأييد نهايي دوره ', @year, '/', @month, N' توسط ', @UserName));

    COMMIT;

    SELECT N'دوره تأييد و قفل شد' AS وضعيت;
END
GO


PRINT N'رويه‌هاي S12 تا S14 ايجاد شدند.';
GO
