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

                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching bug report details for ID {Id}.", id);
                return StatusCode(500, "خطای داخلی سرور هنگام دریافت جزئیات گزارش.");
            }
        }
    }
}
