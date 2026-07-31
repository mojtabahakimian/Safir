/* ============================================================================
   جدول‌های ورود و دسترسی + سه کاربر آزمایشی — فقط برای دیتابیس تست

   ⚠️ این فایل را هرگز روی دیتابیس واقعی اجرا نکنید. رمزها ساده و عمومی‌اند.

   جدول‌های خالی را test_auth_tables.sql می‌سازد؛ این فایل فقط داده می‌ریزد.
   باید **بعد از** pay2_acl_migration.sql اجرا شود، چون به ردیف‌های PAY2_%
   در TFORMS نیاز دارد که مهاجرت می‌سازدشان.

   ترتیب اجرا
   ------------
       1) legacy_dependencies.sql
       2) schema.sql
       3) test_auth_tables.sql        ← ساخت جدول‌های خالی ورود
       4) pay2_runtime_procedures.sql
       5) pay2_acl_migration.sql      ← ردیف‌های TFORMS مربوط به PAY2
       6) pay2_seed.sql
       7) این فایل

   کاربران ساخته‌شده
   ------------------
   | نام کاربری  | رمز    | نقش                                              |
   |-------------|--------|--------------------------------------------------|
   | payadmin    | 111111 | دسترسی کامل به همه فرم‌های PAY2 و همه کارگاه‌ها    |
   | payviewer   | 222222 | فقط RUN+SEE (بدون درج/ویرایش/حذف)، همه کارگاه‌ها  |
   | payscoped   | 333333 | دسترسی کامل ولی فقط محدود به کارگاه شماره ۱      |

   نام کاربری و رمز در SALA_DTL به‌صورت کدشده ذخیره می‌شوند
   (CL_METHODS.DECODEUN / DECODEPS). مقادیر زیر با اجرای واقعی همان توابع
   راستی‌آزمایی شده‌اند.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ── ۱. سه کاربر آزمایشی ─────────────────────────────────────────────── */
-- مقادیر کدشده با اجرای واقعی CL_METHODS.DECODEUN/DECODEPS بررسی شده‌اند.
--
-- ⚠️ رمز payadmin (`111111`) کدشده می‌شود شش آپاستروف: WWW''''''WWW
--    نوشتنش به‌صورت رشته‌ی T-SQL تله دارد، چون هر آپاستروف باید دوبل شود
--    و یک بار همین‌جا نصفه نوشته شد و رمز خراب ذخیره گردید. برای اینکه
--    دوباره تکرار نشود با REPLICATE ساخته می‌شود، نه با رشته‌ی خام.
MERGE dbo.SALA_DTL AS T
USING (VALUES
    (9001, N'\MeMPYUZ',  N'WWW' + REPLICATE(NCHAR(39), 6) + N'WWW', 1, 0),  -- payadmin  / 111111
    (9002, N'\MebUQcQ^', N'WWW((((((WWW',                           1, 0),  -- payviewer / 222222
    (9003, N'\Me_O[\QP', N'WWW))))))WWW',                           1, 0)   -- payscoped / 333333
) AS S ([IDD], [SAL_NAME], [PSAL_NAME], [GRSAL], [ENABL])
    ON T.[IDD] = S.[IDD]
WHEN MATCHED THEN UPDATE SET
    T.[SAL_NAME] = S.[SAL_NAME], T.[PSAL_NAME] = S.[PSAL_NAME],
    T.[GRSAL] = S.[GRSAL], T.[ENABL] = S.[ENABL]
WHEN NOT MATCHED THEN INSERT ([IDD],[SAL_NAME],[PSAL_NAME],[GRSAL],[ENABL])
    VALUES (S.[IDD], S.[SAL_NAME], S.[PSAL_NAME], S.[GRSAL], S.[ENABL]);
GO

/* راستی‌آزمایی: اگر pay2_acl_migration.sql اجرا نشده باشد هیچ فرم PAY2ای
   وجود ندارد و دسترسی‌ها ساخته نمی‌شوند — پس همین‌جا متوقف شو. */
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%')
    THROW 54000, N'ابتدا pay2_acl_migration.sql را اجرا کنید — هیچ فرم PAY2 در TFORMS نیست.', 1;
GO

/* ── ۲. دسترسی فرم‌ها ─────────────────────────────────────────────────── */
DELETE FROM dbo.SAL_CHEK WHERE USERCO IN (9001, 9002, 9003);

-- payadmin — همه‌ی پنج مجوز روی همه‌ی فرم‌های PAY2
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9001, IDH, 1, 1, 1, 1, 1, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%';

-- payviewer — فقط باز کردن و دیدن؛ هیچ تغییری مجاز نیست
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9002, IDH, 1, 1, 0, 0, 0, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%';

-- payscoped — دسترسی کامل به فرم‌ها، ولی در گام بعد فقط به یک کارگاه وصل می‌شود
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9003, IDH, 1, 1, 1, 1, 1, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%';
GO

/* ── ۳. محدوده‌ی کارگاه ──────────────────────────────────────────────── */
-- pay2_seed.sql فقط یک کارگاه دارد. با یک کارگاه، «محدود به کارگاه ۱» و
-- «همه‌ی کارگاه‌ها» عملاً یکی می‌شوند و آزمونِ محدودسازی هیچ‌وقت شکست
-- نمی‌خورد — یعنی چیزی را ثابت نمی‌کند. پس یک کارگاه دوم اضافه می‌کنیم
-- تا تفاوت واقعاً قابل مشاهده باشد.
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_WORKSHOP WHERE WS_CODE = N'TEST-2')
BEGIN
    SET IDENTITY_INSERT dbo.PAY2_WORKSHOP ON;
    INSERT INTO dbo.PAY2_WORKSHOP ([WS_ID], [WS_CODE], [WS_NAME], [INS_MODE], [IS_ACTIVE])
    VALUES (2, N'TEST-2', N'کارگاه آزمایشی ۲ (برای آزمون محدودسازی)', 1, 1);
    SET IDENTITY_INSERT dbo.PAY2_WORKSHOP OFF;
