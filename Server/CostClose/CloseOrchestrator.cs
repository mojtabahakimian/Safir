using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;
using System.Text.Json;

namespace Safir.Server.CostClose
{
    // ═══════════════════════════════════════════════════════════════
    //  قرارداد گام
    // ═══════════════════════════════════════════════════════════════

    public sealed class StepContext
    {
        public required int    RunId       { get; init; }
        public required short  FiscalYear  { get; init; }
        public required byte   Month       { get; init; }
        public required long   DateFrom    { get; init; }
        public required long   DateTo      { get; init; }
        public required string UserName    { get; init; }
        public required byte   RunKind     { get; init; }

        /// <summary>سرویس پایگاه داده با رشته اتصال همان اجرا</summary>
        public required IDatabaseService Db { get; init; }

        public required Func<string, int, string, Task> ReportProgress { get; init; }
        public required CancellationToken Ct { get; init; }
    }

    public sealed record StepResult(
        CostStepStatus Status,
        int            RowsAffected = 0,
        object?        Result       = null,
        string?        Error        = null)
    {
        public static StepResult Ok(int rows = 0, object? result = null)
            => new(CostStepStatus.Success, rows, result);

        public static StepResult Warn(int rows = 0, object? result = null)
            => new(CostStepStatus.Warning, rows, result);

        public static StepResult Fail(string error)
            => new(CostStepStatus.Failed, 0, null, error);
    }

    public interface ICostStep
    {
        string StepCode { get; }
        string Title    { get; }
        short  SeqNo    { get; }

        /// <summary>پیش از اجرا اسنپ‌شات گرفته شود؟</summary>
        bool RequiresSnapshot { get; }

        /// <summary>
        /// دروازه است؟ اگر بله و نتیجه موفق نبود، pipeline متوقف
        /// می‌شود تا کاربر مغایرت‌ها را رفع کند.
        /// </summary>
        bool IsGate { get; }

        /// <summary>در فرمول‌ها می‌نویسد؟ اگر بله، پرچم بازتولید بالا می‌رود.</summary>
        bool WritesFormulas { get; }

        /// <summary>
        /// اگر false، این گام هرگز به‌طور خودکار اجرا نمی‌شود — نه در
        /// زنجیره‌ی کامل یک اجرای معمولی/ادامه (OnlySteps=null)، نه در
        /// بازتولیدِ خودکارِ ناشی از WritesFormulas. فقط وقتی کاربر آن
        /// را صریحاً در OnlySteps انتخاب کند (مثلاً از دیالوگ «اجرای
        /// مجدد گام‌ها») اجرا می‌شود. برای S07B: تخصیص دستمزد روی داده‌ی
        /// دستیِ کاربر (ضریب‌ها) کار می‌کند و اجرای خودکارِ بی‌اطلاعِ او
        /// می‌تواند فرمول‌ها را با آخرین ضریب‌های هنوز کامل‌نشده به‌روز
        /// کند — کاربر باید صراحتاً بخواهد.
        /// </summary>
        bool AutoRun => true;

        Task<StepResult> ExecuteAsync(StepContext ctx);
    }


    // ═══════════════════════════════════════════════════════════════
    //  ارکستریتور
    // ═══════════════════════════════════════════════════════════════

    public sealed class CloseOrchestrator
    {
        private readonly IEnumerable<ICostStep>    _steps;
        private readonly ICostCloseQueue           _queue;
        private readonly ICostCloseNotifier        _notify;
        private readonly IDatabaseServiceFactory   _dbFactory;
        private readonly ILogger<CloseOrchestrator> _logger;

        public CloseOrchestrator(
            IEnumerable<ICostStep> steps,
            ICostCloseQueue queue,
            ICostCloseNotifier notify,
            IDatabaseServiceFactory dbFactory,
            ILogger<CloseOrchestrator> logger)
        {
            _steps     = steps;
            _queue     = queue;
            _notify    = notify;
            _dbFactory = dbFactory;
            _logger    = logger;
        }

