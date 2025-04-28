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

namespace Safir.Server.Controllers
{
    [Route("api/Tasks/{taskId}/[controller]")]
    [ApiController]
    [Authorize]
    public class EventsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<EventsController> _logger;

        public EventsController(IDatabaseService dbService, ILogger<EventsController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EventModel>>> GetEventsForTask(long taskId)
        {
            if (taskId <= 0) return BadRequest("Task ID نامعتبر است.");
            try
            {
                // Fetch raw date/time
                string sql = @"SELECT
                                   IDNUM, IDD, EVENTS,
                                   STDATE as STDATE_DB, STTIME as STTIME_DB,
                                   USERNAME, COMPANY,
                                   SUMTIME as SUMTIME_DB,
                                   skid, num, tg
                               FROM dbo.EVENTS
                               WHERE IDNUM = @TaskId ORDER BY IDD";

                var eventsRaw = await _dbService.DoGetDataSQLAsync<dynamic>(sql, new { TaskId = taskId });
                if (eventsRaw == null) return Ok(Enumerable.Empty<EventModel>());

                // Convert in C#
                List<EventModel> events = eventsRaw.Select(ev => new EventModel
                {
                    IDNUM = ev.IDNUM,
                    IDD = ev.IDD,
                    EVENTS = ev.EVENTS,
                    USERNAME = ev.USERNAME,
                    skid = ev.skid,
                    num = ev.num,
                    STDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(ev.STDATE_DB),
                    STTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(ev.STTIME_DB),
                    SUMTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(ev.SUMTIME_DB)
                }).ToList();

                return Ok(events);
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error fetching events for Task ID: {TaskId}", taskId); return StatusCode(500, "Internal server error while fetching events."); }
        }

        [HttpPost]
        public async Task<ActionResult<EventModel>> CreateEvent(long taskId, [FromBody] EventModel newEvent)
        {
            if (newEvent == null || taskId <= 0) return BadRequest("Task ID یا اطلاعات رویداد نامعتبر است.");
            if (string.IsNullOrWhiteSpace(newEvent.EVENTS)) return BadRequest("شرح رویداد الزامی است.");
            if (newEvent.IDNUM != 0 && newEvent.IDNUM != taskId) return BadRequest("Task ID در آدرس و بدنه درخواست مطابقت ندارد.");
            var currentUsername = User.Identity?.Name;
            if (string.IsNullOrEmpty(currentUsername)) return Unauthorized("اطلاعات کاربر نامعتبر است.");

            long? stDateLong = CL_Tarikh.ConvertToPersianDateLong(newEvent.STDATE ?? DateTime.Now);
            int? stTimeInt = CL_Tarikh.ConvertTimeToInt(newEvent.STTIME) ?? CL_Tarikh.ConvertTimeToInt(DateTime.Now);
            int? sumTimeInt = CL_Tarikh.ConvertTimeToInt(newEvent.SUMTIME);

            try
            {
                string sql = @"INSERT INTO dbo.EVENTS
                               (IDNUM, EVENTS, STDATE, STTIME, USERNAME, COMPANY, SUMTIME, pic, FXTYPE, skid, num, tg)
                             OUTPUT INSERTED.IDD
                             VALUES
                               (@IDNUM, @EVENTS, @STDATE, @STTIME, @USERNAME, @COMPANY, @SUMTIME, NULL, NULL, @skid, @num, @tg)";

                var parameters = new
                {
                    IDNUM = taskId,
                    EVENTS = newEvent.EVENTS?.Trim(),
                    STDATE = stDateLong,
                    STTIME = stTimeInt,
                    USERNAME = currentUsername,
                    COMPANY = (string?)null,
                    SUMTIME = sumTimeInt,
                    newEvent.skid,
                    newEvent.num,
                    tg = (int?)null
                };

                int? newIdd = await _dbService.DoGetDataSQLAsyncSingle<int?>(sql, parameters);
                if (!newIdd.HasValue || newIdd.Value <= 0) return StatusCode(500, "Failed to create event in database.");

                newEvent.IDNUM = taskId; newEvent.IDD = newIdd.Value; newEvent.USERNAME = currentUsername;
                _logger.LogInformation("API: Event {EventId} created successfully for Task {TaskId}", newIdd.Value, taskId);
                return Ok(newEvent); // Return created event
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error creating event for Task ID: {TaskId}", taskId); return StatusCode(500, "Internal server error while creating event."); }
        }

        [HttpPut("{eventId}")]
        public async Task<IActionResult> UpdateEvent(long taskId, int eventId, [FromBody] EventModel updatedEvent)
        {
            if (taskId <= 0 || eventId <= 0 || updatedEvent == null || updatedEvent.IDD != eventId || updatedEvent.IDNUM != taskId) return BadRequest("Task ID, Event ID یا اطلاعات رویداد نامعتبر است.");
            if (string.IsNullOrWhiteSpace(updatedEvent.EVENTS)) return BadRequest("شرح رویداد الزامی است.");
            var currentUsername = User.Identity?.Name;
            if (string.IsNullOrEmpty(currentUsername)) return Unauthorized("اطلاعات کاربر نامعتبر است.");

            int? sumTimeInt = CL_Tarikh.ConvertTimeToInt(updatedEvent.SUMTIME);

            try
            {
                string sql = @"UPDATE dbo.EVENTS SET EVENTS = @EVENTS, SUMTIME = @SUMTIME, skid = @skid, num = @num
                                WHERE IDNUM = @IDNUM AND IDD = @IDD";
                var parameters = new { updatedEvent.EVENTS, SUMTIME = sumTimeInt, updatedEvent.skid, updatedEvent.num, IDNUM = taskId, IDD = eventId };
                int rowsAffected = await _dbService.DoExecuteSQLAsync(sql, parameters);
                if (rowsAffected > 0) return NoContent();
                else return NotFound($"Event with ID {eventId} for Task {taskId} not found.");
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error updating Event {EventId} for Task {TaskId}.", eventId, taskId); return StatusCode(500, "Internal server error while updating event."); }
        }

        [HttpDelete("{eventId}")]
        public async Task<IActionResult> DeleteEvent(long taskId, int eventId)
        {
            if (taskId <= 0 || eventId <= 0) return BadRequest("Task ID یا Event ID نامعتبر است.");
            var currentUsername = User.Identity?.Name;
            if (string.IsNullOrEmpty(currentUsername)) return Unauthorized("اطلاعات کاربر نامعتبر است.");
            try
            {
                string sql = "DELETE FROM dbo.EVENTS WHERE IDNUM = @TaskId AND IDD = @EventId";
                int rowsAffected = await _dbService.DoExecuteSQLAsync(sql, new { TaskId = taskId, EventId = eventId });
                if (rowsAffected > 0) return NoContent();
                else return NotFound($"Event with ID {eventId} for Task {taskId} not found.");
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error deleting Event {EventId} for Task {TaskId}.", eventId, taskId); return StatusCode(500, "Internal server error while deleting event."); }
        }

        [HttpGet("{eventId}")]
        public async Task<ActionResult<EventModel>> GetEventById(long taskId, int eventId)
        {
            // Simplified example
            string sql = @"SELECT IDNUM, IDD, EVENTS, STDATE as STDATE_DB, STTIME as STTIME_DB, USERNAME, COMPANY, SUMTIME as SUMTIME_DB, skid, num, tg
                            FROM dbo.EVENTS WHERE IDNUM = @TaskId AND IDD = @EventId";
            var ev = await _dbService.DoGetDataSQLAsyncSingle<dynamic>(sql, new { TaskId = taskId, EventId = eventId });
            if (ev == null) return NotFound();

            var eventModel = new EventModel
            {
                IDNUM = ev.IDNUM,
                IDD = ev.IDD,
                EVENTS = ev.EVENTS,
                USERNAME = ev.USERNAME,
                skid = ev.skid,
                num = ev.num,
                STDATE = CL_Tarikh.ConvertToDateTimeFromPersianLong(ev.STDATE_DB),
                STTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(ev.STTIME_DB),
                SUMTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(ev.SUMTIME_DB)
            };
            return Ok(eventModel);
        }
    }
}