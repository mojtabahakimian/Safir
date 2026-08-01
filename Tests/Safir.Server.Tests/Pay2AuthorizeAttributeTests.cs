using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Safir.Server.Security;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// آزمون فیلتر واقعی [Pay2Authorize] — همان کدی که در تولید اجرا می‌شود.
/// بدون دیتابیس و بدون وب‌سرور.
/// </summary>
public class Pay2AuthorizeAttributeTests
{
    private const string Form = Pay2Forms.Employee;

    private static AuthorizationFilterContext MakeContext(
        IPay2AccessService access, ClaimsPrincipal user, string method = "POST")
    {
        var services = new ServiceCollection();
        services.AddSingleton(access);

        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = services.BuildServiceProvider()
        };
        http.Request.Method = method;
        http.Request.Path = "/api/pay2/employees/save";

        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal LoggedIn(string idd = "9001", string name = "payadmin") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(BaseknowClaimTypes.IDD, idd),
            new Claim(BaseknowClaimTypes.UUSER, name),
            new Claim(ClaimTypes.NameIdentifier, idd),
        }, authenticationType: "TestAuth"));

    // ── کاربر وارد نشده ──────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_user_is_rejected_with_401()
    {
        var access = FakePay2AccessService.FullAccess(Form);
        var ctx = MakeContext(access, Anonymous());

        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Inp).OnAuthorizationAsync(ctx);

        Assert.IsType<UnauthorizedObjectResult>(ctx.Result);
    }

    [Fact]
    public async Task Anonymous_user_never_reaches_the_access_service()
    {
        var access = FakePay2AccessService.FullAccess(Form);
        var ctx = MakeContext(access, Anonymous());

        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Inp).OnAuthorizationAsync(ctx);

        Assert.Empty(access.AuditLog);
    }

    [Fact]
    public async Task Token_without_a_usable_user_id_is_rejected()
    {
        var broken = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(BaseknowClaimTypes.IDD, "not-a-number") }, "TestAuth"));
        var ctx = MakeContext(FakePay2AccessService.FullAccess(Form), broken);

        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Inp).OnAuthorizationAsync(ctx);

        Assert.IsType<UnauthorizedObjectResult>(ctx.Result);
    }

    // ── مجوز کافی / ناکافی ──────────────────────────────────────────────

    [Theory]
    [InlineData(Pay2Perm.Run)]
    [InlineData(Pay2Perm.See)]
    [InlineData(Pay2Perm.Inp)]
    [InlineData(Pay2Perm.Upd)]
    [InlineData(Pay2Perm.Del)]
    public async Task User_with_every_permission_passes(Pay2Perm perm)
    {
        var ctx = MakeContext(FakePay2AccessService.FullAccess(Form), LoggedIn());

        await new Pay2AuthorizeAttribute(Form, perm).OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);   // null یعنی اجازه بده ادامه دهد
    }

    [Theory]
    [InlineData(Pay2Perm.Inp)]
    [InlineData(Pay2Perm.Upd)]
    [InlineData(Pay2Perm.Del)]
    public async Task View_only_user_is_blocked_from_changing_anything(Pay2Perm perm)
    {
        var ctx = MakeContext(FakePay2AccessService.ViewOnly(Form), LoggedIn("9002", "payviewer"));

        await new Pay2AuthorizeAttribute(Form, perm).OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData(Pay2Perm.Run)]
    [InlineData(Pay2Perm.See)]
    public async Task View_only_user_can_still_open_and_read(Pay2Perm perm)
    {
        var ctx = MakeContext(FakePay2AccessService.ViewOnly(Form), LoggedIn("9002", "payviewer"));

        await new Pay2AuthorizeAttribute(Form, perm).OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task Permission_on_a_different_form_does_not_leak()
    {
        // کاربر روی فرم «پرسنل» دسترسی کامل دارد ولی روی «تنظیمات» هیچ.
        var ctx = MakeContext(FakePay2AccessService.FullAccess(Pay2Forms.Employee), LoggedIn());

        await new Pay2AuthorizeAttribute(Pay2Forms.Settings, Pay2Perm.Upd).OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Denial_message_is_in_Persian_and_names_the_form()
    {
        var ctx = MakeContext(FakePay2AccessService.ViewOnly(Form), LoggedIn("9002"));

        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Del).OnAuthorizationAsync(ctx);

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        var msg = Assert.IsType<string>(result.Value);
        Assert.Contains("دسترسی", msg);
        Assert.Contains(Form, msg);
        Assert.Contains(nameof(Pay2Perm.Del), msg);
    }

    // ── کلید خاموش‌کننده ─────────────────────────────────────────────────

    [Fact]
    public async Task When_ACL_is_disabled_everything_is_allowed()
    {
        // ACL_ENFORCE = 0 حالت پیش‌فرض نصب است؛ نباید چیزی قطع شود.
        var ctx = MakeContext(FakePay2AccessService.Disabled(), LoggedIn("5", "legacy"));

        await new Pay2AuthorizeAttribute(Pay2Forms.Settings, Pay2Perm.Del).OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    // ── ثبت وقایع ────────────────────────────────────────────────────────

    [Fact]
    public async Task Both_allowed_and_denied_attempts_are_audited()
    {
        var allowed = FakePay2AccessService.FullAccess(Form);
        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Del)
            .OnAuthorizationAsync(MakeContext(allowed, LoggedIn()));

        var denied = FakePay2AccessService.ViewOnly(Form);
        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Del)
            .OnAuthorizationAsync(MakeContext(denied, LoggedIn("9002", "payviewer")));

        Assert.True(Assert.Single(allowed.AuditLog).Allowed);
        Assert.False(Assert.Single(denied.AuditLog).Allowed);
    }

    [Fact]
    public async Task Audit_entry_carries_enough_context_to_investigate()
    {
        var access = FakePay2AccessService.ViewOnly(Form);
        var ctx = MakeContext(access, LoggedIn("9002", "payviewer"), method: "DELETE");

        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Del).OnAuthorizationAsync(ctx);

        var entry = Assert.Single(access.AuditLog);
        Assert.Equal(9002, entry.UserCo);
        Assert.Equal("payviewer", entry.UserName);
        Assert.Equal(Form, entry.FormName);
        Assert.Equal(nameof(Pay2Perm.Del), entry.PermFlag);
        Assert.Equal("DELETE", entry.HttpMethod);
        Assert.Equal("/api/pay2/employees/save", entry.Path);
    }

    // ── شناسه کاربر ──────────────────────────────────────────────────────

    [Fact]
    public async Task NameIdentifier_is_accepted_when_the_IDD_claim_is_absent()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "9001") }, "TestAuth"));
        var access = FakePay2AccessService.FullAccess(Form);

        await new Pay2AuthorizeAttribute(Form, Pay2Perm.Inp)
            .OnAuthorizationAsync(MakeContext(access, user));

        Assert.Equal(9001, Assert.Single(access.AuditLog).UserCo);
    }
}
