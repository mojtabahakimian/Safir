/* ═══════════════════════════════════════════════════════════════════
   موتور پیشنهادِ جابه‌جایی مواد برای صفر کردن زیان کالا

   ── قاعده‌ی حاکم (تصمیم صاحب پروژه) ──
   دستمزد (IMBIBE_MANF) و سربار (IMBIBE_SAR) هرگز برای تنظیم سود کالا
   دست‌کاری نمی‌شوند. تنها اهرم مجاز «مقدار مواد» است، و هر مقداری که از
   فرمولی کم می‌شود باید به فرمول کالای دیگری اضافه شود که همان ماده را
   مصرف می‌کند — تا جمع مصرف فیزیکی ماه ثابت بماند و S08/S09 انحرافی
   نبینند. اجرای واقعیِ انتقال با CC_sp_RebalanceMaterialQty انجام
   می‌شود؛ این رویه فقط «چه چیزی را از کجا به کجا» پیشنهاد می‌دهد.

   ── عمق زنجیره: هر عددی، نه فقط ۲ ──
   @MaxDepth=1 یعنی فقط مواد مستقیمِ فرمولِ کالای زیان‌ده؛ هر واحد بیشتر
   یک سطح پایین‌تر در درختِ نیمه‌ساخته‌ها می‌رود. پیمایش بازگشتی است، پس
   ۳ و ۴ و ۵ هم واقعاً کار می‌کنند (نسخه‌ی قبلی سخت‌کدشده روی ۲ بود و
   گزینه‌ی «۳» در واسط هیچ اثری نداشت). سقف ۸ فقط برای مهار حلقه است.

   ⚠ اثرِ سطوح پایین‌تر رقیق است: کم کردن ماده از فرمولِ یک نیمه‌ساخته،
   بهای آن را برای *همه‌ی* مصرف‌کنندگانش کم می‌کند نه فقط کالای زیان‌ده.
   DilutionPct حاصل‌ضربِ سهم در تمام حلقه‌های زنجیره است.

   ── مقصدها: نیمه‌ساخته هم مجاز است ──
   نسخه‌ی قبلی مقصد را به کالاهایی محدود می‌کرد که در CC_ItemMargin سود
   مثبت داشتند، یعنی فقط کالاهای *فروش‌رفته*. روشِ متعارفِ کاربر دقیقاً
   بیرون از آن دایره بود: «از شیر خام کم کن و به شیر اسکیم بریز» — و شیر
   اسکیم (۳۷۳) نیمه‌ساخته است، هرگز فروخته نمی‌شود و در CC_ItemMargin
   سطر ندارد، پس هیچ‌وقت پیشنهاد نمی‌شد.

   حالا هر کالای دارای فرمول می‌تواند مقصد باشد. ظرفیتش از روی سودِ
   کالاهای فروش‌رفته‌ی *پایین‌دستش* حساب می‌شود: اگر ΔV ریال روی مقصد
   بنشیند، هر مصرف‌کننده‌ی نهایی c سهمی به‌اندازه‌ی absorb_c از آن را
   می‌گیرد، پس سقف = MIN(Profit_c / absorb_c). برای یک کالای فروش‌رفته‌ی
   ساده absorb خودش ۱ است و این دقیقاً همان «ظرفیت = سود» قبلی می‌شود.

   ── بازگشتِ بار به کالای زیان‌ده (BouncePct) ──
   قید سختِ قبلی «مقصد نباید در درختِ ورودی‌های کالای هدف باشد» هم برداشته
   شد، چون همان قید بود که شیر اسکیم را حذف می‌کرد (۳۶۸ خودش شیر اسکیم
   مصرف می‌کند). به‌جایش سهمی که از راه همان مصرف به کالای هدف *برمی‌گردد*
   محاسبه و گزارش می‌شود: BouncePct. تسکینِ خالص = ΔV × (۱ − Bounce)،
   و ستون Capacity همین عددِ خالص است. مقصدی که ≥۹۸٪ برگردد کنار می‌رود.

   ── هشدار زیان‌دهِ پایین‌دست (LoserCount) ──
   اگر کالای زیان‌دهِ دیگری هم پایین‌دستِ مقصد باشد، هر مبلغی زیانش را
   بیشتر می‌کند. چنین کالایی در MIN بالا نمی‌آید (سقف را صفر می‌کرد)، ولی
   شمارشش برمی‌گردد تا کاربر کورکورانه انتخاب نکند.

   ── گاف شناخته‌شده ──
   نسبتِ «فروش‌رفته به تولیدشده»ی خودِ کالای مبدأ در محاسبه نیست: اگر
   ۳۶۸ بیش از فروشش تولید شده باشد، برداشتنِ V ریال از فرمولش کمتر از V
   از بهای فروش‌رفته‌اش کم می‌کند. عمداً وارد نشد تا با محاسبه‌ی مقدارِ
   انتقال در rebalance-apply هم‌خوان بماند؛ هر دو با هم باید اصلاح شوند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────── حافظه‌ی انتخاب‌های کاربر ───────────────── */
