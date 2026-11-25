using Raphael.Desktop.ViewModels;
using System.Windows;

namespace Raphael.Desktop.Views.Reports
{
    public partial class OdometerHistoryWindow : Window
    {
        private readonly OdometerHistoryViewModel _vm;

        private OdometerHistoryWindow(int vehicleId, string? vehicleName)
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;
            DataContext = _vm = new OdometerHistoryViewModel(vehicleId, vehicleName);
            Helpers.HelpAssist.SetTopicId(this, "desktop/data/odometer");
            Loaded += async (_, _) => await _vm.LoadAsync();
        }

        /// <summary>True when something was corrected.</summary>
        public static bool Show(int vehicleId, string? vehicleName)
        {
            var window = new OdometerHistoryWindow(vehicleId, vehicleName);
            window.ShowDialog();
            return window._vm.HasCorrected;
        }

        private void OnCorrect(object sender, RoutedEventArgs e) => Correct(sender);

        private void OnReading(object sender, System.Windows.Input.MouseButtonEventArgs e) => Correct(sender);

        private async void Correct(object sender)
        {
            if ((sender as FrameworkElement)?.DataContext is OdometerHistoryRow { CanCorrect: true } row && OdometerCorrectionWindow.Show(row.Target, this))
                await _vm.CorrectedAsync();
        }
    }
}
