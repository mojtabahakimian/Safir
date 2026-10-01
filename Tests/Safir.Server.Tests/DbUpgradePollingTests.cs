using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using Safir.Client.Pages.Admin;
using Safir.Client.Services;
using Safir.Shared.Models.DbAdmin;
using Xunit;

namespace Safir.Server.Tests;

public class DbUpgradePollingTests
{
    private sealed class PendingHandler : HttpMessageHandler
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<HttpResponseMessage> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCanceled;
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Started.TrySetResult();
            try { return await Response.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { WasCanceled = true; throw; }
        }
    }

    // A renderer attaches the real component lifecycle; its visual children are
    // irrelevant to these navigation/in-flight request regressions.
    private sealed class QuietUpgrade : DbUpgrade
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

#pragma warning disable BL0006 // Intentional minimal test renderer, without adding a UI test package.
    private sealed class TestRenderer : Renderer
    {
        public readonly List<Exception> Errors = new();
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public TestRenderer() : base(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance) { }
        public void Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => Errors.Add(exception);
    }
#pragma warning restore BL0006

    public class DialogProxy : DispatchProxy
    {
        public IDialogReference Reference = default!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "ShowAsync") return Task.FromResult(Reference);
            throw new NotSupportedException(method.Name);
        }
    }

    private static void Set(object component, string name, object value) =>
        typeof(DbUpgrade).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static Task Call(object component, string method, params object[] args) =>
        (Task)typeof(DbUpgrade).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args)!;

    private static HttpClient Client(PendingHandler handler) => new(handler) { BaseAddress = new Uri("https://localhost/") };

    private sealed class ReloadHandler : HttpMessageHandler
    {
        public readonly TaskCompletionSource ProgressStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ProgressCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecutionReads;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/status"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DbUpgradeStatus()) };
            if (++ExecutionReads == 1)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new DbUpgradeExecution { Running = true, StartedAtUtc = DateTime.UtcNow })
                };
            ProgressStarted.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { ProgressCanceled.SetResult(); throw; }
            throw new InvalidOperationException("The pending progress request must be canceled by navigation.");
        }
    }

    [Fact]
    public async Task RefreshStartsPollingWhenAnotherPageHasAlreadyStartedTheUpgrade()
    {
        var handler = new ReloadHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var component = new QuietUpgrade();
        Set(component, "Api", new DbAdminApiService(http));
        using var renderer = new TestRenderer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(component));

        await renderer.Dispatcher.InvokeAsync(() => Call(component, "LoadAsync"));
        await handler.ProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.Dispatcher.InvokeAsync(component.Dispose);
        await handler.ProgressCanceled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, handler.ExecutionReads);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task NavigatingAwayCancelsAnOutstandingProgressRequest()
    {
        var handler = new PendingHandler();
        using var http = Client(handler);
        var component = new QuietUpgrade();
        Set(component, "Api", new DbAdminApiService(http));
        var polling = Call(component, "StartPollingAsync", DateTime.MinValue);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        component.Dispose();
        await polling.WaitAsync(TimeSpan.FromSeconds(3));
        component.Dispose();

        Assert.True(handler.WasCanceled);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionAfterNavigationDoesNotUseDisposedPollingOrRenderTheOldPage(bool preview)
    {
        var handler = new PendingHandler();
        using var http = Client(handler);
        var api = new DbAdminApiService(http);
        // The production upgrade client intentionally has a separate timeout;
        // replace only its transport here so no server or database is involved.
        typeof(DbAdminApiService).GetField("_slow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(api, http);

        var dialog = DispatchProxy.Create<IDialogService, DialogProxy>();
        var reference = new DialogReference(Guid.NewGuid(), dialog);
        reference.Dismiss(DialogResult.Ok(true));
        ((DialogProxy)(object)dialog).Reference = reference;

        var component = new QuietUpgrade();
        Set(component, "Api", api);
        Set(component, "Dialog", dialog);
        typeof(DbUpgrade).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component,
            new DbUpgradeStatus { Database = "SafirTest", Server = "localhost" });
        using var renderer = new TestRenderer();
        await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(component));

        var running = renderer.Dispatcher.InvokeAsync(() => Call(component, "ConfirmAndRunAsync", preview));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await renderer.Dispatcher.InvokeAsync(component.Dispose);
        handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new DbUpgradeResult { Success = true, WasPreview = preview })
        });
        await running.WaitAsync(TimeSpan.FromSeconds(3));
        await renderer.Dispatcher.InvokeAsync(component.Dispose);

        Assert.Empty(renderer.Errors);
        Assert.False(handler.WasCanceled); // Navigation must not cancel the upgrade itself.
        Assert.Equal(1, handler.Calls); // No status reload or other request after navigation.
    }
}
