using Safir.Shared.Models.Crm;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface ICrmApiService
    {
        Task<List<CrmCompanyDto>> GetCompaniesAsync(CrmFilterDto filter);
        Task<CrmCompanyDto?> GetCompanyByIdAsync(int id);
        Task<int> SaveCompanyAsync(CrmCompanyDto company);
        Task<bool> DeleteCompanyAsync(int id);

        Task<List<CrmEventDto>> GetEventsByCompanyIdAsync(int companyId);
        Task<int> SaveEventAsync(CrmEventDto crmEvent);
        Task<bool> DeleteEventAsync(int eventId);

        Task<List<CrmStatusDto>> GetStatusListAsync();
        Task<CrmDashboardSummaryDto> GetDashboardSummaryAsync();
        Task<CrmDuplicateCheckResultDto> CheckDuplicateAsync(string? companyName, string? tel, string? mobile, int? excludeCompanyId = null);
        Task<List<CrmPhoneBookItemDto>> SearchPhoneBookAsync(string query);
        Task<List<string>> GetDistinctSalersAsync();
        Task<List<string>> GetDistinctBuyersAsync();
        Task<List<string>> GetDistinctStatusFactsAsync();
    }
}
