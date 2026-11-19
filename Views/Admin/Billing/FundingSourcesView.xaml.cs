using System.Windows.Controls;
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Billing
{
    /// <summary>Admin → Billing → Funding Sources (RE-072).</summary>
    public partial class FundingSourcesView : UserControl
    {
        private bool _loaded;

        public FundingSourcesView()
        {
            InitializeComponent();

            var viewModel = new FundingSourcesViewModel(
                new FundingSourceService(),
                new FundingSourceBillingItemService(),
                new BillingItemService(),
                new SpaceTypeService(),
                new IntegratorService());
            DataContext = viewModel;

            // Loaded fires every time the tab is shown again. Reading everything each time used to
            // empty the list and lose the funding source being worked on, so it loads once.
            // Live while shown (RE-072): the language and other people's changes.
            Unloaded += (s, e) => viewModel.Detach();

            Loaded += async (s, e) =>
            {
                viewModel.Attach();
                if (_loaded) return;
                _loaded = true;
                await viewModel.LoadCommand.ExecuteAsync(null);
            };
        }
    }
}
