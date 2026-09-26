/* ═══════════════════════════════════════════════════════════════════
   تبدیل کالا به کالا

   ── چه کاری است ──
   مقداری از یک کالا از انباری خارج می‌شود و کالای *دیگری* به انبار
   دیگری وارد می‌شود: خامه ۲۸٪ می‌رود و خامه ۶۵٪ می‌آید. چیزی تولید
   نشده و چیزی خریده نشده — فقط همان ارزش، زیر نام دیگری، جای دیگری
   نشسته است.

   ── امروز چطور انجام می‌شود ──
   دستی، با دو برگه و یک حساب واسط:
     ۱. حواله خروج سایر (TAG=11) از انبار مبدأ، به بدهکار حسابِ واسط
     ۲. رسید خرید (TAG=1) + فاکتور خرید (TAG=12) به انبار مقصد، به
        بستانکار همان حساب
   اگر دو مبلغ یکی باشند، حسابِ واسط صفر می‌شود و هیچ سود و زیانی
   ساخته نمی‌شود. این همان چیزی است که باید بشود.

   ── چرا نمی‌شود ──
   نمی‌شود، و روی داده‌ی واقعی هم نشده. روی پایگاه «پودر مروارید»
   حساب ۷۴۱-۳۰۰۰-۳۰۰۰ («تبدیل») دقیقاً دو آرتیکل دارد:

     حواله خروج سایر ۸۱   مورخ ۱۴۰۵/۰۲/۳۱   بدهکار  ۲۶۸٬۱۵۱٬۴۷۶
     فاکتور خرید    ۱۶۱   مورخ ۱۴۰۵/۰۲/۳۱   بستانکار ۲۷۲٬۹۱۵٬۳۳۱
     ───────────────────────────────────────────────────────────
     مانده                                            ۴٬۷۶۳٬۸۵۵

   چهار میلیون و هفتصد هزار ریال روی حسابی نشسته که باید صفر باشد.

   علتش هم دقیقاً معلوم است و ربطی به دقتِ کاربر ندارد: بازسازی نرخ
   میانگین (S07A) ردیف‌های TAG=۱۰/۱۱ را *دوباره قیمت‌گذاری می‌کند* —
   MABL و MABL_K را با میانگین متحرکِ همان لحظه بازنویسی می‌کند. ولی
   ردیف TAG=۱ (خرید) را دست نمی‌زند؛ همان عددی می‌ماند که تایپ شده.
   کاربر نرخ را در لحظه‌ی ثبت از کاردکس می‌خواند و درست هم می‌خواند،
   ولی تا پایان ماه میانگین جابه‌جا شده است. در همان نمونه:

     نرخ تایپ‌شده در رسید   ۸۲۳٬۲۷۴
     نرخ نهایی پس از S07A   ۸۰۸٬۹۰۳
     اختلاف هر کیلو          ۱۴٬۳۷۱

   یعنی این مانده هر بار که تبدیلی ثبت شود دوباره ساخته می‌شود، و
   هیچ‌کس هم مقصر نیست.

   ── راه‌حل ──
   دو برگه را به هم گره می‌زنیم و مبلغِ سمت ورود را *مشتق* می‌کنیم، نه
   تایپ. جدول CC_ItemConversion این گره است. بعد از همگرایی نرخ‌ها، گام
   S11B مبلغ رسید را از روی مبلغِ نهاییِ حواله می‌نویسد. حساب واسط با
   ساختار صفر می‌شود، نه با دقتِ تایپ.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────────────── گره‌ی دو برگه ───────────────────────── */