IF OBJECT_ID('dbo.CC_RebalancePref','U') IS NULL
CREATE TABLE dbo.CC_RebalancePref (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    SourceCode    BIGINT   NOT NULL,   -- کالای زیان‌ده
    MaterialCode  BIGINT   NOT NULL,   -- ماده‌ای که جابه‌جا می‌شود
    TargetCode    BIGINT   NOT NULL,   -- کالای مقصد
    SharePct      FLOAT    NULL,       -- سهم این مقصد وقتی چند مقصد هست (NULL = خودکار)
    IsActive      BIT      NOT NULL DEFAULT 1,
    Note          NVARCHAR(200) NULL,
    CRT           DATETIME NOT NULL DEFAULT GETDATE(),
    UID           INT      NULL,
    CONSTRAINT UQ_CC_RebalancePref UNIQUE (SourceCode, MaterialCode, TargetCode)
);
GO

/* انتخاب‌ها معمولاً بین ماه‌ها معتبر می‌مانند (تصمیم صاحب پروژه)، پس
   عمداً به سال/ماه مقید نیستند: نشان داده می‌شوند و فقط با درخواست صریح
   کاربر دوباره محاسبه می‌شوند. */
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_RebalanceSuggest
    @RunId      INT,
    @Month      TINYINT,
    @DT1        BIGINT,
    @DT2        BIGINT,
    @SourceCode BIGINT,
    @MaxDepth   TINYINT = 2
