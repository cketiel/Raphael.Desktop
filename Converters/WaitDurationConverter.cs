using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// A waiting time the way a dispatcher would say it out loud: "45 m", "2 h 05 m".
    /// </summary>
    /// <remarks>
    /// Not <c>hh:mm</c>, which is the format every other clock field on this screen uses. That is
    /// the point: every one of those is an hour of the day, and this is a length of time. Printing
    /// a duration in the same shape as the hours around it invites reading "00:45" as a quarter to
    /// one in the morning.
    /// </remarks>
    public class WaitDurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not TimeSpan wait || wait <= TimeSpan.Zero) return string.Empty;

            return wait.TotalHours >= 1
                ? $"{(int)wait.TotalHours} h {wait.Minutes:00} m"
                : $"{(int)wait.TotalMinutes} m";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("WaitDurationConverter only formats; nothing parses back.");
        }
    }
}
