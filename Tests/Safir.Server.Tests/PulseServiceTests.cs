using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Controllers;
using Safir.Server.Pulse;
using Safir.Shared.Interfaces;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>تقویمِ «نبض سازمان» — سری‌ها روزبه‌روز و بی‌جاافتادگی، حتی از روی مرزِ ماه و سال.</summary>
public class PulseServiceTests
{
    public class DatabaseProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new InvalidOperationException();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }

    private static IDatabaseService Db(Func<MethodInfo, object?[], object?> handler)
    {
        var db = DispatchProxy.Create<IDatabaseService, DatabaseProxy>();
        ((DatabaseProxy)(object)db).Handler = handler;
        return db;
    }

    [Fact]
    public async Task PulseController_DeniesAccess_WhenPulsePermissionMissing()
    {
        var db = Db((method, args) =>
        {
            Assert.Equal("DoGetDataSQLAsyncSingle", method.Name);
            Assert.Contains("PULSE", (string)args[0]!);
            return Task.FromResult(false);
        });

        var controller = new PulseController(db, NullLogger<PulseController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "133")
                    }, "test"))
                }
            }
        };

        var result = await controller.Get();
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
        Assert.Equal("برای دیدنِ نبض سازمان، دسترسیِ «نبض سازمان» لازم است.", status.Value);
    }

    [Fact]
    public void Days_are_consecutive_and_end_on_the_given_day()
    {
        var days = PulseService.PersianDays(14050711, PulseService.DayCount);

        Assert.Equal(PulseService.DayCount, days.Length);
        Assert.Equal(14050711, days[^1]);
        for (int i = 1; i < days.Length; i++)
            Assert.Equal(PulseService.FromPersian(days[i - 1]).AddDays(1), PulseService.FromPersian(days[i]));
    }

    [Fact]
    public void Days_cross_month_and_year_boundaries()
    {
        // شهریور ۳۱ روز دارد؛ اسفندِ ۱۳۹۹ (کبیسه) ۳۰ روز
        Assert.Equal(new long[] { 14050631, 14050701, 14050702 }, PulseService.PersianDays(14050702, 3));
        Assert.Equal(new long[] { 13991229, 13991230, 14000101 }, PulseService.PersianDays(14000101, 3));
    }

    [Fact]
    public void End_day_is_today_for_the_current_fiscal_year()
        => Assert.Equal(14050711, PulseService.EndDay(14050711, 1405, lastData: 14050623));

    [Fact]
    public void End_day_of_a_closed_year_is_its_last_data_day_or_year_end()
    {
        Assert.Equal(14041215, PulseService.EndDay(14050711, 1404, lastData: 14041215));
        Assert.Equal(14041229, PulseService.EndDay(14050711, 1404, lastData: null));
    }

    [Theory]
    [InlineData(1399, 13991230)] // کبیسه
    [InlineData(1400, 14001229)]
    public void Last_day_of_year_respects_leap_years(int year, long expected)
        => Assert.Equal(expected, PulseService.LastDayOfYear(year));

    [Fact]
    public void Weekday_counts_from_saturday()
    {
        Assert.Equal(0, PulseService.PersianWeekday(PulseService.FromPersian(14050711))); // شنبه ۱۱ مهر ۱۴۰۵
        Assert.Equal(6, PulseService.PersianWeekday(PulseService.FromPersian(14050710))); // جمعه
    }
}
