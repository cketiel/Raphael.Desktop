using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// A number of minutes as a proportional share of a grid's width.
    /// </summary>
    /// <remarks>
    /// The three parts of a stop's time bar — the drive, the wait, the margin kept in front of the
    /// promised hour — are held as minutes and laid out as star-sized columns, so a bar fits
    /// whatever width the column ends up with and no code has to measure anything.
    ///
    /// <para>
    /// Zero becomes an absolute zero rather than <c>0*</c>: a star column asked for no share still
    /// takes its minimum and would draw a sliver of colour for a segment that does not exist —
    /// a red thread on a stop where the driver never waits.
    /// </para>
    /// </remarks>
    public class StarWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var minutes = value as double? ?? 0d;

            if (minutes <= 0) return new GridLength(0, GridUnitType.Pixel);

            return new GridLength(minutes, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("StarWidthConverter only lays out; nothing reads back.");
        }
    }
}
