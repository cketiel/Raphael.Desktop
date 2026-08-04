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
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Billing
{
    /// <summary>
    /// Lógica de interacción para FundingSourcesView.xaml
    /// </summary>
    public partial class FundingSourcesView : UserControl
    {
        public FundingSourcesView()
        {
            InitializeComponent();
            var fsService = new FundingSourceService();
            var fsbiService = new FundingSourceBillingItemService();
            var billingItemService = new BillingItemService();
            var spaceTypeService = new SpaceTypeService();           

            var viewModel = new FundingSourcesViewModel(fsService, fsbiService, billingItemService, spaceTypeService); 
            DataContext = viewModel;            

            Loaded += async (s, e) => {
                if (viewModel.LoadFundingSourcesCommand.CanExecute(null))
                {
                    await viewModel.LoadFundingSourcesCommand.ExecuteAsync(null);
                }
            };
        }
    }
}
