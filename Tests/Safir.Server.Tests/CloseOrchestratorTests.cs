using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.CostClose;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// تست‌های رگرسیون برای CloseOrchestrator.
///
/// هر تست اینجا یک تصمیمِ مستندشده در کامنت‌های خودِ ارکستریتور را قفل
/// می‌کند — تصمیم‌هایی که هرکدام از یک مغایرت واقعی روی یک ران واقعی
/// بیرون آمده‌اند (کدهای ۳۳۶۵، ۳۱۰۰، ۷۱، ۳۵۱۴). بدون این تست‌ها تنها
/// راهِ فهمیدنِ شکستنِ آن‌ها، اجرای دستیِ یک بستنِ ماه کامل است.
/// </summary>
public sealed class CloseOrchestratorTests
{
    // ═══════════════════════════════════════════════════════════════
    //  همگرایی نرخ S07A↔S11
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Rates_within_one_rial_are_treated_as_converged()
    {
        var db = new CostCloseFakeDatabase();
        // دور اول و دوم یکسان → بیشترین تغییر صفر
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });

        var (s07a, s11) = (Step("S07A", 75), Step("S11", 110));
        await RunAsync(db, s07a, s11);

        // یک دورِ اضافه ذاتیِ الگوریتم است (دور اول مبنایی برای مقایسه
        // ندارد)، و یک پاس تأییدیِ کامل چون دورِ دوم با دامنه‌ی باریک اجرا
        // شده. بیشتر از این نباید تکرار شود.
        //
        // ⚠️ چرا تأییدی حذف‌شدنی نیست: مقایسه‌ی همگرایی روی CC_ItemCost است
        // که فقط کالاهای فرمول‌دار را دارد — همان‌هایی که دامنه‌ی باریک نگه
        // می‌دارد. اگر باریک‌سازی کالایی *بیرونِ* آن مجموعه را جا بیندازد،
        // این مقایسه هرگز آن را نمی‌بیند. تنها یک پاس کامل نشانش می‌دهد.
        Assert.Equal(3, s11.Runs);
        Assert.Equal(3, s07a.Runs);
        Assert.Contains(CostRunStatus.Completed, db.StatusWrites);
    }

    /// <summary>
    /// رگرسیون کامیت 30f0b7b («round rate-delta before comparing to
    /// convergence threshold»، کشف‌شده روی کد ۳۵۱۴/پودر whey).
    ///
    /// اختلافِ ۱٫۰۰۰۰۰۰۴ ریال از نظر مالی همگراست (ریال کوچک‌ترین واحد
    /// پول است)، ولی مقایسه‌ی خامِ `<= 1.0` ردش می‌کرد و اجرا بی‌دلیل تا
    /// سقفِ دورها ادامه پیدا می‌کرد.
    /// </summary>
    [Fact]
    public async Task Sub_rial_fraction_above_threshold_still_counts_as_converged()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3514] = 0 });
        db.RateSnapshots.Enqueue(new() { [3514] = 1.0000004 });

        var s11 = Step("S11", 110);
        await RunAsync(db, Step("S07A", 75), s11);

        // بدون گردکردن، این عدد «همگرا» شمرده نمی‌شد و S11 بارها
        // بیشتر اجرا می‌شد. سومی پاس تأییدیِ دامنه است، نه دورِ همگرایی.
        Assert.Equal(3, s11.Runs);
    }

    [Fact]
    public async Task Rates_differing_by_more_than_a_rial_trigger_another_cycle()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1100 });   // ۱۰۰ ریال فاصله
        db.RateSnapshots.Enqueue(new() { [3365] = 1100 });   // همگرا

        var (s07a, s11) = (Step("S07A", 75), Step("S11", 110));
        await RunAsync(db, s07a, s11);

        // ۳ دور تا همگرایی + ۱ پاس تأییدیِ کاملِ S07A بعد از آن
        // (نگاه کنید Full_verification_pass_runs_once_after_convergence).
        Assert.Equal(4, s11.Runs);
        Assert.Equal(4, s07a.Runs);
    }

    /// <summary>
    /// فاصله‌ای که کوچک نمی‌شود، با دورِ بیشتر هم کوچک نمی‌شود.
    ///
    /// ⚠️ این تست قبلاً «دقیقاً ۴۱ اجرای S11» را انتظار داشت: هر فاصله‌ی
    /// پابرجا تا سقفِ ۴۰ دور ادامه پیدا می‌کرد. روی یک گامِ ۱۱ ثانیه‌ای
    /// یعنی هفت دقیقه انتظار برای نتیجه‌ای که از دورِ سوم معلوم بود.
    ///
    /// حالا وقتی بیشترین فاصله سه دورِ پیاپی آب نرود، همان‌جا می‌ایستد و
    /// می‌گوید کدام کالا نگهش داشته. سقفِ ۴۰ سرِ جایش است و برای حالتِ
    /// دیگری است — نگاه کنید تستِ بعدی.
    /// </summary>
    [Fact]
    public async Task Rates_that_never_shrink_stop_early_instead_of_burning_the_whole_budget()
    {
        var db = new CostCloseFakeDatabase
        {
            // هر دور ۱۰۰ ریال دورتر — فاصله ثابت می‌ماند، هرگز آب نمی‌رود
            RateGenerator = read => new Dictionary<long, double> { [71] = read * 100.0 }
        };

        var s11 = Step("S11", 110);
        await RunAsync(db, Step("S07A", 75), s11);

        // خیلی کمتر از سقف، ولی به‌اندازه‌ای که «ثابت بودن» ثابت شود
        Assert.InRange(s11.Runs, 4, 10);

        Assert.Contains(db.Logs, l => l.Severity == 2 && l.Message.Contains("دور پیاپی کوچک نشد"));
        // نرسیدن به همگرایی نباید اجرا را شکست بدهد — «با آخرین مقدار ادامه»
        Assert.Contains(CostRunStatus.Completed, db.StatusWrites);
    }

    /// <summary>
    /// رگرسیون کامیت 53e99f4 («raise S11 cycle budget to 40»، کشف‌شده روی
    /// RunId=6/اردیبهشت با کد ۷۱ «نایلون»). سقف باید ۴۰ بماند: با ۲۵،
    /// کالاهایی که فاصله‌ی اولشان بزرگ‌تر بود به سقف می‌خوردند بدون رسیدن
    /// به زیر آستانه و در CHK-09 گیر می‌افتادند.
    ///
    /// اینجا فاصله هر دور آب می‌رود ولی خیلی کند (۱٪)، پس تشخیصِ «جا زدن»
    /// فعال نمی‌شود و سقف باید کارش را بکند — همان حالتی که آن کامیت
    /// برایش سقف را از ۲۵ به ۴۰ برد.
    /// </summary>
    [Fact]
    public async Task Slowly_shrinking_rates_still_use_the_full_forty_cycle_budget()
    {
        var db = new CostCloseFakeDatabase
        {
            // مقدار به ۱۰۰۰ نزدیک می‌شود، هر دور فقط ۱٪ از فاصله‌ی باقیمانده
            RateGenerator = read =>
                new Dictionary<long, double> { [71] = 1000.0 - 900.0 * Math.Pow(0.99, read) }
        };

        var s11 = Step("S11", 110);
        await RunAsync(db, Step("S07A", 75), s11);

        Assert.Equal(41, s11.Runs);
        Assert.Contains(db.Logs, l => l.Severity == 2 && l.Message.Contains("همگرایی نرخ پس از 40 دور"));
        Assert.Contains(CostRunStatus.Completed, db.StatusWrites);
    }

    /// <summary>
    /// دورهای تکرار باید به **جلوی** صف بروند، نه انتهای آن — وگرنه S12
    /// (سود و زیان) وسط همگرایی اجرا می‌شود و نرخِ یک نقطه‌ی میانی را
    /// قفل می‌کند. همان مغایرت ۹.۷ میلیاردی کد ۳۳۶۵/انبار۲.
    /// </summary>
    [Fact]
    public async Task Convergence_cycles_run_before_later_steps_not_after()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1100 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1100 });

        var order = new List<string>();
        var steps = new[] { Step("S07A", 75, order), Step("S11", 110, order), Step("S12", 120, order) };

        await RunAsync(db, steps);

        Assert.Equal(
            new[] { "S07A", "S11", "S07A", "S11", "S07A", "S11", "S07A", "S11", "S12" },
            order);
    }

    // ═══════════════════════════════════════════════════════════════
    //  دامنه‌ی S07A داخل حلقه‌ی همگرایی
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// اندازه‌گیری روی اجرای ۷: S07A هر ۱۸۳ بار کل ۸۱۹ کالا و ۸۷٬۷۲۶ ردیف
    /// کاردکس را بازسازی کرد — ۴۴ دقیقه از ۶۰ دقیقه‌ی کل اجرا — درحالی‌که
    /// فقط ~۱۰۷ کالا فرمول دارند و تنها همان‌ها می‌توانند بین دو دور عوض
    /// شوند (بین دورها S07 اجرا نمی‌شود، پس ردیف خروجیِ تازه‌ای با نرخ
    /// موقتِ ۱ ساخته نمی‌شود).
    ///
    /// پاس اول باید کامل بماند، دورهای بعدی باریک.
    /// </summary>
    [Fact]
    public async Task First_S07A_pass_is_full_and_later_convergence_passes_are_narrow()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1100 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1100 });

        var scopes = new List<bool>();
        var s07a = Step("S07A", 75, onExecute: ctx => scopes.Add(ctx.NarrowToFormulaItems));

        await RunAsync(db, s07a, Step("S11", 110));

        // پاس اول کامل، دو دورِ میانی باریک، پاس تأییدیِ آخر دوباره کامل
        Assert.Equal(new[] { false, true, true, false }, scopes);
    }

    /// <summary>
    /// بعد از هر بازتولیدِ S07 (که ردیف‌های خروج مواد را با نرخ موقتِ ۱
    /// می‌سازد، روی کدِ *مواد* نه کالای فرمول‌دار)، S07A دوباره باید کل
    /// کاردکس را بسازد. باریک ماندن اینجا یعنی ردیف‌های خروج با نرخ ۱ باقی
    /// می‌مانند و S08 انحراف را روی نرخ جعلی حساب می‌کند.
    /// </summary>
    [Fact]
    public async Task S07A_goes_full_again_after_a_formula_change_rebuild()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });

        var scopes = new List<bool>();
        var s07a = Step("S07A", 75, onExecute: ctx => scopes.Add(ctx.NarrowToFormulaItems));

        await RunAsync(db,
            s07a,
            Step("S11", 110),
            Step("S09", 90, writesFormulas: true),
            Step("S07", 70),
            Step("S08", 80));

        // هیچ پاسی بعد از یک بازتولید نباید باریک اجرا شود
        Assert.NotEmpty(scopes);
        Assert.False(scopes[0]);
        Assert.Contains(false, scopes.Skip(1));
    }

    /// <summary>
    /// پاس تأییدی تنها چیزی است که ثابت می‌کند باریک‌سازی چیزی را جا
    /// نینداخته. اگر آن پاس نرخی را بیش از آستانه تکان دهد، حلقه ادامه
    /// می‌دهد و از آن به بعد همه‌ی پاس‌ها کامل می‌شوند.
    /// </summary>
    [Fact]
    public async Task Verification_pass_that_moves_rates_widens_the_scope_and_warns()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });   // همگرا → پاس تأییدی
        db.RateSnapshots.Enqueue(new() { [3365] = 5000 });   // تأییدی نرخ را تکان داد
        db.RateSnapshots.Enqueue(new() { [3365] = 5000 });   // دوباره همگرا

        var scopes = new List<bool>();
        var s07a = Step("S07A", 75, onExecute: ctx => scopes.Add(ctx.NarrowToFormulaItems));

        await RunAsync(db, s07a, Step("S11", 110));

        Assert.Contains(db.Logs, l => l.Severity == 2
                                   && l.Message.Contains("دامنه‌ی کامل"));
        // بعد از کشفِ جاافتادگی هیچ پاسی دیگر نباید باریک باشد
        Assert.DoesNotContain(true, scopes.Skip(2));
        Assert.Contains(CostRunStatus.Completed, db.StatusWrites);
    }

    /// <summary>
    /// بازسازی نرخ میانگین بدون انتشار نرخ، «بازسازی» را نصفه می‌گذارد:
    /// نرخ‌های تازه هرگز به فرمول‌ها نمی‌رسند. پس انتخابِ تنهای S07A از
    /// دیالوگ «اجرای مجدد گام‌ها» باید خودش S11 را هم بیاورد.
    /// </summary>
    [Fact]
    public async Task Selecting_S07A_alone_pulls_in_S11()
    {
        var db = new CostCloseFakeDatabase();
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });
        db.RateSnapshots.Enqueue(new() { [3365] = 1000 });

        var (s07a, s11, s12) = (Step("S07A", 75), Step("S11", 110), Step("S12", 120));
        await RunAsync(db, new[] { s07a, s11, s12 }, onlySteps: new[] { "S07A" });

        Assert.True(s11.Runs > 0, "انتخاب S07A باید S11 را هم اجرا کند");
        Assert.Equal(0, s12.Runs);   // بقیه‌ی گام‌ها نباید وارد شوند
    }

    // ═══════════════════════════════════════════════════════════════
    //  بازتولید ناشی از تغییر فرمول
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// وقتی گامی فرمول‌ها را عوض می‌کند، خروج مواد و انحراف باید بازسازی
    /// شوند — ولی S07B (تخصیص دستمزد) عمداً نه: روی داده‌ی دستیِ کاربر
    /// کار می‌کند و اجرای خودکارِ بی‌اطلاعِ او می‌تواند فرمول‌ها را با
    /// ضریب‌های هنوز کامل‌نشده به‌روز کند.
    /// </summary>
    [Fact]
    public async Task Formula_change_rebuilds_issue_and_variance_but_never_S07B()
    {
        var db = new CostCloseFakeDatabase();

        var s07  = Step("S07", 70);
        var s07b = Step("S07B", 72, autoRun: false);
        var s08  = Step("S08", 80);
        var s09  = Step("S09", 90, writesFormulas: true);

        await RunAsync(db, s07, s07b, s08, s09);

        Assert.Equal(2, s07.Runs);   // یک‌بار عادی + یک‌بار بازتولید
        Assert.Equal(2, s08.Runs);
        Assert.Equal(0, s07b.Runs);  // ← نکته‌ی اصلی این تست

        Assert.Contains("dbo.CC_sp_SetFormulasDirty", db.ProcedureCalls);
    }

    [Fact]
    public async Task Steps_with_AutoRun_false_are_skipped_unless_explicitly_selected()
    {
        var db = new CostCloseFakeDatabase();
        var s07b = Step("S07B", 72, autoRun: false);

        await RunAsync(db, s07b);
        Assert.Equal(0, s07b.Runs);

        var again = Step("S07B", 72, autoRun: false);
        await RunAsync(new CostCloseFakeDatabase(), new[] { again }, onlySteps: new[] { "S07B" });
        Assert.Equal(1, again.Runs);
    }

    // ═══════════════════════════════════════════════════════════════
    //  دروازه و خطا
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Closed_gate_pauses_the_run_and_stops_later_steps()
    {
        var db = new CostCloseFakeDatabase();
        var gate  = Step("S05", 50, isGate: true, status: CostStepStatus.Warning);
        var later = Step("S07", 70);

        await RunAsync(db, gate, later);

        Assert.Equal(1, gate.Runs);
        Assert.Equal(0, later.Runs);
        Assert.Contains(CostRunStatus.Paused, db.StatusWrites);
        Assert.DoesNotContain(CostRunStatus.Completed, db.StatusWrites);
    }

    [Fact]
    public async Task Failed_step_marks_the_run_failed()
    {
        var db = new CostCloseFakeDatabase();
        var bad   = Step("S07", 70, status: CostStepStatus.Failed);
        var later = Step("S08", 80);

        await RunAsync(db, bad, later);

        Assert.Equal(0, later.Runs);
        Assert.Contains(CostRunStatus.Failed, db.StatusWrites);
    }

    /// <summary>
    /// رگرسیون برای try/catch بیرونی: CC_sp_StepStart بیرونِ try داخلی
    /// صدا زده می‌شود. اگر خطا بدهد و گرفته نشود، CC_Run.Status برای همیشه
    /// روی «در حال اجرا» یخ می‌زند و کاربر یک اجرای گیرافتاده می‌بیند
    /// بدون هیچ خطای قابل‌دیدن.
    /// </summary>
    [Fact]
    public async Task Failure_outside_step_handling_still_marks_the_run_failed()
    {
        var db = new CostCloseFakeDatabase();
        db.ThrowOnProcedure.Add("dbo.CC_sp_StepStart");

        await RunAsync(db, Step("S07", 70));

        Assert.Contains(CostRunStatus.Failed, db.StatusWrites);
        Assert.DoesNotContain(CostRunStatus.Completed, db.StatusWrites);
        Assert.Contains(db.Logs, l => l.Severity == 2 && l.Message.Contains("خطای غیرمنتظره"));
    }

    /// <summary>
    /// رگرسیون «کلید توقف کار نمی‌کند» (گزارش کاربر، ۱۴۰۵/۰۶/۰۹).
    ///
    /// ارکستریتور توکنِ خاموش شدنِ برنامه را به گام‌ها می‌داد، و لغوِ کاربر
    /// فقط یک پرچم بود که *بین* گام‌ها خوانده می‌شد. گام‌هایی که خودشان
    /// توکن را چک می‌کنند (S07A / AverageRateRebuildService) هرگز لغو را
    /// نمی‌دیدند و کاربر تا پایان همان گام — روی ران واقعی تا ۷۳ ثانیه —
    /// فکر می‌کرد دکمه خراب است.
    /// </summary>
    [Fact]
    public async Task Stop_button_cancels_the_token_the_running_step_is_holding()
    {
        var db    = new CostCloseFakeDatabase();
        var queue = new FakeQueue();
        bool seenByStep = false;

        var step = Step("S07", 70, onExecute: ctx =>
        {
            // کاربر دقیقاً وسطِ اجرای گام دکمه‌ی توقف را می‌زند
            queue.RequestCancel(db.Run.RunId);
            seenByStep = ctx.Ct.IsCancellationRequested;
        });

        await RunAsync(db, new[] { step }, queue: queue);

        Assert.True(seenByStep,
            "گامِ در حال اجرا باید لغو را روی همان توکنی ببیند که گرفته است");
        Assert.Contains(CostRunStatus.Paused, db.StatusWrites);
    }

    [Fact]
    public async Task Cancellation_pauses_the_run_rather_than_failing_it()
    {
        var db = new CostCloseFakeDatabase();
        var queue = new FakeQueue { CancelRequested = true };

        await RunAsync(db, new[] { Step("S07", 70) }, queue: queue);

        Assert.Contains(CostRunStatus.Paused, db.StatusWrites);
        Assert.Contains(db.Logs, l => l.Message.Contains("متوقف شد"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  داربست
    // ═══════════════════════════════════════════════════════════════

    private static Task RunAsync(CostCloseFakeDatabase db, params FakeStep[] steps)
        => RunAsync(db, steps, null);

    private static async Task RunAsync(
        CostCloseFakeDatabase db,
        IEnumerable<FakeStep> steps,
        string[]? onlySteps = null,
        FakeQueue? queue = null)
    {
        var orchestrator = new CloseOrchestrator(
            steps,
            queue ?? new FakeQueue(),
            new FakeNotifier(),
            new FakeDbFactory(db),
            NullLogger<CloseOrchestrator>.Instance);

        await orchestrator.RunAsync(
            new CostCloseJob(db.Run.RunId, "fake-connection", "tester", onlySteps),
            CancellationToken.None);
    }

    private static FakeStep Step(
        string code,
        short seq,
        List<string>? order = null,
        bool isGate = false,
        bool writesFormulas = false,
        bool autoRun = true,
        CostStepStatus status = CostStepStatus.Success,
        Action<StepContext>? onExecute = null)
        => new(code, seq, order, isGate, writesFormulas, autoRun, status, onExecute);

    private sealed class FakeStep : ICostStep
    {
        private readonly List<string>? _order;
        private readonly CostStepStatus _status;
        private readonly Action<StepContext>? _onExecute;

        public FakeStep(string code, short seq, List<string>? order,
                        bool isGate, bool writesFormulas, bool autoRun,
                        CostStepStatus status, Action<StepContext>? onExecute = null)
        {
            StepCode = code;
            SeqNo = seq;
            _order = order;
            IsGate = isGate;
            WritesFormulas = writesFormulas;
            AutoRun = autoRun;
            _status = status;
            _onExecute = onExecute;
        }

        public string StepCode { get; }
        public string Title => StepCode;
        public short SeqNo { get; }
        public bool RequiresSnapshot => false;
        public bool IsGate { get; }
        public bool WritesFormulas { get; }
        public bool AutoRun { get; }

        public int Runs { get; private set; }

        public Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            Runs++;
            _order?.Add(StepCode);
            _onExecute?.Invoke(ctx);

            // WritesFormulas فقط باید یک‌بار بازتولید راه بیندازد؛ وگرنه
            // بازتولیدِ خودش دوباره بازتولید می‌خواهد و تست تمام نمی‌شود.
            if (WritesFormulas && Runs > 1)
                return Task.FromResult(StepResult.Ok());

            return Task.FromResult(_status switch
            {
                CostStepStatus.Failed  => StepResult.Fail("خطای ساختگی"),
                CostStepStatus.Warning => StepResult.Warn(),
                _                      => StepResult.Ok(),
            });
        }
    }

    private sealed class FakeQueue : ICostCloseQueue
    {
        private CancellationTokenSource? _cts;

        public bool CancelRequested { get; set; }
        public bool TryEnqueue(CostCloseJob job, out string? error) { error = null; return true; }
        public bool IsRunning(int runId) => false;
        public bool IsCancelRequested(int runId) => CancelRequested;

        public void RequestCancel(int runId)
        {
            CancelRequested = true;
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        public CancellationTokenSource RegisterRun(int runId, CancellationToken appToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(appToken);
            if (CancelRequested) _cts.Cancel();
            return _cts;
        }
    }

    private sealed class FakeNotifier : ICostCloseNotifier
    {
        public Task StepProgressAsync(int r, string s, int p, string m) => Task.CompletedTask;
        public Task StepFinishedAsync(int r, string s, byte st) => Task.CompletedTask;
        public Task RunPausedAsync(int r, string reason) => Task.CompletedTask;
        public Task RunFailedAsync(int r, string s, string? e) => Task.CompletedTask;
        public Task RunCompletedAsync(int r) => Task.CompletedTask;
    }

    private sealed class FakeDbFactory : IDatabaseServiceFactory
    {
        private readonly IDatabaseService _db;
        public FakeDbFactory(IDatabaseService db) => _db = db;
        public IDatabaseService Create(string connectionString) => _db;
    }
}
