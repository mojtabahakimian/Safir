using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization; // Added
using Safir.Client; // Default namespace
using Safir.Client.Services; // Added for IAuthService
using Safir.Client.Auth; // Added for ApiAuthenticationStateProvider
using Blazored.LocalStorage; // Added for Local Storage
using Microsoft.AspNetCore.Components.Web;
using Safir.Shared.Interfaces;
using MudBlazor.Services;
using System.Globalization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app"); // Check if App.razor exists, or use HeadOutlet/Routes
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped<IAutomationApiService, AutomationApiService>(); // Register interface and implementation
builder.Services.AddScoped<LookupApiService>();


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

// Add this line within the builder.Services configuration section
builder.Services.AddScoped<ItemGroupApiService>();

builder.Services.AddScoped<ShoppingCartService>();

builder.Services.AddScoped<ProformaApiService>();

// --- ثبت سرویس تنظیمات کلاینت ---
builder.Services.AddScoped<ClientAppSettingsService>(); // Scoped مناسب است

builder.Services.AddScoped<PermissionApiService>();

builder.Services.AddScoped<ConnectivityService>(); // <--- این خط را اضافه کنید

builder.Services.AddScoped<ReportApiService>();
#endregion


// --- Add Blazored.LocalStorage ---
builder.Services.AddBlazoredLocalStorage();
// --- End Blazored.LocalStorage ---

#if DEBUG
Console.WriteLine("ایجاد تاخیر عمدی برای تست لودینگ...");
//await Task.Delay(5000); // 5000 میلی‌ثانیه = 5 ثانیه تاخیر
Console.WriteLine("پایان تاخیر.");
#endif

await builder.Build().RunAsync();