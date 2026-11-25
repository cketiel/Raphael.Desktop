using System.Globalization;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>A width less a fixed number of pixels: what a row's details may take of the grid they are in.</summary>
    public sealed class MinusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d && d > 0 && double.TryParse(parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var less)
                ? Math.Max(0, d - less) : double.PositiveInfinity;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
