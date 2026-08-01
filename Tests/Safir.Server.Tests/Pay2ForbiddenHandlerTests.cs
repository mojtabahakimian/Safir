using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Safir.Client.Services;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// سرور هنگام رد کردن یک عملیات، متن فارسیِ روشنی در بدنه‌ی ۴۰۳ می‌گذارد، ولی
/// GetFromJsonAsync پیش از خواندن بدنه EnsureSuccessStatusCode را صدا می‌زند و
/// آن را دور می‌ریزد. چیزی که کاربر روی صفحه می‌دید این بود:
/// «Response status code does not indicate success: 403 (Forbidden).»
///
/// Pay2ForbiddenHandler همان متن سرور را بالا می‌دهد. اینجا هم همان را می‌سنجیم و
/// هم اینکه دامنه‌اش از مسیرهای PAY2 بیرون نزند.
/// </summary>
public class Pay2ForbiddenHandlerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _mediaType;

        public StubHandler(HttpStatusCode status, string body, string mediaType = "text/plain")
            => (_status, _body, _mediaType) = (status, body, mediaType);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, _mediaType),
            });
    }

    private static HttpClient Client(HttpStatusCode status, string body, string mediaType = "text/plain")
        => new(new Pay2ForbiddenHandler(new StubHandler(status, body, mediaType)))
        {
            BaseAddress = new Uri("https://localhost/"),
        };

    private const string ServerMessage = "دسترسی لازم برای این عملیات را ندارید. («PAY2_DECREE» / See)";

    [Fact]
    public async Task The_servers_own_message_reaches_the_caller_instead_of_the_english_default()
    {
        var http = Client(HttpStatusCode.Forbidden, ServerMessage);

        // همان فراخوانی‌ای که DecreeModal برای گرفتن فهرست احکام انجام می‌دهد.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => http.GetFromJsonAsync<string[]>("api/pay2/employees/455/decrees"));

        Assert.Equal(ServerMessage, ex.Message);
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.DoesNotContain("Response status code", ex.Message);
    }

    [Fact]
    public async Task A_json_quoted_body_is_unwrapped_not_shown_with_its_quotes()
    {
        var http = Client(HttpStatusCode.Forbidden,
            System.Text.Json.JsonSerializer.Serialize(ServerMessage), "application/json");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => http.GetAsync("api/pay2/itemdefs"));

        Assert.Equal(ServerMessage, ex.Message);
    }

    [Fact]
    public async Task An_empty_body_still_gets_a_persian_message()
    {
        var http = Client(HttpStatusCode.Forbidden, "");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => http.GetAsync("api/pay2/workshops"));

        Assert.Equal("شما دسترسی لازم برای این عملیات را ندارید.", ex.Message);
    }

    [Fact]
    public async Task Non_pay2_paths_are_left_alone()
    {
        // بقیه‌ی برنامه نباید رفتارش عوض شود — پاسخ ۴۰۳ همان پاسخ می‌ماند، نه استثنا.
        var http = Client(HttpStatusCode.Forbidden, "nope");

        var res = await http.GetAsync("api/customers");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Only_403_is_intercepted(HttpStatusCode status)
    {
        var http = Client(status, "whatever");

        var res = await http.GetAsync("api/pay2/employees");

        Assert.Equal(status, res.StatusCode);
    }
}
