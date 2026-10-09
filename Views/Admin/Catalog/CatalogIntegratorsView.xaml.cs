using System.Windows.Controls;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Catalog;
using Raphael.Desktop.ViewModels.Catalog;

namespace Raphael.Desktop.Views.Admin.Catalog
{
    /// <summary>The catalog of entities that could send us trips.</summary>
    public partial class CatalogIntegratorsView : UserControl
    {
        public CatalogIntegratorsView()
        {
            InitializeComponent();

            // Services by constructor and the first load on Loaded, which is the cleaner of
            // the two patterns in this project: it keeps async work out of the constructor.
            var viewModel = new CatalogIntegratorsViewModel(
                new CatalogIntegratorService(),
                new CatalogLookupService(),
                new IntegratorService());

            DataContext = viewModel;

            Loaded += async (_, _) =>
            {
                await viewModel.LoadLookupsAsync();
                await viewModel.ReloadAsync(resetPage: true);
            };
        }
    }
}
