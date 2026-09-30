using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Controllers;
using Safir.Shared.Models.Automation;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// یادآوری‌ها — جدول REMAINDER. STTIME و CTTIME در پایگاه datetime‌اند.
/// API قبلاً عددِ HHmm می‌نوشت (۹۰۰ ← «۱۹۰۲/۰۶/۲۰») و موقعِ خواندن همان
/// ستون را به int تبدیل می‌کرد، پس فهرستِ یادآوری‌ها هیچ‌وقت برنمی‌گشت.
/// </summary>
public class RemindersControllerTests
{
    [Fact]
    public void TimeOf_reads_wpf_datetime_column()
        => Assert.Equal(new TimeSpan(8, 30, 0), RemindersController.TimeOf(new DateTime(1899, 12, 30, 8, 30, 20)));

    [Fact]
    public void TimeOf_still_reads_legacy_hhmm_numbers()
        => Assert.Equal(new TimeSpan(9, 0, 0), RemindersController.TimeOf(900));

    [Fact]
    public void TimeOf_null_is_null() => Assert.Null(RemindersController.TimeOf(null));

    [Fact]
    public async Task CreateReminder_writes_times_as_datetime()
    {
        var db = new InMemoryDatabase();
        var controller = new RemindersController(db, NullLogger<RemindersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "114"),
                        new Claim(ClaimTypes.Name, "tester")
                    }, "test"))
                }
            }
        };

        var result = await controller.CreateReminder(new ReminderCreateRequest
        {
            RecipientUserIds = new() { 114 },
            ReminderText = "تماس با مشتری",
            ReminderDate = new DateTime(2026, 10, 1),
            ReminderTime = new TimeSpan(9, 30, 0)
        });

        Assert.IsType<OkObjectResult>(result);
        var p = Assert.IsType<DynamicParameters>(Assert.Single(db.ExecutedParams));
        Assert.Equal(new DateTime(1899, 12, 30, 9, 30, 0), p.Get<DateTime>("StTime"));
        Assert.IsType<DateTime>(p.Get<object>("CtTime"));
        Assert.Equal(14050709L, p.Get<long>("StDate"));
    }
}
