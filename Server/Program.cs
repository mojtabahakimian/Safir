using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using Safir.Server.Services;
using Safir.Shared.Interfaces;
using Stimulsoft.Base;
using Stimulsoft.Report;
using System.Text;
using System.Text.RegularExpressions;

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

// =================================================================
#region Database Migration (Run Startup Scripts)

// ما یک "scope" جدید می‌سازیم تا بتوانیم به سرویس‌های Scoped مانند IDatabaseService دسترسی پیدا کنیم
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var dbService = services.GetRequiredService<IDatabaseService>();
        var env = services.GetRequiredService<IWebHostEnvironment>();

        // 1. آدرس فایل های اسکریپت را پیدا کنید
        string[] scriptsToRun = new string[]
        {
            "001_CreateEvaporationReportsTable.sql",
            "002_CreateBugReportsTable.sql",
            "003_AlterBugReportsAddMissingColumns.sql",
            "004_AlterBugReportsAddUserNote.sql",
            //"005_CreateBugReportCommentsTable.sql",
            //"006_Pay2_HourlyCalcBasis.sql",
            //"007_Pay2_HourlyLeaveType.sql",
            //"008_DecreeLineAmountDecimal.sql",
        };

        foreach (var scriptName in scriptsToRun)
        {
            string scriptPath = Path.Combine(env.ContentRootPath, "Scripts", scriptName);

            if (File.Exists(scriptPath))
            {
                logger.LogInformation("Migration Script found: {ScriptPath}", scriptPath);

                // 2. محتوای اسکریپت را بخوانید
                string sqlScript = await File.ReadAllTextAsync(scriptPath);

                // 3. مهم: اسکریپت را بر اساس دستور "GO" جدا کنید
                var scriptBatches = Regex.Split(sqlScript, @"^\s*GO\s*$",
                                            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

                // 4. هر بخش (batch) را جداگانه اجرا کنید
                foreach (var batch in scriptBatches)
                {
                    if (string.IsNullOrWhiteSpace(batch)) continue;

                    await dbService.DoExecuteSQLAsync(batch);
                    logger.LogInformation("Successfully executed migration batch.");
                }

                logger.LogInformation("Database migration script '{ScriptName}' executed successfully.", scriptName);
            }
            else
            {
                logger.LogWarning("Database migration script not found at: {ScriptPath}", scriptPath);
            }
        }
    }
    catch (Exception ex)
    {
        //var logger = services.GetRequiredService<ILogger<Program>>();
        //logger.LogCritical(ex, "Failed to execute database migration script at startup. The application will stop.");
        //// اگر اسکریپت دیتابیس اجرا نشود، برنامه نباید بالا بیاید
        //throw;
    }
}
#endregion
// =================================================================


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