using Raphael.Desktop.ViewModels.Admin;
using System.Windows;
using System.Windows.Controls;

namespace Raphael.Desktop.Views.Admin
{
    /// <summary>
    /// The Google Maps control panel: consumption, cost, saving, and the settings that move them.
    /// </summary>
    public partial class GoogleMapsAdminView : UserControl
    {
        private GoogleMapsAdminViewModel ViewModel => DataContext as GoogleMapsAdminViewModel;

        public GoogleMapsAdminView()
        {
            InitializeComponent();

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Loads on first sight rather than on a button, because a panel that opens empty
            // teaches the administrator to distrust it.
            if (ViewModel != null) await ViewModel.LoadAsync();
        }

        // The chart-or-table toggles. Two-way binding on IsChecked would have each radio button
        // fight its own group as the other one clears, so the switch is made here instead.
        private void DailyChart_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.ShowDailyAsTable = false;
        }

        private void DailyTable_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.ShowDailyAsTable = true;
        }

        private void SkuTable_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.ShowSkuAsTable = true;
        }

        private void SkuChart_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.ShowSkuAsTable = false;
        }
    }
}
