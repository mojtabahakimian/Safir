using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;

namespace Safir.Server.Security
{
    /// <summary>
    /// بررسی مجوز به‌همراه ثبت در سابقه‌ی امنیتی.
    ///
    /// چرا لازم است: اتریبیوت [Pay2Authorize] بعد از هر تصمیم، رکوردی در
    /// PAY2_SEC_AUDIT می‌نویسد. ولی چند اکشن مجوز را داخل بدنه بررسی
    /// می‌کنند (چون مجوز لازم به داده‌ی ورودی بستگی دارد — مثلاً ساخت
    /// کارگاه جدید Inp می‌خواهد و ویرایش Upd). آن بررسی‌های داخلی فقط
    /// HasAsync صدا می‌زدند و هیچ ردی از خود باقی نمی‌گذاشتند، پس تلاش‌های
    /// ناموفق روی حساس‌ترین عملیات‌ها — تأیید حکم، تغییر تنظیمات حیاتی،
    /// مدیریت دسترسی‌ها — در سابقه دیده نمی‌شد.
    ///
    /// این متد همان کار اتریبیوت را برای بررسی‌های داخل بدنه انجام می‌دهد.
    /// </summary>
    public static class Pay2AccessAuditExtensions
    {
        public static async Task<bool> HasAndAuditAsync(
            this IPay2AccessService access,
            HttpContext http,
            int userCo,
            string form,
            Pay2Perm perm)
        {
            bool allowed = await access.HasAsync(userCo, form, (int)perm);

            await access.AuditAsync(new Pay2AuditEntry
            {
                UserCo = userCo,
                UserName = http.User.FindFirst(BaseknowClaimTypes.UUSER)?.Value
                           ?? http.User.FindFirst(ClaimTypes.Name)?.Value,
                FormName = form,
                PermFlag = perm.ToString(),
                HttpMethod = http.Request.Method,
                Path = http.Request.Path,
                Ip = http.Connection.RemoteIpAddress?.ToString(),
                Allowed = allowed
            });

            return allowed;
        }
    }
}
