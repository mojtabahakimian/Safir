/* ═══════════════════════════════════════════════════════════════════════
   تنظیمات ماژول بستن ماه و دستیار — انتقال به پایگاه دیگر

   از YAZDSEPAR1405 روی DESKTOP-GLPOA91\SQL2022
   گرفته شده در 2026-09-10 18:57.

   ── چه می‌کند ──
   هر ردیفِ تنظیمات را اگر نبود می‌سازد و اگر بود به‌روز می‌کند (MERGE).
   هیچ ردیفی را حذف نمی‌کند: تنظیمی که روی مقصد هست و اینجا نیست،
   دست‌نخورده می‌ماند.

   ── چه چیزی نمی‌آورد ──
   داده‌ی اجرا: CC_Run، CC_RunLog، CC_RunStep، CC_Exception، CC_Snapshot،
   CC_ItemCost، CC_ItemMargin*، CC_Variance*، CC_FormulaChange و
   CC_ConversionCost (ستون RunId دارد، یعنی به‌ازای هر اجراست نه تنظیم).

   ⚠️ کلید سرویس هوش مصنوعی (AI_Config.ApiKey) عمداً اینجا نیست — کلید
      نباید در فایلی بگردد که کپی و ایمیل می‌شود. بعد از اجرا از صفحه‌ی
      «تنظیمات سرویس هوش مصنوعی» ثبتش کنید.

   ── پیش از اجرا ──
   ۱) جدول‌ها باید از قبل ساخته شده باشند (ScriptSqly).
   ۲) روی پایگاه مقصد اجرا کنید.
   ۳) تراکنش دارد: یا همه می‌نشیند یا هیچ‌کدام.

   ⚠️ شناسه‌ها (UnitId و Id) عیناً منتقل می‌شوند تا ارجاع میان جدول‌ها
      نشکند. اگر مقصد از قبل تنظیماتِ متفاوتی با همان شناسه‌ها دارد،
      بازنویسی می‌شوند — اول پشتیبان بگیرید.

   این فایل تولیدشده است؛ دستی ویرایشش نکنید.
   ═══════════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRAN;
GO


/* ──────────────────────────────────────────────────────────────────
   CC_Unit — واحدهای تولیدی   (2 ردیف)
   ────────────────────────────────────────────────────────────────── */
SET IDENTITY_INSERT dbo.CC_Unit ON;
MERGE dbo.CC_Unit AS t
USING (VALUES
  (1, N'واحد اصلی', 1, 1, 1, 1),
  (2, N'واحد یزد', 2, 1, 1, 2)
) AS s ([UnitId], [UnitName], [Depatman], [SplitMode], [IsActive], [SeqNo])
ON t.[UnitId] = s.[UnitId]
WHEN MATCHED THEN UPDATE SET
    t.[UnitName] = s.[UnitName],
    t.[Depatman] = s.[Depatman],
    t.[SplitMode] = s.[SplitMode],
    t.[IsActive] = s.[IsActive],
    t.[SeqNo] = s.[SeqNo]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UnitId], [UnitName], [Depatman], [SplitMode], [IsActive], [SeqNo])
    VALUES (s.[UnitId], s.[UnitName], s.[Depatman], s.[SplitMode], s.[IsActive], s.[SeqNo]);
PRINT N'CC_Unit: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';
SET IDENTITY_INSERT dbo.CC_Unit OFF;

/* ──────────────────────────────────────────────────────────────────
   CC_UnitAnbar — انبارهای هر واحد و نقششان   (17 ردیف)
   ────────────────────────────────────────────────────────────────── */
MERGE dbo.CC_UnitAnbar AS t
USING (VALUES
  (1, 1, 2, 0, 2),
  (1, 2, 3, 0, 3),
  (1, 3, 3, 0, 5),
  (1, 4, 4, 0, 10),
  (1, 5, 4, 0, 9),
  (1, 7, 1, 1, 1),
  (1, 8, 1, 1, 2),
  (1, 10, 3, 0, 6),
  (1, 14, 3, 0, 7),
  (1, 15, 3, 0, 1),
  (2, 807, 3, 0, 3),
  (2, 808, 3, 0, 5),
  (2, 809, 4, 0, 1),
  (2, 810, 1, 1, 1),
  (2, 811, 2, 0, 2),
  (2, 812, 4, 0, 5),
  (2, 813, 4, 0, 7)
) AS s ([UnitId], [Anbar], [AnbarRole], [DoStockCount], [SeqNo])
ON t.[UnitId] = s.[UnitId] AND t.[Anbar] = s.[Anbar]
WHEN MATCHED THEN UPDATE SET
    t.[AnbarRole] = s.[AnbarRole],
    t.[DoStockCount] = s.[DoStockCount],
    t.[SeqNo] = s.[SeqNo]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UnitId], [Anbar], [AnbarRole], [DoStockCount], [SeqNo])
    VALUES (s.[UnitId], s.[Anbar], s.[AnbarRole], s.[DoStockCount], s.[SeqNo]);
