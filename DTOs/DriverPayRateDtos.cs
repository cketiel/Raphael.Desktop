using System;

namespace Raphael.Desktop.DTOs
{
    /// <summary>Copy of Raphael.Shared.Entities.DriverPayRateType (RE-082). Travels as a number.</summary>
    public enum DriverPayRateType
    {
        Hourly = 0,
        PerMile = 1
    }

    /// <summary>Copy of Raphael.Shared.DTOs.DriverPayRateDto (RE-082): one entry of a driver's pay history.</summary>
    public class DriverPayRateDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DriverPayRateType RateType { get; set; }
        public decimal HourlyRate { get; set; }
        public decimal PerMileRate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedByName { get; set; }

        /// <summary>The amount in force, as the Users list shows it: "$15.00/h" or "$1.00/mi".</summary>
        public string Label => RateType == DriverPayRateType.Hourly ? $"${HourlyRate:N2}/h" : $"${PerMileRate:N2}/mi";
    }

    /// <summary>Copy of Raphael.Shared.DTOs.DriverPayRateCreateDto (RE-082).</summary>
    public class DriverPayRateCreateDto
    {
        public DriverPayRateType RateType { get; set; }
        public decimal HourlyRate { get; set; }
        public decimal PerMileRate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public string? Note { get; set; }
    }
}
