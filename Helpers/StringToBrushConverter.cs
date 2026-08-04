using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Raphael.Desktop.Helpers
{
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorString && !string.IsNullOrEmpty(colorString))
            {
                try
                {
                    // BrushConverter puede manejar nombres de color ("Red") y códigos hexadecimales ("#FF0000")
                    return (Brush)new BrushConverter().ConvertFromString(colorString);
                }
                catch
                {
                    // Si el color no es válido, retorna transparente
                    return Brushes.Transparent;
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}