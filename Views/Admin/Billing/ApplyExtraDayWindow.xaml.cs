using System.Windows;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Billing
{
    public partial class ApplyExtraDayWindow : Window
    {
        public ApplyExtraDayWindow(ApplyExtraDayViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += ok => { DialogResult = ok; Close(); };
        }
    }
}
