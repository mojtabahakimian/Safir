using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using Safir.Server.Services;
using Safir.Shared.Interfaces;
using Stimulsoft.Base;
using Stimulsoft.Report;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<Safir.Server.Security.Pay2ForbiddenFilter>();
});
builder.Services.AddRazorPages();

#region MineServer
builder.Services.AddMemoryCache();

// --- Add Custom Services ---
builder.Services.AddScoped<IConnectionStringProvider, ConnectionStringProvider>();
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

// Register IUserService and its implementation UserService
// Use Scoped lifetime: a new instance per HTTP request
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserStateService, UserStateService>();
builder.Services.AddScoped<ISmsService, Safir.Server.Services.SmsService>();
builder.Services.AddHttpClient();

// --- End Custom Services ---
builder.Services.AddScoped<IAppSettingsService, AppSettingsService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services
    .AddOptions<SmtpSettings>()
    .BindConfiguration("EmailSettings")
    .ValidateDataAnnotations()
    .ValidateOnStart();


// --- Add JWT Authentication ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]
             ?? throw new InvalidOperationException("JWT Key not configured")))
    };

    // SignalR (CostCloseHub) نمی‌تواند روی WebSocket هدر Authorization بگذارد؛
    // مرورگر روی upgrade request اجازه هدر سفارشی نمی‌دهد. راه‌حل استاندارد
    // ASP.NET Core: کلاینت توکن را در query string به‌عنوان access_token
    // می‌فرستد (HubConnectionBuilder با AccessTokenProvider این کار را خودش
    // انجام می‌دهد)، و اینجا همان مسیرهای /hubs/ را از query string می‌خوانیم.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});
// --- End JWT Authentication ---

// Ensure Encoding provider is registered if needed by decoding logic globally
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IPay2AccessService, Pay2AccessService>();
builder.Services.AddScoped<ICrmAccessService, CrmAccessService>();
builder.Services.AddScoped<Safir.Server.Security.Pay2ScopeResolver>();
builder.Services.AddScoped<Safir.Server.Services.Pay2DisketteService>();

// --- ماژول بستن ماه بهای تمام‌شده (Cost Close) ---
// ── دستیار هوش مصنوعی ──
// ابزارها Scoped ثبت می‌شوند چون IDatabaseService هم Scoped است و رشته‌ی
// اتصال از هدرِ همین درخواست می‌آید؛ Singleton یعنی همه‌ی کاربران به
// پایگاهِ اولین درخواست وصل می‌شدند.
builder.Services.AddScoped<Safir.Server.Ai.IAiAccessService, Safir.Server.Ai.AiAccessService>();
builder.Services.AddScoped<Safir.Server.Ai.IAiToolRegistry, Safir.Server.Ai.AiToolRegistry>();
builder.Services.AddScoped<Safir.Server.Ai.IAiTool, Safir.Server.Ai.ListRunsTool>();
builder.Services.AddScoped<Safir.Server.Ai.IAiTool, Safir.Server.Ai.SearchItemTool>();
builder.Services.AddScoped<Safir.Server.Ai.IAiTool, Safir.Server.Ai.ItemMarginTool>();
builder.Services.AddScoped<Safir.Server.Ai.IAiTool, Safir.Server.Ai.UnitSummaryTool>();
builder.Services.AddScoped<Safir.Server.Ai.IAiTool, Safir.Server.Ai.ExceptionsTool>();
builder.Services.AddScoped<Safir.Server.Ai.IAiTool, Safir.Server.Ai.DescribeDataTool>();

// ── ارائه‌دهنده‌ی مدل ──
// انتخاب با تنظیمات است نه با کد، تا رفتن از Ollama محلی به سرویس ابری
// فقط عوض کردن appsettings باشد. کلید هرگز اینجا نیست — فقط نامِ متغیر
// محیطی‌اش، چون appsettings در گیت است.
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection("Ai").Get<Safir.Server.Ai.AiOptions>()
    ?? new Safir.Server.Ai.AiOptions());

builder.Services.AddHttpClient<Safir.Server.Ai.OpenAiCompatibleProvider>();
builder.Services.AddHttpClient<Safir.Server.Ai.AnthropicProvider>();

builder.Services.AddScoped<Safir.Server.Ai.IAiChatProvider>(sp =>
{
    var opt = sp.GetRequiredService<Safir.Server.Ai.AiOptions>();
    return string.Equals(opt.Provider, "anthropic", StringComparison.OrdinalIgnoreCase)
         ? sp.GetRequiredService<Safir.Server.Ai.AnthropicProvider>()
         : sp.GetRequiredService<Safir.Server.Ai.OpenAiCompatibleProvider>();
});

builder.Services.AddScoped<Safir.Server.Ai.IAiConversationService,
                           Safir.Server.Ai.AiConversationService>();

builder.Services.AddSingleton<Safir.Server.CostClose.CostCloseQueue>();
builder.Services.AddSingleton<Safir.Server.CostClose.ICostCloseQueue>(
    sp => sp.GetRequiredService<Safir.Server.CostClose.CostCloseQueue>());
