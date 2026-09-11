/* ═══════════════════════════════════════════════════════════════════
   مقايسه‌ي «انتشار نرخ مسطح» با خروجي S11 — فقط خواندني

   پرسش: اگر به‌جاي CC_sp_S11_PropagateRates فقط ميانگين انبار را
   مسطح روي DTL_MANF بنويسيم (همان UPDATE دستي)، چند کالا و چند ريال
   فرق مي‌کند؟

   ⚠️ اين اسکريپت هيچ چيزي نمي‌نويسد. فقط SELECT و جدول موقت.
      قبل از هر تصميمي درباره‌ي جايگزيني S11 اجرا شود.

   ── طرز استفاده ──
   سه متغير بالاي اسکريپت را پر کنيد و اجرا کنيد. @RunId بايد اجرايي
   باشد که S11 در آن با موفقيت تمام شده، وگرنه CC_ItemCost خالي است و
   بخش ۲ بي‌معنا مي‌شود.

   ── فرضِ کليدي ──
   فرض شده zgheymatmiangin{N}.fi همان چيزي است که S11 در #Z مي‌سازد:
   ميانگين وزنيِ خروج انبار در ماه، يعني
       SUM(KALAS.MABL_K) / SUM(KALAS.MEGHk)  WHERE TAG=10 AND MM=@Month
   بخش ۱ همين فرض را آزمايش مي‌کند. اگر آنجا اختلاف زياد بود، بقيه‌ي
   گزارش را نخوانيد — اول بايد معلوم شود آن جدول چطور ساخته مي‌شود.
   ═══════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;

DECLARE @RunId    INT     = 17;                      -- ← اجراي مرجع
DECLARE @Month    TINYINT = 1;                       -- ← ماه
DECLARE @AvgTable SYSNAME = N'zgheymatmiangin1';     -- ← جدول ميانگين همان ماه
DECLARE @DT1      BIGINT  = 14050100;                -- ← ابتداي بازه (بازِ عمدي)
DECLARE @DT2      BIGINT  = 14050132;                -- ← انتهاي بازه

/* ─────────────────────────────────────────────────────────────────
   آماده‌سازي: جدول ميانگين با نوعِ يکسان‌شده

   TRY_CAST روي هر دو طرف، چون نوعِ CODE در DTL_MANF و در جدول
   ميانگين لزوماً يکي نيست؛ join مستقيم‌شان implicit conversion
   مي‌سازد و ايندکس را از کار مي‌اندازد (همان چيزي که ممکن است
   بخشي از کنديِ UPDATE دستي باشد).
   ───────────────────────────────────────────────────────────────── */
IF OBJECT_ID('tempdb..#Flat') IS NOT NULL DROP TABLE #Flat;
CREATE TABLE #Flat (Code BIGINT PRIMARY KEY, fi FLOAT NULL);

DECLARE @sql NVARCHAR(MAX) = N'
    INSERT #Flat (Code, fi)
    SELECT TRY_CAST(z.code AS BIGINT), MAX(z.fi)
    FROM   dbo.' + QUOTENAME(@AvgTable) + N' z
    WHERE  TRY_CAST(z.code AS BIGINT) IS NOT NULL
    GROUP  BY TRY_CAST(z.code AS BIGINT);';
EXEC sp_executesql @sql;

