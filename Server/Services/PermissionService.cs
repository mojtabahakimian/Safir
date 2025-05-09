using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;
using System.Data.SqlClient;

namespace Safir.Server.Services
{
    public class PermissionService : IPermissionService
    {
        private readonly string _connectionString;
        private readonly ILogger<PermissionService> _logger; // برای لاگ خطا

        public PermissionService(IConfiguration configuration, ILogger<PermissionService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        // معادل تابع WANTEDFORM شما
        private string GetMappedFormName(string formCode)
        {
            // این mapping را بر اساس مقادیر واقعی در جدول TFORMS تنظیم کنید
            return formCode?.ToUpperInvariant() switch
            {
                "DEFA" => "DEFAULT", // مثال
                "CUSTEN" => "CUSTEN", // مثال
                "ELAMGHE" => "elamghe", // مثال - دقت کنید که با مقدار در TFORMS یکی باشد
                "TFTMLOCK" => "TFTMLOCK", // مثال
                _ => formCode // اگر mapping وجود نداشت، خود کد را برگردان
            };
        }

        public async Task<UserPermissionDto?> GetUserPermissionsForFormAsync(int userId, string formCode)
        {
            var formName = GetMappedFormName(formCode);
            if (string.IsNullOrEmpty(formName))
            {
                _logger.LogWarning("Invalid or unmapped formCode provided: {FormCode}", formCode);
                return null;
            }

            var sql = @"
                SELECT TOP 1
                    f.FORMNAME AS FormName,
                    sc.USERCO AS UserCo,
                    CAST(ISNULL(sc.RUN, 0) AS BIT) AS Run,
                    CAST(ISNULL(sc.SEE, 0) AS BIT) AS See,
                    CAST(ISNULL(sc.INP, 0) AS BIT) AS Inp,
                    CAST(ISNULL(sc.UPD, 0) AS BIT) AS Upd,
                    CAST(ISNULL(sc.DEL, 0) AS BIT) AS Del
                FROM dbo.TFORMS f
                INNER JOIN dbo.SAL_CHEK sc ON f.IDH = sc.OBJECT
                WHERE f.FORMNAME = @FormName AND sc.USERCO = @UserId";

            try
            {
                using var connection = new SqlConnection(_connectionString);
                var permissions = await connection.QueryFirstOrDefaultAsync<UserPermissionDto>(sql, new { FormName = formName, UserId = userId });

                if (permissions == null)
                {
                    _logger.LogInformation("No specific permission found for User {UserId}, Form {FormName}. Returning defaults (false).", userId, formName);
                    // برگرداندن یک آبجکت با مقادیر false اگر رکوردی یافت نشد
                    // یا null برگردانید و در کنترلر مدیریت کنید
                    return new UserPermissionDto { UserCo = userId, FormName = formName }; // All flags default to false
                }
                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching permissions for User {UserId}, Form {FormName}", userId, formName);
                return null; // یا throw کنید
            }
        }

        public async Task<bool> CanUserRunFormAsync(int userId, string formCode)
        {
            var permissions = await GetUserPermissionsForFormAsync(userId, formCode);
            // اگر رکورد دسترسی پیدا شد و Run برابر true بود، true برگردان
            return permissions?.Run ?? false;
        }
    }
}