        public async Task RunAsync(CostCloseJob job, CancellationToken ct)
        {
            var db = _dbFactory.Create(job.ConnectionString);

            var run = await db.DoGetDataSQLAsyncSingle<CostRunDto>(
                "SELECT * FROM dbo.CC_Run WHERE RunId = @id", new { id = job.RunId });

            if (run is null)
            {
                _logger.LogWarning("Run {RunId} not found", job.RunId);
                return;
            }

            await SetRunStatusAsync(db, job.RunId, CostRunStatus.Running);

            // این try/catch بیرونی، نه گام‌به‌گام: CC_sp_StepStart/Finish و
            // CC_sp_SetFormulasDirty بین گام‌ها خارج از try داخلی‌اند. اگر هرکدام
            // خطا بدهد (قفل، Timeout، هرچیزی)، بدون این، استثنا تا
            // CostCloseWorker بالا می‌رود که فقط لاگ می‌کند و MarkFinished
            // صدا می‌زند — CC_Run.Status همان «در حال اجرا» می‌ماند، برای همیشه،
            // بدون هیچ خطای قابل‌دیدن در رابط کاربری یا CC_RunLog. کاربر فقط
            // یک اجرای گیرافتاده می‌بیند که هیچ‌وقت گام بعدی را شروع نمی‌کند،
            // و تنها ردِ واقعی خطا در لاگ سمت سرور است.
            try
            {
                // بازسازی نرخ میانگین (S07A) بدون انتشار نرخ (S11) بعدش،
                // مفهوم «بازسازی» را نصفه می‌گذارد — نرخ‌های تازه‌محاسبه‌شده
                // هرگز به فرمول‌ها نمی‌رسند و نتیجه با انتظار کاربر نمی‌خواند.
                // پس در اجرای جزئی (OnlySteps)، اگر S07A خواسته شده ولی S11
                // نه، S11 را خودمان اضافه می‌کنیم تا حلقه‌ی همگرایی زیر هم
                // فرصت اجرا شدن پیدا کند.
                var onlySteps = job.OnlySteps;
                if (onlySteps is not null
                    && onlySteps.Contains("S07A") && !onlySteps.Contains("S11"))
                {
                    onlySteps = onlySteps.Append("S11").ToArray();
                }

                var ordered = _steps
                    .OrderBy(s => s.SeqNo)
                    .Where(s => onlySteps is not null
                                    ? onlySteps.Contains(s.StepCode)
                                    : s.AutoRun)
                    .ToList();

                // اجرای گام‌ها؛ فهرست ممکن است حین اجرا گسترش یابد
                // (وقتی فرمول‌ها عوض شده و بازتولید لازم است)
                var pending = new Queue<ICostStep>(ordered);

                // ── همگرایی نرخ S07A↔S11 ──
                // S07A نرخ میانگین کاردکس را از رسیدهای تولید حساب می‌کند —
                // که خودشان بهایشان را از فرمول (S11) می‌گیرند؛ S11 هم نرخ
                // موادی که این ماه گردش دارند را از میانگین انبار (خروجی
                // S07A) می‌گیرد، نه از کاسکید فرمول. یعنی این دو گام روی هم
                // اثر می‌گذارند و یک پاس تضمین نمی‌کند نرخ نهایی باشد — باید
                // آن‌قدر تکرار شوند تا نرخ‌ها بین دو دور پشت‌سرهم عوض نشوند.
                // بدون این حلقه، اسناد گروهی (انتقال/فروش) که بعداً صادر
                // می‌شوند، نرخِ یک نقطه‌ی میانی از این همگرایی را قفل
                // می‌کنند — دقیقاً همان مغایرت ۹.۷ میلیاردی کد ۳۳۶۵/انبار۲
                // که در بازبینی دستی پیدا شد.
                // ⚠️ اصلاح همگرایی (بعد از فیکس میرایی در CC_sp_S11_PropagateRates):
                // آن فیکس، کالاهای چندسطحیِ خودمصرف (مثل ۳۷۳→۱۷۳۲→۳۳۶۵) را از
                // واگرایی نجات داد، ولی به یک سری هندسیِ همگرا تبدیلشان کرد
                // (تست عملی: هر دور ≈۰.۶۵ برابر دور قبل)، نه یک نقطه‌ی ثابتِ
                // دقیق در یک دور. مقایسه‌ی چک‌سامِ دقیق (CHECKSUM_AGG) هرگز با
                // این سری «برابر» نمی‌شود مگر تغییرِ هر دور از دقتِ FLOAT پایین‌تر
                // برود — که ده‌ها دور طول می‌کشد، نه ۵ تا. راه‌حل درست، مقایسه‌ی
                // «آستانه‌ای» (بیشترین تغییرِ TotalCost بین دو دورِ پیاپی، روی هر
                // کالا) است، نه برابریِ دقیق — با همان آستانه‌ی یک‌ریالیِ CHK-02
                // («این دو باید دقیقاً یکی باشند» تا سطح ریال، نه فراتر).
                const double RateConvergeThreshold = 1.0;
                Dictionary<long, double>? lastRates = null;
                int    s11Cycles          = 0;
                // تست عملی روی ران واقعی (کدهای ۳۳۶۵ و ۳۱۰۰، هر دو زنجیره‌ی
                // خودمصرفِ چندسطحی): با نسبتِ ثابتِ ≈۰.۷۵ در هر دور، از
                // بیشترین‌تغییرِ ~۱۳۸ ریال تا زیر آستانه‌ی یک‌ریالی حدود
                // ۱۹-۲۰ دور طول کشید، نه ۵ تا.
                //
                // ⚠️ اصلاح (تأیید کاربر، RunId=6/اردیبهشت کشف شد): با ۲۵
                // سقف، کالاهایی که فاصله‌ی اولشان بزرگ‌تر بود (مثلاً کد ۷۱
                // «نایلون» با ~۲۱۳ ریال فاصله‌ی اول) به سقف می‌خوردند بدونِ
                // رسیدن به زیرِ آستانه — S11 «با آخرین مقدار» ادامه می‌داد
                // و همین چند فرمول را در CHK-09 («نرخ منتشرنشده») گیر
                // می‌انداخت. همان کاهشِ ۳۵٪ در هر دور (میراییِ S11) امن و
                // درست کار می‌کند، فقط برای فاصله‌ی اولیه‌ی بزرگ‌تر به
                // دورهای بیشتری نیاز دارد؛ ۴۰ حاشیه‌ی اطمینانِ بیشتری می‌دهد.
                const int MaxS11Cycles    = 40;

                while (pending.Count > 0)
                {
                    if (ct.IsCancellationRequested || _queue.IsCancelRequested(job.RunId))
                    {
                        await LogAsync(db, job.RunId, null, 2, "اجرا توسط کاربر متوقف شد");
                        await SetRunStatusAsync(db, job.RunId, CostRunStatus.Paused);
                        await _notify.RunPausedAsync(job.RunId, "cancelled");
                        return;
                    }

                    var step = pending.Dequeue();

                    var ctx = new StepContext
                    {
                        RunId      = job.RunId,
                        FiscalYear = run.FiscalYear,
                        Month      = run.PeriodMonth,
                        DateFrom   = run.DateFrom,
                        DateTo     = run.DateTo,
                        UserName   = job.UserName,
                        RunKind    = run.RunKind,
                        Db         = db,
                        Ct         = ct,
                        ReportProgress = (code, pct, msg) =>
                            _notify.StepProgressAsync(job.RunId, code, pct, msg)
                    };

                    await db.DoGetStoreProcedureSQLAsync<dynamic>("dbo.CC_sp_StepStart", new
                    {
                        RunId    = job.RunId,
                        StepCode = step.StepCode,
                        Title    = step.Title,
                        SeqNo    = step.SeqNo
                    });

                    await _notify.StepProgressAsync(job.RunId, step.StepCode, 0, step.Title);

                    StepResult result;

                    try
                    {
                        if (step.RequiresSnapshot)
                        {
                            await ctx.ReportProgress(step.StepCode, 5, "گرفتن اسنپ‌شات…");
                            await db.DoGetStoreProcedureSQLAsync<dynamic>("dbo.CC_sp_Snapshot", new
                            {
                                RunId    = job.RunId,
                                StepCode = step.StepCode,
                                Month    = run.PeriodMonth,
                                DT1      = run.DateFrom,
                                DT2      = run.DateTo
                            }, commandTimeout: 1800);
                        }

                        result = await step.ExecuteAsync(ctx);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Step {Step} failed in run {RunId}",
                                         step.StepCode, job.RunId);
                        result = StepResult.Fail(ex.Message);
                    }

                    await db.DoGetStoreProcedureSQLAsync<dynamic>("dbo.CC_sp_StepFinish", new
                    {
                        RunId    = job.RunId,
                        StepCode = step.StepCode,
                        Status   = (byte)result.Status,
                        Rows     = result.RowsAffected,
                        Result   = result.Result is null
                                     ? null : JsonSerializer.Serialize(result.Result),
                        Error    = result.Error
                    });

                    await _notify.StepFinishedAsync(job.RunId, step.StepCode, (byte)result.Status);

                    // ── خطا: توقف کامل ──
                    if (result.Status == CostStepStatus.Failed)
                    {
                        await SetRunStatusAsync(db, job.RunId, CostRunStatus.Failed);
                        await _notify.RunFailedAsync(job.RunId, step.StepCode, result.Error);
                        return;
                    }

                    // ── دروازه بسته: توقف تا رفع مغایرت ──
                    if (step.IsGate && result.Status != CostStepStatus.Success)
                    {
                        await SetRunStatusAsync(db, job.RunId, CostRunStatus.Paused);
                        await _notify.RunPausedAsync(job.RunId, step.StepCode);
                        return;
                    }

                    // ── فرمول‌ها عوض شد: بازتولید خروج مواد و انحراف ──
                    //    درسی که از کالای ۲۸۴۱ گرفتیم: حواله‌ای که پس از
                    //    ویرایش فرمول بازسازی نشود، مقدارش با فرمول نمی‌خواند.
                    if (step.WritesFormulas)
                    {
                        await db.DoGetStoreProcedureSQLAsync<dynamic>(
                            "dbo.CC_sp_SetFormulasDirty",
                            new { RunId = job.RunId, Dirty = true });

                        // ⚠️ عمداً S07B اینجا نیست (تأیید کاربر): آن گام فقط با
                        // درخواست صریح کاربر اجرا می‌شود، نه به‌صورت خودکار در
                        // این بازتولید — نگاه کنید AutoRun روی ICostStep.
                        var rebuild = _steps
                            .Where(s => s.StepCode is "S07" or "S07A" or "S08")
                            .OrderBy(s => s.SeqNo);

                        foreach (var rs in rebuild)
                            if (!pending.Contains(rs))
                                pending.Enqueue(rs);

                        await LogAsync(db, job.RunId, step.StepCode, 1,
                            "فرمول‌ها تغییر کرد — خروج مواد و انحراف بازسازی می‌شوند");
                    }

                    if (step.StepCode == "S08")
                        await db.DoGetStoreProcedureSQLAsync<dynamic>(
                            "dbo.CC_sp_SetFormulasDirty",
                            new { RunId = job.RunId, Dirty = false });

                    // ── همگرایی نرخ S07A↔S11 ──
                    // فقط وقتی معنا دارد که خودِ S11 موفق اجرا شده باشد.
                    if (step.StepCode == "S11" && result.Status != CostStepStatus.Failed)
                    {
                        var rates = await GetItemCostSnapshotAsync(db, job.RunId);
                        (double Max, long? Code)? delta = lastRates is null
                            ? null
                            : MaxAbsDelta(lastRates, rates);

                        // ⚠️ اصلاح (تأیید کاربر، کد ۳۵۱۴/whey پودر کشف شد): مقایسه
                        // باید روی مقدارِ گردشده به نزدیک‌ترین ریال باشد، نه رقمِ
                        // دقیقِ اعشاری — چون ریال کوچک‌ترین واحدِ پول است، کسرِ آن
                        // بی‌معناست. بدون این گرد‌کردن، لاگ می‌گفت «بیشترین تغییر: ۱
                        // ریال» (که با آستانه‌ی نمایشیِ ۱ ریال «همگرا» به‌نظر می‌رسید)
                        // ولی چون رقمِ دقیقش چیزی مثل ۱٫۰۰۰۰۰۰X بود (باقیماندهٔ
                        // اعشاریِ طبیعیِ محاسبه روی زنجیره‌های چندسطحی)، شرطِ
                        // `<= 1.0` رد می‌شد و بی‌دلیل تا سقفِ ۲۵ دور ادامه پیدا
                        // می‌کرد — درحالی‌که از نظرِ مالی از قبل همگرا بود.
                        var maxDeltaRounded = delta is null
                            ? (double?)null
                            : Math.Round(delta.Value.Max, MidpointRounding.AwayFromZero);

                        if (maxDeltaRounded is not null && maxDeltaRounded <= RateConvergeThreshold)
                        {
                            // بیشترین تغییرِ نرخِ همه‌ی کالاها بین این دور و دور
                            // قبل زیر یک ریال است — همگرا شد
                        }
                        else if (s11Cycles < MaxS11Cycles)
                        {
                            s11Cycles++;
                            lastRates = rates;

                            var repeat = _steps
                                .Where(s => s.StepCode is "S07A" or "S11")
                                .OrderBy(s => s.SeqNo)
                                .ToList();

                            // باید بلافاصله بعد از S11 اجرا شوند، نه بعد از
                            // S12 و مراحل بعدی — پس به جلوی صف اضافه می‌شوند،
                            // نه به انتهای آن (بر خلاف بازتولید WritesFormulas بالا).
                            pending = new Queue<ICostStep>(repeat.Concat(pending));

                            var deltaTxt = delta is null
                                ? ""
                                : $" (بیشترین تغییر: {delta.Value.Max:N0} ریال روی کالای {await DescribeItemAsync(db, delta.Value.Code)})";
                            await LogAsync(db, job.RunId, "S11", 1,
                                $"نرخ مواد بین این دور و دور قبل فرق دارد{deltaTxt} — S07A/S11 دوباره اجرا می‌شود (دور {s11Cycles + 1})");
                        }
                        else
                        {
                            lastRates = rates;
                            var worstItem = delta is null ? "؟" : await DescribeItemAsync(db, delta.Value.Code);
                            await LogAsync(db, job.RunId, "S11", 2,
                                $"همگرایی نرخ پس از {MaxS11Cycles} دور تکرار کامل نشد (بیشترین تغییر هنوز {delta?.Max:N0} ریال روی کالای {worstItem}) — با آخرین مقدار ادامه داده می‌شود");
                        }
                    }
                }

                await SetRunStatusAsync(db, job.RunId, CostRunStatus.Completed);
                await _notify.RunCompletedAsync(job.RunId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run {RunId} failed outside step handling", job.RunId);

                try
                {
                    await LogAsync(db, job.RunId, null, 2,
                        $"اجرا با خطای غیرمنتظره متوقف شد: {ex.Message}");
                    await SetRunStatusAsync(db, job.RunId, CostRunStatus.Failed);
                    await _notify.RunFailedAsync(job.RunId, string.Empty, ex.Message);
                }
                catch (Exception logEx)
                {
                    // اگر همین گزارش‌کردن هم به دیتابیس نرسد (مثلاً اتصال قطع
                    // شده)، حداقل اجرا در وضعیت «در حال اجرا» یخ‌زده نمی‌ماند
                    // بدون هیچ رد پایی در لاگ سرور.
                    _logger.LogError(logEx,
                        "Could not mark run {RunId} as failed after the original error", job.RunId);
                }
            }
        }

