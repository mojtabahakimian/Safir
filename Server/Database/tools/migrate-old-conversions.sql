/* ═══════════════════════════════════════════════════════════════════
   انتقال تبدیل‌های قدیمی به برگه‌ی TAG=30

   ── چه چیزی را منتقل می‌کند ──
   روشِ قدیمی هر تبدیل را با دو برگه و یک حسابِ واسط می‌ساخت:
     حواله خروج سایر (TAG 11) → بدهکارِ حسابِ واسط
     رسید خرید (TAG 1) + فاکتور خرید (TAG 12) → بستانکارِ همان حساب
   این اسکریپت آن زوج‌ها را پیدا می‌کند و هرکدام را به یک برگه‌ی
   تبدیل (TAG 30) تبدیل می‌کند.

   ── چطور زوج‌ها را می‌شناسد ──
   از سمتِ *سند*، نه از سمتِ سربرگ. حسابِ واسط روی INVO_LST.N_RASID
   می‌نشیند نه روی HEAD_LST.CUST_NO، پس جفت‌کردن از روی سربرگ جواب
   نمی‌دهد. معیار: حسابی که هم از TAG 11 بدهکار شده و هم از TAG 1/12
   بستانکار — و دو برگه‌ای که در یک تاریخ روی همان حساب نشسته‌اند.

   ── سه حالت ──
     @Mode = 0  فقط گزارش (پیش‌فرض) — هیچ چیزی نوشته نمی‌شود
     @Mode = 1  اجرای واقعی
     @Mode = 2  بازگردانی آخرین اجرا از روی جدول پشتیبان

   ⚠️ پیش از @Mode=1 از پایگاه نسخه پشتیبان بگیرید. این اسکریپت سند
   حسابداریِ صادرشده را پاک می‌کند و برگه‌های قدیمی را برمی‌دارد؛
   جدول CC_ConversionMigration همه‌ی آنچه پاک شده را نگه می‌دارد، ولی
   نسخه پشتیبان جای خودش را دارد.

   ⚠️ ماهِ تأییدشده را دست نمی‌زند. اگر تبدیلی در ماهی باشد که
   CC_sp_S14_Approve آن را قفل کرده، رد می‌شود و در گزارش می‌آید —
   چون تغییرِ آن، گزارش‌های تأییدشده را عوض می‌کند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Mode TINYINT = 0;      -- ← ۰ گزارش، ۱ اجرا، ۲ بازگردانی
DECLARE @DT1  BIGINT  = 0;      -- بازه‌ی تاریخ؛ ۰ یعنی بی‌قید
DECLARE @DT2  BIGINT  = 99999999;

/* ───────── جدول پشتیبان ───────── */
IF OBJECT_ID('dbo.CC_ConversionMigration','U') IS NULL
CREATE TABLE dbo.CC_ConversionMigration (
    MigId        INT IDENTITY(1,1) PRIMARY KEY,
    RunAtUtc     DATETIME2     NOT NULL CONSTRAINT DF_CCM_At DEFAULT SYSUTCDATETIME(),
    NewNumber    FLOAT         NULL,      -- شماره‌ی برگه‌ی TAG=30 ساخته‌شده
    IssueNumber  FLOAT         NOT NULL,  -- برگه‌های قدیمی
    ReceiptNumber FLOAT        NOT NULL,
    DateN        BIGINT        NOT NULL,
    Account      NVARCHAR(50)  NULL,
    FromCode     NVARCHAR(20)  NULL, FromAnbar INT NULL, FromQty FLOAT NULL,
    ToCode       NVARCHAR(20)  NULL, ToAnbar   INT NULL, ToQty   FLOAT NULL,
    IssueValue   FLOAT         NULL, ReceiptValue FLOAT NULL,
    OldIssueJson NVARCHAR(MAX) NULL,       -- عینِ سطرها، برای بازگردانی
    OldRecJson   NVARCHAR(MAX) NULL,
    OldDeedJson  NVARCHAR(MAX) NULL,
    Status       NVARCHAR(30)  NOT NULL    -- migrated / reverted / skipped:<دلیل>
);

