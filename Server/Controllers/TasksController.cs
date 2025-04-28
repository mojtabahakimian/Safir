using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Automation;
using Safir.Shared.Utility; // Ensure CL_Tarikh is accessible
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
    public class TasksController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(IDatabaseService dbService, ILogger<TasksController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TaskModel>>> GetTasks(
            [FromQuery] int statusFilter = 1,
            [FromQuery] int? assignedUserId = null,
            [FromQuery] string? taskTypes = "1000"
        )
        {
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserId)) return Unauthorized("User ID not found in token.");
            int userIdToQuery = assignedUserId ?? currentUserId;

            try
            {
                var conditions = new List<string>();
                var parameters = new DynamicParameters();

                if (statusFilter >= 1 && statusFilter <= 3) { conditions.Add("T.STATUS = @Status"); parameters.Add("Status", statusFilter); }

                List<int> validSkids = new List<int>();
                if (!string.IsNullOrWhiteSpace(taskTypes) && taskTypes != "1000")
                {
                    var skidStrings = taskTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var skidStr in skidStrings)
                    {
                        if (int.TryParse(skidStr, out int skid)) validSkids.Add(skid);
                    }
                }
                if (validSkids.Any()) { conditions.Add("T.skid IN @Skids"); parameters.Add("Skids", validSkids); }

                conditions.Add("T.PERSONEL = @PersonelId");
                parameters.Add("PersonelId", userIdToQuery);

                string whereClause = conditions.Any() ? $"WHERE {string.Join(" AND ", conditions)}" : "";

                // Fetch raw date/time fields
                string sql = $@"SELECT
                                    T.IDNUM, CH.NAME, T.GR, T.PERSONEL, T.TASK, T.PERIORITY, T.STATUS,
                                    T.STDATE as STDATE_DB, T.STTIME as STTIME_DB,
                                    T.ENDATE as ENDATE_DB, T.ENTIME as ENTIME_DB,
                                    T.USERNAME, T.COMP_COD,
                                    T.SUMTIME as SUMTIME_DB, T.skid, T.num, T.tg, T.CTIM, T.USERCO, T.SEE,
                                    T.SEET as SEET_DB
                                FROM dbo.TASKS T
                                LEFT OUTER JOIN dbo.CUST_HESAB CH ON T.COMP_COD = CH.hes
                                {whereClause}
                                ORDER BY T.IDNUM DESC";

                var tasksRaw = await _dbService.DoGetDataSQLAsync<dynamic>(sql, parameters);
                if (tasksRaw == null) return Ok(Enumerable.Empty<TaskModel>());

                // Convert in C#
                List<TaskModel> tasks = tasksRaw.Select(t =>
                {
                    // برای جلوگیری از خطای احتمالی اگر فیلدی در یک ردیف وجود نداشت
                    var task = new TaskModel();
                    try { task.IDNUM = (long)t.IDNUM; } catch { /* Log or handle */ }
                    try { task.NAME = (string?)t.NAME; } catch { /* Log or handle */ }
                    try { task.GR = (int?)t.GR; } catch { /* Log or handle */ } // کست به int?
                    try { task.PERSONEL = (int)t.PERSONEL; } catch { /* Log or handle */ } // احتمالاً int است
                    try { task.TASK = (string?)t.TASK; } catch { /* Log or handle */ }
                    try { task.PERIORITY = (int)t.PERIORITY; } catch { /* Log or handle */ } // احتمالاً int است
                    try { task.STATUS = (int)t.STATUS; } catch { /* Log or handle */ } // احتمالاً int است
                    try { task.USERNAME = (string?)t.USERNAME; } catch { /* Log or handle */ }
                    try { task.COMP_COD = (string?)t.COMP_COD; } catch { /* Log or handle */ }
                    try { task.skid = (int?)t.skid; } catch { /* Log or handle */ }       // ***** کست صریح long به int? *****
                    try { task.num = (long?)t.num; } catch { /* Log or handle */ }         // کست به long? (برای اطمینان)
                    try { task.tg = (int?)t.tg; } catch { /* Log or handle */ }           // ***** کست صریح long به int? *****
                    try { task.CTIM = (DateTime?)t.CTIM; } catch { /* Log or handle */ }   // احتمالاً datetime است
                    try { task.USERCO = (int?)t.USERCO; } catch { /* Log or handle */ }     // ***** کست صریح long به int? *****
                    try { task.SEE = (bool?)t.SEE; } catch { /* Log or handle */ }         // کست به bool? (بسته به نوع bit در SQL)

                    // تبدیل تاریخ و زمان
                    try { task.STDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong((long?)t.STDATE_DB); } catch { /* Log or handle */ }
                    try { task.STTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt((int?)t.STTIME_DB); } catch { /* Log or handle */ }
                    try { task.ENDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong((long?)t.ENDATE_DB); } catch { /* Log or handle */ }
                    try { task.ENTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt((int?)t.ENTIME_DB); } catch { /* Log or handle */ }
                    try { task.SUMTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt((int?)t.SUMTIME_DB); } catch { /* Log or handle */ }
                    try { task.SEET = CL_Tarikh.ConvertToDateTimeFromPersianLong((long?)t.SEET_DB); } catch { /* Log or handle */ }

                    return task;

                }).ToList();

                return Ok(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error fetching tasks for UserId: {UserIdToQuery}", userIdToQuery);
                return StatusCode(500, "Internal server error while fetching tasks.");
            }
        }

        [HttpPost]
        public async Task<ActionResult<TaskModel>> CreateTask([FromBody] TaskModel newTask)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUsername = User.Identity?.Name;
            if (!int.TryParse(currentUserIdClaim, out int currentUserCod) || string.IsNullOrEmpty(currentUsername)) return Unauthorized("User info not found in token.");

            long? stDateLong = CL_Tarikh.ConvertToPersianDateLong(newTask.STDATE ?? DateTime.Now);
            int? stTimeInt = CL_Tarikh.ConvertTimeToInt(newTask.STTIME) ?? CL_Tarikh.ConvertTimeToInt(DateTime.Now);

            try
            {
                string sql = @"INSERT INTO dbo.TASKS
                               (PERSONEL, TASK, PERIORITY, STATUS, STDATE, STTIME, USERNAME, COMP_COD, USERCO, SEE, skid, num, tg, GR, CTIM)
                             OUTPUT INSERTED.IDNUM
                             VALUES
                               (@PERSONEL, @TASK, @PERIORITY, @STATUS, @STDATE, @STTIME, @USERNAME, @COMP_COD, @USERCO, @SEE, @skid, @num, @tg, @GR, GETDATE())";

                var parameters = new
                {
                    newTask.PERSONEL,
                    newTask.TASK,
                    PERIORITY = newTask.PERIORITY > 0 ? newTask.PERIORITY : 2,
                    STATUS = newTask.STATUS > 0 ? newTask.STATUS : 1,
                    STDATE = stDateLong,
                    STTIME = stTimeInt,
                    USERNAME = currentUsername,
                    newTask.COMP_COD,
                    USERCO = currentUserCod,
                    SEE = 0,
                    newTask.skid,
                    newTask.num,
                    tg = newTask.skid.HasValue ? newTask.skid : 0,
                    newTask.GR
                };

                long newIdnum = await _dbService.DoGetDataSQLAsyncSingle<long>(sql, parameters);

                if (newIdnum > 0)
                {
                    newTask.IDNUM = newIdnum; newTask.USERNAME = currentUsername; newTask.USERCO = currentUserCod;
                    _logger.LogInformation("API: Task {Idnum} created successfully by User {Username}.", newIdnum, currentUsername);
                    return Ok(newTask);
                }
                else
                {
                    _logger.LogError("API: CreateTask - Failed to insert task or retrieve new IDNUM.");
                    return StatusCode(500, "Failed to create task in database.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error creating task for Personel: {PersonelId}", newTask.PERSONEL);
                return StatusCode(500, "Internal server error while creating task.");
            }
        }

        [HttpPut("{idnum}")]
        public async Task<IActionResult> UpdateTask(long idnum, [FromBody] TaskModel updatedTask)
        {
            if (idnum <= 0 || idnum != updatedTask.IDNUM) return BadRequest("Task ID mismatch or invalid ID.");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserCod)) return Unauthorized("User ID not found in token.");

            long? enDateLong = CL_Tarikh.ConvertToPersianDateLong(updatedTask.ENDATE);
            int? enTimeInt = CL_Tarikh.ConvertTimeToInt(updatedTask.ENTIME);
            long? seetLong = CL_Tarikh.ConvertToPersianDateLong(updatedTask.SEET);

            try
            {
                string sql = @"UPDATE dbo.TASKS SET
                                   PERSONEL = @PERSONEL, TASK = @TASK, PERIORITY = @PERIORITY, STATUS = @STATUS,
                                   ENDATE = @ENDATE, ENTIME = @ENTIME, COMP_COD = @COMP_COD, skid = @skid,
                                   num = @num, SEE = @SEE, SEET = @SEET
                               WHERE IDNUM = @IDNUM";

                var parameters = new
                {
                    updatedTask.PERSONEL,
                    updatedTask.TASK,
                    updatedTask.PERIORITY,
                    updatedTask.STATUS,
                    ENDATE = enDateLong,
                    ENTIME = enTimeInt,
                    updatedTask.COMP_COD,
                    updatedTask.skid,
                    updatedTask.num,
                    updatedTask.SEE,
                    SEET = seetLong,
                    IDNUM = idnum
                };

                int rowsAffected = await _dbService.DoExecuteSQLAsync(sql, parameters);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("API: Task {Idnum} updated successfully.", idnum);
                    return NoContent();
                }
                else
                {
                    _logger.LogWarning("API: UpdateTask - Task {Idnum} not found or update failed (Rows Affected: 0).", idnum);
                    return NotFound($"Task with ID {idnum} not found.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error updating task {Idnum}.", idnum);
                return StatusCode(500, "Internal server error while updating task.");
            }
        }

        // Bulk update DTO
        public class BulkTaskUpdateRequest
        {
            [Required] public List<long>? TaskIds { get; set; }
            public int? PERSONEL { get; set; }
            public int? STATUS { get; set; }
            public int? PERIORITY { get; set; }
        }

        [HttpPut("bulk-update")]
        public async Task<IActionResult> UpdateTasksBulk([FromBody] BulkTaskUpdateRequest request)
        {
            if (request?.TaskIds == null || !request.TaskIds.Any() || (request.PERSONEL == null && request.STATUS == null && request.PERIORITY == null))
                return BadRequest("At least one Task ID and one field to update are required.");
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserCod)) return Unauthorized("User ID not found in token.");

            try
            {
                var setClauses = new List<string>();
                var parameters = new DynamicParameters();
                parameters.Add("Ids", request.TaskIds);

                if (request.PERSONEL.HasValue && request.PERSONEL > 0) { setClauses.Add("PERSONEL = @Personel"); parameters.Add("Personel", request.PERSONEL.Value); }
                if (request.STATUS.HasValue && request.STATUS > 0) { setClauses.Add("STATUS = @Status"); parameters.Add("Status", request.STATUS.Value); }
                if (request.PERIORITY.HasValue && request.PERIORITY > 0) { setClauses.Add("PERIORITY = @Periority"); parameters.Add("Periority", request.PERIORITY.Value); }

                if (!setClauses.Any()) return BadRequest("No valid update values provided.");

                string setClause = string.Join(", ", setClauses);
                string sql = $"UPDATE dbo.TASKS SET {setClause} WHERE IDNUM IN @Ids";

                int rowsAffected = await _dbService.DoExecuteSQLAsync(sql, parameters);
                _logger.LogInformation("API: Bulk update completed. Rows affected: {RowsAffected}", rowsAffected);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error during bulk task update for user {UserId}.", currentUserCod);
                return StatusCode(500, "Internal server error during bulk update.");
            }
        }

        [HttpGet("{idnum}")]
        public async Task<ActionResult<TaskModel>> GetTaskById(long idnum)
        {
            // Simplified example: Fetch raw and convert
            string sql = @"SELECT T.IDNUM, CH.NAME, T.GR, T.PERSONEL, T.TASK, T.PERIORITY, T.STATUS,
                                T.STDATE as STDATE_DB, T.STTIME as STTIME_DB, T.ENDATE as ENDATE_DB, T.ENTIME as ENTIME_DB,
                                T.USERNAME, T.COMP_COD, T.SUMTIME as SUMTIME_DB, T.skid, T.num, T.tg, T.CTIM, T.USERCO, T.SEE,
                                T.SEET as SEET_DB
                            FROM dbo.TASKS T
                            LEFT OUTER JOIN dbo.CUST_HESAB CH ON T.COMP_COD = CH.hes
                            WHERE T.IDNUM = @Idnum";
            var t = await _dbService.DoGetDataSQLAsyncSingle<dynamic>(sql, new { Idnum = idnum });

            if (t == null) return NotFound();

            var task = new TaskModel
            {
                IDNUM = t.IDNUM,
                NAME = t.NAME,
                GR = t.GR,
                PERSONEL = t.PERSONEL,
                TASK = t.TASK,
                PERIORITY = t.PERIORITY,
                STATUS = t.STATUS,
                USERNAME = t.USERNAME,
                COMP_COD = t.COMP_COD,
                skid = t.skid,
                num = t.num,
                tg = t.tg,
                CTIM = t.CTIM,
                USERCO = t.USERCO,
                SEE = t.SEE,
                STDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(t.STDATE_DB),
                STTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(t.STTIME_DB),
                ENDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(t.ENDATE_DB),
                ENTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(t.ENTIME_DB),
                SUMTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(t.SUMTIME_DB),
                SEET = CL_Tarikh.ConvertToDateTimeFromPersianLong(t.SEET_DB)
            };
            return Ok(task);
        }
    }
}