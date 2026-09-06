using Safir.Server.CostClose.AverageRateRebuild;

namespace Safir.Server.CostClose.Steps
{
    /// <summary>
    /// S07A — بازسازی نرخ میانگین کاردکس.
    ///
    /// بین S07 (بازتولید خروج مواد) و S08 (محاسبه انحراف مصرف) اجرا می‌شود:
    /// S07 مقدار (MEGHk) ردیف‌های خروج مواد را می‌سازد، این گام روی همان
    /// ردیف‌ها (و کل کاردکس) نرخ میانگین واقعی را می‌نویسد — قبل از S07A،
    /// S07 موقتاً AVRAGE=1 و MABL=1 می‌گذارد (نگاه کنید CC_sp_S07_RebuildIssue).
    /// بدون این گام، سند گروهی خروج مواد (MaterialIssueRebuildService) و
    /// S08 روی نرخ جعلی ۱ کار می‌کنند.
    ///
    /// پورت از C0_TASK در MainWindow.xaml.cs (پروژه‌ی AUTO_BAZ)؛ جزئیات و
    /// دلایل هر تصمیم در AverageRateRebuildService.cs مستند شده.
    /// </summary>
    public sealed class S07A_RebuildAverageRate : ICostStep
    {
        public string StepCode         => "S07A";
        public string Title            => "بازسازی نرخ میانگین";
        public short  SeqNo            => 75;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;

        // به DTL_MANF/HEAD_MANF (جدول‌های فرمول) نمی‌نویسد — فقط INVO_LST و
        // ANBGRD_LST (کاردکس/موجودی). پس خودش فرمول را «کثیف» نمی‌کند؛ اما
        // وقتی گام دیگری (S09/S10) فرمول را عوض کند، باید *پس از* S07
        // و *پیش از* S08 دوباره اجرا شود — این کار در CloseOrchestrator با
        // افزودن S07A به همان دسته‌ی بازتولید S07/S08 انجام می‌شود، نه با
        // WritesFormulas اینجا.
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            // دامنه‌ی محدود فقط داخل حلقه‌ی همگرایی و فقط وقتی S07 دوباره اجرا
            // نشده باشد — دلیلش در StepContext.NarrowToFormulaItems.
            var onlyCodes = ctx.NarrowToFormulaItems
                ? await GetFormulaItemCodesAsync(ctx)
                : null;

            await ctx.ReportProgress(StepCode, 10, onlyCodes is null
                ? "پیمایش کاردکس و بازسازی نرخ میانگین…"
                : $"بازسازی نرخ میانگین برای {onlyCodes.Count} کالای فرمول‌دار…");

            var svc = new AverageRateRebuildService(ctx.Db);
            var res = await svc.RebuildAsync(onlyCodes: onlyCodes, ct: ctx.Ct);

            await ctx.ReportProgress(StepCode, 100,
                $"{res.ItemsProcessed} کالا، {res.RowsUpdated} ردیف به‌روز شد");

            var payload = new
            {
                items   = res.ItemsProcessed,
                rows    = res.RowsUpdated,
                narrow  = onlyCodes is not null,
                logTail = res.Log.TakeLast(5)
            };

            if (!res.Success)
                return StepResult.Warn(res.RowsUpdated, new { payload.items, payload.rows, error = res.FirstError });

            return StepResult.Ok(res.RowsUpdated, payload);
        }

        /// <summary>
        /// کالاهایی که رسید تولید می‌گیرند، یعنی آن‌هایی که فرمول دارند. بین
        /// دورهای حلقه‌ی همگرایی فقط بهای همین‌ها می‌تواند عوض شود.
        ///
        /// عمداً به ماه مقید نیست: کالایی که فرمولش برای ماه دیگری تعریف شده
        /// ولی در این دوره سند تولید دارد هم باید داخل دامنه بماند. چند ده کد
        /// اضافه ارزانی است در برابر جا انداختنِ یکی.
        /// </summary>
        private static async Task<IReadOnlyCollection<string>> GetFormulaItemCodesAsync(StepContext ctx)
            => (await ctx.Db.DoGetDataSQLAsync<string>(
                    "SELECT DISTINCT CODE FROM dbo.HEAD_MANF WHERE CODE IS NOT NULL"))
               .Where(c => !string.IsNullOrWhiteSpace(c))
               .ToList();
    }
}
