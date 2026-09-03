using Raphael.Desktop.Models;
using System;
using System.Globalization;
using System.Windows;
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

            // Safely extract values.
            // UnsetValue is checked apart from null: a binding that has not resolved yet hands
            // over UnsetValue, and "as" turns that into null just like a genuinely empty field.
            // The two mean different things and only one of them is worth reporting.
            var unresolved = values[0] == DependencyProperty.UnsetValue
                          || values[1] == DependencyProperty.UnsetValue
                          || values[2] == DependencyProperty.UnsetValue
                          || values[3] == DependencyProperty.UnsetValue;

            var eventType = values[0] as ScheduleEventType?;
            var eta = values[1] as TimeSpan?;
            var pickup = values[2] as TimeSpan?;
            var appt = values[3] as TimeSpan?;

            if (unresolved)
            {
                Trace($"binding not resolved yet: eventType={Describe(values[0])} eta={Describe(values[1])} " +
                      $"pickup={Describe(values[2])} appt={Describe(values[3])}");
                return false;
            }

            // If the key values ​​(EventType or ETA) are null, there is no violation.
            if (!eventType.HasValue || !eta.HasValue)
            {
                // An event with an hour but no type cannot be judged, and that is worth knowing:
                // every Pickup and Dropoff is written with a type, so a missing one is a defect
                // upstream, not an ordinary row. Pull-out and Pull-in have no ETA either, so they
                // stay quiet.
                if (eta.HasValue && !eventType.HasValue)
                    Trace($"no event type, ETA {eta.Value}: cannot tell which hour it promised");

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

                Trace($"Pickup with ETA {eta.Value} has no scheduled pickup time: nothing to be late against");
            }
            else if (eventType.Value == ScheduleEventType.Dropoff)
            {
                // There is only a violation if we have an Appointment (Appt) time to compare.
                if (appt.HasValue)
                {
                    return eta.Value > (appt.Value + lateMargin);
                }

                Trace($"Dropoff with ETA {eta.Value} has no appointment time: nothing to be late against");
            }

            // If none of the violation conditions are met, we return false.
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Reports only the cases where this converter declined to judge a row. A row it can
        /// judge says nothing, so the trace stays readable with four hundred rows on screen.
        /// Compiled away outside DEBUG.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Trace(string message) =>
            System.Diagnostics.Debug.WriteLine($"[eta] {message}");

        private static string Describe(object value)
        {
            if (value == DependencyProperty.UnsetValue) return "<unset>";
            return value?.ToString() ?? "<null>";
        }
    }
}