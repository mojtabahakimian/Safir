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

        /// <summary>
        /// کشِ تنظیماتِ کنترل دسترسی (ACL_ENFORCE و هم‌خانواده‌هایش) را دور می‌ریزد.
        /// بعد از ذخیره‌ی صفحه‌ی «تنظیمات سیستم» صدا زده می‌شود تا روشن/خاموش کردن
        /// کنترل دسترسی بلافاصله اثر کند، نه بعد از سر رسیدن TTL.
        /// </summary>
        Task InvalidateConfigAsync();
        Task AuditAsync(Pay2AuditEntry entry);
    }
}
