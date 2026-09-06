using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Server.Security;
using System.Text.Json;

namespace Safir.Server.Ai
{
    /// <summary>
    /// یک کارِ مشخص که دستیار می‌تواند انجام دهد.
    ///
    /// ── چرا ابزار و نه SQL آزاد ──
    /// مدل با SQL آزاد کوئری‌هایی می‌نویسد که *اجرا* می‌شوند ولی از نظر
    /// معنایی غلط‌اند — مثلاً فروش را از KALAS بدون شرط TAGCODE=2 جمع
    /// می‌زند و برگشت از فروش را هم فروش حساب می‌کند. ابزارها همان
    /// منطقی را دارند که گزارش‌های خودِ برنامه دارند، پس جوابِ چت با
    /// جوابِ صفحه یکی درمی‌آید. کوئری آزاد می‌ماند، ولی به‌عنوان
    /// آخرین چاره و با مجوز جدا.
    /// </summary>
    public interface IAiTool
    {
        string Name        { get; }
        string Title       { get; }
        string Description { get; }

        /// <summary>فرمی که کاربر باید دسترسی‌اش را داشته باشد.</summary>
        string    RequiredForm { get; }
        Pay2Perm  RequiredPerm { get; }

        /// <summary>ابزارهای کوئریِ آزاد مجوز جداگانه می‌خواهند.</summary>
        bool RequiresRawSql => false;

