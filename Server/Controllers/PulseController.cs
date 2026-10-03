using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Hesabdari;
using Safir.Server.Pulse;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Pulse;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// «نبض سازمان» — فروش، پیش‌فاکتور، فعالیت‌ها، نقد و چک در ۱۸۰ روزِ اخیر.
    /// پنجره‌های نبضِ WPF در TFORMS تعریف نشده‌اند و دسترسی ندارند؛ چون اینجا از بیرون هم در دسترس است و
    /// جمعِ فروش و صندوق را نشان می‌دهد، همان مجوزِ «تراز آزمایشی چهارستونی کل» (TARAZ_4، SEE) لازم است.
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
            var access = await new TrialBalanceService(_db).GetAccessAsync(UserCo);
            if (!access.Kol)
                return StatusCode(403, "برای دیدنِ نبض سازمان، دسترسیِ «تراز آزمایشی» لازم است.");
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
    }
}
