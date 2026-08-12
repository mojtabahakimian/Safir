namespace Safir.Server.CostClose.Steps
{
    /// <summary>
    /// S12 — محاسبه سود و زیان کالا.
    ///
    /// اعمال هدف حاشیه (S12b) عمداً اینجا نیست: تصمیم کاربر است
    /// و از صفحه سود کالا فراخوانی می‌شود، نه خودکار در pipeline.
    /// </summary>
    public sealed class S12_CalcMargin : ICostStep
    {
        public string StepCode         => "S12";
        public string Title            => "سود و زیان کالا";
        public short  SeqNo            => 120;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 40, "محاسبه سود به تفکیک کالا…");

            // DoGetDataSQLAsync (رشته EXEC) پارامتر commandTimeout ندارد؛
            // مثل S00/S05/S09/S10/S11 با فراخوانی واقعی رویه ذخیره‌شده اجرا
            // می‌شود که commandTimeout را می‌پذیرد.
            var res = (await ctx.Db.DoGetStoreProcedureSQLAsync<MarginSummary>(
                "dbo.CC_sp_S12_CalcMargin",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo },
                commandTimeout: 900)).FirstOrDefault();

            await ctx.ReportProgress(StepCode, 100,
                $"{res?.Items ?? 0} کالا، {res?.LossItems ?? 0} زیان‌ده");

            var payload = new
            {
                items     = res?.Items ?? 0,
                lossItems = res?.LossItems ?? 0,
                sales     = res?.TotalSales ?? 0,
                cost      = res?.TotalCost ?? 0,
                profit    = res?.TotalProfit ?? 0
            };

            // کالای زیان‌ده هشدار است نه خطا — تصمیم با کاربر است
            return (res?.LossItems ?? 0) > 0
                 ? StepResult.Warn(res?.Items ?? 0, payload)
                 : StepResult.Ok(res?.Items ?? 0, payload);
        }

        private sealed class MarginSummary
        {
            public int    Items       { get; set; }
            public int    LossItems   { get; set; }
            public double TotalSales  { get; set; }
            public double TotalCost   { get; set; }
            public double TotalProfit { get; set; }
        }
    }
}
