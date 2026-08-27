/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۳ از ۳ : رویه‌ها

   مدیریت اجرا، اسنپ‌شات و بازگردانی، و گام‌های S00 تا S04.
   قابل اجرای مکرر (همه با CREATE OR ALTER).

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، رویه‌هایی که به CC_ItemCost/CC_ItemMargin می‌نویسند
-- (ستون‌های محاسباتی PERSISTED) با خطای 1934 شکست می‌خورند.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════ مدیریت اجرا ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_RunCreate
    @FiscalYear SMALLINT,
    @Month      TINYINT,
    @DateFrom   BIGINT,
    @DateTo     BIGINT,
    @RunKind    TINYINT,          -- 1=آزمايشي 2=قطعي
    @UserName   NVARCHAR(50),
    @Note       NVARCHAR(500) = NULL,
    @RunId      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
                 AND RunKind = 2 AND Status = 3 AND ApprovedAtUtc IS NOT NULL)
    BEGIN
        RAISERROR(N'براي اين ماه يک اجراي قطعي تأييدشده وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month AND Status = 1)
    BEGIN
        RAISERROR(N'يک اجرا براي اين ماه در حال انجام است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @no SMALLINT =
        ISNULL((SELECT MAX(RunNo) FROM dbo.CC_Run
                WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month), 0) + 1;

    DECLARE @prev INT =
        (SELECT TOP 1 RunId FROM dbo.CC_Run
         WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
         ORDER BY RunNo DESC);

    UPDATE dbo.CC_Run SET IsLatest = 0
    WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month;

    INSERT dbo.CC_Run (FiscalYear, PeriodMonth, DateFrom, DateTo, RunNo,
                       PrevRunId, IsLatest, RunKind, Status, StartedByUser, Note)
    VALUES (@FiscalYear, @Month, @DateFrom, @DateTo, @no,
            @prev, 1, @RunKind, 0, @UserName, @Note);

    SET @RunId = SCOPE_IDENTITY();

    INSERT dbo.CC_RunLog (RunId, Severity, Message)
    VALUES (@RunId, 1, CONCAT(N'اجراي شماره ', @no, N' براي دوره ',
                              @FiscalYear, '/', @Month, N' ايجاد شد'));

    COMMIT;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepStart
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Title    NVARCHAR(120),
    @SeqNo    SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @try TINYINT =
        ISNULL((SELECT MAX(Attempt) FROM dbo.CC_RunStep
                WHERE RunId = @RunId AND StepCode = @StepCode), 0) + 1;

    INSERT dbo.CC_RunStep (RunId, StepCode, StepTitle, SeqNo, Attempt, Status, StartedAtUtc)
    VALUES (@RunId, @StepCode, @Title, @SeqNo, @try, 1, SYSUTCDATETIME());

    UPDATE dbo.CC_Run
       SET Status = 1, StartedAtUtc = ISNULL(StartedAtUtc, SYSUTCDATETIME())
     WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepFinish
    @RunId     INT,
    @StepCode  VARCHAR(10),
    @Status    TINYINT,                     -- 2=موفق 3=هشدار 4=خطا 5=رد‌شده
    @Rows      INT           = NULL,
    @Result    NVARCHAR(MAX) = NULL,
    @Error     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE  s
       SET  s.Status        = @Status,
            s.FinishedAtUtc = SYSUTCDATETIME(),
            s.DurationMs    = DATEDIFF(MILLISECOND, s.StartedAtUtc, SYSUTCDATETIME()),
            s.RowsAffected  = @Rows,
            s.ResultJson    = @Result,
            s.ErrorMessage  = @Error
    FROM    dbo.CC_RunStep s
    JOIN   (SELECT RunId, StepCode, MAX(Attempt) AS Attempt
            FROM   dbo.CC_RunStep
            WHERE  RunId = @RunId AND StepCode = @StepCode
            GROUP BY RunId, StepCode) x
           ON x.RunId = s.RunId AND x.StepCode = s.StepCode AND x.Attempt = s.Attempt;

    IF @Status = 4
        UPDATE dbo.CC_Run SET Status = 4 WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_SetFormulasDirty
    @RunId INT, @Dirty BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.CC_Run SET FormulasDirty = @Dirty WHERE RunId = @RunId;
END
GO


/* ═══════════════ اسنپ‌شات و بازگردانی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_Snapshot
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Month    TINYINT,
    @DT1      BIGINT,
    @DT2      BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @bak SYSNAME, @sql NVARCHAR(MAX), @n INT;

    ---- DTL_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_DTL_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT d.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DTL_MANF d
                 JOIN dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DTL_MANF', @bak, @n);

    ---- HEAD_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_HEAD_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT h.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.HEAD_MANF h WHERE h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'HEAD_MANF', @bak, @n);

    ---- DEED_HED : اسنپ‌شات کامل اسناد بازه، به‌همراه اسناد پس از @DT2 هم —
    -- چون شاخهٔ جابه‌جايي CC_sp_S04_SortDeeds مي‌تواند شمارهٔ اسناد بعد از
    -- پايان ماه را هم عوض کند تا با شمارهٔ تازهٔ اسناد اين ماه تلاقي نکند؛
    -- اگر آن اسناد اينجا اسنپ‌شات نشوند، Rollback راهي براي برگرداندن
    -- شماره‌شان ندارد. ستون‌ها هم کامل ذخيره مي‌شوند (نه فقط base/N_S/DATE_S)
    -- تا اگر CC_sp_S03_DeleteEmptyDeeds سندي را کامل حذف کرد، Rollback
    -- بتواند کل سطر را دوباره درج کند، نه فقط شماره‌اش را برگرداند.
    SET @bak = CONCAT('CC_BAK_DEED_HED_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT * INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DEED_HED WHERE DATE_S >= @a';
    EXEC sp_executesql @sql, N'@a BIGINT', @a = @DT1;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DEED_HED', @bak, @n);

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 1, N'اسنپ‌شات گرفته شد');

    SELECT TableName AS جدول, BackupTable AS جدول_پشتيبان, RowsCopied AS تعداد_سطر
    FROM   dbo.CC_Snapshot
    WHERE  RunId = @RunId AND StepCode = @StepCode;
END
GO


/* ═══════════════ S00 — بازبینی ابتدای ماه ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S00_Preflight
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  StepCode = 'S00' AND ISNULL(RunId, -1) = ISNULL(@RunId, -1);

    ---- CHK-03 : فرمول بدون نرخ جذب
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-03', 9, r.DefaultSeverity,
            MIN(CAST(pl.CODE AS BIGINT)), hm.FNUMB, MAX(h.DATE_N), SUM(pl.MEGHK),
            CONCAT(N'فرمول ', hm.FNUMB, N' نرخ جذب هزينه تبديل ندارد')
    FROM    dbo.HEAD_LST  h
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
    CROSS   JOIN dbo.CC_CheckRule r
    WHERE   r.RuleCode = 'CHK-03' AND r.IsActive = 1
      AND   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(hm.IMBIBE_MANF,0) + ISNULL(hm.IMBIBE_SAR,0) = 0
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-03' AND ae.IsActive = 1
                          AND (ae.Code IS NULL
                               OR ae.Code = CAST(pl.CODE AS BIGINT)))
    GROUP BY hm.FNUMB, r.DefaultSeverity;

    ---- CHK-04 : کالاي توليدشده بدون فرمول ماه
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-04', 12, 2, CAST(pl.CODE AS BIGINT),
            N'کالا در اين ماه توليد شده ولي فرمول ماه را ندارد'
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                          AND hm.GHEYMAT = @Month);

    ---- CHK-05 : ماده بدون منبع نرخ
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-05', 4, 1, CAST(d.CODE AS BIGINT),
            N'ماده بدون منبع نرخ — نرخ صفر به کالاهاي بالادست منتقل مي‌شود'
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF p
                        WHERE CAST(p.CODE AS BIGINT) = CAST(d.CODE AS BIGINT)
                          AND p.GHEYMAT = @Month)
      AND   NOT EXISTS (SELECT 1 FROM dbo.KALAS k
                        WHERE k.code = CAST(d.CODE AS BIGINT)
                          AND k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0);

    ---- CHK-15 : فرمول با مقدار منفی
    -- مقدار منفی در فرمول یعنی مانده حساب کالای در جریان ساخت (۷۵۱) هرگز
    -- متوازن نمی‌شود (CHK-07)؛ چون خروج مواد از روی همین عدد بازتولید
    -- می‌شود. کد سطر (DTL_MANF.id) در DocNumber ذخیره می‌شود تا اصلاح
    -- خودکار دقیقاً همان سطر را هدف بگیرد.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-15', 17, 2, CAST(d.CODE AS BIGINT),
            CAST(d.id AS INT), d.MEGH,
            CONCAT(N'فرمول ', h.FNUMB, N' مقدار منفی دارد: ', d.MEGH)
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   d.MEGH < 0 OR d.MEGHk < 0;

    ---- CHK-16 : برگه تولید به انباري که به هيچ واحد توليدي (نقش «محصول»)
    -- وصل نيست — بدون اين تشخيص، S10 اين برگه‌ها را در محاسبه جذب هيچ
    -- واحدي نمي‌بيند و مانده حساب ۷۵۱ کاذب مي‌شود (دقيقاً همان چيزي که
    -- روي انبار ۱۵ رخ داد و کاربر تأييد کرد بايد به‌صورت خودکار
    -- روي هر پايگاه‌داده‌ي جديد هم چک شود).
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, DocDate, Description)
    SELECT DISTINCT @RunId, 'S00', 'CHK-16', 18, 1,
           TRY_CAST(pl.CODE AS BIGINT), h.NUMBER, h.DATE_N,
           CONCAT(N'برگه تولید شماره ', h.NUMBER, N' به انبار ', pl.ANBAR,
                  N' وارد شده که به هیچ واحد تولیدی (نقش «محصول») وصل نیست')
    FROM   dbo.HEAD_LST h
    JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND  pl.ANBAR IS NOT NULL
      AND  NOT EXISTS (SELECT 1 FROM dbo.CC_UnitAnbar ua
                        JOIN dbo.CC_Unit u ON u.UnitId = ua.UnitId
                        WHERE ua.Anbar = pl.ANBAR AND ua.AnbarRole = 3 AND u.IsActive = 1);

    ---- CHK-17 : شمارش دوم/سوم انبارگردانی بدون مغایرت شمارش اول
    -- طبق فرآیند واقعی انبارگردانی (تأیید کاربر): کالایی که شمارش اول
    -- (NUM1) آن با موجودی سیستم (MOG) برابر است، اصلاً نباید وارد دور
    -- دوم/سوم شمارش شود؛ NUM2/NUM3 فقط برای کالاهایی پر می‌شود که شمارش
    -- اول‌شان مغایرت داشته. اگر با این حال NUM2 یا NUM3 مقدار داشته باشد،
    -- یعنی عدد در ستون اشتباهی ثبت شده — دقیقاً همان چیزی که روی کد
    -- ۳۷۴ (شیر خام)، برگه انبارگردانی ۱۰۴ پیدا شد: MOG=0، NUM1=0 (بدون
    -- مغایرت)، ولی NUM3=29633 — این عدد از راه (MOG-NUM3) وارد موتور نرخ
    -- می‌شود و مقدار پایان‌دوره‌ی کالا را در همان انبار به‌کلی غلط می‌کند.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Anbar, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-17', 19, 2,
            TRY_CAST(al.CODE AS BIGINT), ah.GRD_ANBAR, ah.GRD_NUM, ah.GRD_DATE,
            CASE WHEN ISNULL(al.NUM3,0) <> 0 THEN al.NUM3 ELSE al.NUM2 END,
            CONCAT(N'برگه انبارگردانی ', ah.GRD_NUM, N' / انبار ', ah.GRD_ANBAR,
                   N': شمارش اول (', al.NUM1, N') با موجودی سیستم (', al.MOG,
                   N') برابر بوده ولی شمارش ', CASE WHEN ISNULL(al.NUM3,0) <> 0 THEN N'سوم' ELSE N'دوم' END,
                   N' مقدار دارد (', CASE WHEN ISNULL(al.NUM3,0) <> 0 THEN al.NUM3 ELSE al.NUM2 END,
                   N') — احتمالاً در ستون اشتباه ثبت شده.')
    FROM    dbo.ANBGRD_LST  al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.GRD_DATE BETWEEN @DT1 AND @DT2
      AND   ah.N_S IS NOT NULL
      AND   al.NUM1 IS NOT NULL AND al.NUM1 = al.MOG
      AND   (ISNULL(al.NUM2,0) <> 0 OR ISNULL(al.NUM3,0) <> 0)
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-17' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = ah.GRD_ANBAR)
                          AND (ae.Code  IS NULL OR ae.Code  = TRY_CAST(al.CODE AS BIGINT)));

    /* ─── CHK-18 : فاکتور/برگشت در ماهی متفاوت از حواله/رسید یا سند اصلی ───
       طبق دستور کاربر (پیدا شده از راه فاکتور فروش ۲۴۶۵/کد ۳۴۰۲: تاریخ
       فاکتور ۲۸/۲ ولی حواله‌ی انبار ۲۰/۳): وقتی سند فاکتور (یا برگشت) و
       سند فیزیکیِ متناظرش در دو ماهِ شمسیِ متفاوت ثبت شده‌اند، معلوم
       نیست کدام درست است — باید اپراتور تصمیم بگیرد، نه بازسازی خودکار.

       ⚠️ معیار ابتدا «بیش از ۳۰ روز فاصله» بود، ولی نمونه‌ی محرکِ همین
       قاعده (فاکتور ۲۴۶۵) فقط ۲۳ روز واقعی فاصله دارد (۲۸ اردیبهشت تا
       ۲۰ خرداد) — چون این دو تاریخ درست کنارِ مرز ماه افتاده‌اند، نه
       چون فاصله‌ی زیادی دارند. معیار درست، طبق تأیید کاربر، «ماهِ شمسیِ
       متفاوت» است، نه شمارشِ روز — دقیقاً همان چیزی که برای بستنِ ماه
       اهمیت دارد (کدام ماهِ حسابداری صاحبِ این سند است). DATE_N به‌صورت
       عدد فشرده‌ی YYYYMMDD ذخیره می‌شود، پس DATE_N/100 دقیقاً YYYYMM
       (سال+ماه) را می‌دهد — تقسیم صحیح، بدون نیاز به تبدیل تقویم.

       RefList حاوی یک JSON کوچک است («سند الف»/«سند ب» و جدول/شماره/برچسبِ
       هرکدام) تا دکمه‌ی اصلاح بتواند دقیقاً بفهمد کدام ردیف از کدام جدول
       را باید به تاریخ دیگری تغییر دهد — بدون این، برای هر نوع سند
       (فاکتور فروش، برگشت فروش، برگشت خرید) باید منطق جدا نوشته می‌شد.
       خرید (TAG=۱) عمداً اینجا نیست: بر خلاف فروش، اینجا فاکتور خرید و
       رسید انبار یک سند واحدند (یک تاریخ)، نه دو سند جدا برای مقایسه. */
    ;WITH DateDrift AS (
        -- فاکتور فروش (TAG=13) در برابر حواله انبار فروش (TAG=2)
        -- ⚠️ NUMBER در HEAD_LST/BACK_HEAD از نوع FLOAT است؛ بدون CAST به BIGINT،
        -- FOR JSON PATH پایین‌تر آن را به نماد علمی (مثلاً «۲.۴۶۵e+۳») می‌نویسد
        -- که در سمت C# به‌عنوان long قابل‌خواندن نیست.
        SELECT  N'sale' AS Kind,
                CAST(inv.NUMBER AS BIGINT) AS ANumber, 13 AS ATag, N'HEAD_LST' AS ATable, inv.DATE_N AS ADate,
                CAST(vch.NUMBER AS BIGINT) AS BNumber, 2  AS BTag, N'HEAD_LST' AS BTable, vch.DATE_N AS BDate,
                CONCAT(N'فاکتور فروش ', inv.NUMBER, N': تاریخ فاکتور ',
                       FORMAT(inv.DATE_N,'0000/00/00'), N' با تاریخ حواله انبار ',
                       FORMAT(vch.DATE_N,'0000/00/00'), N' در ماه متفاوتی ثبت شده‌اند')
        AS Description,
                CASE WHEN inv.DATE_N/100 <> vch.DATE_N/100 THEN 1 ELSE 0 END AS DifferentMonth
        FROM    dbo.HEAD_LST inv
        JOIN    dbo.HEAD_LST vch ON vch.NUMBER = inv.NUMBER AND vch.TAG = 2
        WHERE   inv.TAG = 13
          AND   (inv.DATE_N BETWEEN @DT1 AND @DT2 OR vch.DATE_N BETWEEN @DT1 AND @DT2)

        UNION ALL
        -- برگشت فروش (BACK_HEAD.ta=2) در برابر سند اصلیِ فروش (TAG=2)
        SELECT  N'saleReturn',
                CAST(bh.NUMBER AS BIGINT), 2, N'BACK_HEAD', bh.DATE_N,
                CAST(orig.NUMBER AS BIGINT), 2, N'HEAD_LST', orig.DATE_N,
                CONCAT(N'برگشت فروش ', bh.NUMBER, N': تاریخ برگشت ',
                       FORMAT(bh.DATE_N,'0000/00/00'), N' با تاریخ سند اصلیِ فروش ',
                       FORMAT(orig.DATE_N,'0000/00/00'), N' در ماه متفاوتی ثبت شده‌اند'),
                CASE WHEN bh.DATE_N/100 <> orig.DATE_N/100 THEN 1 ELSE 0 END
        FROM    dbo.BACK_HEAD bh
        JOIN    dbo.HEAD_LST  orig ON orig.NUMBER = bh.NUMBER1 AND orig.TAG = 2
        WHERE   bh.ta = 2
          AND   (bh.DATE_N BETWEEN @DT1 AND @DT2 OR orig.DATE_N BETWEEN @DT1 AND @DT2)

        UNION ALL
        -- برگشت خرید (BACK_HEAD.ta=1) در برابر سند اصلیِ خرید (TAG=1)
        SELECT  N'purchaseReturn',
                CAST(bh.NUMBER AS BIGINT), 1, N'BACK_HEAD', bh.DATE_N,
                CAST(orig.NUMBER AS BIGINT), 1, N'HEAD_LST', orig.DATE_N,
                CONCAT(N'برگشت خرید ', bh.NUMBER, N': تاریخ برگشت ',
                       FORMAT(bh.DATE_N,'0000/00/00'), N' با تاریخ سند اصلیِ خرید ',
                       FORMAT(orig.DATE_N,'0000/00/00'), N' در ماه متفاوتی ثبت شده‌اند'),
                CASE WHEN bh.DATE_N/100 <> orig.DATE_N/100 THEN 1 ELSE 0 END
        FROM    dbo.BACK_HEAD bh
        JOIN    dbo.HEAD_LST  orig ON orig.NUMBER = bh.NUMBER1 AND orig.TAG = 1
        WHERE   bh.ta = 1
          AND   (bh.DATE_N BETWEEN @DT1 AND @DT2 OR orig.DATE_N BETWEEN @DT1 AND @DT2)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, DocNumber, DocTag, DocDate, Amount, RefList, Description)
    SELECT  @RunId, 'S00', 'CHK-18', 20, 1,
            d.ANumber, d.ATag, d.ADate, d.BDate,
            (SELECT d.Kind AS kind,
                    d.ANumber AS aNumber, d.ATag AS aTag, d.ATable AS aTable, d.ADate AS aDate,
                    d.BNumber AS bNumber, d.BTag AS bTag, d.BTable AS bTable, d.BDate AS bDate
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            d.Description
    FROM    DateDrift d
    WHERE   d.DifferentMonth = 1
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-18' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL) AND (ae.Code IS NULL));

    ---- CHK-06 : حلقه در ساختار فرمول
    IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
    SELECT DISTINCT CAST(h.CODE AS BIGINT) AS P, CAST(d.CODE AS BIGINT) AS C
    INTO   #E
    FROM   dbo.HEAD_MANF h
    JOIN   dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE  h.GHEYMAT = @Month AND h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND  CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);
    CREATE CLUSTERED INDEX IX_E ON #E(P, C);

    ;WITH W AS (
        SELECT P AS Root, C, 1 AS L,
               CAST('/' + CAST(P AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM   #E
        UNION ALL
        SELECT w.Root, e.C, w.L + 1,
               CAST(w.Pt + CAST(e.P AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM   W w JOIN #E e ON e.P = w.C
        WHERE  w.L < 20
          AND  w.Pt NOT LIKE '%/' + CAST(e.C AS VARCHAR(20)) + '/%'
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT DISTINCT @RunId, 'S00', 'CHK-06', 5, 2, Root,
           N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
    FROM   W WHERE C = Root
    OPTION (MAXRECURSION 0);

    DROP TABLE #E;

    ---- CHK-07 : مانده نامتوازن مواد در ۷۵۱ (آستانه نسبي)
    DECLARE @th FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-07'), 0.001);

    -- DocNumber عمداً پر نمی‌شود: این قاعده مانده یک کالا را در کل بازه بررسی
    -- می‌کند، نه یک سند مشخص را؛ ستون HES_M (کد معین حسابداری) شمارهٔ برگهٔ
    -- تولید نیست و نمایشش به کاربر گمراه‌کننده بود.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-07', 13,
            CASE WHEN SUM(d.BED) = 0 OR SUM(d.BES) = 0 THEN 2 ELSE 1 END,
            TRY_CAST(d.HES_T AS BIGINT),
            SUM(d.BED) - SUM(d.BES),
            CASE WHEN SUM(d.BED) = 0
                 THEN N'ماده با توليد خارج شده ولي با حواله وارد نشده'
                 WHEN SUM(d.BES) = 0
                 THEN N'ماده با حواله وارد شده ولي با توليد خارج نشده'
                 ELSE N'مانده نامتوازن مواد در حساب کالاي در جريان ساخت' END
    FROM    dbo.DEED_DTL d
    JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
    WHERE   d.HES_K = 751 AND d.HES_T <> 99999999
      AND   hd.DATE_S BETWEEN @DT1 AND @DT2
    GROUP BY d.HES_M, d.HES_T
    HAVING  (SUM(d.BED) = 0 AND SUM(d.BES) <> 0)
         OR (SUM(d.BES) = 0 AND SUM(d.BED) <> 0)
         OR (ABS(SUM(d.BED) - SUM(d.BES))
             / NULLIF((SUM(d.BED) + SUM(d.BES)) / 2.0, 0) > @th);

    ---- CHK-09 : نرخ منتشرنشده نيمه‌ساخته
    --
    -- ⚠️ يک کالا مي‌تواند در همان ماه بيش از يک فرمول فعال داشته باشد
    -- (مثلاً روزهاي مختلف با ترکيب مواد متفاوت توليد شده باشد) — طبق تأييد
    -- صاحب پروژه، اين حالت طبيعي است، نه خطاي داده. نسخه‌ي قبلي اين چک هر
    -- (Code,FNUMB) را جدا با نرخ منتشرشده مقايسه مي‌کرد، در حالي‌که موتور
    -- نرخ (S11) فقط يک بهاي واحد به بالادست منتشر مي‌کند — نتيجه: فرمول‌هاي
    -- «غيرمنتخب» هميشه به‌عنوان مغايرت کاذب باقي مي‌ماندند، حتي بعد از
    -- بازسازي نرخ. حالا بهاي «خودِ» کالا ميانگين موزونِ بهاي همه‌ي
    -- فرمول‌هاي فعالش است، وزن‌دهي‌شده با مقدار واقعيِ توليدشده زيرِ هرکدام
    -- (از TAG=9 در همين بازه) — دقيقاً همان معياري که S11 هم استفاده مي‌کند.
    DECLARE @th9 FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-09'), 0.001);

    ;WITH FormulaCost AS (
        SELECT  hm.FNUMB, CAST(hm.CODE AS BIGINT) AS Code,
                SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                       + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha,
                ISNULL(p.Qty, 0) AS Qty
        FROM    dbo.HEAD_MANF hm
        JOIN    dbo.DTL_MANF  d ON d.FNUMB = hm.FNUMB
        CROSS   APPLY (
                    SELECT SUM(pl.MEGHk) AS Qty
                    FROM   dbo.HEAD_LST h
                    JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                    WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                      AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
                ) p
        WHERE   hm.GHEYMAT = @Month
        GROUP BY hm.FNUMB, CAST(hm.CODE AS BIGINT), p.Qty
    ),
    Khod AS (
        -- اگر هيچ‌کدام از فرمول‌هاي اين کالا در بازه توليد واقعي نداشتند
        -- (تعريف شده ولي هنوز مصرف نشده)، ميانگين ساده جايگزين وزن مي‌شود.
        SELECT  Code,
                CASE WHEN SUM(Qty) > 0 THEN SUM(Baha * Qty) / SUM(Qty)
                     ELSE AVG(Baha) END AS Baha
        FROM    FormulaCost
        GROUP BY Code
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code,
               AVG(d.SMABL) AS Nerkh, COUNT(DISTINCT d.FNUMB) AS Valedha
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            CONCAT(N'نرخ منتشر نشده — بهاي فرمول ', CAST(ROUND(k.Baha,0) AS BIGINT),
                   N' ولي نرخ در ', v.Valedha, N' فرمول بالادست ',
                   CAST(ROUND(v.Nerkh,0) AS BIGINT))
    FROM    Khod k
    JOIN    DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > @th9;

    ---- خلاصه
    SELECT  e.RuleCode AS قاعده, r.RuleName AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده' ELSE N'هشدار' END AS شدت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.StepCode = 'S00' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO


/* ═══════════════ S03 — حذف اسناد حسابداری خالی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S03_DeleteEmptyDeeds
    @RunId INT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 1                  -- ۱ = فقط گزارش، بدون حذف
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Empty') IS NOT NULL DROP TABLE #Empty;

    -- «خالی» یعنی نه فقط بدون ردیف DEED_DTL، بلکه هیچ جدول دیگری هم به آن
    -- ارجاع ندهد. طبق sys.foreign_keys، هشت جدول به DEED_HED.N_S کلید
    -- خارجی دارند (DEED_DTL, HEAD_LST, ANBGRD_HEAD, CHKREC_H, CHREC_HP,
    -- WORKHEAD, MO_DTL, PGET_HED, HEAD_LST_TMP_WPF). سندی که هنوز از
    -- کاردکس انبار یا هرکدام دیگر ارجاع می‌شود واقعاً خالی نیست، حتی اگر
    -- DEED_DTL نداشته باشد — نباید حذفش کرد، و مطلقاً نباید ارجاع آن
    -- جدول‌ها را NULL کرد تا حذف زور بشود؛ آن ارجاع همان چیزی است که
    -- ردگیری سند حسابداری را از رکورد انبار ممکن می‌کند.
    SELECT h.N_S, h.DATE_S
    INTO   #Empty
    FROM   dbo.DEED_HED h
    WHERE  h.DATE_S BETWEEN @DT1 AND @DT2
      AND  NOT EXISTS (SELECT 1 FROM dbo.DEED_DTL    d WHERE d.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.HEAD_LST    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.ANBGRD_HEAD x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.CHKREC_H    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.CHREC_HP    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.WORKHEAD    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.MO_DTL      x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.PGET_HED    x WHERE x.N_S = h.N_S);

    -- HEAD_LST_TMP_WPF ممکن است روی همهٔ نصب‌ها نباشد؛ اگر هست همان قاعده.
    IF OBJECT_ID('dbo.HEAD_LST_TMP_WPF', 'U') IS NOT NULL
        DELETE e FROM #Empty e
        WHERE EXISTS (SELECT 1 FROM dbo.HEAD_LST_TMP_WPF t WHERE t.N_S = e.N_S);

    DECLARE @n INT = (SELECT COUNT(*) FROM #Empty);

    IF @WhatIf = 1
    BEGIN
        SELECT N_S AS شماره_سند, DATE_S AS تاريخ FROM #Empty ORDER BY DATE_S, N_S;
        SELECT @n AS تعداد_سند_قابل_حذف, N'حالت گزارش — چيزي حذف نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DELETE h FROM dbo.DEED_HED h JOIN #Empty e ON e.N_S = h.N_S;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S03', 1, CONCAT(N'حذف اسناد خالي: ', @n, N' سند'),
            (SELECT N_S, DATE_S FROM #Empty FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S03_DeleteEmptyDeeds).
    -- Dapper روی نام‌مستعار فارسی نگاشت نمی‌کند و بی‌صدا صفر برمی‌گرداند؛
    -- شرح فارسی در CC_RunLog بالا ثبت شده است.
    SELECT @n AS Value;
END
GO


/* ═══════════════ S04 — مرتب‌سازی اسناد ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S04_SortDeeds
    @RunId     INT,
    @DT1       BIGINT,
    @DT2       BIGINT,
    @WholeYear BIT = 0,
    @WhatIf    BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    DECLARE @seed FLOAT =
        CASE WHEN @WholeYear = 1 THEN 0
             ELSE ISNULL((SELECT MAX(N_S) FROM dbo.DEED_HED WHERE DATE_S < @DT1), 0) END;

    -- کل جدول را می‌آوریم (نه فقط بازهٔ ماه) چون برای جلوگیری از تلاقی با
    -- اسناد ماه‌های بعدی باید بدانیم شمارهٔ فعلی‌شان چیست؛ اسناد بیرون بازه
    -- در ستون NewNS همان شمارهٔ فعلی خودشان را می‌گیرند (دست‌نخورده).
    SELECT  base,
            DATE_S,
            N_S AS OldNS,
            CASE WHEN @WholeYear = 1 OR DATE_S BETWEEN @DT1 AND @DT2
                 THEN @seed + ROW_NUMBER() OVER (
                          PARTITION BY CASE WHEN @WholeYear = 1
                                             OR DATE_S BETWEEN @DT1 AND @DT2
                                        THEN 1 ELSE 0 END
                          ORDER BY DATE_S ASC, N_S ASC)
                 ELSE N_S END AS NewNS
    INTO    #Map
    FROM    dbo.DEED_HED;

    -- اگر بازهٔ شمارهٔ جدید ماه جاری با شمارهٔ فعلی اولین سند ماه‌های بعدی
    -- تلاقی کند، همهٔ اسناد بعد از @DT2 را به یک اندازه جلو می‌بریم؛ چون
    -- همه با هم جابه‌جا می‌شوند، ترتیب و فاصلهٔ نسبی‌شان دست‌نخورده می‌ماند
    -- و تلاقی تازه‌ای ایجاد نمی‌شود.
    IF @WholeYear = 0
    BEGIN
        DECLARE @maxNewInMonth FLOAT =
            ISNULL((SELECT MAX(NewNS) FROM #Map WHERE DATE_S BETWEEN @DT1 AND @DT2), @seed);
        DECLARE @minAfterMonth FLOAT =
            ISNULL((SELECT MIN(OldNS) FROM #Map WHERE DATE_S > @DT2), 0);

        IF @minAfterMonth > 0 AND @maxNewInMonth >= @minAfterMonth
        BEGIN
            DECLARE @shift FLOAT = (@maxNewInMonth - @minAfterMonth) + 1;
            UPDATE #Map SET NewNS = OldNS + @shift WHERE DATE_S > @DT2;
        END
    END

    CREATE UNIQUE CLUSTERED INDEX IX_Map ON #Map(base);

    DECLARE @total INT   = (SELECT COUNT(*) FROM #Map);
    DECLARE @changed INT = (SELECT COUNT(*) FROM #Map WHERE OldNS <> NewNS);

    IF @WhatIf = 1
    BEGIN
        SELECT TOP 100 base, OldNS AS شماره_فعلي, NewNS AS شماره_جديد
        FROM   #Map WHERE OldNS <> NewNS ORDER BY NewNS;
        SELECT @total AS کل_اسناد, @changed AS تعداد_تغيير,
               N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- تريگرهاي Audit را فقط براي همين نشست کنار مي‌گذاريم
    EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

    -- ۹ جدول فرزند با ON UPDATE CASCADE خودکار به‌روز مي‌شوند.
    -- دو مرحله‌اي: چون شمارهٔ جدید یک سند می‌تواند برابر شمارهٔ فعلیِ سند
    -- دیگری باشد که هنوز عوض نشده (Shift یا جابه‌جایی داخل ماه)، یک
    -- UPDATE مستقیم وسط کار به PRIMARY KEY تکراری می‌خورد. اول همه را به
    -- یک بازهٔ منفیِ ناهم‌پوشان می‌بریم، بعد به مقدار نهایی.
    UPDATE  h
       SET  h.N_S = -1000000.0 - m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S <> m.NewNS;

    UPDATE  h
       SET  h.N_S = m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S < 0;

    EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S04', 1, N'بازشماره‌گذاري اسناد انجام شد',
            (SELECT @total AS total, @changed AS changed FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S04_SortDeeds).
    -- Value = تعداد اسناد بازشماره‌شده؛ شرح فارسی در CC_RunLog بالا ثبت شد.
    SELECT @changed AS Value, @total AS Total;
END
GO


PRINT N'رويه‌هاي فاز ۱ ايجاد شدند.';

SELECT  name AS رويه, create_date AS تاريخ_ايجاد, modify_date AS آخرين_تغيير
FROM    sys.procedures
WHERE   name LIKE 'CC[_]sp[_]%'
ORDER BY name;
GO
