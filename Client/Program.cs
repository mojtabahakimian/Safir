using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization; // Added
using Safir.Client; // Default namespace
using Safir.Client.Services; // Added for IAuthService
using Safir.Client.Auth; // Added for ApiAuthenticationStateProvider
using Blazored.LocalStorage; // Added for Local Storage
using Microsoft.AspNetCore.Components.Web;
using Safir.Shared.Interfaces;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app"); // Check if App.razor exists, or use HeadOutlet/Routes
builder.RootComponents.Add<HeadOutlet>("head::after");




// --- Register HttpClient ---
// Configure HttpClient to talk to the Server project's base address
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
// --- End HttpClient ---

#region Mine


// مسیر: Client/Program.cs
builder.Services.AddSingleton<AppState>();

// --- Add Authentication Services ---
builder.Services.AddAuthorizationCore(); // Core authorization services
// Register our custom AuthenticationStateProvider
builder.Services.AddScoped<AuthenticationStateProvider, ApiAuthenticationStateProvider>();
// Register our AuthService for handling login/logout logic
builder.Services.AddScoped<IAuthService, AuthService>();
// --- End Authentication Services ---

builder.Services.AddMudServices();

builder.Services.AddScoped<ThemeService>();

builder.Services.AddScoped<CustomerApi>();

builder.Services.AddScoped<LookupApiService>();

builder.Services.AddScoped<VisitorApiService>();
#endregion


// --- Add Blazored.LocalStorage ---
builder.Services.AddBlazoredLocalStorage();
// --- End Blazored.LocalStorage ---



await builder.Build().RunAsync();