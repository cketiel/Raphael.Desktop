using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>Shows a line only when it has something to say.</summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
