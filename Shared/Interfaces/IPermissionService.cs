using Safir.Shared.Models.Permissions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IPermissionService
    {
        // بررسی دسترسی RUN برای کاربر و فرم مشخص
        Task<bool> CanUserRunFormAsync(int userId, string formCode);
        Task<UserPermissionDto?> GetUserPermissionsForFormAsync(int userId, string formCode); // اختیاری
    }
}
