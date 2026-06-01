// In Safir.Server/Controllers/SettingsController.cs (New File)
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

[Route("api/[controller]")]
[ApiController]
// [Authorize(Roles = "Admin")] // <<< IMPORTANT: Add authorization for production!
public class SettingsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly Safir.Server.Services.IConnectionStringProvider _connectionStringProvider;

    public SettingsController(IConfiguration configuration, IWebHostEnvironment environment, Safir.Server.Services.IConnectionStringProvider connectionStringProvider)
    {
        _configuration = configuration;
        _environment = environment;
        _connectionStringProvider = connectionStringProvider;
    }

    // DTO to return the data
    public class DebugInfoDto
    {
        public string? EnvironmentName { get; set; }
        public string? DatabaseServer { get; set; } // Only return non-sensitive parts
        public string? DatabaseName { get; set; }   // Only return non-sensitive parts
        // DO NOT return the full connection string
    }

    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            var connectionString = _connectionStringProvider.GetConnectionString();
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return Ok(new { success = true, message = "اتصال به دیتابیس با موفقیت انجام شد." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"خطا در اتصال به دیتابیس: {ex.Message}" });
        }
    }

    [HttpGet("debug-info")]
    public ActionResult<DebugInfoDto> GetDebugInfo()
    {
        // --- SECURITY WARNING ---
        // Only enable or use this endpoint in Development or for Administrators
        // if (_environment.IsProduction() && !User.IsInRole("Admin"))
        // {
        //     return Forbid();
        // }
        // --- END WARNING ---

        var connectionString = _connectionStringProvider.GetConnectionString();
        string? dbServer = null;
        string? dbName = null;

        // Basic parsing to extract non-sensitive parts (adjust if needed)
        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                dbServer = builder.DataSource;
                dbName = builder.InitialCatalog;
            }
            catch { /* Ignore parsing errors */ }
        }


        var info = new DebugInfoDto
        {
            EnvironmentName = _environment.EnvironmentName,
            DatabaseServer = dbServer,
            DatabaseName = dbName
        };

        return Ok(info);
    }
}