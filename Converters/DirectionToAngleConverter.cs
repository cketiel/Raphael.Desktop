using DocumentFormat.OpenXml.Math;
using System;
using System.Globalization;
using System.Reflection.Metadata;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    public class DirectionToAngleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {         
            string direction = value as string;
            if (string.IsNullOrEmpty(direction)) return 0.0;

            // Converts the cardinal direction(N, NE, E, SE, S, SW, W, NW) to an angle in degrees.
            switch (direction.ToUpper())
            {
                case "N": return 0.0;
                case "NNE": return 22.5;
                case "NE": return 45.0;
                case "ENE": return 67.5;
                case "E": return 90.0;
                case "ESE": return 112.5;
                case "SE": return 135.0;
                case "SSE": return 157.5;
                case "S": return 180.0;
                case "SSW": return 202.5;
                case "SW": return 225.0;
                case "WSW": return 247.5;
                case "W": return 270.0;
                case "WNW": return 292.5;
                case "NW": return 315.0;
                case "NNW": return 337.5;
                default: return 0.0;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}