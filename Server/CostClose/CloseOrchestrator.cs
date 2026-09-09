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

        /// <summary>
        /// این اجرا فقط باید کالاهایی را بازسازی کند که نرخشان می‌تواند از
        /// دور قبل عوض شده باشد (کالاهای فرمول‌دار)، نه کل کاردکس.
        ///
        /// چرا درست است: بین دورهای حلقه‌ی همگرایی S07A↔S11، گام S07 دیگر
        /// اجرا نمی‌شود، پس هیچ ردیف خروجی تازه‌ای با نرخ موقتِ ۱ ساخته
        /// نمی‌شود. تنها چیزی که بین دو دور تکان می‌خورد بهای رسیدهای تولید
        /// است، و آن فقط برای کالایی معنا دارد که فرمول دارد. میانگین یک
        /// مادهٔ خریدنی بعد از پاس اول ثابت است.
        ///
        /// اندازه‌گیریِ اجرای ۷: S07A هر ۱۸۳ بار کل ۸۱۹ کالا و ۸۷٬۷۲۶ ردیف
        /// را از نو ساخت، درحالی‌که فقط ~۱۰۷ کالا فرمول دارند — یعنی حدود
        /// ۸۷٪ کار هر پاس روی کالاهایی بود که اصلاً نمی‌توانستند عوض شوند.
        ///
        /// فقط گام‌هایی که خودشان دامنه‌ی کار را از کالا می‌گیرند به این نگاه
        /// می‌کنند؛ بقیه بی‌تفاوت‌اند. نگاه کنید S07A_RebuildAverageRate.
        /// </summary>
        public bool NarrowToFormulaItems { get; init; }
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

        public async Task RunAsync(CostCloseJob job, CancellationToken appToken)
        {
            // توکنِ ورودی، توکنِ خاموش شدنِ برنامه است. گام‌ها باید توکنی
            // بگیرند که کلیدِ توقفِ کاربر هم آن را لغو کند — وگرنه گامی مثل
            // S07A که خودش ct را چک می‌کند (و تا ۷۳ ثانیه طول می‌کشد) لغو را
            // نمی‌بیند و دکمه‌ی توقف بی‌اثر به‌نظر می‌رسد.
            using var runCts = _queue.RegisterRun(job.RunId, appToken);
            var ct = runCts.Token;

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
                //
                // ⚠️ اصلاح (ران ۱۰/ماه ۵، کد ۳۳۶۵): آستانه‌ي مطلقِ تنها کافي
                // نيست. وقتي خودِ نرخ بزرگ است، «يک ريال» يک آستانه نيست،
                // يک آرزوست: روي مقدارِ ۱e15 هر دور فقط ۳۵٪ فاصله را
                // مي‌بندد، پس رسيدن به زيرِ يک ريال ~۷۷ دور مي‌خواهد و سقفِ
                // حلقه ۴۰ است. نتيجه: ۴۰ دورِ ۱۱ ثانيه‌اي (~۷ دقيقه) سوخت
                // و اجرا با همان عددِ خراب تمام شد.
                // پس هر کالا با هر دو معيار سنجيده مي‌شود و *يکي* کافي است:
                // يا تغييرش زير يک ريال است، يا زير يک‌ميليونيمِ خودِ نرخ.
                // يک‌ميليونيم روي نرخِ پنج‌ميليوني مي‌شود پنج ريال — از هر
                // آستانه‌ي بااهميتِ حسابداري خيلي ريزتر.
                const double RateConvergeThreshold = 1.0;
                const double RateConvergeRelative  = 1e-6;
                Dictionary<long, double>? lastRates = null;
                int    s11Cycles          = 0;

                // بيشترين تغييرِ دورِ قبل — براي تشخيصِ «دارد کار مي‌کند» از
                // «دارد جا مي‌زند». حلقه‌اي که فاصله‌اش آب نمي‌رود، با ۴۰ دور
                // هم آب نمي‌رود؛ زودتر ايستادن و گفتنِ اينکه کدام کالا
                // مقصر است، از هفت دقيقه انتظارِ بي‌نتيجه بهتر است.
                double? prevWorst   = null;
                int     stalledRows = 0;
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

                // سه دور، نه یکی: تعداد کالاهایی که هر دور به‌روز می‌شوند
                // نوسان دارد، و یک دورِ تخت می‌تواند اتفاقی باشد. سه دور
                // پشت‌سرهم دیگر اتفاق نیست.
                const int StallRounds     = 3;

                // ── دامنه‌ی S07A ──
                // پاس اول (و هر پاس بعد از یک بازتولیدِ تازه‌ی S07) باید کل
                // کاردکس را بسازد، چون S07 ردیف‌های خروج مواد را با نرخ موقتِ
                // ۱ می‌گذارد و آن ردیف‌ها روی کدِ *ماده* هستند، نه کالای
                // فرمول‌دار. پاس‌های بعدیِ داخلِ حلقه فقط کالاهای فرمول‌دار را
                // لازم دارند — نگاه کنید StepContext.NarrowToFormulaItems.
                bool s07aNeedsFullPass = true;

                // بعد از همگرایی یک پاس کاملِ دیگر اجرا می‌شود تا ثابت کند
                // باریک کردنِ دامنه چیزی را جا نینداخته: اگر آن پاس نرخی را
                // بیش از آستانه تکان دهد، حلقه خودش ادامه پیدا می‌کند و پیام
                // هشدار ثبت می‌شود.
                bool s07aVerifyPassDone = false;

                // اگر پاس تأییدی ثابت کند دامنه‌ی باریک ناقص بوده، از آن به بعد
                // همه‌ی پاس‌ها کامل اجرا می‌شوند — درستیِ بها بر سرعت مقدم است.
                bool s07aScopeUntrusted = false;

                // آیا اصلاً پاسی با دامنه‌ی باریک اجرا شد؟ اگر نه، پاس تأییدی
                // چیزی برای تأیید ندارد و بی‌جهت ۱۵ ثانیه به اجرا اضافه می‌کند.
                bool s07aNarrowUsed = false;

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
                        NarrowToFormulaItems = !s07aNeedsFullPass && !s07aScopeUntrusted,
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

                    // یک پاس کاملِ S07A انجام شد؛ تا وقتی S07 دوباره اجرا نشود
                    // پاس‌های بعدی می‌توانند باریک باشند.
                    if (step.StepCode == "S07A" && result.Status != CostStepStatus.Failed)
                    {
                        if (ctx.NarrowToFormulaItems) s07aNarrowUsed = true;
                        s07aNeedsFullPass = false;
                    }

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
                        // S07 دوباره خواهد آمد و ردیف‌های خروج مواد را با نرخ
                        // موقتِ ۱ بازمی‌سازد — آن ردیف‌ها روی کدِ مواد هستند،
                        // پس S07A بعدش باید دوباره کل کاردکس را بسازد.
                        s07aNeedsFullPass = true;

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
                            : MaxUnconvergedDelta(lastRates, rates,
                                                  RateConvergeThreshold, RateConvergeRelative);

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

                        if (maxDeltaRounded is not null && maxDeltaRounded <= RateConvergeThreshold
                            && (s07aVerifyPassDone || !s07aNarrowUsed))
                        {
                            // بیشترین تغییرِ نرخِ همه‌ی کالاها بین این دور و دور
                            // قبل زیر یک ریال است — همگرا شد
                        }
                        else if (maxDeltaRounded is not null && maxDeltaRounded <= RateConvergeThreshold)
                        {
                            // ── پاس تأییدیِ کامل ──
                            // دورهای حلقه با دامنه‌ی باریک (فقط کالاهای فرمول‌دار)
                            // اجرا شدند. یک بار دیگر با دامنه‌ی کامل تکرار می‌شود:
                            // اگر باریک‌سازی چیزی را جا انداخته باشد، نرخ عوض
                            // می‌شود و همین شرط دفعه‌ی بعد رد می‌شود و حلقه ادامه
                            // پیدا می‌کند — یعنی خطا خودش را نشان می‌دهد، نه اینکه
                            // بی‌صدا در بها بنشیند.
                            s07aVerifyPassDone = true;
                            s07aNeedsFullPass  = true;
                            lastRates          = rates;

                            pending = new Queue<ICostStep>(
                                _steps.Where(s => s.StepCode is "S07A" or "S11")
                                      .OrderBy(s => s.SeqNo)
                                      .Concat(pending));

                            await LogAsync(db, job.RunId, "S11", 1,
                                $"همگرایی در دور {s11Cycles + 1} — یک پاس کاملِ S07A برای تأیید اجرا می‌شود");
                        }
                        else if (s11Cycles < MaxS11Cycles && stalledRows < StallRounds)
                        {
                            // ── جا زده یا دارد کار می‌کند؟ ──
                            // میراییِ S11 هر دور ۳۵٪ از فاصله را می‌بندد، پس
                            // یک حلقه‌ی سالم باید هر دور محسوس کوچک‌تر شود.
                            // اگر سه دور پشت‌سرهم تکان نخورَد، دورِ چهل‌ام هم
                            // نمی‌خورَد — فقط ۱۱ ثانیه‌ی دیگر می‌سوزاند.
                            if (delta is not null && prevWorst is not null)
                            {
                                if (delta.Value.Max >= prevWorst.Value * 0.99) stalledRows++;
                                else stalledRows = 0;
                            }
                            prevWorst = delta?.Max;

                            // نرخ بعد از پاس تأییدیِ کامل تکان خورد: یعنی
                            // کالایی بیرون از «فرمول‌دارها» هم عوض می‌شده و
                            // دامنه‌ی باریک آن را جا انداخته بود.
                            if (s07aVerifyPassDone && !s07aScopeUntrusted)
                            {
                                s07aScopeUntrusted = true;
                                await LogAsync(db, job.RunId, "S07A", 2,
                                    $"پاس تأییدیِ کامل نرخ‌ها را {delta?.Max:N0} ریال تکان داد — از این پس S07A با دامنه‌ی کامل اجرا می‌شود");
                            }

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

                            // چرا متوقف شد فرق می‌کند: «به سقف خوردیم» یعنی
                            // کُند بود، «جا زد» یعنی داده‌ی این کالا خراب است
                            // و دور بیشتر درستش نمی‌کند. کاربر باید بداند کدام.
                            var why = stalledRows >= StallRounds
                                ? $"فاصله‌ی نرخ در {StallRounds} دور پیاپی کوچک نشد — تکرارِ بیشتر جوابش نیست"
                                : $"همگرایی نرخ پس از {MaxS11Cycles} دور تکرار کامل نشد";

                            await LogAsync(db, job.RunId, "S11", 2,
                                $"{why} (بیشترین تغییر هنوز {delta?.Max:N0} ریال روی کالای {worstItem}) — با آخرین مقدار ادامه داده می‌شود");
                        }
                    }
                }

                // لغو فقط در ابتدای حلقه چک می‌شود، پس اگر کاربر وسطِ *آخرین*
                // گام دکمه‌ی توقف را بزند، حلقه عادی تمام می‌شود و اجرا
                // «تکمیل» علامت می‌خورد — یعنی درخواست او بی‌صدا نادیده
                // گرفته می‌شود و وضعیتِ گزارش‌شده هم غلط است. اینجا یک بار
                // دیگر چک می‌کنیم تا «توقف» همیشه «توقف» بماند.
                if (ct.IsCancellationRequested || _queue.IsCancelRequested(job.RunId))
                {
                    await LogAsync(db, job.RunId, null, 2, "اجرا توسط کاربر متوقف شد");
                    await SetRunStatusAsync(db, job.RunId, CostRunStatus.Paused);
                    await _notify.RunPausedAsync(job.RunId, "cancelled");
                    return;
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
        /// <remarks>
        /// کالاهایی که *همگرا شده‌اند* اصلاً وارد بیشترین‌گیری نمی‌شوند.
        /// یک کالا همگراست اگر تغییرش زیر یک ریال باشد، یا زیر یک‌ میلیونیمِ
        /// خودِ نرخ. معیار دوم همان چیزی است که آستانه‌ی مطلق کم دارد: روی
        /// نرخِ بزرگ، «یک ریال» دقتی است که خودِ FLOAT هم تضمینش نمی‌کند،
        /// چه رسد به یک زنجیره‌ی چندسطحیِ بازمحاسبه‌شونده.
        ///
        /// اگر همه همگرا باشند صفر برمی‌گردد — یعنی همان معنای قبلیِ
        /// «هیچ تغییرِ معناداری نمانده».
        /// </remarks>
        private static (double Max, long? Code) MaxUnconvergedDelta(
            Dictionary<long, double> prev, Dictionary<long, double> curr,
            double absTol, double relTol)
        {
            double max = 0;
            long? maxCode = null;
            foreach (var code in prev.Keys.Union(curr.Keys))
            {
                var p = prev.TryGetValue(code, out var pv) ? pv : 0;
                var c = curr.TryGetValue(code, out var cv) ? cv : 0;
                var d = Math.Abs(c - p);

                if (d <= absTol) continue;

                var scale = Math.Max(Math.Abs(p), Math.Abs(c));
                if (d <= scale * relTol) continue;

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
