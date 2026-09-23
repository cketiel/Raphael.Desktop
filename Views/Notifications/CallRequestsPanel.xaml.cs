using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Raphael.Desktop.ViewModels.CallRequests;

namespace Raphael.Desktop.Views.Notifications
{
    public partial class CallRequestsPanel : UserControl
    {
        private CallRequestsPanelViewModel? _viewModel;

        private bool _syncing;

        public CallRequestsPanel()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelChanged;

            _viewModel = e.NewValue as CallRequestsPanelViewModel;

            if (_viewModel is not null)
                _viewModel.PropertyChanged += OnViewModelChanged;

            ShowSelection();
        }

        /// <summary>A row picked in either list is the one selection, and the other list lets go of its own.</summary>
        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _viewModel is null)
                return;

            if (sender is not ListBox { SelectedItem: CallRequestItemViewModel item } list)
                return;

            _syncing = true;

            try
            {
                _viewModel.Selected = item;

                var other = ReferenceEquals(list, OpenList) ? ClosedList : OpenList;
                other.SelectedItem = null;
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CallRequestsPanelViewModel.Selected) or "")
                ShowSelection();
        }

        /// <summary>A selection made from outside — an alert, the header counter — shows in the right list.</summary>
        private void ShowSelection()
        {
            if (_syncing || _viewModel is null)
                return;

            _syncing = true;

            try
            {
                var selected = _viewModel.Selected;

                OpenList.SelectedItem = selected is not null && _viewModel.Open.Contains(selected) ? selected : null;
                ClosedList.SelectedItem = selected is not null && _viewModel.Closed.Contains(selected) ? selected : null;

                if (OpenList.SelectedItem is not null)
                    OpenList.ScrollIntoView(OpenList.SelectedItem);
                else if (ClosedList.SelectedItem is not null)
                    ClosedList.ScrollIntoView(ClosedList.SelectedItem);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string topic } && !string.IsNullOrWhiteSpace(topic))
                Services.Help.HelpService.Instance.Open(topic);
        }
    }
}
