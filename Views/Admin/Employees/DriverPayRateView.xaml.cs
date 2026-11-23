using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Employees
{
    /// <summary>RE-082: a driver's pay rate and its history. Logic lives in <see cref="DriverPayRateViewModel"/>.</summary>
    public partial class DriverPayRateView : Window
    {
        public DriverPayRateView(DriverPayRateViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>
        /// Typing an amount means that is the way the driver is paid. Without this, clicking
        /// straight into the field left the other card selected, and nothing said so.
        /// View-only: it ticks the card the field sits in, which the ViewModel already binds.
        /// </summary>
        private void Amount_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            for (DependencyObject? d = sender as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is RadioButton card) { card.IsChecked = true; return; }
        }
    }
}
