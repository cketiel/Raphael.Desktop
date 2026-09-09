using System;
using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Binds one radio button to one value of an enum property.
    /// </summary>
    /// <remarks>
    /// Returning <see cref="Binding.DoNothing"/> when a button is being unchecked matters: a radio
    /// group unchecks the old button before checking the new one, and writing back on that first
    /// event would set the property from the option the dispatcher just left.
    /// </remarks>
    public class EnumMatchConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && parameter != null &&
               string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool chosen || !chosen || parameter == null) return Binding.DoNothing;

            try
            {
                return Enum.Parse(Nullable.GetUnderlyingType(targetType) ?? targetType, parameter.ToString());
            }
            catch (Exception)
            {
                return Binding.DoNothing;
            }
        }
    }
}
