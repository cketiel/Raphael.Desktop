using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Catalog;
using Raphael.Desktop.Views.Admin;
using Raphael.Desktop.Views.Admin.Catalog;

namespace Raphael.Desktop.ViewModels.Catalog
{
    /// <summary>
    /// The catalog of entities that could carry out trips: NEMT companies, NEMT brokers,
    /// private ambulance operators.
    /// </summary>
    public class CatalogProvidersViewModel : CatalogListViewModelBase<CatalogProviderListItemDto>
    {
        private const int GeocodeBatchLimit = 100;

        private readonly ICatalogProviderService _catalog;
        private readonly ICatalogLookupService _lookups;
        private readonly IProviderService _providers;

        public CatalogProvidersViewModel(
            ICatalogProviderService catalog,
            ICatalogLookupService lookups,
            IProviderService providers)
        {
            _catalog = catalog;
            _lookups = lookups;
            _providers = providers;

            AddCommand = new AsyncRelayCommand(AddAsync);
            EditCommand = new AsyncRelayCommand<CatalogProviderListItemDto>(EditAsync);
            AddToMyCompanyCommand =
                new AsyncRelayCommand<CatalogProviderListItemDto>(AddToMyCompanyAsync);
            GeocodeCommand = new AsyncRelayCommand(GeocodePageAsync);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
        }

        public IAsyncRelayCommand AddCommand { get; }
        public IAsyncRelayCommand<CatalogProviderListItemDto> EditCommand { get; }
        public IAsyncRelayCommand<CatalogProviderListItemDto> AddToMyCompanyCommand { get; }
        public IAsyncRelayCommand GeocodeCommand { get; }
        public IAsyncRelayCommand ImportCommand { get; }

        private CatalogProviderListItemDto? _selectedItem;
        public CatalogProviderListItemDto? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        protected override Task<CatalogPageDto<CatalogProviderListItemDto>> FetchAsync(
            CatalogSearchRequestDto request,
            CancellationToken cancellationToken)
            => _catalog.SearchAsync(request, cancellationToken);

        public override async Task LoadLookupsAsync()
        {
            try
            {
                var categories = await _lookups.GetProviderCategoriesAsync();
                var counties = await _lookups.GetCountiesAsync();

                Categories.Clear();

                foreach (var category in categories)
                {
                    Categories.Add(category);
                }

                Counties.Clear();

                foreach (var county in counties)
                {
                    Counties.Add(county);
                }
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.LoadFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task AddAsync()
        {
            var viewModel = new CatalogProviderEditViewModel(
                _catalog, Categories, Counties, entry: null);

            var window = new CatalogProviderEditView
            {
                DataContext = viewModel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            if (window.ShowDialog() == true)
            {
                await ReloadAsync(resetPage: false);
            }
        }

        private async Task EditAsync(CatalogProviderListItemDto? item)
        {
            if (item is null)
            {
                return;
            }

            try
            {
                var full = await _catalog.GetByIdAsync(item.Id);

                if (full is null)
                {
                    return;
                }

                var viewModel = new CatalogProviderEditViewModel(
                    _catalog, Categories, Counties, full);

                var window = new CatalogProviderEditView
                {
                    DataContext = viewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                if (window.ShowDialog() == true)
                {
                    await ReloadAsync(resetPage: false);
                }
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.LoadFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Opens the provider form filled in from the catalog row, for the user to confirm.
        /// </summary>
        /// <remarks>
        /// The timezone comes across empty on purpose — a directory cannot know it, and what a
        /// pickup time means depends on it. The form is where somebody answers that.
        /// </remarks>
        private async Task AddToMyCompanyAsync(CatalogProviderListItemDto? item)
        {
            if (item is null)
            {
                return;
            }

            if (item.InMyCompany)
            {
                MessageBox.Show(
                    LocalizationService.Instance["catalog.AlreadyInMyCompanyMessage"],
                    LocalizationService.Instance["catalog.AddToMyCompany"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                var draft = await _catalog.GetCompanyDraftAsync(item.Id);

                if (draft is null)
                {
                    return;
                }

                var editViewModel = new ProviderEditViewModel(draft);
                var window = new ProviderEditView
                {
                    DataContext = editViewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                if (window.ShowDialog() != true)
                {
                    return;
                }

                await _providers.AddProviderAsync(draft, editViewModel.SelectedImagePath);
                await ReloadAsync(resetPage: false);

                MessageBox.Show(
                    string.Format(
                        LocalizationService.Instance["catalog.AddedToMyCompany"], draft.Name),
                    LocalizationService.Instance["catalog.AddToMyCompany"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.AddToMyCompanyFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Resolves coordinates for the rows on screen that have none. One page at a time:
        /// the daily Geocoding quota is 1,500 calls.
        /// </summary>
        private async Task GeocodePageAsync()
        {
            var ids = Items
                .Where(i => !i.HasCoordinates)
                .Select(i => i.Id)
                .Take(GeocodeBatchLimit)
                .ToList();

            if (ids.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.Instance["catalog.NothingToGeocode"],
                    LocalizationService.Instance["catalog.Geocode"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                string.Format(LocalizationService.Instance["catalog.GeocodeConfirm"], ids.Count),
                LocalizationService.Instance["catalog.Geocode"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var result = await _catalog.GeocodeAsync(ids);

                await ReloadAsync(resetPage: false);

                MessageBox.Show(
                    string.Format(
                        LocalizationService.Instance["catalog.GeocodeResult"],
                        result.Resolved, result.NotFound, result.Failed, result.Skipped),
                    LocalizationService.Instance["catalog.Geocode"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.Geocode"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task ImportAsync()
        {
            var viewModel = new CatalogImportViewModel(
                new CatalogProviderImportTarget(_catalog),
                Categories);

            var window = new CatalogImportView
            {
                DataContext = viewModel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            window.ShowDialog();

            if (viewModel.AnythingImported)
            {
                await LoadLookupsAsync();
                await ReloadAsync(resetPage: true);
            }
        }

        // ---- Translation, the part particular to this catalog ------------------------------

        public string TitleText => LocalizationService.Instance["catalog.ProvidersTitle"];
        public string ColumnNpi => LocalizationService.Instance["catalog.ColumnNpi"];
        public string ColumnServiceLevel => LocalizationService.Instance["catalog.ColumnServiceLevel"];
    }
}
