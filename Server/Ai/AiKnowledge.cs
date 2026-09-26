using Safir.Server.Security;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Ai;

namespace Safir.Server.Ai
{
    /// <summary>
    /// دانش کسب‌وکارِ همین شرکت که حسابدار از صفحه‌ی تنظیمات دستیار می‌نویسد (AI_Knowledge).
    ///
    /// ── چرا جدا از پرامپت و AiDataDictionary ──
    /// آن دو در کدند و برای همه‌ی مشتری‌ها یکی‌اند. «حساب ۱۲۸ جاری شرکاست» یا «فروش ویزیتور
    /// از ویو X درمی‌آید» مالِ یک شرکت است و حسابدارش باید بدون برنامه‌نویس عوضش کند.
    ///
    /// یادداشتِ «همیشه» در پرامپتِ هر گفتگو می‌آید (با سقف طول)؛ بقیه را مدل با business_notes
    /// وقتی لازم دارد می‌خواند، تا پرامپت بزرگ و گران نشود.
    /// </summary>
    public interface IAiKnowledgeStore
    {
        Task<List<AiKnowledgeDto>> AllAsync();
        Task<string?> PromptBlockAsync();
        Task<List<AiKnowledgeDto>> SearchAsync(string q);
        Task<int> SaveAsync(AiKnowledgeDto note, string? user);
        Task DeleteAsync(int id);
    }

    public sealed class AiKnowledgeStore : IAiKnowledgeStore
    {
        /// <summary>سقفِ متنِ یادداشت‌های «همیشه» در پرامپت؛ بیشتر از این باید جست‌وجویی باشد.</summary>
        public const int PromptBudget = 4000;

        private readonly IDatabaseService _db;
        private readonly ILogger<AiKnowledgeStore> _log;

        public AiKnowledgeStore(IDatabaseService db, ILogger<AiKnowledgeStore> log)
        {
            _db  = db;
            _log = log;
        }

        public async Task<List<AiKnowledgeDto>> AllAsync() =>
            (await _db.DoGetDataSQLAsync<AiKnowledgeDto>(
                "SELECT Id, Title, Body, AlwaysInPrompt, IsActive, UpdatedBy, UpdatedAtUtc " +
                "FROM dbo.AI_Knowledge ORDER BY AlwaysInPrompt DESC, Title")).ToList();

        public async Task<string?> PromptBlockAsync()
        {
            // پایگاهی که هنوز مهاجرت ۴۰ را نگرفته نباید کل دستیار را از کار بیندازد
            try
            {
                var notes = (await _db.DoGetDataSQLAsync<AiKnowledgeDto>(
                    "SELECT Title, Body FROM dbo.AI_Knowledge WHERE IsActive = 1 AND AlwaysInPrompt = 1 ORDER BY Id")).ToList();
                return Block(notes);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AI_Knowledge خوانده نشد؛ پرامپت بدون دانش کسب‌وکار ساخته می‌شود.");
                return null;
            }
        }

        /// <summary>متنِ پرامپت از یادداشت‌ها، با سقف طول — یادداشتی که جا نشد صریحاً گفته می‌شود.</summary>
        public static string? Block(IEnumerable<AiKnowledgeDto> notes)
        {
            var sb = new System.Text.StringBuilder();
            int skipped = 0;
            const string cut = " …(بقیه‌اش با business_notes)\n";
            foreach (var n in notes)
            {
                var line = $"• {n.Title}: {n.Body.Trim()}\n";
                int room = PromptBudget - sb.Length;
                if (line.Length <= room) { sb.Append(line); continue; }

                // یادداشتِ بلند (صفحه تا ۴۰۰۰ نویسه می‌پذیرد) قبلاً کامل حذف می‌شد، حتی وقتی
                // تنها یادداشت بود؛ حالا سرش می‌آید و بقیه‌اش جست‌وجو می‌شود.
                if (room - cut.Length >= 200) sb.Append(line, 0, room - cut.Length).Append(cut);
                else skipped++;
            }
            if (skipped > 0)
                sb.Append($"({skipped} یادداشت دیگر جا نشد؛ با business_notes بگرد.)\n");
            return sb.Length == 0 ? null : sb.ToString();
        }

