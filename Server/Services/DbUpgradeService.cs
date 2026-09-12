using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.DbAdmin;

namespace Safir.Server.Services
{
    /// <summary>
    /// وضعیت‌سنجی و اجرای مهاجرت‌های دیتابیس از داخل سفیر.
    ///
    /// ⚠ چرا فراخوانی فرآیند بیرونی و نه رفرنسِ مستقیم به ScriptSqly.Core:
    ///
    ///   ۱) ScriptSqly یک submodule است و در Safir.sln نیست. اگر
    ///      ProjectReference بدهیم، CI که بدون submodule کد را می‌گیرد
    ///      اصلاً کامپایل نمی‌شود.
    ///   ۲) مهاجرت باید یک عملِ عمدی باشد. با فرآیند جدا، هر بار که سرور
    ///      restart می‌شود — از جمله recycle خودکارِ IIS — هیچ DDLای اجرا
    ///      نمی‌شود. همان دلیلی که Program.cs از اول مهاجرت را به
    ///      «updater جدا» سپرده بود؛ این سرویس آن تصمیم را عوض نمی‌کند،
    ///      فقط دکمه‌اش را جلوی دست می‌آورد.
    /// </summary>
    public sealed class DbUpgradeService
    {
        private readonly IConnectionStringProvider _conn;
        private readonly IDatabaseService _db;
        private readonly IConfiguration _config;
        private readonly ILogger<DbUpgradeService> _logger;

        public DbUpgradeService(IConnectionStringProvider conn,
                                IDatabaseService db,
                                IConfiguration config,
                                ILogger<DbUpgradeService> logger)
        {
            _conn   = conn;
            _db     = db;
            _config = config;
            _logger = logger;
        }

        // ─────────────────────────── شاخص‌ها ───────────────────────────
        //
        // نمونه‌هایی از تازه‌ترین مهاجرت‌ها. نبودِ هرکدام یعنی دیتابیس عقب
        // است. ترتیب از تازه به قدیم، تا در صفحه اول چیزی را ببینید که
        // احتمال جا ماندنش بیشتر است.
        //
        // 👉 هر مهاجرتِ تازه‌ای که اضافه شد، یک شاخص از آن هم اینجا بیاید.
        //    این فهرست تنها چیزی است که «دیتابیس به‌روز است» را معنادار
        //    می‌کند؛ اگر عقب بماند، این صفحه با اطمینانِ کاذب سبز می‌شود.

        private enum ProbeKind { Object, Column, Row }

        private sealed record Probe(ProbeKind Kind, string Target, string Member,
                                    string Display, string Caption, string Why);

        private static readonly Probe[] Probes =
        {
            new(ProbeKind.Column, "dbo.CC_Run", "LastHeartbeatUtc",
                "CC_Run.LastHeartbeatUtc", "ستون",
                "بدون آن، اجرایی که مرده تا ابد «در حال اجرا» می‌ماند (۳۴)"),

            new(ProbeKind.Object, "dbo.CC_sp_ReleaseStaleRuns", "",
                "CC_sp_ReleaseStaleRuns", "رویه",
                "آزادکردن اجراهای رهاشده در شروع برنامه (۳۴)"),

            new(ProbeKind.Column, "dbo.CC_CheckRule", "IsBlocking",
                "CC_CheckRule.IsBlocking", "ستون",
                "بدون آن دروازه پشتِ هر هشداری می‌ایستد، نه فقط کاردکس منفی (۳۳)"),

            new(ProbeKind.Row, "CHK-23", "",
                "قاعده CHK-23", "داده",
                "برگه‌های بدون فاکتور و فاکتورهای بدون برگه گزارش نمی‌شوند"),

            new(ProbeKind.Column, "dbo.CC_Exception", "OpeningKind",
                "CC_Exception.OpeningKind", "ستون",
                "نبودنش کل فهرست استثناها را می‌شکند — یک بار روی تولید افتاد"),

            new(ProbeKind.Object, "dbo.AI_Conversation", "",
                "AI_Conversation", "جدول",
                "تاریخچه‌ی گفتگوی دستیار (۳۲)"),

            new(ProbeKind.Object, "dbo.AI_Config", "",
                "AI_Config", "جدول",
                "تنظیمات دستیار هوش مصنوعی (۳۱)"),

            new(ProbeKind.Object, "dbo.AI_UserAccess", "",
                "AI_UserAccess", "جدول",
                "دسترسی کاربران به دستیار (۳۰)"),
        };

        // ─────────────────────────── وضعیت ───────────────────────────

