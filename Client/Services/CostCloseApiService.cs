using Safir.Shared.Models.CostClose;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class CostCloseApiService
    {
        private readonly HttpClient _http;
        public CostCloseApiService(HttpClient http) => _http = http;

        private const string Base = "api/cost-close";

        // ───────── اجراها ─────────

        public async Task<List<CostRunDto>> GetRunsAsync(short? year = null, byte? month = null)
        {
            var q = new List<string>();
            if (year  is not null) q.Add($"year={year}");
            if (month is not null) q.Add($"month={month}");
            var url = $"{Base}/runs" + (q.Count > 0 ? "?" + string.Join("&", q) : "");

            return await _http.GetFromJsonAsync<List<CostRunDto>>(url) ?? new();
        }

        public async Task<CostRunStateDto?> GetRunStateAsync(int runId)
            => await _http.GetFromJsonAsync<CostRunStateDto>($"{Base}/runs/{runId}");

        public async Task<(bool Ok, int RunId, string? Error)> CreateRunAsync(CreateCostRunRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/runs", req);
            if (!res.IsSuccessStatusCode)
                return (false, 0, await res.Content.ReadAsStringAsync());

            return (true, await res.Content.ReadFromJsonAsync<int>(), null);
        }

        public async Task<(bool Ok, string? Error)> StartRunAsync(int runId, string[]? onlySteps = null)
        {
            var url = $"{Base}/runs/{runId}/start";
            if (onlySteps is { Length: > 0 })
                url += "?" + string.Join("&", onlySteps.Select(s => $"onlySteps={s}"));

            var res = await _http.PostAsync(url, null);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> ResumeRunAsync(int runId)
        {
            var res = await _http.PostAsync($"{Base}/runs/{runId}/resume", null);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task CancelRunAsync(int runId)
            => await _http.PostAsync($"{Base}/runs/{runId}/cancel", null);

        public async Task<List<CostRunLogDto>> GetLogsAsync(int runId, long afterId = 0)
            => await _http.GetFromJsonAsync<List<CostRunLogDto>>(
                   $"{Base}/runs/{runId}/logs?afterId={afterId}") ?? new();

        // ───────── استثناها ─────────

        public async Task<List<CostExceptionDto>> RunPreflightAsync(
            byte month, long dateFrom, long dateTo, int? runId = null)
        {
            var url = $"{Base}/preflight?month={month}&dateFrom={dateFrom}&dateTo={dateTo}"
                    + (runId is not null ? $"&runId={runId}" : "");

            var res = await _http.PostAsync(url, null);
            if (!res.IsSuccessStatusCode) return new();

            return await res.Content.ReadFromJsonAsync<List<CostExceptionDto>>() ?? new();
        }

        public async Task<List<CostExceptionDto>> GetExceptionsAsync(
            int? runId = null, string? ruleCode = null, bool includeResolved = false)
        {
            var q = new List<string> { $"includeResolved={includeResolved}" };
            if (runId    is not null)               q.Add($"runId={runId}");
            if (!string.IsNullOrEmpty(ruleCode))    q.Add($"ruleCode={ruleCode}");

            return await _http.GetFromJsonAsync<List<CostExceptionDto>>(
                       $"{Base}/exceptions?{string.Join("&", q)}") ?? new();
        }

        public async Task<(bool Ok, string? Error)> ResolveAsync(long id, string? note)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/exceptions/{id}/resolve", new ResolveExceptionRequest { Note = note });
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> AcceptPermanentlyAsync(long id, string reason)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/exceptions/{id}/accept-permanently",
                new ResolveExceptionRequest { Note = reason });
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<List<AcceptedExceptionDto>> GetAcceptedExceptionsAsync()
            => await _http.GetFromJsonAsync<List<AcceptedExceptionDto>>($"{Base}/accepted-exceptions") ?? new();

        public async Task<(bool Ok, string? Error)> RevokeAcceptedExceptionAsync(int id)
        {
            var res = await _http.PostAsync($"{Base}/accepted-exceptions/{id}/revoke", null);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, int Count, string? Error)> AcceptPermanentlyBulkAsync(List<long> ids, string note)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/exceptions/bulk-accept-permanently",
                new BulkResolveRequest { ExceptionIds = ids, Note = note });
            if (!res.IsSuccessStatusCode) return (false, 0, await res.Content.ReadAsStringAsync());

            var body = await res.Content.ReadFromJsonAsync<Dictionary<string, int>>();
            return (true, body?.GetValueOrDefault("count") ?? 0, null);
        }

        public async Task<(bool Ok, string? Error, List<string> Log)> RebuildSaleDocForExceptionAsync(long exceptionId)
        {
            var res = await _http.PostAsync($"{Base}/exceptions/{exceptionId}/rebuild-sale-doc", null);
            if (!res.IsSuccessStatusCode)
                return (false, await res.Content.ReadAsStringAsync(), new());

            var body = await res.Content.ReadFromJsonAsync<RebuildSaleDocResult>();
            return (body?.Success ?? false, body?.Error, body?.Log ?? new());
        }

        private sealed class RebuildSaleDocResult
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public List<string> Log { get; set; } = new();
        }

        public async Task<(bool Ok, string? Error)> FixDateMismatchAsync(long exceptionId, bool useA)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/exceptions/{exceptionId}/fix-date-mismatch",
                new FixDateMismatchRequest { UseA = useA });
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, int Count, string? Error)> BulkResolveAsync(List<long> ids, string? note)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/exceptions/bulk-resolve",
                new BulkResolveRequest { ExceptionIds = ids, Note = note });
            if (!res.IsSuccessStatusCode) return (false, 0, await res.Content.ReadAsStringAsync());

            var body = await res.Content.ReadFromJsonAsync<Dictionary<string, int>>();
            return (true, body?.GetValueOrDefault("count") ?? 0, null);
        }

        /// <summary>رفع مغایرت CHK-02 با سند اصلاحی — WhatIf=true فقط پیش‌نمایش می‌دهد.</summary>
        public async Task<PostCorrectionResultDto?> PostCorrectionAsync(PostCorrectionRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/exceptions/post-correction", req);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<PostCorrectionResultDto>();
        }

        // ───────── اصلاح خودکار ─────────

        public async Task<AutoFixResultDto?> FixMissingFormulaAsync(AutoFixRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/fix/missing-formula", req);
            if (!res.IsSuccessStatusCode)
                return new AutoFixResultDto
                {
                    Message = await res.Content.ReadAsStringAsync()
                };

            return await res.Content.ReadFromJsonAsync<AutoFixResultDto>();
        }

        // ───────── سرفصل‌های هزینه‌ی دوره ─────────

        public async Task<List<CostExpenseAccDto>> GetExpenseAccsAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<List<CostExpenseAccDto>>(
                    $"{Base}/expense-accs") ?? new();
            }
            catch { return new(); }
        }

        public async Task<(bool Ok, string? Error)> AddExpenseAccAsync(UpsertExpenseAccRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/expense-accs", req);
            return res.IsSuccessStatusCode
                ? (true, null)
                : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> UpdateExpenseAccAsync(int id, UpsertExpenseAccRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/expense-accs/{id}", req);
            return res.IsSuccessStatusCode
                ? (true, null)
                : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteExpenseAccAsync(int id)
        {
            var res = await _http.DeleteAsync($"{Base}/expense-accs/{id}");
            return res.IsSuccessStatusCode
                ? (true, null)
                : (false, await res.Content.ReadAsStringAsync());
        }

        /// <summary>صورت‌های مالی این اجرا (بهای ساخته‌شده، بهای فروش‌رفته، سود و زیان)</summary>
        public async Task<FinancialStatementsDto?> GetFinancialStatementsAsync(int runId)
        {
            try
            {
                return await _http.GetFromJsonAsync<FinancialStatementsDto>(
                    $"{Base}/runs/{runId}/financial-statements");
            }
            catch { return null; }
        }

        /// <summary>
        /// فرمول‌های این کالا در ماه‌های دیگر — برای وقتی کالا برای ماهِ جاری
        /// فرمول ندارد و باید یکی کپی شود. ماهِ قبل اولِ فهرست است.
        /// </summary>
        public async Task<List<FormulaOptionDto>> GetFormulaOptionsAsync(long code, byte month)
        {
            try
            {
                return await _http.GetFromJsonAsync<List<FormulaOptionDto>>(
                    $"{Base}/fix/formula-options?code={code}&month={month}") ?? new();
            }
            catch { return new(); }
        }

        /// <summary>کپیِ فرمول از ماهِ دیگر به ماهِ جاری و وصل‌کردنش به برگه‌های تولید.</summary>
        public async Task<CopyFormulaResultDto?> CopyFormulaAsync(CopyFormulaRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/fix/copy-formula", req);
            if (!res.IsSuccessStatusCode)
                return new CopyFormulaResultDto
                {
                    Message = await res.Content.ReadAsStringAsync()
                };

            return await res.Content.ReadFromJsonAsync<CopyFormulaResultDto>();
        }

        /// <summary>دکمه «بازسازی نرخ» برای CHK-09 — S10 و S11 را دوباره اجرا می‌کند.</summary>
        public async Task<(bool Ok, int Remaining, string? Error)> RebuildRatesAsync(int runId)
        {
            var res = await _http.PostAsync($"{Base}/runs/{runId}/rebuild-rates", null);
            if (!res.IsSuccessStatusCode)
                return (false, 0, await res.Content.ReadAsStringAsync());

            var body = await res.Content.ReadFromJsonAsync<RebuildRatesResultDto>();
            return (true, body?.Remaining ?? 0, null);
        }

        /// <summary>
        /// بازسازی سند حواله خروج مواد برای برگه‌های همان ماه این اجرا — بعد از اصلاح
        /// نرخ فرمول لازم است تا سند حسابداری با نرخ تازه هم‌خوان شود.
        /// </summary>
        public async Task<(bool Ok, MaterialIssueRebuildResultDto? Result, string? Error)> RebuildMaterialIssueDocsAsync(int runId)
        {
            var res = await _http.PostAsync($"{Base}/runs/{runId}/rebuild-material-issue-docs", null);
            if (!res.IsSuccessStatusCode)
                return (false, null, await res.Content.ReadAsStringAsync());

            var body = await res.Content.ReadFromJsonAsync<MaterialIssueRebuildResultDto>();
            return (true, body, null);
        }

        /// <summary>
        /// بازسازی یکی از ۵ سند گروهی دیگر (انتقالی/فروش/برگشت فروش/خروج سایر/
        /// ورود ساخته‌شده/انبارگردانی) برای برگه‌های همان ماه این اجرا. endpointSuffix
        /// یکی از rebuild-transfer-docs، rebuild-sale-docs، rebuild-sale-return-docs،
        /// rebuild-other-issue-docs، rebuild-production-receipt-docs، rebuild-stock-count-docs.
        /// </summary>
        public async Task<(bool Ok, GroupDocumentRebuildResultDto? Result, string? Error)> RebuildGroupDocsAsync(
            int runId, string endpointSuffix)
        {
            var res = await _http.PostAsync($"{Base}/runs/{runId}/{endpointSuffix}", null);
            if (!res.IsSuccessStatusCode)
            {
                // ⚠️ بدنه همیشه متن ندارد: ۴۰۴ و خطاهای مدیریت‌نشده‌ی سرور
                // خالی برمی‌گردند و کاربر فقط یک «خطا»ی بی‌معنا می‌دید — هشت
                // بار، بدون هیچ سرنخی. کدِ وضعیت تنها چیزی است که همیشه هست،
                // پس وقتی بدنه خالی است همان را می‌گوییم.
                var errBody = (await res.Content.ReadAsStringAsync()).Trim();
                var code    = (int)res.StatusCode;

                var msg = errBody.Length > 0
                    ? $"{errBody} (کد {code})"
                    : code switch
                    {
                        401 => "وارد نشده‌اید یا نشستتان منقضی شده — دوباره وارد شوید. (کد ۴۰۱)",
                        403 => "دسترسی «بازسازی اسناد گروهی» را ندارید. (کد ۴۰۳)",
                        404 => "این اجرا پیدا نشد یا این قابلیت روی سرور نصب نیست. (کد ۴۰۴)",
                        409 => "این اجرا در حال انجام است؛ اول متوقفش کنید. (کد ۴۰۹)",
                        500 => "خطای سمت سرور — معمولاً یعنی اسکریپت‌های دیتابیس عقب‌اند. (کد ۵۰۰)",
                        _   => $"پاسخ نامنتظره از سرور (کد {code})"
                    };

                return (false, null, msg);
            }

            var body = await res.Content.ReadFromJsonAsync<GroupDocumentRebuildResultDto>();
            return (true, body, null);
        }

        /// <summary>اصلاح CHK-15 — «صفر کن» یا «حذف کن» روی یک سطر فرمول.</summary>
        public async Task<(bool Ok, string? Error)> FixNegativeFormulaQtyAsync(
            long exceptionId, string action, int? runId)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/fix/negative-formula-qty",
                new FixNegativeFormulaQtyRequest
                {
                    ExceptionId = exceptionId, Action = action, RunId = runId, WhatIf = false
                });

            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── انحراف مصرف ─────────

        public async Task<List<VarianceRowDto>> GetVariancesAsync(int runId)
            => await _http.GetFromJsonAsync<List<VarianceRowDto>>(
                   $"{Base}/runs/{runId}/variances") ?? new();

        public async Task<(bool Ok, string? Error)> SaveDecisionsAsync(
            int runId, List<VarianceDecisionInput> items)
        {
            var res = await _http.PutAsJsonAsync(
                $"{Base}/runs/{runId}/variance-decisions", items);

            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── نتایج محاسبه ─────────

        public async Task<List<ConversionCostDto>> GetConversionAsync(int runId)
            => await _http.GetFromJsonAsync<List<ConversionCostDto>>(
                   $"{Base}/runs/{runId}/conversion") ?? new();

        public async Task<List<ItemCostDto>> GetItemCostsAsync(int runId, short? level = null)
            => await _http.GetFromJsonAsync<List<ItemCostDto>>(
                   $"{Base}/runs/{runId}/item-costs"
                   + (level is not null ? $"?level={level}" : "")) ?? new();

        public async Task<List<FormulaChangeDto>> GetChangesAsync(
            int runId, long? code = null, string? stepCode = null)
        {
            var q = new List<string>();
            if (code     is not null)            q.Add($"code={code}");
            if (!string.IsNullOrEmpty(stepCode)) q.Add($"stepCode={stepCode}");

            return await _http.GetFromJsonAsync<List<FormulaChangeDto>>(
                       $"{Base}/runs/{runId}/changes"
                       + (q.Count > 0 ? "?" + string.Join("&", q) : "")) ?? new();
        }

        public async Task<(bool Ok, string? Error)> RollbackAsync(
            int runId, string? stepCode = null, bool whatIf = true)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/runs/{runId}/rollback",
                new RollbackRequest { StepCode = stepCode, WhatIf = whatIf });

            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── سود و زیان کالا ─────────

        public async Task<List<ItemMarginDto>> GetMarginsAsync(int runId)
            => await _http.GetFromJsonAsync<List<ItemMarginDto>>(
                   $"{Base}/runs/{runId}/margins") ?? new();

        public async Task<(bool Ok, string? Error)> SaveMarginTargetsAsync(
            List<MarginTargetInput> items)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/margin-targets", items);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> ApplyMarginTargetsAsync(
            int runId, bool whatIf = true)
        {
            var res = await _http.PostAsync(
                $"{Base}/runs/{runId}/apply-margin-targets?whatIf={whatIf}", null);

            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<List<ActiveMarginTargetDto>> GetActiveMarginTargetsAsync()
            => await _http.GetFromJsonAsync<List<ActiveMarginTargetDto>>($"{Base}/margin-targets/active") ?? new();

        public async Task<(bool Ok, string? Error)> DeactivateMarginTargetAsync(int id)
        {
            var res = await _http.PostAsync($"{Base}/margin-targets/{id}/deactivate", null);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── جابه‌جایی مصرف ماده بین فرمول‌ها ─────────

        public async Task<List<FormulaMaterialDto>> GetFormulaMaterialsAsync(int runId, string? search = null)
        {
            var url = $"{Base}/runs/{runId}/formula-materials";
            if (!string.IsNullOrWhiteSpace(search))
                url += $"?search={Uri.EscapeDataString(search)}";

            return await _http.GetFromJsonAsync<List<FormulaMaterialDto>>(url) ?? new();
        }

        public async Task<List<MaterialConsumerDto>> GetMaterialConsumersAsync(int runId, long materialCode)
            => await _http.GetFromJsonAsync<List<MaterialConsumerDto>>(
                   $"{Base}/runs/{runId}/material-consumers/{materialCode}") ?? new();

        public async Task<(bool Ok, List<RebalancePreviewDto>? Preview, string? Error)> RebalanceMaterialAsync(
            int runId, RebalanceMaterialRequest req, bool whatIf = true)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/runs/{runId}/rebalance-material?whatIf={whatIf}", req);

            if (!res.IsSuccessStatusCode)
                return (false, null, await res.Content.ReadAsStringAsync());

            return (true, await res.Content.ReadFromJsonAsync<List<RebalancePreviewDto>>(), null);
        }

        // ───────── سود و زیان به تفکیک واحد ─────────
        //
        // prodBasis دو سؤال متفاوت را جدا می‌کند:
        //   false → هر واحد چقدر «فروخت» (انبار فروش)
        //   true  → تولیدِ هر واحد چقدر سود داد، حتی وقتی از انبار واحد
        //           دیگری فروش رفته باشد

        public async Task<List<ItemMarginUnitDto>> GetMarginsByUnitAsync(
            int runId, int? unitId = null, bool prodBasis = false)
            => await _http.GetFromJsonAsync<List<ItemMarginUnitDto>>(
                   $"{Base}/runs/{runId}/margins-by-unit?basis={Basis(prodBasis)}" +
                   (unitId is null ? "" : $"&unitId={unitId}")) ?? new();

        public async Task<List<UnitMarginSummaryDto>> GetMarginUnitSummaryAsync(
            int runId, bool prodBasis = false)
            => await _http.GetFromJsonAsync<List<UnitMarginSummaryDto>>(
                   $"{Base}/runs/{runId}/margin-unit-summary?basis={Basis(prodBasis)}") ?? new();

        private static string Basis(bool prodBasis) => prodBasis ? "prod" : "sale";

        // ───────── پیشنهاد خودکار جابه‌جایی مواد ─────────

        public async Task<(bool Ok, RebalanceSuggestResultDto? Data, string? Error)>
            RebalanceSuggestAsync(int runId, long sourceCode, byte maxDepth = 2)
        {
            var res = await _http.GetAsync(
                $"{Base}/runs/{runId}/rebalance-suggest/{sourceCode}?maxDepth={maxDepth}");

            if (!res.IsSuccessStatusCode)
                return (false, null, await res.Content.ReadAsStringAsync());

            return (true, await res.Content.ReadFromJsonAsync<RebalanceSuggestResultDto>(), null);
        }

        public async Task<(bool Ok, RebalanceApplyResultDto? Data, string? Error)>
            RebalanceApplyAsync(int runId, RebalanceApplyRequest req, bool recompute = true)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/runs/{runId}/rebalance-apply?recompute={recompute}", req);

            if (!res.IsSuccessStatusCode)
                return (false, null, await res.Content.ReadAsStringAsync());

            return (true, await res.Content.ReadFromJsonAsync<RebalanceApplyResultDto>(), null);
        }

        public async Task<(bool Ok, RebalanceBatchResultDto? Data, string? Error)>
            RebalanceApplyBatchAsync(int runId, RebalanceBatchRequest req, bool recompute = true)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/runs/{runId}/rebalance-apply-batch?recompute={recompute}", req);

            if (!res.IsSuccessStatusCode)
                return (false, null, await res.Content.ReadAsStringAsync());

            return (true, await res.Content.ReadFromJsonAsync<RebalanceBatchResultDto>(), null);
        }

        public async Task ForgetRebalancePrefAsync(int runId, long sourceCode, long materialCode)
            => await _http.DeleteAsync($"{Base}/rebalance-pref/{sourceCode}/{materialCode}");

        /// <summary>
        /// بایت‌های گزارش اکسل هیئت‌مدیره.
        ///
        /// عمداً URL برنمی‌گرداند: توکن JWT و هدر X-DB-Connection روی
        /// DefaultRequestHeaders همین HttpClient نشسته‌اند، نه در کوکی. پس
        /// یک &lt;a href&gt; ساده (ناوبری مرورگر) بدون احراز هویت می‌رود و
        /// endpoint محافظت‌شده ۴۰۱ می‌دهد. باید با همین HttpClient گرفته و
        /// با downloadFileFromBytes به کاربر داده شود — همان الگویی که
        /// PayrollTab و ReportsTab در بخش حقوق و دستمزد استفاده می‌کنند.
        /// </summary>
        public async Task<byte[]> GetReportBytesAsync(
            int runId, int? unitId = null, bool prodBasis = false)
        {
            var res = await _http.GetAsync(
                $"{Base}/runs/{runId}/report.xlsx?basis={Basis(prodBasis)}" +
                (unitId is null ? "" : $"&unitId={unitId}"));

            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err)
                    ? $"خطا در تهیهٔ گزارش اکسل (کد {(int)res.StatusCode})."
                    : err);
            }

            return await res.Content.ReadAsByteArrayAsync();
        }

        public async Task<(bool Ok, string? Error)> ApproveAsync(int runId)
        {
            var res = await _http.PostAsync($"{Base}/runs/{runId}/approve", null);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── مرجع ─────────

        public async Task<List<CostCheckRuleDto>> GetRulesAsync()
            => await _http.GetFromJsonAsync<List<CostCheckRuleDto>>($"{Base}/rules") ?? new();

        public async Task<(bool Ok, string? Error)> UpdateRuleThresholdAsync(string ruleCode, double? threshold)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/rules/{ruleCode}/threshold",
                new UpdateRuleThresholdRequest { Threshold = threshold });
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<List<CostUnitDto>> GetUnitsAsync()
            => await _http.GetFromJsonAsync<List<CostUnitDto>>($"{Base}/units") ?? new();

        // ───────── مدیریت واحدها (تنظیمات) ─────────

        public async Task<(bool Ok, int UnitId, string? Error)> CreateUnitAsync(UpsertUnitRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/units", req);
            if (!res.IsSuccessStatusCode)
                return (false, 0, await res.Content.ReadAsStringAsync());

            return (true, await res.Content.ReadFromJsonAsync<int>(), null);
        }

        public async Task<(bool Ok, string? Error)> UpdateUnitAsync(int unitId, UpsertUnitRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/units/{unitId}", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteUnitAsync(int unitId)
        {
            var res = await _http.DeleteAsync($"{Base}/units/{unitId}");
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> AddUnitWarehouseAsync(
            int unitId, UpsertUnitAnbarRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/units/{unitId}/warehouses", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> UpdateUnitWarehouseAsync(
            int unitId, int anbar, UpsertUnitAnbarRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/units/{unitId}/warehouses/{anbar}", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteUnitWarehouseAsync(int unitId, int anbar)
        {
            var res = await _http.DeleteAsync($"{Base}/units/{unitId}/warehouses/{anbar}");
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> AddUnitAccountAsync(
            int unitId, UpsertUnitAccRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/units/{unitId}/accounts", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> UpdateUnitAccountAsync(
            int unitId, int accId, UpsertUnitAccRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/units/{unitId}/accounts/{accId}", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteUnitAccountAsync(int unitId, int accId)
        {
            var res = await _http.DeleteAsync($"{Base}/units/{unitId}/accounts/{accId}");
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── نگاشت انبار به حساب موجودی (CHK-02) ─────────

        public async Task<List<CostAnbarHesDto>> GetAnbarHesAsync()
            => await _http.GetFromJsonAsync<List<CostAnbarHesDto>>($"{Base}/anbar-hes") ?? new();

        public async Task<(bool Ok, string? Error)> AddAnbarHesAsync(UpsertAnbarHesRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/anbar-hes", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> UpdateAnbarHesAsync(int anbar, UpsertAnbarHesRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/anbar-hes/{anbar}", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteAnbarHesAsync(int anbar)
        {
            var res = await _http.DeleteAsync($"{Base}/anbar-hes/{anbar}");
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        // ───────── جستجوی زنجیره‌ای حساب ─────────

        public async Task<List<AccountLookupDto>> SearchKolAsync(string? q)
            => await _http.GetFromJsonAsync<List<AccountLookupDto>>(
                   $"{Base}/accounts/kol" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}")) ?? new();

        public async Task<List<AccountLookupDto>> SearchMoinAsync(int kol, string? q)
            => await _http.GetFromJsonAsync<List<AccountLookupDto>>(
                   $"{Base}/accounts/moin?kol={kol}"
                   + (string.IsNullOrEmpty(q) ? "" : $"&q={Uri.EscapeDataString(q)}")) ?? new();

        public async Task<List<AccountLookupDto>> SearchTafsiliAsync(int kol, int moin, string? q)
            => await _http.GetFromJsonAsync<List<AccountLookupDto>>(
                   $"{Base}/accounts/tafsili?kol={kol}&moin={moin}"
                   + (string.IsNullOrEmpty(q) ? "" : $"&q={Uri.EscapeDataString(q)}")) ?? new();

        // ───────── نرخ استاندارد دستمزد به تفکیک کالا ─────────

        public async Task<List<CostLaborRateDto>> GetLaborRatesAsync()
            => await _http.GetFromJsonAsync<List<CostLaborRateDto>>($"{Base}/labor-rates") ?? new();

        public async Task<(bool Ok, string? Error)> AddLaborRateAsync(UpsertLaborRateRequest req)
        {
            var res = await _http.PostAsJsonAsync($"{Base}/labor-rates", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> UpdateLaborRateAsync(int unitId, string code, UpsertLaborRateRequest req)
        {
            var res = await _http.PutAsJsonAsync($"{Base}/labor-rates/{unitId}/{Uri.EscapeDataString(code)}", req);
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<(bool Ok, string? Error)> DeleteLaborRateAsync(int unitId, string code)
        {
            var res = await _http.DeleteAsync($"{Base}/labor-rates/{unitId}/{Uri.EscapeDataString(code)}");
            return res.IsSuccessStatusCode
                 ? (true, null)
                 : (false, await res.Content.ReadAsStringAsync());
        }

        public async Task<List<ItemLookupDto>> SearchItemsAsync(string? q)
            => await _http.GetFromJsonAsync<List<ItemLookupDto>>(
                   $"{Base}/items/search" + (string.IsNullOrEmpty(q) ? "" : $"?q={Uri.EscapeDataString(q)}")) ?? new();

        public async Task<(bool Ok, int Added, string? Error)> SyncLaborRatesFromFormulasAsync()
        {
            var res = await _http.PostAsync($"{Base}/labor-rates/sync-from-formulas", null);
            if (!res.IsSuccessStatusCode)
                return (false, 0, await res.Content.ReadAsStringAsync());
            var added = await res.Content.ReadFromJsonAsync<int>();
            return (true, added, null);
        }
    }
}
