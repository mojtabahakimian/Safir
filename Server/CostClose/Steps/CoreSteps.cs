using Safir.Shared.Models.CostClose;

namespace Safir.Server.CostClose.Steps
{
    // ═══════════════════════════════════════════════════════════════
    //  گام‌ها
    //
    //  هر گام یک پوسته نازک روی رویه SQL است. منطق در پایگاه داده
    //  می‌ماند تا هم قابل آزمون مستقل باشد و هم اگر روزی رابط عوض
    //  شد، دوباره نوشته نشود.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>S00 — بازبینی ابتدای ماه</summary>
    public sealed class S00_Preflight : ICostStep
    {
        public string StepCode         => "S00";
        public string Title            => "بازبینی ابتدای ماه";
        public short  SeqNo            => 0;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;   // هشداردهنده، نه مسدودکننده
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 20, "بررسی فرمول‌ها و نرخ‌ها…");

            await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S00_Preflight",
                new { Month = ctx.Month, DT1 = ctx.DateFrom, DT2 = ctx.DateTo, RunId = ctx.RunId },
                commandTimeout: 900);

            await ctx.ReportProgress(StepCode, 70, "بررسی برگه‌های بدون فرمول…");

            await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_Chk04_MissingFormula",
                new { Month = ctx.Month, DT1 = ctx.DateFrom, DT2 = ctx.DateTo, RunId = ctx.RunId },
                commandTimeout: 900);

            var counts = await ctx.Db.DoGetDataSQLAsyncSingle<ExceptionCounts>(
                @"SELECT SUM(CASE WHEN Severity = 2 THEN 1 ELSE 0 END) AS Blocking,
                         SUM(CASE WHEN Severity = 1 THEN 1 ELSE 0 END) AS Warning
                  FROM   dbo.CC_Exception
                  WHERE  RunId = @r AND StepCode = 'S00' AND IsResolved = 0",
                new { r = ctx.RunId });

            await ctx.ReportProgress(StepCode, 100, "پایان");

            var blocking = counts?.Blocking ?? 0;
            var warning  = counts?.Warning  ?? 0;
            var res = new { blocking, warning };

            // بدون این پیام، ردیف این گام در صفحه پایش فقط یک آیکون هشدار
            // خالی نشان می‌داد و کاربر نمی‌فهمید چرا — باید تا پایین صفحه
            // اسکرول می‌کرد تا نوار هشدار جداگانه را ببیند.
            return blocking > 0 || warning > 0
                 ? new StepResult(CostStepStatus.Warning, 0, res,
                       $"{blocking} مسدودکننده، {warning} هشدار یافت شد")
                 : StepResult.Ok(0, res);
        }

        private sealed class ExceptionCounts
        {
            public int Blocking { get; set; }
            public int Warning  { get; set; }
        }
    }


    /// <summary>S02 — اسنپ‌شات (خودِ ارکستریتور می‌گیرد؛ این گام فقط ثبت می‌کند)</summary>
    public sealed class S02_Snapshot : ICostStep
    {
        public string StepCode         => "S02";
        public string Title            => "اسنپ‌شات جداول";
        public short  SeqNo            => 20;
        public bool   RequiresSnapshot => true;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            var rows = await ctx.Db.DoGetDataSQLAsyncSingle<int?>(
                @"SELECT SUM(RowsCopied) FROM dbo.CC_Snapshot
                  WHERE RunId = @r AND StepCode = 'S02'",
                new { r = ctx.RunId });

            return StepResult.Ok(rows ?? 0);
        }
    }


    /// <summary>S03 — حذف اسناد حسابداری خالی</summary>
    public sealed class S03_DeleteEmptyDeeds : ICostStep
    {
        public string StepCode         => "S03";
        public string Title            => "حذف اسناد خالی";
        public short  SeqNo            => 30;
        public bool   RequiresSnapshot => false;   // S02 قبلاً گرفته
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 30, "یافتن اسناد بدون ردیف…");

            var res = await ctx.Db.DoGetStoreProcedureSQLAsync<CountResult>(
                "dbo.CC_sp_S03_DeleteEmptyDeeds",
                new { RunId = ctx.RunId, DT1 = ctx.DateFrom,
                      DT2 = ctx.DateTo, WhatIf = false },
                commandTimeout: 600);

            var n = res.LastOrDefault()?.Value ?? 0;

            await ctx.ReportProgress(StepCode, 100, $"{n} سند حذف شد");
            return StepResult.Ok(n, new { deleted = n });
        }
    }


    /// <summary>S04 — مرتب‌سازی اسناد</summary>
    public sealed class S04_SortDeeds : ICostStep
    {
        public string StepCode         => "S04";
        public string Title            => "مرتب‌سازی اسناد";
        public short  SeqNo            => 40;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => false;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 20, "بازشماره‌گذاری اسناد بازه…");

            var res = await ctx.Db.DoGetStoreProcedureSQLAsync<CountResult>(
                "dbo.CC_sp_S04_SortDeeds",
                new { RunId = ctx.RunId, DT1 = ctx.DateFrom, DT2 = ctx.DateTo,
                      WholeYear = false, WhatIf = false },
                commandTimeout: 1800);

            var n = res.LastOrDefault()?.Value ?? 0;

            await ctx.ReportProgress(StepCode, 100, $"{n} سند بازشماره شد");
            return StepResult.Ok(n, new { renumbered = n });
        }
    }


    /// <summary>
    /// S05 — دروازه اعتبارسنجی.
    /// تنها گامی که اجرا را متوقف می‌کند تا کاربر مغایرت‌ها را رفع کند.
    /// </summary>
    public sealed class S05_Gate : ICostStep
    {
        public string StepCode         => "S05";
        public string Title            => "دروازه اعتبارسنجی";
        public short  SeqNo            => 50;
        public bool   RequiresSnapshot => false;
        public bool   IsGate           => true;
        public bool   WritesFormulas   => false;

        public async Task<StepResult> ExecuteAsync(StepContext ctx)
        {
            await ctx.ReportProgress(StepCode, 20, "بررسی کاردکس منفی…");

            await ctx.Db.DoGetStoreProcedureSQLAsync<dynamic>(
                "dbo.CC_sp_S05_Gate",
                new { RunId = ctx.RunId, Month = ctx.Month,
                      DT1 = ctx.DateFrom, DT2 = ctx.DateTo },
                commandTimeout: 1800);

            // ⚠️ «مسدودکننده» از CC_CheckRule.IsBlocking می‌آید، نه از
            // Severity. قبلاً هر استثنای Severity=2 اجرا را می‌خواباند —
            // ده قاعده این شدت را دارند و روی یک ماه واقعی یعنی توقف پشتِ
            // CHK-02 با ۲۲۳ مورد و CHK-17 با ۴۸ مورد.
            //
            // ولی CHK-02 در گامِ ششمِ کارِ صاحب پروژه رفع می‌شود، بعد از
            // صدور اسناد گروهی؛ تا آن اسناد نباشند مغایرتِ کاردکس و
            // حسابداری اصلاً معنا ندارد. ایستادن پشتش، جلوی همان کاری را
            // می‌گرفت که قرار بود رفعش کند.
            //
            // قاعده‌ی او: کاردکس منفی متوقف کند، بقیه فقط گزارش شوند.
            // تنها همان است که *محاسبه* را خراب می‌کند (تقسیم میانگین
            // متحرک بر مقدار منفی)، نه اینکه صرفاً وضعیت را نشان دهد.
            //
            // نگاه کنید 33-gate-blocking-rules.sql برای اینکه چرا ستونِ
            // جدا و نه خودِ Severity.
            var counts = await ctx.Db.DoGetDataSQLAsyncSingle<GateCounts>(
                @"SELECT SUM(CASE WHEN ISNULL(r.IsBlocking, 0) = 1 THEN 1 ELSE 0 END) AS Blocking,
                         SUM(CASE WHEN ISNULL(r.IsBlocking, 0) = 0 THEN 1 ELSE 0 END) AS Warning
                  FROM   dbo.CC_Exception e
                  LEFT   JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
                  WHERE  e.RunId = @r AND e.IsResolved = 0",
                new { r = ctx.RunId });

            var blocking = counts?.Blocking ?? 0;
            var warning  = counts?.Warning  ?? 0;

            await ctx.ReportProgress(StepCode, 100,
                blocking > 0 ? $"{blocking} مورد مسدودکننده" : "دروازه باز است");

            var payload = new { blocking, warning };

            // شکست دروازه = توقف pipeline (ارکستریتور خودش می‌فهمد).
            // پیام صریح لازم است چون بدون آن ردیف «دروازه اعتبارسنجی» در
            // صفحه پایش فقط یک آیکون هشدار خالی نشان می‌داد؛ کاربر می‌پرسید
            // «چرا اجرا اینجا متوقف شد؟» بدون اینکه ردیف خودش جواب بدهد.
            // پیام صریح می‌گوید چه چیزی متوقف کرده، چون حالا فقط یک چیز
            // می‌تواند: کاردکس منفی. بدون این، کاربر دنبال ۲۲۳ مورد CHK-02
            // می‌گشت که اصلاً جلوی کار را نگرفته‌اند.
            return blocking > 0
                 ? new StepResult(CostStepStatus.Warning, 0, payload,
                       $"{blocking} مورد کاردکس منفی — اول این‌ها را رفع کنید، سپس «ادامه اجرا» را بزنید" +
                       (warning > 0 ? $" ({warning} مورد دیگر فقط هشدارند و جلوی اجرا را نمی‌گیرند)" : ""))
                 : StepResult.Ok(0, payload);
        }

        private sealed class GateCounts
        {
            public int Blocking { get; set; }
            public int Warning  { get; set; }
        }
    }


    internal sealed class CountResult
    {
        public int Value { get; set; }
    }
}
