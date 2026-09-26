using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// لیست بیمه/مالیاتِ یک Run از صفحه‌ی «مدیریت اجرای حقوق» بدون wsId صدا زده
/// می‌شود (wsId=0). سرور قبلاً کارگاهِ ۰ را بررسی می‌کرد و با روشن بودن
/// ACL_ENFORCE به همه — حتی مدیری با همه‌ی کارگاه‌ها — می‌گفت
/// «دسترسی لازم برای این کارگاه را ندارید». از آن طرف، یک wsIdِ مجازِ دلخواه
/// Runِ کارگاه دیگری را باز می‌کرد. حالا کارگاه از خودِ Run خوانده می‌شود.
/// </summary>
public class Pay2RunReportScopeTests : IClassFixture<Pay2RunReportScopeTests.Factory>
{
    private const int AdminCo = 9001, ScopedCo = 9003;
    private const int RunInWs1 = 101, RunInWs2 = 202;

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

        public Factory()
        {
            var forms = new[] { Pay2Forms.Reports, Pay2Forms.ActExport }
                .Select(f => new InMemoryDatabase.FormPerm(f, f, true, true, true, true, true))
                .ToList();

            Db.UserForms[AdminCo] = forms;
            Db.UserWorkshops[AdminCo] = new List<int> { 1, 2, 3 };
            Db.UserForms[ScopedCo] = forms;
            Db.UserWorkshops[ScopedCo] = new List<int> { 1 };

            Db.RunWorkshops[RunInWs1] = 1;
            Db.RunWorkshops[RunInWs2] = 2;
        }
    }

    private readonly Factory _factory;
    public Pay2RunReportScopeTests(Factory factory) => _factory = factory;

    private HttpClient ClientFor(int userCo)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.For(userCo));
        return client;
    }

    // بعد از گذشتن از بررسی دسترسی، کنترلر به کوئری‌ای می‌رسد که دیتابیس تست
    // پیاده نکرده و ۵۰۰ می‌دهد؛ اینجا فقط مهم است که ۴۰۳ نباشد.
    [Theory]
    [InlineData("insurance-report")]
    [InlineData("tax-report")]
    public async Task Report_opened_from_the_run_page_without_wsId_is_not_forbidden(string report)
    {
        var res = await ClientFor(AdminCo).GetAsync($"/api/pay2/run/{RunInWs1}/{report}?wsId=0");
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("insurance-report")]
    [InlineData("tax-report")]
    public async Task An_allowed_wsId_does_not_unlock_a_run_of_another_workshop(string report)
    {
        var res = await ClientFor(ScopedCo).GetAsync($"/api/pay2/run/{RunInWs2}/{report}?wsId=1");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("insurance-report")]
    [InlineData("tax-report")]
    public async Task Aggregate_report_still_checks_the_requested_workshop(string report)
    {
        var res = await ClientFor(ScopedCo).GetAsync($"/api/pay2/run/0/{report}?wsId=2");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
