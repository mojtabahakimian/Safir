using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.BugReport
{
    public class BugReportCommentDto
    {
        public int Id { get; set; }
        public int BugReportId { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public bool IsAdmin { get; set; }

        [Required(ErrorMessage = "متن پی‌نوشت الزامی است.")]
        public string CommentText { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
