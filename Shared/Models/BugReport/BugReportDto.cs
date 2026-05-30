using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.BugReport
{
    public class BugReportDto
    {
        public int Id { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [MaxLength(100)]
        public string? CreatedBy { get; set; } // UserId if available

        [MaxLength(200)]
        public string? CustomerName { get; set; }

        [MaxLength(100)]
        public string? ContactInfo { get; set; }

        [MaxLength(50)]
        public string? AppVersion { get; set; }

        [MaxLength(50)]
        public string? FrontendVersion { get; set; }

        [MaxLength(50)]
        public string? BackendVersion { get; set; }

        [MaxLength(500)]
        public string? PageUrl { get; set; }

        [MaxLength(200)]
        public string? Route { get; set; }

        [MaxLength(100)]
        public string? ModuleName { get; set; }

        [MaxLength(100)]
        public string? MenuName { get; set; }

        [MaxLength(100)]
        public string? DatabaseName { get; set; }

        [MaxLength(100)]
        public string? ServerName { get; set; } // Logical server if safe

        [MaxLength(50)]
        public string? EnvironmentName { get; set; }

        [Required(ErrorMessage = "شدت مشکل الزامی است.")]
        [MaxLength(50)]
        public string Severity { get; set; } // Low / Medium / High / Critical

        [MaxLength(100)]
        public string? Category { get; set; }

        public bool HappensAlways { get; set; } // Whether the problem always happens

        public bool IsBlocking { get; set; }
        public bool TestedOnAnotherDevice { get; set; }
        public bool HasRecentChanges { get; set; }

        [Required(ErrorMessage = "مراحل تکرار خطا الزامی است.")]
        public string ReproduceSteps { get; set; }

        [Required(ErrorMessage = "نتیجه مورد انتظار الزامی است.")]
        public string ExpectedResult { get; set; }

        [Required(ErrorMessage = "نتیجه واقعی الزامی است.")]
        public string ActualResult { get; set; }

        [Required(ErrorMessage = "شرح مشکل الزامی است.")]
        public string UserDescription { get; set; }

        [MaxLength(200)]
        public string? BrowserInfo { get; set; }

        [MaxLength(100)]
        public string? OperatingSystem { get; set; }

        [MaxLength(50)]
        public string? ScreenSize { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [MaxLength(500)]
        public string? ApiEndpoint { get; set; }

        public int? HttpStatusCode { get; set; }

        [MaxLength(4000)]
        public string? ErrorMessage { get; set; }

        public string? StackTrace { get; set; }

        [MaxLength(100)]
        public string? TraceId { get; set; } // TraceId/CorrelationId

        [MaxLength(50)]
        public string Status { get; set; } = "New"; // New / InReview / Fixed / Rejected / NeedMoreInfo

        public string? AdminNote { get; set; }
        public string? UserNote { get; set; }
    }
}
