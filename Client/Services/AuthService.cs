// In Safir.Client/Services directory
using Blazored.LocalStorage; // Using Blazored.LocalStorage for token storage
using Microsoft.AspNetCore.Components.Authorization; // For AuthenticationStateProvider
using Safir.Client.Auth;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.User_Model;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers; // For AuthenticationHeaderValue
using System.Net.Http.Json; // For PostAsJsonAsync, ReadFromJsonAsync
using System.Net.NetworkInformation;
using System.Security.Claims;

namespace Safir.Client.Services
{
    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        // Using Blazored.LocalStorage for easy local storage access
        private readonly ILocalStorageService _localStorage;
        private const string AuthTokenKey = "authToken"; // Key to store token in local storage

        private readonly AppState _appState;

        public AuthService(HttpClient httpClient,
                           AuthenticationStateProvider authenticationStateProvider,
                           ILocalStorageService localStorage,
                           AppState appState)
        {
            _httpClient = httpClient;
            _authenticationStateProvider = authenticationStateProvider;
            _localStorage = localStorage;
            _appState = appState;
        }

        public async Task<LoginResult> Login(LoginRequest loginRequest)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/Auth/login", loginRequest);
                var loginResult = await response.Content.ReadFromJsonAsync<LoginResult>();

                if (loginResult == null)
                {
                    return new LoginResult { Successful = false, Error = "Failed to deserialize login response." };
                }

                if (!response.IsSuccessStatusCode || !loginResult.Successful || string.IsNullOrEmpty(loginResult.Token))
                {
                    // Use error from server if available, otherwise provide generic one
                    return new LoginResult { Successful = false, Error = loginResult.Error ?? "Login failed." };
                }

                #region Mine
                var token = loginResult.Token;

                // پارس کردن توکن
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);

                var username = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                var userIdStr = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var roleStr = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

                if (!string.IsNullOrEmpty(username))
                    _appState.SetUUSER(username);

                if (int.TryParse(userIdStr, out var userId))
                    _appState.USERCOD = userId;

                if (int.TryParse(roleStr, out var roleId))
                    _appState.UGRP = roleId;
                #endregion


                // Login successful, store the token
                await _localStorage.SetItemAsync(AuthTokenKey, loginResult.Token);

                // Notify the AuthenticationStateProvider that the user has logged in
                // The cast is necessary because we know we are using our custom provider
                ((ApiAuthenticationStateProvider)_authenticationStateProvider).MarkUserAsAuthenticated(loginResult.Token);

                // Set default authorization header for subsequent requests
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", loginResult.Token);

                return loginResult;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP Request Error during login: {ex.Message}");
                return new LoginResult { Successful = false, Error = "Network error during login. Please try again." };
            }
            catch (Exception ex) // Catch other potential errors (e.g., JSON parsing)
            {
                Console.WriteLine($"Error during login: {ex.Message}");
                return new LoginResult { Successful = false, Error = "An unexpected error occurred during login." };
            }
        }

        public async Task Logout()
        {
            // Remove token from local storage
            await _localStorage.RemoveItemAsync(AuthTokenKey);

            // Notify AuthenticationStateProvider
            ((ApiAuthenticationStateProvider)_authenticationStateProvider).MarkUserAsLoggedOut();

            // Clear default authorization header
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        public async Task<string?> GetTokenAsync()
        {
            return await _localStorage.GetItemAsync<string>(AuthTokenKey);
        }
    }
}