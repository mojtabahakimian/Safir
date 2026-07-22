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
builder.Services.AddControllersWithViews();
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

// --- End Custom Services ---
builder.Services.AddScoped<IAppSettingsService, AppSettingsService>();


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
});
// --- End JWT Authentication ---

// Ensure Encoding provider is registered if needed by decoding logic globally
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<Safir.Server.Services.Pay2DisketteService>();
#endregion


var app = builder.Build();

#region Mine - Font Registration
// --- رجیستر کردن فونت فارسی برای QuestPDF ---
try
{
    var env = app.Services.GetRequiredService<IWebHostEnvironment>();
    // <<< --- مسیر و نام فایل فونت اصلاح شد --- >>>
    string fontPath = Path.Combine(env.ContentRootPath, "Fonts", "IRANYekanFN.ttf"); // استفاده از فونت شما

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

app.MapFallbackToFile("index.html"); // Fallback for Blazor routing

app.Run();
