namespace Safir.Server.CostClose.Steps
{
    /// <summary>
    /// S12 — محاسبه سود و زیان کالا.
    ///
    /// ⚠ اینجا عمداً هیچ گامی برای «اعمال هدف حاشیه» نیست. گامِ S12B که
    /// موقتاً اضافه شده بود حذف شد، چون تنها اهرمش IMBIBE_MANF (نرخ جذب
    /// دستمزد) بود و طبق تصمیم صاحب پروژه، دستمزد و سربار هرگز نباید برای
    /// تنظیم سود کالا دست‌کاری شوند — هر تعدیلی فقط روی «مقدار مواد» انجام
    /// می‌شود، آن هم به‌صورت انتقال به فرمولِ کالای سوددهی که همان ماده را
    /// مصرف می‌کند، تا جمع مصرف فیزیکی ماه ثابت بماند و انحراف مصرف ایجاد
    /// نشود. مکانیزم جایگزین: CC_sp_RebalanceSuggest + CC_sp_RebalanceMaterialQty.
    ///
    /// ضمناً آن گام عملاً بی‌اثر هم بود: این رویه بها را از میانگین کاردکس
    /// (MABRIAL) می‌گیرد و اصلاً IMBIBE_MANF را نمی‌خواند.
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

            // گزارش تفکیکی به‌ازای هر واحد تولید — جدول جداگانه‌ی
            // CC_ItemMarginUnit، بدون دست زدن به گرینِ CC_ItemMargin.
            // «علاوه بر» گزارش کل است، نه به‌جای آن.
            await ctx.ReportProgress(StepCode, 80, "تفکیک سود به واحدهای تولید…");

            await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S12u_MarginByUnit",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo },
                commandTimeout: 900);

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
