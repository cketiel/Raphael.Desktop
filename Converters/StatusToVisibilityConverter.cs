using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Converters
{
    public class StatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            
            if (value is string statusString && parameter is string targetStatus)
            {
                
                bool isCanceled = (statusString == TripStatus.Canceled);

                if (targetStatus == "Canceled" && isCanceled)
                {
                    return Visibility.Visible;
                }

                if (targetStatus == "NotCanceled" && !isCanceled)
                {
                    return Visibility.Visible;
                }

                return Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}