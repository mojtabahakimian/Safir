using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Safir.Shared.Models.User_Model;

namespace Safir.Client.Services
{
    public class UserStateApiService
    {
        private readonly HttpClient _http;
        private readonly ILogger<UserStateApiService> _logger;

        public UserStateApiService(HttpClient http, ILogger<UserStateApiService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<UserStateDto?> GetStateAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<UserStateDto>("api/userstate");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user state");
                return null;
            }
        }

        public async Task SaveStateAsync(UserStateDto state)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/userstate", state);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user state");
            }
        }
        public async Task ClearStateAsync()
        {
            try
            {
                var response = await _http.DeleteAsync("api/userstate");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing user state");
            }
        }
    }
}