END
GO

DELETE FROM dbo.PAY2_USER_WS WHERE USERCO IN (9001, 9002, 9003);

-- payadmin و payviewer به همه‌ی کارگاه‌ها
INSERT INTO dbo.PAY2_USER_WS (USERCO, WS_ID, CRT)
SELECT U.USERCO, W.WS_ID, GETDATE()
FROM (VALUES (9001), (9002)) AS U(USERCO)
CROSS JOIN dbo.PAY2_WORKSHOP W;

-- payscoped فقط به کارگاه شماره ۱ — اینجاست که محدودسازی سطر آزمایش می‌شود
INSERT INTO dbo.PAY2_USER_WS (USERCO, WS_ID, CRT)
SELECT 9003, W.WS_ID, GETDATE()
FROM dbo.PAY2_WORKSHOP W WHERE W.WS_ID = 1;
GO

/* ── ۴. کنترل دسترسی را روشن کن ──────────────────────────────────────── */
-- در دیتابیس واقعی پیش‌فرض ۰ (خاموش) است تا چیزی ناگهان قطع نشود؛
-- ولی در دیتابیس تست باید روشن باشد وگرنه اصلاً چیزی آزمایش نمی‌شود.
--
-- ⚠️ اول وجود کلیدها را چک می‌کنیم. اگر pay2_acl_migration.sql اجرا نشده
--    باشد — یا اگر pay2_seed.sql بعد از آن اجرا شده و با DELETE کل
--    PAY2_CONFIG را خالی کرده باشد — این UPDATE ها روی صفر ردیف اثر
--    می‌کنند و بی‌صدا رد می‌شوند. نتیجه‌اش این است که کنترل دسترسی خاموش
--    می‌ماند و همه‌ی تست‌ها سبز به‌نظر می‌رسند در حالی که هیچ چیزی
--    آزمایش نشده. پس همین‌جا با خطا متوقف شو.
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY = N'ACL_ENFORCE')
    THROW 54005, N'کلید ACL_ENFORCE در PAY2_CONFIG نیست — مهاجرت ACL باید بعد از pay2_seed.sql اجرا شود.', 1;
GO

UPDATE dbo.PAY2_CONFIG SET CFG_VALUE = N'1' WHERE CFG_KEY = N'ACL_ENFORCE';
UPDATE dbo.PAY2_CONFIG SET CFG_VALUE = N'1' WHERE CFG_KEY = N'ACL_WS_SCOPE_ENFORCE';

IF (SELECT CFG_VALUE FROM dbo.PAY2_CONFIG WHERE CFG_KEY = N'ACL_ENFORCE') <> N'1'
    THROW 54006, N'ACL_ENFORCE روشن نشد.', 1;
GO

/* ── ۵. گزارش نهایی ──────────────────────────────────────────────────── */
IF (SELECT COUNT(*) FROM dbo.SALA_DTL WHERE IDD IN (9001,9002,9003)) <> 3
    THROW 54001, N'کاربران آزمایشی ساخته نشدند.', 1;

-- هر سه رمز کدشده باید دقیقاً ۱۲ کاراکتر باشند (۳ + ۶ + ۳).
-- اگر آپاستروف‌های رمز در رشته‌ی T-SQL نصفه نوشته شوند، اینجا لو می‌رود
-- به‌جای اینکه بعداً «رمز اشتباه» بگیرید و دنبال جای دیگری بگردید.
IF EXISTS (SELECT 1 FROM dbo.SALA_DTL
            WHERE IDD IN (9001,9002,9003) AND LEN(PSAL_NAME) <> 12)
    THROW 54004, N'رمز کدشده طول درستی ندارد — احتمالاً آپاستروف‌ها در رشته escape نشده‌اند.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK WHERE USERCO = 9002 AND [INP] = 0)
    THROW 54002, N'دسترسی محدود payviewer درست ثبت نشد.', 1;
-- اگر این دو برابر باشند، آزمون محدودسازی کارگاه بی‌معنا می‌شود.
IF (SELECT COUNT(*) FROM dbo.PAY2_USER_WS WHERE USERCO = 9001)
   <= (SELECT COUNT(*) FROM dbo.PAY2_USER_WS WHERE USERCO = 9003)
    THROW 54003, N'payadmin باید کارگاه‌های بیشتری از payscoped داشته باشد وگرنه محدودسازی قابل آزمایش نیست.', 1;

SELECT  D.IDD                                            AS UserId,
        CASE D.IDD WHEN 9001 THEN N'payadmin'
                   WHEN 9002 THEN N'payviewer'
                   ELSE           N'payscoped' END       AS UserName,
        COUNT(DISTINCT C.[OBJECT])                       AS Pay2Forms,
        SUM(CAST(C.[INP] AS INT))                        AS FormsWithInsert,
        (SELECT COUNT(*) FROM dbo.PAY2_USER_WS W
          WHERE W.USERCO = D.IDD)                        AS AllowedWorkshops
FROM dbo.SALA_DTL D
LEFT JOIN dbo.SAL_CHEK C ON C.USERCO = D.IDD
WHERE D.IDD IN (9001, 9002, 9003)
GROUP BY D.IDD
ORDER BY D.IDD;

PRINT N'✅ کاربران آزمایشی و دسترسی‌هایشان ساخته شدند. ACL_ENFORCE = 1';
GO
