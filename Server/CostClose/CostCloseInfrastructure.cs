using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Safir.Server.Services;
using Safir.Shared.Interfaces;

namespace Safir.Server.CostClose
{
    // ═══════════════════════════════════════════════════════════════
    //  کارخانه سرویس پایگاه داده برای کار پس‌زمینه
    //
    //  DatabaseService رشته اتصال را از IConnectionStringProvider
    //  می‌گیرد و آن هم از هدر درخواست. کار پس‌زمینه درخواستی ندارد،
    //  پس اینجا یک provider ثابت با رشته ذخیره‌شده می‌سازیم.
    // ═══════════════════════════════════════════════════════════════

    public interface IDatabaseServiceFactory
    {
        IDatabaseService Create(string connectionString);
    }

    public sealed class DatabaseServiceFactory : IDatabaseServiceFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public DatabaseServiceFactory(ILoggerFactory loggerFactory)
            => _loggerFactory = loggerFactory;

        public IDatabaseService Create(string connectionString)
            => new DatabaseService(
                   new FixedConnectionStringProvider(connectionString),
                   _loggerFactory.CreateLogger<DatabaseService>());

        private sealed class FixedConnectionStringProvider : IConnectionStringProvider
        {
            private readonly string _cs;
            public FixedConnectionStringProvider(string cs) => _cs = cs;
            public string GetConnectionString() => _cs;
        }
    }


    // ═══════════════════════════════════════════════════════════════
    //  اطلاع‌رسانی زنده
    // ═══════════════════════════════════════════════════════════════

    public interface ICostCloseNotifier
    {
        Task StepProgressAsync(int runId, string stepCode, int percent, string message);
        Task StepFinishedAsync(int runId, string stepCode, byte status);
        Task RunPausedAsync   (int runId, string reason);
        Task RunFailedAsync   (int runId, string stepCode, string? error);
        Task RunCompletedAsync(int runId);
    }

    public sealed class CostCloseNotifier : ICostCloseNotifier
    {
        private readonly IHubContext<CostCloseHub> _hub;
        public CostCloseNotifier(IHubContext<CostCloseHub> hub) => _hub = hub;

        private IClientProxy Group(int runId) => _hub.Clients.Group(Key(runId));
        private static string Key(int runId) => $"cost-run-{runId}";

        public Task StepProgressAsync(int runId, string code, int pct, string msg)
            => Group(runId).SendAsync("StepProgress",
                   new { stepCode = code, percent = pct, message = msg });

        public Task StepFinishedAsync(int runId, string code, byte status)
            => Group(runId).SendAsync("StepFinished",
                   new { stepCode = code, status });

        public Task RunPausedAsync(int runId, string reason)
            => Group(runId).SendAsync("RunPaused", reason);

        public Task RunFailedAsync(int runId, string code, string? error)
            => Group(runId).SendAsync("RunFailed", new { stepCode = code, error });

        public Task RunCompletedAsync(int runId)
            => Group(runId).SendAsync("RunCompleted", runId);
    }

    [Authorize]
    public sealed class CostCloseHub : Hub
    {
        public Task Subscribe(int runId)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"cost-run-{runId}");

        public Task Unsubscribe(int runId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cost-run-{runId}");
    }
}
