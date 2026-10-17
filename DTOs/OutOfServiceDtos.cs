using System;
using System.Collections.Generic;

// RE-053. Hand copy of Raphael.Shared/DTOs/OutOfServiceDtos.cs: keep the two in step.
namespace Raphael.Desktop.DTOs
{
    /// <summary>One subject to take out of service: a vehicle or a driver, with why (RE-053).</summary>
    public class OutOfServiceSubjectDto
    {
        /// <summary><c>VEHICLE</c> or <c>DRIVER</c>.</summary>
        public string Subject { get; set; } = string.Empty;

        public int SubjectId { get; set; }

        public string ReasonCode { get; set; } = string.Empty;

        public string? ReasonNote { get; set; }
    }

    /// <summary>Another vehicle and/or driver taking over a route, so its trips stay on it.</summary>
    public class OutOfServiceSubstitutionDto
    {
        public int VehicleRouteId { get; set; }

        public int? NewVehicleId { get; set; }

        public int? NewDriverId { get; set; }
    }

    /// <summary>
    /// What to take out of service. Taking out a route from Runs is the same request with the
    /// route's vehicle, its driver or both, and <see cref="OriginVehicleRouteId"/> set.
    /// </summary>
    public class OutOfServiceRequestDto
    {
        public List<OutOfServiceSubjectDto> Subjects { get; set; } = new();

        public List<OutOfServiceSubstitutionDto> Substitutions { get; set; } = new();

        public int? OriginVehicleRouteId { get; set; }

        /// <summary>
        /// The preview's <see cref="OutOfServicePreviewDto.Fingerprint"/>. Required to execute: if the
        /// affected trips changed since, nothing is done and a new preview is asked for.
        /// </summary>
        public string? Fingerprint { get; set; }
    }

    /// <summary>One event (Pickup or Dropoff) still to be done.</summary>
    public class OutOfServiceEventDto
    {
        public int ScheduleId { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Address { get; set; }

        public TimeSpan? ScheduledTime { get; set; }

        public TimeSpan? Eta { get; set; }

        public bool Performed { get; set; }
    }

    /// <summary>One trip affected, and what will happen or happened to it.</summary>
    public class OutOfServiceTripDto
    {
        public int TripId { get; set; }

        public DateTime Date { get; set; }

        public string? PatientName { get; set; }

        /// <summary><c>UNROUTED</c>, <c>EXCLUDED_ONBOARD</c> or <c>KEPT_BY_SUBSTITUTION</c>.</summary>
        public string Outcome { get; set; } = string.Empty;

        public List<OutOfServiceEventDto> Events { get; set; } = new();
    }

    /// <summary>A route of the vehicle or the driver, with the trips it still has.</summary>
    public class OutOfServiceRouteDto
    {
        public int VehicleRouteId { get; set; }

        public string RouteName { get; set; } = string.Empty;

        public int VehicleId { get; set; }

        public string? VehicleName { get; set; }

        public int DriverId { get; set; }

        public string? DriverName { get; set; }

        public int? NewVehicleId { get; set; }

        public string? NewVehicleName { get; set; }

        public int? NewDriverId { get; set; }

        public string? NewDriverName { get; set; }

        public List<OutOfServiceTripDto> Trips { get; set; } = new();
    }

    /// <summary>What the operation will do (preview) or did (result). Nothing is saved by a preview.</summary>
    public class OutOfServicePreviewDto
    {
        public string Fingerprint { get; set; } = string.Empty;

        public Guid? OperationId { get; set; }

        public List<OutOfServiceRouteDto> Routes { get; set; } = new();

        public int TripsUnrouted { get; set; }

        public int EventsUnrouted { get; set; }

        public int TripsOnboard { get; set; }

        public int TripsKept { get; set; }

        /// <summary>The days that got trips back in the unscheduled list, for the links to it.</summary>
        public List<DateTime> UnroutedDates { get; set; } = new();
    }

    /// <summary>A vehicle or driver out of service right now.</summary>
    public class OutOfServiceRecordDto
    {
        public int Id { get; set; }

        public Guid OperationId { get; set; }

        public string Subject { get; set; } = string.Empty;

        public int SubjectId { get; set; }

        public string? SubjectName { get; set; }

        public string ReasonCode { get; set; } = string.Empty;

        public string? ReasonNote { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public string? StartedByName { get; set; }

        public DateTime? EndedAtUtc { get; set; }

        public string? EndedByName { get; set; }
    }

    /// <summary>The reasons the Desktop suggests, by subject.</summary>
    public class OutOfServiceReasonCatalogDto
    {
        public List<string> Vehicle { get; set; } = new();

        public List<string> Driver { get; set; } = new();

        public int NoteMaxLength { get; set; }
    }
}
