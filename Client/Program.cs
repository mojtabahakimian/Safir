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
using Syncfusion.Blazor;

// ───────────────────────────────────────────────────────────────────────────
// لایسنس Syncfusion: برای حذف پیام «trial/unlicensed»، کلید لایسنس معتبرِ نسخهٔ 29
// را این‌جا (قبل از ساختِ host و استفاده از کامپوننت‌ها) ثبت کنید.
// نمونه:
// Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("YOUR-SYNCFUSION-V29-LICENSE-KEY");
// ───────────────────────────────────────────────────────────────────────────

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

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.TopCenter;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4500;
    config.SnackbarConfiguration.ShowTransitionDuration = 180;
    config.SnackbarConfiguration.HideTransitionDuration = 150;
    config.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Filled;
});

builder.Services.AddScoped<ThemeService>();

builder.Services.AddScoped<CustomerApi>();

builder.Services.AddScoped<LookupApiService>();

builder.Services.AddScoped<VisitorApiService>();

// Add this line within the builder.Services configuration section
builder.Services.AddScoped<ItemGroupApiService>();

builder.Services.AddScoped<ShoppingCartService>();
builder.Services.AddScoped<UserStateApiService>();
builder.Services.AddScoped<ProformaApiService>();

// --- ثبت سرویس تنظیمات کلاینت ---
builder.Services.AddScoped<ClientAppSettingsService>(); // Scoped مناسب است

builder.Services.AddScoped<PermissionApiService>();

builder.Services.AddScoped<ConnectivityService>();

builder.Services.AddScoped<ReportApiService>();

builder.Services.AddScoped<Pay2WorkshopApiService>();

builder.Services.AddScoped<Pay2EmployeeApiService>();

builder.Services.AddScoped<Pay2AttendanceApiService>();

builder.Services.AddScoped<Pay2AdvanceApiService>();

builder.Services.AddScoped<Pay2SettingsApiService>();

builder.Services.AddScoped<Pay2ItemDefApiService>();

builder.Services.AddScoped<BugReportApiService>();

builder.Services.AddScoped<Pay2RunApiService>();

builder.Services.AddScoped<Pay2DashboardApiService>();

builder.Services.AddScoped<IProductionReportApiService, ProductionReportApiService>();

builder.Services.AddSyncfusionBlazor();

// محلی‌سازِ فارسیِ کامپوننت‌های Syncfusion (برچسب‌های فیلترِ گرید و ...)
builder.Services.AddSingleton(typeof(Syncfusion.Blazor.ISyncfusionStringLocalizer), typeof(Safir.Client.Services.SyncfusionLocalizer));
#endregion


// --- Add Blazored.LocalStorage ---
builder.Services.AddBlazoredLocalStorage();
// --- End Blazored.LocalStorage ---

builder.Services.AddScoped<ConnectionManagerService>();

#if DEBUG
Console.WriteLine("ایجاد تاخیر عمدی برای تست لودینگ...");
//await Task.Delay(5000); // 5000 میلی‌ثانیه = 5 ثانیه تاخیر
Console.WriteLine("پایان تاخیر.");
#endif

var host = builder.Build();

// Load DB connection settings from local storage and apply to HttpClient
var connectionManager = host.Services.GetRequiredService<ConnectionManagerService>();
await connectionManager.LoadSettingsAsync();

await host.RunAsync();
