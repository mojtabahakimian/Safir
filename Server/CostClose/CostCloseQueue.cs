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

        /// <summary>
        /// توکن لغوِ مخصوص این اجرا (گره‌خورده به توکنِ خاموش شدن برنامه) تا
        /// کلیدِ توقف بتواند گامِ در حال اجرا را هم قطع کند، نه فقط بینِ گام‌ها.
        /// </summary>
        CancellationTokenSource RegisterRun(int runId, CancellationToken appToken);
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

        /// <summary>توکن لغو هر اجرای در جریان — نگاه کنید RegisterRun</summary>
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _runTokens = new();

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

        /// <summary>
        /// توکن لغوِ مخصوص همین اجرا، گره‌خورده به توکنِ خاموش شدن برنامه.
        ///
        /// چرا لازم است: پیش از این، ارکستریتور همان توکنِ shutdownِ
        /// CostCloseWorker را به گام‌ها می‌داد، و کلیدِ توقف فقط یک پرچم در
        /// _cancels می‌گذاشت که *بین* گام‌ها خوانده می‌شد. یعنی گامی مثل S07A
        /// که خودش ct را چک می‌کند (AverageRateRebuildService) هیچ‌وقت لغوِ
        /// کاربر را نمی‌دید و کاربر تا پایان همان گام — روی ران واقعی تا ۷۳
        /// ثانیه — فکر می‌کرد دکمه خراب است.
        /// </summary>
        public CancellationTokenSource RegisterRun(int runId, CancellationToken appToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(appToken);
            _runTokens[runId] = cts;

            // اگر کاربر بینِ ثبت در صف و شروعِ واقعیِ اجرا دکمه را زده باشد،
            // پرچم از قبل بالاست و این اجرا باید فوراً لغو‌شده به دنیا بیاید.
            if (_cancels.ContainsKey(runId)) cts.Cancel();

            return cts;
        }

        public void RequestCancel(int runId)
        {
            _cancels[runId] = 1;

            if (_runTokens.TryGetValue(runId, out var cts))
            {
                // اجرا ممکن است دقیقاً همین لحظه تمام شده و توکن dispose شده باشد
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        public bool IsCancelRequested(int runId) => _cancels.ContainsKey(runId);

        internal void MarkFinished(int runId)
        {
            _active .TryRemove(runId, out _);
            _cancels.TryRemove(runId, out _);

            if (_runTokens.TryRemove(runId, out var cts)) cts.Dispose();
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
            // خودِ ReadAllAsync وقتی صف خالي است و توکن لغو مي‌شود
            // OperationCanceledException مي‌اندازد — يعني مسير عادي خاموش شدن
            // برنامه. اگر اينجا نگيريمش، از ExecuteAsync بيرون مي‌زند و چون
            // BackgroundServiceExceptionBehavior روي StopHost است، هر خاموش
            // شدن سالم يک لاگ critical «unhandled exception» توليد مي‌کند و
            // در پايش خطا شبيه کرش ديده مي‌شود.
            try
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Cost close worker stopping.");
            }
        }
    }
}
