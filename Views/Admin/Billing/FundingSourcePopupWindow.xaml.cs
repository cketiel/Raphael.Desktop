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
using System.Windows.Shapes;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Billing
{
    /// <summary>
    /// Lógica de interacción para FundingSourcePopupWindow.xaml
    /// </summary>
    public partial class FundingSourcePopupWindow : Window
    {
        public FundingSourcePopupWindow(FundingSourcePopupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += (dialogResult) => {
                this.DialogResult = dialogResult;
                this.Close();
            };
        }
    }
}
