namespace Safir.Server.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Safir.Shared.Interfaces;
    using Safir.Shared.Models;
    using System.Threading.Tasks;

    [Route("api/appsettings")]
    [ApiController]
    [Authorize]
    public class AppSettingsController : ControllerBase
    {
        private readonly IAppSettingsService _appSettingsService;
        private readonly ILogger<AppSettingsController> _logger;

        // cooldown: حداقل ۳۰ ثانیه بین هر refresh
        private static DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
        private static readonly TimeSpan _refreshCooldown = TimeSpan.FromSeconds(30);

        public AppSettingsController(IAppSettingsService appSettingsService, ILogger<AppSettingsController> logger)
        {
            _appSettingsService = appSettingsService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<SAZMAN>> GetAppSettings()
        {
            _logger.LogInformation("API request received for GetAppSettings");
            var settings = await _appSettingsService.GetSazmanSettingsAsync();
            if (settings == null)
            {
                _logger.LogWarning("SAZMAN settings are not available.");
                return NotFound("Application settings could not be loaded.");
            }
            return Ok(settings);
        }

        [HttpPost("refresh")]
        public async Task<ActionResult<SAZMAN>> RefreshAppSettings()
        {
            var now = DateTimeOffset.UtcNow;
            var remaining = _refreshCooldown - (now - _lastRefresh);
            if (remaining > TimeSpan.Zero)
            {
                return StatusCode(429, $"لطفاً {(int)remaining.TotalSeconds} ثانیه دیگر صبر کنید.");
            }

            _logger.LogInformation("Cache reset requested for AppSettingsService by user {User}",
                User.Identity?.Name);
            _appSettingsService.ResetCache();
            _lastRefresh = now;

            var settings = await _appSettingsService.GetSazmanSettingsAsync();
            if (settings == null)
            {
                return NotFound("Application settings could not be loaded after refresh.");
            }
            return Ok(settings);
        }
    }
}
