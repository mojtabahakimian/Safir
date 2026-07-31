/* ============================================================================
   خروجی گرفتن از داده‌های حقوق و دستمزد برای ابزار راستی‌آزمایی pay2_audit.py

   طرز استفاده در SSMS:
     ۱. این اسکریپت را روی دیتابیس اجرا کنید.
     ۲. روی تنها سلول نتیجه راست‌کلیک کنید و «Save Results As…» را بزنید.
     ۳. با نام pay2_export.json ذخیره کنید.
     ۴. اجرا کنید:  python3 Server/Tools/pay2_audit.py pay2_export.json

   نکته: اگر خروجی SSMS سطرها را شکست، مشکلی نیست — ابزار خودش
         شکست‌های سطر را نادیده می‌گیرد.

   نکته امنیتی: این خروجی داده‌های واقعی پرسنل (نام، کد ملی، حقوق) دارد.
         بعد از بررسی، فایل را پاک کنید و در گیت کامیتش نکنید.

   اگر تعداد فیش‌ها زیاد است، متغیر @PerIdFrom را مقدار بدهید تا فقط
   دوره‌های جدیدتر خروجی بگیرند.
   ============================================================================ */

DECLARE @PerIdFrom INT = 0;   -- ۰ یعنی همه دوره‌ها

SELECT (
    SELECT
        (SELECT * FROM dbo.PAY2_CONFIG        FOR JSON PATH) AS [PAY2_CONFIG],
        (SELECT * FROM dbo.PAY2_TAX_BRACKET   FOR JSON PATH) AS [PAY2_TAX_BRACKET],
        (SELECT * FROM dbo.PAY2_ITEM_DEF      FOR JSON PATH) AS [PAY2_ITEM_DEF],
        (SELECT * FROM dbo.PAY2_EMPLOYEE      FOR JSON PATH) AS [PAY2_EMPLOYEE],

        (SELECT * FROM dbo.PAY2_PERIOD
          WHERE PER_ID >= @PerIdFrom          FOR JSON PATH) AS [PAY2_PERIOD],

        (SELECT R.* FROM dbo.PAY2_RUN R
          WHERE R.PER_ID >= @PerIdFrom        FOR JSON PATH) AS [PAY2_RUN],

        (SELECT RL.* FROM dbo.PAY2_RUN_LINE RL
           INNER JOIN dbo.PAY2_RUN R ON R.RUN_ID = RL.RUN_ID
          WHERE R.PER_ID >= @PerIdFrom        FOR JSON PATH) AS [PAY2_RUN_LINE],

        (SELECT RD.* FROM dbo.PAY2_RUN_DETAIL RD
           INNER JOIN dbo.PAY2_RUN R ON R.RUN_ID = RD.RUN_ID
          WHERE R.PER_ID >= @PerIdFrom        FOR JSON PATH) AS [PAY2_RUN_DETAIL],

        (SELECT * FROM dbo.PAY2_ATTENDANCE
          WHERE PER_ID >= @PerIdFrom          FOR JSON PATH) AS [PAY2_ATTENDANCE],

        (SELECT * FROM dbo.PAY2_DECREE        FOR JSON PATH) AS [PAY2_DECREE],
        (SELECT * FROM dbo.PAY2_DECREE_LINE   FOR JSON PATH) AS [PAY2_DECREE_LINE]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS [pay2_export];
