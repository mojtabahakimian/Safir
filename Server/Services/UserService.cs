// In Safir.Server/Services/UserService.cs
using Dapper;
using Safir.Shared.Interfaces; // Make sure this is included
using Safir.Shared.Models.User_Model;
using Safir.Shared.Utility;
using System.Data.SqlClient;

namespace Safir.Server.Services
{
    public class UserService : IUserService
    {
        // Change the type here to the interface
        private readonly IDatabaseService dbms; // _dbService Use the interface type

        // Inject the interface in the constructor
        public UserService(IDatabaseService dbService) // Inject the interface
        {
            dbms = dbService; // Assign to the interface field
            // Ensure Encoding provider is registered if needed by decoding
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
    }
}