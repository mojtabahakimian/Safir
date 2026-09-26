using Safir.Server.CostClose.AverageRateRebuild;

namespace Safir.Server.CostClose.Steps
{
    /// <summary>
    /// S11B — تراز تبدیل کالا.
    ///
    /// ── چرا لازم است ──
    /// تبدیل کالا دو سر دارد: یک حواله خروج سایر (TAG=11) و یک رسید خرید
    /// (TAG=1). بازسازی نرخ میانگین فقط سرِ اول را دوباره قیمت می‌زند —
    /// برای TAG=۱۰/۱۱ هم MABL و هم MABL_K را با میانگین متحرک بازنویسی
    /// می‌کند. سرِ دوم (TAG=1) همان مبلغی می‌ماند که موقع ثبت تایپ شده
    /// بود.
    ///
    /// نتیجه‌اش روی داده‌ی واقعی دیده شده: حساب «تبدیل» در پایگاه پودر
    /// مروارید با دو آرتیکل، ۴٬۷۶۰٬۸۷۲ ریال مانده دارد. این گام همان
    /// مانده را می‌بندد.
    ///
    /// ── چرا اینجا و نه زودتر ──
    /// مبلغ نهاییِ حواله تا وقتی حلقه‌ی همگرایی S07A↔S11 تمام نشده معلوم
    /// نیست. SeqNo=115 یعنی بعد از S11 (۱۱۰) و پیش از S12 (۱۲۰) —
    /// سود و زیان کالا باید ارزشِ درستِ کالای مقصد را ببیند.
    ///
    /// ── چرا بعدش دوباره کاردکس ──
    /// عوض‌کردن MABL_K یک رسید یعنی عوض‌شدن ارزشِ ورودیِ آن کالا، و
    /// میانگینِ آن کالا از همان‌جا به بعد. پس کاردکسِ *همان کدها* دوباره
    /// ساخته می‌شود — نه همه‌ی کالاها، که گران است و بی‌مورد.
    /// </summary>
    public sealed class S11B_BalanceConversions : ICostStep
    {
        public string StepCode         => "S11B";
        public string Title            => "تراز تبدیل کالا";
        public short  SeqNo            => 115;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 10, "بررسی تبدیل‌های این دوره…");

            var touched = (await ctx.Db.DoGetDataSQLAsync<string>(
                "EXEC dbo.CC_sp_ResyncConversions @RunId, @DT1, @DT2",
                new { ctx.RunId, DT1 = ctx.DateFrom, DT2 = ctx.DateTo }))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (touched.Count == 0)
            {
                await ctx.ReportProgress(StepCode, 100, "همه‌ی تبدیل‌ها تراز بودند");
                return StepResult.Ok(0, new { rebalanced = 0 });
            }

            await ctx.ReportProgress(StepCode, 50,
                $"{touched.Count} کالای مقصد مبلغش اصلاح شد؛ بازسازی کاردکس همان‌ها…");

            var svc = new AverageRateRebuildService(ctx.Db);
            var res = await svc.RebuildAsync(onlyCodes: touched, ct: ctx.Ct);

            await ctx.ReportProgress(StepCode, 100,
                $"{touched.Count} تبدیل تراز شد، {res.RowsUpdated} ردیف کاردکس به‌روز شد");

            var payload = new
            {
                rebalanced = touched.Count,
                codes      = touched.Take(20),
                kardexRows = res.RowsUpdated
            };

            // اگر بازسازیِ بعدی نگرفت، خودِ تراز انجام شده و ارزشِ ورودی
            // درست است — فقط میانگینِ بعد از آن نقطه هنوز قدیمی است. این
            // هشدار است، نه شکست.
            return res.Success
                ? StepResult.Ok(touched.Count, payload)
                : StepResult.Warn(touched.Count, new { payload.rebalanced, error = res.FirstError });
        }
    }
}
