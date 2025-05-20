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
using System.IO; // For MemoryStream, FileStream
using Microsoft.AspNetCore.Http; // For IFormFile
using Microsoft.AspNetCore.StaticFiles;

namespace Safir.Server.Controllers
{
    [Route("api/Tasks/{taskId}/[controller]")]
    [ApiController]
    [Authorize]
    public class EventsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<EventsController> _logger;

        private readonly string? _attachmentsBasePath;
        private static readonly string[] AllowedFileExtensions = { ".jpg", ".jpeg", ".png", ".pdf" }; // Allowed extensions

        public EventsController(IDatabaseService dbService, ILogger<EventsController> logger, IConfiguration configuration) // Inject IConfiguration
        {
            _dbService = dbService;
            _logger = logger;
            // Get base path from appsettings.json for attachments
            _attachmentsBasePath = configuration["EventAttachmentsPath"]; // NEW: Define a new key in appsettings.json
            if (string.IsNullOrEmpty(_attachmentsBasePath))
            {
                _logger.LogWarning("EventAttachmentsPath is not configured. File attachments will not be saved to disk.");
            }
        }

        public class CreateEventRequestDto
        {
            public long IDNUM { get; set; } // Task ID
            [Required(ErrorMessage = "شرح رویداد الزامی است.")]
            [MaxLength(4000)]
            public string? EVENTS { get; set; } // شرح رویداد
            public DateTime? STDATE { get; set; }
            public TimeSpan? STTIME { get; set; }
            public TimeSpan? SUMTIME { get; set; }
            public int? skid { get; set; }
            public long? num { get; set; }
            // No direct file property here, will be handled via IFormFile parameter
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EventModel>>> GetEventsForTask(long taskId)
        {
            if (taskId <= 0) return BadRequest("Task ID نامعتبر است.");
            try
            {
                // Fetch raw date/time and also pic (image bytes) and FXTYPE
                string sql = @"SELECT
                                   IDNUM, IDD, EVENTS,
                                   STDATE as STDATE_DB, STTIME as STTIME_DB,
                                   USERNAME, COMPANY,
                                   SUMTIME as SUMTIME_DB,
                                   skid, num, tg,
                                   pic, FXTYPE -- NEW: Fetch pic and FXTYPE
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
                    SUMTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(ev.SUMTIME_DB),
                    // NEW: Map attached file info
                    // The 'pic' column is of type 'image' in SQL, which maps to byte[]. [cite: 31]
                    // If files are large, we might not want to send full bytes over API unless specifically requested.
                    // For now, let's just indicate if a file exists without sending bytes, or send a small indicator.
                    // We'll provide a separate endpoint for file download.
                    AttachedFileType = ev.FXTYPE, // Store file type (e.g., ".pdf", ".jpg")
                    AttachedFileName = !string.IsNullOrEmpty(ev.FXTYPE) ? $"ضمیمه_رویداد_{ev.IDD}{ev.FXTYPE}" : null // Generate a generic name for display
                    // AttachedFileBytes = ev.pic // Do NOT send large bytes unless explicitly requested (e.g., small thumbnails)
                }).ToList();

                _logger.LogInformation("API: Fetched {Count} events for Task ID: {TaskId}", events.Count, taskId);
                return Ok(events);
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error fetching events for Task ID: {TaskId}", taskId); return StatusCode(500, "Internal server error while fetching events."); }
        }

        [HttpPost]
        public async Task<ActionResult<EventModel>> CreateEvent(
            long taskId,
            [FromForm] CreateEventRequestDto request, // Use [FromForm] for multipart/form-data
            IFormFile? file) // Accept IFormFile for the uploaded file
        {
            if (request == null || taskId <= 0) return BadRequest("Task ID یا اطلاعات رویداد نامعتبر است.");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (request.IDNUM != 0 && request.IDNUM != taskId) return BadRequest("Task ID در آدرس و بدنه درخواست مطابقت ندارد.");

            var currentUsername = User.Identity?.Name;
            if (string.IsNullOrEmpty(currentUsername)) return Unauthorized("اطلاعات کاربر نامعتبر است.");

            long? stDateLong = CL_Tarikh.ConvertToPersianDateLong(request.STDATE ?? DateTime.Now);
            int? stTimeInt = CL_Tarikh.ConvertTimeToInt(request.STTIME) ?? CL_Tarikh.ConvertTimeToInt(DateTime.Now);
            int? sumTimeInt = CL_Tarikh.ConvertTimeToInt(request.SUMTIME);

            byte[]? fileBytes = null;
            string? fileExtension = null;

            if (file != null)
            {
                // Validate file size
                const long maxFileSize = 10 * 1024 * 1024; // 10 MB
                if (file.Length > maxFileSize)
                {
                    return BadRequest($"اندازه فایل ضمیمه نباید بیشتر از {maxFileSize / (1024 * 1024)} مگابایت باشد.");
                }

                // Validate file extension
                fileExtension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
                if (!AllowedFileExtensions.Contains(fileExtension))
                {
                    return BadRequest($"پسوند فایل '{fileExtension}' مجاز نیست. پسوندهای مجاز: {string.Join(", ", AllowedFileExtensions)}");
                }

                // Read file bytes into memory if storing in DB (pic column)
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);
                    fileBytes = memoryStream.ToArray();
                }

