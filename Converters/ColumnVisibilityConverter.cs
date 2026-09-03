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
        // The same collection instance is handed to this converter once per column — twenty
        // times on the Schedule screen — and each call used to scan it from the start. The
        // lookup is built once per collection instance and reused; a new instance (the view
        // model assigns a new collection when the column settings are saved) rebuilds it.
        private IEnumerable<ColumnConfig> _cachedFor;
        private int _cachedCount = -1;
        private Dictionary<string, bool> _visibilityByProperty;

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

            // The count guards the case where the same instance is refilled rather than replaced.
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}