builder.Services.AddHostedService<Safir.Server.CostClose.CostCloseWorker>();

builder.Services.AddSingleton<Safir.Server.CostClose.IDatabaseServiceFactory,
                               Safir.Server.CostClose.DatabaseServiceFactory>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostCloseNotifier,
                            Safir.Server.CostClose.CostCloseNotifier>();
builder.Services.AddScoped<Safir.Server.CostClose.CloseOrchestrator>();

// گام‌ها — با افزودن گام جدید فقط یک خط اینجا اضافه می‌شود
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S00_Preflight>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S02_Snapshot>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S03_DeleteEmptyDeeds>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S04_SortDeeds>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S05_Gate>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S07_RebuildIssue>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S07B_SyncLaborRate>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S07A_RebuildAverageRate>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S08_CalcVariance>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S09_ApplyDecisions>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S10_BalanceConversion>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S11_PropagateRates>();
builder.Services.AddScoped<Safir.Server.CostClose.ICostStep, Safir.Server.CostClose.Steps.S12_CalcMargin>();

builder.Services.AddScoped<Safir.Server.CostClose.IBoardReportBuilder, Safir.Server.CostClose.BoardReportBuilder>();

builder.Services.AddSignalR();
// --- پایان ماژول بستن ماه بهای تمام‌شده ---
#endregion


var app = builder.Build();

#region Mine - Font Registration
// --- رجیستر کردن فونت فارسی برای QuestPDF ---
try
{
    var env = app.Services.GetRequiredService<IWebHostEnvironment>();
    // <<< --- مسیر و نام فایل فونت اصلاح شد --- >>>
    // نام فایل روی Linux به حروف کوچک/بزرگ حساس است.
    string fontPath = Path.Combine(env.ContentRootPath, "Fonts", "IRANYekanFN.TTF");

    if (File.Exists(fontPath))
    {
        QuestPDF.Settings.License = LicenseType.Community;
        FontManager.RegisterFont(File.OpenRead(fontPath));
        app.Logger.LogInformation("فونت QuestPDF با موفقیت از مسیر {FontPath} رجیستر شد.", fontPath);


        StiFontCollection.AddFontFile(fontPath);

        // <<< --- حذف بررسی FontManager.FontFamilies --- >>>
    }
    else
    {
        app.Logger.LogError("فایل فونت QuestPDF در مسیر مورد انتظار یافت نشد: {FontPath}. از کپی شدن فایل به پوشه خروجی اطمینان حاصل کنید.", fontPath);
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "خطا در رجیستر کردن فونت QuestPDF هنگام شروع برنامه.");
}
#endregion

// Database upgrades are handled exclusively by the separate updater. : ScriptSqly.cs

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var acceptsHtml = context.Request.Headers.Accept
        .ToString()
        .Contains("text/html", StringComparison.OrdinalIgnoreCase);
    var isUpdateMetadata = path.Equals("/service-worker.js", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/service-worker-assets.js", StringComparison.OrdinalIgnoreCase);

    if (isUpdateMetadata || (HttpMethods.IsGet(context.Request.Method) && acceptsHtml))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    await next();
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

// --- Add Authentication/Authorization Middleware ---
// IMPORTANT: Place these AFTER UseRouting and BEFORE UseEndpoints/MapControllers
app.UseAuthentication(); // Checks for valid tokens
app.UseAuthorization(); // Enforces authorization policies ([Authorize] attribute)
// --- End Authentication/Authorization Middleware ---

// --- BugReporter Global Backend Security Middleware ---
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var isBugReporter = user.FindFirst(Safir.Shared.Constants.BaseknowClaimTypes.GRSAL)?.Value == "999";
            if (isBugReporter)
            {
                // Only allow specific APIs for the Bug Reporter role
                var path = context.Request.Path.Value?.ToLowerInvariant();
                bool isAllowedApi = path != null && (
                    path.StartsWith("/api/bugreport") ||
                    path.StartsWith("/api/auth") ||
                    path.StartsWith("/api/userstate") // Add any other generic APIs needed
                );

                if (!isAllowedApi)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("You do not have permission to access this resource.");
                    return; // Short-circuit the pipeline
                }
            }
        }
    }
    await next();
});
// --- End Backend Security Middleware ---

app.MapRazorPages();
app.MapControllers(); // Make sure API controllers are mapped
app.MapHub<Safir.Server.CostClose.CostCloseHub>("/hubs/cost-close");

app.MapFallbackToFile("index.html"); // Fallback for Blazor routing

app.Run();

// برای تست‌های یکپارچه (WebApplicationFactory) لازم است کلاس Program
// از بیرون قابل دسترسی باشد. با top-level statements این کلاس به‌صورت
// internal ساخته می‌شود، پس اینجا صریحاً public اعلامش می‌کنیم.
// هیچ اثری روی اجرای برنامه ندارد.
public partial class Program { }
