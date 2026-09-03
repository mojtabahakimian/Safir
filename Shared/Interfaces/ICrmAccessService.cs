using System.Threading.Tasks;
using Safir.Shared.Models.Crm;

namespace Safir.Shared.Interfaces
{
    /// <summary>
    /// کنترل دسترسی CRM. فقط یک سؤال را جواب می‌دهد: این کاربر داده‌های همه را
    /// می‌بیند یا فقط داده‌های خودش را؟
    /// </summary>
    public interface ICrmAccessService
    {
        Task<CrmAccessDto> GetAccessAsync(int userId, string userName);

        /// <summary>
        /// کشِ تنظیمات را دور می‌ریزد تا روشن/خاموش کردن CRM_ACL_ENFORCE
        /// بلافاصله اثر کند، نه بعد از سر رسیدن TTL.
        /// </summary>
        void InvalidateConfig();
    }
}
