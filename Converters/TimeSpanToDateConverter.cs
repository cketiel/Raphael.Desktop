using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Lets a TimePicker, which speaks DateTime, edit a plain time of day.
    /// </summary>
    /// <remarks>
    /// The date half is thrown away on the way back. A filter that says "from 08:00" must not
    /// quietly also mean "on the day the control happened to be showing".
    /// </remarks>
    public class TimeSpanToDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TimeSpan span ? DateTime.Today.Add(span) : (DateTime?)null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DateTime moment ? moment.TimeOfDay : (TimeSpan?)null;
    }
}
