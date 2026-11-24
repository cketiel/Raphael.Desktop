using System;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// Requests one screen makes of the main window to open another (RE-051).
    /// </summary>
    /// <remarks>
    /// A static event because the screens are created by the main window and hold no reference back
    /// to it. MainWindow is the only subscriber.
    /// </remarks>
    public static class AppNavigation
    {
        /// <summary>Raised to open Schedule on a route and day, with one trip's two stops picked out.</summary>
        public static event Action<DateTime, int, int> ShowTripInScheduleRequested;

        public static void ShowTripInSchedule(DateTime date, int vehicleRouteId, int tripId)
        {
            if (vehicleRouteId <= 0) return;
            ShowTripInScheduleRequested?.Invoke(date.Date, vehicleRouteId, tripId);
        }

        /// <summary>
        /// Raised to open Schedule on a day's unscheduled trips, with some picked out: the trips an
        /// Out of Service sent back (RE-053).
        /// </summary>
        public static event Action<DateTime, IReadOnlyCollection<int>> ShowUnscheduledRequested;
        public static void ShowUnscheduled(DateTime date, IReadOnlyCollection<int> tripIds)
        {
            ShowUnscheduledRequested?.Invoke(date.Date, tripIds ?? Array.Empty<int>());
        }

        /// <summary>RE-080: opens Dispatch (Schedules) on a route and day, with no trip picked out.</summary>
        public static event Action<DateTime, int> ShowRouteInScheduleRequested;

        public static void ShowRouteInSchedule(DateTime date, int vehicleRouteId)
        {
            if (vehicleRouteId <= 0) return;
            ShowRouteInScheduleRequested?.Invoke(date.Date, vehicleRouteId);
        }

        /// <summary>RE-085: opens Home on a day, showing one part of it (done, canceled…).</summary>
        public static event Action<DateTime, Models.TripPart> ShowPartInHomeRequested;

        public static void ShowPartInHome(DateTime date, Models.TripPart part) => ShowPartInHomeRequested?.Invoke(date.Date, part);

        /// <summary>RE-080: opens Home searching one trip by the broker's number.</summary>
        public static event Action<string> ShowTripInHomeRequested;

        public static void ShowTripInHome(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber)) return;
            ShowTripInHomeRequested?.Invoke(tripNumber.Trim());
        }
    }
}
