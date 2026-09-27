using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// یک سرور Safir هم‌زمان به چند دیتابیس (شرکت یا سال مالی) سرویس می‌دهد؛ کاربر می‌خواهد
/// یزدسپار و پودر را در دو تب هم‌زمان باز کند. کد کاربر ۱۵۲ در یک شرکت ممکن است آدم دیگری
/// در شرکت دیگر باشد، پس نه کش سرور و نه توکن ورود نباید بین دیتابیس‌ها رد و بدل شوند.
/// </summary>
public class DbIsolationTests : IClassFixture<DbIsolationTests.Factory>
{
    [Theory]
    [InlineData(@"Data Source=MERCEDES\SQL2022;Initial Catalog=YAZDSEPAR1405;Integrated Security=True", @"mercedes\sql2022|yazdsepar1405")]
    [InlineData(@"Server= DB2 ;Database= yazdsepar1405 ;User Id=sa;Password=x", "db2|yazdsepar1405")]
    [InlineData("not a connection string", "")]
    public void DbKey_is_server_and_database_lowercase(string cs, string expected) =>
        Assert.Equal(expected, DbKey.From(cs));

    private sealed class FixedDb : IConnectionStringProvider
    {
        private readonly string _cs;
        public FixedDb(string database) => _cs = $"Data Source=srv;Initial Catalog={database};Integrated Security=True";
        public string GetConnectionString() => _cs;
    }

    [Fact]
    public async Task Access_cache_of_one_database_is_not_used_for_another()
    {
        const int sameUserCode = 152;
        var sharedCache = new MemoryCache(new MemoryCacheOptions());

        var yazd = new InMemoryDatabase();
        yazd.Config["ACL_ENFORCE"] = "0";
        var poodr = new InMemoryDatabase();
        poodr.Config["ACL_ENFORCE"] = "1";

        var inYazd = await new Pay2AccessService(yazd, sharedCache, new FixedDb("YAZD")).GetAccessAsync(sameUserCode);
        var inPoodr = await new Pay2AccessService(poodr, sharedCache, new FixedDb("POODR")).GetAccessAsync(sameUserCode);

        Assert.False(inYazd.AclEnforced);
        Assert.True(inPoodr.AclEnforced);
    }

    // ── توکن ورود فقط برای دیتابیس خودش ──

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public InMemoryDatabase Db { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseService>();
                services.AddSingleton<IDatabaseService>(Db);
            });
            return base.CreateHost(builder);
        }
    }

    private const int UserCo = 9101;
    private readonly Factory _factory;
    public DbIsolationTests(Factory factory) => _factory = factory;

    private static string Header(string server, string database) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { Server = server, Database = database, IsWindowsAuthentication = true })));

    private async Task<HttpResponseMessage> CallAsync(string? tokenDb, string headerDatabase)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.For(UserCo, db: tokenDb));
        client.DefaultRequestHeaders.Add("X-DB-Connection", Header("SRV", headerDatabase));
        return await client.GetAsync("/api/pay2/access/me");
    }

    [Fact]
    public async Task Token_of_another_database_is_rejected()
    {
        var res = await CallAsync(tokenDb: "srv|yazdsepar1405", headerDatabase: "NEWPOODR1405");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.True(res.Headers.Contains("X-Safir-Db-Mismatch"));
    }

    [Fact]
    public async Task Token_of_the_same_database_is_accepted()
    {
        var res = await CallAsync(tokenDb: "srv|yazdsepar1405", headerDatabase: "YAZDSEPAR1405");
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // توکن‌هایی که قبل از این تغییر صادر شده‌اند claim دیتابیس ندارند؛ تا انقضا پذیرفته می‌شوند
    [Fact]
    public async Task Old_token_without_database_claim_still_works()
    {
        var res = await CallAsync(tokenDb: null, headerDatabase: "NEWPOODR1405");
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}

/// <summary>شماره‌ی اجرای بستن ماه در هر دیتابیس از ۱ شروع می‌شود؛ صف باید آن‌ها را از هم جدا کند.</summary>
public class CostCloseQueueDbIsolationTests
{
    private static Safir.Server.CostClose.CostCloseJob Job(string database, int runId) =>
        new(runId, $"Data Source=srv;Initial Catalog={database};Integrated Security=True", "u", null);

    [Fact]
    public void Same_run_number_in_two_databases_runs_independently()
    {
        var queue = new Safir.Server.CostClose.CostCloseQueue();

        Assert.True(queue.TryEnqueue(Job("YAZD", 7), out _));
        Assert.True(queue.TryEnqueue(Job("POODR", 7), out _));   // قبلاً: «این اجرا هم‌اکنون در حال انجام است»
        Assert.False(queue.TryEnqueue(Job("YAZD", 7), out _));   // همان اجرا در همان دیتابیس هنوز قفل است

        queue.RequestCancel("srv|yazd", 7);
        Assert.True(queue.IsCancelRequested("srv|yazd", 7));
        Assert.False(queue.IsCancelRequested("srv|poodr", 7));    // لغو یزد، اجرای پودر را متوقف نمی‌کند
    }
}
