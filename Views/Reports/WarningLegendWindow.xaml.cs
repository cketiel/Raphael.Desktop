using Raphael.Desktop.Services.Help;
using Raphael.Desktop.ViewModels;
using System.Windows;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>The legend stays open beside the report (one at a time), so a warning and its meaning are seen together.</summary>
    public partial class WarningLegendWindow : Window
    {
        private static WarningLegendWindow? _open;
        private readonly WarningLegendViewModel _vm;

        private WarningLegendWindow(string? code)
        {
            InitializeComponent();
            DataContext = _vm = new WarningLegendViewModel(code);
            _vm.Confirm = text => MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            Owner = Application.Current?.MainWindow;
            Helpers.HelpAssist.SetTopicId(this, "desktop/reports/warnings");
            Loaded += async (_, _) => await _vm.LoadAsync();
            Closed += (_, _) => _open = null;
        }

        /// <summary>Opens the legend on a warning, or brings the one already open to it.</summary>
        public static void ShowLegend(string? code)
        {
            if (_open is null)
            {
                _open = new WarningLegendWindow(code);
                _open.Show();
                return;
            }
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open._vm.OpenOn(code);
            _open.Activate();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnHelp(object sender, RoutedEventArgs e) =>
            HelpService.Instance.Open(_vm.Selected?.IsShift == true ? "desktop/reports/productivity-hours" : "desktop/reports/warnings");
    }
}
