using Safir.Shared.Interfaces;
using Safir.Shared.Models.User_Model;
using System.Text.Json;

namespace Safir.Server.Services
{
    public class UserStateService : IUserStateService
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<UserStateService> _logger;

        public UserStateService(IDatabaseService db, ILogger<UserStateService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<UserStateDto?> GetUserStateAsync(int userId)
        {
            try
            {
                var json = await _db.GetUserStateJsonAsync(userId);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<UserStateDto>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user state for {UserId}", userId);
                return null;
            }
        }

        public async Task SaveUserStateAsync(int userId, UserStateDto state)
        {
            try
            {
                var json = JsonSerializer.Serialize(state);
                await _db.SaveUserStateJsonAsync(userId, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user state for {UserId}", userId);
            }
        }

        public async Task ClearUserStateAsync(int userId)
        {
            try
            {
                await _db.ClearUserStateAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing user state for {UserId}", userId);
            }
        }
    }
}
