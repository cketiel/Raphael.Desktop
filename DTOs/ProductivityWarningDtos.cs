#nullable enable
using System;
using System.Collections.Generic;

// Copies of Raphael.Shared/DTOs/ProductivityWarningDtos.cs (RE-092). See _meta/CONTRACT_MAP.md.
namespace Raphael.Desktop.DTOs
{
    /// <summary>The warnings of one route-day of the fleet productivity report (RE-092). No PHI.</summary>
    public class ProductivityDayWarningsDto
    {
        public DateTime OperatingDate { get; set; }
        public int VehicleRouteId { get; set; }
        public string? RouteName { get; set; }
        public int? DriverId { get; set; }
        public string? DriverName { get; set; }
        public int? VehicleId { get; set; }
        public string? VehicleName { get; set; }

        /// <summary>True for a shift with no Pull-in: it has no row in the report.</summary>
        public bool IsOpenShift { get; set; }

        public List<ProductivityWarningDto> Warnings { get; set; } = new();

        /// <summary>How the paid shift came out. Filled only in the detail of a closed day.</summary>
        public ShiftCalculationDto? Shift { get; set; }

        /// <summary>How the miles came out, leg by leg. Filled only in the detail of a closed day.</summary>
        public MileCalculationDto? Miles { get; set; }
    }

    /// <summary>
    /// The miles of a shift, leg by leg. <b>Miles with a patient on board = miles × patients</b>: one mile
    /// with two patients is two. The figures kept that day are the authority; the legs explain them.
    /// </summary>
    public class MileCalculationDto
    {
        public List<MileLegDto> Legs { get; set; } = new();
        public long? OdometerOut { get; set; }
        public long? OdometerIn { get; set; }
        public double? VehicleMiles { get; set; }

        /// <summary>Vehicle miles with anyone on board, each counted once.</summary>
        public double? OccupiedMiles { get; set; }

        /// <summary>Odometer less occupied.</summary>
        public double? EmptyMiles { get; set; }

        /// <summary>Miles × patients: what a driver paid per mile earns.</summary>
        public double? PatientMiles { get; set; }
        public double? GpsMiles { get; set; }
        public double? PlannedMiles { get; set; }

        /// <summary>The legs no longer add up to what was kept: the track has expired or a stop was changed since.</summary>
        public bool Incomplete { get; set; }
    }

    public class MileLegDto
    {
        /// <summary><c>Pull-out</c>, <c>Pickup</c>, <c>Dropoff</c>, <c>Pull-in</c>.</summary>
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string? FromTrip { get; set; }
        public string? ToTrip { get; set; }
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public double Miles { get; set; }
        public int Patients { get; set; }

        /// <summary>Miles × patients.</summary>
        public double PatientMiles { get; set; }

        /// <summary>Measured by the planned distance: the leg had no GPS.</summary>
        public bool Planned { get; set; }
    }

    /// <summary>
    /// The paid shift, step by step (RE-070 rules). Every figure is the one that was used: nothing here
    /// is worked out again with other data.
    /// </summary>
    public class ShiftCalculationDto
    {
        /// <summary>The first real stop's promised hour (pickup, or appointment without one).</summary>
        public DateTime? FirstCommitmentUtc { get; set; }

        /// <summary>Garage to that stop, as it was priced.</summary>
        public int? DriveOutMinutes { get; set; }
        public int PreparationMinutes { get; set; }
        public int InspectionMinutes { get; set; }

        /// <summary>Null: it could not be worked out, so the start has no limit.</summary>
        public DateTime? AllowedStartUtc { get; set; }
        public DateTime? RealStartUtc { get; set; }
        public DateTime? PaidStartUtc { get; set; }

        /// <summary>The last real stop's Perform, and the drive back priced from it.</summary>
        public DateTime? LastPerformUtc { get; set; }
        public int? DriveBackMinutes { get; set; }

        /// <summary>Null: no drive back was priced, so the end has no limit.</summary>
        public DateTime? AllowedEndUtc { get; set; }
        public DateTime? RealEndUtc { get; set; }
        public DateTime? PaidEndUtc { get; set; }

        public int RealMinutes { get; set; }
        public int PaidMinutes { get; set; }
        public int StartUnpaidMinutes { get; set; }
        public int EndUnpaidMinutes { get; set; }
    }