PRINT N'CC_UnitAnbar: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';

/* ──────────────────────────────────────────────────────────────────
   CC_UnitAcc — حساب‌های هزینه‌ی هر واحد   (8 ردیف)
   ────────────────────────────────────────────────────────────────── */
SET IDENTITY_INSERT dbo.CC_UnitAcc ON;
MERGE dbo.CC_UnitAcc AS t
USING (VALUES
  (1, 1, 711, 1, 1.000000, 1, N'هزينه دستمزد توليد', NULL, NULL),
  (2, 1, 712, 1, 1.000000, 1, N'هزينه دستمزد خدمات — سهم توليدي', NULL, NULL),
  (3, 1, 713, 1, 1.000000, 1, N'هزينه دستمزد اداري — سهم توليدي', NULL, NULL),
  (4, 1, 721, 1, 1.000000, 1, N'ساير هزينه‌هاي توليد', NULL, NULL),
  (5, 1, 723, 1, 1.000000, 1, N'ساير هزينه‌هاي اداري — سهم توليدي', NULL, NULL),
  (6, 1, 745, 1, 1.000000, 1, N'مرکز هزينه ضايعات و ساير', NULL, NULL),
  (7, 2, 743, 1, 1.000000, 1, N'هزينه‌هاي واحد يزد', NULL, NULL),
  (22, 1, 725, 1, 1.000000, 1, NULL, NULL, NULL)
) AS s ([Id], [UnitId], [HesKol], [CostKind], [Ratio], [IsActive], [Note], [HesMoin], [HesTafsili])
ON t.[Id] = s.[Id]
WHEN MATCHED THEN UPDATE SET
    t.[UnitId] = s.[UnitId],
    t.[HesKol] = s.[HesKol],
    t.[CostKind] = s.[CostKind],
    t.[Ratio] = s.[Ratio],
    t.[IsActive] = s.[IsActive],
    t.[Note] = s.[Note],
    t.[HesMoin] = s.[HesMoin],
    t.[HesTafsili] = s.[HesTafsili]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [UnitId], [HesKol], [CostKind], [Ratio], [IsActive], [Note], [HesMoin], [HesTafsili])
    VALUES (s.[Id], s.[UnitId], s.[HesKol], s.[CostKind], s.[Ratio], s.[IsActive], s.[Note], s.[HesMoin], s.[HesTafsili]);
PRINT N'CC_UnitAcc: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';
SET IDENTITY_INSERT dbo.CC_UnitAcc OFF;

/* ──────────────────────────────────────────────────────────────────
   CC_AnbarHes — حساب معین هر انبار   (18 ردیف)
   ────────────────────────────────────────────────────────────────── */
MERGE dbo.CC_AnbarHes AS t
USING (VALUES
  (1, 121, 1, NULL),
  (2, 121, 2, NULL),
  (3, 121, 3, NULL),
  (4, 121, 4, NULL),
  (5, 121, 5, NULL),
  (7, 121, 7, NULL),
  (8, 121, 8, NULL),
  (10, 121, 10, NULL),
  (14, 121, 14, NULL),
  (15, 121, 15, NULL),
  (806, 121, 806, NULL),
  (807, 121, 807, NULL),
  (808, 121, 808, NULL),
  (809, 121, 809, NULL),
  (810, 121, 810, NULL),
  (811, 121, 811, NULL),
  (812, 121, 812, NULL),
  (813, 121, 813, NULL)
) AS s ([Anbar], [HesKol], [HesMoin], [Note])
ON t.[Anbar] = s.[Anbar]
WHEN MATCHED THEN UPDATE SET
    t.[HesKol] = s.[HesKol],
    t.[HesMoin] = s.[HesMoin],
    t.[Note] = s.[Note]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Anbar], [HesKol], [HesMoin], [Note])
    VALUES (s.[Anbar], s.[HesKol], s.[HesMoin], s.[Note]);
PRINT N'CC_AnbarHes: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';

