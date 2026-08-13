using Safir.Shared.Models.CostClose;

namespace Safir.Server.CostClose.Steps
{
    /// <summary>
    /// S07 — بازتولید خروج مواد و انبارگردانی.
    ///
    /// جایگزین اسکریپت فعلی با دو کرسر تودرتو. انبارها از
    /// CC_UnitAnbar خوانده می‌شوند، پس باگ تکرار انبار ۸ ممکن نیست.
    /// </summary>
    public sealed class S07_RebuildIssue : ICostStep
    {
        public string StepCode         => "S07";
        public string Title            => "بازتولید خروج مواد و انبارگردانی";
        public short  SeqNo            => 70;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 20, "بازتولید خروج مواد…");

            // انبارگردانی روی چند انبار و چند روز اجرا می‌شود؛
            // مهلت پیش‌فرض ۳۰ ثانیه کافی نیست.
            var res = (await ctx.Db.DoGetStoreProcedureSQLAsync<RebuildResult>(
                "dbo.CC_sp_S07_RebuildIssue",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo, WhatIf = false },
                commandTimeout: 3600)).FirstOrDefault();

            await ctx.ReportProgress(StepCode, 100,
                $"{res?.Inserted ?? 0} سطر خروج، {res?.StockCount ?? 0} سطر انبارگردانی");

            return StepResult.Ok((res?.Inserted ?? 0) + (res?.StockCount ?? 0), new
            {
                deleted    = res?.Deleted ?? 0,
                inserted   = res?.Inserted ?? 0,
                stockCount = res?.StockCount ?? 0
            });
        }

        private sealed class RebuildResult
        {
            public int Deleted    { get; set; }
            public int Inserted   { get; set; }
            public int StockCount { get; set; }
        }
    }


    /// <summary>
    /// S08 — محاسبه انحراف مصرف.
    ///
    /// مانده انبار «مبنای انحراف» هر واحد = انحراف مصرف،
    /// به شرط صفر بودن کالای در جریان ساخت.
    /// </summary>
    public sealed class S08_CalcVariance : ICostStep
    {
        public string StepCode         => "S08";
        public string Title            => "محاسبه انحراف مصرف";
        public short  SeqNo            => 80;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 30, "محاسبه انحراف…");

            var res = (await ctx.Db.DoGetStoreProcedureSQLAsync<VarianceSummary>(
                "dbo.CC_sp_S08_CalcVariance",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo },
                commandTimeout: 1800)).FirstOrDefault();

            await ctx.ReportProgress(StepCode, 70, "تولید پیشنهاد از ماه قبل…");

            // پیشنهادها فقط وقتی ساخته می‌شوند که هنوز تصمیمی ثبت نشده
            var hasDecisions = await ctx.Db.DoGetDataSQLAsyncSingle<int>(
                "SELECT COUNT(*) FROM dbo.CC_VarianceDecision WHERE RunId = @r AND DecidedBy <> N'system'",
                new { r = ctx.RunId });

            if (hasDecisions == 0)
                await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                    "dbo.CC_sp_S09a_SeedDecisions",
                    new { RunId = ctx.RunId, Month = ctx.Month,
                          DT1 = ctx.DateFrom, DT2 = ctx.DateTo });

            await ctx.ReportProgress(StepCode, 100,
                $"{res?.Items ?? 0} کالا، خالص {res?.NetAmount ?? 0:N0} ریال");

            var payload = new
            {
                items    = res?.Items ?? 0,
                keyItems = res?.KeyItems ?? 0,
                net      = res?.NetAmount ?? 0,
                gross    = res?.GrossAmount ?? 0
            };

            // انحراف باقیمانده بزرگ = هشدار، نه خطا.
            // تصمیم با کاربر است، نه سامانه.
            return Math.Abs(res?.NetAmount ?? 0) > 1_000_000
                 ? StepResult.Warn(res?.Items ?? 0, payload)
                 : StepResult.Ok(res?.Items ?? 0, payload);
        }

        private sealed class VarianceSummary
        {
            public int    Items       { get; set; }
            public int    KeyItems    { get; set; }
            public double NetAmount   { get; set; }
            public double GrossAmount { get; set; }
        }
    }


    /// <summary>
    /// S09 — اعمال تصمیم‌های تخصیص انحراف.
    ///
    /// مقادیر فرمول را عوض می‌کند، پس WritesFormulas دارد و
    /// ارکستریتور خودکار S07 و S08 را بعدش صف می‌کند.
    /// </summary>
    public sealed class S09_ApplyDecisions : ICostStep
    {
        public string StepCode         => "S09";
        public string Title            => "تخصیص انحراف";
        public short  SeqNo            => 90;
        public bool   RequiresSnapshot => true;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => true;   // MEGHk عوض می‌شود

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 30, "اعمال تصمیم‌ها روی فرمول‌ها…");

            var res = (await ctx.Db.DoGetStoreProcedureSQLAsync<ApplyResult>(
                "dbo.CC_sp_S09_ApplyDecisions",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo, WhatIf = false },
                commandTimeout: 1800)).FirstOrDefault();

            await ctx.ReportProgress(StepCode, 100, $"{res?.Value ?? 0} سطر به‌روز شد");

            return StepResult.Ok(res?.Value ?? 0, new { rows = res?.Value ?? 0 });
        }

        private sealed class ApplyResult
        {
            public int Value { get; set; }
        }
    }
}