---- ميانگينِ انبار همان‌طور که خودِ S11 حسابش مي‌کند (#Z در رويه)
IF OBJECT_ID('tempdb..#Z') IS NOT NULL DROP TABLE #Z;
SELECT  k.code                                  AS Code,
        SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
INTO    #Z
FROM    dbo.KALAS k
WHERE   k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
GROUP BY k.code;

---- مقدار توليدشده‌ي هر فرمول در ماه — وزنِ اثر ريالي
IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;
SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB,
        SUM(pl.MEGHk)             AS ProdQty
INTO    #Prod
FROM    dbo.HEAD_LST h
JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
  AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
GROUP BY TRY_CAST(pl.N_KOL AS INT)
HAVING  SUM(pl.MEGHk) > 0;
CREATE UNIQUE CLUSTERED INDEX IX_Prod ON #Prod(FNUMB);

---- سطرهاي در دامنه: همان سطرهايي که UPDATE دستي لمس مي‌کند
IF OBJECT_ID('tempdb..#Row') IS NOT NULL DROP TABLE #Row;
SELECT  d.FNUMB,
        TRY_CAST(d.CODE AS BIGINT)  AS Code,
        ISNULL(d.SMABL, 0)          AS SmablNow,   -- آنچه S11 نوشته
        d.MEGHk,
        p.ProdQty
INTO    #Row
FROM    dbo.DTL_MANF  d
JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
JOIN    #Prod p ON p.FNUMB = d.FNUMB
WHERE   TRY_CAST(d.CODE AS BIGINT) IS NOT NULL;
CREATE INDEX IX_Row_Code ON #Row(Code);


/* ═══ ۱) آيا جدول ميانگين همان #Z خودِ S11 است؟ ═══
   اگر «اختلاف_بيش_از_نيم_ريال» بزرگ بود، تحليل بعدي بي‌اعتبار است. */
SELECT  N'۱) اعتبارِ فرضِ جدول ميانگين'                       AS بخش,
        (SELECT COUNT(*) FROM #Z)                            AS کد_در_محاسبه_S11,
        (SELECT COUNT(*) FROM #Flat)                         AS کد_در_جدول_ميانگين,
        (SELECT COUNT(*) FROM #Z z JOIN #Flat f ON f.Code = z.Code
          WHERE ABS(ISNULL(f.fi,0) - ISNULL(z.fi,0)) > 0.5)  AS اختلاف_بيش_از_نيم_ريال,
        (SELECT COUNT(*) FROM #Z z LEFT JOIN #Flat f ON f.Code = z.Code
          WHERE f.Code IS NULL)                              AS فقط_در_S11,
        (SELECT COUNT(*) FROM #Flat f LEFT JOIN #Z z ON z.Code = f.Code
          WHERE z.Code IS NULL)                              AS فقط_در_جدول_ميانگين;


/* ═══ ۲) نرخِ ناممکن — محافظي که UPDATE دستي ندارد ═══
   S11 اينها را رد مي‌کند (شرط fi > 0 و سقف ۲^۵۳) و استثناي CHK-22
   ثبت مي‌کند. UPDATE دستي فقط fi IS NOT NULL دارد، پس همين‌ها را
   به‌عنوان نرخ در فرمول مي‌نويسد. حادثه‌ي کد ۳۳۶۵ از همين‌جا آمد. */
SELECT  N'۲) نرخ ناممکن در جدول ميانگين'   AS بخش,
        f.Code                             AS کد,
        st.NAME                            AS نام,
        f.fi                               AS نرخ_جدول,
        CASE WHEN f.fi <= 0 THEN N'منفي يا صفر'
             ELSE N'خارج از مقياس' END     AS نوع,
        (SELECT COUNT(*) FROM #Row r WHERE r.Code = f.Code)
                                           AS تعداد_سطر_فرمول_متأثر
FROM    #Flat f
LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = f.Code
WHERE   f.fi IS NOT NULL
  AND   (f.fi <= 0 OR ABS(f.fi) >= 9007199254740992)
ORDER BY f.fi;


/* ═══ ۳) خلاصه‌ي اثر — دسته‌بندي سطرهاي فرمول ═══
   «اثر ريالي ماه» = (نرخ مسطح − نرخ فعلي) × MEGHk × مقدار توليد.
   يعني اگر به‌جاي S11 روش مسطح را بنشانيم، بهاي تمام‌شده‌ي توليدِ
   اين ماه چقدر جابه‌جا مي‌شود. */
SELECT  CASE
          WHEN f.Code IS NULL              THEN N'ب) بدون نرخ در جدول — مسطح رهايش مي‌کند (نرخ کهنه مي‌ماند)'
          WHEN f.fi <= 0                   THEN N'الف) نرخ ناممکن — S11 رد مي‌کند، مسطح مي‌نويسد'
          WHEN ABS(f.fi - r.SmablNow) <= 0.5 THEN N'ج) يکسان — بدون اثر'
          ELSE                                  N'د) متفاوت — اثر ريالي دارد'
        END                                                   AS دسته,
        COUNT(*)                                              AS تعداد_سطر_فرمول,
        COUNT(DISTINCT r.Code)                                AS تعداد_کالا,
        SUM((ISNULL(f.fi, r.SmablNow) - r.SmablNow)
            * r.MEGHk * r.ProdQty)                            AS اثر_ريالي_ماه,
        SUM(ABS((ISNULL(f.fi, r.SmablNow) - r.SmablNow)
            * r.MEGHk * r.ProdQty))                           AS اثر_ريالي_قدرمطلق
FROM    #Row r
LEFT    JOIN #Flat f ON f.Code = r.Code
GROUP BY CASE
          WHEN f.Code IS NULL              THEN N'ب) بدون نرخ در جدول — مسطح رهايش مي‌کند (نرخ کهنه مي‌ماند)'
          WHEN f.fi <= 0                   THEN N'الف) نرخ ناممکن — S11 رد مي‌کند، مسطح مي‌نويسد'
          WHEN ABS(f.fi - r.SmablNow) <= 0.5 THEN N'ج) يکسان — بدون اثر'
          ELSE                                  N'د) متفاوت — اثر ريالي دارد'
        END
ORDER BY اثر_ريالي_قدرمطلق DESC;


/* ═══ ۴) پنجاه کالاي با بيشترين اختلاف ريالي ═══
   SourceKind از CC_ItemCost مي‌گويد S11 نرخ را از کجا گرفته بود:
   ۱ = ميانگين انبار (همان کاري که روش مسطح مي‌کند — بايد بخوانند)
   ۲ = کاسکيد فرمول (همان جايي که دو روش واقعاً فرق مي‌کنند)
   ۳ = بدون منبع نرخ */
SELECT  TOP 50
        r.Code                                   AS کد,
        st.NAME                                  AS نام,
        CASE ic.SourceKind WHEN 1 THEN N'ميانگين انبار'
                           WHEN 2 THEN N'کاسکيد فرمول'
                           WHEN 3 THEN N'بدون منبع'
                           ELSE N'—' END          AS منبعِ_نرخ_در_S11,
        ic.LowLevelCode                          AS سطح_BOM,
        MAX(r.SmablNow)                          AS نرخ_فعلي_S11,
        MAX(f.fi)                                AS نرخ_مسطح,
        MAX(f.fi) - MAX(r.SmablNow)              AS اختلاف_واحد,
        COUNT(*)                                 AS تعداد_سطر_فرمول,
        SUM((f.fi - r.SmablNow) * r.MEGHk * r.ProdQty)      AS اثر_ريالي_ماه
FROM    #Row r
JOIN    #Flat f ON f.Code = r.Code AND f.fi > 0
LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = r.Code AND ic.RunId = @RunId
LEFT    JOIN dbo.STUF_DEF  st ON TRY_CAST(st.CODE AS BIGINT) = r.Code
WHERE   ABS(f.fi - r.SmablNow) > 0.5
GROUP BY r.Code, st.NAME, ic.SourceKind, ic.LowLevelCode
ORDER BY ABS(SUM((f.fi - r.SmablNow) * r.MEGHk * r.ProdQty)) DESC;


/* ═══ ۵) کالاهايي که فقط S11 مي‌تواند قيمت بگذارد ═══
   فرمول دارند، ولي اين ماه از انبار حواله نخورده‌اند پس در جدول
   ميانگين نيستند. روش مسطح دست‌نخورده رهايشان مي‌کند و نرخِ ماهِ
   قبل باقي مي‌ماند. اگر اين فهرست خالي بود، ايرادِ «۲» منتفي است. */
SELECT  TOP 50
        r.Code                                   AS کد,
        st.NAME                                  AS نام,
        ic.LowLevelCode                          AS سطح_BOM,
        MAX(r.SmablNow)                          AS نرخ_کهنه‌اي_که_مي‌ماند,
        ic.TotalCost                             AS نرخي_که_S11_حساب_کرد,
        SUM(r.MEGHk * r.ProdQty)                 AS مقدار_مصرف_ماه,
        SUM((ISNULL(ic.TotalCost, 0) - r.SmablNow) * r.MEGHk * r.ProdQty)
                                                 AS اثر_ريالي_جاافتاده
FROM    #Row r
LEFT    JOIN #Flat f ON f.Code = r.Code
LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = r.Code AND ic.RunId = @RunId
LEFT    JOIN dbo.STUF_DEF  st ON TRY_CAST(st.CODE AS BIGINT) = r.Code
WHERE   f.Code IS NULL
GROUP BY r.Code, st.NAME, ic.LowLevelCode, ic.TotalCost
ORDER BY ABS(SUM((ISNULL(ic.TotalCost, 0) - r.SmablNow) * r.MEGHk * r.ProdQty)) DESC;


/* ═══ ۶) زمانِ واقعيِ گام‌ها — آن يک ساعت کجا مي‌رود؟ ═══
   قبل از تصميم درباره‌ي حذف S11 بايد معلوم باشد سهمش از کل چقدر
   است. اگر S11 چند دقيقه است و بقيه تکرارِ S07A، حذفِ S11 مسئله را
   حل نمي‌کند و بايد سراغ تعداد دورهاي حلقه‌ي همگرايي رفت. */
SELECT  StepCode                                   AS گام,
        COUNT(*)                                   AS تعداد_رکورد,
        MAX(Attempt)                               AS بيشترين_دور,
        SUM(DurationMs) / 1000.0                   AS مجموع_ثانيه,
        CAST(100.0 * SUM(DurationMs)
             / NULLIF(SUM(SUM(DurationMs)) OVER (), 0) AS DECIMAL(5,1))
                                                   AS درصد_از_کل,
        AVG(DurationMs) / 1000.0                   AS ميانگين_ثانيه,
        MAX(DurationMs) / 1000.0                   AS بيشينه_ثانيه
FROM    dbo.CC_RunStep
WHERE   RunId = @RunId AND DurationMs IS NOT NULL
GROUP BY StepCode
ORDER BY مجموع_ثانيه DESC;
