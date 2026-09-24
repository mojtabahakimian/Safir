/* ============================================================================
   دسترسی کاربران آزمایشی به بهای تمام‌شده — فقط برای دیتابیس تست

   ⚠️ این فایل را هرگز روی دیتابیس واقعی اجرا نکنید.

   فرم‌های COST_* را مهاجرت بهای تمام‌شده (ScriptSqly.CostClose.cs) در TFORMS
   می‌سازد، پس این فایل باید **بعد از** آن مهاجرت اجرا شود. بدون آن، payadmin
   که روی حقوق همه‌کاره است در همهٔ صفحه‌های بهای تمام‌شده ۴۰۳ می‌گیرد و هیچ
   بخشی از آن ماژول در آزمون سرتاسری قابل رسیدن نیست.

   | کاربر     | دسترسی روی COST_*        |
   |-----------|--------------------------|
   | payadmin  | هر پنج مجوز               |
   | payviewer | فقط RUN+SEE (فقط‌خواندنی) |
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME LIKE N'COST[_]%')
    THROW 53100, N'فرم‌های COST_* نیستند — اول مهاجرت بهای تمام‌شده را اجرا کنید.', 1;
GO

DELETE C FROM dbo.SAL_CHEK C
JOIN dbo.TFORMS F ON F.IDH = C.[OBJECT]
WHERE C.USERCO IN (9001, 9002) AND F.FORMNAME LIKE N'COST[_]%';

INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9001, IDH, 1, 1, 1, 1, 1, GETDATE() FROM dbo.TFORMS WHERE FORMNAME LIKE N'COST[_]%';

INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
SELECT 9002, IDH, 1, 1, 0, 0, 0, GETDATE() FROM dbo.TFORMS WHERE FORMNAME LIKE N'COST[_]%';
GO

PRINT N'✅ دسترسی بهای تمام‌شده برای payadmin (کامل) و payviewer (فقط‌خواندنی) ثبت شد.';
GO
