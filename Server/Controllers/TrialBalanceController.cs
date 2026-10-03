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
            var sazman = await _settings.GetSazmanSettingsAsync();
            return Ok(new TrialBalanceMetaDto { CompanyName = sazman?.NAME?.Trim(), FiscalYear = sazman?.YEA ?? 0, CanKol = a.Kol, CanMoin = a.Moin, CanTafsili = a.Tafsili });
        }

        [HttpGet]
        public async Task<ActionResult<List<TrialBalanceRowDto>>> Get([FromQuery] TrialBalanceQuery q)
        {
            if (UserCo <= 0) return Unauthorized();
            if (TrialBalanceService.Validate(q) is { } bad) return BadRequest(bad);

            var access = await _svc.GetAccessAsync(UserCo);
            if (!TrialBalanceService.Allowed(access, q.Level))
                return StatusCode(403, "اجازه‌ی دیدن این تراز را ندارید.");

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

        /// <summary>تراز ماهانه — کل، معین (یک کل) یا تفصیلی (یک کل، یک یا همه‌ی معین‌ها).</summary>
        [HttpGet("monthly")]
        public async Task<ActionResult<List<TrialBalanceMonthlyRowDto>>> Monthly([FromQuery] TrialBalanceQuery q)
        {
            if (UserCo <= 0) return Unauthorized();
            if (TrialBalanceService.ValidateMonthly(q) is { } bad) return BadRequest(bad);

            var access = await _svc.GetAccessAsync(UserCo);
            if (!TrialBalanceService.Allowed(access, q.Level))
                return StatusCode(403, "اجازه‌ی دیدن این تراز را ندارید.");

            try
            {
                return Ok(await _svc.LoadMonthlyAsync(q));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Monthly trial balance failed for level {Level}", q.Level);
                return StatusCode(500, "خواندن تراز ماهانه با خطا روبه‌رو شد.");
            }
        }

        /// <summary>حساب‌های کل، یا معین‌های یک کل — برای فیلترِ تراز معین و تفصیلی.</summary>
        [HttpGet("accounts")]
        public async Task<ActionResult<List<TrialBalanceAccountDto>>> Accounts([FromQuery] int? kol)
        {
            if (UserCo <= 0) return Unauthorized();
            var a = await _svc.GetAccessAsync(UserCo);
            if (!a.Kol && !a.Moin && !a.Tafsili) return StatusCode(403, "اجازه‌ی دیدن تراز آزمایشی را ندارید.");
            return Ok(await _svc.AccountsAsync(kol));
        }
    }
}
