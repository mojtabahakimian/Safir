using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Safir.Server.Hesabdari;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Hesabdari;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// تراز آزمایشی چهارستونی — کل، معین، تفصیلی و تفصیلی ۲ تا ۴، همان زنجیره‌ی
    /// پنجره‌های WPF. مجوزها از همان SAL_CHEK می‌آیند: فرم TARAZ_4 برای کل،
    /// TARAZ_4_MOIN برای معین و TARAZ_4_TAFZ برای تفصیلی و پایین‌تر (SEE لازم است).
    /// </summary>
    [ApiController]
    [Route("api/trial-balance")]
    [Authorize]
    public class TrialBalanceController : ControllerBase
    {
        private readonly TrialBalanceService _svc;
        private readonly IAppSettingsService _settings;
        private readonly ILogger<TrialBalanceController> _logger;

        public TrialBalanceController(IDatabaseService db, IAppSettingsService settings, ILogger<TrialBalanceController> logger)
        {
            _svc = new TrialBalanceService(db);
            _settings = settings;
            _logger = logger;
        }

        private int UserCo => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        [HttpGet("meta")]
        public async Task<ActionResult<TrialBalanceMetaDto>> Meta()
        {
            if (UserCo <= 0) return Unauthorized();
            var a = await _svc.GetAccessAsync(UserCo);
            var year = (await _settings.GetSazmanSettingsAsync())?.YEA ?? 0;
            return Ok(new TrialBalanceMetaDto { FiscalYear = year, CanKol = a.Kol, CanMoin = a.Moin, CanTafsili = a.Tafsili });
        }

        [HttpGet]
        public async Task<ActionResult<List<TrialBalanceRowDto>>> Get([FromQuery] TrialBalanceQuery q)
        {
            if (UserCo <= 0) return Unauthorized();
            if (TrialBalanceService.Validate(q) is { } bad) return BadRequest(bad);

            var access = await _svc.GetAccessAsync(UserCo);
            if (!TrialBalanceService.Allowed(access, q.Level))
                return StatusCode(403, $"اجازه‌ی دیدن این تراز (فرم {TrialBalanceService.FormFor(q.Level)}) را ندارید.");

            try
            {
                return Ok(await _svc.LoadAsync(q));
            }
            catch (SqlException ex) when (ex.Number == 2812)
            {
                // رویه را WPF در دیتابیس می‌سازد؛ اگر نیست، دیتابیس به‌روز نیست.
                _logger.LogWarning(ex, "Trial balance procedure missing: {Proc}", TrialBalanceService.ProcedureFor(q.Level));
                return StatusCode(500, $"رویه‌ی {TrialBalanceService.ProcedureFor(q.Level)} در این دیتابیس نیست. نرم‌افزار ویندوزی را به‌روز کنید.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trial balance failed for level {Level}", q.Level);
                return StatusCode(500, "خواندن تراز با خطا روبه‌رو شد.");
            }
        }
    }
}