/* ═════════ حالت ۲ : بازگردانی ═════════ */
IF @Mode = 2
BEGIN
    PRINT N'بازگردانی هنوز دستی است: ردیف‌های CC_ConversionMigration با Status=migrated';
    PRINT N'ستون‌های OldIssueJson / OldRecJson / OldDeedJson عینِ داده‌ی پاک‌شده را دارند.';
    SELECT MigId, NewNumber, IssueNumber, ReceiptNumber, DateN, Status
    FROM   dbo.CC_ConversionMigration WHERE Status = 'migrated' ORDER BY MigId DESC;
    RETURN;
END

/* ═════════ کشفِ زوج‌ها ═════════ */

;WITH Bridge AS (
    /* حسابِ واسط: هم از حواله بدهکار، هم از خرید بستانکار */
    SELECT  d.HES_K, d.HES_M, d.HES_T
    FROM    dbo.DEED_DTL d
    WHERE   d.TAG IN (1, 11, 12)
    GROUP BY d.HES_K, d.HES_M, d.HES_T
    HAVING  SUM(CASE WHEN d.TAG = 11      THEN ISNULL(d.BED,0) ELSE 0 END) > 0
       AND  SUM(CASE WHEN d.TAG IN (1,12) THEN ISNULL(d.BES,0) ELSE 0 END) > 0
),
IssueSide AS (
    SELECT  Num = d.NUMBER, d.N_S,
            Account = CONCAT(d.HES_K,'-',d.HES_M,'-',d.HES_T),
            Amount  = SUM(ISNULL(d.BED,0))
    FROM    dbo.DEED_DTL d
    JOIN    Bridge b ON b.HES_K=d.HES_K AND b.HES_M=d.HES_M AND b.HES_T=d.HES_T
    WHERE   d.TAG = 11 AND ISNULL(d.BED,0) > 0
    GROUP BY d.NUMBER, d.N_S, d.HES_K, d.HES_M, d.HES_T
),
BuySide AS (
    SELECT  Num = d.NUMBER, d.N_S,
            Account = CONCAT(d.HES_K,'-',d.HES_M,'-',d.HES_T),
            Amount  = SUM(ISNULL(d.BES,0))
    FROM    dbo.DEED_DTL d
    JOIN    Bridge b ON b.HES_K=d.HES_K AND b.HES_M=d.HES_M AND b.HES_T=d.HES_T
    WHERE   d.TAG IN (1,12) AND ISNULL(d.BES,0) > 0
    GROUP BY d.NUMBER, d.N_S, d.HES_K, d.HES_M, d.HES_T
)
SELECT
    IssueNumber   = i.Num,
    ReceiptNumber = r.Num,
    Account       = i.Account,
    DateN         = hi.DATE_N,
    FromCode      = li.CODE,  FromName = sf.NAME,
    FromAnbar     = li.ANBAR, FromQty  = li.MEGHk,
    IssueValue    = li.MABL_K,
    ToCode        = lr.CODE,  ToName   = st.NAME,
    ToAnbar       = lr.ANBAR, ToQty    = lr.MEGHk,
    ReceiptValue  = lr.MABL_K,
    Gap           = ISNULL(li.MABL_K,0) - ISNULL(lr.MABL_K,0),
    Blocker =
        CASE
          WHEN EXISTS (SELECT 1 FROM dbo.CC_Run rn
                       WHERE rn.ApprovedAtUtc IS NOT NULL
                         AND hi.DATE_N BETWEEN rn.DateFrom AND rn.DateTo)
               THEN N'ماه تأییدشده است'
          WHEN li.CODE IS NULL OR lr.CODE IS NULL THEN N'یکی از دو برگه ردیف ندارد'
          WHEN (SELECT COUNT(*) FROM dbo.INVO_LST x WHERE x.TAG=11 AND x.NUMBER=i.Num) > 1
               THEN N'حواله بیش از یک ردیف دارد'
          WHEN (SELECT COUNT(*) FROM dbo.INVO_LST x WHERE x.TAG=1  AND x.NUMBER=r.Num) > 1
               THEN N'رسید بیش از یک ردیف دارد'
          ELSE NULL
        END
