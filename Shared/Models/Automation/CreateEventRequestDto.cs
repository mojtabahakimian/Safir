// Safir/Shared/Models/Automation/CreateEventRequestDto.cs
using System.ComponentModel.DataAnnotations;
using System; // For DateTime, TimeSpan

namespace Safir.Shared.Models.Automation
{
    public class CreateEventRequestDto
    {
        public long IDNUM { get; set; } // Task ID (This will be filled from the URL path)
        [Required(ErrorMessage = "شرح رویداد الزامی است.")]
        [MaxLength(4000)]
        public string? EVENTS { get; set; } // Event description
        public DateTime? STDATE { get; set; }
        public TimeSpan? STTIME { get; set; }
        public TimeSpan? SUMTIME { get; set; }
        public int? skid { get; set; }
        public long? num { get; set; }
    }
}