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
    /// ScriptSqly.Core مستقیماً رفرنس شده و <c>LetsGo</c> در همین فرآیند
    /// اجرا می‌شود — نه فایل اجرایی جدا، نه مسیری که باید کنار برنامه کپی
    /// شود، نه چیزی که بشود فراموشش کرد.
    ///
    /// ⚠ این *تغییر نمی‌دهد* که مهاجرت یک عملِ عمدی است: هیچ‌جا هنگام بالا
    /// آمدن برنامه صدا زده نمی‌شود. restart سرور — از جمله recycle خودکار
    /// IIS — همچنان هیچ DDLای اجرا نمی‌کند. تنها راه، همان دکمه‌ی تأییددار
    /// در /admin/db-upgrade است.
    /// </summary>
    public sealed class DbUpgradeService
    {
        private readonly IConnectionStringProvider _conn;
        private readonly IDatabaseService _db;
        private readonly ILogger<DbUpgradeService> _logger;

        /// <summary>
        /// فقط یک مهاجرت در هر لحظه. دو اجرای هم‌زمان روی یک دیتابیس یعنی
        /// دو بار همان ALTER، و چون خروجی کنسول هم موقتاً قرض گرفته می‌شود،
        /// خروجی دو اجرا در هم می‌رفت.
        /// </summary>
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public DbUpgradeService(IConnectionStringProvider conn,
                                IDatabaseService db,
                                ILogger<DbUpgradeService> logger)
        {
            _conn   = conn;
            _db     = db;
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
        /// مهاجرت‌ها را روی دیتابیسِ *جاری* اجرا می‌کند (همانی که هدر
        /// X-DB-Connection مشخص کرده، نه لزوماً DefaultConnection).
        ///
        /// <paramref name="previewOnly"/> هیچ تغییری نمی‌دهد: فقط اتصال و
        /// دسترسی را می‌سنجد. ScriptSqly خودش حالت آزمایشی ندارد، پس
        /// «بررسی خشک» یعنی تا لبه‌ی اجرا رفتن و برگشتن.
        /// </summary>
        public async Task<DbUpgradeResult> RunAsync(bool previewOnly, bool includeBaseData, CancellationToken ct)
        {
            var cs    = _conn.GetConnectionString();
            var sw    = Stopwatch.StartNew();
            var start = DateTime.UtcNow;

            await Gate.WaitAsync(ct);
            try
            {
                if (previewOnly)
                {
                    var (ok, message) = await ProbeConnectionAsync(cs);
                    sw.Stop();
                    return new DbUpgradeResult
                    {
                        Success      = ok,
                        ExitCode     = ok ? 0 : 1,
                        Output       = message,
                        DurationMs   = sw.ElapsedMilliseconds,
                        StartedAtUtc = start,
                        WasPreview   = true
                    };
                }

                var buffer = new StringWriter();

                // ScriptSqly خطاهای SQL را با Console.WriteLine گزارش
                // می‌دهد و راه دیگری برای گرفتن‌شان ندارد. پس کنسول موقتاً
                // «انشعاب» می‌گیرد: هم در بافر ما می‌نویسد، هم سر جای
                // خودش. اگر فقط جایگزین می‌کردیم، لاگ‌های عادی سرور در
                // این چند دقیقه بلعیده می‌شدند.
                var prevOut = Console.Out;
                var prevErr = Console.Error;
                Console.SetOut(new TeeWriter(prevOut, buffer));
                Console.SetError(new TeeWriter(prevErr, buffer));

                Exception? failure = null;
                try
                {
                    // روی نخِ جدا، چون LetsGo همگام است و دقیقه‌ها طول
                    // می‌کشد؛ روی نخِ درخواست، کلِ سرور را معطل می‌کرد.
                    //
                    // بدون CancellationToken اجرا می‌شود، عمداً: مهاجرتِ
                    // نیمه‌کاره بدتر از مهاجرتِ کند است. اگر کاربر مرورگر
                    // را ببندد، کار تا آخر می‌رود.
                    await Task.Run(() =>
                        ScriptSqly.Migrations.ScriptSqly.LetsGo(cs, includeBaseData, 2),
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    _logger.LogError(ex, "اجرای مهاجرت‌ها شکست خورد.");
                }
                finally
                {
                    Console.SetOut(prevOut);
                    Console.SetError(prevErr);
                }

                sw.Stop();

                var text = buffer.ToString();
                if (failure is not null)
                    text += Environment.NewLine + failure;
                else if (string.IsNullOrWhiteSpace(text))
                    text = "مهاجرت‌ها بدون خطا اجرا شدند.";

                return new DbUpgradeResult
                {
                    Success      = failure is null,
                    ExitCode     = failure is null ? 0 : 1,
                    Output       = text,
                    DurationMs   = sw.ElapsedMilliseconds,
                    StartedAtUtc = start,
                    WasPreview   = false
                };
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// بررسی خشک: فقط وصل می‌شود و می‌پرسد کجاست. نه چیزی می‌نویسد و
        /// نه چیزی را قفل می‌کند.
        /// </summary>
        private static async Task<(bool Ok, string Message)> ProbeConnectionAsync(string cs)
        {
            try
            {
                await using var db = new SqlConnection(cs);
                await db.OpenAsync();

                await using var cmd = db.CreateCommand();
                cmd.CommandText =
                    "SELECT DB_NAME(), @@SERVERNAME, CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(64)), " +
                    "CAST(IS_SRVROLEMEMBER('sysadmin') AS INT), CAST(IS_MEMBER('db_owner') AS INT)";

                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return (false, "اتصال برقرار شد ولی پاسخی نیامد.");

                var sb = new StringBuilder();
                sb.AppendLine($"اتصال برقرار است.");
                sb.AppendLine($"  دیتابیس : {r.GetString(0)}");
                sb.AppendLine($"  سرور    : {r.GetString(1)}");
                sb.AppendLine($"  نسخه    : {r.GetString(2)}");

                var sysadmin = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                var dbowner  = r.IsDBNull(4) ? 0 : r.GetInt32(4);

                // مهاجرت DDL می‌زند؛ کاربری که این دسترسی را ندارد وسط کار
                // شکست می‌خورد، و چون ScriptSqly بیشتر خطاها را می‌بلعد،
                // شکستش بی‌صدا خواهد بود. بهتر است همین‌جا معلوم شود.
                sb.AppendLine(sysadmin == 1 || dbowner == 1
                    ? "  دسترسی  : کافی برای تغییر ساختار"
                    : "  ⚠ دسترسی : نه sysadmin و نه db_owner — احتمالاً مهاجرت ناقص می‌ماند");

                return (true, sb.ToString());
            }
            catch (Exception ex)
            {
                return (false, "اتصال برقرار نشد:" + Environment.NewLine + ex.Message);
            }
        }

        /// <summary>هر چه نوشته شود به هر دو مقصد می‌رود.</summary>
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _a, _b;
            public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }

            public override Encoding Encoding => _a.Encoding;

            public override void Write(char value)
            {
                _a.Write(value);
                lock (_b) _b.Write(value);
            }

            public override void Write(string? value)
            {
                _a.Write(value);
                lock (_b) _b.Write(value);
            }

            public override void WriteLine(string? value)
            {
                _a.WriteLine(value);
                lock (_b) _b.WriteLine(value);
            }
        }
    }
}