    /// <summary>The figure of the calculation an administrator may change. It decides pay.</summary>
    public class PayRuleDto
    {
        public int InspectionMinutes { get; set; }
        public int DefaultInspectionMinutes { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public int PreparationMinutes { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class ProductivityWarningDto
    {
        /// <summary>See <c>WarningCodes</c>. A code a client does not know is shown as a generic warning.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary><c>error</c>, <c>review</c> or <c>info</c>.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>How many cases the detail lists.</summary>
        public int Count { get; set; } = 1;

        /// <summary>The figure that tripped it, in the limit's unit.</summary>
        public double? Value { get; set; }

        public double? Limit { get; set; }

        /// <summary>The Pull-out or Pull-in whose odometer an administrator may correct.</summary>
        public int? CorrectScheduleId { get; set; }

        /// <summary>Filled only in the detail of a day.</summary>
        public List<ProductivityWarningCaseDto> Cases { get; set; } = new();
    }

    /// <summary>One case behind a warning. Only the fields its code uses are filled.</summary>
    public class ProductivityWarningCaseDto
    {
        /// <summary>Wall-clock hour of the stop, as the dispatcher sees it.</summary>
        public TimeSpan? Time { get; set; }

        /// <summary><c>Arrive</c>, <c>Perform</c>, <c>Start</c>, <c>End</c>, <c>Pull-out</c>, <c>Pull-in</c>.</summary>
        public string? Action { get; set; }

        public string? StopType { get; set; }
        public int? ScheduleId { get; set; }
        public int? TripId { get; set; }

        /// <summary>The trip number the office knows.</summary>
        public string? TripNumber { get; set; }
        public double? Value { get; set; }
        public DateTime? RealUtc { get; set; }
        public DateTime? AllowedUtc { get; set; }
        public int? Minutes { get; set; }
        public double? StopLatitude { get; set; }
        public double? StopLongitude { get; set; }

        /// <summary>Where the GPS had the vehicle at that moment. Null when it had no fix near it.</summary>
        public double? MarkLatitude { get; set; }
        public double? MarkLongitude { get; set; }

        public long? OdometerOut { get; set; }
        public long? OdometerIn { get; set; }
        public long? PreviousReading { get; set; }
        public double? VehicleMiles { get; set; }
        public double? GpsMiles { get; set; }
        public double? PlannedMiles { get; set; }
        public string? RouteName { get; set; }
        public int? TripsCompleted { get; set; }
    }

    /// <summary>A warning's limit, as an administrator sees and edits it.</summary>
    public class WarningLimitDto
    {
        public string Code { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public bool Enabled { get; set; }

        /// <summary>Null when the warning has no figure to adjust.</summary>
        public double? Limit { get; set; }
        public double? DefaultLimit { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }

        /// <summary><c>mi</c>, <c>min</c>, <c>percent</c>, <c>times</c>, <c>hours</c>.</summary>
        public string? Unit { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class WarningLimitUpdateDto
    {
        public string Code { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public double? Limit { get; set; }
    }

    public class WarningPreviewDto
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>Route-days that would carry the warning.</summary>
        public int Days { get; set; }

        /// <summary>Cases in all: nine marks far from the stop on one day are nine.</summary>
        public int Cases { get; set; }

        /// <summary>Route-days looked at.</summary>
        public int OfDays { get; set; }
    }

    /// <summary>One reading of a vehicle's odometer, as it stands after any correction.</summary>
    public class OdometerHistoryRowDto
    {
        public int ScheduleId { get; set; }
        public DateTime OperatingDate { get; set; }
        public string Event { get; set; } = string.Empty;
        public int VehicleRouteId { get; set; }
        public string? RouteName { get; set; }
        public string? DriverName { get; set; }
        public TimeSpan? Time { get; set; }
        public long Reading { get; set; }

        /// <summary>Against the last reading that made sense; null for the first one.</summary>
        public long? MilesSincePrevious { get; set; }

        /// <summary><c>ok</c>, or the code of the warning it raises.</summary>
        public string Status { get; set; } = string.Empty;

        public List<OdometerCorrectionDto> Corrections { get; set; } = new();
    }

    public class OdometerCorrectionDto
    {
        public long? PreviousReading { get; set; }
        public long Reading { get; set; }
        public string? Reason { get; set; }
        public string? CorrectedBy { get; set; }
        public DateTime AtUtc { get; set; }
    }

    public class VehicleOdometerHistoryDto
    {
        public int VehicleId { get; set; }
        public string? VehicleName { get; set; }
        public long? LastOdometer { get; set; }
        public DateTime? LastOdometerAtUtc { get; set; }
        public List<OdometerHistoryRowDto> Readings { get; set; } = new();
    }

    public class OdometerCorrectionRequestDto
    {
        public long Reading { get; set; }

        /// <summary>The number on the screen when it was corrected: refused (409) if it has changed since.</summary>
        public long? Expected { get; set; }

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>What a driver's reading is compared with before saving. A warning, never a limit.</summary>
    public class OdometerReferenceDto
    {
        public int ScheduleId { get; set; }
        public string Event { get; set; } = string.Empty;

        /// <summary>The vehicle's last reading that makes sense, other than this one.</summary>
        public long? LastReading { get; set; }
        public DateTime? LastReadingDate { get; set; }

        /// <summary>At a Pull-in: the same route-day's Pull-out.</summary>
        public long? PullOutReading { get; set; }
        public double? GpsMiles { get; set; }
        public double? PlannedMiles { get; set; }

        /// <summary>Null when that warning is off.</summary>
        public double? PullInTimesLimit { get; set; }
        public double? PullOutMilesLimit { get; set; }
        public bool WarnWhenLower { get; set; }
    }
}
