/* ═══════════════════════════════════════════════════════════════════
   دستیار هوش مصنوعی: ساخت login فقط‌خواندنی — یک‌بار، به‌دست مدیر سرور

   پیش‌نیاز: 38-ai-readonly-role.sql روی همین پایگاه اجرا شده باشد، و SQL Server
   در حالت «SQL Server and Windows Authentication» (mixed mode) باشد —
   SELECT SERVERPROPERTY('IsIntegratedSecurityOnly') باید 0 بدهد؛ وگرنه login
   ساخته می‌شود ولی نمی‌تواند وصل شود (روی سیستم توسعه همین پیش آمد).
   با کاربری اجرا کنید که sysadmin یا securityadmin است.

   ۱) نام پایگاه و رمز را پایین عوض کنید (رمز را در این فایل ذخیره نکنید).
   ۲) اجرا کنید.
   ۳) در تنظیمات سرور Safir (appsettings یا متغیر محیطی) بگذارید:
        ConnectionStrings:AiReadOnly =
          Data Source=<سرور>;Initial Catalog=<پایگاه>;User Id=safir_ai;Password=<رمز>;TrustServerCertificate=True;
      متغیر محیطی معادل: ConnectionStrings__AiReadOnly
   ۴) Safir را دوباره راه‌اندازی کنید. از این به بعد run_sql دستیار با این
      کاربر اجرا می‌شود: نوشتن و خواندن جدول‌های حساس را خودِ SQL Server رد می‌کند.

   برای حذف: DROP USER safir_ai; (روی پایگاه)  و  DROP LOGIN safir_ai; (روی master)
   ═══════════════════════════════════════════════════════════════════ */

DECLARE @db       SYSNAME       = N'YAZDSEPAR1405';          -- ← نام پایگاه
DECLARE @password NVARCHAR(128) = N'<<یک رمز قوی بگذارید>>';  -- ← رمز

IF @password LIKE N'<<%'
BEGIN
    RAISERROR(N'رمز را در خط بالا عوض کنید.', 16, 1);
    RETURN;
END

-- EXEC(...) فقط رشته و متغیر می‌پذیرد، نه QUOTENAME/REPLACE؛ پس اول در متغیر ساخته می‌شود
DECLARE @sql NVARCHAR(MAX);

IF SUSER_ID(N'safir_ai') IS NULL
BEGIN
    SET @sql = N'CREATE LOGIN safir_ai WITH PASSWORD = N' + QUOTENAME(@password, N'''') +
               N', CHECK_POLICY = ON, DEFAULT_DATABASE = ' + QUOTENAME(@db) + N';';
    EXEC (@sql);
END

SET @sql = N'USE ' + QUOTENAME(@db) + N';
IF DATABASE_PRINCIPAL_ID(N''safir_ai'') IS NULL CREATE USER safir_ai FOR LOGIN safir_ai;
ALTER ROLE safir_ai_reader ADD MEMBER safir_ai;';
EXEC (@sql);

PRINT N'login و کاربر safir_ai ساخته شد و عضو safir_ai_reader است.';
