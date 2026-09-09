using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Ai;
using Safir.Server.Security;

namespace Safir.Server.Ai
{
    /// <summary>
    /// تصمیم می‌گیرد چت برای یک کاربر چه کاری می‌تواند بکند.
    ///
    /// ── چرا اشتراک و نه فقط جدولِ AI_UserAccess ──
    /// اگر تنها همان جدول ملاک بود، یک تیکِ اشتباه در صفحه‌ی ادمین کافی
    /// بود تا کاربری که در برنامه حقوق و دستمزد نمی‌بیند، با یک جمله در
    /// چت همه‌اش را بگیرد. پس هر ابزار، پیش از اجرا، *هم* مجوز چت را
    /// می‌خواهد و *هم* همان دسترسیِ فرمی که خودِ کاربر برای دیدن آن صفحه
    /// لازم دارد. کاربری که سطر AI ندارد اصلاً چت ندارد — پیش‌فرضِ بسته،
    /// وگرنه هر کاربر تازه‌ای فردا بی‌سروصدا دسترسی پیدا می‌کرد.
    /// </summary>
    public interface IAiAccessService
    {
        Task<AiEffectiveAccessDto> GetEffectiveAsync(int userCo);

        /// <summary>
        /// آیا این کاربر می‌تواند این ابزار را اجرا کند. دلیلِ رد هم
        /// برمی‌گردد تا هم در لاگ بنشیند و هم به کاربر گفته شود — «دسترسی
        /// ندارید» بدون توضیح، همان چیزی است که پشتیبانی را پر از تماس
        /// می‌کند.
        /// </summary>
        Task<(bool Allowed, string? Reason)> CanUseToolAsync(int userCo, IAiTool tool);

        Task LogAsync(AiLogEntry entry);

        /// <summary>سرِ گفتگو را می‌سازد یا زمانش را جلو می‌برد.</summary>
        Task TouchConversationAsync(
            Guid conversationId, int userCo, string? userName, string firstQuestion);
    }

    public sealed class AiLogEntry
    {
        public Guid    ConversationId { get; set; }
        public int     UserCo         { get; set; }
        public string? UserName       { get; set; }
        public byte    Kind           { get; set; }   // 0=کاربر 1=مدل 2=ابزار
        public string? ToolName       { get; set; }
        public string? Payload        { get; set; }
        public int?    RowsReturned   { get; set; }
        public bool    Allowed        { get; set; } = true;
        public string? DenyReason     { get; set; }
        public int?    DurationMs     { get; set; }
    }

    public sealed class AiAccessService : IAiAccessService
    {
        private readonly IDatabaseService  _db;
        private readonly IPay2AccessService _acl;
        private readonly IAiToolRegistry   _tools;

        public AiAccessService(IDatabaseService db, IPay2AccessService acl, IAiToolRegistry tools)
        {
            _db    = db;
            _acl   = acl;
            _tools = tools;
        }

        private async Task<AiUserAccessDto?> GetGrantAsync(int userCo)
            => await _db.DoGetDataSQLAsyncSingle<AiUserAccessDto>(@"
                SELECT  a.UserCo, a.IsEnabled, a.Mode, a.AllowRawSql, a.MaxRows,
                        a.DailyMessages, a.BlockedForms, a.Note,
                        a.UpdatedBy, a.UpdatedAtUtc,
                        dbo.AI_fn_TodayMessageCount(a.UserCo) AS TodayMessages
                FROM    dbo.AI_UserAccess a
                WHERE   a.UserCo = @userCo", new { userCo });

        public async Task<AiEffectiveAccessDto> GetEffectiveAsync(int userCo)
        {
            var grant = await GetGrantAsync(userCo);

            if (grant is null || !grant.IsEnabled)
                return new AiEffectiveAccessDto
                {
                    IsEnabled = false,
                    DisabledReason = "دستیار هوش مصنوعی برای این کاربر فعال نشده است. " +
                                     "از مدیر سیستم بخواهید در تنظیمات دسترسی، آن را فعال کند."
                };

            var eff = new AiEffectiveAccessDto
            {
                IsEnabled     = true,
                Mode          = grant.Mode,
                AllowRawSql   = grant.AllowRawSql,
                MaxRows       = grant.MaxRows,
                DailyMessages = grant.DailyMessages,
                TodayMessages = grant.TodayMessages
            };

            if (eff.QuotaExhausted)
                eff.DisabledReason =
                    $"سقف {grant.DailyMessages} پیام در روز پر شده است. فردا دوباره در دسترس است.";

            var blocked = ParseForms(grant.BlockedForms);

            foreach (var tool in _tools.All)
            {
                if (tool.RequiresRawSql && !grant.AllowRawSql) continue;
                if (blocked.Contains(tool.RequiredForm))        continue;
                if (!await _acl.HasAsync(userCo, tool.RequiredForm, (int)tool.RequiredPerm)) continue;

                eff.Tools.Add(new AiToolInfoDto
                {
                    Name        = tool.Name,
                    Title       = tool.Title,
                    Description = tool.Description
                });
            }

            return eff;
        }

        public async Task<(bool Allowed, string? Reason)> CanUseToolAsync(int userCo, IAiTool tool)
        {
            var grant = await GetGrantAsync(userCo);

            if (grant is null || !grant.IsEnabled)
                return (false, "دستیار برای این کاربر فعال نیست.");

            if (grant.TodayMessages >= grant.DailyMessages)
                return (false, $"سقف {grant.DailyMessages} پیام روزانه پر شده است.");

            if (tool.RequiresRawSql && !grant.AllowRawSql)
                return (false, "اجرای کوئری آزاد برای این کاربر مجاز نیست.");

            if (ParseForms(grant.BlockedForms).Contains(tool.RequiredForm))
                return (false, $"فرم «{tool.RequiredForm}» از دسترس دستیار خارج شده است.");

            // لایه‌ی دوم و تعیین‌کننده: همان دسترسی‌ای که کاربر برای دیدن
            // این اطلاعات در خودِ برنامه لازم دارد.
            if (!await _acl.HasAsync(userCo, tool.RequiredForm, (int)tool.RequiredPerm))
                return (false, "شما به این بخش دسترسی ندارید، پس دستیار هم ندارد.");

            return (true, null);
        }

        public async Task LogAsync(AiLogEntry e)
            => await _db.DoExecuteSQLAsync(@"
                INSERT dbo.AI_ChatLog
                    (ConversationId, UserCo, UserName, Kind, ToolName,
                     Payload, RowsReturned, Allowed, DenyReason, DurationMs)
                VALUES
                    (@ConversationId, @UserCo, @UserName, @Kind, @ToolName,
                     @Payload, @RowsReturned, @Allowed, @DenyReason, @DurationMs)", e);

        public async Task TouchConversationAsync(
            Guid conversationId, int userCo, string? userName, string firstQuestion)
            => await _db.DoGetStoreProcedureSQLAsync<dynamic>(
                   "dbo.AI_sp_TouchConversation",
                   new { ConversationId = conversationId, UserCo = userCo,
                         UserName = userName, FirstQuestion = firstQuestion });

        /// <summary>
        /// فهرست فرم‌های مسدود. «ي»/«ك» عربی اینجا موضوعیت ندارد چون
        /// نام فرم‌ها لاتین‌اند، ولی فاصله و خالی‌بودن باید تحمل شود —
        /// این رشته را آدم تایپ می‌کند.
        /// </summary>
        private static HashSet<string> ParseForms(string? csv)
            => string.IsNullOrWhiteSpace(csv)
             ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
             : new HashSet<string>(
                   csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                   StringComparer.OrdinalIgnoreCase);
    }
}