        private static Task SetRunStatusAsync(IDatabaseService db, int runId, CostRunStatus st)
            => db.DoExecuteSQLAsync(
                @"UPDATE dbo.CC_Run
                     SET Status = @st,
                         FinishedAtUtc = CASE WHEN @st IN (3,4,5)
                                              THEN SYSUTCDATETIME() ELSE FinishedAtUtc END
                   WHERE RunId = @runId",
                new { runId, st = (byte)st });

        private static Task LogAsync(
            IDatabaseService db, int runId, string? step, byte sev, string msg)
            => db.DoExecuteSQLAsync(
                @"INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
                  VALUES (@runId, @step, @sev, @msg)",
                new { runId, step, sev, msg });

        /// <summary>
        /// عکسِ نرخ‌های همین اجرا — برای تشخیص همگراییِ آستانه‌ایِ S07A↔S11.
        /// CC_ItemCost خروجی مستقیم S11 است (هر بار DELETE/INSERT کامل
        /// می‌شود). قبلاً یک چک‌سامِ کل جدول مقایسه می‌شد (برابریِ دقیق)؛
        /// چون فیکس میراییِ S11 کالاهای خودمصرف را به یک سریِ هندسیِ
        /// همگرا تبدیل می‌کند نه یک نقطه‌ی ثابتِ دقیق در یک دور، چک‌سام
        /// دقیق تا ده‌ها دور «برابر» نمی‌شد. اینجا مقدار واقعیِ هر کالا
        /// نگه داشته می‌شود تا بیشترین تغییر محاسبه و با یک آستانه
        /// مقایسه شود (نگاه کنید MaxAbsDelta).
        /// </summary>
        private static async Task<Dictionary<long, double>> GetItemCostSnapshotAsync(
            IDatabaseService db, int runId)
        {
            var rows = await db.DoGetDataSQLAsync<(long Code, double TotalCost)>(
                "SELECT Code, TotalCost FROM dbo.CC_ItemCost WHERE RunId = @runId",
                new { runId });

            var map = new Dictionary<long, double>();
            foreach (var r in rows) map[r.Code] = r.TotalCost;
            return map;
        }

