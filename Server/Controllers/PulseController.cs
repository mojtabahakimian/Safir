using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Pulse;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Pulse;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// «نبض سازمان» — فروش، پیش‌فاکتور، فعالیت‌ها، نقد و چک در ۱۸۰ روزِ اخیر.
    /// دسترسی با فرم اختصاصی PULSE («نبض سازمان») در TFORMS و تیک‌های RUN و SEE در SAL_CHEK کنترل می‌شود.
    /// </summary>
    [ApiController]
    [Route("api/pulse")]
    [Authorize]
    public class PulseController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<PulseController> _logger;

        public PulseController(IDatabaseService db, ILogger<PulseController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private int UserCo => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        [HttpGet]
        public async Task<ActionResult<PulseDto>> Get()
        {
            if (UserCo <= 0) return Unauthorized();
            if (!await CanAccessPulseAsync(UserCo))
                return StatusCode(403, "برای دیدنِ نبض سازمان، دسترسیِ «نبض سازمان» لازم است.");
            try
            {
                return Ok(await new PulseService(_db).LoadAsync(DateTime.Now));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pulse dashboard failed");
                return StatusCode(500, "خواندنِ داده‌ی نبض سازمان با خطا روبه‌رو شد.");
            }
        }

        private async Task<bool> CanAccessPulseAsync(int userCo)
        {
            const string sql = @"
                SELECT TOP 1
                    CAST(CASE WHEN ISNULL(sc.RUN, 0) = 1 AND ISNULL(sc.SEE, 0) = 1 THEN 1 ELSE 0 END AS bit)
                FROM dbo.TFORMS f
                JOIN dbo.SAL_CHEK sc ON sc.[OBJECT] = f.IDH AND sc.USERCO = @userCo
                WHERE f.FORMNAME = N'PULSE'";
            return await _db.DoGetDataSQLAsyncSingle<bool>(sql, new { userCo });
        }
    }
}
