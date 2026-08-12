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

            var res = new { blocking = counts?.Blocking ?? 0, warning = counts?.Warning ?? 0 };

            return (counts?.Blocking ?? 0) > 0 || (counts?.Warning ?? 0) > 0
                 ? StepResult.Warn(0, res)
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

            var res = await ctx.Db.DoGetDataSQLAsync<CountResult>(
                "EXEC dbo.CC_sp_S03_DeleteEmptyDeeds @RunId=@r, @DT1=@a, @DT2=@b, @WhatIf=0",
                new { r = ctx.RunId, a = ctx.DateFrom, b = ctx.DateTo });

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

            var res = await ctx.Db.DoGetDataSQLAsync<CountResult>(
                "EXEC dbo.CC_sp_S04_SortDeeds @RunId=@r, @DT1=@a, @DT2=@b, " +
                "@WholeYear=0, @WhatIf=0",
                new { r = ctx.RunId, a = ctx.DateFrom, b = ctx.DateTo });

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

            var counts = await ctx.Db.DoGetDataSQLAsyncSingle<GateCounts>(
                @"SELECT SUM(CASE WHEN Severity = 2 THEN 1 ELSE 0 END) AS Blocking,
                         SUM(CASE WHEN Severity = 1 THEN 1 ELSE 0 END) AS Warning
                  FROM   dbo.CC_Exception
                  WHERE  RunId = @r AND IsResolved = 0",
                new { r = ctx.RunId });

            var blocking = counts?.Blocking ?? 0;
            var warning  = counts?.Warning  ?? 0;

            await ctx.ReportProgress(StepCode, 100,
                blocking > 0 ? $"{blocking} مورد مسدودکننده" : "دروازه باز است");

            var payload = new { blocking, warning };

            // شکست دروازه = توقف pipeline (ارکستریتور خودش می‌فهمد)
            return blocking > 0
                 ? StepResult.Warn(0, payload)
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
