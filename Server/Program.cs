using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Safir.Server.Services;
using Safir.Shared.Interfaces;
using System.Text;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

#region MineServer
// --- Add Custom Services ---
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

// Register IUserService and its implementation UserService
// Use Scoped lifetime: a new instance per HTTP request
builder.Services.AddScoped<IUserService, UserService>();
// --- End Custom Services ---
builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();

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

#endregion


var app = builder.Build();

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


app.MapRazorPages();
app.MapControllers(); // Make sure API controllers are mapped
app.MapFallbackToFile("index.html"); // Fallback for Blazor routing

app.Run();