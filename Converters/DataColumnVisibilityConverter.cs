using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Whether a column of figures is on screen: the dispatcher chose to see it, and the route is
    /// not currently folded down to its time bars.
    /// </summary>
    /// <remarks>
    /// ⚠️ Two conditions and not one, and deliberately stateless. The folding could have been done
    /// by flipping <c>IsVisible</c> on the saved column settings, which would have needed no
    /// binding changes at all — and would have put a temporary view mode inside the thing that
    /// gets written to the dispatcher's configuration file. One save while the route was folded
    /// and their whole grid layout would come back empty. Asking the question here means the
    /// saved settings are never touched by it.
    ///
    /// <para>
    /// Sibling of <see cref="ColumnVisibilityConverter"/>, which still serves the columns that
    /// stay through both views — actions, the marker number, the bars themselves.
    /// </para>
    /// </remarks>
    public class DataColumnVisibilityConverter : IMultiValueConverter
    {
        // Same trick as ColumnVisibilityConverter: this is asked once per column on every pass,
        // twenty times on this screen, and the collection is normally the same instance.
        private IEnumerable<ColumnConfig> _cachedFor;
        private int _cachedCount = -1;
        private Dictionary<string, bool> _visibilityByProperty;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Visibility.Collapsed;

            var configs = values[0] as IEnumerable<ColumnConfig>;
            var propertyName = parameter as string;

            if (configs == null || propertyName == null) return Visibility.Collapsed;

            // One stop switched to a bar is enough to fold the figures away, because that is
            // the only way to give that row the layout it needs: a DataGrid's columns belong to
            // the grid, so a row cannot have a different set from its neighbours. What is left is
            // the name, the bar and the marker number — the shape the dispatcher asked for.
            // UnsetValue — a binding that has not resolved — means "not folded", which is the
            // state the grid opens in.
            if (values[1] is bool anyRowAsBar && anyRowAsBar) return Visibility.Collapsed;

            var count = (configs as ICollection<ColumnConfig>)?.Count ?? configs.Count();

            if (!ReferenceEquals(configs, _cachedFor) || count != _cachedCount)
            {
                _visibilityByProperty = new Dictionary<string, bool>();
                foreach (var config in configs)
                {
                    if (config?.PropertyName == null) continue;
                    _visibilityByProperty[config.PropertyName] = config.IsVisible;
                }
                _cachedFor = configs;
                _cachedCount = count;
            }

            return _visibilityByProperty.TryGetValue(propertyName, out var isVisible) && isVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException(
                "DataColumnVisibilityConverter reports a condition; nothing sets it.");
        }
    }
}
