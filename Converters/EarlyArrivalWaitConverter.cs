using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// True when a stop leaves its driver waiting long enough to be worth colouring the row.
    /// </summary>
    /// <remarks>
    /// The route never shows a vehicle arriving more than a quarter of an hour before the hour
    /// the patient was promised, so an earlier arrival is raised to that limit. The drive is not
    /// shortened — the roads take what they take — so the difference is a driver sitting outside
    /// a house with the engine off.
    ///
    /// <para>
    /// ⚠️ This and <see cref="EtaViolationConverter"/> can never both be true for the same row:
    /// one says the vehicle gets there too early and the other that it gets there too late. That
    /// is what lets both paint <c>Background</c> without fighting over it, and it is why the
    /// order of the two in the row style is safe.
    /// </para>
    ///
    /// <para>
    /// Any wait at all is shown by the chip on the arrival hour. This is only the point at which
    /// it becomes worth a whole row, and that point is an administrator's decision — an operation
    /// running tight routes wants to see a quarter of an hour, one with slack would have every
    /// row painted and would stop reading the colour.
    /// </para>
    /// </remarks>
    public class EarlyArrivalWaitConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Two values: the wait, and the threshold the administrator set.
            if (values.Length < 2) return false;

            // A binding that has not resolved yet hands over UnsetValue, which "as" would turn
            // into a perfectly innocent null. Nothing to judge either way, so stay quiet.
            if (values[0] == DependencyProperty.UnsetValue ||
                values[1] == DependencyProperty.UnsetValue)
            {
                return false;
            }

            if (values[0] is not TimeSpan wait) return false;

            if (wait <= TimeSpan.Zero) return false;

            var thresholdMinutes = values[1] as int? ?? 30;

            // A threshold of zero would mean "colour every wait", which turns the whole screen
            // amber and makes the colour mean nothing. Treated as "not configured".
            if (thresholdMinutes <= 0) return false;

            return wait >= TimeSpan.FromMinutes(thresholdMinutes);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException(
                "EarlyArrivalWaitConverter reports a condition; nothing sets it.");
        }
    }
}
