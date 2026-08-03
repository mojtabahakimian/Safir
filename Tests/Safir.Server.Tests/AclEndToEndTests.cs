using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// آزمون سرتاسری کنترل دسترسی از روی HTTP واقعی.
///
/// کل خط لوله‌ی ASP.NET اجرا می‌شود: مسیریابی، بررسی توکن،
/// Pay2AuthorizeAttribute، Pay2AccessService، و خود کنترلرها — همگی
/// کد تولید. تنها چیزی که جایگزین شده موتور SQL است، چون در این محیط
/// SQL Server قابل نصب نیست.
///
/// محتوای «دیتابیس» همان چیزی است که
/// Server/Database/test_auth_and_acl_users.sql روی دیتابیس واقعی می‌سازد.
/// </summary>
public class AclEndToEndTests : IClassFixture<AclEndToEndTests.Factory>
{
    private const int AdminCo = 9001, ViewerCo = 9002, ScopedCo = 9003, AclAdminCo = 9004;

    private static readonly string[] AllForms =
    {
        Pay2Forms.Dashboard, Pay2Forms.Workshop, Pay2Forms.Employee,
        Pay2Forms.Attendance, Pay2Forms.Run, Pay2Forms.ItemDef, Pay2Forms.Settings,
    };

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
            // payadmin — همه‌ی مجوزها روی همه‌ی فرم‌ها، همه‌ی کارگاه‌ها
            Db.UserForms[AdminCo] = AllForms
                .Select(f => new InMemoryDatabase.FormPerm(f, f, true, true, true, true, true))
                .ToList();
            Db.UserWorkshops[AdminCo] = new List<int> { 1, 2, 3 };

            // payviewer — فقط باز کردن و دیدن
            Db.UserForms[ViewerCo] = AllForms
                .Select(f => new InMemoryDatabase.FormPerm(f, f, true, true, false, false, false))
                .ToList();
            Db.UserWorkshops[ViewerCo] = new List<int> { 1, 2, 3 };

            // payscoped — دسترسی کامل ولی فقط کارگاه ۱
            Db.UserForms[ScopedCo] = AllForms
                .Select(f => new InMemoryDatabase.FormPerm(f, f, true, true, true, true, true))
                .ToList();
            Db.UserWorkshops[ScopedCo] = new List<int> { 1 };

