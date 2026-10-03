/* ═══════════════════════════════════════════════════════════════════
   تعریف دسترسی اختصاصی «نبض سازمان» (FORMNAME = PULSE)

   برای اینکه در نرم‌افزار ویندوزی (WPF) در فرم تعریف سطوح دسترسی کاربران،
   با جستجوی «نبض»، گزینه «نبض سازمان» نمایش داده شود و بتوان دسترسی
   اجرا (RUN) و مشاهده (SEE) را مستقلاً به کاربر اختصاص داد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/* ── ۱) تعریف فرم نبض سازمان در TFORMS در صورت عدم وجود ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'PULSE')
BEGIN
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PULSE',
            N'نبض سازمان',
            3,
            ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'TARAZ_4'), 2),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
            GETDATE());
END
GO

/* در صورتی که عنوان قبلاً چیز دیگری بوده، به‌روزرسانی شود */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    UPDATE [dbo].[TFORMS]
    SET CAPTION = N'نبض سازمان'
    WHERE FORMNAME = N'PULSE' AND CAPTION <> N'نبض سازمان';
END
GO

/* ── ۲) افزودن ردیف دسترسی در SAL_CHEK برای همه کاربران ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @PulseId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'PULSE');
    DECLARE @Taraz4Id INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'TARAZ_4');

    IF @PulseId IS NOT NULL
    BEGIN
        INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
        SELECT D.IDD, @PulseId,
               ISNULL(scTaraz.RUN, 0),
               ISNULL(scTaraz.SEE, 0),
               0, 0, 0, GETDATE()
        FROM [dbo].[SALA_DTL] D
        LEFT JOIN [dbo].[SAL_CHEK] scTaraz ON scTaraz.USERCO = D.IDD AND scTaraz.[OBJECT] = @Taraz4Id
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[SAL_CHEK] E
            WHERE E.USERCO = D.IDD AND E.[OBJECT] = @PulseId
        );
    END
END
GO
