using System.Collections.Generic;
using System.Threading.Tasks;
using Safir.Shared.Models.Permissions;

namespace Safir.Shared.Interfaces
{
    public interface IPay2AccessService
    {
        Task<Pay2AccessDto> GetAccessAsync(int userCo);
        Task<bool> HasAsync(int userCo, string form, int permVal);
        Task<bool> CanAccessWorkshopAsync(int userCo, int wsId);
        Task<IReadOnlyList<int>> GetAllowedWorkshopIdsAsync(int userCo);
        Task InvalidateAsync(int userCo);
        Task AuditAsync(Pay2AuditEntry entry);
    }
}
