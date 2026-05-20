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

    }
}