using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Safir.Server.Security;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using System.Text.RegularExpressions;

namespace Safir.Server.Ai
{
    /// <summary>
    /// نگهبانِ کوئریِ آزاد.
    ///
    /// ── چرا اعتبارسنجی و نه فقط دستور به مدل ──
    /// «فقط SELECT بزن» یک درخواست است، نه یک سد. اینجا هر چیزی که
    /// SELECT نباشد پیش از رسیدن به پایگاه رد می‌شود.
    ///
    /// ── سدّ واقعی جای دیگری است ──
    /// این لایه لازم است ولی کافی نیست. تضمینِ درست، کاربرِ SQL جداگانه‌ای
    /// با نقش db_datareader است؛ آن‌وقت حتی اگر این اعتبارسنجی دور زده
    /// شود، خودِ SQL Server نوشتن را رد می‌کند. تا وقتی نصب چنین کاربری
    /// ندارد، این تنها سد است — و در صفحه‌ی دسترسی هم همین نوشته شده.
    /// </summary>
    public static class AiSqlGuard
    {
        /// <summary>
        /// کلمه‌های ممنوع، به‌صورت کلمه‌ی کامل. «Updated» یا «Created» در
        /// نام ستون نباید به‌اشتباه ممنوع شمرده شود، برای همین \b لازم است.
        /// </summary>
        private static readonly string[] Forbidden =
        {
            "insert", "update", "delete", "merge", "drop", "alter", "create",
            "truncate", "exec", "execute", "grant", "revoke", "deny",
            "backup", "restore", "shutdown", "reconfigure", "waitfor",
            "openrowset", "opendatasource", "bulk", "into"
        };

        public static (bool Ok, string? Error) Validate(string? sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return (false, "کوئری خالی است.");

            var q = sql.Trim();

            // چند دستور در یک درخواست، رایج‌ترین راهِ دور زدنِ اعتبارسنجی
            // است: یک SELECT بی‌آزار و بعد از «؛» هر چیزی.
            if (q.TrimEnd(';').Contains(';'))
                return (false, "فقط یک دستور مجاز است؛ «؛» چندتایی پذیرفته نمی‌شود.");

            if (!Regex.IsMatch(q, @"^\s*(select|with)\b", RegexOptions.IgnoreCase))
                return (false, "فقط SELECT (یا WITH … SELECT) مجاز است.");

            foreach (var w in Forbidden)
                if (Regex.IsMatch(q, $@"\b{w}\b", RegexOptions.IgnoreCase))
                    return (false, $"کلمه‌ی «{w}» در کوئری مجاز نیست.");

            // توضیح‌ها می‌توانند بخشی از کوئری را پنهان کنند و اعتبارسنجی
            // را گمراه کنند.
            if (q.Contains("--") || q.Contains("/*"))
                return (false, "توضیح داخل کوئری مجاز نیست.");

            return ValidateAst(q);
        }

        // ── لایه‌ی دوم: پارسر رسمی T-SQL ──
        // Regex بالا متن را می‌بیند نه ساختار را؛ OPENQUERY، جدولِ موقت، نام
        // سه‌بخشیِ دیتابیسِ دیگر و خواندن از sys از آن رد می‌شدند.
        // TSql150 = SQL Server 2019، پایین‌ترین نسخه‌ی مشتری‌ها.
        //
        // ⚠ این هم سدّ نهایی نیست: سدّ واقعی کاربرِ فقط‌خواندنیِ جدا با DENY روی
        // همین جدول‌هاست. viewی که یکی از این جدول‌ها را join کند از اینجا رد
        // می‌شود؛ فهرست پایین فقط راه‌های مستقیم را می‌بندد.

        /// <summary>
        /// جدول‌هایی که دستیار هرگز نباید بخواند: SALA_DTL رمزِ کاربران را دارد و
        /// کدگذاری‌اش برگشت‌پذیر است؛ SAL_CHEK مجوزها؛ AI_Config کلید API؛
        /// لاگ‌های دستیار پرسش‌های کاربرانِ دیگر.
        /// </summary>
        private static readonly HashSet<string> DeniedTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "SALA_DTL", "SAL_CHEK",
            // viewهایی که همان جدول‌ها را می‌خوانند؛ SALS نام کاربری کدشده را نشان می‌دهد
            "SALS", "SALSUSER",
            "AI_Config", "AI_UserAccess", "AI_ChatLog", "AI_Conversation"
        };