        public async Task<List<AiKnowledgeDto>> SearchAsync(string q)
        {
            var like = "%" + AiText.NormalizeFa(q) + "%";
            return (await _db.DoGetDataSQLAsync<AiKnowledgeDto>(
                "SELECT TOP (20) Id, Title, Body FROM dbo.AI_Knowledge WHERE IsActive = 1 AND (" +
                AiText.SqlFa("Title") + " LIKE @like OR " + AiText.SqlFa("Body") + " LIKE @like) ORDER BY Id",
                new { like })).ToList();
        }

        public async Task<int> SaveAsync(AiKnowledgeDto n, string? user)
        {
            if (n.Id == 0)
                return (await _db.DoGetDataSQLAsync<int>(@"
                    INSERT dbo.AI_Knowledge (Title, Body, AlwaysInPrompt, IsActive, UpdatedBy)
                    OUTPUT INSERTED.Id
                    VALUES (@Title, @Body, @AlwaysInPrompt, @IsActive, @user)",
                    new { n.Title, n.Body, n.AlwaysInPrompt, n.IsActive, user })).First();

            await _db.DoExecuteSQLAsync(@"
                UPDATE dbo.AI_Knowledge
                SET Title = @Title, Body = @Body, AlwaysInPrompt = @AlwaysInPrompt, IsActive = @IsActive,
                    UpdatedBy = @user, UpdatedAtUtc = SYSUTCDATETIME()
                WHERE Id = @Id",
                new { n.Id, n.Title, n.Body, n.AlwaysInPrompt, n.IsActive, user });
            return n.Id;
        }

        public Task DeleteAsync(int id) =>
            _db.DoExecuteSQLAsync("DELETE dbo.AI_Knowledge WHERE Id = @id", new { id });
    }

    /// <summary>جست‌وجو در یادداشت‌های حسابدار — عدد نمی‌دهد، فقط «کجا و چطور».</summary>
    public sealed class BusinessNotesTool : IAiTool
    {
        private readonly IAiKnowledgeStore _store;
        public BusinessNotesTool(IAiKnowledgeStore store) => _store = store;

        public string Name        => "business_notes";
        public string Title       => "یادداشت‌های حسابدار";
        public string Description =>
            "قاعده‌ها و توضیح‌هایی که حسابدارِ همین شرکت نوشته: کدام حساب چه نقشی دارد، گزارش X در کدام جدول " +
            "یا ویو است، چه چیزی جزو چه چیزی حساب می‌شود. پیش از نوشتن کوئری برای مفهومی که ابزار ثابت ندارد، " +
            "اینجا بگرد؛ این یادداشت‌ها بر حدس تو و بر مستند عمومی مقدم‌اند.";
        public string RequiredForm   => CostForms.Dashboard;
        public Pay2Perm RequiredPerm => Pay2Perm.See;
        public string Parameters     => "q: عبارت جست‌وجو، فارسی (الزامی).";

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
        {
            var q = call.Str("q");
            if (string.IsNullOrWhiteSpace(q)) return AiToolResult.Fail("پارامتر q لازم است.");

            List<AiKnowledgeDto> hits;
            try { hits = await _store.SearchAsync(q!); }
            catch { return AiToolResult.Fail("یادداشت‌های حسابدار روی این پایگاه موجود نیست (مهاجرت ۴۰)."); }

            return new AiToolResult
            {
                Rows = hits.Count,
                Data = hits.Count == 0
                    ? (object)new { Found = false, Message = "یادداشتی پیدا نشد. حدس نزن؛ اگر با ابزار دیگر هم معلوم نشد، بگو نمی‌دانم." }
                    : new { Found = true, Notes = hits.Select(h => new { h.Title, h.Body }) }
            };
        }
    }
}
