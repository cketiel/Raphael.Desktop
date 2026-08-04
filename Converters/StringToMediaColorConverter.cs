// Raphael.Desktop/Converters/StringToMediaColorConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Raphael.Desktop.Converters
{
    public class StringToMediaColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorString)
            {
                try
                {
                    // ColorConverter puede manejar nombres de color ("Red") y códigos hexadecimales ("#FF0000")
                    return (Color)ColorConverter.ConvertFromString(colorString);
                }
                catch
                {
                    return Colors.Transparent; // Color por defecto si la conversión falla
                }
            }
            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                // Devuelve el color en formato #AARRGGBB. 
                // Si solo quieres nombres de color comunes, la lógica sería más compleja.
                // Para el ColorPicker, es mejor que devuelva el string exacto que espera.
                // Un nombre de color como "Red" se convertirá a "#FFFF0000" por Color.ToString().
                // Si quieres mantener "Red" si el usuario eligió "Red", necesitarías una lógica más específica.
                // Por simplicidad, y porque el ColorPicker trabaja bien con hex, usamos ToString().
                return color.ToString();
            }
            return string.Empty;
        }
    }
}