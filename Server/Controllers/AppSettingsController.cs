namespace Safir.Server.Controllers
{
    // File: Server/Controllers/AppSettingsController.cs (یا اکشن در SettingsController)
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Safir.Shared.Interfaces;
    using Safir.Shared.Models;
    using System.Threading.Tasks;

    [Route("api/appsettings")] // آدرس API
    [ApiController]
    [Authorize] // <<-- دسترسی فقط برای کاربران لاگین کرده
    public class AppSettingsController : ControllerBase
    {
        private readonly IAppSettingsService _appSettingsService;
        private readonly ILogger<AppSettingsController> _logger;

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
            _logger.LogInformation("Returning SAZMAN settings via API.");
            return Ok(settings);
        }

        [HttpPost("refresh")]
        public async Task<ActionResult<SAZMAN>> RefreshAppSettings()
        {
            _logger.LogInformation("Cache reset requested for AppSettingsService");
            _appSettingsService.ResetCache();
            var settings = await _appSettingsService.GetSazmanSettingsAsync();
            if (settings == null)
            {
                return NotFound("Application settings could not be loaded after refresh.");
            }
            return Ok(settings);
        }
    }
}
