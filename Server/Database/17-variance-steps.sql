/* ═══════════════════════════════════════════════════════════════════
   S07 تا S09 — بازتولید، انحراف، و تخصیص

   S07  بازتولید خروج مواد + انبارگردانی  (بازنویسی مجموعه‌ای)
   S08  محاسبه انحراف مصرف
   S09  تخصیص انحراف با تصمیم کاربر
   S09a تولید پیشنهاد پیش‌فرض از ماه قبل

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S07 — بازتولید خروج مواد و انبارگردانی

   جایگزین اسکریپت فعلی با دو کرسر تودرتو و sp_executesql.
   خروج مواد یک INSERT مجموعه‌ای است؛ انبارگردانی کرسر روز دارد
   چون dbo.MOGUDI تابع جدولی پارامتری است.

   انبارها از CC_UnitAnbar خوانده می‌شوند، نه از کد. با این کار
   باگ تکرار انبار ۸ در اسکریپت فعلی موضوعیت خود را از دست می‌دهد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S07_RebuildIssue
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── بخش يک: خروج مواد ─── */
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  h.NUMBER  AS ProdNo,
            h.NUMBER1 AS IssueNo
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    WHERE   h.TAG = 9
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   EXISTS (SELECT 1 FROM dbo.HEAD_LST x
                    WHERE x.NUMBER = h.NUMBER1 AND x.TAG = 10);

    CREATE CLUSTERED INDEX IX_Prod ON #Prod(IssueNo);

    IF @WhatIf = 1
    BEGIN
        SELECT  COUNT(*) AS تعداد_برگه_توليد,
                (SELECT COUNT(*) FROM dbo.INVO_LST i
                 JOIN #Prod p ON p.IssueNo = i.NUMBER AND i.TAG = 10) AS سطر_فعلي_خروج
        FROM    #Prod;
        RETURN;
    END

    BEGIN TRAN;

    DELETE  i
    FROM    dbo.INVO_LST i
    JOIN    #Prod p ON p.IssueNo = i.NUMBER AND i.TAG = 10;

    DECLARE @deleted INT = @@ROWCOUNT;

    INSERT dbo.INVO_LST
        (NUMBER, TAG, ANBAR, CODE, VAHED_K, MEGH, MEGHK,
         N_RASID, MABL, AVRAGE, MABL_K)
    SELECT  p.IssueNo, 10, dm.ANBAR, dm.CODE, dm.VAHED_K,
            (dm.MEGH  + dm.PERT) * pl.MEGHK,
            (dm.MEGHK + dm.PERT) * pl.MEGHK,
            dm.FNUMB, 1, 1,
            (dm.MEGHK + dm.PERT) * pl.MEGHK
    FROM    #Prod p
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = p.ProdNo AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                            AND hm.GHEYMAT = @Month
    JOIN    dbo.DTL_MANF  dm ON dm.FNUMB  = hm.FNUMB;

    DECLARE @inserted INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S07', 1,
            CONCAT(N'بازتوليد خروج مواد: ', @deleted, N' حذف، ', @inserted, N' درج'),
            (SELECT @deleted AS deleted, @inserted AS inserted FOR JSON PATH));

    COMMIT;

    /* ─── بخش دو: انبارگرداني ─── */
    DECLARE @anb INT, @grdNum INT, @grdDate INT, @countRows INT = 0;

    -- NUM3 مقدارِ شمارشِ فیزیکیِ واقعی است (برای انبارهایی که واقعاً
    -- شمارش دستی دارند، نه اسنپ‌شاتِ خودکارِ روزانه) — این رویه نمی‌تواند
    -- آن را بازتولید کند. DELETE پایین آن را همراه کل ردیف پاک می‌کرد و
    -- INSERT بعدی هرگز NUM3 را دوباره نمی‌گذاشت، پس هر بار اجرای این گام
    -- بی‌صدا پاکش می‌کرد — دقیقاً همان چیزی که برای سند ۷۲ (انبار ۳)
    -- رخ داد و کاربر تأیید کرد باگ بوده. قبل از DELETE نگهش می‌داریم و
    -- بعد از INSERT دوباره رویش می‌گذاریم.
    IF OBJECT_ID('tempdb..#Num3') IS NOT NULL DROP TABLE #Num3;

    SELECT  l.GRD_NUM, l.CODE, l.NUM3
    INTO    #Num3
    FROM    dbo.ANBGRD_LST  l
    JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
    WHERE   h.GRD_DATE BETWEEN @DT1 AND @DT2
      AND   l.NUM3 IS NOT NULL AND l.NUM3 <> 0
      AND   h.GRD_ANBAR IN (SELECT ua.Anbar FROM dbo.CC_UnitAnbar ua
                             JOIN dbo.CC_Unit u ON u.UnitId = ua.UnitId AND u.IsActive = 1
                             WHERE ua.DoStockCount = 1);

    CREATE CLUSTERED INDEX IX_Num3 ON #Num3(GRD_NUM, CODE);

    DECLARE cAnb CURSOR LOCAL FAST_FORWARD FOR
        SELECT   ua.Anbar
        FROM     dbo.CC_UnitAnbar ua
        JOIN     dbo.CC_Unit u ON u.UnitId = ua.UnitId AND u.IsActive = 1
        WHERE    ua.DoStockCount = 1
        GROUP BY ua.Anbar, ua.SeqNo      -- يک انبار در دو واحد = يک بار پردازش
        ORDER BY ua.SeqNo;

    OPEN cAnb;
    FETCH NEXT FROM cAnb INTO @anb;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DELETE  l
        FROM    dbo.ANBGRD_LST l
        JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
        WHERE   h.GRD_ANBAR = @anb AND h.GRD_DATE BETWEEN @DT1 AND @DT2;

        DECLARE cDay CURSOR LOCAL FAST_FORWARD FOR
            SELECT GRD_NUM, GRD_DATE
            FROM   dbo.ANBGRD_HEAD
            WHERE  GRD_ANBAR = @anb AND GRD_DATE BETWEEN @DT1 AND @DT2
            ORDER BY GRD_DATE;

        OPEN cDay;
        FETCH NEXT FROM cDay INTO @grdNum, @grdDate;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            INSERT dbo.ANBGRD_LST (CODE, MOG, GRD_NUM)
            SELECT CODE, MAND, @grdNum FROM dbo.MOGUDI(@grdDate, @anb);

            SET @countRows += @@ROWCOUNT;
            FETCH NEXT FROM cDay INTO @grdNum, @grdDate;
        END

        CLOSE cDay;
        DEALLOCATE cDay;

        FETCH NEXT FROM cAnb INTO @anb;
    END

    CLOSE cAnb;
    DEALLOCATE cAnb;

    UPDATE  l
       SET  l.NUM3 = n.NUM3
    FROM    dbo.ANBGRD_LST l
    JOIN    #Num3 n ON n.GRD_NUM = l.GRD_NUM AND n.CODE = l.CODE;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S07', 1, CONCAT(N'انبارگرداني: ', @countRows, N' سطر'));

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (CostCloseController/S07_RebuildIssue)؛
    -- برای بازبینی دستی در SSMS از دو خلاصه بالا (INSERT به CC_RunLog) استفاده کنید.
    SELECT @deleted AS Deleted, @inserted AS Inserted, @countRows AS StockCount;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S08 — محاسبه انحراف مصرف

   مانده انبار مواد مصرفی تولید = انحراف مصرف
   (به شرط صفر بودن کالای در جریان ساخت)

   همان zanbekht{MM}، ولی با مبلغ ریالی و درصد نسبت به مصرف،
   تا بتوان بر اساس اهمیت مرتب کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S08_CalcVariance
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Variance WHERE RunId = @RunId;

    ---- انبارهاي «مبناي انحراف» هر واحد
    INSERT dbo.CC_Variance
        (RunId, Anbar, Code, QtyVariance, UnitRate, AmountVariance, ConsumedQty)
    SELECT  @RunId,
            h.GRD_ANBAR,
            l.CODE,
            SUM(l.MOG - ISNULL(l.NUM3, 0))                     AS QtyVar,
            MAX(ic.TotalCost)                                  AS Rate,
            SUM(l.MOG - ISNULL(l.NUM3, 0)) * MAX(ic.TotalCost) AS AmtVar,
            MAX(u.Consumed)                                    AS Consumed
    FROM    dbo.ANBGRD_LST  l
    JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
    JOIN    dbo.CC_UnitAnbar ua ON ua.Anbar = h.GRD_ANBAR AND ua.AnbarRole = 1
    LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = l.CODE AND ic.RunId = @RunId
    OUTER   APPLY (
                SELECT SUM(pl.MEGHK * d.MEGHk) AS Consumed
                FROM   dbo.HEAD_LST  hl
                JOIN   dbo.INVO_LST  pl ON pl.NUMBER = hl.NUMBER AND pl.TAG = 9
                JOIN   dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                       AND hm.GHEYMAT = @Month
                JOIN   dbo.DTL_MANF  d  ON d.FNUMB   = hm.FNUMB
                                       AND CAST(d.CODE AS BIGINT) = l.CODE
                WHERE  hl.TAG = 9 AND hl.DATE_N BETWEEN @DT1 AND @DT2
            ) u
    WHERE   h.GRD_DATE BETWEEN @DT1 AND @DT2
    GROUP BY h.GRD_ANBAR, l.CODE
    HAVING  ABS(SUM(l.MOG - ISNULL(l.NUM3, 0))) > 0.0001;

    ---- کالاي کليدي: بالاي يک درصد کل انحراف
    DECLARE @total FLOAT =
        (SELECT SUM(ABS(ISNULL(AmountVariance, 0)))
         FROM dbo.CC_Variance WHERE RunId = @RunId);

    IF @total > 0
        UPDATE dbo.CC_Variance
           SET IsKeyItem = 1
         WHERE RunId = @RunId
           AND ABS(ISNULL(AmountVariance, 0)) > @total * 0.01;

    ---- CHK-11: انحراف روي ماده‌اي که در هيچ فرمولي مصرف نشده
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-11';

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
    SELECT  @RunId, 'S08', 'CHK-11', 11, 1, v.Anbar, v.Code, v.AmountVariance,
            N'انحراف روي ماده‌اي که در هيچ فرمول اين ماه مصرف نشده'
    FROM    dbo.CC_Variance v
    WHERE   v.RunId = @RunId
      AND   ISNULL(v.ConsumedQty, 0) = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S08', 1,
            CONCAT(N'انحراف مصرف: ', COUNT(*), N' کالا، جمع ',
                   FORMAT(SUM(ISNULL(AmountVariance,0)), 'N0'), N' ريال'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN IsKeyItem = 1 THEN 1 ELSE 0 END) AS keyItems,
                    SUM(ISNULL(AmountVariance,0)) AS netAmount
             FROM dbo.CC_Variance WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_Variance WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S08_CalcVariance.VarianceSummary)
    SELECT  COUNT(*)                                        AS Items,
            SUM(CASE WHEN IsKeyItem = 1 THEN 1 ELSE 0 END)  AS KeyItems,
            SUM(ISNULL(AmountVariance, 0))                  AS NetAmount,
            SUM(ABS(ISNULL(AmountVariance, 0)))             AS GrossAmount
    FROM    dbo.CC_Variance WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S09a — تولید پیشنهاد پیش‌فرض

   زنجیره سه‌مرحله‌ای:
     ۱) فرمول مقصد ماه قبل امسال هم هست  → همان تصمیم (Manual)
     ۲) نیست ولی ماده مصرف شده           → تسهیم (Prorata)
     ۳) ماده اصلاً مصرف نشده             → بدون تخصیص (Ignore)

   کلید حمل تصمیم بین ماه‌ها TargetCode است نه TargetFNUMB، چون
   GHEYMAT شماره ماه است و فرمول هر ماه FNUMB جداگانه دارد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S09a_SeedDecisions
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_VarianceDecision WHERE RunId = @RunId;

    -- CC_Variance يک رديف به ازای هر (RunId,Anbar,Code) دارد؛ اگر مستقيم
    -- ازش INSERT کنيم، کالاهای چندانباره چند بار seed می‌شوند و همان
    -- مشکلِ تکرارِ CC_VarianceDecision که در Client/GetVariances/S09 رفع
    -- شد اينجا هم دوباره رخ می‌دهد. اول به ازای هر کد جمع می‌زنيم.
    ;WITH VarByCode AS (
        SELECT  Code, SUM(ConsumedQty) AS ConsumedQty
        FROM    dbo.CC_Variance
        WHERE   RunId = @RunId
        GROUP BY Code
    ),
    Prev AS (
        SELECT  d.Code, d.Mode, d.TargetCode,
                ROW_NUMBER() OVER (PARTITION BY d.Code
                                   ORDER BY d.DecisionId DESC) AS rn
        FROM    dbo.CC_VarianceDecision d
        JOIN    dbo.CC_Run r ON r.RunId = d.RunId
        WHERE   r.Status = 3            -- فقط از اجراهاي تکميل‌شده
          AND   d.RunId <> @RunId
    )
    INSERT dbo.CC_VarianceDecision
        (RunId, Code, Mode, TargetCode, TargetFNUMB, DecidedBy, Note)
    SELECT  @RunId,
            v.Code,
            CASE
              WHEN p.Mode = 1 AND hm.FNUMB IS NOT NULL THEN 1   -- ادامه تصميم قبلي
              WHEN ISNULL(v.ConsumedQty, 0) > 0         THEN 2   -- تسهيم
              ELSE 3                                             -- بدون تخصيص
            END,
            CASE WHEN hm.FNUMB IS NOT NULL THEN p.TargetCode END,
            hm.FNUMB,
            N'system',
            CASE
              WHEN p.Mode = 1 AND hm.FNUMB IS NOT NULL
                   THEN N'مثل ماه قبل'
              WHEN p.Mode = 1 AND hm.FNUMB IS NULL
                   THEN N'فرمول مقصد ماه قبل امسال نيست — تسهيم'
              WHEN ISNULL(v.ConsumedQty, 0) = 0
                   THEN N'ماده در هيچ فرمولي مصرف نشده — بررسي شود'
              ELSE N'تصميم جديد'
            END
    FROM    VarByCode v
    LEFT    JOIN Prev p ON p.Code = v.Code AND p.rn = 1
    OUTER   APPLY (SELECT TOP 1 h.FNUMB
                   FROM   dbo.HEAD_MANF h
                   WHERE  CAST(h.CODE AS BIGINT) = p.TargetCode
                     AND  h.GHEYMAT = @Month
                   ORDER BY h.FNUMB DESC) hm;

    ---- CHK-12: تصميم ماه قبل قابل ادامه نيست
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-12';

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  @RunId, 'S09', 'CHK-12', 15, 1, d.Code,
            N'فرمول مقصد ماه قبل در اين ماه وجود ندارد؛ پيش‌فرض روي تسهيم رفت'
    FROM    dbo.CC_VarianceDecision d
    WHERE   d.RunId = @RunId
      AND   d.Note LIKE N'%امسال نيست%';

    SELECT  CASE Mode WHEN 1 THEN N'اختصاص'
                      WHEN 2 THEN N'تسهيم'
                      ELSE N'بدون تخصيص' END AS حالت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_VarianceDecision
    WHERE   RunId = @RunId
    GROUP BY Mode ORDER BY Mode;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S09 — اعمال تصمیم‌ها

   Manual   کل انحراف کالا به یک فرمول مشخص
   Prorata  تسهیم بین فرمول‌هایی که آن ماده را مصرف کرده‌اند
   Ignore   دست‌نخورده در حساب ۷۷۲ می‌ماند

   تغییر روی MEGHk انجام می‌شود، به ازای یک واحد محصول:
   مقدار افزوده = سهم انحراف ÷ مقدار تولید همان محصول
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S09_ApplyDecisions
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- مقدار توليد هر فرمول در ماه
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

    CREATE UNIQUE CLUSTERED INDEX IX_Prod ON #Prod(FNUMB);

    ---- سهم هر فرمول از انحراف هر ماده
    IF OBJECT_ID('tempdb..#Share') IS NOT NULL DROP TABLE #Share;

    -- CC_Variance يک رديف به ازای هر (RunId,Anbar,Code) دارد؛ تصميم‌ها
    -- در سطح کالا هستند، نه انبار. جوين مستقيم به CC_Variance برای
    -- کالاهای چندانباره چند رديف #Share توليد می‌کرد و UPDATE پايين
    -- فقط يکی را (به‌صورت غيرقطعی) اعمال می‌کرد — انحراف انبارهای
    -- ديگر آن کالا اصلاً به فرمول نمی‌رسيد و «باقيمانده» هرگز صفر
    -- نمی‌شد. اول به ازای هر کد جمع می‌زنيم.
    ;WITH VarByCode AS (
        SELECT  Code, SUM(QtyVariance) AS QtyVariance
        FROM    dbo.CC_Variance
        WHERE   RunId = @RunId
        GROUP BY Code
    ),
    Usage AS (
        SELECT  d.FNUMB,
                CAST(d.CODE AS BIGINT) AS Code,
                p.ProdQty * d.MEGHk    AS UsedQty
        FROM    dbo.DTL_MANF  d
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        JOIN    #Prod p ON p.FNUMB = d.FNUMB
        WHERE   d.MEGHk > 0
    )
    SELECT  u.FNUMB,
            u.Code,
            v.QtyVariance,
            dc.Mode,
            CASE
              -- اختصاص: کل انحراف به همان يک فرمول
              WHEN dc.Mode = 1 AND u.FNUMB = dc.TargetFNUMB THEN 1.0
              -- تسهيم: به نسبت مصرف
              WHEN dc.Mode = 2
                   THEN u.UsedQty / NULLIF(SUM(u.UsedQty) OVER (PARTITION BY u.Code), 0)
              ELSE 0
            END AS Ratio
    INTO    #Share
    FROM    Usage u
    JOIN    VarByCode                v  ON v.Code  = u.Code
    JOIN    dbo.CC_VarianceDecision  dc ON dc.Code = u.Code AND dc.RunId = @RunId
    WHERE   dc.Mode IN (1, 2);

    DELETE #Share WHERE Ratio IS NULL OR Ratio = 0;

    IF @WhatIf = 1
    BEGIN
        SELECT  s.FNUMB                              AS شماره_فرمول,
                s.Code                               AS کد_ماده,
                st.NAME                              AS نام_ماده,
                CASE s.Mode WHEN 1 THEN N'اختصاص'
                            ELSE N'تسهيم' END        AS حالت,
                s.QtyVariance                        AS کل_انحراف,
                s.Ratio                              AS سهم,
                s.QtyVariance * s.Ratio              AS مقدار_سهم,
                p.ProdQty                            AS مقدار_توليد,
                s.QtyVariance * s.Ratio / p.ProdQty  AS افزايش_در_فرمول
        FROM    #Share s
        JOIN    #Prod  p  ON p.FNUMB = s.FNUMB
        LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = s.Code
        ORDER BY ABS(s.QtyVariance * s.Ratio) DESC;
        RETURN;
    END

    BEGIN TRAN;

    UPDATE  d
       SET  d.MEGHk = d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty),
            d.MABLK = ROUND(ISNULL(d.SMABL, 0) *
                            (d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty)), 0)
    OUTPUT  @RunId, 'S09', inserted.FNUMB,
            NULL, TRY_CAST(inserted.CODE AS BIGINT), 'MEGHk',
            deleted.MEGHk, inserted.MEGHk,
            N'تخصيص انحراف مصرف'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.DTL_MANF d
    JOIN    #Share s ON s.FNUMB = d.FNUMB
                    AND s.Code  = CAST(d.CODE AS BIGINT)
    JOIN    #Prod  p ON p.FNUMB = d.FNUMB;

    DECLARE @n INT = @@ROWCOUNT;

    ---- ثبت مقدار اعمال‌شده در تصميم‌ها
    UPDATE  dc
       SET  dc.AppliedQty = x.Applied
    FROM    dbo.CC_VarianceDecision dc
    JOIN   (SELECT Code, SUM(QtyVariance * Ratio) AS Applied
            FROM   #Share GROUP BY Code) x ON x.Code = dc.Code
    WHERE   dc.RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S09', 1,
            CONCAT(N'تخصيص انحراف: ', @n, N' سطر فرمول به‌روز شد'));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (S09_ApplyDecisions.ApplyResult)
    SELECT @n AS Value;
END
GO


PRINT N'رويه‌هاي S07 تا S09 ايجاد شدند.';
GO
