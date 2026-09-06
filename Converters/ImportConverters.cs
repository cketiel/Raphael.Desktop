using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Raphael.Desktop.Models.Import;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Colours one line of the running account by how loud it is.
    /// </summary>
    /// <remarks>
    /// A hundred lines scroll past during an import and almost all of them are routine. The only
    /// reason to colour them is so the two that are not routine can be found without reading the
    /// other ninety-eight — so info stays deliberately quiet, and only warning and error carry a
    /// colour at all.
    /// </remarks>
    public class ImportSeverityToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Quiet = Frozen("#FF5F5F6B");
        private static readonly SolidColorBrush Good = Frozen("#FF2E7D32");
        private static readonly SolidColorBrush Careful = Frozen("#FFB26A00");
        private static readonly SolidColorBrush Bad = Frozen("#FFC62828");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value switch
            {
                ImportSeverity.Success => Good,
                ImportSeverity.Warning => Careful,
                ImportSeverity.Error => Bad,
                _ => Quiet
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static SolidColorBrush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();

            return brush;
        }
    }

    /// <summary>
    /// Shows an editor field only when the problem being fixed is about that field.
    /// </summary>
    /// <remarks>
    /// A row rejected for having no patient identity opens on the patient fields, not on all
    /// twenty. Handing a dispatcher every field of a trip and letting them work out which one the
    /// server meant is how a correction screen turns into a puzzle — and a puzzle is where people
    /// change the wrong thing and send it again.
    /// </remarks>
    public class ImportFieldVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ImportField fields || parameter is not string name) return Visibility.Collapsed;

            return Enum.TryParse<ImportField>(name, out var wanted) && fields.HasFlag(wanted)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Shows a block only when the thing it describes exists.
    /// </summary>
    /// <remarks>
    /// Not EmptyStringToVisibilityConverter, which casts with `as string` and therefore answers
    /// "collapsed" for every object that is not a string - the conflict this panel exists to show
    /// included. The block simply never appeared.
    /// </remarks>
    public class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>The other half: shown only while there is nothing to show.</summary>
    public class IsNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>Formats a time of day, or a dash where a file left one out.</summary>
    public class TimeSpanToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TimeSpan span ? DateTime.Today.Add(span).ToString("HH:mm") : "—";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
