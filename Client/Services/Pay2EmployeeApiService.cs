using Safir.Shared.Models;
using Safir.Shared.Models.Salary;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    public class Pay2EmployeeApiService
    {
        private readonly HttpClient _http;
        public Pay2EmployeeApiService(HttpClient http) => _http = http;

        public async Task<List<Pay2EmployeeDto>> GetEmployeesAsync()
            => await _http.GetFromJsonAsync<List<Pay2EmployeeDto>>("api/pay2/employees") ?? new();

        public async Task<int> SaveEmployeeAsync(Pay2EmployeeDto emp)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/employees/save", emp);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
            return await res.Content.ReadFromJsonAsync<int>();
        }

        public async Task<List<Pay2DecreeDto>> GetDecreesAsync(int empId)
            => await _http.GetFromJsonAsync<List<Pay2DecreeDto>>($"api/pay2/employees/{empId}/decrees") ?? new();

        public async Task<int> SaveDecreeAsync(Pay2DecreeDto decree)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/employees/decree/save", decree);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
            return await res.Content.ReadFromJsonAsync<int>();
        }

        public async Task<IEnumerable<LookupDto<int>>> GetJobsLookupAsync(string? searchTerm, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _http.GetFromJsonAsync<List<LookupDto<int>>>(
                    $"api/pay2/employees/jobs-lookup?searchTerm={searchTerm}",
                    cancellationToken);

                return response ?? new List<LookupDto<int>>();
            }
            catch (Exception ex)
            {
                // ✅ مهار کامل استثنا (خطای ۵۰۰ یا لغو ناگهانی اتصال شبکه)
                // این کار باعث می‌شود کامپوننت جنریک شما کرش نکرده و وضعیت در حال جستجو به صورت ایمن خاموش شود
                Console.WriteLine($"[PAY2 UI Handled] Jobs lookup API error: {ex.Message}");

                return Enumerable.Empty<LookupDto<int>>();
            }
        }
        public async Task<List<LookupDto<int>>> GetTemplatesLookupAsync()
        {
            return await _http.GetFromJsonAsync<List<LookupDto<int>>>("api/pay2/employees/templates-lookup") ?? new();
        }
        public async Task DeleteDecreeAsync(int decId)
        {
            var res = await _http.DeleteAsync($"api/pay2/employees/decree/{decId}");
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task<List<LookupDto<int>>> GetItemDefsLookupAsync()
        {
            return await _http.GetFromJsonAsync<List<LookupDto<int>>>("api/pay2/employees/itemdefs-lookup") ?? new();
        }
        public async Task<List<Pay2DecreeLineDto>> GetDecreeLinesAsync(int decId)
        {
            return await _http.GetFromJsonAsync<List<Pay2DecreeLineDto>>($"api/pay2/employees/decree/{decId}/lines") ?? new();
        }
        public async Task SaveDecreeLineAsync(Pay2DecreeLineDto line)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/employees/decree/line/save", line);
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }
        public async Task DeleteDecreeLineAsync(int decId, int itemId)
        {
            var res = await _http.DeleteAsync($"api/pay2/employees/decree/{decId}/line/{itemId}");
            if (!res.IsSuccessStatusCode)
                throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task<List<LookupDto<int>>> GetEmployeesLookupAsync()
        {
            return await _http.GetFromJsonAsync<List<LookupDto<int>>>("api/pay2/employees/lookup") ?? new();
        }

        public async Task<int> GetLeaveBalanceAsync(int empId, int year)
        {
            return await _http.GetFromJsonAsync<int>($"api/pay2/employees/{empId}/leave-balance?year={year}");
        }

        public async Task<List<Pay2LeaveDto>> GetLeavesAsync(int empId)
        {
            return await _http.GetFromJsonAsync<List<Pay2LeaveDto>>($"api/pay2/employees/{empId}/leaves") ?? new();
        }

        public async Task SaveLeaveAsync(Pay2LeaveDto leave)
        {
            var res = await _http.PostAsJsonAsync("api/pay2/employees/leave/save", leave);
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }

        public async Task DeleteLeaveAsync(int levId)
        {
            var res = await _http.DeleteAsync($"api/pay2/employees/leave/{levId}");
            if (!res.IsSuccessStatusCode) throw new Exception(await res.Content.ReadAsStringAsync());
        }
    }
}