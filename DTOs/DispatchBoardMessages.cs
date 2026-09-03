using System;

namespace Raphael.Desktop.DTOs
{
    // Mirror of Raphael.Shared/DTOs/Realtime/DispatchBoardMessages.cs, copied by hand like
    // every other DTO here.
    //
    // ⚠️ These are NOT notifications. They never reach the bell, never count as unread, and
    // are not stored anywhere — not on the server and not here. A dispatcher who was not
    // looking has lost nothing: opening the tab loads the state from the API.
    //
    // ⚠️ They carry identifiers and never patient data. The screen turns an id into data
    // through the ordinary endpoints, which apply the provider filter.

    /// <summary>A trip was put on a route by somebody else. Stop offering it.</summary>
    public class TripRoutedMessage
    {
        public int TripId { get; set; }

        public int VehicleRouteId { get; set; }

        public DateTime Date { get; set; }
    }

    /// <summary>A trip came off its route and is waiting again.</summary>
    public class TripUnroutedMessage
    {
        public int TripId { get; set; }

        public int VehicleRouteId { get; set; }

        public DateTime Date { get; set; }
    }

    /// <summary>
    /// The stops of one route on one day moved. Says which route, not how: this screen reloads
    /// that route, which is a single query, and may be showing it under a different filter than
    /// whoever changed it.
    /// </summary>
    public class RouteChangedMessage
    {
        public int VehicleRouteId { get; set; }

        public DateTime Date { get; set; }
    }

    /// <summary>Where a vehicle is, as its driver last reported.</summary>
    public class VehiclePositionMessage
    {
        public int VehicleRouteId { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public double Speed { get; set; }

        public string? Direction { get; set; }

        /// <summary>
        /// When the fix was taken. This is what lets the map move the vehicle over the real gap
        /// between two reports instead of guessing one.
        /// </summary>
        public DateTime AtUtc { get; set; }
    }
}