INTO    #Pairs
FROM    IssueSide i
JOIN    BuySide  r ON r.Account = i.Account
JOIN    dbo.HEAD_LST hi ON hi.TAG=11 AND hi.NUMBER=i.Num
JOIN    dbo.HEAD_LST hr ON hr.TAG=1  AND hr.NUMBER=r.Num AND hr.DATE_N = hi.DATE_N
LEFT    JOIN dbo.INVO_LST li ON li.TAG=11 AND li.NUMBER=i.Num
LEFT    JOIN dbo.INVO_LST lr ON lr.TAG=1  AND lr.NUMBER=r.Num
LEFT    JOIN dbo.STUF_DEF sf ON sf.CODE=li.CODE
LEFT    JOIN dbo.STUF_DEF st ON st.CODE=lr.CODE
WHERE   hi.DATE_N BETWEEN @DT1 AND @DT2
  AND   NOT EXISTS (SELECT 1 FROM dbo.CC_ConversionMigration m
                    WHERE m.IssueNumber=i.Num AND m.Status='migrated');

PRINT N'--- زوج‌های یافت‌شده ---';
SELECT * FROM #Pairs ORDER BY DateN, IssueNumber;

SELECT  Total = COUNT(*),
        Ready = SUM(CASE WHEN Blocker IS NULL THEN 1 ELSE 0 END),
        Skipped = SUM(CASE WHEN Blocker IS NOT NULL THEN 1 ELSE 0 END)
FROM    #Pairs;

IF @Mode = 0
BEGIN
    PRINT N'';
    PRINT N'حالت گزارش. برای اجرا @Mode = 1 بگذارید — و اول نسخه پشتیبان بگیرید.';
    DROP TABLE #Pairs;
    RETURN;
END

/* ═════════ حالت ۱ : اجرا ═════════ */

DECLARE @IssueNo FLOAT, @RecNo FLOAT, @Date BIGINT, @Acc NVARCHAR(50),
        @FCode NVARCHAR(20), @FAnbar INT, @FQty FLOAT, @FVal FLOAT,
        @TCode NVARCHAR(20), @TAnbar INT, @TQty FLOAT,
        @NewNo FLOAT, @Vahed INT, @Note NVARCHAR(200), @User NVARCHAR(50);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT IssueNumber, ReceiptNumber, DateN, Account,
           FromCode, FromAnbar, FromQty, IssueValue, ToCode, ToAnbar, ToQty
    FROM   #Pairs WHERE Blocker IS NULL ORDER BY DateN, IssueNumber;

OPEN cur;
FETCH NEXT FROM cur INTO @IssueNo, @RecNo, @Date, @Acc,
                         @FCode, @FAnbar, @FQty, @FVal, @TCode, @TAnbar, @TQty;

WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRAN;

    SELECT @User = ISNULL(MAX(USER_NAME), N'migration'),
           @Note = N'منتقل‌شده از حواله ' + CAST(CAST(@IssueNo AS BIGINT) AS NVARCHAR(20))
                 + N' و رسید ' + CAST(CAST(@RecNo AS BIGINT) AS NVARCHAR(20))
    FROM   dbo.HEAD_LST WHERE TAG=11 AND NUMBER=@IssueNo;

    SELECT @Vahed = ISNULL(VAHED,0) FROM dbo.STUF_DEF WHERE CODE=@FCode;
    SELECT @NewNo = ISNULL(MAX(NUMBER),0)+1 FROM dbo.HEAD_LST WITH (UPDLOCK, HOLDLOCK) WHERE TAG=30;

    /* نگه‌داشتنِ عینِ داده‌ی قدیمی پیش از هر حذفی */
    INSERT dbo.CC_ConversionMigration
        (NewNumber, IssueNumber, ReceiptNumber, DateN, Account,
         FromCode, FromAnbar, FromQty, ToCode, ToAnbar, ToQty,
         IssueValue, ReceiptValue, OldIssueJson, OldRecJson, OldDeedJson, Status)
    SELECT @NewNo, @IssueNo, @RecNo, @Date, @Acc,
           @FCode, @FAnbar, @FQty, @TCode, @TAnbar, @TQty,
           @FVal,
           (SELECT TOP 1 MABL_K FROM dbo.INVO_LST WHERE TAG=1 AND NUMBER=@RecNo),
           (SELECT * FROM dbo.INVO_LST WHERE TAG=11 AND NUMBER=@IssueNo FOR JSON AUTO),
           (SELECT * FROM dbo.INVO_LST WHERE TAG=1  AND NUMBER=@RecNo   FOR JSON AUTO),
           (SELECT * FROM dbo.DEED_DTL WHERE (TAG=11 AND NUMBER=@IssueNo)
                                          OR (TAG IN (1,12) AND NUMBER=@RecNo) FOR JSON AUTO),
           'migrated';

    /* ── برگه‌ی تازه ── */
    INSERT dbo.HEAD_LST (NUMBER, TAG, DATE_N, MAS, VAS, ANBAR, ANBARF, MOLAH, USER_NAME,
                         M_NAGHD, MABL_VAR, MABL_HAV, MABL_HAZ, TAKHFIF, CRT)
    VALUES (@NewNo, 30, @Date, 0, 0, @FAnbar, @TAnbar, @Note, @User, 0,0,0,0,0, GETDATE());

    /* FK به STUF_FSK — کالای مقصد ممکن است بارِ اول باشد که به آن انبار می‌آید */
    IF NOT EXISTS (SELECT 1 FROM dbo.STUF_FSK WHERE CODE=@FCode AND ANBAR=@FAnbar)
        INSERT dbo.STUF_FSK (CODE,ANBAR,MOGODI_A,FI_A,MABL_A,MANDAH_A,MIN_M,MAX_M,CRT)
        VALUES (@FCode,@FAnbar,0,0,0,0,0,0,GETDATE());
    IF NOT EXISTS (SELECT 1 FROM dbo.STUF_FSK WHERE CODE=@TCode AND ANBAR=@TAnbar)
        INSERT dbo.STUF_FSK (CODE,ANBAR,MOGODI_A,FI_A,MABL_A,MANDAH_A,MIN_M,MAX_M,CRT)
        VALUES (@TCode,@TAnbar,0,0,0,0,0,0,GETDATE());

    /* ⚠️ مبلغ از سمتِ *خروج* برداشته می‌شود، نه از رسید.
       سمت خروج همان است که بازسازی نرخ میانگین آن را درست نگه داشته؛
       رسید همان عددی است که روزِ ثبت تایپ شده و از آن فاصله گرفته. */
    INSERT dbo.INVO_LST (NUMBER, TAG, ANBAR, ANBARF, CODE, MEGH, MEGHk, MEGH_R,
                         N_RASID, MEGH_MAR, MABL, MABL_K, FROM_A, VAHED_K, CRT)
    VALUES (@NewNo, 30, @FAnbar, @TAnbar, @FCode, @FQty, @FQty, @FQty,
            @TCode, @TQty,
            CASE WHEN @FQty = 0 THEN 0 ELSE @FVal / @FQty END, @FVal,
            0, @Vahed, GETDATE());

    /* ── برداشتنِ برگه‌ها و سندهای قدیمی ── */
    DELETE FROM dbo.DEED_DTL WHERE (TAG=11 AND NUMBER=@IssueNo) OR (TAG IN (1,12) AND NUMBER=@RecNo);
    DELETE FROM dbo.INVO_LST WHERE (TAG=11 AND NUMBER=@IssueNo) OR (TAG=1 AND NUMBER=@RecNo);
    DELETE FROM dbo.HEAD_LST WHERE (TAG=11 AND NUMBER=@IssueNo) OR (TAG IN (1,12) AND NUMBER=@RecNo);

    COMMIT;

    PRINT N'برگه ' + CAST(CAST(@NewNo AS BIGINT) AS NVARCHAR(20))
        + N' ← حواله ' + CAST(CAST(@IssueNo AS BIGINT) AS NVARCHAR(20))
        + N' + رسید ' + CAST(CAST(@RecNo AS BIGINT) AS NVARCHAR(20));

    FETCH NEXT FROM cur INTO @IssueNo, @RecNo, @Date, @Acc,
                             @FCode, @FAnbar, @FQty, @FVal, @TCode, @TAnbar, @TQty;
END
CLOSE cur; DEALLOCATE cur;

PRINT N'';
PRINT N'انجام شد. حالا این سه کار را به ترتیب اجرا کنید:';
PRINT N'  ۱) بازسازی نرخ میانگین  (تا Case 30/31 مبلغ و میانگین را بنویسند)';
PRINT N'  ۲) بازسازی اسناد گروهی → «تبدیل کالا به کالا»';
PRINT N'  ۳) یک اجرای بستن ماه، تا CHK-24 برگه‌ی ناقصی نمانده باشد';

DROP TABLE #Pairs;
