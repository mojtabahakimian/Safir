-- =====================================================================
-- 010_Pay2_SeniorityBase_Split_Preview.sql
-- ---------------------------------------------------------------------
-- هدف: انتقال «پایه سنوات» که فعلاً داخل BASE_SAL (حقوق اسمی) ادغام شده
--       به آیتم مستقل SANOVAT_PAYE، فقط برای احکام فعال.
--
-- ⚠️ هشدارهای ایمنی (طبق دستور کارفرما):
--   • این اسکریپت در Program.cs ثبت نشده و به‌صورت خودکار اجرا نمی‌شود.
--   • هیچ مبلغ ثابتی (مثل 166667) کورکورانه از همه کم نمی‌شود.
--   • Runهای قطعی و تاریخچه (PAY2_RUN / PAY2_RUN_LINE / PAY2_RUN_DETAIL)
--     به‌هیچ‌وجه لمس نمی‌شوند؛ فقط PAY2_DECREE_LINE احکامِ فعال هدف است.
--   • چون منبع قطعیِ «مبلغ سنوات هر پرسنل» در داده موجود نیست،
--     بخش APPLY به‌صورت پیش‌فرض غیرفعال است و بدون منبع معتبر اجرا نمی‌شود.
--   • اسکریپت Idempotent و Transactional است؛ اجرای چندباره نتیجهٔ تکراری نمی‌سازد.
--
-- نحوهٔ استفاده:
--   1) این اسکریپت را با @APPLY = 0 اجرا کنید تا فقط Preview را ببینید.
--   2) جدول #SenioritySource را با مبلغ سنوات واقعیِ هر پرسنل (از منبع معتبر) پر کنید.
--   3) پس از صحت‌سنجی Preview، @APPLY = 1 قرار دهید و دوباره اجرا کنید.
-- =====================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

-- ── پارامترهای کنترلی ───────────────────────────────────────────────
DECLARE @APPLY BIT = 0;   -- 0 = فقط Preview | 1 = اعمال واقعی (نیازمند منبع معتبر)

-- =====================================================================
-- 1) اطمینان Idempotent از وجود آیتم SANOVAT_PAYE (مطابق تعریف ScriptSqly)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'SANOVAT_PAYE')
    INSERT INTO PAY2_ITEM_DEF
        (ITEM_CODE, ITEM_NAME, ITEM_TYPE, CALC_BASIS, INS_SUBJECT,
         TAX_SUBJECT, INS_BASE_DAYS, PAY_BASE_DAYS, IS_SYSTEM, SORT_ORDER)
    VALUES
        ('SANOVAT_PAYE', N'پایه سنوات', 1, 1, 1, 1, 1, 2, 1, 25);

DECLARE @BASE_ITEM_ID INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'BASE_SAL');
DECLARE @SANV_ITEM_ID INT = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'SANOVAT_PAYE');

-- =====================================================================
-- 2) منبع مبلغ سنوات هر پرسنل
--    این جدول موقت باید توسط اپراتور با داده معتبر پر شود:
--       EMP_ID = شناسه پرسنل
--       SENIORITY_AMOUNT = مبلغ سنوات که هم‌اکنون داخل BASE_SAL همان حکم فعال است
--    اگر خالی بماند، هیچ تغییری اعمال نمی‌شود (کمبود منبع → توقف ایمن).
-- =====================================================================
IF OBJECT_ID('tempdb..#SenioritySource') IS NULL
    CREATE TABLE #SenioritySource
    (
        EMP_ID           INT           NOT NULL PRIMARY KEY,
        SENIORITY_AMOUNT DECIMAL(18,2) NOT NULL
    );

-- نمونه (باید توسط اپراتور از منبع معتبر پر شود):
-- INSERT INTO #SenioritySource (EMP_ID, SENIORITY_AMOUNT) VALUES (9, 166667);

-- =====================================================================
-- 3) احکام فعال هدف = آخرین حکمِ باز (EFF_TO IS NULL) هر پرسنل که دارای BASE_SAL است
-- =====================================================================
IF OBJECT_ID('tempdb..#Targets') IS NOT NULL DROP TABLE #Targets;

