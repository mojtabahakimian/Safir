using Safir.Shared.Models.Salary;
using Safir.Shared.Models.Salary.Reports;
using System.Net.Http.Json;
using static Safir.Shared.Models.Salary.Reports.InsuranceReportDto;

namespace Safir.Client.Services
{
    public class Pay2RunApiService
    {
        private readonly HttpClient _http;
        public Pay2RunApiService(HttpClient http) => _http = http;

        public async Task<Pay2PeriodDto?> GetPeriodInfoAsync(int wsId, long periodDate)
        {
            var res = await _http.GetAsync($"api/pay2/run/period-info?wsId={wsId}&periodDate={periodDate}");

            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            try
            {
                return await res.Content.ReadFromJsonAsync<Pay2PeriodDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                // در صورتی که بادی کاملاً خالی باشد
                return null;
            }
        }

        public async Task<Pay2RunDto?> GetLatestRunAsync(int perId)
        {
            var res = await _http.GetAsync($"api/pay2/run/latest?perId={perId}");

            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            try
            {
                return await res.Content.ReadFromJsonAsync<Pay2RunDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        public async Task<Pay2RunResultDto> GetRunLinesAsync(int runId)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/lines");
            if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
                return new Pay2RunResultDto();

            try
            {
                return await res.Content.ReadFromJsonAsync<Pay2RunResultDto>() ?? new Pay2RunResultDto();
            }
            catch (System.Text.Json.JsonException)
            {
                return new Pay2RunResultDto();
            }
        }

        public async Task<int> CalculateRunAsync(Pay2RunCalcRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/run/calculate", request);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
            return await res.Content.ReadFromJsonAsync<int>();
        }

        public async Task RevertRunAsync(int runId)
        {
            var res = await _http.PutAsync($"api/pay2/run/{runId}/revert", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task FinalizeRunAsync(int runId)
        {
            var res = await _http.PutAsync($"api/pay2/run/{runId}/finalize", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task GenerateDeedAsync(int runId)
        {
            var res = await _http.PostAsync($"api/pay2/run/{runId}/generate-deed", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task UnfinalizeDeedAsync(int runId)
        {
            var res = await _http.PutAsync($"api/pay2/run/{runId}/unfinalize-deed", null);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }
        public async Task<Pay2DeedPreviewDto> PreviewDeedAsync(int runId, byte? overrideMode = null)
        {
            var url = $"api/pay2/run/{runId}/preview-deed";
            if (overrideMode.HasValue) url += $"?overrideMode={overrideMode.Value}";

            var res = await _http.GetAsync(url);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());

            return await res.Content.ReadFromJsonAsync<Pay2DeedPreviewDto>() ?? new Pay2DeedPreviewDto();
        }
        // دریافت بایت‌های اکسلِ تحلیلیِ فرمول‌دار برای کل اجرا
        public async Task<byte[]> GetExcelAuditAsync(int runId)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/excel-audit");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err)
                    ? $"خطا در تهیهٔ اکسل تحلیلی (کد {(int)res.StatusCode})."
                    : err);
            }
            return await res.Content.ReadAsByteArrayAsync();
        }

        // دریافت بایت‌های PDF فیش حقوقی یک پرسنل (برای نمایش در نمایشگر داخلی مرورگر)
        public async Task<byte[]> GetPayslipPdfAsync(int runId, int empId, bool isOfficial = false)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/employee/{empId}/payslip?isOfficial={isOfficial}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err)
                    ? $"خطا در دریافت فیش حقوقی (کد {(int)res.StatusCode})."
                    : err);
            }
            return await res.Content.ReadAsByteArrayAsync();
        }

        // دریافت بایت‌های PDF لیست بیمه
        public async Task<byte[]> GetInsuranceReportPdfAsync(int runId, int wsId = 0)
        {
            // 🚀 اضافه شدن کوئری پارامتر wsId برای پشتیبانی از گزارش تجمیعی کل سال
            var res = await _http.GetAsync($"api/pay2/run/{runId}/insurance-report?wsId={wsId}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err)
                    ? $"خطا در دریافت لیست بیمه (کد {(int)res.StatusCode})."
                    : err);
            }
            return await res.Content.ReadAsByteArrayAsync();
        }

        // دریافت بایت‌های فایل ZIP دیسکت بیمه
        public async Task<byte[]> GetInsuranceDisketteZipAsync(int runId)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/insurance-diskette");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err) ? "خطا در دریافت دیسکت بیمه." : err);
            }
            return await res.Content.ReadAsByteArrayAsync();
        }

        public async Task<DiskettePreviewDto> GetInsuranceDiskettePreviewAsync(int runId)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/insurance-diskette-preview");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err) ? "خطا در دریافت پیش‌نمایش دیسکت." : err);
            }
            return await res.Content.ReadFromJsonAsync<DiskettePreviewDto>() ?? new DiskettePreviewDto();
        }

        // دریافت بایت‌های PDF لیست مالیات حقوق
        public async Task<byte[]> GetTaxReportPdfAsync(int runId, int wsId = 0)
        {
            var res = await _http.GetAsync($"api/pay2/run/{runId}/tax-report?wsId={wsId}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err)
                    ? $"خطا در دریافت لیست مالیات (کد {(int)res.StatusCode})."
                    : err);
            }
            return await res.Content.ReadAsByteArrayAsync();
        }

        public async Task<Pay2MonthCompareResultDto> CompareMonthsAsync(int wsId, long period1, long period2)
        {
            var res = await _http.GetAsync($"api/pay2/run/compare-months?wsId={wsId}&period1={period1}&period2={period2}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err) ? "خطا در دریافت گزارش مقایسه ماه‌ها." : err);
            }
            return await res.Content.ReadFromJsonAsync<Pay2MonthCompareResultDto>() ?? new Pay2MonthCompareResultDto();
        }

        // دریافت فایل اکسل گزارش مالیات سالانه
        public async Task<byte[]> GetAnnualTaxReportExcelAsync(int wsId, long periodDate)
        {
            var res = await _http.GetAsync($"api/pay2/run/tax-report-excel?wsId={wsId}&periodDate={periodDate}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(err) ? "خطا در دریافت گزارش مالیات." : err);
            }
            return await res.Content.ReadAsByteArrayAsync();
        }
    }
}