        /// <summary>
        /// حقوق و دستمزد: خواندنِ مستقیمش کنترل دسترسیِ کارگاه (PAY2_USER_WS)
        /// را دور می‌زند.
        /// </summary>
        private static readonly string[] DeniedPrefixes = { "PAY2_", "V_PAY2_" };

        /// <summary>
        /// همان فهرستِ ممنوع برای ابزارهای ساختار و مستند (list_tables، describe_table،
        /// find_column، table_doc، search_docs). در آزمون طلایی داده‌ی حقوق خوانده نشد ولی
        /// مدل با describe_table ستون‌های PAY2_EMPLOYEE (کد ملی، شماره حساب…) را دید.
        /// </summary>
        public static bool IsDenied(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return false;
            // «dbo.[SALA_DTL]» → «SALA_DTL»
            var name = objectName.Trim().Split('.').Last().Trim('[', ']', '"', ' ');
            return DeniedTables.Contains(name) ||
                   DeniedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        private static (bool Ok, string? Error) ValidateAst(string sql)
        {
            var parser   = new TSql150Parser(initialQuotedIdentifiers: true);
            var fragment = parser.Parse(new StringReader(sql), out var parseErrors);

            if (parseErrors.Count > 0)
                return (false, $"کوئری قابل تجزیه نیست: {parseErrors[0].Message}");

            if (fragment is not TSqlScript script
                || script.Batches.Count != 1
                || script.Batches[0].Statements.Count != 1)
                return (false, "فقط یک دستور مجاز است.");

            if (script.Batches[0].Statements[0] is not SelectStatement select)
                return (false, "فقط SELECT (یا WITH … SELECT) مجاز است.");

            if (select.Into is not null)
                return (false, "SELECT … INTO مجاز نیست.");

            var v = new ObjectVisitor();
            select.Accept(v);
            return v.Error is null ? (true, null) : (false, v.Error);
        }

        private sealed class ObjectVisitor : TSqlFragmentVisitor
        {
            public string? Error { get; private set; }

            private void Deny(string message) => Error ??= message;

            private void Check(SchemaObjectName? name)
            {
                if (name is null) return;

                var baseName = name.BaseIdentifier?.Value ?? "";

                if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
                    Deny("ارجاع به دیتابیس یا سرور دیگر مجاز نیست.");
                else if (baseName.StartsWith("#"))
                    Deny("جدول موقت مجاز نیست.");
                else if (string.Equals(name.SchemaIdentifier?.Value, "sys", StringComparison.OrdinalIgnoreCase))
                    Deny("خواندن از schema سیستمی (sys) مجاز نیست؛ برای ساختار جدول‌ها از describe_table استفاده کن.");
                else if (DeniedTables.Contains(baseName)
                         || DeniedPrefixes.Any(p => baseName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    Deny($"دسترسی دستیار به «{baseName}» بسته است.");
            }

            public override void Visit(NamedTableReference node)                => Check(node.SchemaObject);
            public override void Visit(SchemaObjectFunctionTableReference node) => Check(node.SchemaObject);

            public override void Visit(OpenRowsetTableReference node) => Deny("OPENROWSET مجاز نیست.");
            public override void Visit(OpenQueryTableReference node)  => Deny("OPENQUERY مجاز نیست.");
            public override void Visit(AdHocTableReference node)      => Deny("OPENDATASOURCE مجاز نیست.");
            public override void Visit(OpenXmlTableReference node)    => Deny("OPENXML مجاز نیست.");
        }
    }


    /// <summary>فهرست جدول‌ها — نقطه‌ی شروعِ کشفِ ساختار.</summary>
    public sealed class ListTablesTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public ListTablesTool(IDatabaseService db) => _db = db;

        public string Name        => "list_tables";
        public string Title       => "فهرست جدول‌ها";
        public string Description =>
            "جدول‌های پایگاه با تعداد تقریبی سطر. برای پیدا کردن جدولِ مرتبط با " +
            "سؤال، اول اینجا بگرد. با q می‌توانی فیلتر کنی.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public bool   RequiresRawSql => true;
        public string Parameters     => "q: بخشی از نام جدول (اختیاری).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var q = call.Str("q");

            // ویوها هم می‌آیند: این پایگاه ۳۹۵ ویو دارد و بعضی گزارش‌ها
            // آنجا از قبل درست ساخته شده‌اند. ندادنشان یعنی مدل همان
            // منطق را دوباره و بدتر می‌نویسد.
            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (@n) * FROM (
                    SELECT t.name AS ObjectName, 'table' AS Kind,
                           SUM(p.rows) AS ApproxRows
                    FROM   sys.tables t
                    JOIN   sys.partitions p ON p.object_id = t.object_id
                                           AND p.index_id IN (0,1)
                    GROUP BY t.name
                    UNION ALL
                    SELECT v.name, 'view', NULL FROM sys.views v
                ) x
                WHERE (@q IS NULL OR x.ObjectName LIKE '%' + @q + '%')
                ORDER BY x.ObjectName",
                new { n = call.MaxRows, q })).ToList();

