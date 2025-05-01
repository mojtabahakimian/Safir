// In Safir.Server/Services/UserService.cs
using Dapper;
using Safir.Shared.Interfaces; // Make sure this is included
using Safir.Shared.Models.User_Model;
using Safir.Shared.Utility;
using System.Data.SqlClient;
using System.Security.Claims;

namespace Safir.Server.Services
{
    public class UserService : IUserService
    {
        // Change the type here to the interface
        private readonly IDatabaseService dbms; // _dbService Use the interface type

        private readonly IDatabaseService _dbService;
        private readonly IHttpContextAccessor _httpContextAccessor; // برای دسترسی به User Claims
        private readonly ILogger<UserService> _logger;

        // Inject the interface in the constructor
        public UserService(IDatabaseService dbService, IHttpContextAccessor httpContextAccessor, ILogger<UserService> logger)
        {
            _dbService = dbService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        public async Task<SALA_DTL?> GetUserByEncodedUsernameAsync(string encodedUsername)
        {
            const string sql = "SELECT IDD, SAL_NAME, PSAL_NAME, GRSAL,PORID , ENABL FROM SALA_DTL WHERE SAL_NAME = @Username AND ENABL = 0";
            try
            {
                // Use the injected interface field
                var user = await dbms.DoGetDataSQLAsyncSingle<SALA_DTL>(sql, new { Username = encodedUsername });

                return user;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching user by encoded username '{encodedUsername}': {ex.Message}");
                return null;
            }
        }

        public async Task<SALA_DTL?> GetUserByDecodedUsernameAsync(string decodedUsername)
        {
            const string sql = "SELECT IDD, SAL_NAME, PSAL_NAME, GRSAL, HES, PORID,erjabe, ENABL FROM SALA_DTL WHERE ENABL = 0";
            try
            {
                // Use the injected interface field
                var allUsers = await dbms.DoGetDataSQLAsync<SALA_DTL>(sql);

                var targetUsernameFixed = decodedUsername.FixPersianChars();
                var user = allUsers.FirstOrDefault(u =>
                     CL_METHODS.DECODEUN(u.SAL_NAME).FixPersianChars().Equals(targetUsernameFixed, StringComparison.OrdinalIgnoreCase)
                );

                return user;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching or decoding users for username '{decodedUsername}': {ex.Message}");
                return null;
            }
        }

        public async Task<bool> CanViewSubordinateTasksAsync()
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out int userCod))
            {
                _logger.LogWarning("CanViewSubordinateTasksAsync: Could not parse User ID from claims.");
                return false; // یا throw exception?
            }

            try
            {
                // کوئری برای چک کردن وجود رکورد در CHARTSAZMANI
                const string sql = "SELECT COUNT(*) FROM dbo.CHARTSAZMANI WHERE USERCO = @UserCod";
                int count = await _dbService.DoGetDataSQLAsyncSingle<int>(sql, new { UserCod = userCod });
                bool canView = count > 0;
                _logger.LogInformation("CanViewSubordinateTasksAsync: User {UserCod} check resulted in: {CanViewResult} (Count: {Count})", userCod, canView, count);
                return canView;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CanViewSubordinateTasksAsync: Error checking CHARTSAZMANI for User {UserCod}", userCod);
                return false; // در صورت خطا، دسترسی نده
            }
        }
    }
}