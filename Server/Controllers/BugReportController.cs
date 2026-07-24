using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.BugReport;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Safir.Shared.Constants;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BugReportController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<BugReportController> _logger;

        public BugReportController(IDatabaseService dbService, ILogger<BugReportController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpPost("submit")]
        [AllowAnonymous] // Allow public submission
        public async Task<IActionResult> SubmitBugReport([FromBody] BugReportDto bugReport)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // Capture logged in user if available and not already provided
                if (string.IsNullOrEmpty(bugReport.CreatedBy) && User.Identity?.IsAuthenticated == true)
                {
                    bugReport.CreatedBy = User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;
                }

                // If DB context info is requested but sensitive, we handle it on the server if needed
                // Currently, we let the client send what is safe, but we don't expose passwords.

                string sql = @"
                    INSERT INTO [dbo].[BugReports] (
                        [CreatedBy], [CustomerName], [ContactInfo], [AppVersion], [FrontendVersion], 
                        [BackendVersion], [PageUrl], [Route], [ModuleName], [MenuName], 
                        [DatabaseName], [ServerName], [EnvironmentName], [Severity], [Category], 
                        [HappensAlways], [IsBlocking], [TestedOnAnotherDevice], [HasRecentChanges], 
                        [ReproduceSteps], [ExpectedResult], [ActualResult], [UserDescription], 
                        [BrowserInfo], [OperatingSystem], [ScreenSize], [UserAgent], [ApiEndpoint], 
                        [HttpStatusCode], [ErrorMessage], [StackTrace], [TraceId], [Status]
                    ) VALUES (
                        @CreatedBy, @CustomerName, @ContactInfo, @AppVersion, @FrontendVersion, 
                        @BackendVersion, @PageUrl, @Route, @ModuleName, @MenuName, 
                        @DatabaseName, @ServerName, @EnvironmentName, @Severity, @Category, 
                        @HappensAlways, @IsBlocking, @TestedOnAnotherDevice, @HasRecentChanges, 
                        @ReproduceSteps, @ExpectedResult, @ActualResult, @UserDescription, 
                        @BrowserInfo, @OperatingSystem, @ScreenSize, @UserAgent, @ApiEndpoint, 
                        @HttpStatusCode, @ErrorMessage, @StackTrace, @TraceId, @Status
                    );";

                int result = await _dbService.DoExecuteSQLAsync(sql, bugReport);

                if (result > 0)
                {
                    _logger.LogInformation("New bug report submitted successfully.");
                    return Ok(new { Message = "گزارش خطای شما با موفقیت ثبت شد." });
                }
                else
                {
                    _logger.LogError("Failed to save bug report.");
                    return StatusCode(500, "خطایی در ثبت گزارش رخ داد. لطفاً بعداً تلاش کنید.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting bug report.");
                return StatusCode(500, "خطای داخلی سرور. لطفاً با پشتیبانی تماس بگیرید.");
            }
        }

        [HttpGet]
        [Authorize] // Example: restrict list access to authorized users
        public async Task<IActionResult> GetBugReports()
        {
            if (User.FindFirst(BaseknowClaimTypes.GRSAL)?.Value == "999")
            {
                return StatusCode(StatusCodes.Status403Forbidden, "دسترسی به لیست کل گزارش‌ها برای این نقش مجاز نیست.");
            }

            try
            {
                string sql = "SELECT * FROM [dbo].[BugReports] ORDER BY CreatedAt DESC";
                var reports = await _dbService.DoGetDataSQLAsync<BugReportDto>(sql);
                return Ok(reports);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching bug reports.");
                return StatusCode(500, "خطای داخلی سرور هنگام دریافت گزارشات.");
            }
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetBugReportDetails(int id)
        {
            try
            {
                string sql = "SELECT * FROM [dbo].[BugReports] WHERE Id = @Id";
                var report = await _dbService.DoGetDataSQLAsyncSingle<BugReportDto>(sql, new { Id = id });

                if (report == null)
                    return NotFound("گزارش یافت نشد.");

                // Enforce Row-Level Security for Bug Reporters
                if (User.FindFirst(BaseknowClaimTypes.GRSAL)?.Value == "999")
                {
                    string currentUser = User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
                    if (!string.Equals(report.CreatedBy, currentUser, StringComparison.OrdinalIgnoreCase))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, "شما فقط مجاز به مشاهده جزئیات گزارش‌های ثبت شده توسط خودتان هستید.");
                    }
                }

                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching bug report details for ID {Id}.", id);
                return StatusCode(500, "خطای داخلی سرور هنگام دریافت جزئیات گزارش.");
            }
        }

        [HttpPatch("{id}/status")]
        [Authorize]
        public async Task<IActionResult> UpdateBugReportStatus(int id, [FromBody] UpdateStatusRequest request)
        {
            if (User.FindFirst(BaseknowClaimTypes.GRSAL)?.Value == "999")
            {
                return StatusCode(StatusCodes.Status403Forbidden, "شما مجاز به تغییر وضعیت گزارش‌ها نیستید.");
            }

            try
            {
                string sql = @"UPDATE [dbo].[BugReports] SET [Status] = @Status, [AdminNote] = @AdminNote WHERE Id = @Id";
                int result = await _dbService.DoExecuteSQLAsync(sql, new { Status = request.Status, AdminNote = request.AdminNote, Id = id });

                if (result > 0)
                    return Ok(new { Message = "وضعیت گزارش با موفقیت به‌روز شد." });

                return NotFound("گزارش یافت نشد.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating bug report status for ID {Id}.", id);
                return StatusCode(500, "خطای داخلی سرور هنگام به‌روز‌رسانی وضعیت گزارش.");
            }
        }

        [HttpGet("{id}/comments")]
        [Authorize]
        public async Task<IActionResult> GetBugReportComments(int id)
        {
            try
            {
                // Check authorization for Bug Reporters
                if (User.FindFirst(BaseknowClaimTypes.GRSAL)?.Value == "999")
                {
                    string getSql = "SELECT Id, CreatedBy FROM [dbo].[BugReports] WHERE Id = @Id";
                    var report = await _dbService.DoGetDataSQLAsyncSingle<BugReportDto>(getSql, new { Id = id });

                    if (report == null) return NotFound("گزارش یافت نشد.");

                    string currentUser = User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
                    if (!string.Equals(report.CreatedBy, currentUser, StringComparison.OrdinalIgnoreCase))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, "شما مجاز به مشاهده پی‌نوشت‌های این گزارش نیستید.");
                    }
                }

                string sql = "SELECT * FROM [dbo].[BugReportComments] WHERE BugReportId = @Id ORDER BY CreatedAt ASC";
                var comments = await _dbService.DoGetDataSQLAsync<BugReportCommentDto>(sql, new { Id = id });
                return Ok(comments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching comments for bug report ID {Id}.", id);
                return StatusCode(500, "خطای داخلی سرور هنگام دریافت پی‌نوشت‌ها.");
            }
        }

        [HttpPost("{id}/comments")]
        [Authorize]
        public async Task<IActionResult> AddBugReportComment(int id, [FromBody] AddCommentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CommentText))
                return BadRequest("متن پی‌نوشت نمی‌تواند خالی باشد.");

            try
            {
                // CreatedBy is nullable for reports submitted anonymously. Fetch the row as
                // an object so a null creator is not mistaken for a missing report.
                string getSql = "SELECT Id, CreatedBy FROM [dbo].[BugReports] WHERE Id = @Id";
                var report = await _dbService.DoGetDataSQLAsyncSingle<BugReportDto>(getSql, new { Id = id });

                if (report == null)
                    return NotFound("گزارش یافت نشد.");

                bool isBugReporter = User.FindFirst(BaseknowClaimTypes.GRSAL)?.Value == "999";
                string currentUser = User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

                if (isBugReporter && !string.Equals(report.CreatedBy, currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, "شما فقط مجاز به ثبت پی‌نوشت روی گزارش‌های خودتان هستید.");
                }

                string sql = @"
                    INSERT INTO [dbo].[BugReportComments] (BugReportId, UserId, UserName, IsAdmin, CommentText)
                    VALUES (@BugReportId, @UserId, @UserName, @IsAdmin, @CommentText);
                ";

                var commentParams = new
                {
                    BugReportId = id,
                    UserId = currentUser,
                    UserName = currentUser,
                    IsAdmin = !isBugReporter,
                    CommentText = request.CommentText
                };

                int result = await _dbService.DoExecuteSQLAsync(sql, commentParams);

                if (result > 0)
                    return Ok(new { Message = "پی‌نوشت با موفقیت ثبت شد." });

                return StatusCode(500, "خطا در ثبت پی‌نوشت.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding comment to bug report ID {Id}.", id);
                return StatusCode(500, "خطای داخلی سرور هنگام ثبت پی‌نوشت.");
            }
        }

        [HttpGet("my-reports")]
        [Authorize]
        public async Task<IActionResult> GetMyBugReports()
        {
            try
            {
                string currentUser = User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("نام کاربری یافت نشد.");
                }

                string sql = "SELECT * FROM [dbo].[BugReports] WHERE CreatedBy = @CreatedBy ORDER BY CreatedAt DESC";
                var reports = await _dbService.DoGetDataSQLAsync<BugReportDto>(sql, new { CreatedBy = currentUser });
                return Ok(reports);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user's bug reports.");
                return StatusCode(500, "خطای داخلی سرور هنگام دریافت گزارشات شما.");
            }
        }
        public class UpdateStatusRequest
        {
            public string Status { get; set; } = "New";
            public string? AdminNote { get; set; }
        }

        public class AddCommentRequest
        {
            public string CommentText { get; set; } = string.Empty;
        }
    }
}
