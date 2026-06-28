-- ============================================================================
-- PAY2 v6.1 — پشتیبانی از مبنای محاسبه «ساعتی» و حق شیفت درصدی از حقوق ماهیانه
--
-- این اسکریپت پس از PAY2_DDL_v6.sql و PAY2_Procedures_v6.sql اجرا می‌شود
-- (یا محتوای آن جایگزین SP_PAY2_CALC_RUN در اسکریپت SalaryScript گردد).
--
-- تغییرات نسبت به v6.0:
--   ۱. CALC_BASIS / BASIS_OV مقدار 3 = «ساعتی» را می‌پذیرد (قید CK_CALC_BASIS باز شد)
--   ۲. در موتور محاسبه، آیتم حکم با مبنای ساعتی این‌گونه محاسبه می‌شود:
--        OT_NORMAL  → نرخ ساعتی حکم × OT_NORMAL_H کارکرد
--        OT_HOLIDAY → نرخ ساعتی حکم × OT_HOLIDAY_H کارکرد
--        OT_ADMIN   → نرخ ساعتی حکم × OT_ADMIN_H کارکرد
--        سایر       → نرخ ساعتی حکم × (روز کارکرد × OT_HOUR_BASE)
--      توجه: نرخ ساعتی ثبت‌شده در حکم نرخ نهایی است و ضریب 1.4 روی آن اعمال نمی‌شود.
--   ۳. اگر آیتم اضافه‌کار صریحاً در حکم باشد، محاسبه خودکار از روی حقوق پایه
--      انجام نمی‌شود (جلوگیری از دوبار منظور شدن).
--   ۴. حق شیفت (SHIFT) با SHIFT_MODE=PCT اکنون «درصدی از حقوق پایه ماهیانه»
--      (نرخ روزانه × ۳۰) با تناسب روز کارکرد است — فرمول قبلی عملاً درصدی از
--      دستمزد روزانه را می‌داد. در SHIFT_MODE=FIXED مبلغ ثابت روزشمار است.
--   ۵. رفع باگ موروثی v6.0: نشت متغیرهای استثنا (PAY2_OVERRIDE) از یک آیتم به
--      آیتم‌های بعدی همان حکم وقتی برای آیتم بعدی استثنایی وجود نداشت
--      + افزودن ORDER BY به TOP 1 برای انتخاب قطعیِ جدیدترین استثنا.
--
-- ⚠️ نکته مهاجرت: پس از اجرای این اسکریپت، مقدار آیتم «حق شیفت» در احکامِ موجود
--    به معنای «درصد از حقوق پایه ماهیانه» تفسیر می‌شود؛ مقادیر قبلی را بازبینی کنید.
-- ============================================================================

-- ── ۱. باز کردن قید CK_CALC_BASIS برای مقدار 3 (ساعتی) ─────────────────────
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_CALC_BASIS'
             AND parent_object_id = OBJECT_ID(N'dbo.PAY2_ITEM_DEF')
             AND definition NOT LIKE '%(3)%')
BEGIN
    ALTER TABLE dbo.PAY2_ITEM_DEF DROP CONSTRAINT CK_CALC_BASIS;
    ALTER TABLE dbo.PAY2_ITEM_DEF ADD CONSTRAINT CK_CALC_BASIS CHECK ([CALC_BASIS] IN (1,2,3));
END;
GO

-- ── ۲. سایر قیدهای احتمالی روی BASIS_OV که مقدار 3 را مجاز نمی‌دانند ────────
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) IN ('PAY2_DECREE_LINE', 'PAY2_OVERRIDE', 'PAY2_ITEM_TMPL_LINE')
  AND cc.definition LIKE '%BASIS_OV%'
  AND cc.definition NOT LIKE '%(3)%';

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

