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
    }
}