/* ──────────────────────────────────────────────────────────────────
   CC_ExpenseAcc — حساب‌های هزینه   (2 ردیف)
   ────────────────────────────────────────────────────────────────── */
SET IDENTITY_INSERT dbo.CC_ExpenseAcc ON;
MERGE dbo.CC_ExpenseAcc AS t
USING (VALUES
  (1, 1, 714, NULL, NULL, 1.000000, 1, N'هزینه دستمزد توزیع و فروش'),
  (2, 1, 724, NULL, NULL, 1.000000, 1, N'سایر هزینه های توزیع و فروش')
) AS s ([Id], [ExpenseKind], [HesKol], [HesMoin], [HesTafsili], [Ratio], [IsActive], [Note])
ON t.[Id] = s.[Id]
WHEN MATCHED THEN UPDATE SET
    t.[ExpenseKind] = s.[ExpenseKind],
    t.[HesKol] = s.[HesKol],
    t.[HesMoin] = s.[HesMoin],
    t.[HesTafsili] = s.[HesTafsili],
    t.[Ratio] = s.[Ratio],
    t.[IsActive] = s.[IsActive],
    t.[Note] = s.[Note]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [ExpenseKind], [HesKol], [HesMoin], [HesTafsili], [Ratio], [IsActive], [Note])
    VALUES (s.[Id], s.[ExpenseKind], s.[HesKol], s.[HesMoin], s.[HesTafsili], s.[Ratio], s.[IsActive], s.[Note]);
PRINT N'CC_ExpenseAcc: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';
SET IDENTITY_INSERT dbo.CC_ExpenseAcc OFF;

/* ──────────────────────────────────────────────────────────────────
   CC_LaborAbsorptionRate — ضریب جذب دستمزد و سربار هر کالا   (141 ردیف)
   ────────────────────────────────────────────────────────────────── */
