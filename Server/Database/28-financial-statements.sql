/* ═══════════════════════════════════════════════════════════════════
   صورت‌های مالیِ بهای تمام‌شده

   سه صورتِ استاندارد، همه از داده‌ی همین اجرا:
     ۱) صورت بهای تمام‌شده کالای ساخته‌شده  (COGM)
     ۲) صورت بهای تمام‌شده کالای فروش‌رفته   (COGS)
     ۳) صورت سود و زیان ناخالص

   ── منبع هر رقم ──
   موجودی اول/پایان دوره : کاردکس، «مانده‌مقدار × آخرین نرخ میانگین» —
                           همان روشی که dbo.MOGHA_ANBAR و دروازه‌ی S05
                           استفاده می‌کنند، نه جمعِ خامِ MABL_K (توضیحش
                           در 14-s05-gate.sql آمده: S07A فقط AVRAGE را
                           به‌روز می‌کند نه MABL_K).
   خرید دوره             : INVO_LST TAG=1 به انبارهای مواد
   دستمزد و سربار        : CC_ConversionCost.ActualAmount — یعنی رقمِ
                           واقعیِ حسابداری، نه جذب‌شده
   فروش و بهای فروش‌رفته  : CC_ItemMargin (خروجی S12)

   ── تله‌ای که این صورت عمداً پنهانش نمی‌کند ──
   انبارها نقش دارند (CC_UnitAnbar.AnbarRole): ۱=مواد مصرفی تولید،
   ۲=مواد اولیه، ۳=محصول، ۴=سایر. روی داده‌ی واقعی (خرداد ۱۴۰۵) دیده شد
   که ۱٬۰۸۱ میلیارد ریال از انبارِ *محصول* به انبارِ مصرفی برمی‌گردد —
   یعنی نیمه‌ساخته‌ای که دوباره وارد تولید می‌شود.

   اگر آن مبلغ ساده در «مواد مستقیم» بنشیند، دستمزد و سرباری که در
   مرحله‌ی قبل داخلش رفته دوباره شمرده می‌شود و صورت متورم می‌شود. پس
   به‌عنوان یک سطرِ اطلاعیِ جدا گزارش می‌گردد، نه قاطیِ مواد.

   ── خط تطبیق ──
   آخرین سطرِ صورت دوم، COGS محاسبه‌شده از گردشِ انبار را با جمعِ
   CC_ItemMargin.CostAmount می‌سنجد. این دو باید بخوانند؛ اختلافشان
   یعنی جایی از زنجیره‌ی انبار→بها ناسازگار است. عمداً «تطبیق» است نه
   «تصحیح»: عدد را دستکاری نمی‌کنیم، اختلاف را نشان می‌دهیم.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────── سرفصل‌های هزینه‌های دوره ─────────
   دستمزد و سربار در CC_UnitAcc تعریف می‌شوند چون به «واحد تولیدی»
   می‌چسبند. هزینه‌های فروش و اداری این‌طور نیستند — هزینه‌ی دوره‌اند و
   به کل شرکت تعلق دارند، پس جدول خودشان را دارند.

   الگوی سطح حساب عیناً همان CC_UnitAcc است: معین/تفصیلی خالی یعنی
   «همه‌ی زیرمجموعه‌های سطح بالاتر»، و Ratio برای وقتی است که فقط سهمی
   از یک حساب به این طبقه تعلق دارد. */
