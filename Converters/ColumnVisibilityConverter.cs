using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    public class ColumnVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // The value 'value' is the entire collection of ColumnConfigurations
            var configs = value as IEnumerable<ColumnConfig>;
            // The 'parameter' is the PropertyName of the current column.
            var propertyName = parameter as string;

            if (configs == null || propertyName == null)
            {
                return Visibility.Collapsed;
            }

            var config = configs.FirstOrDefault(c => c.PropertyName == propertyName);
            if (config == null)
            {
                return Visibility.Collapsed;
            }

            return config.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}