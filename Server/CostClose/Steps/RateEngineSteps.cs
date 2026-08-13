using Safir.Shared.Models.CostClose;

namespace Safir.Server.CostClose.Steps
{
    /// <summary>
    /// S10 — تراز هزینه تبدیل.
    ///
    /// قبل از S11 اجرا می‌شود، نه بعدش. چون جذب‌شده حاصل
    /// «مقدار تولید × نرخ جذب» است و مقدار تولید در این مرحله ثابت،
    /// ضریب یکنواخت k دقیقاً جذب را برابر واقعی می‌کند — مستقل از
    /// نرخ مواد. پس یک بار محاسبه کافی است و نیازی به تکرار ندارد.
    /// </summary>
    public sealed class S10_BalanceConversion : ICostStep
    {
        public string StepCode         => "S10";
        public string Title            => "تراز هزینه تبدیل";
        public short  SeqNo            => 100;
        public bool   RequiresSnapshot => true;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => true;   // IMBIBE_MANF و IMBIBE_SAR

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 20, "محاسبه جذب‌شده از برگه‌های تولید…");

            await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S10_BalanceConversion",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo, WhatIf = false },
                commandTimeout: 1800);

            await ctx.ReportProgress(StepCode, 90, "ثبت نتیجه…");

            var rows = (await ctx.Db.DoGetDataSQLAsync<ConversionSummary>(
                @"SELECT c.UnitId, u.UnitName, c.AbsorbedAmount, c.ActualAmount,
                         c.AdjustFactor, c.AbsorbedFromWip
                  FROM   dbo.CC_ConversionCost c
                  JOIN   dbo.CC_Unit u ON u.UnitId = c.UnitId
                  WHERE  c.RunId = @r AND c.CostKind = 0",
                new { r = ctx.RunId })).ToList();

            await ctx.ReportProgress(StepCode, 100, "پایان");

            // اگر ضریب هیچ واحدی تغییر نکرد، یعنی چیزی نوشته نشده
            var anyChange = rows.Any(r => Math.Abs(r.AdjustFactor - 1) > 0.000001m);

            return StepResult.Ok(rows.Count, new
            {
                units = rows.Select(r => new
                {
                    r.UnitName,
                    absorbed = r.AbsorbedAmount,
                    actual   = r.ActualAmount,
                    factor   = Math.Round(r.AdjustFactor, 5),
                    wipDiff  = r.AbsorbedFromWip - r.AbsorbedAmount
                }),
                anyChange
            });
        }

        private sealed class ConversionSummary
        {
            public int     UnitId          { get; set; }
            public string  UnitName        { get; set; } = "";
            public decimal AbsorbedAmount  { get; set; }
            public decimal ActualAmount    { get; set; }
            public decimal AdjustFactor    { get; set; }
            public decimal? AbsorbedFromWip { get; set; }

            // برای مقایسه اعشاری
            public double AdjustFactorD => (double)AdjustFactor;
        }
    }


    /// <summary>
    /// S11 — انتشار نرخ.
    ///
    /// این گام جایگزین «۵ تا ۶ بار تکرار + job یک‌دقیقه‌ای» است.
    /// درخت فرمول یک‌بار سطح‌بندی می‌شود و محاسبه از عمیق‌ترین سطح
    /// (مواد خام) به سطح صفر (محصول نهایی) در یک پاس انجام می‌گیرد.
    ///
    /// نتیجه قطعی است: دو بار اجرا، دقیقاً یک عدد.
    /// </summary>
    public sealed class S11_PropagateRates : ICostStep
    {
        public string StepCode         => "S11";
        public string Title            => "انتشار نرخ";
        public short  SeqNo            => 110;
        public bool   RequiresSnapshot => true;
        public bool   IsGate           => false;

        // عمداً false: خودِ این گام نرخ‌ها را منتشر می‌کند، پس بازتولید
        // خروج مواد پس از آن معنا ندارد و فقط حلقه بی‌پایان می‌سازد.
        // مقادیر (MEGHk) دست‌نخورده می‌مانند؛ فقط نرخ‌ها عوض می‌شوند.
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 15, "سطح‌بندی درخت فرمول…");

            await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S11_PropagateRates",
                new { RunId = ctx.RunId, Month = ctx.Month, WhatIf = false },
                commandTimeout: 3600);

            await ctx.ReportProgress(StepCode, 85, "بررسی سلامت نرخ‌ها…");

            var health = await ctx.Db.DoGetDataSQLAsyncSingle<RateHealth>(
                @"SELECT
                    (SELECT COUNT(*) FROM dbo.CC_ItemCost
                      WHERE RunId = @r)                              AS Items,
                    (SELECT MAX(LowLevelCode) FROM dbo.CC_ItemCost
                      WHERE RunId = @r)                              AS MaxLevel,
                    (SELECT COUNT(*) FROM dbo.CC_ItemCost
                      WHERE RunId = @r AND SourceKind = 3)           AS NoSource,
                    (SELECT COUNT(*) FROM dbo.CC_FormulaChange
                      WHERE RunId = @r AND StepCode = 'S11')         AS Changes,
                    (SELECT COUNT(*) FROM dbo.CC_Exception
                      WHERE RunId = @r AND RuleCode = 'CHK-09'
                        AND IsResolved = 0)                          AS Unpropagated",
                new { r = ctx.RunId });

            await ctx.ReportProgress(StepCode, 100,
                $"{health?.Changes ?? 0} نرخ به‌روز شد");

            var payload = new
            {
                items        = health?.Items ?? 0,
                maxLevel     = health?.MaxLevel ?? 0,
                noSource     = health?.NoSource ?? 0,
                changes      = health?.Changes ?? 0,
                unpropagated = health?.Unpropagated ?? 0
            };

            // آزمون سلامت: پس از اجرای درست موتور، CHK-09 باید صفر باشد.
            // اگر صفر نشد یعنی چیزی در محاسبه جا مانده.
            if ((health?.Unpropagated ?? 0) > 0)
                return StepResult.Warn(health?.Changes ?? 0, payload);

            return StepResult.Ok(health?.Changes ?? 0, payload);
        }

        private sealed class RateHealth
        {
            public int Items        { get; set; }
            public int MaxLevel     { get; set; }
            public int NoSource     { get; set; }
            public int Changes      { get; set; }
            public int Unpropagated { get; set; }
        }
    }
}
