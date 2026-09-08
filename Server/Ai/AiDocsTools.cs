using Safir.Server.Security;
using Safir.Shared.Constants;

namespace Safir.Server.Ai
{
    /// <summary>
    /// مستندِ فارسیِ یک جدول یا ویو.
    ///
    /// این ابزارِ همراهِ describe_table است، نه جایگزینش: آن یکی ستون‌ها
    /// را زنده می‌دهد، این یکی می‌گوید هر ستون *یعنی چه*.
    /// </summary>
    public sealed class TableDocTool : IAiTool
    {
        private readonly IAiDocsIndex _docs;
        public TableDocTool(IAiDocsIndex docs) => _docs = docs;

        public string Name        => "table_doc";
        public string Title       => "مستند فارسی جدول یا ویو";
        public string Description =>
            "عنوانِ فارسیِ تک‌تک ستون‌ها، کلید اصلی، و فرمِ مرتبط در برنامه‌ی " +
            "قدیمی. برای ویو، متنِ کاملِ SQL و گزارشی که از آن ساخته شده. " +
            "نام‌های این پایگاه کوتاه و مبهم‌اند (MEGHK، BES، RADAH)؛ پیش از " +
            "اینکه معنیِ ستونی را حدس بزنی، اینجا را ببین.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public bool   RequiresRawSql => true;
        public string Parameters     => "name: نام جدول یا ویو (الزامی).";

        public Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            if (!_docs.Loaded)
                return Task.FromResult(AiToolResult.Fail("مستند ساختار پایگاه روی سرور موجود نیست."));

            var name = call.Str("name");
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult(AiToolResult.Fail("پارامتر name لازم است."));

            var body = _docs.Section(name!);
            if (body is null)
                return Task.FromResult(AiToolResult.Fail(
                    $"برای «{name}» مستندی نیست. با search_docs عبارتِ فارسی را بگرد، " +
                    "یا ساختارش را با describe_table بگیر."));

            return Task.FromResult(new AiToolResult { Rows = 1, Data = body });
        }
    }


    /// <summary>
    /// جست‌وجوی مفهومی در مستند.
    ///
    /// ── چرا لازم است ──
    /// find_column روی نامِ انگلیسیِ ستون کار می‌کند. کاربر ولی به فارسی
    /// فکر می‌کند: «تخفیف»، «ویزیتور»، «مرجوعی». این عبارت‌ها فقط در
    /// عنوان‌های فارسیِ مستند هستند، در خودِ پایگاه هیچ‌جا نیستند.
    /// </summary>
    public sealed class SearchDocsTool : IAiTool
    {
        private readonly IAiDocsIndex _docs;
        public SearchDocsTool(IAiDocsIndex docs) => _docs = docs;

        public string Name        => "search_docs";
        public string Title       => "جست‌وجو در مستند پایگاه";
        public string Description =>
            "جدول‌ها و ویوهایی که این عبارت در مستندشان آمده — عبارتِ فارسی " +
            "هم کار می‌کند. راهِ رسیدن از مفهومِ سؤالِ کاربر به جدولِ درست. " +
            "این پایگاه ۳۸۳ ویو دارد و گزارشِ خواسته‌شده اغلب از قبل ساخته " +
            "شده؛ پیش از نوشتنِ کوئریِ سنگین، اینجا بگرد.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public bool   RequiresRawSql => true;
        public string Parameters     => "q: عبارتِ جست‌وجو، فارسی یا انگلیسی (الزامی).";

        public Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            if (!_docs.Loaded)
                return Task.FromResult(AiToolResult.Fail("مستند ساختار پایگاه روی سرور موجود نیست."));

            var q = call.Str("q");
            if (string.IsNullOrWhiteSpace(q))
                return Task.FromResult(AiToolResult.Fail("پارامتر q لازم است."));

            // سقفِ خودمان، نه MaxRows: هر تطبیق یک سطرِ کوتاه است و
            // فهرستِ بلند فقط پنجره را پر می‌کند. با نامِ دقیق‌تر دوباره
            // گشتن بهتر از گرفتنِ ۵۰۰ نامِ نامرتبط است.
            var hits = _docs.Search(q!, Math.Min(call.MaxRows, 40));

            if (hits.Count == 0)
                return Task.FromResult(AiToolResult.Fail(
                    $"«{q}» در مستند نبود. عبارتِ کوتاه‌تر یا مترادفش را امتحان کن."));

            var data = hits.Select(h => new { h.Name, h.Kind, Context = h.Hit }).ToList();

            return Task.FromResult(new AiToolResult
            {
                Rows      = data.Count,
                Data      = data,
                Truncated = data.Count >= 40
            });
        }
    }
}
