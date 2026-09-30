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
