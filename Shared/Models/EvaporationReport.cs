using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models
{
    public class EvaporationReport
    {
        [Key]
        public int Id { get; set; }

        public DateTime? ReportDate { get; set; }

        [StringLength(50)]
        public string? Shift { get; set; }

        [StringLength(100)]
        public string? OperatorName { get; set; }

        public TimeSpan? StartTime { get; set; }

        public TimeSpan? EndTime { get; set; }

        public int? DurationInMinutes { get; set; }

        public decimal? OutletDryMatterPercentage { get; set; }

        public int? FillTime20LitreContainerInSeconds { get; set; }

        public decimal? BoilerSteamPressure { get; set; }

        public decimal? TvrSteamPressure { get; set; }

        public decimal? VacuumPressure { get; set; }

        public decimal? Tower1Temperature { get; set; }

        public decimal? Tower2Temperature { get; set; }

        public decimal? Tower3Temperature { get; set; }

        public decimal? CondenserInletTemperature { get; set; }

        public decimal? CondenserOutletTemperature { get; set; }

        public bool? IsTower1PumpOn { get; set; } = false;

        public bool? IsTower2PumpOn { get; set; } = false;

        public decimal? DistilledWaterTemperature { get; set; }

        [StringLength(50)]
        public string? CipStatus { get; set; }

        public int? HoursSinceLastCip { get; set; }
    }
}
