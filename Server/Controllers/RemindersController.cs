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
    public class RemindersController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<RemindersController> _logger;

        public RemindersController(IDatabaseService dbService, ILogger<RemindersController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ReminderModel>>> GetReminders([FromQuery] int? statusFilter = null)
        {
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserId)) return Unauthorized("Invalid user token.");

            try
            {
                var conditions = new List<string> { "R.PERSONEL = @UserId" };
                var parameters = new DynamicParameters();
                parameters.Add("UserId", currentUserId);
                if (statusFilter.HasValue && statusFilter.Value >= 1 && statusFilter.Value <= 3) { conditions.Add("R.STATUS = @Status"); parameters.Add("Status", statusFilter.Value); }
                string whereClause = $"WHERE {string.Join(" AND ", conditions)}";

                // Fetch raw date/time
                string sql = $@"SELECT R.IDNUM, R.PERSONEL, R.PAYAM, R.STATUS,
                                   R.CTDATE AS CTDATE_DB, R.CTTIME AS CTTIME_DB,
                                   R.USERNAME, R.COMP_COD,
                                   R.STDATE as STDATE_DB, R.STTIME as STTIME_DB,
                                   R.CRT, R.UID, CH.NAME
                            FROM dbo.REMAINDER R
                            LEFT OUTER JOIN dbo.CUST_HESAB CH ON R.COMP_COD = CH.hes
                            {whereClause}
                            ORDER BY R.STDATE DESC, R.STTIME DESC";

                var remindersRaw = await _dbService.DoGetDataSQLAsync<dynamic>(sql, parameters);
                if (remindersRaw == null) return Ok(Enumerable.Empty<ReminderModel>());

                // Convert in C#
                List<ReminderModel> reminders = remindersRaw.Select(r => new ReminderModel
                {
                    IDNUM = r.IDNUM,
                    PERSONEL = r.PERSONEL,
                    PAYAM = r.PAYAM,
                    STATUS = r.STATUS,
                    USERNAME = r.USERNAME,
                    COMP_COD = r.COMP_COD,
                    CRT = r.CRT,
                    UID = r.UID,
                    NAME = r.NAME,
                    CTDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(r.CTDATE_DB),
                    CTTIME = null, // TODO: Implement conversion from DB CTTIME format if needed
                    STDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(r.STDATE_DB),
                    STTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(r.STTIME_DB)
                }).ToList();

                return Ok(reminders);
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error fetching reminders for UserID: {UserId}", currentUserId); return StatusCode(500, "Internal server error while fetching reminders."); }
        }

        [HttpPost]
        public async Task<ActionResult> CreateReminder([FromBody] ReminderCreateRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (request == null || request.RecipientUserIds == null || !request.RecipientUserIds.Any()) return BadRequest("حداقل یک گیرنده باید مشخص شود.");
            if (!request.ReminderDate.HasValue || !request.ReminderTime.HasValue) return BadRequest("تاریخ و زمان یادآوری الزامی است.");

            var senderUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var senderUsername = User.Identity?.Name;
            if (!int.TryParse(senderUserIdClaim, out int senderUserCod) || string.IsNullOrEmpty(senderUsername)) return Unauthorized("Invalid sender token.");

            long? reminderDateLong = CL_Tarikh.ConvertToPersianDateLong(request.ReminderDate);
            if (!reminderDateLong.HasValue) return BadRequest("فرمت تاریخ یادآوری نامعتبر است.");
            int? reminderTimeInt = CL_Tarikh.ConvertTimeToInt(request.ReminderTime); // Use helper

            long currentCtDate = CL_Tarikh.GetCurrentPersianDateAsLong();
            int currentCtTimeInt = int.Parse(DateTime.Now.ToString("HHmm"));

            try
            {
                var validRecipients = request.RecipientUserIds.Distinct().Where(id => id > 0).ToList();
                if (!validRecipients.Any()) return BadRequest("هیچ گیرنده معتبری یافت نشد.");

                int successCount = 0;
                List<int> failedRecipients = new List<int>();
                int chunkSize = 200; // Safe size to stay well below SQL Server's 2100 parameter limit and 1000 row limits

                string singleInsertSql = @"INSERT INTO dbo.REMAINDER (PERSONEL, COMP_COD, PAYAM, STATUS, STDATE, STTIME, USERNAME, CTDATE, CTTIME, CRT, UID)
                                           VALUES (@RecipientUserId, @CompCod, @Payam, @Status, @StDate, @StTime, @SenderUsername, @CtDate, @CtTime, GETDATE(), @SenderUserCod)";

                for (int chunkIndex = 0; chunkIndex < validRecipients.Count; chunkIndex += chunkSize)
                {
                    var chunk = validRecipients.Skip(chunkIndex).Take(chunkSize).ToList();

                    var sqlBuilder = new System.Text.StringBuilder("INSERT INTO dbo.REMAINDER (PERSONEL, COMP_COD, PAYAM, STATUS, STDATE, STTIME, USERNAME, CTDATE, CTTIME, CRT, UID) VALUES ");
                    var dParams = new DynamicParameters();

                    dParams.Add("CompCod", request.CompCod);
                    dParams.Add("Payam", request.ReminderText.Trim());
                    dParams.Add("Status", 1);
                    dParams.Add("StDate", reminderDateLong.Value);
                    dParams.Add("StTime", reminderTimeInt);
                    dParams.Add("SenderUsername", senderUsername);
                    dParams.Add("CtDate", currentCtDate);
                    dParams.Add("CtTime", currentCtTimeInt);
                    dParams.Add("SenderUserCod", senderUserCod);

                    var valueClauses = new List<string>();
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        string pName = $"@RecipientUserId_{i}";
                        dParams.Add(pName, chunk[i]);
                        valueClauses.Add($"({pName}, @CompCod, @Payam, @Status, @StDate, @StTime, @SenderUsername, @CtDate, @CtTime, GETDATE(), @SenderUserCod)");
                    }

                    sqlBuilder.Append(string.Join(", ", valueClauses));

                    try
                    {
                        int rowsAffected = await _dbService.DoExecuteSQLAsync(sqlBuilder.ToString(), dParams);
                        successCount += chunk.Count;
                    }
                    catch (Exception)
                    {
                        // Fallback to single inserts if bulk fails (to find the specific failed rows)
                        foreach (var recipientId in chunk)
                        {
                            var singleParams = new
                            {
                                RecipientUserId = recipientId,
                                request.CompCod,
                                Payam = request.ReminderText.Trim(),
                                Status = 1,
                                StDate = reminderDateLong.Value,
                                StTime = reminderTimeInt,
                                SenderUsername = senderUsername,
                                CtDate = currentCtDate,
                                CtTime = currentCtTimeInt,
                                SenderUserCod = senderUserCod
                            };
                            try
                            {
                                await _dbService.DoExecuteSQLAsync(singleInsertSql, singleParams);
                                successCount++;
                            }
                            catch (Exception insertEx)
                            {
                                _logger.LogError(insertEx, "API: CreateReminder - Error inserting reminder for Recipient: {RecipientId}", recipientId);
                                failedRecipients.Add(recipientId);
                            }
                        }
                    }
                }

                if (failedRecipients.Count == 0 && successCount == validRecipients.Count) return Ok(new { Message = "یادآوری(ها) با موفقیت ثبت شد." });
                else return StatusCode(207, new { Message = $"یادآوری برای {successCount} نفر از {validRecipients.Count} نفر ثبت شد.", FailedRecipients = failedRecipients });
            }
            catch (Exception ex) { _logger.LogError(ex, "API: General error creating reminder from user {SenderUsername}.", senderUsername); return StatusCode(500, "Internal server error while creating reminder."); }
        }

        [HttpPut("{idnum}/cancel")]
        public async Task<IActionResult> CancelReminder(long idnum)
        {
            if (idnum <= 0) return BadRequest("Reminder ID نامعتبر است.");
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserId)) return Unauthorized("Invalid user token.");
            try
            {
                string sql = "UPDATE REMAINDER SET STATUS = 3 WHERE IDNUM = @Idnum AND PERSONEL = @UserId AND STATUS = 1";
                int rowsAffected = await _dbService.DoExecuteSQLAsync(sql, new { Idnum = idnum, UserId = currentUserId });
                if (rowsAffected > 0) return NoContent();
                else return NotFound($"Reminder {idnum} not found or cannot be cancelled by this user.");
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error cancelling reminder {Idnum} for user {UserId}", idnum, currentUserId); return StatusCode(500, "Internal server error."); }
        }

        [HttpGet("active-count")]
        public async Task<ActionResult<int>> GetActiveReminderCount()
        {
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserId)) return Unauthorized("Invalid user token.");
            try
            {
                string sql = "SELECT COUNT(*) FROM REMAINDER WHERE STATUS = 1 AND PERSONEL = @UserId";
                int count = await _dbService.DoGetDataSQLAsyncSingle<int>(sql, new { UserId = currentUserId });
                return Ok(count);
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error getting active reminder count for UserID: {UserId}", currentUserId); return StatusCode(500, "Internal server error."); }
        }
    }
}