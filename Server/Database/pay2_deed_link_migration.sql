-- پیوند دوره‌ی حقوق به سندِ حسابداری با DEED_HED.base (ثابت) به‌جای شماره‌ی سند N_S.
-- چرا: بازشماره‌گذاریِ اسناد در نرم‌افزار WPF (CC_sp_S04_SortDeeds) N_S را عوض می‌کند و
-- PAY2_PERIOD جزو جدول‌های فرزندِ آن (ON UPDATE CASCADE) نیست؛ «لغو صدور» سندِ حقوق را
-- پاک نمی‌کرد و بازصدور سندِ دوم می‌ساخت.
-- مو‌به‌مو یکی با بلوک «DEED_BASE» در External/ScriptSqly/ScriptSqly.Core/ScriptSqly.Salary.cs.
-- تکرارش بی‌خطر است.

IF COL_LENGTH('dbo.PAY2_PERIOD', 'DEED_BASE') IS NULL
    ALTER TABLE dbo.PAY2_PERIOD ADD DEED_BASE INT NULL;
GO

-- فقط پیوندهایی که همین حالا معتبرند (سندی با همان شماره هست و عنوانش سندِ حقوقِ همین دوره
-- است) به base تبدیل می‌شوند. پیوندهای کهنه/سندهای یتیم را صدورِ بعدی با عنوان پیدا می‌کند.
-- دیتابیسِ فقط‌حقوق (بدون DEED_HED) چیزی برای وصل کردن ندارد.
IF OBJECT_ID('dbo.DEED_HED') IS NOT NULL
BEGIN
    UPDATE p SET p.DEED_BASE = h.base
    FROM dbo.PAY2_PERIOD p
    JOIN dbo.DEED_HED h ON h.N_S = p.DEED_N_S_PAY
    WHERE p.DEED_BASE IS NULL
      AND p.DEED_N_S_PAY > 0
      AND h.SHARH_S = N'سند حقوق و دستمزد دوره ' + CAST(p.PERIOD_DATE AS NVARCHAR(20));
END
GO
