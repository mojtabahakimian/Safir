/* ═══════════════════════════════════════════════════════════════════
   CHK-04 — وقتی کالا برای ماهِ جاری هیچ فرمولی ندارد

   ── مسئله ──
   اصلاح خودکارِ موجود (CC_sp_Fix_MissingFormula) فقط وقتی کار می‌کند که
   فرمولی با GHEYMAT برابرِ ماهِ جاری از قبل وجود داشته باشد؛ کارش صرفاً
   نسبت‌دادنِ آن به برگه‌های تولید است. اگر چنین فرمولی نباشد،
   CanAutoFix=0 می‌شود و کاربر هیچ راهی ندارد.

   نمونه‌ی واقعی (تیر ۱۴۰۵): کد ۲۸۱۲ «پنیر پیتزا پامپارو ۱۸۰ گرمی
   شادنوش» یازده فرمول دارد — ماه‌های ۰، ۲، ۳، ۵، ۷ تا ۱۲ — ولی برای
   ماه ۴ هیچ‌کدام.

   ── چرا «نسبت دادنِ فرمولِ ماه دیگر» جواب نمی‌دهد ──
   خودِ شرطِ CHK-04 این است که برگه به فرمولی با GHEYMAT = ماهِ جاری
   اشاره کند. اگر برگه را به فرمولِ ماه ۳ وصل کنیم، کنترل همچنان مغایرت
   نشان می‌دهد و S11 هم نرخِ ماه را روی آن منتشر نمی‌کند. پس فرمول باید
   به ماهِ جاری **کپی** شود، نه فقط اشاره داده شود.

   ── قاعده‌ی صاحب پروژه ──
   «فرمول را از ماه قبل بگیرد؛ اگر ماه قبل نداشت، لیست فرمول‌های آن کالا
   بدون توجه به ماه را بیاورد و کاربر خودش انتخاب کند.»

   پس CC_sp_FormulaOptions همه‌ی فرمول‌های کالا را با رتبه‌ی پیشنهاد
   برمی‌گرداند (ماه قبل اول)، و انتخاب نهایی با کاربر است.

   ── نکته‌ی مهم درباره‌ی GHEYMAT ──
   GHEYMAT فقط شماره‌ی ماه است (۱ تا ۱۲)، بدون سال — فرمول‌ها قالبِ
   ماهانه‌اند و بین سال‌ها دوباره استفاده می‌شوند. روی داده‌ی واقعی،
   فرمولِ «ماه ۳»ِ کد ۲۸۱۲ تاریخ فعال‌سازی ۱۴۰۴/۰۳/۲۰ دارد و برای خرداد
   ۱۴۰۵ هم همان به کار می‌رود. پس «ماه قبل» یعنی GHEYMAT = @Month - 1،
   و برای فروردین یعنی ۱۲ (اسفند).

   ── دستمزد و سربار ──
   IMBIBE_MANF/IMBIBE_SAR عیناً از فرمولِ مبدأ کپی می‌شوند و دست‌کاری
   نمی‌شوند؛ S07B بعداً خودش نرخِ واقعیِ ماه را رویشان می‌نشاند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────────────────────────────────────────────────────────
   فهرست فرمول‌های یک کالا، برای انتخاب کاربر
   ─────────────────────────────────────────────────────────────────── */
CREATE OR ALTER PROCEDURE dbo.CC_sp_FormulaOptions
    @Code  BIGINT,
    @Month TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    -- ماهِ قبل، با چرخشِ سال: قبلِ فروردین، اسفند است.
    DECLARE @Prev TINYINT = CASE WHEN @Month <= 1 THEN 12 ELSE @Month - 1 END;

    SELECT  hm.FNUMB                                   AS Fnumb,
            CAST(hm.GHEYMAT AS INT)                    AS Mah,
            hm.DATE_ACTIV                              AS DateActiv,
            hm.IMBIBE_MANF                             AS Wage,
            hm.IMBIBE_SAR                              AS Overhead,
            (SELECT COUNT(*) FROM dbo.DTL_MANF d WHERE d.FNUMB = hm.FNUMB) AS LineCount,
            CASE WHEN CAST(hm.GHEYMAT AS INT) = @Prev THEN 1 ELSE 0 END    AS IsPrevMonth,
            -- آیا این فرمول در ماهِ خودش واقعاً استفاده شده؟ فرمولی که
            -- هیچ‌وقت تولیدی نداشته احتمالاً متروک است و نباید اول
            -- پیشنهاد شود.
            CASE WHEN EXISTS (SELECT 1 FROM dbo.INVO_LST pl
                              WHERE pl.TAG = 9 AND TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB)
                 THEN 1 ELSE 0 END                     AS EverUsed
    FROM    dbo.HEAD_MANF hm
    WHERE   TRY_CAST(hm.CODE AS BIGINT) = @Code
      -- فرمولی که از قبل مالِ همین ماه است اینجا بی‌معناست: در آن حالت
      -- اصلاً کپی لازم نیست و CC_sp_Fix_MissingFormula کار می‌کند.
      AND   CAST(hm.GHEYMAT AS INT) <> @Month
    ORDER BY
            -- ۱) ماه قبل، همان چیزی که صاحب پروژه پیش‌فرض خواست
            CASE WHEN CAST(hm.GHEYMAT AS INT) = @Prev THEN 0 ELSE 1 END,
            -- ۲) فرمولی که واقعاً استفاده شده
            CASE WHEN EXISTS (SELECT 1 FROM dbo.INVO_LST pl
                              WHERE pl.TAG = 9 AND TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB)
                 THEN 0 ELSE 1 END,
            -- ۳) تازه‌ترین
            hm.DATE_ACTIV DESC, hm.FNUMB DESC;
