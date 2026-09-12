using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Security;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Models.DbAdmin;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// صفحه‌ی ادمینِ به‌روزرسانی دیتابیس.
    ///
    /// زیرِ PAY2_ADMIN_ACL می‌نشیند — همان مجوزی که «چه کسی به چه چیزی
    /// دسترسی دارد» را تعیین می‌کند. فرمِ تازه‌ای با پیشوندِ جدید تعریف
    /// نشد چون فیلترهای LIKE روی TFORMS در Pay2AccessService باید همراهش
    /// به‌روز می‌شدند و همان دامی است که یک بار برای COST_* افتاد و فقط
    /// روی دیتابیس واقعی معلوم شد.
    /// </summary>
    [ApiController]
    [Route("api/dbadmin")]
    [Authorize]
    public sealed class DbAdminController : ControllerBase
    {
        private readonly DbUpgradeService _svc;
        private readonly ILogger<DbAdminController> _logger;

        public DbAdminController(DbUpgradeService svc, ILogger<DbAdminController> logger)
        {
            _svc    = svc;
            _logger = logger;
        }

        /// <summary>وضعیت دیتابیسِ جاری. فقط می‌خواند.</summary>
        [HttpGet("status")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.See)]
        public async Task<ActionResult<DbUpgradeStatus>> GetStatus()
        {
            try
            {
                return Ok(await _svc.GetStatusAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خواندن وضعیت دیتابیس ممکن نشد.");
                return StatusCode(500, "خواندن وضعیت دیتابیس ممکن نشد: " + ex.Message);
            }
        }

        /// <summary>
        /// اجرا — چه خشک و چه واقعی. تأییدیه روی خودِ سرور بررسی می‌شود،
        /// نه فقط در رابط کاربری: اگر فقط آنجا بود، یک POST ساده از بیرون
        /// همان DDL را بدون هیچ پرسشی اجرا می‌کرد.
        /// </summary>
        [HttpPost("upgrade")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<ActionResult<DbUpgradeResult>> Upgrade(
            [FromBody] DbUpgradeRequest req, CancellationToken ct)
        {
            if (req is null) return BadRequest("درخواست خالی است.");

            var status = await _svc.GetStatusAsync();

            // نامِ دیتابیس باید دقیقاً تایپ شده باشد. مقایسه بدون حساسیت
            // به بزرگی و کوچکی، چون نام‌ها در SQL Server هم همین‌طورند.
            if (!string.Equals(req.ConfirmDatabase?.Trim(), status.Database,
                               StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    $"نام دیتابیس تأیید نشد. باید دقیقاً «{status.Database}» نوشته شود.");
            }

            if (!req.PreviewOnly && !req.BackupConfirmed)
                return BadRequest("برای اجرای واقعی، تأیید گرفتن بکاپ اجباری است.");

            _logger.LogWarning(
                "به‌روزرسانی دیتابیس {Db} روی {Server} توسط {User} آغاز شد (preview={Preview}).",
                status.Database, status.Server,
                User.Identity?.Name ?? "?", req.PreviewOnly);

            try
            {
                var result = await _svc.RunAsync(req.PreviewOnly, ct);

                _logger.LogWarning(
                    "به‌روزرسانی دیتابیس {Db} پایان یافت. کد خروج={Code}، مدت={Ms}ms",
                    status.Database, result.ExitCode, result.DurationMs);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "اجرای به‌روزرسانی دیتابیس شکست خورد.");
                return StatusCode(500, "اجرای به‌روزرسانی ممکن نشد: " + ex.Message);
            }
        }
    }
}
