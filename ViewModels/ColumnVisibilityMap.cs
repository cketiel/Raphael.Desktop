using Raphael.Desktop.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// Answers "is this column showing?" for a grid whose columns are declared in XAML.
    /// </summary>
    /// <remarks>
    /// A <c>DataGridColumn</c> is not in the visual tree, so it inherits no DataContext and cannot
    /// bind to the ViewModel directly; the binding has to go through a proxy and name the column.
    /// An indexer is what lets one line in XAML say which column it is —
    /// <c>{Binding Data.ColumnVisibility[PickupCity], Source={StaticResource vmProxy}}</c> — instead
    /// of a property per column.
    ///
    /// Raising <c>Item[]</c> is what WPF listens to for "every indexed value changed"; without it
    /// the dialog would save a new layout and the grid would keep the old one until reopened.
    /// </remarks>
    public class ColumnVisibilityMap : INotifyPropertyChanged
    {
        private Dictionary<string, bool> _shown = new();

        public event PropertyChangedEventHandler PropertyChanged;

        public Visibility this[string propertyName] =>
            !_shown.TryGetValue(propertyName, out var visible) || visible
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        /// Takes a layout. A column the layout does not mention stays visible, so a column added
        /// to the grid later does not disappear for everyone who already saved a layout.
        /// </summary>
        public void Apply(IEnumerable<ColumnConfig> columns)
        {
            _shown = columns?.ToDictionary(c => c.PropertyName, c => c.IsVisible) ?? new Dictionary<string, bool>();

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
