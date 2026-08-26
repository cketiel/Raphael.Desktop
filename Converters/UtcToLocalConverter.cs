using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Shows an instant stored in UTC in the hour of whoever is looking at the screen.
    /// </summary>
    /// <remarks>
    /// For "when did this happen" values — a change to a trip, a notification. Those are
    /// instants, and a dispatcher anywhere should read them on their own clock.
    ///
    /// <para>
    /// ⚠️ Not for trip times. A pickup at 09:15 is 09:15 at the pickup address, and
    /// converting it to the viewer's zone would show a dispatcher in another region an hour
    /// the vehicle is not due — which is how a car gets sent three hours early.
    /// </para>
    ///
    /// <para>
    /// Rows written before 2026-08-25 carry the writer's local time rather than UTC, so they
    /// are shifted. Nothing here can tell them apart; see <c>_meta/BACKLOG.md</c>.
    /// </para>
    /// </remarks>
    public class UtcToLocalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime utc)
                return value;

            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime local)
                return value;

            return local.ToUniversalTime();
        }
    }
}
