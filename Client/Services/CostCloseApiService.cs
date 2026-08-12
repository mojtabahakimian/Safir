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

        public async Task<bool> ResolveAsync(long id, string? note)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/exceptions/{id}/resolve", new ResolveExceptionRequest { Note = note });
            return res.IsSuccessStatusCode;
        }

        public async Task<bool> AcceptPermanentlyAsync(long id, string reason)
        {
            var res = await _http.PostAsJsonAsync(
                $"{Base}/exceptions/{id}/accept-permanently",
                new ResolveExceptionRequest { Note = reason });
            return res.IsSuccessStatusCode;
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
        public async Task<byte[]> GetReportBytesAsync(int runId)
        {
            var res = await _http.GetAsync($"{Base}/runs/{runId}/report.xlsx");

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

        public async Task<List<CostUnitDto>> GetUnitsAsync()
            => await _http.GetFromJsonAsync<List<CostUnitDto>>($"{Base}/units") ?? new();
    }
}
