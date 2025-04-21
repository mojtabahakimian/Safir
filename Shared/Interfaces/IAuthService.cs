using Safir.Shared.Models.User_Model;

namespace Safir.Shared.Interfaces
{
    public interface IAuthService
    {
        Task<LoginResult> Login(LoginRequest loginRequest);
        Task Logout();
        Task<string?> GetTokenAsync(); // Helper to get current token
    }
}
