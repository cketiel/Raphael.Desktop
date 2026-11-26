using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Lógica de interacción para ReportsView.xaml
    /// </summary>
    public partial class ReportsView : UserControl
    {
        public ReportsView()
        {
            InitializeComponent();
            this.DataContext = new ViewModels.ReportsViewModel();

            // RE-092: a new list of rows is measured with its day open and wider than the grid, and the star
            // columns stay at their minimum. Once it is drawn they are given their share again.
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
                .AddValueChanged(ProductivityGrid, (_, _) => Dispatcher.BeginInvoke(new Action(SpreadColumns), System.Windows.Threading.DispatcherPriority.ApplicationIdle));
        }

        private void SpreadColumns()
        {
            foreach (var column in ProductivityGrid.Columns)
            {
                if (!column.Width.IsStar) continue;
                var star = column.Width;
                column.Width = new DataGridLength(column.MinWidth);
                column.Width = star;
            }
        }

        // RE-092: a click on the open row closes its details, so the whole report can be seen. A click inside
        // the details, or on a button, is theirs.
        private void ProductivityGrid_RowClick(object sender, MouseButtonEventArgs e)
        {
            for (var at = e.OriginalSource as DependencyObject; at is not null and not DataGrid;
                 at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            {
                if (at is System.Windows.Controls.Primitives.DataGridDetailsPresenter or System.Windows.Controls.Primitives.ButtonBase) return;
                if (at is not DataGridRow row) continue;
                if (!row.IsSelected) return;
                ProductivityGrid.SelectedItem = null;
                e.Handled = true;
                return;
            }
        }

        // RE-081: the productivity filters are read the first time the tab is opened, not with the screen.
        /// <summary>RE-096: opens on the impossible and loose trips of a day.</summary>
        public async void ShowHours(System.DateTime date)
        {
            HoursTab.IsSelected = true;
            if (DataContext is ViewModels.ReportsViewModel vm) await vm.Impossible.ShowDayAsync(date);
        }

        private async void ProductivityGrid_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ReportsViewModel vm) await vm.Productivity.EnsureFiltersAsync();
        }
    }
}
