using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Helpers
{
    /// <summary>True for false and the other way round.</summary>
    public sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }
}
