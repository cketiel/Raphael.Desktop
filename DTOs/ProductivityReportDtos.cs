using System;

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// RE-081: one driver's day in the fleet productivity report, read from <c>RouteDayMetrics</c>
    /// (RE-062, algorithm v2). No PHI: driver, vehicle, line and figures only.
    /// </summary>
    /// <remarks>
    /// Minutes: <see cref="PaidMinutes"/> is the paid shift and the six buckets add up to it.
    /// Miles: <see cref="VehicleMiles"/> is the odometer and the authority; loaded + deadhead add up
    /// to it. A null mile figure means there was nothing to measure it with, never zero.
    /// </remarks>
    public class ProductivityReportRowDto
    {
        public DateTime OperatingDate { get; set; }

        public int VehicleRouteId { get; set; }
        public string? RouteName { get; set; }

        public int? DriverId { get; set; }
        public string? DriverName { get; set; }

        public int? VehicleId { get; set; }
        public string? VehicleName { get; set; }

        /// <summary>Instants (UTC); each app shows them in the viewer's clock.</summary>
        public DateTime? ShiftStartUtc { get; set; }
        public DateTime? ShiftEndUtc { get; set; }
        public DateTime? PaidStartUtc { get; set; }
        public DateTime? PaidEndUtc { get; set; }

        /// <summary>The real shift, first record to last.</summary>
        public int ShiftMinutes { get; set; }

        /// <summary>The paid shift: what the buckets add up to.</summary>
        public int PaidMinutes { get; set; }
        public int StartTrimmedMinutes { get; set; }
        public int EndTrimmedMinutes { get; set; }

        public int LoadedMinutes { get; set; }
        public int InspectionMinutes { get; set; }
        public int ScheduleWaitMinutes { get; set; }
        public int PickupWaitMinutes { get; set; }
        public int DeadheadDriveMinutes { get; set; }
        public int OtherMinutes { get; set; }

        /// <summary>Patient-hours, outside the sum: two patients for an hour count two.</summary>
        public int PatientMinutes { get; set; }

        public long? OdometerOut { get; set; }
        public long? OdometerIn { get; set; }
        public double? VehicleMiles { get; set; }
        public double? LoadedMiles { get; set; }
        public double? DeadheadMiles { get; set; }
        public double? GpsMiles { get; set; }
        public double? PlannedMiles { get; set; }
        public double? PassengerMiles { get; set; }
        public long? OffRouteMiles { get; set; }

        public int TripsCompleted { get; set; }

        /// <summary>Arrivals or performs pressed too far from the stop: the day's reliability.</summary>
        public int SuspectMarks { get; set; }

        public int AlgorithmVersion { get; set; }
        public DateTime ComputedAtUtc { get; set; }
    }
}
