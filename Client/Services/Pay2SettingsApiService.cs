using System.Net.Http.Json;
using Safir.Shared.Models;
using Safir.Shared.Models.Salary;

namespace Safir.Client.Services
{
    public class Pay2SettingsApiService
    {
        private readonly HttpClient _http;

        public Pay2SettingsApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<Pay2ConfigDto>> GetConfigsAsync()
            => await _http.GetFromJsonAsync<List<Pay2ConfigDto>>("api/pay2/settings/configs") ?? new();

        public async Task SaveConfigsAsync(Pay2ConfigSaveRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/settings/configs/save", request);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task<List<short>> GetTaxYearsAsync()
            => await _http.GetFromJsonAsync<List<short>>("api/pay2/settings/tax/years") ?? new();

        public async Task<List<Pay2TaxBracketDto>> GetTaxBracketsAsync(short? year)
        {
            var url = year.HasValue
                ? $"api/pay2/settings/tax/brackets?year={year.Value}"
                : "api/pay2/settings/tax/brackets";

            return await _http.GetFromJsonAsync<List<Pay2TaxBracketDto>>(url) ?? new();
        }

        public async Task SaveTaxBracketsAsync(Pay2TaxBracketSaveRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/settings/tax/brackets/save", request);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task CopyTaxYearAsync(Pay2TaxBracketCopyRequest request)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/settings/tax/brackets/copy-year", request);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        private string? _cachedShiftMode;

        public async Task<string> GetShiftModeAsync()
        {
            if (_cachedShiftMode is not null) return _cachedShiftMode;
            try
            {
                var configs = await GetConfigsAsync();
                _cachedShiftMode = configs.FirstOrDefault(c => c.CFG_KEY == "SHIFT_MODE")?.CFG_VALUE ?? "PCT";
            }
            catch { _cachedShiftMode = "PCT"; }
            return _cachedShiftMode;
        }

        public static bool IsShiftPctItem(string shiftMode, IEnumerable<Pay2ItemDefDto> defs, int itemId)
        {
            if (string.Equals(shiftMode, "FIXED", StringComparison.OrdinalIgnoreCase)) return false;
            var def = defs.FirstOrDefault(d => d.ITEM_ID == itemId);
            return string.Equals(def?.ITEM_CODE, "SHIFT", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly HashSet<string> _editableItemFilter = new(StringComparer.OrdinalIgnoreCase)
            { "INS_DED", "TAX_DED", "LOAN_DED", "ADVANCE_DED" };

        public static List<LookupDto<int>> FilterEditableItemDefs(IEnumerable<Pay2ItemDefDto> defs) =>
            defs.Where(d => d.IS_ACTIVE
                         && (d.ITEM_TYPE == 1 || d.ITEM_TYPE == 2)
                         && !_editableItemFilter.Contains(d.ITEM_CODE ?? ""))
                .Select(d => new LookupDto<int>(d.ITEM_ID, d.ITEM_NAME ?? ""))
                .ToList();
    }
}