            rows = rows.Where(r => !AiSqlGuard.IsDenied((string)r.ObjectName)).ToList();

            return new AiToolResult { Rows = rows.Count, Data = rows,
                                      Truncated = rows.Count >= call.MaxRows };
        }
    }


    /// <summary>ستون‌های یک جدول — پیش از نوشتن کوئری.</summary>
    public sealed class DescribeTableTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public DescribeTableTool(IDatabaseService db) => _db = db;

        public string Name        => "describe_table";
        public string Title       => "ساختار یک جدول";
        public string Description =>
            "نام و نوع ستون‌های یک جدول. پیش از نوشتن کوئری روی جدولی که " +
            "نمی‌شناسی، حتماً این را صدا بزن — حدس زدنِ نام ستون خطا می‌دهد.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public bool   RequiresRawSql => true;
        public string Parameters     => "table: نام جدول (الزامی).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var table = call.Str("table");
            if (string.IsNullOrWhiteSpace(table))
                return AiToolResult.Fail("پارامتر table لازم است.");
            if (AiSqlGuard.IsDenied(table))
                return AiToolResult.Fail($"دسترسی دستیار به «{table}» بسته است.");

            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT c.name AS ColumnName, ty.name AS DataType,
                       c.max_length AS MaxLength, c.is_nullable AS IsNullable
                FROM   sys.columns c
                JOIN   sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE  c.object_id = OBJECT_ID(@table)
                ORDER BY c.column_id", new { table })).ToList();

            if (rows.Count == 0)
                return AiToolResult.Fail($"جدول «{table}» پیدا نشد. با list_tables نامش را بررسی کن.");

            // ── روابط، در هر دو جهت ──
            // این پایگاه ۱۹۰ کلید خارجی دارد. بدون این، مدل شرط JOIN را
            // از روی شباهت نام حدس می‌زند و کوئری‌ای می‌سازد که اجرا
            // می‌شود ولی سطرها را غلط جفت می‌کند.
            var links = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT OBJECT_NAME(fk.parent_object_id)     AS FromTable,
                       pc.name                              AS FromColumn,
                       OBJECT_NAME(fk.referenced_object_id) AS ToTable,
                       rc.name                              AS ToColumn
                FROM   sys.foreign_keys fk
                JOIN   sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN   sys.columns pc ON pc.object_id = fkc.parent_object_id
                                     AND pc.column_id = fkc.parent_column_id
                JOIN   sys.columns rc ON rc.object_id = fkc.referenced_object_id
                                     AND rc.column_id = fkc.referenced_column_id
                WHERE  fk.parent_object_id = OBJECT_ID(@table)
                    OR fk.referenced_object_id = OBJECT_ID(@table)", new { table })).ToList();

            return new AiToolResult
            {
                Rows = rows.Count,
                Data = new { Columns = rows, Relations = links }
            };
        }
    }


    /// <summary>
    /// «کدام جدول‌ها ستونی با این نام دارند؟»
    ///
    /// در پایگاهی با ۵۰۲ جدول، این سریع‌ترین راه رسیدن از یک مفهوم به
    /// جدولِ درست است — مثلاً «ویزیتور» یا «تخفیف» که ممکن است در چند
    /// جدول باشند و نامشان در عنوان جدول نیاید.
    /// </summary>
    public sealed class FindColumnTool : IAiTool
    {
        private readonly IDatabaseService _db;
        public FindColumnTool(IDatabaseService db) => _db = db;

        public string Name        => "find_column";
        public string Title       => "یافتن جدول از روی نام ستون";
        public string Description =>
            "جدول‌ها و ویوهایی که ستونی با این نام دارند. وقتی می‌دانی دنبال " +
            "چه مفهومی هستی ولی نمی‌دانی در کدام جدول است، از این استفاده کن.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public bool   RequiresRawSql => true;
        public string Parameters     => "column: بخشی از نام ستون (الزامی).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var col = call.Str("column");
            if (string.IsNullOrWhiteSpace(col))
                return AiToolResult.Fail("پارامتر column لازم است.");

            var rows = (await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (@n) OBJECT_NAME(c.object_id) AS ObjectName,
                       c.name AS ColumnName, ty.name AS DataType
                FROM   sys.columns c
                JOIN   sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE  c.name LIKE '%' + @col + '%'
                ORDER BY OBJECT_NAME(c.object_id), c.name",
                new { n = call.MaxRows, col })).ToList();

            rows = rows.Where(r => !AiSqlGuard.IsDenied((string)r.ObjectName)).ToList();

            return new AiToolResult { Rows = rows.Count, Data = rows,
                                     Truncated = rows.Count >= call.MaxRows };
        }
    }


    /// <summary>
    /// اجرای کوئریِ خواندنی دلخواه.
    ///
    /// ⚠ این ابزار محدودیتِ فرم‌به‌فرم را دور می‌زند. کاربری که آن را
    /// دارد، عملاً به هر جدولی که کاربرِ SQL برنامه می‌بیند دسترسی دارد —
    /// از جمله حقوق و دستمزد. برای همین پیش‌فرضش خاموش است و مجوز
    /// جداگانه‌ی خودش را دارد.
    /// </summary>
    public sealed class RunSqlTool : IAiTool
    {
        private readonly string _connectionString;

        // اتصالِ فقط‌خواندنیِ جدا اگر در تنظیمات باشد (ConnectionStrings:AiReadOnly)؛
        // وگرنه همان اتصال برنامه — و آن‌وقت سدّ اصلی همان AiSqlGuard است.
        public RunSqlTool(IConfiguration config, IConnectionStringProvider fallback) =>
            _connectionString = config.GetConnectionString("AiReadOnly") is { Length: > 0 } ro
                ? ro
                : fallback.GetConnectionString();

        public string Name        => "run_sql";
        public string Title       => "اجرای کوئری";
        public string Description =>
            "یک کوئری SELECT روی پایگاه اجرا می‌کند و سطرها را برمی‌گرداند. " +
            "فقط خواندنی. پیش از استفاده، ساختار جدول را با describe_table " +
            "بگیر و نام ستون‌ها را حدس نزن. برای محدود کردن نتیجه از TOP " +
            "استفاده کن.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public bool   RequiresRawSql => true;
        public string Parameters     => "sql: متن کوئری SELECT (الزامی).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var sql = call.Str("sql");

            var (ok, error) = AiSqlGuard.Validate(sql);
            if (!ok) return AiToolResult.Fail(error!);

            // ردیف‌به‌ردیف تا سقف، نه اول همه و بعد Take: قبلاً یک SELECT بی‌TOP
            // روی DEED_DTL (۳۳۰ هزار ردیف) کلش را در حافظه‌ی سرور می‌آورد و
            // بعد ۵۰۰ تا را نگه می‌داشت.
            //
            // تایم‌اوت کوتاه: کوئریِ بدِ مدل نباید سرورِ همه را بخواباند.
            // ۳۰ ثانیه برای گزارش‌های معمول کافی است و برای اسکنِ کاملِ
            // یک جدول بزرگ نیست. (قبلاً همین کامنت بود ولی تایم‌اوتی تنظیم نمی‌شد.)
            var rows = new List<Dictionary<string, object?>>();
            bool truncated = false;

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count == call.MaxRows)
                    {
                        truncated = true;
                        // بدون Cancel، بستنِ reader بقیه‌ی نتیجه را تا آخر می‌خواند
                        cmd.Cancel();
                        break;
                    }

                    var row = new Dictionary<string, object?>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
            }

            return new AiToolResult
            {
                Rows      = rows.Count,
                Data      = rows,
                Truncated = truncated
            };
        }
    }
}