        /// <summary>توضیح پارامترها برای مدل — به زبان ساده، نه JSON Schema.</summary>
        string Parameters { get; }

        Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default);
    }

    public sealed class AiToolCall
    {
        public int  UserCo  { get; init; }
        public int  MaxRows { get; init; } = 500;

        /// <summary>پارامترها به‌صورت JSON، همان‌طور که مدل تولید می‌کند.</summary>
        public JsonElement Args { get; init; }

        public int    Int(string name, int fallback = 0)
            => Args.ValueKind == JsonValueKind.Object &&
               Args.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

        public string? Str(string name)
            => Args.ValueKind == JsonValueKind.Object &&
               Args.TryGetProperty(name, out var v) ? v.ToString() : null;
    }

    public sealed class AiToolResult
    {
        public bool   Ok      { get; init; } = true;
        public string? Error  { get; init; }
        public int    Rows    { get; init; }

        /// <summary>خروجی برای مدل. همیشه داده است، هرگز دستور.</summary>
        public object? Data   { get; init; }

        /// <summary>
        /// وقتی نتیجه بریده شده باشد. اگر این را نگوییم، مدل ۵۰۰ سطرِ اول
        /// را «همه» فرض می‌کند و جمع غلط تحویل کاربر می‌دهد.
        /// </summary>
        public bool Truncated { get; init; }

        public static AiToolResult Fail(string error) => new() { Ok = false, Error = error };
    }

    public interface IAiToolRegistry
    {
        IReadOnlyList<IAiTool> All { get; }
        IAiTool? Find(string name);
    }

    public sealed class AiToolRegistry : IAiToolRegistry
    {
        public IReadOnlyList<IAiTool> All { get; }

        public AiToolRegistry(IEnumerable<IAiTool> tools) => All = tools.ToList();

        public IAiTool? Find(string name)
            => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }


    // ═══════════════════════════ ابزارها ═══════════════════════════

    /// <summary>فهرست اجراهای بستن ماه — نقطه‌ی شروع تقریباً هر سؤالی.</summary>
    public sealed class ListRunsTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public ListRunsTool(IDatabaseService db) => _db = db;

        public string Name        => "list_runs";
        public string Title       => "فهرست اجراهای بستن ماه";
        public string Description => "اجراهای بهای تمام‌شده با سال، ماه، بازه‌ی تاریخ و وضعیت. " +
                                     "برای پیدا کردن RunId مربوط به یک ماه از این استفاده کن.";
        public string RequiredForm => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "بدون پارامتر.";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (@n) RunId, FiscalYear, PeriodMonth, DateFrom, DateTo,
                       Status, IsLatest
                FROM   dbo.CC_Run ORDER BY RunId DESC", new { n = call.MaxRows })).ToList();

            return new AiToolResult { Rows = rows.Count, Data = rows };
        }
    }

    /// <summary>جستجوی کالا با نام یا کد.</summary>
    public sealed class SearchItemTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public SearchItemTool(IDatabaseService db) => _db = db;

        public string Name        => "search_item";
        public string Title       => "جستجوی کالا";
        public string Description => "کالا را با بخشی از نام یا با کد پیدا می‌کند. " +
                                     "قبل از هر ابزاری که کد کالا می‌خواهد، از این استفاده کن.";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "q: بخشی از نام کالا یا کد آن (رشته).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var q = call.Str("q");
            if (string.IsNullOrWhiteSpace(q)) return AiToolResult.Fail("پارامتر q لازم است.");

            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (@n) TRY_CAST(s.CODE AS BIGINT) AS Code, s.NAME
                FROM   dbo.STUF_DEF s
                WHERE  s.NAME LIKE @like OR s.CODE LIKE @like
                ORDER BY s.NAME",
                new { n = call.MaxRows, like = "%" + q.Trim() + "%" })).ToList();

            return new AiToolResult { Rows = rows.Count, Data = rows };
        }
    }

    /// <summary>
    /// سود و زیان کالا در یک اجرا، با همان منطقِ گزارشِ خودِ برنامه.
    ///
    /// سه مبنا دارد چون سه سؤال متفاوت‌اند و قاطی کردنشان همان اشتباهی
    /// است که گزارش قبلی می‌کرد: کل، انبارِ فروش، واحدِ تولیدکننده.
    /// </summary>
    public sealed class ItemMarginTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public ItemMarginTool(IDatabaseService db) => _db = db;

        public string Name        => "item_margin";
        public string Title       => "سود و زیان کالا";
        public string Description =>
            "سود و زیان کالا در یک اجرا. basis: all = کل، sale = به تفکیک انبار فروش، " +
            "prod = به تفکیک واحد تولیدکننده (کالایی که یک واحد ساخته، حتی اگر از انبار " +
            "واحد دیگری فروش رفته باشد).";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  =>
            "runId: شناسه‌ی اجرا (عدد، الزامی). " +
            "basis: all | sale | prod (پیش‌فرض all). " +
            "unitId: شناسه‌ی واحد (عدد، اختیاری — فقط با basis=sale یا prod). " +
            "lossOnly: true یعنی فقط کالاهای زیان‌ده.";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var runId = call.Int("runId");
            if (runId <= 0) return AiToolResult.Fail("پارامتر runId لازم است.");

            var basis = (call.Str("basis") ?? "all").ToLowerInvariant();
            var table = basis switch
            {
                "sale" => "dbo.CC_ItemMarginUnit",
                "prod" => "dbo.CC_ItemMarginProdUnit",
                _      => "dbo.CC_ItemMargin"
            };

            var unitId   = call.Int("unitId", -1);
            var lossOnly = string.Equals(call.Str("lossOnly"), "true", StringComparison.OrdinalIgnoreCase);

            var unitCols   = basis == "all" ? "" : "m.UnitId, cu.UnitName,";
            var unitJoin   = basis == "all" ? "" : "LEFT JOIN dbo.CC_Unit cu ON cu.UnitId = m.UnitId";
            var unitFilter = basis != "all" && unitId >= 0 ? "AND m.UnitId = @unitId" : "";
            var lossFilter = lossOnly ? "AND m.Profit < 0" : "";

            var rows = (await _db.DoGetDataSQLAsync<dynamic>($@"
                SELECT TOP (@n)
                       {unitCols}
                       m.Code, s.NAME AS ItemName,
                       m.QtySold, m.SalesAmount, m.CostAmount, m.Profit,
                       m.UnitPrice, m.UnitCost
                FROM   {table} m
                LEFT   JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = m.Code
                {unitJoin}
                WHERE  m.RunId = @runId {unitFilter} {lossFilter}
                ORDER BY m.Profit",
                new { n = call.MaxRows, runId, unitId })).ToList();

            return new AiToolResult
            {
                Rows      = rows.Count,
                Data      = rows,
                Truncated = rows.Count >= call.MaxRows
            };
        }
    }

    /// <summary>سرجمع هر واحد — برای سؤال‌های «کدام واحد سودده‌تر بود».</summary>
    public sealed class UnitSummaryTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public UnitSummaryTool(IDatabaseService db) => _db = db;

        public string Name        => "unit_summary";
        public string Title       => "سرجمع سود هر واحد";
        public string Description => "جمع فروش، بها و سودِ هر واحد در یک اجرا. " +
                                     "basis مثل ابزار item_margin است.";
        public string RequiredForm => CostForms.Margin;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "runId: عدد (الزامی). basis: sale | prod (پیش‌فرض prod).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var runId = call.Int("runId");
            if (runId <= 0) return AiToolResult.Fail("پارامتر runId لازم است.");

            var prod  = (call.Str("basis") ?? "prod").ToLowerInvariant() != "sale";
            var table = prod ? "dbo.CC_ItemMarginProdUnit" : "dbo.CC_ItemMarginUnit";
            var none  = prod ? "هرگز تولید نشده" : "بدون واحد";

            var rows = (await _db.DoGetDataSQLAsync<dynamic>($@"
                SELECT ISNULL(cu.UnitName, N'{none}') AS UnitName,
                       COUNT(*) AS Items,
                       SUM(CASE WHEN m.Profit < 0 THEN 1 ELSE 0 END) AS LossItems,
                       SUM(m.SalesAmount) AS Sales,
                       SUM(m.CostAmount)  AS Cost,
                       SUM(m.Profit)      AS Profit
                FROM   {table} m
                LEFT   JOIN dbo.CC_Unit cu ON cu.UnitId = m.UnitId
                WHERE  m.RunId = @runId
                GROUP BY m.UnitId, cu.UnitName
                ORDER BY SUM(m.Profit) DESC", new { runId })).ToList();

            return new AiToolResult { Rows = rows.Count, Data = rows };
        }
    }

    /// <summary>مغایرت‌ها و استثناهای یک اجرا.</summary>
    public sealed class ExceptionsTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public ExceptionsTool(IDatabaseService db) => _db = db;

        public string Name        => "run_exceptions";
        public string Title       => "استثناها و مغایرت‌های اجرا";
        public string Description => "خطاها و هشدارهای کنترلی یک اجرا (CHK-01 تا CHK-21) " +
                                     "با شرح و وضعیت رفع‌شدن.";
        public string RequiredForm => CostForms.Exceptions;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters  => "runId: عدد (الزامی). openOnly: true یعنی فقط رفع‌نشده‌ها.";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var runId = call.Int("runId");
            if (runId <= 0) return AiToolResult.Fail("پارامتر runId لازم است.");

            var openOnly = string.Equals(call.Str("openOnly"), "true", StringComparison.OrdinalIgnoreCase);
            var filter   = openOnly ? "AND e.IsResolved = 0" : "";

            var rows = (await _db.DoGetDataSQLAsync<dynamic>($@"
                SELECT TOP (@n) e.ExceptionId, e.RuleCode, e.StepCode, e.Severity,
                       e.Description, e.Code, e.Anbar, e.DocNumber, e.DocDate, e.Amount,
                       e.IsResolved, e.ResolvedAtUtc, e.ResolvedBy
                FROM   dbo.CC_Exception e
                WHERE  e.RunId = @runId {filter}
                ORDER BY e.Severity DESC, e.ExceptionId",
                new { n = call.MaxRows, runId })).ToList();

            return new AiToolResult
            {
                Rows      = rows.Count,
                Data      = rows,
                Truncated = rows.Count >= call.MaxRows
            };
        }
    }
}
