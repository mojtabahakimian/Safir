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
        Task StepProgressAsync(string db, int runId, string stepCode, int percent, string message);
        Task StepFinishedAsync(string db, int runId, string stepCode, byte status);
        Task RunPausedAsync   (string db, int runId, string reason);
        Task RunFailedAsync   (string db, int runId, string stepCode, string? error);
        Task RunCompletedAsync(string db, int runId);
    }

    public sealed class CostCloseNotifier : ICostCloseNotifier
    {
        private readonly IHubContext<CostCloseHub> _hub;
        public CostCloseNotifier(IHubContext<CostCloseHub> hub) => _hub = hub;

        // گروه قدیمی (بدون دیتابیس) هم پیام می‌گیرد تا توکن‌های صادرشده پیش از ارتقا، که claim
        // دیتابیس ندارند، تا انقضا (۸ ساعت) پیشرفت را مثل قبل ببینند؛ بعد از آن این گروه خالی است.
        private IClientProxy Group(string db, int runId) =>
            _hub.Clients.Groups(Key(db, runId), LegacyKey(runId));

        internal static string LegacyKey(int runId) => $"cost-run-{runId}";

        /// <summary>
        /// شماره‌ی اجرا در هر دیتابیس از ۱ شروع می‌شود؛ بدون دیتابیس در نام گروه، تبی که اجرای ۷
        /// یک شرکت را نگاه می‌کند پیشرفت اجرای ۷ شرکت دیگر را هم می‌گرفت.
        /// </summary>
        internal static string Key(string db, int runId) => $"cost-run-{db}-{runId}";

        public Task StepProgressAsync(string db, int runId, string code, int pct, string msg)
            => Group(db, runId).SendAsync("StepProgress",
                   new { stepCode = code, percent = pct, message = msg });

        public Task StepFinishedAsync(string db, int runId, string code, byte status)
            => Group(db, runId).SendAsync("StepFinished",
                   new { stepCode = code, status });

        public Task RunPausedAsync(string db, int runId, string reason)
            => Group(db, runId).SendAsync("RunPaused", reason);

        public Task RunFailedAsync(string db, int runId, string code, string? error)
            => Group(db, runId).SendAsync("RunFailed", new { stepCode = code, error });

        public Task RunCompletedAsync(string db, int runId)
            => Group(db, runId).SendAsync("RunCompleted", runId);
    }

    [Authorize]
    public sealed class CostCloseHub : Hub
    {
        // WebSocket هدر X-DB-Connection ندارد؛ دیتابیس از توکن ورود خوانده می‌شود. توکن‌های
        // قدیمیِ بدون این claim به گروه قدیمی (فقط شماره‌ی اجرا) می‌روند، یعنی همان رفتار قبل.
        private string GroupFor(int runId)
        {
            var db = Context.User?.FindFirst(Safir.Shared.Constants.BaseknowClaimTypes.DB)?.Value;
            return string.IsNullOrEmpty(db) ? CostCloseNotifier.LegacyKey(runId) : CostCloseNotifier.Key(db, runId);
        }

        public Task Subscribe(int runId)
            => Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(runId));

        public Task Unsubscribe(int runId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(runId));
    }
}
