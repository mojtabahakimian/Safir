using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Automation;
using Safir.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<MessagesController> _logger;

        public MessagesController(IDatabaseService dbService, ILogger<MessagesController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MessageModel>>> GetMessages(
            [FromQuery] bool includeSent = true,
            [FromQuery] bool includeReceived = true
        )
        {
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUsername = User.Identity?.Name;
            if (!int.TryParse(currentUserIdClaim, out int currentUserId) || string.IsNullOrEmpty(currentUsername)) return Unauthorized("Invalid user token.");

            if (!includeReceived && !includeSent) return Ok(Enumerable.Empty<MessageModel>());

            try
            {
                var conditions = new List<string>();
                var parameters = new DynamicParameters();
                parameters.Add("UserId", currentUserId);
                parameters.Add("Username", currentUsername);
                if (includeReceived) conditions.Add("M.PERSONEL = @UserId");
                if (includeSent) conditions.Add("M.USERNAME = @Username");
                string filterClause = $"WHERE ({string.Join(" OR ", conditions)})";

                // Fetch raw date/time
                string sql = $@"SELECT M.IDNUM, M.PERSONEL, M.PAYAM, M.STATUS,
                                   M.STDATE as STDATE_DB, M.STTIME as STTIME_DB,
                                   M.USERNAME, M.COMP_COD, M.CRT, M.UID, CH.NAME
                            FROM dbo.MESAGEP M
                            LEFT OUTER JOIN dbo.CUST_HESAB CH ON M.COMP_COD = CH.hes
                            {filterClause}
                            ORDER BY M.CRT DESC";

                var messagesRaw = await _dbService.DoGetDataSQLAsync<dynamic>(sql, parameters);
                _logger.LogInformation("API: GetMessages - Raw data count for UserID {UserId}: {Count}", currentUserId, messagesRaw?.Count() ?? 0); // لاگ تعداد خام
                if (messagesRaw == null) return Ok(Enumerable.Empty<MessageModel>());

                // Convert in C#
                List<MessageModel> messages = messagesRaw.Select(m => new MessageModel
                {
                    IDNUM = m.IDNUM,
                    PERSONEL = m.PERSONEL,
                    PAYAM = m.PAYAM,
                    STATUS = m.STATUS,
                    USERNAME = m.USERNAME,
                    COMP_COD = m.COMP_COD,
                    CRT = m.CRT,
                    UID = m.UID,
                    NAME = m.NAME,
                    STDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(m.STDATE_DB),
                    STTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(m.STTIME_DB)
                }).ToList();
                _logger.LogInformation("API: GetMessages - Converted data count for UserID {UserId}: {Count}", currentUserId, messages.Count); // لاگ تعداد نهایی
                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error fetching messages for UserID: {UserId}", currentUserId);
                return StatusCode(500, "Internal server error while fetching messages.");
            }
        }

        [HttpPost]
        public async Task<ActionResult> SendMessage([FromBody] MessageSendRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (request == null || request.RecipientUserIds == null || !request.RecipientUserIds.Any()) return BadRequest("حداقل یک گیرنده پیام باید مشخص شود.");
            if (string.IsNullOrWhiteSpace(request.MessageText)) return BadRequest("متن پیام نمی‌تواند خالی باشد.");

            var senderUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var senderUsername = User.Identity?.Name;
            if (!int.TryParse(senderUserIdClaim, out int senderUserCod) || string.IsNullOrEmpty(senderUsername)) return Unauthorized("Invalid sender token.");

            try
            {
                string sql = @"INSERT INTO dbo.MESAGEP
                                (PERSONEL, COMP_COD, PAYAM, STATUS, STDATE, STTIME, USERNAME, UID, CRT)
                             VALUES
                                (@RecipientUserId, @CompCod, @Payam, @Status, @StDate, @StTime, @SenderUsername, @SenderUserCod, GETDATE())";

                int successCount = 0;
                List<int> failedRecipients = new List<int>();
                long currentDate = CL_Tarikh.GetCurrentPersianDateAsLong();
                int currentTime = int.Parse(DateTime.Now.ToString("HHmm"));

                foreach (var recipientId in request.RecipientUserIds.Distinct())
                {
                    if (recipientId <= 0) continue;
                    var parameters = new
                    {
                        RecipientUserId = recipientId,
                        request.CompCod,
                        Payam = request.MessageText.Trim(),
                        Status = 1,
                        StDate = currentDate,
                        StTime = currentTime,
                        SenderUsername = senderUsername,
                        SenderUserCod = senderUserCod
                    };
                    try
                    {
                        int result = await _dbService.DoExecuteSQLAsync(sql, parameters);
                        if (result > 0) successCount++;
                        else failedRecipients.Add(recipientId);
                    }
                    catch (Exception insertEx) { _logger.LogError(insertEx, "API: SendMessage - Error inserting message for Recipient: {RecipientId}", recipientId); failedRecipients.Add(recipientId); }
                }

                if (successCount == request.RecipientUserIds.Count) return Ok(new { Message = "پیام(ها) با موفقیت ارسال شد." });
                else return StatusCode(207, new { Message = $"پیام برای {successCount} نفر از {request.RecipientUserIds.Count} نفر ارسال شد.", FailedRecipients = failedRecipients });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: General error sending message from user {SenderUsername}.", senderUsername);
                return StatusCode(500, "Internal server error while sending message.");
            }
        }

        [HttpGet("unread-count")]
        public async Task<ActionResult<int>> GetUnreadMessageCount()
        {
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserId)) return Unauthorized("Invalid user token.");
            try
            {
                string sql = "SELECT COUNT(*) FROM MESAGEP WHERE STATUS = 1 AND PERSONEL = @UserId";
                int count = await _dbService.DoGetDataSQLAsyncSingle<int>(sql, new { UserId = currentUserId });
                return Ok(count);
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error getting unread message count for UserID: {UserId}", currentUserId); return StatusCode(500, "Internal server error."); }
        }

        [HttpPut("{idnum}/mark-read")]
        public async Task<IActionResult> MarkMessageAsRead(long idnum)
        {
            if (idnum <= 0) return BadRequest("Message ID نامعتبر است.");
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserId)) return Unauthorized("Invalid user token.");
            try
            {
                string sql = "UPDATE MESAGEP SET STATUS = 2 WHERE IDNUM = @Idnum AND PERSONEL = @UserId AND STATUS = 1";
                int rowsAffected = await _dbService.DoExecuteSQLAsync(sql, new { Idnum = idnum, UserId = currentUserId });
                if (rowsAffected > 0) return NoContent();
                else return NotFound($"Message {idnum} not found or cannot be marked as read by this user.");
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error marking message {Idnum} as read for user {UserId}", idnum, currentUserId); return StatusCode(500, "Internal server error."); }
        }
    }
}