MERGE dbo.CC_LaborAbsorptionRate AS t
USING (VALUES
  (1, N'1', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'10', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'1732', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'1742', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'1786', 4.0460000000000001e-003, 0.0000000000000000e+000, 0, NULL),
  (1, N'1787', 0.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'1795', NULL, 0.0000000000000000e+000, 1, NULL),
  (1, N'1830', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2319', 5.0000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'2534', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2537', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2586', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2587', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2603', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'2648', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2649', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2657', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2658', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2660', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2735', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'2765', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2769', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2786', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2791', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2793', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2807', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2812', 1.7999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'2813', 5.0000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'2889', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'2912', 5.0000000000000000e-001, 0.0000000000000000e+000, 1, NULL),
  (1, N'3', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3100', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3127', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3160', 4.0000000000000002e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3170', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3204', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3206', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3217', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3220', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3221', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3222', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3223', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3224', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3225', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3226', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3227', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3228', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3249', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3253', 1.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3258', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3263', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3271', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3286', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'33', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3301', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3316', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3320', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3322', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3329', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3330', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3331', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3365', NULL, 0.0000000000000000e+000, 1, NULL),
  (1, N'3369', 5.0000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3465', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3469', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3479', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3480', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3495', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3499', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3514', NULL, 0.0000000000000000e+000, 1, NULL),
  (1, N'3515', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3518', 8.0000000000000004e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3526', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'3540', 1.7999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3541', 5.0000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'3559', NULL, 0.0000000000000000e+000, 1, NULL),
  (1, N'3561', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'366', 2.9999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'368', 2.9999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'373', 0.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'45', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'48', 5.0000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'5', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'82', 8.0000000000000004e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'88', 1.7999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'89', 1.7999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'90', 1.7999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (1, N'93', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'94', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (1, N'95', 1.7999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'16', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'1761', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'19', 2.5000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'20', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'2165', 1.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'2626', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'2882', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'29', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'30', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3076', 2.7000000000000002e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3080', 7.0000000000000007e-002, 0.0000000000000000e+000, 0, NULL),
  (2, N'3118', 1.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3119', 1.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3123', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3129', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3132', 1.4999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3135', 1.3000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3142', 5.9999999999999998e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3294', 4.0000000000000002e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3300', 1.4999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3342', 2.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3348', 2.2500000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3349', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3350', 9.0000000000000002e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3351', 3.8000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3352', 1.0000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3353', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3360', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3445', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3446', 2.9999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3447', 2.9999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3457', 9.0000000000000002e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3459', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3460', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3470', 1.4999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3471', 2.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3477', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3490', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3493', 2.9999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3496', 5.9999999999999998e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3500', 2.3000000000000001e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3503', 2.5000000000000000e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3511', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3513', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3521', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3538', 2.3999999999999999e-001, 0.0000000000000000e+000, 0, NULL),
  (2, N'3539', 1.3400000000000001e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3542', 1.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3555', 1.5000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'3562', 1.3000000000000000e+000, 0.0000000000000000e+000, 0, NULL),
  (2, N'373', 0.0000000000000000e+000, 0.0000000000000000e+000, 0, NULL)
) AS s ([UnitId], [CODE], [Coefficient], [OverheadCoefficient], [IsFixed], [Note])
ON t.[UnitId] = s.[UnitId] AND t.[CODE] = s.[CODE]
WHEN MATCHED THEN UPDATE SET
    t.[Coefficient] = s.[Coefficient],
    t.[OverheadCoefficient] = s.[OverheadCoefficient],
    t.[IsFixed] = s.[IsFixed],
    t.[Note] = s.[Note]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UnitId], [CODE], [Coefficient], [OverheadCoefficient], [IsFixed], [Note])
    VALUES (s.[UnitId], s.[CODE], s.[Coefficient], s.[OverheadCoefficient], s.[IsFixed], s.[Note]);
PRINT N'CC_LaborAbsorptionRate: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';

/* ──────────────────────────────────────────────────────────────────
   CC_MarginTarget — هدف حاشیه سود   (67 ردیف)
   ────────────────────────────────────────────────────────────────── */
SET IDENTITY_INSERT dbo.CC_MarginTarget ON;
MERGE dbo.CC_MarginTarget AS t
USING (VALUES
  (1, 368, 4, NULL, NULL, NULL, 0, NULL),
  (2, 366, 4, NULL, NULL, NULL, 0, NULL),
  (3, 2735, 4, NULL, NULL, NULL, 0, NULL),
  (4, 3357, 4, NULL, NULL, NULL, 0, NULL),
  (5, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (6, 85, 4, NULL, NULL, NULL, 0, NULL),
  (7, 3514, 4, NULL, NULL, NULL, 0, NULL),
  (8, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (9, 368, 4, NULL, NULL, NULL, 0, NULL),
  (10, 366, 4, NULL, NULL, NULL, 0, NULL),
  (11, 2735, 4, NULL, NULL, NULL, 0, NULL),
  (12, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (13, 85, 4, NULL, NULL, NULL, 0, NULL),
  (14, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (15, 368, 4, NULL, NULL, NULL, 0, NULL),
  (16, 2735, 4, NULL, NULL, NULL, 0, NULL),
  (17, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (18, 85, 4, NULL, NULL, NULL, 0, NULL),
  (19, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (20, 368, 4, NULL, NULL, NULL, 0, NULL),
  (21, 2735, 4, NULL, NULL, NULL, 0, NULL),
  (22, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (23, 85, 4, NULL, NULL, NULL, 0, NULL),
  (24, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (25, 368, 4, NULL, NULL, NULL, 0, NULL),
  (26, 85, 4, NULL, NULL, NULL, 0, NULL),
  (27, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (28, 368, 4, NULL, NULL, NULL, 0, NULL),
  (29, 3294, 4, NULL, NULL, NULL, 0, NULL),
  (30, 3471, 4, NULL, NULL, NULL, 0, NULL),
  (31, 29, 4, NULL, NULL, NULL, 0, NULL),
  (32, 3135, 4, NULL, NULL, NULL, 0, NULL),
  (33, 3123, 4, NULL, NULL, NULL, 0, NULL),
  (34, 3353, 4, NULL, NULL, NULL, 0, NULL),
  (35, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (36, 85, 4, NULL, NULL, NULL, 0, NULL),
  (37, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (38, 368, 4, NULL, NULL, NULL, 0, NULL),
  (39, 3471, 4, NULL, NULL, NULL, 0, NULL),
  (40, 29, 4, NULL, NULL, NULL, 0, NULL),
  (41, 3135, 4, NULL, NULL, NULL, 0, NULL),
  (42, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (43, 85, 4, NULL, NULL, NULL, 0, NULL),
  (44, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (45, 3294, 4, NULL, NULL, NULL, 0, NULL),
  (46, 3353, 4, NULL, NULL, NULL, 0, NULL),
  (47, 3123, 4, NULL, NULL, NULL, 0, NULL),
  (48, 368, 4, NULL, NULL, NULL, 0, NULL),
  (49, 3471, 4, NULL, NULL, NULL, 0, NULL),
  (50, 29, 4, NULL, NULL, NULL, 0, NULL),
  (51, 3135, 4, NULL, NULL, NULL, 0, NULL),
  (52, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (53, 85, 4, NULL, NULL, NULL, 0, NULL),
  (54, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (55, 3294, 4, NULL, NULL, NULL, 0, NULL),
  (56, 3353, 4, NULL, NULL, NULL, 0, NULL),
  (57, 3123, 4, NULL, NULL, NULL, 0, NULL),
  (58, 368, 4, NULL, NULL, NULL, 0, NULL),
  (59, 3471, 4, NULL, NULL, NULL, 0, NULL),
  (60, 29, 4, NULL, NULL, NULL, 0, NULL),
  (61, 3135, 4, NULL, NULL, NULL, 0, NULL),
  (62, 2977, 4, NULL, NULL, NULL, 0, NULL),
  (63, 85, 4, NULL, NULL, NULL, 0, NULL),
  (64, 1733, 4, NULL, NULL, NULL, 0, NULL),
  (65, 3294, 4, NULL, NULL, NULL, 0, NULL),
  (66, 3353, 4, NULL, NULL, NULL, 0, NULL),
  (67, 3123, 4, NULL, NULL, NULL, 0, NULL)
) AS s ([Id], [Code], [TargetKind], [TargetPct], [BalancingCode], [BalancingFNUMB], [IsActive], [Note])
ON t.[Id] = s.[Id]
WHEN MATCHED THEN UPDATE SET
    t.[Code] = s.[Code],
    t.[TargetKind] = s.[TargetKind],
    t.[TargetPct] = s.[TargetPct],
    t.[BalancingCode] = s.[BalancingCode],
    t.[BalancingFNUMB] = s.[BalancingFNUMB],
    t.[IsActive] = s.[IsActive],
    t.[Note] = s.[Note]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [Code], [TargetKind], [TargetPct], [BalancingCode], [BalancingFNUMB], [IsActive], [Note])
    VALUES (s.[Id], s.[Code], s.[TargetKind], s.[TargetPct], s.[BalancingCode], s.[BalancingFNUMB], s.[IsActive], s.[Note]);
PRINT N'CC_MarginTarget: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';
SET IDENTITY_INSERT dbo.CC_MarginTarget OFF;

/* ──────────────────────────────────────────────────────────────────
   CC_RebalancePref — ترجیح توزیع مجدد مواد   (1 ردیف)
   ────────────────────────────────────────────────────────────────── */
SET IDENTITY_INSERT dbo.CC_RebalancePref ON;
MERGE dbo.CC_RebalancePref AS t
USING (VALUES
  (1, 368, 374, 373, NULL, 1, NULL, '2026-09-01T23:27:50.527', NULL)
) AS s ([Id], [SourceCode], [MaterialCode], [TargetCode], [SharePct], [IsActive], [Note], [CRT], [UID])
ON t.[Id] = s.[Id]
WHEN MATCHED THEN UPDATE SET
    t.[SourceCode] = s.[SourceCode],
    t.[MaterialCode] = s.[MaterialCode],
    t.[TargetCode] = s.[TargetCode],
    t.[SharePct] = s.[SharePct],
    t.[IsActive] = s.[IsActive],
    t.[Note] = s.[Note],
    t.[CRT] = s.[CRT],
    t.[UID] = s.[UID]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [SourceCode], [MaterialCode], [TargetCode], [SharePct], [IsActive], [Note], [CRT], [UID])
    VALUES (s.[Id], s.[SourceCode], s.[MaterialCode], s.[TargetCode], s.[SharePct], s.[IsActive], s.[Note], s.[CRT], s.[UID]);
PRINT N'CC_RebalancePref: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';
SET IDENTITY_INSERT dbo.CC_RebalancePref OFF;

/* ──────────────────────────────────────────────────────────────────
   CC_CheckRule — قواعد کنترلی (آستانه و فعال/غیرفعال)   (22 ردیف)
   ────────────────────────────────────────────────────────────────── */
MERGE dbo.CC_CheckRule AS t
USING (VALUES
  (N'CHK-01', N'کاردکس منفی', N'S05', 1, 2, -1.0000000000000000e-002, N'تاریخ رسید یا حواله را جابه‌جا کنید تا موجودی در هیچ لحظه‌ای منفی نشود.', 1, 10, NULL, NULL),
  (N'CHK-02', N'مغایرت کارت انبار و حسابداری', N'S05', 2, 2, NULL, N'معمولاً حواله‌ای است که فاکتورش صادر نشده، یا تاریخ فاکتور در ماه بعد افتاده. تاریخ‌ها را یکسان کنید.', 1, 20, NULL, NULL),
  (N'CHK-03', N'فرمول بدون نرخ جذب هزینه تبدیل', N'S00', 9, 1, NULL, N'در فرمول، «جذب هزینه دستمزد» را پر کنید. اگر عمداً صفر است (محصول فرعی مانند آب پنیر خالص)، آن را در فهرست استثناهای پذیرفته‌شده ثبت کنید تا دیگر هشدار ندهد.', 1, 30, NULL, NULL),
  (N'CHK-04', N'کالای تولیدشده بدون فرمول ماه', N'S00', 12, 2, NULL, N'نسخه ماه جاری فرمول ساخته نشده است. با «کپی فرمول» نسخه ماه را بسازید.', 1, 40, N'CC_sp_Fix_MissingFormula', N'اصلاح خودکار برگه'),
  (N'CHK-05', N'ماده بدون منبع نرخ', N'S00', 4, 1, NULL, N'این ماده نه فرمول دارد و نه گردش خروج در ماه، بنابراین نرخش صفر می‌ماند و صفر را به همه کالاهای بالادست منتقل می‌کند. یک نرخ برایش تعیین کنید.', 1, 50, NULL, NULL),
  (N'CHK-06', N'حلقه در ساختار فرمول', N'S00', 5, 2, NULL, N'کالا مستقیم یا غیرمستقیم خودش را مصرف می‌کند. تا این حلقه شکسته نشود، محاسبه نرخ ممکن نیست.', 1, 60, NULL, NULL),
  (N'CHK-07', N'مانده نامتوازن مواد در حساب ۷۵۱', N'S00', 13, 1, 1.0000000000000000e-003, N'اگر یک طرف صفر باشد، حواله جا افتاده است. آستانه یک در هزار است؛ کمتر از آن گِردکردن طبیعی است و نیاز به اقدام ندارد.', 1, 70, NULL, NULL),
  (N'CHK-08', N'اختلاف جذب برگه تولید با سند', N'S10', 10, 1, NULL, N'سند حسابداری وقتی صادر شده که فرمول نرخ دیگری داشته است. برگه تولید را بازسازی کنید.', 1, 80, NULL, NULL),
  (N'CHK-09', N'نرخ منتشرنشده نیمه‌ساخته', N'S11', 14, 2, 1.0000000000000000e-003, N'بهای خودِ فرمول این کالا با نرخی که در فرمول کالاهای بالادست دارد نمی‌خواند؛ یعنی انتشار نرخ کامل نشده. پس از اجرای کامل محاسبه نرخ، این قاعده باید صفر شود.', 1, 90, NULL, NULL),
  (N'CHK-10', N'مانده حساب کالای در جریان ساخت', N'S10', 8, 1, 1.0000000000000000e+007, N'فرض «کالای در جریان ساخت صفر» نقض شده است. آستانه ده میلیون ریال تنظیم شده تا باقیمانده گِردکردن هشدار کاذب ندهد.', 1, 100, NULL, NULL),
  (N'CHK-11', N'انحراف روی ماده مصرف‌نشده', N'S09', 11, 1, NULL, N'این ماده در هیچ فرمولی مصرف نشده ولی انحراف دارد. برگه انتقال یا انبارِ انبارگردانی را بررسی کنید.', 1, 110, NULL, NULL),
  (N'CHK-12', N'فرمول مقصد ماه قبل موجود نیست', N'S09', 15, 1, NULL, N'تصمیم ماه قبل قابل ادامه نیست چون کالای مقصد امسال فرمول ندارد. پیش‌فرض روی تسهیم به نسبت مصرف قرار گرفت.', 1, 120, NULL, NULL),
  (N'CHK-13', N'حواله با مقدار صفر', N'S07', 16, 2, NULL, N'ماده در فرمول مقدار دارد ولی حواله‌اش با مقدار صفر صادر شده؛ یعنی فرمول پس از صدور حواله ویرایش شده است. خروج مواد باید بازسازی شود.', 1, 130, NULL, NULL),
  (N'CHK-14', N'فروش بدون نرخ کاردکس', N'S12', 17, 1, NULL, N'اين کالا فروخته شده ولي ميانگين نرخ در کاردکس صفر است، پس بهاي تمام‌شده و سودش صفر محاسبه مي‌شود. کاردکس کالا را بررسي کنيد؛ معمولاً يعني رسيد بدون مبلغ ثبت شده.', 1, 140, NULL, NULL),
  (N'CHK-15', N'فرمول با مقدار منفی', N'S00', 17, 2, NULL, N'مقدار منفی در یک سطر فرمول قابل قبول نیست و باعث می‌شود مانده حساب کالای در جریان ساخت (۷۵۱) هرگز متوازن نشود. با دکمه اصلاح، آن سطر را صفر یا حذف کنید.', 1, 75, NULL, NULL),
  (N'CHK-16', N'برگه تولید به انبار بدون واحد تعریف‌شده', N'S00', 18, 1, NULL, N'این انبار را در تنظیمات، به تعریف واحدهای تولیدی (نقش «محصول») اضافه کنید — وگرنه هزینه تبدیل این برگه‌ها در هیچ واحدی جذب نمی‌شود و مانده حساب ۷۵۱ کاذب می‌شود.', 1, 45, NULL, NULL),
  (N'CHK-17', N'شمارش دوم/سوم انبارگردانی بدون مغایرت شمارش اول', N'S00', 19, 2, NULL, N'شمارش اول این کالا با موجودی سیستم برابر بوده، پس نباید وارد شمارش دوم/سوم می‌شد. ستون NUM2/NUM3 را که اشتباه پر شده صفر کنید — این عدد مستقیم مقدار پایان‌دوره‌ی کالا را در موتور نرخ غلط می‌کند.', 1, 46, NULL, NULL),
  (N'CHK-18', N'فاصله بیش از یک ماه بین فاکتور و حواله/رسید یا برگشت', N'S00', 20, 2, NULL, N'مشخص نیست کدام تاریخ درست است — از دکمه‌ی «اصلاح تاریخ» کنار همین ردیف استفاده کنید و تاریخ درست را انتخاب کنید تا سند دیگر با آن یکی شود.', 1, 47, NULL, NULL),
  (N'CHK-19', N'تاریخ فاکتور با تاریخ سند حسابداری‌اش یکی نیست', N'S00', 21, 1, NULL, N'از دکمه‌ی «اصلاح تاریخ» کنار همین ردیف استفاده کنید و تاریخ درست را انتخاب کنید — معمولاً بعد از اصلاح تاریخ یک فاکتور (CHK-18) پیش می‌آید، چون آن اصلاح فقط فاکتور/حواله را عوض می‌کند، نه سند حسابداریِ از قبل صادرشده را.', 1, 48, NULL, NULL),
  (N'CHK-20', N'نرخ میانگین منفی', N'S00', 22, 1, NULL, N'این نرخ منفی معمولاً پیامد یک کاردکس منفی (CHK-01) در تاریخی نزدیک همین سند است. آن مغایرت را بررسی و در صورت لزوم فیِ این سند را دستی به نرخ واقعیِ همان لحظه اصلاح کنید.', 1, 49, NULL, NULL),
  (N'CHK-21', N'تاریخ برگشت فروش با تاریخ سند حسابداری‌اش یکی نیست', N'S00', 23, 1, NULL, N'از دکمه‌ی «اصلاح تاریخ» کنار همین ردیف استفاده کنید و تاریخ درست را انتخاب کنید — تا وقتی این دو یکی نشوند، کاردکس این حواله را در ماهِ خودش می‌بیند ولی حسابداری در ماهِ دیگر، و CHK-02 مغایرتِ کاذب نشان می‌دهد.', 1, 50, NULL, NULL),
  (N'CHK-22', N'ميانگين انبار منفي — به‌عنوان نرخ پذيرفته نشد', N'S11', 24, 2, NULL, N'نرخِ موادِ هر واحد نمي‌تواند منفي باشد. ريشه‌اش معمولاً کاردکسِ منفي (CHK-01) يا سندي است که مقدارش ثبت شده ولي مبلغش نه. تا وقتي اين درست نشود بهاي اين کالا از فرمول/آخرين ميانگين مي‌آيد، نه از گردشِ اين ماه.', 1, 51, NULL, NULL)
) AS s ([RuleCode], [RuleName], [StepCode], [ExType], [DefaultSeverity], [Threshold], [RemedyText], [IsActive], [SortOrder], [FixProcName], [FixButtonText])
ON t.[RuleCode] = s.[RuleCode]
WHEN MATCHED THEN UPDATE SET
    t.[RuleName] = s.[RuleName],
    t.[StepCode] = s.[StepCode],
    t.[ExType] = s.[ExType],
    t.[DefaultSeverity] = s.[DefaultSeverity],
    t.[Threshold] = s.[Threshold],
    t.[RemedyText] = s.[RemedyText],
    t.[IsActive] = s.[IsActive],
    t.[SortOrder] = s.[SortOrder],
    t.[FixProcName] = s.[FixProcName],
    t.[FixButtonText] = s.[FixButtonText]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([RuleCode], [RuleName], [StepCode], [ExType], [DefaultSeverity], [Threshold], [RemedyText], [IsActive], [SortOrder], [FixProcName], [FixButtonText])
    VALUES (s.[RuleCode], s.[RuleName], s.[StepCode], s.[ExType], s.[DefaultSeverity], s.[Threshold], s.[RemedyText], s.[IsActive], s.[SortOrder], s.[FixProcName], s.[FixButtonText]);
PRINT N'CC_CheckRule: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';

/* ──────────────────────────────────────────────────────────────────
   AI_UserAccess — دسترسی کاربران به دستیار   (1 ردیف)
   ────────────────────────────────────────────────────────────────── */
MERGE dbo.AI_UserAccess AS t
USING (VALUES
  (114, 1, 1, 1, 500, 100, N'PAY2_PAYROLL,PAY2_EMPLOYEE', NULL, N'آقای دکتر حکیمیان', '2026-09-06T22:02:20.8325250')
) AS s ([UserCo], [IsEnabled], [Mode], [AllowRawSql], [MaxRows], [DailyMessages], [BlockedForms], [Note], [UpdatedBy], [UpdatedAtUtc])
ON t.[UserCo] = s.[UserCo]
WHEN MATCHED THEN UPDATE SET
    t.[IsEnabled] = s.[IsEnabled],
    t.[Mode] = s.[Mode],
    t.[AllowRawSql] = s.[AllowRawSql],
    t.[MaxRows] = s.[MaxRows],
    t.[DailyMessages] = s.[DailyMessages],
    t.[BlockedForms] = s.[BlockedForms],
    t.[Note] = s.[Note],
    t.[UpdatedBy] = s.[UpdatedBy],
    t.[UpdatedAtUtc] = s.[UpdatedAtUtc]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UserCo], [IsEnabled], [Mode], [AllowRawSql], [MaxRows], [DailyMessages], [BlockedForms], [Note], [UpdatedBy], [UpdatedAtUtc])
    VALUES (s.[UserCo], s.[IsEnabled], s.[Mode], s.[AllowRawSql], s.[MaxRows], s.[DailyMessages], s.[BlockedForms], s.[Note], s.[UpdatedBy], s.[UpdatedAtUtc]);
PRINT N'AI_UserAccess: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';

/* ──────────────────────────────────────────────────────────────────
   AI_Config — تنظیمات سرویس هوش مصنوعی   (1 ردیف)
   ────────────────────────────────────────────────────────────────── */
MERGE dbo.AI_Config AS t
USING (VALUES
  (1, 1, N'openai', N'http://localhost:20128', N'ag/gemini-3.8-flash-low', 900, 30, N'آقای دکتر حکیمیان', '2026-09-07T18:21:13.3901214')
) AS s ([Id], [IsEnabled], [Provider], [BaseUrl], [Model], [TimeoutSeconds], [MaxToolLoops], [UpdatedBy], [UpdatedAtUtc])
ON t.[Id] = s.[Id]
WHEN MATCHED THEN UPDATE SET
    t.[IsEnabled] = s.[IsEnabled],
    t.[Provider] = s.[Provider],
    t.[BaseUrl] = s.[BaseUrl],
    t.[Model] = s.[Model],
    t.[TimeoutSeconds] = s.[TimeoutSeconds],
    t.[MaxToolLoops] = s.[MaxToolLoops],
    t.[UpdatedBy] = s.[UpdatedBy],
    t.[UpdatedAtUtc] = s.[UpdatedAtUtc]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [IsEnabled], [Provider], [BaseUrl], [Model], [TimeoutSeconds], [MaxToolLoops], [UpdatedBy], [UpdatedAtUtc])
    VALUES (s.[Id], s.[IsEnabled], s.[Provider], s.[BaseUrl], s.[Model], s.[TimeoutSeconds], s.[MaxToolLoops], s.[UpdatedBy], s.[UpdatedAtUtc]);
PRINT N'AI_Config: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';


COMMIT;
GO

PRINT N'';
PRINT N'تنظیمات منتقل شد.';
PRINT N'یادآوری: کلید سرویس هوش مصنوعی را از صفحه‌ی تنظیمات ثبت کنید.';
GO
