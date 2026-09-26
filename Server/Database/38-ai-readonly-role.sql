/* ═══════════════════════════════════════════════════════════════════
   دستیار هوش مصنوعی: نقشِ فقط‌خواندنی

   ── چرا ──
   ابزار run_sql دستیار کوئریِ نوشته‌ی مدل را اجرا می‌کند. نگهبانِ کد
   (AiSqlGuard) فقط SELECT می‌پذیرد و جدول‌های حساس را رد می‌کند، ولی سدّ
   واقعی باید خودِ SQL Server باشد: کاربری که اصلاً اجازه‌ی نوشتن و دیدنِ
   آن جدول‌ها را ندارد.

   ── این اسکریپت فقط «نقش» را می‌سازد ──
   login در سطح سرور است و رمز می‌خواهد؛ آن را مدیر سرور با
   tools/ai_readonly_login.sql جدا می‌سازد و بعد
   ConnectionStrings:AiReadOnly را در تنظیمات Safir می‌گذارد. تا وقتی آن
   نباشد، دستیار با اتصالِ عادی برنامه کار می‌کند و رفتار عوض نمی‌شود.

   ── چه چیزی بسته است ──
   رمز و نام کاربران (SALA_DTL و viewهای SALS/SALSUSER)، مجوزها (SAL_CHEK)،
   کلید و لاگ دستیار (AI_*)، حقوق پرسنل (PAY2_* و V_PAY2_*)، حقوق قدیمیِ WPF
   (PERSONEL، PHOKM، WORKING، … و تابع‌های فیش و لیست حقوق) — همان
   فهرستِ AiSqlGuard. اجرای رویه (EXECUTE) هم بسته است.
   ⚠ جدولِ PAY2 تازه‌ای که بعداً ساخته شود خودکار بسته نمی‌شود؛ بعد از هر
   به‌روزرسانیِ حقوق، این اسکریپت را دوباره اجرا کنید (تکرارش بی‌خطر است).

   اگر کاربرِ اجراکننده اجازه‌ی ساختن نقش نداشته باشد، فقط پیام می‌دهد و
   بقیه‌ی به‌روزرسانی را متوقف نمی‌کند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

BEGIN TRY
    IF DATABASE_PRINCIPAL_ID('safir_ai_reader') IS NULL
        CREATE ROLE safir_ai_reader;

    GRANT SELECT ON SCHEMA::dbo TO safir_ai_reader;
    DENY  EXECUTE ON SCHEMA::dbo TO safir_ai_reader;

    -- جدول‌های حساس: کاربران، دستیار، حقوق جدید (PAY2) و حقوق قدیمی WPF
    DECLARE @denied TABLE (object_id INT PRIMARY KEY);
    INSERT @denied (object_id)
    SELECT o.object_id
    FROM   sys.objects o
    WHERE  o.type IN ('U', 'V', 'IF', 'TF')
      AND  (o.name IN (N'SALA_DTL', N'SAL_CHEK', N'SALS', N'SALSUSER',
                       N'AI_Config', N'AI_UserAccess', N'AI_ChatLog', N'AI_Conversation',
                       N'PERSONEL', N'PHOKM', N'PGHARAR', N'WORKING', N'WORKHEAD', N'PVAM', N'PVAM_BAZ',
                       N'SALARY_BEDTT', N'SALARY_BESTT', N'SALARY_EYDY', N'ONE_SALARY', N'PMORAKH',
                       N'PLIST_MALIAT', N'DSKKAR00', N'DSKWOR00')
            OR o.name IN (N'LASTHOKM', N'LASTHOK2', N'SELECT_HOKM')
            OR o.name LIKE N'PAY2[_]%'
            OR o.name LIKE N'V[_]PAY2[_]%'
            -- تابع‌های فیش و لیست حقوق WPF. عمداً نه «هر چیزی که این جدول‌ها را می‌خواند»:
            -- آن قاعده QSL_INVOICE_FROOSH، RASID_ANBAR و چند view فروش و انبار دیگر را هم
            -- می‌بست، چون فقط برای نام کاربر به SALA_DTL وصل‌اند.
            OR o.name LIKE N'LIST[_]SALARY%'
            OR o.name LIKE N'LISTSALARY%'
            OR o.name LIKE N'Q[_]FISH[_]%');

    DECLARE @sql NVARCHAR(MAX) = N'';
    SELECT @sql += N'DENY SELECT ON ' + QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name)
                 + N' TO safir_ai_reader;' + NCHAR(10)
    FROM   sys.objects o
    JOIN   @denied x ON x.object_id = o.object_id;

    EXEC sys.sp_executesql @sql;

    PRINT N'نقش safir_ai_reader آماده شد.';
END TRY
BEGIN CATCH
    PRINT N'نقش safir_ai_reader ساخته نشد (احتمالاً مجوز کافی نیست): ' + ERROR_MESSAGE();
END CATCH
GO
