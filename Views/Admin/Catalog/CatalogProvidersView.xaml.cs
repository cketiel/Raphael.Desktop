using System.Windows.Controls;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Catalog;
using Raphael.Desktop.ViewModels.Catalog;

namespace Raphael.Desktop.Views.Admin.Catalog
{
    /// <summary>The catalog of entities that could carry out trips.</summary>
    public partial class CatalogProvidersView : UserControl
    {
        public CatalogProvidersView()
        {
            InitializeComponent();

            var viewModel = new CatalogProvidersViewModel(
                new CatalogProviderService(),
                new CatalogLookupService(),
                new ProviderService());

            DataContext = viewModel;

            Loaded += async (_, _) =>
            {
                await viewModel.LoadLookupsAsync();
                await viewModel.ReloadAsync(resetPage: true);
            };
        }
    }
}
