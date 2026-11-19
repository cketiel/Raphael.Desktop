using System.Windows;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Billing
{
    public partial class CopyRatesWindow : Window
    {
        public CopyRatesWindow(CopyRatesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += ok => { DialogResult = ok; Close(); };
        }
    }
}
