using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Ai;
using Safir.Server.Security;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Ai;
using Safir.Shared.Models.Permissions;
using Safir.Shared.Utility;
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
                req.History, req.Question.Trim(),
                req.AttachmentName, req.AttachmentText,
                HttpContext.RequestAborted);

            Response.Headers["X-Conversation-Id"] = convId.ToString();
            return Ok(reply);
        }

        /// <summary>
        /// خواندن فایل پیوست و برگرداندن متنِ استخراج‌شده.
        ///
        /// فایل ذخیره نمی‌شود؛ متن به کلاینت برمی‌گردد و او آن را همراه
        /// سؤال می‌فرستد. این‌طور سرور حالتی نگه نمی‌دارد و کاربر هم پیش
        /// از فرستادن می‌بیند چه چیزی خوانده شده.
        /// </summary>
        [HttpPost("attachment")]
        [RequestSizeLimit(AiAttachmentReader.MaxFileBytes + 1024)]
        public async Task<ActionResult<AiAttachmentDto>> ReadAttachment(IFormFile file)
        {
            if (file is null || file.Length == 0)
                return BadRequest("فایلی دریافت نشد.");

            if (file.Length > AiAttachmentReader.MaxFileBytes)
                return BadRequest(
                    $"حجم فایل بیشتر از {AiAttachmentReader.MaxFileBytes / 1024 / 1024} مگابایت است.");

            if (!AiAttachmentReader.IsSupported(file.FileName))
                return BadRequest($"این نوع فایل پشتیبانی نمی‌شود. مجاز: {AiAttachmentReader.SupportedList}");

            // فقط کاربری که خودش دستیار دارد می‌تواند فایل بفرستد، وگرنه
            // این اندپوینت یک مبدلِ اکسل‌به‌متنِ رایگان برای همه می‌شد.
            var eff = await _access.GetEffectiveAsync(CurrentUserCo);
            if (!eff.IsEnabled) return StatusCode(403, eff.DisabledReason);

            try
            {
                await using var stream = file.OpenReadStream();
                var text = AiAttachmentReader.Read(stream, file.FileName);

                return Ok(new AiAttachmentDto
                {
                    FileName = file.FileName,
                    Text     = text,
                    Chars    = text.Length
                });
            }
            catch (Exception)
            {
                // پیام خام کتابخانه برای کاربر بی‌معنی است؛ چیزی که به
                // دردش می‌خورد این است که فایل خراب یا رمزدار است.
                return BadRequest("خواندن این فایل ممکن نشد. شاید خراب یا رمزگذاری‌شده باشد.");
            }
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
        {
            var rows = await _db.DoGetDataSQLAsync<AiUserAccessDto>(@"
                SELECT  a.UserCo, a.IsEnabled, a.Mode, a.AllowRawSql, a.MaxRows,
                        a.DailyMessages, a.BlockedForms, a.Note,
                        a.UpdatedBy, a.UpdatedAtUtc,
                        u.SAL_NAME AS UserName,
                        dbo.AI_fn_TodayMessageCount(a.UserCo) AS TodayMessages
                FROM    dbo.AI_UserAccess a
                LEFT    JOIN dbo.SALA_DTL u ON u.IDD = a.UserCo");

            return Ok(rows.Select(r =>
            {
                r.UserName = CL_METHODS.FixPersianChars(
                                 CL_METHODS.DECODEUN(r.UserName ?? string.Empty));
                return r;
            })
            .OrderBy(r => r.UserName)
            .ToList());
        }

        /// <summary>
        /// کاربران فعال، برای انتخاب از فهرست به‌جای تایپ کردن کد.
        /// ENABL = 0 یعنی فعال — همان شرطی که خودِ ورود به برنامه دارد،
        /// وگرنه کاربر غیرفعال هم در فهرست می‌آمد و دسترسی گرفتن برایش
        /// بی‌معنی بود.
        /// </summary>
        [HttpGet("admin/users")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<AiUserLookupDto>>> ListUsers()
        {
            var rows = await _db.DoGetDataSQLAsync<AiUserLookupDto>(@"
                SELECT  u.IDD AS UserCo, u.SAL_NAME AS UserName,
                        CAST(CASE WHEN a.UserCo IS NULL THEN 0 ELSE 1 END AS BIT) AS HasAccess
                FROM    dbo.SALA_DTL u
                LEFT    JOIN dbo.AI_UserAccess a ON a.UserCo = u.IDD
                WHERE   u.ENABL = 0");

            // ⚠ SAL_NAME رمزگذاری‌شده ذخیره می‌شود و خام نشان دادنش رشته‌ای
            // مثل «/[Z`^[XXQ^» می‌دهد. همان کدگشایی و نرمال‌سازی‌ای که
            // LookupController و صفحه‌ی ورود دارند، اینجا هم لازم است.
            // مرتب‌سازی هم بعد از کدگشایی معنی دارد، نه روی متن رمزشده.
            var list = rows.Select(u =>
            {
                u.UserName = CL_METHODS.FixPersianChars(
                                 CL_METHODS.DECODEUN(u.UserName ?? string.Empty));
                return u;
            })
            .OrderBy(u => u.UserName)
            .ToList();

            return Ok(list);
        }

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

        // ─────────────────── تنظیمات سرویس ───────────────────

        /// <summary>
        /// تنظیمات فعلی. ⚠ کلید برنمی‌گردد — فقط اینکه ثبت شده و چهار
        /// نویسه‌ی آخرش، که برای «همانی است که فکر می‌کنم؟» کافی است.
        /// </summary>
        [HttpGet("admin/config")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.See)]
        public async Task<ActionResult<AiConfigDto>> GetConfig(
            [FromServices] IAiSettingsProvider settings)
        {
            var row = await ((AiSettingsProvider)settings).ReadRowAsync();
            var eff = await settings.GetAsync();

            var key = row?.ApiKey;

            return Ok(new AiConfigDto
            {
                IsEnabled      = row?.IsEnabled ?? false,
                Provider       = row?.Provider ?? eff.Provider,
                BaseUrl        = string.IsNullOrWhiteSpace(row?.BaseUrl) ? eff.BaseUrl : row!.BaseUrl,
                Model          = string.IsNullOrWhiteSpace(row?.Model)   ? eff.Model   : row!.Model,
                TimeoutSeconds = row?.TimeoutSeconds > 0 ? row.TimeoutSeconds : eff.TimeoutSeconds,
                MaxToolLoops   = row?.MaxToolLoops   > 0 ? row.MaxToolLoops   : eff.MaxToolLoops,
                // ⚠ از خودِ سطر، نه از تنظیماتِ مؤثر. وقتی IsEnabled خاموش
                // است تنظیماتِ مؤثر این سطر را نادیده می‌گیرد، و صفحه
                // می‌نوشت «کلیدی ثبت نشده» در حالی که کلید ذخیره شده بود.
                HasApiKey      = !string.IsNullOrWhiteSpace(key) ||
                                 !string.IsNullOrWhiteSpace(eff.ApiKey),
                ApiKeyTail     = key is { Length: >= 4 } ? key[^4..] : null,
                // کلیدی هست ولی در پایگاه نیست، پس از محیط آمده. ادمین باید
                // بداند چرا صفحه کلید نشان نمی‌دهد ولی سرویس کار می‌کند.
                KeyFromEnv     = string.IsNullOrWhiteSpace(key) &&
                                 !string.IsNullOrWhiteSpace(eff.ApiKey),
                UpdatedBy      = row?.UpdatedBy,
                UpdatedAtUtc   = row?.UpdatedAtUtc
            });
        }

        [HttpPut("admin/config")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Upd)]
        public async Task<IActionResult> SaveConfig(
            [FromBody] UpsertAiConfigRequest req, [FromServices] IAiSettingsProvider settings)
        {
            if (req.TimeoutSeconds is < 5 or > 900) return BadRequest("تایم‌اوت باید بین ۵ تا ۹۰۰ ثانیه باشد.");
            if (req.MaxToolLoops   is < 1 or > 30)  return BadRequest("سقف مراحل باید بین ۱ تا ۳۰ باشد.");

            if (req.IsEnabled && string.IsNullOrWhiteSpace(req.BaseUrl))
                return BadRequest("برای فعال کردن سرویس، آدرس لازم است.");

            // آدرس باید ریشه باشد. مسیرِ صفحه‌ی وبِ درگاه اشتباهِ رایجی است
            // که ۳۰۷ به صفحه‌ی ورود می‌دهد و پیامش برای کاربر بی‌معنی است.
            if (!string.IsNullOrWhiteSpace(req.BaseUrl) &&
                !Uri.TryCreate(req.BaseUrl, UriKind.Absolute, out _))
                return BadRequest("آدرس معتبر نیست. نمونه: http://localhost:20128");

            await _db.DoExecuteSQLAsync(@"
                UPDATE dbo.AI_Config
                SET IsEnabled = @IsEnabled, Provider = @Provider, BaseUrl = @BaseUrl,
                    Model = @Model, TimeoutSeconds = @TimeoutSeconds,
                    MaxToolLoops = @MaxToolLoops,
                    -- کلید فقط وقتی عوض می‌شود که مقدارِ تازه آمده باشد یا
                    -- صراحتاً پاک‌کردن خواسته شده باشد. ذخیره‌ی ساده‌ی
                    -- تنظیماتِ دیگر نباید کلید را بی‌سروصدا بشوید.
                    ApiKey = CASE WHEN @ClearApiKey = 1 THEN NULL
                                  WHEN @ApiKey IS NOT NULL AND LEN(@ApiKey) > 0 THEN @ApiKey
                                  ELSE ApiKey END,
                    UpdatedBy = @user, UpdatedAtUtc = SYSUTCDATETIME()
                WHERE Id = 1",
                new
                {
                    req.IsEnabled, req.Provider, req.BaseUrl, req.Model,
                    req.TimeoutSeconds, req.MaxToolLoops,
                    req.ApiKey, ClearApiKey = req.ClearApiKey ? 1 : 0,
                    user = CurrentUserName
                });

            settings.Invalidate();
            return Ok();
        }

        /// <summary>
        /// آزمایش اتصال: فهرست مدل‌های سرویس را می‌گیرد. هم آدرس و کلید را
        /// می‌سنجد و هم نام دقیق مدل‌ها را نشان می‌دهد تا ادمین مجبور نباشد
        /// حدس بزند یا از جای دیگری کپی کند.
        /// </summary>
        [HttpPost("admin/config/test")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.See)]
        public async Task<ActionResult<AiConnectionTestDto>> TestConnection(
            [FromServices] IAiSettingsProvider settings,
            [FromServices] IHttpClientFactory httpFactory,
            [FromBody] UpsertAiConfigRequest? draft = null)
        {
            // ⚠ کل بدنه داخل try است، نه فقط بخشِ شبکه. اولین نسخه فقط
            // فراخوانی HTTP را می‌گرفت و خطای خواندنِ تنظیمات به‌صورت
            // ۵۰۰ بیرون می‌زد — یعنی همان چیزی که این دکمه باید تشخیص
            // بدهد، خودش تبدیل به خطای بی‌توضیح می‌شد.
            AiOptions opt;
            try
            {
                opt = await settings.GetAsync();
            }
            catch (Exception ex)
            {
                return Ok(new AiConnectionTestDto
                {
                    Ok = false,
                    Message = "خواندن تنظیمات ممکن نشد: " + ex.Message
                });
            }

            // مقادیرِ روی فرم بر تنظیماتِ ذخیره‌شده اولویت دارند.
            //
            // بدون این، ادمین آدرس و کلید را تایپ می‌کرد، «آزمایش اتصال»
            // می‌زد و سرور تنظیماتِ *قدیمی* را می‌سنجید — دقیقاً همان
            // چیزی که یک بار پیش آمد و نتیجه‌اش گیج‌کننده بود. کلیدِ خالی
            // یعنی «همان کلیدِ ذخیره‌شده»، تا برای آزمایش لازم نباشد
            // دوباره تایپش کند.
            if (draft is not null)
            {
                if (!string.IsNullOrWhiteSpace(draft.Provider)) opt.Provider = draft.Provider;
                if (!string.IsNullOrWhiteSpace(draft.BaseUrl))  opt.BaseUrl  = draft.BaseUrl;
                if (!string.IsNullOrWhiteSpace(draft.ApiKey))   opt.ApiKey   = draft.ApiKey;
            }

            if (string.IsNullOrWhiteSpace(opt.BaseUrl))
                return Ok(new AiConnectionTestDto { Ok = false, Message = "آدرس سرویس تنظیم نشده است." });

            var http = httpFactory.CreateClient("ai");
            http.Timeout = TimeSpan.FromSeconds(20);

            if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opt.ApiKey);

            try
            {
                var res = await http.GetAsync(AiUrl.Combine(opt.BaseUrl, "/v1/models"));
                var raw = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                    return Ok(new AiConnectionTestDto
                    {
                        Ok = false,
                        // متنِ خودِ سرویس هم می‌آید: «Missing API key» خیلی
                        // گویاتر از «کد ۴۰۱» است.
                        Message = $"سرویس خطا داد (کد {(int)res.StatusCode}). " +
                                  (raw.Length > 300 ? raw[..300] : raw)
                    });

                var models = new List<string>();
                using (var doc = JsonDocument.Parse(raw))
                {
                    if (doc.RootElement.TryGetProperty("data", out var arr) &&
                        arr.ValueKind == JsonValueKind.Array)
                        foreach (var m in arr.EnumerateArray())
                            if (m.TryGetProperty("id", out var id))
                                models.Add(id.GetString() ?? "");
                }

                // ⚠ فهرست مدل‌ها روی این درگاه بدون کلید هم جواب می‌دهد، پس
                // موفق شدنش هیچ چیزی درباره‌ی کلید ثابت نمی‌کند — یک بار
                // «اتصال برقرار است» داد در حالی که کلیدی در کار نبود و
                // بعد خودِ گفتگو ۴۰۱ گرفت. آزمایش واقعی یک درخواستِ
                // کوچکِ تولید است که هم کلید و هم نامِ مدل را می‌سنجد.
                var model = string.IsNullOrWhiteSpace(draft?.Model) ? opt.Model : draft!.Model;

                var probe = await http.PostAsJsonAsync(
                    AiUrl.Combine(opt.BaseUrl, "/v1/chat/completions"),
                    new
                    {
                        model,
                        max_tokens = 8,
                        messages = new[] { new { role = "user", content = "ping" } }
                    });

                var probeRaw = await probe.Content.ReadAsStringAsync();

                if (!probe.IsSuccessStatusCode)
                    return Ok(new AiConnectionTestDto
                    {
                        Ok = false,
                        Message = $"فهرست مدل‌ها گرفته شد ولی خودِ مدل جواب نداد " +
                                  $"(کد {(int)probe.StatusCode}): " +
                                  (probeRaw.Length > 300 ? probeRaw[..300] : probeRaw),
                        Models = models
                    });

                return Ok(new AiConnectionTestDto
                {
                    Ok = true,
                    Message = $"اتصال و کلید سالم است. مدل «{model}» جواب داد. " +
                              $"{models.Count} مدل در دسترس.",
                    Models = models
                });
            }
            catch (Exception ex)
            {
                return Ok(new AiConnectionTestDto
                {
                    Ok = false,
                    Message = "اتصال برقرار نشد: " + ex.Message
                });
            }
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