                _logger.LogInformation("API: Received file '{FileName}' ({ContentType}) with size {Size} bytes for Task {TaskId}.", file.FileName, file.ContentType, file.Length, taskId);

                // Option 1: Save to disk and store path/name in DB (Recommended for large files)
                // If you use this, the 'pic' column in EVENTS should be nvarchar(MAX) for path, not 'image'.
                // If your 'pic' column is 'image' (which it is [cite: 31]), then you MUST store bytes in DB.
                /*
                if (!string.IsNullOrEmpty(_attachmentsBasePath))
                {
                    // Generate a unique file name
                    string uniqueFileName = $"{Guid.NewGuid().ToString()}_{file.FileName}";
                    string filePath = Path.Combine(_attachmentsBasePath, uniqueFileName);

                    try
                    {
                        await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);
                        // Store uniqueFileName in DB (instead of fileBytes in 'pic')
                        // And maybe original file name in FXTYPE or a new column
                        // This requires changes to EventModel and DB schema (pic column type)
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error saving attachment to disk: {FilePath}", filePath);
                        return StatusCode(500, "خطا در ذخیره فایل ضمیمه در سرور.");
                    }
                }
                else
                {
                    _logger.LogWarning("EventAttachmentsPath is not configured. File attachment will be stored directly in DB (pic column).");
                }
                */
            }

            try
            {
                string sql = @"INSERT INTO dbo.EVENTS
                               (IDNUM, EVENTS, STDATE, STTIME, USERNAME, COMPANY, SUMTIME, pic, FXTYPE, skid, num, tg)
                             OUTPUT INSERTED.IDD
                             VALUES
                               (@IDNUM, @EVENTS, @STDATE, @STTIME, @USERNAME, @COMPANY, @SUMTIME, @pic, @FXTYPE, @skid, @num, @tg)";

                var parameters = new
                {
                    IDNUM = taskId,
                    EVENTS = request.EVENTS?.Trim(),
                    STDATE = stDateLong,
                    STTIME = stTimeInt,
                    USERNAME = currentUsername,
                    COMPANY = (string?)null, // No company field in EventModel
                    SUMTIME = sumTimeInt,
                    pic = fileBytes, // NEW: Pass file bytes [cite: 31]
                    FXTYPE = fileExtension, // NEW: Pass file extension [cite: 31]
                    request.skid,
                    request.num,
                    tg = (int?)null // tg in EventModel is null, matches DB example if it's not used [cite: 31]
                };

                int? newIdd = await _dbService.DoGetDataSQLAsyncSingle<int?>(sql, parameters);
                if (!newIdd.HasValue || newIdd.Value <= 0) return StatusCode(500, "Failed to create event in database.");

                // Create the EventModel to return to client
                var createdEvent = new EventModel
                {
                    IDNUM = taskId,
                    IDD = newIdd.Value,
                    EVENTS = request.EVENTS,
                    USERNAME = currentUsername,
                    STDATE = request.STDATE,
                    STTIME = request.STTIME,
                    SUMTIME = request.SUMTIME,
                    skid = request.skid,
                    num = request.num,
                    AttachedFileType = fileExtension, // Return the file type to client
                    AttachedFileName = file != null ? file.FileName : null // Return original file name for display
                    // Do NOT return AttachedFileBytes here, fetch separately for download
                };

                _logger.LogInformation("API: Event {EventId} created successfully for Task {TaskId} with attachment: {HasAttachment}.", newIdd.Value, taskId, file != null);
                return Ok(createdEvent); // Return created event with some file info
            }
            catch (Exception ex) { _logger.LogError(ex, "API: Error creating event for Task ID: {TaskId}", taskId); return StatusCode(500, "Internal server error while creating event."); }
        }

        [HttpPut("{eventId}")]
        public async Task<IActionResult> UpdateEvent(long taskId, int eventId, [FromBody] EventModel updatedEvent)
        {
            // Note: This PUT currently updates only EVENTS, SUMTIME, skid, num.
            // If you want to update/change attachments, you need to modify this to accept file uploads again
            // and handle deletion/replacement of old files or bytes in DB.
            if (taskId <= 0 || eventId <= 0 || updatedEvent == null || updatedEvent.IDD != eventId || updatedEvent.IDNUM != taskId) return BadRequest("Task ID, Event ID یا اطلاعات رویداد نامعتبر است.");
            if (string.IsNullOrWhiteSpace(updatedEvent.EVENTS)) return BadRequest("شرح رویداد الزامی است.");
            var currentUsername = User.Identity?.Name;
            if (string.IsNullOrEmpty(currentUsername)) return Unauthorized("اطلاعات کاربر نامعتبر است.");

            int? sumTimeInt = CL_Tarikh.ConvertTimeToInt(updatedEvent.SUMTIME);

            try
            {
                // Current SQL doesn't update pic or FXTYPE. If attachment update is needed, modify here.
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
            // Note: If files are stored on disk, you must delete the file from disk BEFORE deleting the DB record.
            // This requires retrieving FXTYPE/file path from DB first.
            if (taskId <= 0 || eventId <= 0) return BadRequest("Task ID یا Event ID نامعتبر است.");
            var currentUsername = User.Identity?.Name;
            if (string.IsNullOrEmpty(currentUsername)) return Unauthorized("اطلاعات کاربر نامعتبر است.");
            try
            {
                // If storing files on disk, retrieve FXTYPE to construct file path and delete it.
                string? fileExtension = await _dbService.DoGetDataSQLAsyncSingle<string>("SELECT FXTYPE FROM dbo.EVENTS WHERE IDNUM = @TaskId AND IDD = @EventId", new { TaskId = taskId, EventId = eventId });
                if (!string.IsNullOrEmpty(fileExtension) && !string.IsNullOrEmpty(_attachmentsBasePath))
                {
                    string filePath = Path.Combine(_attachmentsBasePath, $"{taskId}_{eventId}{fileExtension}"); // Example naming convention
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                        _logger.LogInformation("Deleted attached file: {FilePath}", filePath);
                    }
                }

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
            string sql = @"SELECT IDNUM, IDD, EVENTS, STDATE as STDATE_DB, STTIME as STTIME_DB, USERNAME, COMPANY, SUMTIME as SUMTIME_DB, skid, num, tg,
                            pic, FXTYPE -- NEW: Fetch pic and FXTYPE
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
                SUMTIME = CL_Tarikh.ConvertToTimeSpanFromTimeInt(ev.SUMTIME_DB),
                // Only populate attachment details for display, not actual bytes
                AttachedFileType = ev.FXTYPE,
                AttachedFileName = !string.IsNullOrEmpty(ev.FXTYPE) ? $"ضمیمه_رویداد_{ev.IDD}{ev.FXTYPE}" : null
            };
            return Ok(eventModel);
        }

        // NEW: Endpoint to download the attachment
        [HttpGet("{eventId}/file")]
        public async Task<IActionResult> DownloadEventAttachment(long taskId, int eventId)
        {
            _logger.LogInformation("Attempting to download attachment for Task {TaskId}, Event {EventId}", taskId, eventId);
            try
            {
                string sql = "SELECT pic, FXTYPE FROM dbo.EVENTS WHERE IDNUM = @TaskId AND IDD = @EventId";
                var result = await _dbService.DoGetDataSQLAsyncSingle<dynamic>(sql, new { TaskId = taskId, EventId = eventId });

                if (result == null || result.pic == null)
                {
                    _logger.LogWarning("Attachment not found for Task {TaskId}, Event {EventId}.", taskId, eventId);
                    return NotFound("فایل ضمیمه یافت نشد.");
                }

                byte[] fileBytes = result.pic; // Direct bytes from 'image' column [cite: 31]
                string? fileExtension = result.FXTYPE;

                string fileName = $"attachment_{eventId}{fileExtension ?? ""}";

                var provider = new FileExtensionContentTypeProvider();
                string contentType;
                if (!string.IsNullOrEmpty(fileExtension) && provider.TryGetContentType(fileName, out contentType))
                {
                    // Found content type
                }
                else
                {
                    // Default to octet-stream or specific image type if no extension
                    if (fileExtension == ".pdf") contentType = "application/pdf";
                    else if (fileExtension == ".jpg" || fileExtension == ".jpeg") contentType = "image/jpeg";
                    else if (fileExtension == ".png") contentType = "image/png";
                    else contentType = "application/octet-stream"; // Fallback
                }

                _logger.LogInformation("Serving attachment for Task {TaskId}, Event {EventId}. File size: {FileSize} bytes, ContentType: {ContentType}", taskId, eventId, fileBytes.Length, contentType);
                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading attachment for Task {TaskId}, Event {EventId}", taskId, eventId);
                return StatusCode(500, "خطا در دانلود فایل ضمیمه.");
            }
        }
    }
}