IF OBJECT_ID('dbo.CC_ItemConversion','U') IS NULL
CREATE TABLE dbo.CC_ItemConversion (
    ConversionId   INT IDENTITY(1,1) PRIMARY KEY,
    DateN          BIGINT        NOT NULL,

    /* حساب واسط، به شکل کامل «کل-معین-تفصیل» — مثلاً '741-3000-3000'.
       عمداً متن است و نه سه ستون عددی: همین رشته در HEAD_LST.CUST_NO و
       INVO_LST.N_RASID می‌نشیند و باید عیناً همان باشد. */
    AccountCode    NVARCHAR(50)  NOT NULL,

    FromCode       NVARCHAR(20)  NOT NULL,
    FromAnbar      INT           NOT NULL,
    FromQty        FLOAT         NOT NULL,

    ToCode         NVARCHAR(20)  NOT NULL,
    ToAnbar        INT           NOT NULL,
    ToQty          FLOAT         NOT NULL,

    /* شماره‌ی سه برگه‌ای که ساخته شده. FLOAT چون HEAD_LST.NUMBER هم
       FLOAT است و مقایسه‌ی INT با FLOAT در JOIN، ایندکس را از کار
       می‌اندازد. */
    IssueNumber    FLOAT         NOT NULL,   -- TAG=11 حواله خروج سایر
    ReceiptNumber  FLOAT         NOT NULL,   -- TAG=1  رسید خرید
    InvoiceNumber  FLOAT         NULL,       -- TAG=12 فاکتور خرید

    /* نرخ و مبلغِ لحظه‌ی ثبت. این‌ها *مرجع نیستند* — مرجع همیشه
       INVO_LST است. اینجا می‌مانند تا بشود گفت «موقع ثبت چه دیدیم» و
       اختلافش با مبلغ نهایی را نشان داد. */
    RateAtEntry    FLOAT         NOT NULL,
    ValueAtEntry   FLOAT         NOT NULL,

    Note           NVARCHAR(200) NULL,
    Status         TINYINT       NOT NULL CONSTRAINT DF_CC_ItemConv_Status DEFAULT (1),
                                             -- 1=فعال  9=ابطال‌شده
    CreatedBy      NVARCHAR(100) NULL,
    CreatedAt      DATETIME      NOT NULL CONSTRAINT DF_CC_ItemConv_CreatedAt DEFAULT (GETDATE())
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_CC_ItemConversion_Date' AND object_id = OBJECT_ID('dbo.CC_ItemConversion'))
    CREATE INDEX IX_CC_ItemConversion_Date
        ON dbo.CC_ItemConversion(DateN, Status) INCLUDE (IssueNumber, ReceiptNumber);
GO

/* ───────────────────── وضعیت زنده‌ی هر تبدیل ─────────────────────

   مبلغ را از خودِ INVO_LST می‌خواند، نه از ValueAtEntry — چون بعد از
   S07A فقط INVO_LST درست است. Gap همان چیزی است که روی حساب واسط
   می‌ماند. */

IF OBJECT_ID('dbo.CC_vw_ItemConversion','V') IS NOT NULL
    DROP VIEW dbo.CC_vw_ItemConversion;
GO

CREATE VIEW dbo.CC_vw_ItemConversion
AS
SELECT  c.ConversionId,
        c.DateN,
        c.AccountCode,
        c.FromCode,
        FromName    = sf.NAME,
        c.FromAnbar,
        FromAnbarName = af.NAMES,
        c.FromQty,
        c.ToCode,
        ToName      = st.NAME,
        c.ToAnbar,
        ToAnbarName = at.NAMES,
        c.ToQty,
        c.IssueNumber,
        c.ReceiptNumber,
        c.InvoiceNumber,
        c.RateAtEntry,
        c.ValueAtEntry,

        IssueValue   = ISNULL(iss.MABL_K, 0),
        ReceiptValue = ISNULL(rcp.MABL_K, 0),
        Gap          = ISNULL(iss.MABL_K, 0) - ISNULL(rcp.MABL_K, 0),

        /* نرخ واحدِ کالای مقصد، از مبلغ نهایی. همان عددی که در کاردکس
           مقصد می‌نشیند. */
        ToRate       = CASE WHEN c.ToQty = 0 THEN 0
                            ELSE ISNULL(iss.MABL_K, 0) / c.ToQty END,

        c.Note,
        c.Status,
        c.CreatedBy,
        c.CreatedAt
FROM    dbo.CC_ItemConversion c
LEFT    JOIN dbo.STUF_DEF   sf ON sf.CODE  = c.FromCode
LEFT    JOIN dbo.STUF_DEF   st ON st.CODE  = c.ToCode
LEFT    JOIN dbo.TCOD_ANBAR af ON af.CODE  = c.FromAnbar
LEFT    JOIN dbo.TCOD_ANBAR at ON at.CODE  = c.ToAnbar
OUTER   APPLY (SELECT SUM(i.MABL_K) AS MABL_K FROM dbo.INVO_LST i
               WHERE i.TAG = 11 AND i.NUMBER = c.IssueNumber) iss
OUTER   APPLY (SELECT SUM(i.MABL_K) AS MABL_K FROM dbo.INVO_LST i
               WHERE i.TAG = 1  AND i.NUMBER = c.ReceiptNumber) rcp;
GO

/* ──────────────── هم‌ترازکردن سمت ورود با سمت خروج ────────────────

   بعد از اینکه S07A/S11 همگرا شدند، مبلغ حواله دیگر عوض نمی‌شود. آن
   وقت این رویه مبلغ رسید را *از روی آن* می‌نویسد.

   ⚠️ MABL (نرخ واحد) هم بازنویسی می‌شود و نه فقط MABL_K: کاردکس مقصد
   از MABL_K ارزش می‌گیرد ولی گزارش‌های نرخ از MABL می‌خوانند؛ رها
   کردن یکی از این دو یعنی دو عدد متناقض در دو گزارش.

   ⚠️ مقدار (MEGHk) دست نمی‌خورد. تبدیل ممکن است مقدارش عوض شود —
   ۳۳۱ کیلو خامه ۲۸٪ می‌تواند ۱۴۲ کیلو خامه ۶۵٪ بدهد — و این مقدار
   واقعیتِ فیزیکی است، نه چیزی که از حسابداری مشتق شود. فقط نرخ عوض
   می‌شود: نرخ = مبلغِ خروج ÷ مقدارِ ورود. */

IF OBJECT_ID('dbo.CC_sp_ResyncConversions','P') IS NOT NULL
    DROP PROCEDURE dbo.CC_sp_ResyncConversions;
GO

CREATE PROCEDURE dbo.CC_sp_ResyncConversions
    @RunId INT          = NULL,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Changed TABLE (
        ReceiptNumber FLOAT,
        ToCode        NVARCHAR(20),
        OldValue      FLOAT,
        NewValue      FLOAT
    );

    ;WITH Target AS (
        SELECT  v.ConversionId, v.ToCode, v.ToQty, v.ReceiptNumber,
                v.IssueValue, v.ReceiptValue
        FROM    dbo.CC_vw_ItemConversion v
        WHERE   v.Status = 1
          AND   v.DateN BETWEEN @DT1 AND @DT2
          /* صفر یعنی حواله هنوز قیمت نخورده — آن را تحمیل نمی‌کنیم،
             وگرنه کالای مقصد با ارزش صفر وارد انبار می‌شود و همان
             فاجعه‌ای می‌شود که روی کد ۲۰۲۱ دیدیم. */
          AND   v.IssueValue <> 0
          AND   ABS(v.IssueValue - v.ReceiptValue) > 0.5
    )
    UPDATE  i
    SET     i.MABL_K = t.IssueValue,
            i.MABL   = CASE WHEN t.ToQty = 0 THEN 0 ELSE t.IssueValue / t.ToQty END
    OUTPUT  INSERTED.NUMBER, INSERTED.CODE, DELETED.MABL_K, INSERTED.MABL_K
            INTO @Changed (ReceiptNumber, ToCode, OldValue, NewValue)
    FROM    dbo.INVO_LST i
    JOIN    Target t ON t.ReceiptNumber = i.NUMBER AND i.TAG = 1 AND i.CODE = t.ToCode;

    /* ─── CHK-24 : تبدیلی که هنوز تراز نشده ───

       بعد از به‌روزرسانی بالا، هر اختلافی که مانده یعنی چیزی غیرعادی
       است: حواله‌ی صفر، رسیدِ حذف‌شده، یا کالایی که کد مقصدش با برگه
       نمی‌خواند. همه‌شان باید دیده شوند، چون همه‌شان مانده روی حساب
       واسط می‌گذارند. */
    IF @RunId IS NOT NULL
    BEGIN
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code,
             DocNumber, DocTag, DocDate, Amount, Description)
        SELECT  @RunId, 'S11B', 'CHK-24', 26, 1,
                v.ToAnbar, TRY_CAST(v.ToCode AS BIGINT),
                CAST(v.IssueNumber AS INT), 11, v.DateN,
                v.Gap,
                CONCAT(N'تبدیل ', v.FromName, N' به ', v.ToName,
                       N' مورخ ', FORMAT(v.DateN, '0000/00/00'),
                       N': حواله ', CAST(v.IssueNumber AS BIGINT),
                       N' و رسید ', CAST(v.ReceiptNumber AS BIGINT),
                       N' هم‌مبلغ نیستند — ',
                       FORMAT(ABS(v.Gap), 'N0'), N' ریال روی حساب ',
                       v.AccountCode, N' می‌ماند',
                       CASE WHEN v.IssueValue = 0
                            THEN N' (حواله هنوز قیمت نخورده است)'
                            ELSE N'' END)
        FROM    dbo.CC_vw_ItemConversion v
        WHERE   v.Status = 1
          AND   v.DateN BETWEEN @DT1 AND @DT2
          AND   ABS(v.Gap) > 0.5
          AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                            WHERE ae.RuleCode = 'CHK-24' AND ae.IsActive = 1
                              AND (ae.Anbar IS NULL OR ae.Anbar = v.ToAnbar)
                              AND (ae.Code  IS NULL OR ae.Code  = TRY_CAST(v.ToCode AS BIGINT)));
    END

    /* کدهای مقصدی که مبلغشان عوض شد — فراخوان باید کاردکس همین‌ها را
       دوباره بسازد، چون ارزشِ ورودی‌شان تغییر کرده. */
    SELECT DISTINCT ToCode FROM @Changed;
END
GO

/* ───────────────────────── ثبت قاعده ───────────────────────── */

MERGE dbo.CC_CheckRule AS t
USING (VALUES
 ('CHK-24', N'تبدیل کالا نامتوازن', 'S11B', 26, 1, 0.5,
  N'مبلغ حواله خروج و رسید ورودِ یک تبدیل باید یکی باشد. اگر حواله قیمت نخورده، اول بازسازی نرخ میانگین را اجرا کنید؛ اگر یکی از دو برگه پاک شده، تبدیل را ابطال و دوباره ثبت کنید.', 240)
) AS s (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
ON t.RuleCode = s.RuleCode
WHEN MATCHED THEN UPDATE SET
    t.RuleName = s.RuleName, t.StepCode = s.StepCode, t.ExType = s.ExType,
    t.DefaultSeverity = s.DefaultSeverity, t.Threshold = s.Threshold,
    t.RemedyText = s.RemedyText, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT
    (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
    VALUES (s.RuleCode, s.RuleName, s.StepCode, s.ExType, s.DefaultSeverity,
            s.Threshold, s.RemedyText, s.SortOrder);
GO

PRINT N'تبدیل کالا: جدول، نما، رویه و قاعده CHK-24 آماده شد.';
GO
