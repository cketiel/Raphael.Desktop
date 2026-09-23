using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // Mirror of Raphael.Shared/DTOs/CallRequests/CallRequestDtos.cs, copied by hand like every other
    // DTO here. The driver-side DTOs are left out: this application never calls those endpoints.
    //
    // Status and Change are text on the wire (Waiting, InProgress, Resolved, Cancelled, Expired) so
    // this copy cannot drift on enum numbers. CallRequestStatuses below names them.

    /// <summary>A call request as every screen shows it. Carries no patient data.</summary>
    public class CallRequestSummaryDto
    {
        public int Id { get; set; }

        public string Status { get; set; } = string.Empty;

        public int DriverId { get; set; }

        public string DriverName { get; set; } = string.Empty;

        public int? VehicleRouteId { get; set; }

        public string? RouteName { get; set; }

        public DateTime OperatingDate { get; set; }

        public DateTime RequestedAtUtc { get; set; }

        public DateTime QueuedAtUtc { get; set; }

        public int ReminderCount { get; set; }

        public DateTime? LastReminderAtUtc { get; set; }

        public int CallAttempts { get; set; }

        public DateTime? LastAttemptAtUtc { get; set; }

        public DateTime? DriverAvailableAtUtc { get; set; }

        public int? ClaimedByUserId { get; set; }

        public string? ClaimedByName { get; set; }

        public DateTime? ClaimedAtUtc { get; set; }

        public int? ResolvedByUserId { get; set; }

        public string? ResolvedByName { get; set; }

        public DateTime? ClosedAtUtc { get; set; }

        public string? ReasonCode { get; set; }

        public int RequestNumberOfDay { get; set; }

        /// <summary>Grows with every change. Anything older than what is on screen is ignored.</summary>
        public long Revision { get; set; }
    }

    public class CallRequestDetailDto
    {
        public CallRequestSummaryDto Request { get; set; } = new();

        public string? DriverPhone { get; set; }

        public string? VehicleName { get; set; }

        /// <summary>⚠️ May contain PHI. Shown in the detail only: never logged, exported or copied into a list.</summary>
        public string? ResolutionNote { get; set; }

        public CallRequestRouteContextDto? Route { get; set; }

        public CallRequestPositionDto? RequestPosition { get; set; }

        public CallRequestPositionDto? LastPosition { get; set; }

        public List<CallRequestTimelineItemDto> Timeline { get; set; } = new();

        public List<CallRequestSummaryDto> OtherRequestsOfDay { get; set; } = new();
    }

    public class CallRequestRouteContextDto
    {
        public bool PulledOut { get; set; }

        public TimeSpan? PulledOutAt { get; set; }

        public bool PulledIn { get; set; }

        public int StopsDone { get; set; }

        public int StopsTotal { get; set; }

        public CallRequestStopDto? LastPerformed { get; set; }

        public CallRequestStopDto? Next { get; set; }

        public CallRequestStopDto? AtRequest { get; set; }
    }

    public class CallRequestStopDto
    {
        public int ScheduleId { get; set; }

        public int? TripId { get; set; }

        /// <summary>Pickup, Dropoff, PullOut or PullIn.</summary>
        public string Kind { get; set; } = string.Empty;

        public TimeSpan? ScheduledTime { get; set; }

        public TimeSpan? Eta { get; set; }

        public int? MinutesLate { get; set; }

        public bool Performed { get; set; }

        public TimeSpan? PerformedAt { get; set; }
    }

    public class CallRequestPositionDto
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public DateTime? AtUtc { get; set; }

        public double? Speed { get; set; }

        public string? Address { get; set; }
    }

    public class CallRequestTimelineItemDto
    {
        public string Type { get; set; } = string.Empty;

        public DateTime AtUtc { get; set; }

        public string? ByName { get; set; }

        public string? Detail { get; set; }
    }

    public class ResolveCallRequestDto
    {
        public string ReasonCode { get; set; } = string.Empty;

        public string? Note { get; set; }
    }

    /// <summary>Body of a 409: the request changed under the dispatcher, and this is how it stands now.</summary>
    public class CallRequestConflictDto
    {
        public string Message { get; set; } = string.Empty;

        public CallRequestSummaryDto? Current { get; set; }
    }

    public static class CallRequestStatuses
    {
        public const string Waiting = "Waiting";
        public const string InProgress = "InProgress";
        public const string Resolved = "Resolved";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
    }

    /// <summary>The <c>Change</c> of a live message, and the <c>Type</c> of a timeline line.</summary>
    public static class CallRequestChanges
    {
        public const string Requested = "Requested";
        public const string Reminded = "Reminded";
        public const string Claimed = "Claimed";
        public const string TakenOver = "TakenOver";
        public const string Released = "Released";
        public const string CallNotAnswered = "CallNotAnswered";
        public const string DriverAvailable = "DriverAvailable";
        public const string Resolved = "Resolved";
        public const string Cancelled = "Cancelled";
        public const string Reopened = "Reopened";
        public const string Expired = "Expired";
    }

    /// <summary>Server-validated reasons for closing a request. Must match CallRequestReasonCodes.</summary>
    public static class CallRequestReasonCodes
    {
        public const string Address = "ADDRESS";
        public const string PatientLocation = "PATIENT_LOCATION";
        public const string Vehicle = "VEHICLE";
        public const string RouteSchedule = "ROUTE_SCHEDULE";
        public const string NotNeeded = "NOT_NEEDED";
        public const string Other = "OTHER";

        public const int NoteMaxLength = 250;

        public static readonly IReadOnlyList<string> All = new[]
        {
            Address, PatientLocation, Vehicle, RouteSchedule, NotNeeded, Other
        };
    }
}
