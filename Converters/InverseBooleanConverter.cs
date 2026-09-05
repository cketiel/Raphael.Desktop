using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// The opposite of a bool, for the half of a two-way toggle that has no property of its own.
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }
}