        /// <summary>
        /// بیشترین قدرمطلقِ تغییرِ نرخ روی هر کالا بین دو عکسِ پیاپی، به‌همراه
        /// کدِ همان کالای مقصر — تا وقتی همگرایی کند است یا به سقفِ دور
        /// می‌خورد، مستقیم در CC_RunLog معلوم باشد کدام زنجیره مسئول است،
        /// نه فقط عددِ خامِ بیشترین تغییر (که قبلاً هیچ ردی از این‌که کدام
        /// کالا بود نمی‌گذاشت — برای تشخیصش باید دستی اسنپ‌شات‌های هر دور
        /// را که اصلاً جایی ذخیره نمی‌شوند بازسازی می‌کردیم).
        ///
        /// کالایی که فقط در یکی از دو عکس هست (مثلاً بین این دور تازه به
        /// مجموعه اضافه/حذف شده) هم به همان اندازه‌ی خودش تغییر حساب
        /// می‌شود، نه نادیده گرفته می‌شود — یک کالای گم‌شده نباید بی‌صدا از
        /// چشمِ آستانه‌ی همگرایی رد شود.
        /// </summary>
        private static (double Max, long? Code) MaxAbsDelta(
            Dictionary<long, double> prev, Dictionary<long, double> curr)
        {
            double max = 0;
            long? maxCode = null;
            foreach (var code in prev.Keys.Union(curr.Keys))
            {
                var p = prev.TryGetValue(code, out var pv) ? pv : 0;
                var c = curr.TryGetValue(code, out var cv) ? cv : 0;
                var d = Math.Abs(c - p);
                if (d > max) { max = d; maxCode = code; }
            }
            return (max, maxCode);
        }

        /// <summary>«کد — نام» برای پیام لاگ؛ فقط وقتی همگرایی کند/ناقص است
        /// صدا زده می‌شود (نه هر دور)، پس هزینه‌ی یک کوئریِ اضافه ناچیز
        /// است. نبودِ نام (کالای حذف‌شده/نامعتبر) را بی‌سروصدا به خودِ کد
        /// برمی‌گرداند، چون این فقط برای خواناییِ لاگ است، نه منطق.</summary>
        private static async Task<string> DescribeItemAsync(IDatabaseService db, long? code)
        {
            if (code is null) return "؟";
            try
            {
                var name = await db.DoGetDataSQLAsyncSingle<string>(
                    "SELECT NAME FROM dbo.STUF_DEF WHERE TRY_CAST(CODE AS BIGINT) = @code",
                    new { code = code.Value });
                return string.IsNullOrWhiteSpace(name) ? code.Value.ToString() : $"{code} ({name})";
            }
            catch
            {
                return code.Value.ToString();
            }
        }
    }
}
