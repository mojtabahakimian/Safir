using System;

namespace Safir.Shared.Models.Production
{
    public class ProductionReportDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime? ReportDate { get; set; } = DateTime.Today;
        public string WheyCompany { get; set; }
        public TimeSpan? ConcentrationStart { get; set; }
        public TimeSpan? ConcentrationEnd { get; set; }
        public decimal? ConcentrationWheyQty { get; set; }
        public TimeSpan? SprayStart { get; set; }
        public TimeSpan? SprayEnd { get; set; }
        public decimal? SprayPowderQty { get; set; }
        public string CounterNumber { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}