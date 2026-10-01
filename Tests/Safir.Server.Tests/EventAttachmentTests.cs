using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Controllers;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Automation;
using Xunit;

namespace Safir.Server.Tests;

public class EventAttachmentTests
{
    public class DbProxy : DispatchProxy
    {
        public byte[]? Bytes;
        public string? Extension;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("DoGetDataSQLAsyncSingle", method!.Name);
            var parameters = args![1]!;
            Bytes = (byte[])parameters.GetType().GetProperty("pic")!.GetValue(parameters)!;
            Extension = (string)parameters.GetType().GetProperty("FXTYPE")!.GetValue(parameters)!;
            return Task.FromResult<int?>(5);
        }
    }

    /// <summary>
    /// اکسل و ورد همان مسیرِ ذخیره‌ی ضمیمه (EVENTS.pic + FXTYPE) را می‌روند — WPF هم فایل را با همان
    /// پسوند باز می‌کند. FormFile بدونِ سربرگ عمداً: بخشی بدونِ Content-Type نباید کنترلر را بشکند.
    /// </summary>
    [Theory]
    [InlineData("test.XLSX", ".xlsx")]
    [InlineData("test.xls", ".xls")]
    [InlineData("report.DOCX", ".docx")]
    [InlineData("report.doc", ".doc")]
    public async Task OfficeBytesReachTheExistingAttachmentStorage(string fileName, string extension)
    {
        var db = DispatchProxy.Create<IDatabaseService, DbProxy>();
        var bytes = new byte[] { 80, 75, 3, 4, 1, 2, 3 };
        var controller = new EventsController(db, NullLogger<EventsController>.Instance, new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "test")) } }
        };
        using var stream = new MemoryStream(bytes);
        var result = await controller.CreateEvent(1, new CreateEventRequestDto { IDNUM=1, EVENTS="Excel attachment" },
            new FormFile(stream, 0, bytes.Length, "file", fileName));
        // CreateEvent «200 + رویداد» برمی‌گرداند و AutomationApiService همان را می‌خواند
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(extension, Assert.IsType<EventModel>(ok.Value).AttachedFileType);
        var captured = (DbProxy)(object)db;
        Assert.Equal(bytes, captured.Bytes);
        Assert.Equal(extension, captured.Extension);
    }

    [Theory]
    [InlineData("invoice.xlsx.exe")]
    [InlineData("invoice.xlsm")]
    [InlineData("report.docm")]
    [InlineData("report.docx.js")]
    [InlineData("invoice")]
    public void UnsupportedFilesRemainRejected(string fileName) => Assert.False(EventAttachmentPolicy.IsAllowed(fileName));
}