;WITH ActiveDecree AS
(
    SELECT d.DEC_ID, d.EMP_ID,
           ROW_NUMBER() OVER (PARTITION BY d.EMP_ID ORDER BY d.EFF_FROM DESC, d.DEC_ID DESC) AS rn
    FROM PAY2_DECREE d
    WHERE d.EFF_TO IS NULL          -- فقط احکام فعال/باز
)
SELECT
    ad.EMP_ID,
    ad.DEC_ID,
    dl_base.AMOUNT                              AS CURRENT_BASE_SAL,
    src.SENIORITY_AMOUNT                        AS SENIORITY_AMOUNT,
    (dl_base.AMOUNT - ISNULL(src.SENIORITY_AMOUNT, 0)) AS NEW_BASE_SAL,
    CAST(CASE WHEN dl_sanv.DEC_ID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS ALREADY_SPLIT,
    CAST(CASE WHEN src.EMP_ID IS NULL THEN 1 ELSE 0 END AS BIT)         AS SOURCE_MISSING
INTO #Targets
FROM ActiveDecree ad
INNER JOIN PAY2_DECREE_LINE dl_base
        ON dl_base.DEC_ID = ad.DEC_ID AND dl_base.ITEM_ID = @BASE_ITEM_ID
LEFT JOIN PAY2_DECREE_LINE dl_sanv
        ON dl_sanv.DEC_ID = ad.DEC_ID AND dl_sanv.ITEM_ID = @SANV_ITEM_ID
LEFT JOIN #SenioritySource src
        ON src.EMP_ID = ad.EMP_ID
WHERE ad.rn = 1;

-- =====================================================================
-- 4) PREVIEW — خروجی صرفاً گزارشی (بدون هیچ تغییری در داده)
-- =====================================================================
SELECT
    t.EMP_ID,
    t.DEC_ID,
    t.CURRENT_BASE_SAL,
    t.SENIORITY_AMOUNT,
    t.NEW_BASE_SAL,
    t.ALREADY_SPLIT,
    t.SOURCE_MISSING,
    CASE
        WHEN t.ALREADY_SPLIT = 1        THEN N'قبلاً تفکیک شده — نادیده گرفته می‌شود'
        WHEN t.SOURCE_MISSING = 1       THEN N'منبع مبلغ سنوات موجود نیست — اعمال نمی‌شود'
        WHEN t.SENIORITY_AMOUNT <= 0    THEN N'مبلغ سنوات نامعتبر (<=0) — اعمال نمی‌شود'
        WHEN t.NEW_BASE_SAL < 0         THEN N'سنوات از پایه بزرگ‌تر است — اعمال نمی‌شود'
        ELSE                                 N'آماده اعمال'
    END AS STATUS
FROM #Targets t
ORDER BY t.EMP_ID;

-- گزارش صریح کمبود منبع
DECLARE @MissingCount INT = (SELECT COUNT(*) FROM #Targets WHERE SOURCE_MISSING = 1 AND ALREADY_SPLIT = 0);
IF @MissingCount > 0
    PRINT N'⚠️ برای ' + CAST(@MissingCount AS NVARCHAR(10))
        + N' حکم فعال، منبع مبلغ سنوات موجود نیست. این موارد اعمال نخواهند شد.';

-- =====================================================================
-- 5) APPLY — اعمال واقعی (Transactional / Idempotent)
--    فقط ردیف‌هایی که «آماده اعمال» هستند تغییر می‌کنند.
--    مجموع (BASE_SAL جدید + SANOVAT_PAYE) دقیقاً برابر BASE_SAL قبلی می‌ماند.
-- =====================================================================
IF @APPLY = 1
BEGIN
    DECLARE @Ready INT = (SELECT COUNT(*) FROM #Targets
                          WHERE ALREADY_SPLIT = 0 AND SOURCE_MISSING = 0
                            AND SENIORITY_AMOUNT > 0 AND NEW_BASE_SAL >= 0);

    IF @Ready = 0
    BEGIN
        PRINT N'هیچ حکم قابل‌اعمالی یافت نشد (منبع سنوات موجود نیست یا قبلاً تفکیک شده). هیچ تغییری انجام نشد.';
    END
    ELSE
    BEGIN
        BEGIN TRAN;
        BEGIN TRY
            -- درج آیتم SANOVAT_PAYE برای احکام آماده (Idempotent: فقط اگر وجود نداشته باشد)
            INSERT INTO PAY2_DECREE_LINE (DEC_ID, ITEM_ID, AMOUNT)
            SELECT t.DEC_ID, @SANV_ITEM_ID, t.SENIORITY_AMOUNT
            FROM #Targets t
            WHERE t.ALREADY_SPLIT = 0 AND t.SOURCE_MISSING = 0
              AND t.SENIORITY_AMOUNT > 0 AND t.NEW_BASE_SAL >= 0;

            -- کسر همان مبلغ از BASE_SAL همان حکم (تراز کامل مجموع)
            UPDATE dl
            SET dl.AMOUNT = dl.AMOUNT - t.SENIORITY_AMOUNT
            FROM PAY2_DECREE_LINE dl
            INNER JOIN #Targets t
                    ON t.DEC_ID = dl.DEC_ID AND dl.ITEM_ID = @BASE_ITEM_ID
            WHERE t.ALREADY_SPLIT = 0 AND t.SOURCE_MISSING = 0
              AND t.SENIORITY_AMOUNT > 0 AND t.NEW_BASE_SAL >= 0;

            COMMIT TRAN;
            PRINT N'اعمال شد: ' + CAST(@Ready AS NVARCHAR(10)) + N' حکم فعال تفکیک شد (مجموع بدون تغییر).';
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRAN;
            THROW;
        END CATCH
    END
END
ELSE
BEGIN
    PRINT N'حالت Preview (APPLY=0): هیچ تغییری در داده انجام نشد.';
END
GO
