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

    public SettingsController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    // DTO to return the data
    public class DebugInfoDto
    {
        public string? EnvironmentName { get; set; }
        public string? DatabaseServer { get; set; } // Only return non-sensitive parts
        public string? DatabaseName { get; set; }   // Only return non-sensitive parts
        // DO NOT return the full connection string
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

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
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