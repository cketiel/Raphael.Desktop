using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // RE-062. Manual copies of Raphael.Shared/DTOs/Inspections/VehicleInspectionDtos.cs, the source of
    // truth: only the types the office reads.

    /// <summary>One inspection at Pull-out or Pull-in, as the office sees it.</summary>
    public class VehicleInspectionDetailDto
    {
        public int Id { get; set; }
        public Guid ClientId { get; set; }
        public int ScheduleId { get; set; }

        /// <summary><c>PullOut</c> or <c>PullIn</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        public int VehicleRouteId { get; set; }
        public string RouteName { get; set; }
        public DateTime OperatingDate { get; set; }
        public int ProviderId { get; set; }
        public int DriverId { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public int VehicleId { get; set; }
        public string VehicleName { get; set; } = string.Empty;
        public long Odometer { get; set; }
        public string VinOnFile { get; set; }
        public bool VinConfirmed { get; set; }
        public string VinReported { get; set; }

        /// <summary>⚠️ Written by the driver: may contain PHI. Shown, never logged.</summary>
        public string Comment { get; set; } = string.Empty;

        public bool HasSignature { get; set; }
        public int ChecklistVersion { get; set; }
        public int ProblemCount { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime CompletedAtUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public List<VehicleInspectionItemDetailDto> Items { get; set; } = new();
    }

    public class VehicleInspectionItemDetailDto
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>The English text the driver read. The office reads <c>inspection.item.{Code}</c> in its language.</summary>
        public string Label { get; set; } = string.Empty;

        public bool IsProblem { get; set; }
        public bool HasPhoto { get; set; }
    }
}