        public async Task<DbUpgradeStatus> GetStatusAsync()
        {
            var cs = _conn.GetConnectionString();
            var b  = new SqlConnectionStringBuilder(cs);

            var status = new DbUpgradeStatus
            {
                Server   = b.DataSource,
                Database = b.InitialCatalog
            };

            var runner = ResolveRunnerPath();
            status.RunnerPath      = runner.Path;
            status.RunnerAvailable = runner.Exists;
            if (!runner.Exists)
                status.Blocker = string.IsNullOrWhiteSpace(runner.Path)
                    ? "مسیر ScriptSqly.Runner در تنظیمات (DbUpgrade:RunnerPath) تعریف نشده است."
                    : $"فایل اجرایی در «{runner.Path}» پیدا نشد.";

            // یک رفت‌وبرگشت برای همه‌ی شاخص‌ها. جدا جدا پرسیدن یعنی هشت
            // رفت‌وبرگشت برای کاری که فقط گزارش می‌دهد.
            var sql = new StringBuilder();
            for (int i = 0; i < Probes.Length; i++)
            {
                var p = Probes[i];
                if (i > 0) sql.Append(" UNION ALL ");

                sql.Append(p.Kind switch
                {
                    ProbeKind.Object => $"SELECT {i} AS Idx, CAST(CASE WHEN OBJECT_ID('{p.Target}') IS NULL THEN 0 ELSE 1 END AS BIT) AS Present",
                    ProbeKind.Column => $"SELECT {i} AS Idx, CAST(CASE WHEN COL_LENGTH('{p.Target}','{p.Member}') IS NULL THEN 0 ELSE 1 END AS BIT) AS Present",

                    // شاخصِ داده‌ای فقط وقتی معنی دارد که خودِ جدول باشد؛
                    // وگرنه کوئری با «Invalid object name» می‌شکند و کل
                    // صفحه خطا می‌دهد به‌جای اینکه بگوید چه چیزی کم است.
                    _ => $"SELECT {i} AS Idx, CAST(CASE WHEN OBJECT_ID('dbo.CC_CheckRule') IS NOT NULL " +
                         $"AND EXISTS (SELECT 1 FROM dbo.CC_CheckRule WHERE RuleCode = '{p.Target}') THEN 1 ELSE 0 END AS BIT) AS Present"
                });
            }

            var rows = (await _db.DoGetDataSQLAsync<ProbeRow>(sql.ToString()))
                       .ToDictionary(r => r.Idx, r => r.Present);

            for (int i = 0; i < Probes.Length; i++)
            {
                var p = Probes[i];
                status.Probes.Add(new DbObjectProbe
                {
                    Name        = p.Display,
                    Kind        = p.Caption,
                    Description = p.Why,
                    Exists      = rows.TryGetValue(i, out var ok) && ok
                });
            }

            return status;
        }

        private sealed class ProbeRow
        {
            public int  Idx     { get; set; }
            public bool Present { get; set; }
        }

        // ─────────────────────────── اجرا ───────────────────────────

        /// <summary>
        /// ScriptSqly.Runner را روی دیتابیسِ *جاری* اجرا می‌کند (همانی که
        /// هدر X-DB-Connection مشخص کرده، نه لزوماً DefaultConnection).
        /// </summary>
        public async Task<DbUpgradeResult> RunAsync(bool previewOnly, CancellationToken ct)
        {
            var runner = ResolveRunnerPath();
            if (!runner.Exists)
                throw new InvalidOperationException(
                    $"فایل اجرایی ScriptSqly.Runner پیدا نشد: {runner.Path}");

            var args = "--type 2 --custom-call" + (previewOnly ? " --preview-only" : "");

            var psi = new ProcessStartInfo
            {
                FileName               = runner.Path!,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                WorkingDirectory       = Path.GetDirectoryName(runner.Path!)!
            };

            // ⚠ رشته‌ی اتصال از متغیر محیطی می‌رود، نه از آرگومان. آرگومان‌ها
            // در فهرست فرآیندهای ویندوز برای هر کاربری قابل دیدن‌اند و اگر
            // احراز هویت SQL باشد، پسورد آنجا لو می‌رفت.
            psi.Environment["SCRIPTSQLY_CONN_STR"] = _conn.GetConnectionString();

            var sw     = Stopwatch.StartNew();
            var start  = DateTime.UtcNow;
            var output = new StringBuilder();

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // مهاجرتِ نیمه‌کاره بدتر از مهاجرتِ کند است؛ کشتنِ فرآیند
                // وسطِ کار می‌تواند دیتابیس را بین دو حالت رها کند. پس
                // منتظر می‌مانیم و فقط ثبت می‌کنیم که کاربر قطع کرده.
                _logger.LogWarning("درخواست لغو شد ولی مهاجرت تا پایان ادامه می‌یابد.");
                await proc.WaitForExitAsync(CancellationToken.None);
            }

            sw.Stop();

            string text;
            lock (output) text = output.ToString();

            return new DbUpgradeResult
            {
                Success      = proc.ExitCode == 0,
                ExitCode     = proc.ExitCode,
                Output       = text,
                DurationMs   = sw.ElapsedMilliseconds,
                StartedAtUtc = start,
                WasPreview   = previewOnly
            };
        }

        /// <summary>
        /// مسیر از تنظیمات می‌آید تا روی سرور تولید که ساختار پوشه‌ها فرق
        /// دارد قابل تنظیم باشد؛ اگر نبود، مسیرِ متعارفِ کنارِ مخزن.
        /// </summary>
        private (string? Path, bool Exists) ResolveRunnerPath()
        {
            var configured = _config["DbUpgrade:RunnerPath"];

            var candidates = new List<string?> { configured };

            if (string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(Path.Combine(AppContext.BaseDirectory,
                    "ScriptSqly", "ScriptSqly.Runner.exe"));
                candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "External", "ScriptSqly", "ScriptSqly.Runner",
                    "bin", "Release", "net8.0", "publish", "win-x64", "ScriptSqly.Runner.exe")));
            }

            foreach (var c in candidates)
                if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
                    return (c, true);

            return (configured ?? candidates.LastOrDefault(), false);
        }
    }
}