AS
BEGIN
    SET NOCOUNT ON;

    -- سقف ۸: پیمایش بازگشتی است و فرمول‌ها می‌توانند حلقه بسازند
    -- (نیمه‌ساخته‌ای که برگشتی خودش را مصرف کند). عمقِ محدود تنها مهارِ
    -- مطمئنی است که CTE بازگشتیِ SQL Server بدون «مجموعه‌ی دیده‌شده‌ها»
    -- در اختیار می‌گذارد.
    IF @MaxDepth IS NULL OR @MaxDepth < 1 SET @MaxDepth = 1;
    IF @MaxDepth > 8 SET @MaxDepth = 8;

    ---- کسری: مبلغی که باید از بهای کالای هدف خارج شود تا سودش صفر شود
    DECLARE @Deficit FLOAT, @SourceProfit FLOAT;

    SELECT  @SourceProfit = Profit
    FROM    dbo.CC_ItemMargin
    WHERE   RunId = @RunId AND Code = @SourceCode;

    IF @SourceProfit IS NULL
    BEGIN
        RAISERROR(N'این کالا در سود و زیانِ این اجرا وجود ندارد (شاید فروشی نداشته).', 16, 1);
        RETURN;
    END

    SET @Deficit = CASE WHEN @SourceProfit < 0 THEN -@SourceProfit ELSE 0 END;

    ---- مقدار تولید هر فرمول در این ماه — عیناً منطق CC_sp_S09_ApplyDecisions
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

    ---- فرمول‌های فعالِ این ماه (فقط آن‌ها که سند تولید دارند)
    IF OBJECT_ID('tempdb..#F') IS NOT NULL DROP TABLE #F;

    SELECT  hm.FNUMB,
            TRY_CAST(hm.CODE AS BIGINT) AS ParentCode,
            p.ProdQty
    INTO    #F
    FROM    dbo.HEAD_MANF hm
    JOIN    #Prod p ON p.FNUMB = hm.FNUMB
    WHERE   hm.GHEYMAT = @Month;

    ---- ── گرافِ «چه چیزی چه چیزی را مصرف می‌کند» ──────────────────────
    -- یک بار ساخته می‌شود و هر دو پیمایش (بالادست برای نامزدها، پایین‌دست
    -- برای ظرفیتِ مقصدها) روی همین می‌نشینند. Qty در واحدِ اصلیِ خودِ ماده
    -- است، چون MEGHk همان واحد را دارد.
    IF OBJECT_ID('tempdb..#Use') IS NOT NULL DROP TABLE #Use;

    SELECT  f.ParentCode,
            TRY_CAST(d.CODE AS BIGINT) AS ChildCode,
            SUM(d.MEGHk * f.ProdQty)   AS Qty,
            MAX(ISNULL(d.SMABL, 0))    AS Rate
    INTO    #Use
    FROM    #F f
    JOIN    dbo.DTL_MANF d ON d.FNUMB = f.FNUMB
    WHERE   TRY_CAST(d.CODE AS BIGINT) IS NOT NULL
    GROUP BY f.ParentCode, TRY_CAST(d.CODE AS BIGINT);

    CREATE INDEX IX_Use_Parent ON #Use (ParentCode);
    CREATE INDEX IX_Use_Child  ON #Use (ChildCode);

    -- کل تولیدِ ماهِ هر کالای دارای فرمول (مخرجِ همه‌ی نسبت‌های رقت)
    IF OBJECT_ID('tempdb..#ProdByCode') IS NOT NULL DROP TABLE #ProdByCode;

    SELECT  ParentCode, SUM(ProdQty) AS ProdQty
    INTO    #ProdByCode
    FROM    #F
    GROUP BY ParentCode;

    CREATE INDEX IX_PBC ON #ProdByCode (ParentCode);

    ---- ── نامزدها: پیمایشِ بالادستِ درختِ کالای زیان‌ده ────────────────
    IF OBJECT_ID('tempdb..#Cand') IS NOT NULL DROP TABLE #Cand;

    WITH up AS (
        -- سطح ۱: مواد مستقیمِ فرمولِ کالای هدف، بدون رقت
        SELECT  u.ChildCode          AS MaterialCode,
                1                    AS Depth,
                u.Qty                AS Qty,
                u.Rate               AS Rate,
                CAST(1.0 AS FLOAT)   AS Dilution,
                CAST(NULL AS BIGINT) AS ViaCode
        FROM    #Use u
        WHERE   u.ParentCode = @SourceCode

        UNION ALL

        -- هر سطح پایین‌تر: رقتِ انباشته × سهمی از تولیدِ این نیمه‌ساخته که
        -- به مصرف‌کننده‌ی بالادستی‌اش می‌رسد.
        -- ⚠ سقفِ ۱: مصرف می‌تواند از تولیدِ همان ماه بیشتر باشد (برداشت از
        -- موجودی اول دوره) و کسر از ۱ رد کند؛ بیش از صد درصدِ اثر بی‌معناست.
        SELECT  u2.ChildCode,
                up.Depth + 1,
                u2.Qty,
                u2.Rate,
                up.Dilution * CASE WHEN up.Qty / p.ProdQty > 1 THEN 1
                                   ELSE up.Qty / p.ProdQty END,
                up.MaterialCode
        FROM    up
        JOIN    #ProdByCode p ON p.ParentCode = up.MaterialCode AND p.ProdQty > 0
        JOIN    #Use u2       ON u2.ParentCode = up.MaterialCode
        WHERE   up.Depth < @MaxDepth
          AND   up.Dilution > 0.0005          -- زیر این، اثر عملاً صفر است
          AND   u2.ChildCode <> @SourceCode   -- حلقه‌ی بدیهی به خودِ کالا
    )
    SELECT  MaterialCode,
            MIN(Depth)                    AS Depth,
            SUM(Qty)                      AS AvailableQty,
            MAX(Rate)                     AS Rate,
            SUM(Qty * Rate)               AS RemovableValue,
            SUM(Qty * Rate * Dilution)    AS EffectiveValue
    INTO    #Cand
    FROM    up
    WHERE   MaterialCode <> @SourceCode
    GROUP BY MaterialCode
    HAVING  SUM(Qty) > 0 AND MAX(Rate) > 0
    OPTION (MAXRECURSION 32);

    -- «از داخلِ …» برای نمایش: نیمه‌ساخته‌ی کم‌عمق‌ترین مسیر
    ALTER TABLE #Cand ADD ViaCode BIGINT NULL;

    WITH up2 AS (
        SELECT  u.ChildCode AS MaterialCode, 1 AS Depth, CAST(NULL AS BIGINT) AS ViaCode,
                CAST(1.0 AS FLOAT) AS Dilution, u.Qty AS Qty
        FROM    #Use u WHERE u.ParentCode = @SourceCode
        UNION ALL
        SELECT  u2.ChildCode, up2.Depth + 1, up2.MaterialCode,
                up2.Dilution * CASE WHEN up2.Qty / p.ProdQty > 1 THEN 1
                                    ELSE up2.Qty / p.ProdQty END,
                u2.Qty
        FROM    up2
        JOIN    #ProdByCode p ON p.ParentCode = up2.MaterialCode AND p.ProdQty > 0
        JOIN    #Use u2       ON u2.ParentCode = up2.MaterialCode
        WHERE   up2.Depth < @MaxDepth AND up2.Dilution > 0.0005
          AND   u2.ChildCode <> @SourceCode
    )
    UPDATE  c
       SET  c.ViaCode = v.ViaCode
    FROM    #Cand c
    CROSS   APPLY (SELECT TOP 1 ViaCode FROM up2
                   WHERE  up2.MaterialCode = c.MaterialCode
                   ORDER  BY Depth) v
    OPTION (MAXRECURSION 32);

    ---- ── مقصدهای بالقوه ───────────────────────────────────────────────
    -- هر کالایی که همین ماده را مصرف می‌کند و خودش کالای زیان‌ده نیست —
    -- چه فروش‌رفته باشد چه نیمه‌ساخته. کالاهایی که هدفِ فعالِ حاشیه سود
    -- دارند کنار می‌روند تا زنجیره‌ی تعدیل‌های تودرتو ساخته نشود.
    IF OBJECT_ID('tempdb..#DestRaw') IS NOT NULL DROP TABLE #DestRaw;

    SELECT  DISTINCT c.MaterialCode, u.ParentCode AS TargetCode
    INTO    #DestRaw
    FROM    #Cand c
    JOIN    #Use u ON u.ChildCode = c.MaterialCode
    WHERE   u.ParentCode <> @SourceCode
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_MarginTarget t
                        WHERE t.Code = u.ParentCode AND t.IsActive = 1);

    ---- ── جذبِ پایین‌دست: ΔV روی مقصد، چقدرش به هر کالای فروش‌رفته می‌رسد ──
    -- از هر مقصد رو به بالا در گراف حرکت می‌کنیم (مصرف‌کننده‌های مقصد،
    -- مصرف‌کننده‌های آن‌ها، …) و سهم را در هر گام ضرب می‌کنیم. خودِ مقصد با
    -- سهم ۱ در مجموعه هست، پس یک کالای فروش‌رفته‌ی ساده absorb=1 می‌گیرد و
    -- ظرفیتش دقیقاً «سودش» می‌شود — همان رفتار قبلی.
    IF OBJECT_ID('tempdb..#Node') IS NOT NULL DROP TABLE #Node;
    SELECT DISTINCT TargetCode AS Node INTO #Node FROM #DestRaw;

    IF OBJECT_ID('tempdb..#Down') IS NOT NULL DROP TABLE #Down;

    WITH dn AS (
        SELECT  n.Node, n.Node AS Descendant, CAST(1.0 AS FLOAT) AS Share, 0 AS Lvl
        FROM    #Node n
        UNION ALL
        SELECT  dn.Node, u.ParentCode,
                dn.Share * CASE WHEN u.Qty / p.ProdQty > 1 THEN 1
                                ELSE u.Qty / p.ProdQty END,
                dn.Lvl + 1
        FROM    dn
        JOIN    #ProdByCode p ON p.ParentCode = dn.Descendant AND p.ProdQty > 0
        JOIN    #Use u        ON u.ChildCode = dn.Descendant
        WHERE   dn.Lvl < 8 AND dn.Share > 0.0005
    )
    SELECT  Node, Descendant, SUM(Share) AS Share
    INTO    #Down
    FROM    dn
    GROUP BY Node, Descendant
    OPTION (MAXRECURSION 64);

    CREATE INDEX IX_Down ON #Down (Node);

    -- جذبِ نهایی به تفکیک کالای فروش‌رفته. عاملِ فروش/تولید: بهایی که وارد
    -- کالایی می‌شود فقط به‌نسبتِ مقدارِ فروش‌رفته‌اش در سود ماه اثر دارد؛
    -- بقیه در موجودی می‌نشیند. بدون سندِ تولید (تولید ماه‌های قبل) عامل ۱
    -- گرفته می‌شود تا مقصد بی‌دلیل حذف نشود.
    IF OBJECT_ID('tempdb..#Absorb') IS NOT NULL DROP TABLE #Absorb;

    SELECT  d.Node,
            d.Descendant                AS Code,
            m.Profit,
            d.Share * CASE WHEN p.ProdQty > 0 AND m.QtySold / p.ProdQty < 1
                           THEN m.QtySold / p.ProdQty ELSE 1 END AS Absorb
    INTO    #Absorb
    FROM    #Down d
    JOIN    dbo.CC_ItemMargin m ON m.RunId = @RunId AND m.Code = d.Descendant
    LEFT    JOIN #ProdByCode p  ON p.ParentCode = d.Descendant
    WHERE   m.QtySold <> 0;

    ---- ── ظرفیتِ خالصِ هر مقصد ───────────────────────────────────────────
    IF OBJECT_ID('tempdb..#Dest') IS NOT NULL DROP TABLE #Dest;

    SELECT  r.MaterialCode,
            r.TargetCode,
            agg.GrossCapacity,
            agg.BouncePct,
            agg.LoserCount,
            -- تسکینِ خالصی که این مقصد می‌تواند بدهد
            agg.GrossCapacity * (1.0 - agg.BouncePct / 100.0) AS Capacity,
            CASE WHEN sm.Code IS NULL THEN 1 ELSE 0 END AS IsSemi
    INTO    #Dest
    FROM    #DestRaw r
    LEFT    JOIN dbo.CC_ItemMargin sm
            ON sm.RunId = @RunId AND sm.Code = r.TargetCode AND sm.QtySold <> 0
    -- سه زیرپرس‌وجوی جدا و نه یک CROSS APPLY با چند تجمیع: شکل دوم برای
    -- هر سطرِ بی‌ربط یک NULL می‌سازد و «Null value is eliminated by an
    -- aggregate» در لاگ می‌نشیند — بی‌ضرر ولی گمراه‌کننده.
    CROSS   APPLY (
        SELECT
            -- سقف: تنگ‌ترین مصرف‌کننده‌ی سودده. کالای زیان‌ده‌ی هدف در این
            -- MIN نمی‌آید؛ اثرش جداگانه به‌صورت Bounce حساب می‌شود.
            (SELECT MIN(a.Profit / a.Absorb) FROM #Absorb a
             WHERE  a.Node = r.TargetCode AND a.Code <> @SourceCode
               AND  a.Profit > 0 AND a.Absorb > 0)                       AS GrossCapacity,
            ISNULL((SELECT MAX(a.Absorb) FROM #Absorb a
                    WHERE a.Node = r.TargetCode AND a.Code = @SourceCode), 0)
                                                             * 100.0     AS BouncePct,
            (SELECT COUNT(*) FROM #Absorb a
             WHERE  a.Node = r.TargetCode AND a.Code <> @SourceCode
               AND  a.Profit <= 0 AND a.Absorb > 0)                      AS LoserCount
    ) agg
    WHERE   agg.GrossCapacity > 0
      -- مقصدی که تقریباً همه‌ی بار را به خودِ کالای زیان‌ده برمی‌گرداند
      -- بی‌فایده است. نمونه‌ی واقعی: کالای ۳۳۶۵ و مقصدِ ۱۷۳۲.
      AND   agg.BouncePct < 98;

    ---- ── جمع‌بندی و رتبه‌بندی ───────────────────────────────────────
    SELECT  c.MaterialCode,
            s.NAME                                   AS MaterialName,
            c.Depth,
            c.ViaCode,
            sv.NAME                                  AS ViaName,
            c.AvailableQty,
            c.Rate,
            c.RemovableValue,
            CASE WHEN c.RemovableValue > 0
                 THEN c.EffectiveValue / c.RemovableValue * 100.0
                 ELSE 0 END                          AS DilutionPct,
            c.EffectiveValue,
            ISNULL(dd.DestCount, 0)                  AS DestCount,
            ISNULL(dd.DestCapacity, 0)               AS DestCapacity,
            @Deficit                                 AS Deficit,
            -- چقدر از کسری با این ماده واقعاً پوشش داده می‌شود
            CASE WHEN ISNULL(dd.DestCount, 0) = 0 THEN 0
                 ELSE (SELECT MIN(v) FROM (VALUES
                          (@Deficit),
                          (c.EffectiveValue),
                          (ISNULL(dd.DestCapacity, 0))) AS x(v))
            END                                      AS Coverage,
            CASE WHEN pref.TargetCode IS NOT NULL THEN 1 ELSE 0 END AS IsRemembered,
            pref.TargetCode                          AS RememberedTarget
    FROM    #Cand c
    LEFT    JOIN (SELECT MaterialCode, COUNT(*) AS DestCount,
                         SUM(Capacity) AS DestCapacity
                  FROM   #Dest GROUP BY MaterialCode) dd
            ON dd.MaterialCode = c.MaterialCode
    LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = c.MaterialCode
    LEFT    JOIN dbo.STUF_DEF sv ON TRY_CAST(sv.CODE AS BIGINT) = c.ViaCode
    LEFT    JOIN dbo.CC_RebalancePref pref
            ON pref.SourceCode = @SourceCode
           AND pref.MaterialCode = c.MaterialCode
           AND pref.IsActive = 1
    ORDER BY
            -- ۱) انتخابِ به‌خاطرسپرده‌ی کاربر همیشه اول
            CASE WHEN pref.TargetCode IS NOT NULL THEN 0 ELSE 1 END,
            -- ۲) موادی که کسری را کامل می‌پوشانند
            CASE WHEN ISNULL(dd.DestCount,0) > 0
                  AND c.EffectiveValue >= @Deficit
                  AND ISNULL(dd.DestCapacity,0) >= @Deficit THEN 0 ELSE 1 END,
            -- ۳) کمترین تعداد مقصد
            ISNULL(dd.DestCount, 0),
            -- ۴) بیشترین پوشش
            c.EffectiveValue DESC;

    ---- مقصدهای هر ماده — برای نمایش در دیالوگ انتخاب
    SELECT  d.MaterialCode,
            d.TargetCode,
            st.NAME      AS TargetName,
            d.Capacity,
            d.GrossCapacity,
            d.BouncePct,
            d.LoserCount,
            d.IsSemi,
            CASE WHEN pref.TargetCode IS NOT NULL THEN 1 ELSE 0 END AS IsRemembered
    FROM    #Dest d
    LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = d.TargetCode
    LEFT    JOIN dbo.CC_RebalancePref pref
            ON pref.SourceCode = @SourceCode
           AND pref.MaterialCode = d.MaterialCode
           AND pref.TargetCode = d.TargetCode
           AND pref.IsActive = 1
    -- بدونِ زیان‌دهِ پایین‌دست اول، بعد بیشترین ظرفیتِ خالص
    ORDER BY d.MaterialCode, d.LoserCount, d.Capacity DESC;
END
GO

PRINT N'رويه CC_sp_RebalanceSuggest (عمق آزاد + مقصد نيمه‌ساخته) به‌روز شد.';
GO
