using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Catalog;
using Raphael.Desktop.Views.Admin;
using Raphael.Desktop.Views.Admin.Catalog;

namespace Raphael.Desktop.ViewModels.Catalog
{
    /// <summary>
    /// The catalog of entities that could send us trips: nursing homes, assisted living
    /// residences, hospitals.
    /// </summary>
    public class CatalogIntegratorsViewModel : CatalogListViewModelBase<CatalogIntegratorListItemDto>
    {
        /// <summary>
        /// Most rows one geocoding request takes, matching the server's own limit. The daily
        /// Geocoding quota is 1,500 calls and the catalog is thousands of rows.
        /// </summary>
        private const int GeocodeBatchLimit = 100;

        private readonly ICatalogIntegratorService _catalog;
        private readonly ICatalogLookupService _lookups;
        private readonly IIntegratorService _integrators;

        public CatalogIntegratorsViewModel(
            ICatalogIntegratorService catalog,
            ICatalogLookupService lookups,
            IIntegratorService integrators)
        {
            _catalog = catalog;
            _lookups = lookups;
            _integrators = integrators;

            AddCommand = new AsyncRelayCommand(AddAsync);
            EditCommand = new AsyncRelayCommand<CatalogIntegratorListItemDto>(EditAsync);
            AddToMyCompanyCommand =
                new AsyncRelayCommand<CatalogIntegratorListItemDto>(AddToMyCompanyAsync);
            GeocodeCommand = new AsyncRelayCommand(GeocodePageAsync);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
        }

        public IAsyncRelayCommand AddCommand { get; }
        public IAsyncRelayCommand<CatalogIntegratorListItemDto> EditCommand { get; }
        public IAsyncRelayCommand<CatalogIntegratorListItemDto> AddToMyCompanyCommand { get; }
        public IAsyncRelayCommand GeocodeCommand { get; }
        public IAsyncRelayCommand ImportCommand { get; }

        private CatalogIntegratorListItemDto? _selectedItem;
        public CatalogIntegratorListItemDto? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        protected override Task<CatalogPageDto<CatalogIntegratorListItemDto>> FetchAsync(
            CatalogSearchRequestDto request,
            CancellationToken cancellationToken)
            => _catalog.SearchAsync(request, cancellationToken);

        public override async Task LoadLookupsAsync()
        {
            try
            {
                var categories = await _lookups.GetIntegratorCategoriesAsync();
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
            var viewModel = new CatalogIntegratorEditViewModel(
                _catalog, Categories, Counties, entry: null);

            var window = new CatalogIntegratorEditView
            {
                DataContext = viewModel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            if (window.ShowDialog() == true)
            {
                await ReloadAsync(resetPage: false);
            }
        }

        private async Task EditAsync(CatalogIntegratorListItemDto? item)
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

                var viewModel = new CatalogIntegratorEditViewModel(
                    _catalog, Categories, Counties, full);

                var window = new CatalogIntegratorEditView
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
        /// Opens the integrator form filled in from the catalog row. The user confirms it; the
        /// catalog row stays where it is either way.
        /// </summary>
        /// <remarks>
        /// Nothing is created behind anybody's back. Saving generates a live API key against
        /// our API, which is not something a single click should do unseen.
        /// </remarks>
        private async Task AddToMyCompanyAsync(CatalogIntegratorListItemDto? item)
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

                var editViewModel = new IntegratorEditViewModel(draft);
                var window = new IntegratorEditView
                {
                    DataContext = editViewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                if (window.ShowDialog() != true)
                {
                    return;
                }

                await _integrators.AddIntegratorAsync(draft);
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
        /// Resolves coordinates for the rows on screen that have none.
        /// </summary>
        /// <remarks>
        /// One page at a time, and only what is missing. The daily Geocoding quota is 1,500
        /// calls against a catalog of thousands, so this is deliberately a button somebody
        /// presses rather than something the import does on its own.
        /// </remarks>
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
                new CatalogIntegratorImportTarget(_catalog),
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

        public string TitleText => LocalizationService.Instance["catalog.IntegratorsTitle"];
        public string ColumnFacilityType => LocalizationService.Instance["catalog.ColumnFacilityType"];
        public string ColumnBeds => LocalizationService.Instance["catalog.ColumnBeds"];
    }
}
