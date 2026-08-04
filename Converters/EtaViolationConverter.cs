using Raphael.Desktop.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    public class EtaViolationConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // We define the tolerance margin.
            var lateMargin = TimeSpan.FromMinutes(15);

            // We expect 4 values: EventType, ETA, Pickup, Appt
            if (values.Length < 4) return false;

            // Safely extract values
            var eventType = values[0] as ScheduleEventType?;
            var eta = values[1] as TimeSpan?;
            var pickup = values[2] as TimeSpan?;
            var appt = values[3] as TimeSpan?;

            // If the key values ​​(EventType or ETA) are null, there is no violation.
            if (!eventType.HasValue || !eta.HasValue)
            {
                return false;
            }

            // Check violation based on event type
            if (eventType.Value == ScheduleEventType.Pickup)
            {
                // There is only a violation if we have a Pickup time to compare.
                if (pickup.HasValue)
                {                                    
                    return eta.Value > (pickup.Value + lateMargin);
                }
            }
            else if (eventType.Value == ScheduleEventType.Dropoff)
            {
                // There is only a violation if we have an Appointment (Appt) time to compare.
                if (appt.HasValue)
                {
                    return eta.Value > (appt.Value + lateMargin);
                }
            }

            // If none of the violation conditions are met, we return false.
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}