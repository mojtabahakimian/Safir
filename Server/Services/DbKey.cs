using System.Data.SqlClient;
using System.Net;

namespace Safir.Server.Services;

/// <summary>
/// شناسه‌ی پایدار دیتابیسِ یک رشته‌ی اتصال: «سرور|دیتابیس» با حروف کوچک.
///
/// یک سرور Safir هم‌زمان به چند دیتابیس (چند شرکت یا چند سال مالی) سرویس می‌دهد و دیتابیس
/// هر درخواست از هدر X-DB-Connection می‌آید. هر چیزی که بین درخواست‌ها نگه داشته می‌شود
/// (کش، صف، گروه SignalR، توکن ورود) باید این شناسه را در کلیدش داشته باشد، وگرنه
/// داده یا دسترسیِ یک شرکت برای شرکت دیگر برگردانده می‌شود.
/// </summary>
public static class DbKey
{
    public static string From(string connectionString)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            return $"{Server(b.DataSource)}|{b.InitialCatalog.Trim()}".ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// یک سرور را با چند نوشتار می‌شود صدا زد («.\SQL2022» روی خود سرور، «MERCEDES\SQL2022» از
    /// شبکه). اگر کلید فرق کند، قفلِ «این اجرا در حال انجام است» دو کاربر روی همان دیتابیس را
    /// دو دیتابیس می‌بیند و بازسازی سند هم‌زمان دو بار اجرا می‌شود. پس نوشتارهای رایجِ یک ماشین
    /// یکی می‌شوند؛ «.» و localhost یعنی همین ماشینی که Safir رویش است.
    /// </summary>
    private static string Server(string dataSource)
    {
        var s = dataSource.Trim();
        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) s = s[4..];

        var port = string.Empty;
        var comma = s.IndexOf(',');
        if (comma >= 0)
        {
            port = s[comma..].Replace(" ", "");
            s = s[..comma];
            if (port == ",1433") port = string.Empty;
        }

        var slash = s.IndexOf('\\');
        var host = (slash >= 0 ? s[..slash] : s).Trim();
        var instance = slash >= 0 ? s[slash..].Trim() : string.Empty;

        if (host.ToLowerInvariant() is "." or "(local)" or "localhost" or "127.0.0.1" or "::1")
            host = Environment.MachineName;
        else if (!IPAddress.TryParse(host, out _))
            host = host.Split('.')[0];   // mercedes.corp.local → mercedes

        return host + instance + port;
    }

    public static string DatabaseKey(this IConnectionStringProvider provider) =>
        From(provider.GetConnectionString());
}
