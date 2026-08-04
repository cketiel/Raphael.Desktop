using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    // Custom converter to hide the placeholder when the report is ready
    public class AviataInverseVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // If True -> Collapsed (Hide placeholder), If False -> Visible (Show placeholder)
            return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}