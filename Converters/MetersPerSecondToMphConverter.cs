using System;
using System.Globalization;
using System.Windows.Data;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Shows a GPS speed (m/s, as the Driver app sends it) in mph. See <see cref="SpeedUnits"/>.
    /// </summary>
    public class MetersPerSecondToMphConverter : IValueConverter
    {
        public static readonly MetersPerSecondToMphConverter Instance = new MetersPerSecondToMphConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double mps ? SpeedUnits.ToMph(mps) : 0d;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
