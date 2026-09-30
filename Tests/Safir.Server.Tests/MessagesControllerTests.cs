using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Controllers;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// پیام‌های داخلی — جدول MESAGEP (هر گیرنده یک ردیف).
/// «خواندنِ گفتگو» فقط باید پیام‌هایی را خوانده کند که همان فرستنده برای
/// کاربرِ جاری فرستاده، نه پیام‌های دیگران را.
/// </summary>
public class MessagesControllerTests
{
    private static (MessagesController Controller, InMemoryDatabase Db) Make(string userId = "114")
    {
        var db = new InMemoryDatabase();
        var controller = new MessagesController(db, NullLogger<MessagesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId),
                        new Claim(ClaimTypes.Name, "tester")
                    }, "test"))
                }
            }
        };
        return (controller, db);
    }

    [Fact]
    public async Task MarkConversationRead_only_touches_messages_from_that_sender_to_me()
    {
        var (controller, db) = Make("114");

        var result = await controller.MarkConversationRead(108);

        Assert.IsType<OkObjectResult>(result.Result);
        var sql = Assert.Single(db.ExecutedSql);
        Assert.Contains("PERSONEL = @UserId", sql);
        Assert.Contains("UID = @SenderId", sql);
        Assert.Contains("STATUS = 1", sql);

        var p = Assert.Single(db.ExecutedParams)!;
        Assert.Equal(114, p.GetType().GetProperty("UserId")!.GetValue(p));
        Assert.Equal(108, p.GetType().GetProperty("SenderId")!.GetValue(p));
    }
}