-- ── ۳. موتور محاسبه حقوق — نسخه v6.1 ────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CALC_RUN]
    @WS_ID       INT,
    @PER_ID      INT,
    @PAYROLL_N_S FLOAT,
    @CALC_BY     INT          = NULL,
    @IS_RERUN    BIT          = 0,
    @NEW_RUN_ID  INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET ANSI_WARNINGS OFF;

    -- گام ۱ — بارگذاری تنظیمات
    DECLARE
        @MONTH_DAYS_MODE   NVARCHAR(10), @MONTH_DAYS        TINYINT,
        @OT_NORMAL_MULT    DECIMAL(6,4), @OT_HOLIDAY_MULT   DECIMAL(6,4),
        @OT_HOUR_BASE      DECIMAL(6,4), @SHIFT_MODE        NVARCHAR(10),
        @ROUND_MODE        INT,          @INS_WORKER_RATE   DECIMAL(6,4),
        @INS_EMPLOYER_RATE DECIMAL(6,4), @INS_UNEMP_RATE    DECIMAL(6,4),
        @INS_CEILING_APPLY BIT,          @INS_CEILING       BIGINT,
        @TAX_YEAR          SMALLINT,     @TAX_EXEMPT        BIGINT,
        @TAX_DEDUCT_INS    BIT,          @TAX_DEP_APPLY     BIT,
        @ADV_ENABLED       BIT,          @PERIOD_DATE       BIGINT,
        @PERIOD_MONTH      INT;

    SELECT
        @MONTH_DAYS_MODE   = ISNULL(MAX(CASE WHEN CFG_KEY='MONTH_DAYS_MODE'    THEN CFG_VALUE END), '30'),
        @OT_NORMAL_MULT    = ISNULL(MAX(CASE WHEN CFG_KEY='OT_NORMAL_MULT'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOLIDAY_MULT   = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOLIDAY_MULT'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOUR_BASE      = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOUR_BASE'       THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 7.33),
        @SHIFT_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='SHIFT_MODE'         THEN CFG_VALUE END), 'PCT'),
        @ROUND_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='ROUND_MODE'         THEN CAST(CFG_VALUE AS INT) END), 1),
        @INS_WORKER_RATE   = ISNULL(MAX(CASE WHEN CFG_KEY='INS_WORKER_RATE'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.07),
        @INS_EMPLOYER_RATE = ISNULL(MAX(CASE WHEN CFG_KEY='INS_EMPLOYER_RATE'  THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.20),
        @INS_UNEMP_RATE    = ISNULL(MAX(CASE WHEN CFG_KEY='INS_UNEMP_RATE'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.03),
        @INS_CEILING       = ISNULL(MAX(CASE WHEN CFG_KEY='INS_CEILING_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 999999999),
        @TAX_YEAR          = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'           THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT        = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @INS_CEILING_APPLY = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='INS_CEILING_APPLY'  THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEDUCT_INS    = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEDUCT_INS'     THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEP_APPLY     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEPRIVATION_APPLY' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @ADV_ENABLED       = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='ADV_ENABLED'        THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0)
    FROM PAY2_CONFIG
    WHERE CFG_KEY IN (
        'MONTH_DAYS_MODE','OT_NORMAL_MULT','OT_HOLIDAY_MULT','OT_HOUR_BASE',
        'SHIFT_MODE','ROUND_MODE','INS_WORKER_RATE','INS_EMPLOYER_RATE',
        'INS_UNEMP_RATE','INS_CEILING_APPLY','INS_CEILING_MONTHLY',
        'TAX_YEAR','TAX_EXEMPT_MONTHLY','TAX_DEDUCT_INS',
        'TAX_DEPRIVATION_APPLY','ADV_ENABLED'
    );

    SELECT @PERIOD_DATE = PERIOD_DATE FROM PAY2_PERIOD WHERE PER_ID = @PER_ID;
    IF @PERIOD_DATE IS NULL
    BEGIN
        RAISERROR(N'دوره %d یافت نشد.', 16, 1, @PER_ID);
        RETURN;
    END;

    SET @MONTH_DAYS = CASE WHEN @MONTH_DAYS_MODE = '30' THEN 30 ELSE 30 END;
    SET @PERIOD_MONTH = @PERIOD_DATE / 100;  

    -- گام ۲ — ایجاد هدر PAY2_RUN
    DECLARE @PREV_RUN_ID INT = NULL;
    DECLARE @NEXT_RUN_NO SMALLINT = 1;

    IF @IS_RERUN = 1
    BEGIN
        SELECT TOP 1
            @PREV_RUN_ID = RUN_ID,
            @NEXT_RUN_NO = RUN_NO + 1
        FROM PAY2_RUN
        WHERE PER_ID = @PER_ID AND IS_LATEST = 1
        ORDER BY RUN_NO DESC;

        UPDATE PAY2_RUN SET IS_LATEST = 0 WHERE PER_ID = @PER_ID;
    END;

    INSERT INTO PAY2_RUN (PER_ID, RUN_NO, IS_LATEST, CALC_AT, CALC_BY, STATUS, PREV_RUN_ID)
    VALUES (@PER_ID, @NEXT_RUN_NO, 1, GETDATE(), @CALC_BY, 1, @PREV_RUN_ID);

    SET @NEW_RUN_ID = SCOPE_IDENTITY();

    CREATE TABLE #AdvResult (
        EMP_ID             INT, PCODE NVARCHAR(50), FULL_NAME NVARCHAR(150),
        RAW_BALANCE        BIGINT, MANUAL_EXCL BIGINT, ADVANCE_DEDUCTION  BIGINT
    );

    IF @ADV_ENABLED = 1
    BEGIN
        INSERT INTO #AdvResult (EMP_ID, PCODE, FULL_NAME, RAW_BALANCE, MANUAL_EXCL, ADVANCE_DEDUCTION)
        EXEC SP_PAY2_GET_ADVANCES
            @PERIOD_DATE = @PERIOD_DATE,
            @PAYROLL_N_S = @PAYROLL_N_S,
            @WS_ID       = @WS_ID;
    END;

    -- گام ۳ — حلقه روی پرسنل فعال کارگاه
    DECLARE
        @EMP_ID INT, @IS_MANAGER BIT, @INS_TYPE TINYINT,
        @TAX_EXEMPT_FLAG BIT, @REGION_DEP TINYINT, @ACC_T NVARCHAR(50); 

    DECLARE @WS_SHIFT_MODE NVARCHAR(10);
    SELECT @WS_SHIFT_MODE = NULLIF(SHIFT_MODE, N'') FROM PAY2_WORKSHOP WHERE WS_ID = @WS_ID;

    DECLARE cur_emp CURSOR LOCAL FAST_FORWARD FOR
        SELECT E.EMP_ID, E.IS_MANAGER, E.INS_TYPE, E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.ACC_T
        FROM PAY2_EMPLOYEE E
        WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
          AND EXISTS (SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID = @PER_ID AND A.EMP_ID = E.EMP_ID);

    OPEN cur_emp;
    FETCH NEXT FROM cur_emp INTO @EMP_ID, @IS_MANAGER, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE
            @WORK_DAYS DECIMAL(5,2)=0, @DAYS DECIMAL(5,2)=0, @DAYSB DECIMAL(5,2)=0,
            @FRID_COUNT TINYINT=0, @TDAYS DECIMAL(5,2)=0, @OT_NORMAL_H DECIMAL(6,2)=0,
            @OT_HOLIDAY_H DECIMAL(6,2)=0, @OT_ADMIN_H DECIMAL(6,2)=0, @LEAVE_DAYS DECIMAL(5,2)=0,
            @PERF_AMOUNT BIGINT=0, @TRANSP_AMOUNT BIGINT=0, @KASR_OTHER BIGINT=0;

        SELECT
            @WORK_DAYS = ISNULL(WORK_DAYS,0), @DAYS = ISNULL(DAYS,0), @DAYSB = ISNULL(DAYSB,0),
            @FRID_COUNT = ISNULL(FRID_COUNT,0), @TDAYS = ISNULL(TDAYS,0), @OT_NORMAL_H = ISNULL(OT_NORMAL_H,0),
            @OT_HOLIDAY_H = ISNULL(OT_HOLIDAY_H,0), @OT_ADMIN_H = ISNULL(OT_ADMIN_H,0), @LEAVE_DAYS = ISNULL(LEAVE_DAYS,0),
            @PERF_AMOUNT = ISNULL(PERF_AMOUNT,0), @TRANSP_AMOUNT = ISNULL(TRANSP_AMOUNT,0), @KASR_OTHER = ISNULL(KASR_OTHER,0)
        FROM PAY2_ATTENDANCE WHERE PER_ID = @PER_ID AND EMP_ID = @EMP_ID;

        CREATE TABLE #ItemCalc (
            ITEM_ID INT, ITEM_CODE NVARCHAR(30), ITEM_TYPE TINYINT,
            AMOUNT BIGINT, INS_AMOUNT BIGINT, INS_SUBJECT BIT, TAX_SUBJECT BIT
        );

        -- گام ۴ — یافتن حکم معتبر
        DECLARE @DEC_ID INT, @DEC_FROM BIGINT, @DEC_TO BIGINT, @DEC_SHIFT_MODE NVARCHAR(10);

        DECLARE cur_dec CURSOR LOCAL FAST_FORWARD FOR
            SELECT DEC_ID, EFF_FROM, ISNULL(EFF_TO, 99991231), NULLIF(SHIFT_MODE, N'')
            FROM PAY2_DECREE
            WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1
              AND EFF_FROM <= @PERIOD_DATE + 30   
              AND (EFF_TO IS NULL OR EFF_TO >= @PERIOD_DATE)
            ORDER BY EFF_FROM;

        OPEN cur_dec;
        FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- گام ۵ — محاسبه هر آیتم حکم
            DECLARE
                @ITEM_ID INT, @ITEM_CODE NVARCHAR(30), @ITEM_TYPE TINYINT, @ITEM_AMOUNT DECIMAL(18,2),
                @ITEM_BASIS TINYINT, @ITEM_INS BIT, @ITEM_TAX BIT, @ITEM_PBD TINYINT, @ITEM_IBD TINYINT, @DL_SHIFT_MODE_OV NVARCHAR(10),
                @OV_INS BIT, @OV_TAX BIT, @OV_BASIS TINYINT, @CALC_AMOUNT BIGINT = 0, @INS_CALC_AMOUNT BIGINT = 0;

            DECLARE cur_line CURSOR LOCAL FAST_FORWARD FOR
                SELECT DL.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, ISNULL(DL.AMOUNT, 0),
                    DL.SHIFT_MODE_OV,
                    ISNULL(DL.BASIS_OV, ID.CALC_BASIS), ISNULL(DL.INS_OV, ID.INS_SUBJECT), ISNULL(DL.TAX_OV, ID.TAX_SUBJECT), ID.PAY_BASE_DAYS, ID.INS_BASE_DAYS
                FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
                WHERE DL.DEC_ID = @DEC_ID AND ID.IS_ACTIVE = 1 AND ID.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                ORDER BY ID.SORT_ORDER;

            OPEN cur_line;
            FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                -- v6.1: ریست متغیرها قبل از خواندن استثنا — اگر SELECT ردیفی برنگرداند،
                -- مقادیر استثنای آیتم قبلی نباید به این آیتم نشت کند (باگ موروثی v6.0)
                SELECT @OV_INS = NULL, @OV_TAX = NULL, @OV_BASIS = NULL;

                SELECT TOP 1 @OV_INS = INS_OV, @OV_TAX = TAX_OV, @OV_BASIS = BASIS_OV
                FROM PAY2_OVERRIDE WHERE EMP_ID = @EMP_ID AND ITEM_ID = @ITEM_ID
                  AND VALID_FROM <= @PERIOD_DATE AND (VALID_TO IS NULL OR VALID_TO >= @PERIOD_DATE)
                ORDER BY VALID_FROM DESC;

                IF @OV_INS IS NOT NULL SET @ITEM_INS = @OV_INS;
                IF @OV_TAX IS NOT NULL SET @ITEM_TAX = @OV_TAX;
                IF @OV_BASIS IS NOT NULL SET @ITEM_BASIS = @OV_BASIS;

                DECLARE @INS_DAYS DECIMAL(5,2) = CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END;

                -- v6.1: مبنای ساعتی (3) — مبلغ ثبت‌شده در حکم، «نرخ هر ساعت» است
                IF @ITEM_BASIS = 3
                BEGIN
                    SET @CALC_AMOUNT =
                        CASE @ITEM_CODE
                            WHEN 'OT_NORMAL'  THEN CAST(@ITEM_AMOUNT * @OT_NORMAL_H  AS BIGINT)
                            WHEN 'OT_HOLIDAY' THEN CAST(@ITEM_AMOUNT * @OT_HOLIDAY_H AS BIGINT)
                            WHEN 'OT_ADMIN'   THEN CAST(@ITEM_AMOUNT * @OT_ADMIN_H   AS BIGINT)
                            -- سایر آیتم‌های ساعتی: نرخ ساعتی × (روز کارکرد × ساعت کاری روزانه)
                            ELSE CAST(@ITEM_AMOUNT * (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @OT_HOUR_BASE AS BIGINT)
                        END;
                    SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                END;
                ELSE IF @ITEM_BASIS = 1 
                BEGIN
                    DECLARE @PAY_DAYS DECIMAL(5,2) = CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END;

                    IF @ITEM_CODE IN ('HOME','CHILDREN','GROCERY')
                    BEGIN
                        SET @CALC_AMOUNT = CASE WHEN @PAY_DAYS >= 28 THEN @ITEM_AMOUNT ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS / 30.0 AS BIGINT) END;
                        SET @INS_CALC_AMOUNT = CASE WHEN @INS_DAYS >= 28 THEN @ITEM_AMOUNT ELSE CAST(@ITEM_AMOUNT * @INS_DAYS / 30.0 AS BIGINT) END;
                    END;
                    ELSE IF @ITEM_CODE = 'NAHAR'
                    BEGIN
                        DECLARE @NAHAR_DAYS DECIMAL(5,2) = @DAYSB - @FRID_COUNT - @LEAVE_DAYS + @TDAYS;
                        SET @CALC_AMOUNT = CASE WHEN @NAHAR_DAYS > 0 THEN CAST(@ITEM_AMOUNT * @NAHAR_DAYS AS BIGINT) ELSE CAST(@ITEM_AMOUNT * @DAYSB AS BIGINT) END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END;
                    ELSE IF @ITEM_CODE = 'SHIFT'
                    BEGIN
                        DECLARE @EFF_SHIFT_MODE NVARCHAR(10) = COALESCE(NULLIF(@DL_SHIFT_MODE_OV, N''), @DEC_SHIFT_MODE, @WS_SHIFT_MODE, @SHIFT_MODE, 'PCT');
                        IF @EFF_SHIFT_MODE = 'FIXED'
                            -- حالت مبلغ ثابت: مانند سایر آیتم‌های روزانه با تناسب روز کارکرد
                            SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                        ELSE
                        BEGIN
                            -- v6.1: درصدی از حقوق پایه «ماهیانه» (نرخ روزانه × 30) با تناسب روز کارکرد
                            -- @BASE_SAL_B محاسبه‌شده = نرخ روزانه × DAYSB  →  درست محاسبه شده
                            -- NOTE: این منطق در Server/Info/PAY2_Procedures_v6.sql هم وجود دارد؛ تغییر باید در هر دو فایل اعمال شود
                            DECLARE @BASE_SAL_B BIGINT = ISNULL((SELECT TOP 1 AMOUNT FROM #ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B'), 0);
                            SET @CALC_AMOUNT = CAST(ROUND(@BASE_SAL_B * @ITEM_AMOUNT / 100.0, 0) AS BIGINT);
                        END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END;
                    ELSE
                    BEGIN
                        SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END;
                END;
                ELSE 
                BEGIN
                    SET @CALC_AMOUNT = ISNULL(@ITEM_AMOUNT, 0);
                    SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                END;

                INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
                VALUES (@ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @CALC_AMOUNT, @INS_CALC_AMOUNT, @ITEM_INS, @ITEM_TAX);

                FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;
            END;
            CLOSE cur_line; DEALLOCATE cur_line;

            FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;
        END;
        CLOSE cur_dec; DEALLOCATE cur_dec;

        -- گام ۶ — افزودن آیتم‌های متغیر
        -- v6.1: اگر آیتم اضافه‌کار به صورت صریح در حکم تعریف شده باشد (مثلاً با نرخ ساعتی)،
        -- محاسبه خودکار آن از روی حقوق پایه انجام نمی‌شود تا دوبار منظور نگردد
        DECLARE @BASE_SAL_IN_MONTH BIGINT = ISNULL((SELECT TOP 1 AMOUNT FROM #ItemCalc WHERE ITEM_CODE = 'BASE_SAL' OR ITEM_CODE = 'BASE_SAL_B'), 0);
        DECLARE @EFFECTIVE_HOURLY DECIMAL(18,2) = 0;
        IF @DAYSB > 0 AND @OT_HOUR_BASE > 0
            SET @EFFECTIVE_HOURLY = ISNULL((CAST(@BASE_SAL_IN_MONTH AS DECIMAL(18,2)) / @DAYSB) / NULLIF(@OT_HOUR_BASE, 0), 0);

        IF @OT_NORMAL_H > 0 AND NOT EXISTS (SELECT 1 FROM #ItemCalc WHERE ITEM_CODE = 'OT_NORMAL')
        BEGIN
            DECLARE @OT_NORMAL_AMT BIGINT = ISNULL(CAST(@EFFECTIVE_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), 0);
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_NORMAL', 2, @OT_NORMAL_AMT, @OT_NORMAL_AMT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_NORMAL';
        END;

        IF @OT_HOLIDAY_H > 0 AND NOT EXISTS (SELECT 1 FROM #ItemCalc WHERE ITEM_CODE = 'OT_HOLIDAY')
        BEGIN
            DECLARE @OT_HOLIDAY_AMT BIGINT = ISNULL(CAST(@EFFECTIVE_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), 0);
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_HOLIDAY', 2, @OT_HOLIDAY_AMT, @OT_HOLIDAY_AMT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_HOLIDAY';
        END;

        IF @OT_ADMIN_H > 0 AND NOT EXISTS (SELECT 1 FROM #ItemCalc WHERE ITEM_CODE = 'OT_ADMIN')
        BEGIN
            DECLARE @OT_ADMIN_AMT BIGINT = ISNULL(CAST(@EFFECTIVE_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), 0);
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_ADMIN', 2, @OT_ADMIN_AMT, @OT_ADMIN_AMT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_ADMIN';
        END;

        IF @PERF_AMOUNT > 0
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'PERF_BONUS', 2, @PERF_AMOUNT, @PERF_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'PERF_BONUS';

        IF @TRANSP_AMOUNT > 0
            INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'TRANSP', 2, @TRANSP_AMOUNT, @TRANSP_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'TRANSP';

        INSERT INTO #ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT AV.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, AV.VALUE, AV.VALUE, ID.INS_SUBJECT, ID.TAX_SUBJECT
        FROM PAY2_ATT_VALUE AV INNER JOIN PAY2_ITEM_DEF ID ON AV.ITEM_ID = ID.ITEM_ID
        WHERE AV.PER_ID = @PER_ID AND AV.EMP_ID = @EMP_ID AND AV.VALUE <> 0
          AND NOT EXISTS (SELECT 1 FROM #ItemCalc X WHERE X.ITEM_ID = AV.ITEM_ID);

        -- گام ۷ — محاسبه بیمه
        DECLARE @GROSS_PAY BIGINT=0, @INS_BASE BIGINT=0, @INS_WORKER BIGINT=0, @INS_EMPLOYER BIGINT=0;

        SELECT @GROSS_PAY = ISNULL(SUM(AMOUNT), 0) FROM #ItemCalc WHERE ITEM_TYPE IN (1, 2);
        SELECT @INS_BASE = ISNULL(SUM(INS_AMOUNT), 0) FROM #ItemCalc WHERE INS_SUBJECT = 1 AND ITEM_TYPE IN (1, 2);

        IF @INS_CEILING_APPLY = 1 AND @INS_TYPE <> 3
            SET @INS_BASE = CASE WHEN @INS_BASE > @INS_CEILING THEN @INS_CEILING ELSE @INS_BASE END;

        IF @INS_TYPE = 3 
        BEGIN
            SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0;
        END;
        ELSE
        BEGIN
            SET @INS_WORKER = ISNULL(CAST(@INS_BASE * @INS_WORKER_RATE AS BIGINT), 0);
            
            DECLARE @EMP_IS_JANBAZ BIT = ISNULL((SELECT IS_JANBAZ FROM PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID), 0);
            DECLARE @JANBAZ_RATE   DECIMAL(6,4) = ISNULL(CAST((SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY='INS_JANBAZ_RATE') AS DECIMAL(6,4)), 0.18);

            IF @EMP_IS_JANBAZ = 1
                SET @INS_EMPLOYER = ISNULL(CAST(@INS_BASE * @JANBAZ_RATE AS BIGINT), 0); 
            ELSE
                SET @INS_EMPLOYER = ISNULL(CAST(@INS_BASE * (@INS_EMPLOYER_RATE + CASE WHEN ISNULL(@IS_MANAGER,0)=0 THEN @INS_UNEMP_RATE ELSE 0 END) AS BIGINT), 0);
        END;

        -- گام ۸ — محاسبه مالیات
        DECLARE @TAX_BASE BIGINT=0, @TAX_AMOUNT BIGINT=0;

        IF @TAX_EXEMPT_FLAG = 1
        BEGIN
            SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        END;
        ELSE
        BEGIN
            SELECT @TAX_BASE = ISNULL(SUM(AMOUNT), 0) FROM #ItemCalc WHERE TAX_SUBJECT = 1 AND ITEM_TYPE IN (1, 2);
            IF @TAX_DEDUCT_INS = 1 SET @TAX_BASE = @TAX_BASE - @INS_WORKER;
            SET @TAX_BASE = CASE WHEN @TAX_BASE > @TAX_EXEMPT THEN @TAX_BASE - @TAX_EXEMPT ELSE 0 END;
            IF @TAX_DEP_APPLY = 1 AND @REGION_DEP > 0 SET @TAX_BASE = CAST(@TAX_BASE * (1.0 - @REGION_DEP / 100.0) AS BIGINT);
            SET @TAX_AMOUNT = ISNULL([dbo].[FN_PAY2_CALC_TAX](@TAX_BASE * 12, @TAX_YEAR) / 12, 0);
            IF @TAX_AMOUNT < 0 SET @TAX_AMOUNT = 0;
        END;

        -- گام ۹ — مساعده هوشمند
        DECLARE @ADVANCE_DED BIGINT = 0;
        IF @ADV_ENABLED = 1
            SELECT @ADVANCE_DED = ISNULL(ADVANCE_DEDUCTION, 0) FROM #AdvResult WHERE EMP_ID = @EMP_ID;

        -- گام ۱۰ — کسر وام خودکار
        DECLARE @LOAN_DED BIGINT = 0;
        SELECT @LOAN_DED = ISNULL(SUM(LS.AMOUNT), 0) FROM PAY2_LOAN_SCHED LS INNER JOIN PAY2_LOAN L ON LS.LOAN_ID = L.LOAN_ID
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1 AND LS.DUE_PERIOD = @PERIOD_DATE AND LS.RUN_ID IS NULL;

        DECLARE @OTHER_DED BIGINT = ISNULL(@KASR_OTHER, 0);

        -- گام ۱۱ — محاسبه خالص
        DECLARE @TOTAL_DED BIGINT = @INS_WORKER + @TAX_AMOUNT + @LOAN_DED + @ADVANCE_DED + @OTHER_DED;
        DECLARE @NET_PAY BIGINT = @GROSS_PAY - @TOTAL_DED;

        IF @ROUND_MODE > 1
            SET @NET_PAY = ISNULL(ROUND(@NET_PAY / CAST(@ROUND_MODE AS FLOAT), 0) * @ROUND_MODE, 0);

        -- گام ۱۲ — ذخیره نتایج
        DECLARE @LEAVE_BAL_DAYS DECIMAL(5,2) = NULL;
        SELECT @LEAVE_BAL_DAYS = CAST(BALANCE_MIN AS DECIMAL(10,2)) / 440.0 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;

        DECLARE @LOAN_BAL BIGINT = NULL;
        SELECT @LOAN_BAL = ISNULL(SUM(BALANCE), 0) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID;

        INSERT INTO PAY2_RUN_LINE (
            RUN_ID, EMP_ID, WORK_DAYS, GROSS_PAY, INS_BASE, INS_WORKER, INS_EMPLOYER, TAX_BASE, TAX_AMOUNT,
            LOAN_DED, ADVANCE_DED, OTHER_DED, TOTAL_DED, NET_PAY, LEAVE_BAL_DAYS, LOAN_BALANCE, ADVANCE_BALANCE_SNAP
        ) VALUES (
            @NEW_RUN_ID, @EMP_ID, @DAYSB, @GROSS_PAY, @INS_BASE, @INS_WORKER, @INS_EMPLOYER, @TAX_BASE, @TAX_AMOUNT,
            @LOAN_DED, @ADVANCE_DED, @OTHER_DED, @TOTAL_DED, @NET_PAY, @LEAVE_BAL_DAYS, @LOAN_BAL, @ADVANCE_DED
        );

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT @NEW_RUN_ID, @EMP_ID, ITEM_ID, ISNULL(AMOUNT,0), ISNULL(INS_SUBJECT,0), ISNULL(TAX_SUBJECT,0) FROM #ItemCalc;

        DECLARE @INS_DED_ID  INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='INS_DED');
        DECLARE @TAX_DED_ID  INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='TAX_DED');
        DECLARE @LOAN_DED_ID INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='LOAN_DED');
        DECLARE @ADV_DED_ID  INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='ADVANCE_DED');

        -- 🚀 رفع باگ T-SQL 213: افزودن نام ستون‌ها در INSERT VALUES 🚀
        IF @INS_WORKER  > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@INS_DED_ID, @INS_WORKER, 0,0);
        IF @TAX_AMOUNT  > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@TAX_DED_ID, @TAX_AMOUNT, 0,0);
        IF @LOAN_DED    > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@LOAN_DED_ID,@LOAN_DED,   0,0);
        IF @ADVANCE_DED > 0 INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@ADV_DED_ID, @ADVANCE_DED,0,0);

        UPDATE PAY2_LOAN_SCHED SET RUN_ID = @NEW_RUN_ID, PAID_AT = GETDATE()
        WHERE DUE_PERIOD = @PERIOD_DATE AND RUN_ID IS NULL AND LOAN_ID IN (SELECT LOAN_ID FROM PAY2_LOAN WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1);

        UPDATE PAY2_LOAN SET PAID_INST = PAID_INST + (SELECT COUNT(*) FROM PAY2_LOAN_SCHED WHERE LOAN_ID=PAY2_LOAN.LOAN_ID AND DUE_PERIOD=@PERIOD_DATE AND RUN_ID=@NEW_RUN_ID)
        WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1;

        DECLARE @LEAVE_MIN_USED INT = CAST(@LEAVE_DAYS * 440 AS INT);
        IF @LEAVE_MIN_USED > 0
        BEGIN
            UPDATE PAY2_LEAVE_BAL SET USED_MIN = USED_MIN + @LEAVE_MIN_USED, UPDATED_AT = GETDATE()
            WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;
        END;

        DROP TABLE #ItemCalc;

        FETCH NEXT FROM cur_emp INTO @EMP_ID, @IS_MANAGER, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;
    END;

    CLOSE cur_emp; DEALLOCATE cur_emp;
    DROP TABLE #AdvResult;

    UPDATE PAY2_PERIOD SET STATUS = 3 WHERE PER_ID = @PER_ID;

END;
GO
