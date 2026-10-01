using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Client.Services;
using Xunit;

namespace Safir.Server.Tests;

public class CustomerStatementAccessTests
{
    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static HttpClient Client(HttpStatusCode status, string body) =>
        new(new ResponseHandler(status, body)) { BaseAddress = new Uri("https://localhost/") };

    [Fact]
    public async Task ForbiddenStatementIsNotConvertedToAnEmptySuccessfulResult()
    {
        using var http = Client(HttpStatusCode.Forbidden, "شما اجازه دسترسی به این حساب را ندارید.");
        var api = new CustomerApi(http, NullLogger<CustomerApi>.Instance);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetCustomerStatementAsync("115-1-1037"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("اجازه دسترسی", error.Message);
    }

    [Fact]
    public async Task PermittedAccountWithoutTransactionsReturnsAnEmptySuccessfulResult()
    {
        using var http = Client(HttpStatusCode.OK, "[]");
        var api = new CustomerApi(http, NullLogger<CustomerApi>.Instance);
        Assert.Empty((await api.GetCustomerStatementAsync("115-33-26-1"))!);
    }

    [Fact]
    public async Task ForbiddenPdfPreservesReadableAccessError()
    {
        using var http = Client(HttpStatusCode.Forbidden, "");
        var api = new CustomerApi(http, NullLogger<CustomerApi>.Instance);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<HttpRequestException>(() => api.GetCustomerStatementPdfBytesAsync("115-1-1037"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<HttpRequestException>(() => new ReportApiService(http).GeneratePdfAsync(
                "R_DAFTAR_TAFZILY_2_2.mrt", new()))).StatusCode);
    }
}
