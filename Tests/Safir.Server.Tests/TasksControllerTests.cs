using System.Dynamic;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Controllers;
using Safir.Shared.Models.Automation;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// کارتابل اتوماسیون — جدول TASKS.
///
/// SEE («مجری دیده است») در پایگاه int است و SEET datetime. خواندنشان
/// قبلاً بی‌صدا شکست می‌خورد (null)، و چون UpdateTask همان null را
/// برمی‌گرداند، هر ویرایشِ وظیفه این دو را پاک می‌کرد. با تیکِ یک‌کلیکیِ
/// «انجام شد» در کارتابلِ جدید، این پاک شدن روی هر کلیک رخ می‌داد.
/// </summary>
public class TasksControllerTests
{
    private static object Row(object? see, object? seet)
    {
        dynamic r = new ExpandoObject();
        r.IDNUM = 123L; r.NAME = "مشتری"; r.GR = null; r.PERSONEL = 114; r.TASK = "پیگیری";
        r.PERIORITY = 2; r.STATUS = 1; r.USERNAME = "admin"; r.COMP_COD = "128-3-1";
        r.skid = 13; r.num = 7368L; r.tg = 13L; r.CTIM = DateTime.Now; r.USERCO = 114;
        r.SEE = see; r.SEET_DB = seet;
        r.STDATE_DB = 14050708L; r.STTIME_DB = 1530; r.ENDATE_DB = null; r.ENTIME_DB = null; r.SUMTIME_DB = null;
        return r;
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(null, null)]
    public void MapTask_reads_int_SEE_column(int? see, bool? expected)
        => Assert.Equal(expected, TasksController.MapTask(Row(see, null)).SEE);

    [Fact]
    public void MapTask_keeps_datetime_SEET()
    {
        var seen = new DateTime(2026, 9, 29, 10, 15, 0);
        Assert.Equal(seen, TasksController.MapTask(Row(1, seen)).SEET);
    }

    [Fact]
    public void MapTask_still_maps_the_other_fields()
    {
        var t = TasksController.MapTask(Row(0, null));
        Assert.Equal(123, t.IDNUM);
        Assert.Equal(13, t.skid);
        Assert.Equal(7368, t.num);
        Assert.Equal(new TimeSpan(15, 30, 0), t.STTIME);
        Assert.NotNull(t.STDATE);
    }

    [Fact]
    public async Task UpdateTask_never_writes_SEE_or_SEET()
    {
        var db = new InMemoryDatabase();
        var controller = new TasksController(db, NullLogger<TasksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "114") }, "test"))
                }
            }
        };

        var result = await controller.UpdateTask(5, new TaskModel
        {
            IDNUM = 5, PERSONEL = 114, TASK = "x", PERIORITY = 2, STATUS = 2,
            COMP_COD = "128-3-1", SEE = null, SEET = null
        });

        Assert.IsType<NoContentResult>(result);
        var update = Assert.Single(db.ExecutedSql, s => s.Contains("UPDATE dbo.TASKS"));
        Assert.DoesNotMatch(new Regex(@"\bSEET?\s*="), update);
    }
}