IF OBJECT_ID('dbo.CC_ExpenseAcc','U') IS NULL
CREATE TABLE dbo.CC_ExpenseAcc (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    ExpenseKind TINYINT       NOT NULL,   -- 1=فروش 2=اداری 3=مالی 4=ساير
    HesKol      INT           NOT NULL,
    HesMoin     INT           NULL,       -- خالی = همه معین‌های این کل
    HesTafsili  INT           NULL,       -- خالی = همه تفصیلی‌های همان معین
    Ratio       DECIMAL(9,6)  NOT NULL DEFAULT 1,
    IsActive    BIT           NOT NULL DEFAULT 1,
    Note        NVARCHAR(200) NULL,
    CONSTRAINT UQ_CC_ExpenseAcc UNIQUE (ExpenseKind, HesKol, HesMoin, HesTafsili)
);
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_FinancialStatements
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Month TINYINT, @DT1 BIGINT, @DT2 BIGINT, @Year SMALLINT;
    SELECT  @Month = PeriodMonth, @DT1 = DateFrom, @DT2 = DateTo, @Year = FiscalYear
    FROM    dbo.CC_Run WHERE RunId = @RunId;

    IF @DT1 IS NULL
    BEGIN
        RAISERROR(N'اجرا پیدا نشد.', 16, 1);
        RETURN;
    END

    ---- ارزش موجودی هر (انبار،کالا) در یک تاریخ ─────────────────────
    -- دو بار لازم است (ابتدا و پایان دوره)، پس یک جدول موقت با ستون
    -- Cut نگه می‌داریم به‌جای دو بلوک تکراری.
    IF OBJECT_ID('tempdb..#Inv') IS NOT NULL DROP TABLE #Inv;
    CREATE TABLE #Inv (Cut BIGINT, Anbar INT, Code BIGINT, Qty FLOAT, Rate FLOAT);

    DECLARE @Open BIGINT = @DT1 - 1;   -- تاریخ‌ها عدد YYYYMMDD اند؛ روزِ صفر
                                       -- وجود ندارد پس این همیشه بین دو ماه می‌افتد
    DECLARE @cut BIGINT, @i INT = 0;

    WHILE @i < 2
    BEGIN
        SET @cut = CASE WHEN @i = 0 THEN @Open ELSE @DT2 END;

        ;WITH Mv AS (
            -- ورودها
            SELECT il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS Code, il.MEGHk AS M
            FROM   dbo.INVO_LST il JOIN dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
            WHERE  il.TAG IN (1,7,9,24) AND h.DATE_N <= @cut
            UNION ALL
            SELECT CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.MEGHk
            FROM   dbo.INVO_LST il JOIN dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
            WHERE  il.TAG=5 AND il.ANBARF IS NOT NULL AND h.DATE_N <= @cut
            UNION ALL
            -- خروج‌ها
            SELECT il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGHk
            FROM   dbo.INVO_LST il JOIN dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
            WHERE  il.TAG IN (2,5,8,10,11,26) AND h.DATE_N <= @cut
            UNION ALL
            -- موجودی اول سال
            SELECT f.ANBAR, TRY_CAST(f.CODE AS BIGINT), f.MOGODI_A FROM dbo.STUF_FSK f
        ),
        Q AS (
            SELECT Anbar, Code, SUM(M) AS Qty FROM Mv
            WHERE Anbar IS NOT NULL AND Code IS NOT NULL
            GROUP BY Anbar, Code
        ),
        R AS (
            -- آخرین نرخ میانگینِ ثبت‌شده تا این تاریخ؛ تای‌برک عیناً
            -- همان چیزی که در دروازه‌ی S05 تصحیح شد (id نزولی).
            SELECT Anbar, Code, Rate,
                   ROW_NUMBER() OVER (PARTITION BY Anbar, Code
                                      ORDER BY DATE_N DESC, tartib DESC, NUMBER DESC, ID DESC) rn
            FROM (
                SELECT il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS Code,
                       il.AVRAGE AS Rate, h.DATE_N, t.tartib, il.NUMBER, il.ID
                FROM   dbo.INVO_LST il
                JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
                JOIN   dbo.TAGCOD t ON t.CODE=il.TAG
                WHERE  il.TAG IN (1,7,9,24) AND h.DATE_N <= @cut
                UNION ALL
                SELECT CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT),
                       il.AVRAGE2, h.DATE_N, t.tartib, il.NUMBER, il.ID
                FROM   dbo.INVO_LST il
                JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
                JOIN   dbo.TAGCOD t ON t.CODE=il.TAG
                WHERE  il.TAG=5 AND il.ANBARF IS NOT NULL AND h.DATE_N <= @cut
            ) x
        )
        INSERT #Inv (Cut, Anbar, Code, Qty, Rate)
        SELECT @cut, q.Anbar, q.Code, q.Qty,
               ISNULL(r.Rate, f.FI_A)
        FROM   Q q
        LEFT   JOIN R r ON r.Anbar=q.Anbar AND r.Code=q.Code AND r.rn=1
        LEFT   JOIN dbo.STUF_FSK f ON f.ANBAR=q.Anbar AND TRY_CAST(f.CODE AS BIGINT)=q.Code
        WHERE  ABS(q.Qty) > 0.0001;

        SET @i += 1;
    END

    ---- ارزش موجودی به تفکیک نقش انبار ─────────────────────────────
    IF OBJECT_ID('tempdb..#ByRole') IS NOT NULL DROP TABLE #ByRole;

    SELECT  i.Cut, ua.AnbarRole AS Role,
            SUM(ROUND(i.Qty, 2) * ISNULL(i.Rate, 0)) AS Val
    INTO    #ByRole
    FROM    #Inv i
    JOIN    (SELECT Anbar, MIN(AnbarRole) AS AnbarRole
             FROM dbo.CC_UnitAnbar GROUP BY Anbar) ua ON ua.Anbar = i.Anbar
    GROUP BY i.Cut, ua.AnbarRole;

    DECLARE @MatOpen  FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@Open AND Role IN (1,2)), 0),
            @MatClose FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@DT2  AND Role IN (1,2)), 0),
            @FgOpen   FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@Open AND Role = 3),     0),
            @FgClose  FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@DT2  AND Role = 3),     0);

    ---- خرید دوره: رسید خرید به انبارهای مواد
    DECLARE @Purchase FLOAT = ISNULL((
        SELECT SUM(il.MABL_K)
        FROM   dbo.INVO_LST il
        JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
        JOIN   (SELECT Anbar, MIN(AnbarRole) AS AnbarRole
                FROM dbo.CC_UnitAnbar GROUP BY Anbar) ua ON ua.Anbar = il.ANBAR
        WHERE  il.TAG = 1 AND ua.AnbarRole IN (1,2)
          AND  h.DATE_N BETWEEN @DT1 AND @DT2), 0);

    ---- نیمه‌ساخته‌ی بازگشتی: انتقال از انبار محصول به انبار مصرفی.
    -- سطر اطلاعی است، نه جزئی از مواد — دلیلش بالای همین فایل.
    DECLARE @Recirc FLOAT = ISNULL((
        SELECT SUM(il.MABL_K)
        FROM   dbo.INVO_LST il
        JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
        JOIN   (SELECT Anbar, MIN(AnbarRole) AS AnbarRole FROM dbo.CC_UnitAnbar GROUP BY Anbar) us
               ON us.Anbar = il.ANBAR
        JOIN   (SELECT Anbar, MIN(AnbarRole) AS AnbarRole FROM dbo.CC_UnitAnbar GROUP BY Anbar) ud
               ON ud.Anbar = CAST(il.ANBARF AS INT)
        WHERE  il.TAG = 5 AND us.AnbarRole = 3 AND ud.AnbarRole = 1
          AND  h.DATE_N BETWEEN @DT1 AND @DT2), 0);

    ---- دستمزد و سربارِ واقعی
    DECLARE @Wage FLOAT = ISNULL((SELECT SUM(ActualAmount) FROM dbo.CC_ConversionCost
                                  WHERE RunId=@RunId AND CostKind=1), 0),
            @Oh   FLOAT = ISNULL((SELECT SUM(ActualAmount) FROM dbo.CC_ConversionCost
                                  WHERE RunId=@RunId AND CostKind=2), 0);

    DECLARE @MatAvail FLOAT = @MatOpen + @Purchase,
            @MatUsed  FLOAT = @MatOpen + @Purchase - @MatClose;
    DECLARE @MfgCost  FLOAT = @MatUsed + @Wage + @Oh;
    DECLARE @COGM     FLOAT = @MfgCost;          -- WIP جدا نگه‌داری نمی‌شود
    DECLARE @COGS     FLOAT = @FgOpen + @COGM - @FgClose;

    ---- ارقام سود و زیان از S12
    DECLARE @Sales FLOAT = ISNULL((SELECT SUM(SalesAmount) FROM dbo.CC_ItemMargin WHERE RunId=@RunId), 0),
            @S12Cost FLOAT = ISNULL((SELECT SUM(CostAmount) FROM dbo.CC_ItemMargin WHERE RunId=@RunId), 0);

    /* ═══ ۱) صورت بهای تمام‌شده کالای ساخته‌شده ═══ */
    SELECT * FROM (VALUES
        (10, N'موجودی اول دوره مواد',                @MatOpen,  0),
        (20, N'خرید مواد طی دوره',                   @Purchase, 0),
        (30, N'مواد آماده مصرف',                     @MatAvail, 1),
        (40, N'کسر: موجودی پایان دوره مواد',         -@MatClose, 0),
        (50, N'مواد مصرف‌شده',                        @MatUsed,  1),
        (60, N'دستمزد',                              @Wage,     0),
        (70, N'سربار ساخت',                          @Oh,       0),
        (80, N'بهای تمام‌شده کالای ساخته‌شده',        @COGM,     2),
        (90, N'ــ اطلاعی: نیمه‌ساخته بازگشتی به تولید', @Recirc, 3)
    ) v(ردیف, شرح, مبلغ, نوع)
    ORDER BY ردیف;

    /* ═══ ۲) صورت بهای تمام‌شده کالای فروش‌رفته ═══ */
    SELECT * FROM (VALUES
        (10, N'موجودی اول دوره کالای ساخته‌شده',      @FgOpen,  0),
        (20, N'بهای تمام‌شده کالای ساخته‌شده',        @COGM,    0),
        (30, N'کالای آماده فروش',                    @FgOpen + @COGM, 1),
        (40, N'کسر: موجودی پایان دوره کالای ساخته‌شده', -@FgClose, 0),
        (50, N'بهای تمام‌شده کالای فروش‌رفته',        @COGS,    2),
        (60, N'ــ تطبیق: بهای فروش‌رفته طبق سود و زیان', @S12Cost, 3),
        (70, N'ــ اختلاف',                            @COGS - @S12Cost, 3)
    ) v(ردیف, شرح, مبلغ, نوع)
    ORDER BY ردیف;

    /* ═══ ۳) صورت سود و زیان ═══
       هزینه‌های دوره از CC_ExpenseAcc می‌آیند. مانده‌ی هر سرفصل عیناً
       مثل CC_UnitAcc حساب می‌شود: بدهکار منهای بستانکار در بازه‌ی همین
       اجرا، ضربدر Ratio. سرفصلی که تعریف نشده باشد صفر می‌ماند و سطرش
       هم نمایش داده می‌شود تا معلوم باشد جایش خالی است، نه اینکه بی‌صدا
       از صورت حذف شود. */
    IF OBJECT_ID('tempdb..#Exp') IS NOT NULL DROP TABLE #Exp;

    SELECT  m.ExpenseKind AS Kind,
            ISNULL(SUM(t.Amount * m.Ratio), 0) AS Amount
    INTO    #Exp
    FROM    dbo.CC_ExpenseAcc m
    CROSS   APPLY (
                SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                FROM   dbo.DEED_DTL d
                JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                  AND  d.HES_K = m.HesKol
                  AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                  AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
            ) t
    WHERE   m.IsActive = 1
    GROUP BY m.ExpenseKind;

    DECLARE @ExpSell  FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=1), 0),
            @ExpAdmin FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=2), 0),
            @ExpFin   FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=3), 0),
            @ExpOther FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=4), 0);

    DECLARE @Gross FLOAT = @Sales - @S12Cost;
    DECLARE @ExpAll FLOAT = @ExpSell + @ExpAdmin + @ExpFin + @ExpOther;

    SELECT * FROM (VALUES
        (10, N'فروش خالص',                           @Sales,   0),
        (20, N'کسر: بهای تمام‌شده کالای فروش‌رفته',   -@S12Cost, 0),
        (30, N'سود ناخالص',                          @Gross,   1),
        (40, N'کسر: هزینه‌های فروش',                 -@ExpSell,  0),
        (50, N'کسر: هزینه‌های اداری',                -@ExpAdmin, 0),
        (60, N'کسر: هزینه‌های مالی',                 -@ExpFin,   0),
        (70, N'کسر: سایر هزینه‌ها',                  -@ExpOther, 0),
        (80, N'جمع هزینه‌های دوره',                  -@ExpAll,   1),
        (90, N'سود عملیاتی',                         @Gross - @ExpAll, 2),
        (100, N'ــ درصد سود ناخالص',
             CASE WHEN @Sales <> 0 THEN ROUND(@Gross / @Sales * 100, 1) END, 3),
        (110, N'ــ درصد سود عملیاتی',
             CASE WHEN @Sales <> 0 THEN ROUND((@Gross - @ExpAll) / @Sales * 100, 1) END, 3)
    ) v(ردیف, شرح, مبلغ, نوع)
    ORDER BY ردیف;

    /* ═══ ۴) تفکیک سرفصل‌های هزینه — تا معلوم باشد هر رقم از کجا آمده ═══ */
    SELECT  CASE m.ExpenseKind WHEN 1 THEN N'فروش' WHEN 2 THEN N'اداری'
                               WHEN 3 THEN N'مالی' ELSE N'ساير' END AS طبقه,
            m.HesKol      AS کل,
            m.HesMoin     AS معین,
            m.HesTafsili  AS تفصیلی,
            m.Ratio       AS ضریب,
            ISNULL(t.Amount, 0)            AS مانده_حساب,
            ISNULL(t.Amount, 0) * m.Ratio  AS سهم_این_طبقه,
            m.Note        AS یادداشت
    FROM    dbo.CC_ExpenseAcc m
    CROSS   APPLY (
                SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                FROM   dbo.DEED_DTL d
                JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                  AND  d.HES_K = m.HesKol
                  AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                  AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
            ) t
    WHERE   m.IsActive = 1
    ORDER BY m.ExpenseKind, m.HesKol, m.HesMoin, m.HesTafsili;
END
GO

PRINT N'رويه CC_sp_FinancialStatements ايجاد شد.';
GO
