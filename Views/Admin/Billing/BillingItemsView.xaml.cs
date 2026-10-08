using System;
using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;

using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Billing
{
    /// <summary>
    /// Lógica de interacción para BillingItemsView.xaml
    /// </summary>
    public partial class BillingItemsView : UserControl
    {
        public BillingItemsView()
        {
            InitializeComponent();

            var billingItemService = new BillingItemService();
            var unitService = new UnitService(); 
            var viewModel = new BillingItemsViewModel(billingItemService, unitService);
            DataContext = viewModel;

            // Every load or reload ends in a fresh column layout (see RefreshColumnWidths).
            viewModel.BillingItems.CollectionChanged += OnItemsChanged;

            // Load data when the control is ready to display
            Loaded += async (sender, e) => {
                if (viewModel.LoadBillingItemsCommand.CanExecute(null))
                {
                    await viewModel.LoadBillingItemsCommand.ExecuteAsync(null);
                }
            };
        }

        private bool _refreshQueued;

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // A load adds the rows one by one: one refresh once they are all in, not one per row.
            if (_refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.BeginInvoke(RefreshColumnWidths, DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// Lays the columns out again once the groups exist.
        /// </summary>
        /// <remarks>
        /// ⚠️ A grouped DataGrid sizes its columns once, while the groups still have no width, and
        /// leaves every column at its 20 px minimum, headers included (measured with UI Automation,
        /// RE-029). Folding and unfolding a group fixed it, which is what this does by hand: setting
        /// each width again makes the grid compute them with the space it really has.
        /// </remarks>
        private void RefreshColumnWidths()
        {
            _refreshQueued = false;
            foreach (var column in BillingItemsGrid.Columns)
            {
                var width = column.Width;
                column.Width = 0;
                column.Width = width;
            }
        }
    }
}
