using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    public class IndexToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index && index > 0)
            {
                // For each index, we move 12 pixels to the right and 12 down.
                //This way they will look staggered.
                double offset = index * 12.0;
                return new Thickness(offset, offset, 0, 0);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}