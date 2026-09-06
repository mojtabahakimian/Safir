using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Ai;
using Safir.Server.Security;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Ai;
using Safir.Shared.Models.Permissions;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// دستیار هوش مصنوعی — فاز یک: دسترسی، ابزارها و لاگ.
    ///
    /// هنوز هیچ مدلی به این وصل نیست. این عمدی است: لایه‌ی دسترسی باید
    /// پیش از رسیدنِ مدل کامل و قابل آزمایش باشد، وگرنه اولین چیزی که
    /// تست می‌شود «مدل جواب داد یا نه» می‌شود و نه «آیا کاربر چیزی دید
    /// که نباید».
    /// </summary>
    [ApiController]
    [Route("api/ai")]
    [Authorize]
    public class AiAssistantController : ControllerBase
    {
        private readonly IAiAccessService       _access;
        private readonly IAiToolRegistry        _tools;
        private readonly IDatabaseService       _db;
        private readonly IAiConversationService _chat;

        public AiAssistantController(
            IAiAccessService access, IAiToolRegistry tools, IDatabaseService db,
            IAiConversationService chat)
        {
            _access = access;
            _tools  = tools;
            _db     = db;
            _chat   = chat;
        }

        private int CurrentUserCo
        {
            get
            {
                var c = User.FindFirst(BaseknowClaimTypes.IDD) ?? User.FindFirst(ClaimTypes.NameIdentifier);
                return c is not null && int.TryParse(c.Value, out var i) ? i : 0;
            }
        }

        private string? CurrentUserName => User.FindFirst(BaseknowClaimTypes.UUSER)?.Value;

        // ─────────────────── کاربر ───────────────────

        /// <summary>دستیار برای منِ کاربر چه می‌تواند بکند.</summary>
        [HttpGet("access")]
        public async Task<ActionResult<AiEffectiveAccessDto>> GetMyAccess()
            => Ok(await _access.GetEffectiveAsync(CurrentUserCo));

        /// <summary>
        /// پرسیدن یک سؤال از دستیار.
        ///
        /// تاریخچه از کلاینت می‌آید ولی هیچ مجوزی از آن گرفته نمی‌شود —
        /// دسترسی همیشه از هویتِ همین درخواست و جدولِ دسترسی خوانده
        /// می‌شود، وگرنه یک کلاینت دستکاری‌شده می‌توانست با جعل تاریخچه
        /// خودش را مجاز نشان بدهد.
        /// </summary>
        [HttpPost("chat")]
        public async Task<ActionResult<AiChatReplyDto>> Chat([FromBody] AiChatRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Question))
                return BadRequest("سؤال خالی است.");

            var convId = req.ConversationId ?? Guid.NewGuid();

            var reply = await _chat.AskAsync(
                CurrentUserCo, CurrentUserName, convId,
                req.History, req.Question.Trim(), HttpContext.RequestAborted);

            Response.Headers["X-Conversation-Id"] = convId.ToString();
            return Ok(reply);
        }

        /// <summary>
        /// اجرای مستقیم یک ابزار. فعلاً کاربر (یا آزمایش) مستقیم صدا می‌زند؛ در
        /// فاز بعد همین مسیر را حلقه‌ی عامل صدا می‌زند. مجوز در هر دو حالت
        /// یکی است — عمداً، تا رسیدن مدل هیچ در امنیت عوض نکند.
        /// </summary>
        [HttpPost("tools/{name}")]
        public async Task<IActionResult> RunTool(
            string name, [FromBody] JsonElement args, [FromQuery] Guid? conversationId = null)
        {
            var userCo = CurrentUserCo;
            var convId = conversationId ?? Guid.NewGuid();
            var tool   = _tools.Find(name);

            if (tool is null) return NotFound($"ابزار «{name}» وجود ندارد.");

            var (allowed, reason) = await _access.CanUseToolAsync(userCo, tool);

            if (!allowed)
            {
                await _access.LogAsync(new AiLogEntry
                {
                    ConversationId = convId, UserCo = userCo, UserName = CurrentUserName,
                    Kind = 2, ToolName = name, Payload = args.ToString(),
                    Allowed = false, DenyReason = reason
                });

                return StatusCode(403, reason);
            }

            var eff = await _access.GetEffectiveAsync(userCo);
            var sw  = Stopwatch.StartNew();

            AiToolResult result;
            try
            {
                result = await tool.ExecuteAsync(new AiToolCall
                {
                    UserCo  = userCo,
                    MaxRows = eff.MaxRows,
                    Args    = args
                }, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                // پیام خام خطای SQL به کاربر نمی‌رود ولی در لاگ می‌ماند —
                // آنجا نام جدول و ستون لازم است، اینجا فقط سردرگمی می‌سازد.
                await _access.LogAsync(new AiLogEntry
                {
                    ConversationId = convId, UserCo = userCo, UserName = CurrentUserName,
                    Kind = 2, ToolName = name, Payload = args.ToString(),
                    Allowed = true, DenyReason = ex.Message, DurationMs = (int)sw.ElapsedMilliseconds
                });

                return BadRequest("اجرای این درخواست ممکن نشد.");
            }

            await _access.LogAsync(new AiLogEntry
            {
                ConversationId = convId, UserCo = userCo, UserName = CurrentUserName,
                Kind = 2, ToolName = name, Payload = args.ToString(),
                RowsReturned = result.Rows, Allowed = true,
                DurationMs = (int)sw.ElapsedMilliseconds
            });

            return Ok(result);
        }

        // ─────────────────── ادمین ───────────────────
        //
        // زیر PAY2_ADMIN_ACL می‌نشیند، یعنی همان مجوزی که «چه کسی به چه
        // چیزی دسترسی دارد» را تعیین می‌کند. دادنِ دسترسیِ چت هم دقیقاً
        // همان جنس تصمیم است، نه کارِ کارشناس بهای تمام‌شده.

        [HttpGet("admin/access")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<AiUserAccessDto>>> ListAccess()
            => Ok(await _db.DoGetDataSQLAsync<AiUserAccessDto>(@"
                SELECT  a.UserCo, a.IsEnabled, a.Mode, a.AllowRawSql, a.MaxRows,
                        a.DailyMessages, a.BlockedForms, a.Note,
                        a.UpdatedBy, a.UpdatedAtUtc,
                        dbo.AI_fn_TodayMessageCount(a.UserCo) AS TodayMessages
                FROM    dbo.AI_UserAccess a
                ORDER BY a.UserCo"));

        [HttpPut("admin/access/{userCo:int}")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Upd)]
        public async Task<IActionResult> UpsertAccess(int userCo, [FromBody] UpsertAiAccessRequest req)
        {
            if (req.MaxRows       is < 1 or > 20000) return BadRequest("سقف سطر باید بین ۱ تا ۲۰۰۰۰ باشد.");
            if (req.DailyMessages is < 0 or > 10000) return BadRequest("سقف پیام روزانه باید بین ۰ تا ۱۰۰۰۰ باشد.");
            if (req.Mode > 1)                        return BadRequest("حالت نامعتبر است.");

            await _db.DoExecuteSQLAsync(@"
                MERGE dbo.AI_UserAccess AS t
                USING (SELECT @userCo AS UserCo) AS s ON t.UserCo = s.UserCo
                WHEN MATCHED THEN UPDATE SET
                    IsEnabled = @IsEnabled, Mode = @Mode, AllowRawSql = @AllowRawSql,
                    MaxRows = @MaxRows, DailyMessages = @DailyMessages,
                    BlockedForms = @BlockedForms, Note = @Note,
                    UpdatedBy = @user, UpdatedAtUtc = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN INSERT
                    (UserCo, IsEnabled, Mode, AllowRawSql, MaxRows, DailyMessages,
                     BlockedForms, Note, UpdatedBy)
                    VALUES (@userCo, @IsEnabled, @Mode, @AllowRawSql, @MaxRows,
                            @DailyMessages, @BlockedForms, @Note, @user);",
                new
                {
                    userCo, req.IsEnabled, req.Mode, req.AllowRawSql, req.MaxRows,
                    req.DailyMessages, req.BlockedForms, req.Note,
                    user = CurrentUserName
                });

            return Ok();
        }

        [HttpDelete("admin/access/{userCo:int}")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteAccess(int userCo)
        {
            // حذف یعنی «چت ندارد» — چون نبودنِ سطر پیش‌فرضِ بسته است.
            await _db.DoExecuteSQLAsync(
                "DELETE FROM dbo.AI_UserAccess WHERE UserCo = @userCo", new { userCo });

            return Ok();
        }

        /// <summary>لاگ — برای بازرسی اینکه چه کسی چه چیزی از دستیار پرسید.</summary>
        [HttpGet("admin/log")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<dynamic>>> GetLog(
            [FromQuery] int? userCo = null, [FromQuery] int take = 200)
            => Ok(await _db.DoGetDataSQLAsync<dynamic>(@"
                SELECT TOP (@take) Id, ConversationId, UserCo, UserName, AtUtc, Kind,
                       ToolName, Payload, RowsReturned, Allowed, DenyReason, DurationMs
                FROM   dbo.AI_ChatLog
                WHERE  (@userCo IS NULL OR UserCo = @userCo)
                ORDER BY Id DESC", new { take = Math.Clamp(take, 1, 2000), userCo }));
    }
}