END
GO


/* ───────────────────────────────────────────────────────────────────
   کپیِ یک فرمول به ماهِ جاری و نسبت‌دادنش به برگه‌های تولید
   ─────────────────────────────────────────────────────────────────── */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_CopyFormulaToMonth
    @Code         BIGINT,
    @Month        TINYINT,
    @SourceFnumb  INT,
    @DT1          BIGINT,
    @DT2          BIGINT,
    @RunId        INT          = NULL,
    @ExceptionId  BIGINT       = NULL,
    @UserName     NVARCHAR(50) = N'system',
    @WhatIf       BIT          = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- اعتبارسنجی
    IF NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF
                   WHERE FNUMB = @SourceFnumb AND TRY_CAST(CODE AS BIGINT) = @Code)
    BEGIN
        RAISERROR(N'فرمول انتخاب‌شده متعلق به این کالا نیست.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.HEAD_MANF
               WHERE TRY_CAST(CODE AS BIGINT) = @Code AND CAST(GHEYMAT AS INT) = @Month)
    BEGIN
        RAISERROR(N'این کالا برای این ماه از قبل فرمول دارد؛ از «اصلاح خودکار» استفاده کنید، نه کپی.', 16, 1);
        RETURN;
    END

    ---- برگه‌های تولیدی که باید به فرمول تازه وصل شوند
    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- کلید تطبیق id است نه (NUMBER, RADIF) — به همان دلیلی که در
    -- CC_sp_Fix_MissingFormula مستند شده: RADIF می‌تواند NULL باشد.
    SELECT  pl.id      AS InvoId,
            h.NUMBER   AS ProdNo,
            h.DATE_N   AS ProdDate,
            pl.N_KOL   AS OldFnumb,
            pl.MEGHK   AS Meghdar
    INTO    #Rows
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.CODE AS BIGINT) = @Code
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                          AND CAST(hm.GHEYMAT AS INT) = @Month);

    DECLARE @n INT = (SELECT COUNT(*) FROM #Rows);

    IF @WhatIf = 1
    BEGIN
        SELECT  r.ProdNo   AS شماره_برگه,
                r.ProdDate AS تاریخ,
                @Code      AS کد_کالا,
                r.OldFnumb AS فرمول_فعلی,
                r.Meghdar  AS مقدار
        FROM    #Rows r
        ORDER BY r.ProdNo;

        -- نامِ ستون‌ها عمداً بدون «أ»: کاراکترهای همزه‌دار در identifierهای
        -- SQL وقتی فایل بدون codepage درست خوانده شود خطای نحوی می‌سازند.
        SELECT  @n           AS تعداد_سطر_قابل_اصلاح,
                @SourceFnumb AS فرمول_مبدا,
                (SELECT CAST(GHEYMAT AS INT) FROM dbo.HEAD_MANF WHERE FNUMB = @SourceFnumb)
                             AS ماه_مبدا,
                (SELECT COUNT(*) FROM dbo.DTL_MANF WHERE FNUMB = @SourceFnumb)
                             AS تعداد_ردیف_فرمول,
                N'حالت گزارش — چیزی تغییر نکرد' AS وضعیت;
        RETURN;
    END

    BEGIN TRAN;

    ---- شماره‌ی فرمول تازه.
    -- FNUMB کلید اصلیِ غیرهویتی است و نرم‌افزار قدیمی هم با MAX+1 جلو
    -- می‌رود. UPDLOCK/HOLDLOCK جلوی گرفتنِ شماره‌ی تکراری توسط دو کاربر
    -- هم‌زمان را می‌گیرد.
    DECLARE @NewFnumb INT;
    SELECT  @NewFnumb = ISNULL(MAX(FNUMB), 0) + 1
    FROM    dbo.HEAD_MANF WITH (UPDLOCK, HOLDLOCK);

    -- نامِ ماهِ مقصد. CHOOSE با ايندکس ۱ شروع مي‌شود، پس @Month مستقيماً
    -- مي‌نشيند. اگر مقدارِ بيرون از ۱..۱۲ بيايد NULL برمي‌گردد و TOZIH
    -- خالي مي‌ماند — بهتر از نوشتنِ نامِ ماهِ اشتباه.
    DECLARE @MonthName NVARCHAR(20) = CHOOSE(@Month,
        N'فروردین', N'اردیبهشت', N'خرداد', N'تیر', N'مرداد', N'شهریور',
        N'مهر', N'آبان', N'آذر', N'دی', N'بهمن', N'اسفند');

    ---- سربرگ فرمول
    -- DATE_ACTIV روی اولین روزِ همین دوره می‌نشیند تا فرمول از ابتدای ماه
    -- معتبر باشد؛ اگر تاریخِ مبدأ کپی شود، فرمول «از آینده» یا «از سالِ
    -- قبل» به نظر می‌رسد و گزارش‌های تاریخی را گمراه می‌کند.
    INSERT dbo.HEAD_MANF
        (FNUMB, CODE, DATE_ACTIV, IMBIBE_MANF, IMBIBE_SAR, GHEYMAT,
         NAMES, N_KOL, NUMBER, TNUMBER, SA_HOUR, SA_NHOU, TOZIH, CRT, UID)
    SELECT  @NewFnumb, hm.CODE, @DT1, hm.IMBIBE_MANF, hm.IMBIBE_SAR, @Month,
            hm.NAMES, hm.N_KOL, hm.NUMBER, hm.TNUMBER, hm.SA_HOUR, hm.SA_NHOU,
            -- توضیحات فقط نامِ ماهِ *مقصد*. قبلاً توضیحِ فرمولِ مبدأ به
            -- اضافه‌ی «[کپی از فرمول ۱۲۳ ماه ۴]» نوشته می‌شد؛ روی کالایی
            -- که چند بار کپی می‌شد این رشته هر بار درازتر و ناخواناتر
            -- می‌شد و ماهِ نوشته‌شده هم ماهِ مبدأ بود نه ماهی که فرمول
            -- برایش ساخته شده — یعنی دقیقاً برعکسِ چیزی که خواننده
            -- می‌خواهد بداند.
            @MonthName,
            GETDATE(), NULL
    FROM    dbo.HEAD_MANF hm
    WHERE   hm.FNUMB = @SourceFnumb;

    ---- ردیف‌های فرمول
    INSERT dbo.DTL_MANF
        (FNUMB, CODE, ANBAR, VAHED_K, MEGH, MEGHk, PERT, SMABL, MABLK, TOZIH, CRT, UID)
    SELECT  @NewFnumb, d.CODE, d.ANBAR, d.VAHED_K, d.MEGH, d.MEGHk, d.PERT,
            d.SMABL, d.MABLK, d.TOZIH, GETDATE(), NULL
    FROM    dbo.DTL_MANF d
    WHERE   d.FNUMB = @SourceFnumb;

    ---- وصل کردن برگه‌های تولید به فرمول تازه
    DECLARE @applied INT = 0;

    UPDATE  pl
       SET  pl.N_KOL = @NewFnumb
    FROM    dbo.INVO_LST pl
    JOIN    #Rows r ON r.InvoId = pl.id;

    SET @applied = @@ROWCOUNT;

    ---- بستن استثنا
    UPDATE  e
       SET  e.IsResolved = 1, e.ResolvedBy = @UserName, e.ResolvedAtUtc = SYSUTCDATETIME(),
            e.ResolutionNote = CONCAT(N'فرمول ', @SourceFnumb, N' به ماه ', @Month,
                                      N' کپی شد (فرمول تازه ', @NewFnumb, N') و ',
                                      @applied, N' برگه به آن وصل شد.')
    FROM    dbo.CC_Exception e
    WHERE   e.RuleCode = 'CHK-04'
      AND   e.Code = @Code
      AND   ISNULL(e.RunId, -1) = ISNULL(@RunId, -1)
      AND   (@ExceptionId IS NULL OR e.ExceptionId = @ExceptionId);

    ---- خروج مواد باید بازسازی شود، چون فرمولِ برگه عوض شد
    IF @RunId IS NOT NULL AND @applied > 0
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT  @applied  AS تعداد_سطر_اصلاح_شده,
            @NewFnumb AS فرمول_جدید,
            @n        AS تعداد_سطر_نامزد;
END
GO

PRINT N'رويه‌هاي CC_sp_FormulaOptions و CC_sp_Fix_CopyFormulaToMonth ايجاد شدند.';
GO
