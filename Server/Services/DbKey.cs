using System.Data.SqlClient;

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
            return $"{b.DataSource.Trim()}|{b.InitialCatalog.Trim()}".ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string DatabaseKey(this IConnectionStringProvider provider) =>
        From(provider.GetConnectionString());
}
