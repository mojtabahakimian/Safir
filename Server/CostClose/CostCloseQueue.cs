using Safir.Shared.Models.CostClose;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Safir.Server.CostClose
{
    // ═══════════════════════════════════════════════════════════════
    //  اجرای پس‌زمینه
    //
    //  چالش: ConnectionStringProvider رشته اتصال را از هدر
    //  X-DB-Connection هر درخواست می‌گیرد. کار پس‌زمینه HttpContext
    //  ندارد، پس رشته اتصال هم ندارد.
    //
    //  راه‌حل: رشته اتصال هنگام ثبت کار از درخواست جاری برداشته
    //  و همراه کار نگه داشته می‌شود. هنگام اجرا، یک IDatabaseService
    //  با همان رشته ساخته می‌شود.
    //
    //  چرا Hangfire نه: پروژه فعلاً وابستگی به آن ندارد و افزودنش
    //  یعنی جدول‌های جدید در همان پایگاه. کانال درون‌فرایندی برای
    //  یک کار در هر ماه کافی است. اگر بعداً چند سروری شد، جایگزینی
    //  آن با Hangfire فقط این یک فایل را عوض می‌کند.
    // ═══════════════════════════════════════════════════════════════

    public sealed record CostCloseJob(
        int      RunId,
        string   ConnectionString,
        string   UserName,
        string[]? OnlySteps);

    public interface ICostCloseQueue
    {
        bool TryEnqueue(CostCloseJob job, out string? error);
        bool IsRunning(int runId);
        void RequestCancel(int runId);
        bool IsCancelRequested(int runId);
    }

    public sealed class CostCloseQueue : ICostCloseQueue
    {
        private readonly Channel<CostCloseJob> _channel =
            Channel.CreateUnbounded<CostCloseJob>(new UnboundedChannelOptions
            {
                SingleReader = true
            });

        private readonly ConcurrentDictionary<int, byte> _active  = new();
        private readonly ConcurrentDictionary<int, byte> _cancels = new();

        public ChannelReader<CostCloseJob> Reader => _channel.Reader;

        public bool TryEnqueue(CostCloseJob job, out string? error)
        {
            if (_active.ContainsKey(job.RunId))
            {
                error = "این اجرا هم‌اکنون در حال انجام است.";
                return false;
            }

            _active[job.RunId] = 1;
            _cancels.TryRemove(job.RunId, out _);

            if (!_channel.Writer.TryWrite(job))
            {
                _active.TryRemove(job.RunId, out _);
                error = "امکان ثبت کار در صف نبود.";
                return false;
            }

            error = null;
            return true;
        }

        public bool IsRunning(int runId) => _active.ContainsKey(runId);

        public void RequestCancel(int runId) => _cancels[runId] = 1;

        public bool IsCancelRequested(int runId) => _cancels.ContainsKey(runId);

        internal void MarkFinished(int runId)
        {
            _active .TryRemove(runId, out _);
            _cancels.TryRemove(runId, out _);
        }
    }


    // ═══════════════════════════════════════════════════════════════
    //  سرویس مصرف‌کننده صف
    // ═══════════════════════════════════════════════════════════════

    public sealed class CostCloseWorker : BackgroundService
    {
        private readonly CostCloseQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<CostCloseWorker> _logger;

        public CostCloseWorker(
            ICostCloseQueue queue,
            IServiceScopeFactory scopes,
            ILogger<CostCloseWorker> logger)
        {
            _queue  = (CostCloseQueue)queue;
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = _scopes.CreateScope();

                    var orchestrator = scope.ServiceProvider
                        .GetRequiredService<CloseOrchestrator>();

                    await orchestrator.RunAsync(job, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Cost close worker stopping.");
                    throw;
                }
                catch (Exception ex)
                {
                    // خطا اینجا نباید حلقه را بکشد؛ ارکستریتور خودش
                    // وضعیت اجرا را روی «خطا» گذاشته است.
                    _logger.LogError(ex, "Cost close run {RunId} failed", job.RunId);
                }
                finally
                {
                    _queue.MarkFinished(job.RunId);
                }
            }
        }
    }
}
