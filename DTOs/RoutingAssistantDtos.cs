#nullable enable
using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // RE-098: copy of Raphael.Shared/DTOs/Routing/RoutingAssistantDtos.cs.

    /// <summary>RE-098: the lines that could do a trip, best first.</summary>
    public class LineSuggestionsRequestDto
    {
        public int TripId { get; set; }
    }

    /// <summary>RE-099: the open trips a line could do, best first.</summary>
    public class TripSuggestionsRequestDto
    {
        public int VehicleRouteId { get; set; }
        public DateTime Date { get; set; }
    }

    /// <summary>
    /// RE-099: the whole day planned at once, so each open trip has its place and the ones that break
    /// the routes are known before anybody tries them.
    /// </summary>
    public class DayPlanDto
    {
        public DateTime Date { get; set; }
        public DateTime MadeAtUtc { get; set; }
        public List<PlannedTripDto> Trips { get; set; } = new();
    }

    /// <summary>One open trip in the plan. Reasons: NO_LINE, LATE, LONG, COST.</summary>
    public class PlannedTripDto
    {
        public int TripId { get; set; }
        public int? VehicleRouteId { get; set; }
        public string? Line { get; set; }
        /// <summary>Better left for the end: expensive or late wherever it goes.</summary>
        public bool IsOverflow { get; set; }
        public List<string> Reasons { get; set; } = new();
        public bool IsLong { get; set; }
        public double Miles { get; set; }
        /// <summary>What it adds to the day: miles, shift minutes, and patients late for the broker.</summary>
        public double AddedMiles { get; set; }
        public int AddedMinutes { get; set; }
        public int LatePatients { get; set; }
        /// <summary>How many trips like the average of the day cost the same.</summary>
        public double EqualsTrips { get; set; }
        /// <summary>Chances that it is cancelled, from the patient's last sixty days.</summary>
        public int CancelPercent { get; set; }
        /// <summary>Until when it can wait.</summary>
        public TimeSpan? DecideBy { get; set; }
        public bool IsUrgent { get; set; }
    }

    /// <summary>RE-099: what the daily limits of Google roads are and how they are being used, for the administrator.</summary>
    public class MapsBudgetReportDto
    {
        public string TrafficMode { get; set; } = "";
        public int SuggestionsMaxLegsPerDay { get; set; }
        public int SuggestionsConfirmTop { get; set; }
        public int AppointmentChecksPerDay { get; set; }
        /// <summary>Roads bought in the last 30 days by everything, the suggestions and checks included.</summary>
        public long RoadsBoughtLast30Days { get; set; }
        /// <summary>Trips not cancelled per working day, over the last 30 days.</summary>
        public int TripsPerDay { get; set; }
        /// <summary>Days with trips in the last 30.</summary>
        public int WorkingDays { get; set; }
        public List<MapsBudgetDayDto> Days { get; set; } = new();
    }

    /// <summary>Purpose: 1 routing suggestions, 2 appointment checks.</summary>
    public class MapsBudgetDayDto
    {
        public DateTime Day { get; set; }
        public int Purpose { get; set; }
        public int Used { get; set; }
        public int Requests { get; set; }
        public int Refused { get; set; }
    }

    /// <summary>One suggestion worked out with Google's times, at the place that was suggested.</summary>
    public class SuggestionDetailRequestDto
    {
        public int TripId { get; set; }
        public int VehicleRouteId { get; set; }
        /// <summary>The stop each event goes after; null with no route yet, and then the garage.</summary>
        public int? PickupAfterScheduleId { get; set; }
        public int? DropoffAfterScheduleId { get; set; }
    }

    public class RoutingSuggestionsDto
    {
        public List<RoutingSuggestionDto> Suggestions { get; set; } = new();
        /// <summary>Google roads left today for suggestions; 0 means the list stays estimated.</summary>
        public int ConfirmationsLeft { get; set; }
        public int MaxExtraRideMinutes { get; set; }
    }

    /// <summary>
    /// Where a trip would go in a line and what follows. Group: 1 fits, 2 inside the broker's fifteen minutes, 3 with a problem,
    /// 4 the vehicle cannot.
    /// </summary>
    public class RoutingSuggestionDto
    {
        public int Position { get; set; }
        public int Group { get; set; }
        public int TripId { get; set; }
        public string? TripNumber { get; set; }
        public string? Patient { get; set; }
        public string? SpaceType { get; set; }
        public string? TripType { get; set; }
        public string? PickupAddress { get; set; }
        public string? DropoffAddress { get; set; }
        public TimeSpan FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }
        public int VehicleRouteId { get; set; }
        public string Line { get; set; } = "";
        public string? Driver { get; set; }
        public string? Vehicle { get; set; }
        /// <summary>AMB, WCH, STR or null when the vehicle has no configuration.</summary>
        public string? VehicleKind { get; set; }
        public string? GroupColor { get; set; }

        public TimeSpan PickupEta { get; set; }
        /// <summary>Negative: minutes before the promised hour.</summary>
        public int PickupOffsetMinutes { get; set; }
        public int WaitMinutes { get; set; }
        public TimeSpan PickupDeparture { get; set; }
        public TimeSpan DropoffEta { get; set; }
        /// <summary>Minutes left before the appointment; negative when it is missed; null with no appointment.</summary>
        public int? AppointmentMarginMinutes { get; set; }
        public double MilesToPickup { get; set; }
        public int MinutesToPickup { get; set; }
        public double TripMiles { get; set; }
        public double AddedMiles { get; set; }
        public double AddedEmptyMiles { get; set; }
        public int AddedPaidMinutes { get; set; }
        public double LineMilesBefore { get; set; }
        public double LineMilesAfter { get; set; }
        public int OnBoard { get; set; }
        public bool Shared { get; set; }
        public int ExtraRideMinutes { get; set; }
        public List<string> RidesWith { get; set; } = new();
        public bool OpensLine { get; set; }
        /// <summary>Every new road is Google's at its hour.</summary>
        public bool Confirmed { get; set; }
        public List<SuggestionProblemDto> Problems { get; set; } = new();
        /// <summary>Why it is above the next one: GROUP, PLAN, HOUR, LATE, COST, EARLIER; null for the last.</summary>
        public string? AboveBecause { get; set; }
        public double? AboveByMiles { get; set; }
        public int? AboveByMinutes { get; set; }
        /// <summary>The line the day's plan gives this trip.</summary>
        public bool IsPlanned { get; set; }
        /// <summary>Equivalent miles: what the whole day costs more with it here.</summary>
        public double Cost { get; set; }
        /// <summary>Patients, this one included, that would arrive 1 to 15 minutes past their appointment, and how many minutes between all.</summary>
        public int MarginPatients { get; set; }
        public int MarginMinutes { get; set; }
        /// <summary>How many other lines fit this trip (trip list only).</summary>
        public int? OtherLinesThatFit { get; set; }

        public string? PreviousStop { get; set; }
        public string? NextStop { get; set; }
        public int? PickupAfterScheduleId { get; set; }
        public int? DropoffAfterScheduleId { get; set; }
        public int TargetSequence { get; set; }
        public int DropoffTargetSequence { get; set; }
        /// <summary>The last hour to be at the pickup and make the appointment on time.</summary>
        public TimeSpan? LatestPickup { get; set; }
        /// <summary>The line's day as it would be.</summary>
        public List<SuggestionStopDto> Stops { get; set; } = new();
    }

    /// <summary>Kind: LATE_PICKUP, MISSES_APPOINTMENT, DELAYS_PICKUP, DELAYS_APPOINTMENT.</summary>
    public class SuggestionProblemDto
    {
        public string Kind { get; set; } = "";
        public int Minutes { get; set; }
        public string? Stop { get; set; }
    }

    public class SuggestionStopDto
    {
        public int? ScheduleId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>PULL_OUT, PICKUP, DROPOFF or PULL_IN.</summary>
        public string Kind { get; set; } = "";
        public bool IsNew { get; set; }
        public bool Done { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public TimeSpan Eta { get; set; }
        /// <summary>The hour it has without this trip; null for the new stops.</summary>
        public TimeSpan? EtaBefore { get; set; }
        public TimeSpan? Promised { get; set; }
        public TimeSpan? Appointment { get; set; }
        public double Miles { get; set; }
        public int TravelMinutes { get; set; }
        public int WaitMinutes { get; set; }
        public int OnBoardAfter { get; set; }
        public bool Priced { get; set; }
        /// <summary>The road that reaches it, when it is one this trip changes and Google has drawn it.</summary>
        public string? EncodedPolyline { get; set; }
    }
}
