// Server/Controllers/HealthCheckController.cs
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class HealthCheckController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<HealthCheckController> _logger;

        public HealthCheckController(IDatabaseService dbService, ILogger<HealthCheckController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            string dbStatus = "Unhealthy";
            string dbMessage = "Database connection could not be verified.";
            try
            {
                var result = await _dbService.DoGetDataSQLAsyncSingle<int>("SELECT 1");
                if (result == 1)
                {
                    dbStatus = "Healthy";
                    dbMessage = "Database connection successful.";
                    _logger.LogInformation("Health check: Database connection successful.");
                    return Ok(new { Status = "Healthy", Message = "Server and Database are reachable.", DatabaseStatus = dbStatus });
                }
                else
                {
                    _logger.LogWarning("Health check: Database query did not return expected result (1).");
                    dbMessage = "Database query did not return expected result.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check: Database connection failed.");
                dbMessage = $"Database connection failed: {ex.Message}";
            }
            // اگر به اینجا برسیم یعنی دیتابیس مشکل داشته یا کوئری موفق نبوده
            return StatusCode(503, new { Status = "Unhealthy", Message = "Server is reachable, but there is an issue with the database.", DatabaseStatus = dbStatus, DatabaseMessage = dbMessage });
        }
    }
}