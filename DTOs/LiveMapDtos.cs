using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// Manual copy of Raphael.Shared/DTOs/LiveMapDtos.cs (RE-080). Keep in step with the backend.
    /// RE-080: the group a line falls in on the live map. The group is decided here, once, so every
    /// screen draws the same line in the same color.
    /// </summary>
    /// <remarks>
    /// When a line meets several conditions the most urgent wins: out of service, then suspended,
    /// then expired. All three mean "does not run today"; out of service is the one somebody has to
    /// act on.
    /// </remarks>
    public static class LiveMapGroups
    {
        /// <summary>Active, with at least one trip today. The only group shown by default.</summary>
        public const string OnRoute = "ON_ROUTE";

        /// <summary>Active, no trips today.</summary>
        public const string Idle = "IDLE";

        /// <summary>A <c>RouteSuspension</c> covers today.</summary>
        public const string Suspended = "SUSPENDED";

        /// <summary>Its vehicle or its driver has an open Out of Service record.</summary>
        public const string OutOfService = "OUT_OF_SERVICE";

        /// <summary><c>ToDate</c> is before today, or <c>FromDate</c> after it.</summary>
        public const string Expired = "EXPIRED";
    }

    /// <summary>RE-080: everything the live map needs to open, in one call.</summary>
    public class LiveMapSnapshotDto
    {
        /// <summary>The operating day the groups were decided for.</summary>
        public DateTime OperatingDate { get; set; }

        public List<LiveMapRouteDto> Routes { get; set; } = new();

        /// <summary>The day in figures, the same ones Home prints under its list.</summary>
        public LiveMapDaySummaryDto Day { get; set; } = new();
    }

    /// <summary>
    /// RE-080: the day in figures, counted like Home does over the same trips (the caller's Provider):
    /// cancelled trips never add to the money.
    /// </summary>
    public class LiveMapDaySummaryDto
    {
        public int Trips { get; set; }
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Canceled { get; set; }

        /// <summary>Trips with no line, as Home's "without a run".</summary>
        public int Unrouted { get; set; }

        /// <summary>Sum of the charges of the trips not cancelled.</summary>
        public double Charges { get; set; }

        public double Paid { get; set; }
    }

    /// <summary>
    /// RE-080: a line's working day so far. Live figures (from the GPS track and the trips) while the
    /// shift is open; <see cref="Metrics"/> joins them once the Pull-in has produced the route-day
    /// metrics of RE-062.
    /// </summary>
    public class LiveMapShiftDto
    {
        /// <summary>First fix of the operating day (UTC). Null if the vehicle has not reported today.</summary>
        public DateTime? StartUtc { get; set; }

        /// <summary>Last fix of the operating day (UTC).</summary>
        public DateTime? LastUtc { get; set; }

        /// <summary>Length of today's GPS track, in miles. Measured at home, no Google.</summary>
        public double GpsMiles { get; set; }

        /// <summary>Trips finished today on this line.</summary>
        public int TripsDone { get; set; }

        /// <summary>The closed day's breakdown (RE-062), or null while the shift is open.</summary>
        public LiveMapRouteMetricsDto? Metrics { get; set; }
    }

    /// <summary>The figures of a closed route-day, from <c>RouteDayMetrics</c> (RE-062).</summary>
    public class LiveMapRouteMetricsDto
    {
        public int ShiftMinutes { get; set; }
        public int LoadedMinutes { get; set; }

        /// <summary>Waiting at stops, before and after the window opened.</summary>
        public int WaitMinutes { get; set; }

        public double? VehicleMiles { get; set; }
        public double? LoadedMiles { get; set; }
        public double? DeadheadMiles { get; set; }
    }

    /// <summary>One line on the live map: who drives it, its group and where it was last seen.</summary>
    public class LiveMapRouteDto
    {
        public int VehicleRouteId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? DriverName { get; set; }

        public string? VehicleName { get; set; }

        /// <summary>One of <see cref="LiveMapGroups"/>.</summary>
        public string Group { get; set; } = LiveMapGroups.Idle;

        /// <summary>Trips on this line today, cancelled ones left out.</summary>
        public int TripsToday { get; set; }

        /// <summary>The working day so far: hours, miles, trips done.</summary>
        public LiveMapShiftDto Shift { get; set; } = new();

        /// <summary>The last fix, or null when the line has not reported in the last days.</summary>
        public LiveMapPositionDto? Position { get; set; }
    }

    /// <summary>The last fix of a line. Same fields as <c>VehiclePositionMessage</c>.</summary>
    public class LiveMapPositionDto
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        /// <summary>Metres per second, as the driver's app reports it.</summary>
        public double Speed { get; set; }

        public string? Direction { get; set; }

        /// <summary>When the fix was taken (UTC).</summary>
        public DateTime AtUtc { get; set; }
    }
}

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// RE-080: what the route card shows when a vehicle is clicked on the live map.
    /// </summary>
    /// <remarks>
    /// ⚠️ About the line, never about a patient: the next stop says its kind and its times, not who or
    /// where. The trip under way carries only its ids, so the card can open it in Home.
    /// </remarks>
    public class LiveMapRouteDetailDto
    {
        public int VehicleRouteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Group { get; set; } = LiveMapGroups.Idle;
        public string? Garage { get; set; }
        public DateTime OperatingDate { get; set; }

        public int? DriverId { get; set; }
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }

        public int VehicleId { get; set; }
        public string? VehicleName { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public int? Year { get; set; }
        public string? Plate { get; set; }
        public string? Color { get; set; }
        public string? VehicleType { get; set; }

        /// <summary>
        /// The most demanding space the vehicle carries: <c>STR</c> (stretcher), <c>WCH</c>
        /// (wheelchair) or <c>AMB</c> (ambulatory only). Picks the card's illustration.
        /// </summary>
        public string Kind { get; set; } = "AMB";

        public List<LiveMapCapacityDto> Capacities { get; set; } = new();

        public int TripsToday { get; set; }
        public int TripsDone { get; set; }
        public int TripsCanceled { get; set; }

        /// <summary>Charges of today's trips on this line, cancelled ones left out.</summary>
        public double ChargesToday { get; set; }

        /// <summary>The trip under way, or null.</summary>
        public LiveMapTripRefDto? CurrentTrip { get; set; }

        /// <summary>The next stop not yet performed, or null.</summary>
        public LiveMapNextStopDto? NextStop { get; set; }

        /// <summary>
        /// The trip under way, measured as Booking's tracking measures it (TripProgress): the phase,
        /// the miles still to go and the live ETAs. Null with no trip under way or no recent fix.
        /// </summary>
        public LiveMapTripProgressDto? Progress { get; set; }

        public LiveMapComplianceDto VehicleCompliance { get; set; } = new();
        public LiveMapComplianceDto DriverCompliance { get; set; } = new();
    }

    /// <summary>The trip under way, for the card's Live ETA tile and trip bar (the same pieces as Booking).</summary>
    public class LiveMapTripProgressDto
    {
        /// <summary><c>Pickup</c> (heading to the patient) or <c>Dropoff</c> (patient on board).</summary>
        public string Phase { get; set; } = "Pickup";

        public double? RemainingMiles { get; set; }

        /// <summary>The trip's own distance, pickup to drop-off: the bar's full length.</summary>
        public double? TripMiles { get; set; }

        public TimeSpan? PickupEta { get; set; }
        public TimeSpan? DropoffEta { get; set; }

        /// <summary>Promised pickup time and appointment time, business wall clock.</summary>
        public TimeSpan? PromisedPickup { get; set; }
        public TimeSpan? PromisedAppointment { get; set; }

        /// <summary>When the fix these figures come from was taken (UTC).</summary>
        public DateTime AtUtc { get; set; }

        /// <summary>
        /// The business's wall clock when this was measured (IOperationClock), so the client counts
        /// the minutes to go in the same clock the ETAs are in, never in its own PC's time zone.
        /// </summary>
        public TimeSpan BusinessNow { get; set; }

        /// <summary>When the patient was picked up (the pickup's ActualPerformTime), as Booking's bar shows it.</summary>
        public TimeSpan? PickedUpAt { get; set; }
    }

    public class LiveMapCapacityDto
    {
        public string SpaceType { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    /// <summary>A trip by its ids only: the internal one and the broker's number.</summary>
    public class LiveMapTripRefDto
    {
        public int TripId { get; set; }
        public string? TripNumber { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>The next stop: kind and times, never the patient or the address.</summary>
    public class LiveMapNextStopDto
    {
        /// <summary><c>Pickup</c> or <c>Dropoff</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Promised pickup or appointment time, business wall clock.</summary>
        public TimeSpan? Promised { get; set; }

        public TimeSpan? Eta { get; set; }
    }

    /// <summary>The state of a person's or a vehicle's documents, in one line.</summary>
    public class LiveMapComplianceDto
    {
        /// <summary>Approved documents already expired.</summary>
        public int Expired { get; set; }

        /// <summary>The approved document that expires next, and when.</summary>
        public string? NextDocument { get; set; }
        public DateTime? NextExpiresOn { get; set; }
    }
}

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// RE-080: one line's day on the map: its stops in order and the legs between them.
    /// </summary>
    /// <remarks>
    /// Office staff only. The stops carry coordinates (they are where patients are picked up and
    /// left, as on the Schedule tab's map) but no name, address or phone. The shapes come from the
    /// routing cache; a leg nobody has drawn yet comes without one and the client asks for it.
    /// </remarks>
    public class LiveMapRouteLegsDto
    {
        public int VehicleRouteId { get; set; }
        public DateTime OperatingDate { get; set; }
        public List<LiveMapStopDto> Stops { get; set; } = new();
        public List<LiveMapLegDto> Legs { get; set; } = new();
    }

    public class LiveMapStopDto
    {
        /// <summary>Order of the stop in the day, from 1.</summary>
        public int Number { get; set; }

        /// <summary><c>Pickup</c> or <c>Dropoff</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool Performed { get; set; }
        public TimeSpan? Eta { get; set; }

        /// <summary>The trip of this stop (internal id): lets the map show only the trip in progress.</summary>
        public int TripId { get; set; }

        /// <summary>The driver has arrived (ActualArriveTime): purple, as on the Dispatch map.</summary>
        public bool Arrived { get; set; }

        /// <summary>
        /// The stop's time for its label: when it was performed, or else its ETA. Business wall clock.
        /// </summary>
        public TimeSpan? Time { get; set; }
    }

    /// <summary>One stretch of road between two points of the day.</summary>
    public class LiveMapLegDto
    {
        public double FromLatitude { get; set; }
        public double FromLongitude { get; set; }
        public double ToLatitude { get; set; }
        public double ToLongitude { get; set; }

        /// <summary>The stop this leg ends at (<see cref="LiveMapStopDto.Number"/>).</summary>
        public int ToStop { get; set; }

        /// <summary>The leg starts at the garage: the drive to the first pickup.</summary>
        public bool FromGarage { get; set; }

        /// <summary>Patients on board along this leg. 0 is driving empty.</summary>
        public int Onboard { get; set; }

        /// <summary>Its end stop is performed: the whole leg is behind the vehicle.</summary>
        public bool Driven { get; set; }

        /// <summary>The leg the vehicle is on now: the first whose end is not performed.</summary>
        public bool Current { get; set; }

        /// <summary>
        /// Whether to draw it. A leg driven empty towards a pickup only shows once that trip has been
        /// started: before that the vehicle is waiting, and the map does not guess its next move.
        /// </summary>
        public bool Visible { get; set; }

        /// <summary>When the vehicle leaves for it, as the route plans it (to ask the shape for that hour).</summary>
        public TimeSpan? Departure { get; set; }

        /// <summary>The road, Google's encoded polyline, from the routing cache. Null when nobody has drawn it yet.</summary>
        public string? EncodedPolyline { get; set; }
    }
}

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// RE-080: the traffic on a line's road right now, from its vehicle to the stop it is heading
    /// for: Google's speed per stretch and the delay it adds. For the live map's delays layer.
    /// </summary>
    /// <summary>Copy of Raphael.Shared's LiveMapPunctualityDto: how late a line on route is running.</summary>
    public class LiveMapPunctualityDto
    {
        public int VehicleRouteId { get; set; }
        public string Phase { get; set; } = "Pickup";
        public TimeSpan? Eta { get; set; }
        public TimeSpan? Promised { get; set; }
        public int? LateMinutes { get; set; }
        public DateTime AtUtc { get; set; }
    }

    public class LiveMapRouteTrafficDto
    {
        public int VehicleRouteId { get; set; }
        public string? EncodedPolyline { get; set; }
        public List<LiveMapSpeedIntervalDto> Intervals { get; set; } = new();

        /// <summary>Minutes of driving with today's traffic, and without it.</summary>
        public int DurationInTrafficSeconds { get; set; }
        public int DurationSeconds { get; set; }

        /// <summary>What the traffic adds, never negative.</summary>
        public int DelaySeconds => Math.Max(0, DurationInTrafficSeconds - DurationSeconds);

        /// <summary>When Google was asked (UTC). Answers are kept a few minutes.</summary>
        public DateTime AtUtc { get; set; }
    }

    public class LiveMapSpeedIntervalDto
    {
        public int Start { get; set; }
        public int End { get; set; }

        /// <summary><c>NORMAL</c>, <c>SLOW</c> or <c>TRAFFIC_JAM</c>.</summary>
        public string Speed { get; set; } = "NORMAL";
    }
}
