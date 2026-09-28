using Safir.Shared.Interfaces;
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
        string[]? OnlySteps)
    {
        /// <summary>
        /// شماره‌ی اجرا در هر دیتابیس از ۱ شروع می‌شود؛ صف و گروه SignalR بین همه‌ی
        /// دیتابیس‌های این سرور مشترک‌اند، پس کلیدشان «دیتابیس + شماره» است، نه فقط شماره.
        /// </summary>
        public string Db => Safir.Server.Services.DbKey.From(ConnectionString);
    }

    public interface ICostCloseQueue
    {
        bool TryEnqueue(CostCloseJob job, out string? error);
        bool IsRunning(string db, int runId);
        bool AnyRunning(string db);
        void RequestCancel(string db, int runId);
        bool IsCancelRequested(string db, int runId);

        /// <summary>
        /// توکن لغوِ مخصوص این اجرا (گره‌خورده به توکنِ خاموش شدن برنامه) تا
        /// کلیدِ توقف بتواند گامِ در حال اجرا را هم قطع کند، نه فقط بینِ گام‌ها.
        /// </summary>
        CancellationTokenSource RegisterRun(string db, int runId, CancellationToken appToken);
    }

    public sealed class CostCloseQueue : ICostCloseQueue
    {
        private readonly Channel<CostCloseJob> _channel =
            Channel.CreateUnbounded<CostCloseJob>(new UnboundedChannelOptions
            {
                SingleReader = true
            });

        private static string Key(string db, int runId) => $"{db}#{runId}";

        private readonly ConcurrentDictionary<string, byte> _active  = new();
        private readonly ConcurrentDictionary<string, byte> _cancels = new();

        /// <summary>توکن لغو هر اجرای در جریان — نگاه کنید RegisterRun</summary>
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runTokens = new();

        public ChannelReader<CostCloseJob> Reader => _channel.Reader;

        public bool TryEnqueue(CostCloseJob job, out string? error)
        {
            var key = Key(job.Db, job.RunId);
            if (_active.ContainsKey(key))
            {
                error = "این اجرا هم‌اکنون در حال انجام است.";
                return false;
            }

            _active[key] = 1;
            _cancels.TryRemove(key, out _);

            if (!_channel.Writer.TryWrite(job))
            {
                _active.TryRemove(key, out _);
                error = "امکان ثبت کار در صف نبود.";
                return false;
            }

            error = null;
            return true;
        }

        public bool IsRunning(string db, int runId) => _active.ContainsKey(Key(db, runId));

        /// <summary>اجرایی از این دیتابیس در صف یا در حال اجرا روی همین سرور هست؟</summary>
        public bool AnyRunning(string db) => _active.Keys.Any(k => k.StartsWith(db + "#", StringComparison.Ordinal));

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
        public CancellationTokenSource RegisterRun(string db, int runId, CancellationToken appToken)
        {
            var key = Key(db, runId);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(appToken);
            _runTokens[key] = cts;

            // اگر کاربر بینِ ثبت در صف و شروعِ واقعیِ اجرا دکمه را زده باشد،
            // پرچم از قبل بالاست و این اجرا باید فوراً لغو‌شده به دنیا بیاید.
            if (_cancels.ContainsKey(key)) cts.Cancel();

            return cts;
        }

        public void RequestCancel(string db, int runId)
        {
            var key = Key(db, runId);
            _cancels[key] = 1;

            if (_runTokens.TryGetValue(key, out var cts))
            {
                // اجرا ممکن است دقیقاً همین لحظه تمام شده و توکن dispose شده باشد
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        public bool IsCancelRequested(string db, int runId) => _cancels.ContainsKey(Key(db, runId));

        internal void MarkFinished(string db, int runId)
        {
            var key = Key(db, runId);
            _active .TryRemove(key, out _);
            _cancels.TryRemove(key, out _);

            if (_runTokens.TryRemove(key, out var cts)) cts.Dispose();
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
            // ───── آزادسازیِ اجراهای یخ‌زده، پیش از هر کار دیگر ─────
            // صف در حافظه‌ی همین پروسه است. اگر سرور وسط یک اجرا ری‌استارت
            // شود، صف از بین می‌رود ولی CC_Run.Status روی «در حال اجرا»
            // می‌ماند — برای همیشه، چون هیچ‌کس دیگر آن اجرا را دست نمی‌گیرد.
            // کاربر ساعت‌ها بعد صفحه را باز می‌کند و اجرایی می‌بیند که
            // هیچ‌وقت گام بعدی را شروع نمی‌کند.
            //
            // اینجا، درست بعد از بالا آمدن، هر اجرایی که ضربانش کهنه است
            // «متوقف‌شده» علامت می‌خورد تا با «ادامه اجرا» قابل ادامه باشد.
            // نگاه کنید 34-run-heartbeat.sql.
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

                var freed = (await db.DoGetDataSQLAsync<int>(
                    "EXEC dbo.CC_sp_ReleaseStaleRuns @StaleMinutes = 15")).ToList();

                if (freed.Count > 0)
                    _logger.LogWarning(
                        "{Count} اجرای یخ‌زده آزاد شد: {Runs}", freed.Count, string.Join(", ", freed));
            }
            catch (Exception ex)
            {
                // نبودِ رویه (پایگاهی که هنوز 34 را نگرفته) نباید جلوی
                // بالا آمدنِ سرویس را بگیرد.
                _logger.LogWarning(ex, "آزادسازی اجراهای یخ‌زده انجام نشد");
            }

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
                        _queue.MarkFinished(job.Db, job.RunId);
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
