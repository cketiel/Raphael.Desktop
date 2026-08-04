using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Helpers
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (value is bool b) && b;

            // Invierte el resultado si el parámetro es "Invert"
            if (parameter != null && parameter.ToString().Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // La conversión inversa no es necesaria para este caso de uso.
            throw new NotImplementedException();
        }
    }
}