// In Safir.Client/Auth directory (create Auth folder if needed)
using Blazored.LocalStorage; // Using Blazored.LocalStorage
using Microsoft.AspNetCore.Components.Authorization;
using System.IdentityModel.Tokens.Jwt; // For JwtSecurityTokenHandler
using System.Net.Http.Headers;
using System.Security.Claims;


namespace Safir.Client.Auth
{
    public class ApiAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage; // Using Blazored.LocalStorage
        private const string AuthTokenKey = "authToken"; // Key to store token

        // توکن هر تب جداست (TabSession)؛ دو تب می‌توانند هم‌زمان در دو شرکت وارد باشند
        private readonly Safir.Client.Services.TabSession _tab;

        public ApiAuthenticationStateProvider(HttpClient httpClient, ILocalStorageService localStorage,
                                              Safir.Client.Services.TabSession tab)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _tab = tab;
        }

        // This method is called by Blazor framework to get the initial auth state
        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var savedToken = await _tab.GetAsync<string>(AuthTokenKey);

            if (string.IsNullOrWhiteSpace(savedToken))
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())); // Not authenticated
            }

            // Check if token is expired (optional but recommended)
            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var jwtToken = tokenHandler.ReadJwtToken(savedToken);
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    Console.WriteLine("Token expired. Logging out.");
                    await _tab.RemoveAsync<string>(AuthTokenKey); // Clean up expired token
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())); // Treat as logged out
                }
            }
            catch (Exception ex) // Handle cases where token is invalid/malformed
            {
                Console.WriteLine($"Error reading token: {ex.Message}. Logging out.");
                await _tab.RemoveAsync<string>(AuthTokenKey);
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }


            // Set default auth header for HttpClient if token exists
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", savedToken);

            // Parse claims from the token
            var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(ParseClaimsFromJwt(savedToken), "jwtAuthType"));

            return new AuthenticationState(claimsPrincipal); // Authenticated
        }

        // Called by AuthService after successful login
        public void MarkUserAsAuthenticated(string token)
        {
            var authenticatedUser = new ClaimsPrincipal(new ClaimsIdentity(ParseClaimsFromJwt(token), "jwtAuthType"));
            var authState = Task.FromResult(new AuthenticationState(authenticatedUser));
            NotifyAuthenticationStateChanged(authState); // Notify Blazor framework
        }

        // Called by AuthService after logout
        public void MarkUserAsLoggedOut()
        {
            var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity()); // Empty identity
            var authState = Task.FromResult(new AuthenticationState(anonymousUser));
            _httpClient.DefaultRequestHeaders.Authorization = null; // Clear header
            NotifyAuthenticationStateChanged(authState); // Notify Blazor framework
        }

        // Helper method to parse claims from JWT string
        private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>();
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(jwt);
                claims.AddRange(token.Claims);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not parse claims from JWT: {ex.Message}");
                // Handle error appropriately, maybe return empty list or log severe error
            }

            return claims;
        }
    }
}