            // مدیرِ دسترسی‌ها که هنوز هیچ کارگاهی به خودش نداده — همان وضعیتی
            // که بعد از اجرای مهاجرت روی یک دیتابیس واقعی پیش آمد و باعث شد
            // صفحه‌ی «کارگاه‌های مجاز» خالی بماند و کار به بن‌بست بخورد.
            Db.UserForms[AclAdminCo] = new List<InMemoryDatabase.FormPerm>
            {
                new(Pay2Forms.AdminAcl, "مدیریت دسترسی‌ها", true, true, true, true, true),
                new(Pay2Forms.Workshop, "کارگاه‌ها",        true, true, true, true, true),
            };
            Db.UserWorkshops[AclAdminCo] = new List<int>();
        }
    }

    private readonly Factory _factory;
    public AclEndToEndTests(Factory factory) => _factory = factory;

    /// <summary>یک کلاینت با توکن معتبر برای کاربر داده‌شده.</summary>
    private HttpClient ClientFor(int userCo)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.For(userCo));
        return client;
    }

    // ── بدون توکن ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/pay2/access/me")]
    [InlineData("/api/pay2/workshops")]
    [InlineData("/api/pay2/itemdefs")]
    [InlineData("/api/pay2/employees/me/leave-statement?year=1405")]
    [InlineData("/api/pay2/employees/me/leave-statement/pdf?year=1405")]
    public async Task Anonymous_requests_are_rejected(string path)
    {
        var res = await _factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── دسترسی‌هایی که به کلاینت اعلام می‌شود ────────────────────────────

    [Fact]
    public async Task Admin_is_told_they_have_every_permission()
    {
        var me = await ClientFor(AdminCo).GetFromJsonAsync<JsonElement>("/api/pay2/access/me");

        Assert.True(me.GetProperty("aclEnforced").GetBoolean());
        var forms = me.GetProperty("forms").EnumerateArray().ToList();
        Assert.NotEmpty(forms);
        foreach (var f in forms)
        {
            foreach (var perm in new[] { "run", "see", "inp", "upd", "del" })
                Assert.True(f.GetProperty(perm).GetBoolean(),
                    $"مدیر باید مجوز {perm} روی {f.GetProperty("formName")} داشته باشد");
        }
    }

    [Fact]
    public async Task Viewer_is_told_they_may_read_but_not_change()
    {
        var me = await ClientFor(ViewerCo).GetFromJsonAsync<JsonElement>("/api/pay2/access/me");

        foreach (var f in me.GetProperty("forms").EnumerateArray())
        {
            Assert.True(f.GetProperty("see").GetBoolean());
            Assert.False(f.GetProperty("inp").GetBoolean());
            Assert.False(f.GetProperty("upd").GetBoolean());
            Assert.False(f.GetProperty("del").GetBoolean());
        }
    }

    [Fact]
    public async Task Scoped_user_is_told_about_only_their_workshop()
    {
        var me = await ClientFor(ScopedCo).GetFromJsonAsync<JsonElement>("/api/pay2/access/me");

        Assert.True(me.GetProperty("wsScopeEnforced").GetBoolean());
        Assert.Equal(new[] { 1 },
            me.GetProperty("allowedWorkshopIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
    }

    /// <summary>
    /// یک درخواست معتبر ساخت کارگاه. باید از اعتبارسنجی رد شود تا واقعاً
    /// به لایه‌ی کنترل دسترسی برسد — وگرنه تست به‌جای ۴۰۳ عدد ۴۰۰ می‌گیرد
    /// و چیزی را ثابت نمی‌کند.
    /// </summary>
    private static object ValidWorkshop(string code) => new
    {
        Workshop = new { WS_ID = 0, WS_CODE = code, WS_NAME = "کارگاه آزمایشی" },
        Accounts = new { },
    };

    // ── اعمال واقعی روی نوشتن — مهم‌ترین بخش ─────────────────────────────

    [Fact]
    public async Task Viewer_calling_the_write_API_directly_gets_403()
    {
        // کسی که دکمه را نمی‌بیند، اگر مستقیم API را صدا بزند هم باید رد شود.
        // اگر این تست بشکند یعنی کنترل دسترسی فقط آرایشی بوده است.
        var res = await ClientFor(ViewerCo).PostAsJsonAsync("/api/pay2/workshops/save", ValidWorkshop("991"));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_delete()
    {
        var res = await ClientFor(ViewerCo).DeleteAsync("/api/pay2/itemdefs/1");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task The_403_body_explains_what_was_missing_in_Persian()
    {
        var res = await ClientFor(ViewerCo).DeleteAsync("/api/pay2/itemdefs/1");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Contains("دسترسی", body);
        Assert.Contains(Pay2Forms.ItemDef, body);
    }

    [Fact]
    public async Task Admin_is_not_blocked_by_the_authorization_layer()
    {
        // فیلتر باید اجازه بدهد رد شود. بعد از آن کنترلر سراغ دیتابیس می‌رود
        // و چون این کوئری در دیتابیس تست پیاده‌سازی نشده خطا می‌دهد —
        // ولی مهم این است که ۴۰۳ نگیریم.
        var res = await ClientFor(AdminCo).DeleteAsync("/api/pay2/itemdefs/1");

        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Validation_runs_before_the_permission_check_on_workshops_save()
    {
        // این اکشن مجوز را داخل بدنه بررسی می‌کند، نه با اتریبیوت، و
        // اعتبارسنجی جلوتر از آن اجرا می‌شود. پس کاربر بدون دسترسی با
        // ورودی نامعتبر ۴۰۰ می‌گیرد نه ۴۰۳ — یعنی می‌تواند قواعد
        // اعتبارسنجی فرم را بدون داشتن مجوز کشف کند.
        // چیزی نوشته نمی‌شود، پس رخنه‌ی امنیتی نیست؛ ولی رفتار مستندی است
        // که اگر عوض شد باید عمدی باشد.
        var res = await ClientFor(ViewerCo).PostAsJsonAsync("/api/pay2/workshops/save",
            new { Workshop = new { WS_ID = 0, WS_CODE = "NOT-A-NUMBER", WS_NAME = "x" }, Accounts = new { } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── ثبت وقایع امنیتی ─────────────────────────────────────────────────

    [Fact]
    public async Task A_denied_attempt_is_written_to_the_audit_table()
    {
        int before = _factory.Db.AuditWrites.Count;

        await ClientFor(ViewerCo).PostAsJsonAsync("/api/pay2/workshops/save", ValidWorkshop("992"));

        Assert.True(_factory.Db.AuditWrites.Count > before,
            "تلاش ناموفق باید در PAY2_SEC_AUDIT ثبت شود");
    }

    // ── صفحه‌ی مدیریت دسترسی نباید خودش را قفل کند ───────────────────────

    [Fact]
    public async Task An_acl_admin_with_no_workshops_of_their_own_still_sees_every_workshop()
    {
        // بن‌بستِ واقعی: مهاجرت فقط به چند کاربر خاص کارگاه می‌دهد. اگر مدیرِ
        // دسترسی‌ها جزو آن‌ها نباشد و فهرست کارگاه‌ها هم به محدوده‌ی خودش
        // محدود شود، فهرست خالی است و او هرگز نمی‌تواند به کسی — از جمله
        // خودش — کارگاه بدهد. یعنی روشن کردن کنترل دسترسی سیستم را قفل می‌کند.
        var res = await ClientFor(AclAdminCo)
            .GetFromJsonAsync<JsonElement>("/api/pay2/access/workshops");

        Assert.Equal(JsonValueKind.Array, res.ValueKind);
        Assert.NotEmpty(res.EnumerateArray());
    }

    [Fact]
    public async Task The_ordinary_workshop_list_stays_scoped_for_that_same_admin()
    {
        // اگر این هم همه‌ی کارگاه‌ها را برگرداند یعنی محدودسازی از بین رفته و
        // آزمون بالا هیچ چیزِ تازه‌ای ثابت نمی‌کند.
        var res = await ClientFor(AclAdminCo)
            .GetFromJsonAsync<JsonElement>("/api/pay2/workshops");

        Assert.Empty(res.EnumerateArray());
    }

    [Fact]
    public async Task Listing_every_workshop_needs_the_acl_admin_permission()
    {
        var res = await ClientFor(ScopedCo).GetAsync("/api/pay2/access/workshops");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ── کلید خاموش‌کننده ─────────────────────────────────────────────────

    [Fact]
    public async Task Turning_ACL_off_restores_the_old_behaviour()
    {
        // ACL_ENFORCE = 0 حالت پیش‌فرض نصب روی مشتری است. در آن حالت
        // هیچ‌کس نباید ناگهان دسترسی‌اش قطع شود.
        var factory = new Factory();
        factory.Db.Config["ACL_ENFORCE"] = "0";

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.For(ViewerCo));

        var res = await client.PostAsJsonAsync("/api/pay2/workshops/save", ValidWorkshop("993"));

        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(s => s.ServiceType == typeof(T)).ToList())
            services.Remove(d);
    }
}
