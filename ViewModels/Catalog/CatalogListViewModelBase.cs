using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.Catalog
{
    /// <summary>
    /// Everything the two catalog screens do the same way: search, filter, page.
    /// </summary>
    /// <remarks>
    /// The search runs on the server, which is new for this application — every other grid here
    /// fetches its whole table and filters in memory. With thousands of rows that stops working:
    /// the grid holds one page and the count comes back with it.
    ///
    /// <para>
    /// The properties are written out rather than generated because the class is generic, and a
    /// hand-written <c>SetProperty</c> costs nothing here.
    /// </para>
    /// </remarks>
    public abstract class CatalogListViewModelBase<TItem> : ObservableObject
        where TItem : ICatalogGroupedRow
    {
        protected const int DefaultPageSize = 50;

        /// <summary>
        /// The search in flight, cancelled when a newer one starts.
        /// </summary>
        /// <remarks>
        /// The search box fires on every keystroke with a 250 ms delay on the binding. Without
        /// this, typing a word quickly leaves five requests racing and the grid shows whichever
        /// answered last, which is not necessarily the one matching what is on screen.
        /// </remarks>
        private CancellationTokenSource? _inFlight;

        protected CatalogListViewModelBase()
        {
            Items = new ObservableCollection<TItem>();

            // ⚠️ Held as a field, never built on the fly. A CollectionViewSource created inside
            // a property getter is collected while its view is still attached to the grid, and
            // WPF then dies inside ListCollectionView.PrepareLocalArray — the fault the
            // notification centre hit and wrote down.
            _grouped = new CollectionViewSource { Source = Items };
            _grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ICatalogGroupedRow.GroupLabel)));
            Categories = new ObservableCollection<CatalogCategoryDto>();
            Counties = new ObservableCollection<CountyDto>();
            Suggestions = new ObservableCollection<CatalogSuggestionDto>();

            SearchCommand = new AsyncRelayCommand(() => ReloadAsync(resetPage: true));
            RefreshCommand = new AsyncRelayCommand(() => ReloadAsync(resetPage: false));
            NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => CanGoNextPage);
            PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => CanGoPreviousPage);
            ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);
            UseSuggestionCommand = new AsyncRelayCommand<CatalogSuggestionDto>(UseSuggestionAsync);

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        private readonly CollectionViewSource _grouped;

        public ObservableCollection<TItem> Items { get; }

        /// <summary>
        /// The rows with a header over each group.
        /// </summary>
        /// <remarks>
        /// The grouping only reads correctly because the server returns a page already ordered
        /// by group — see CatalogSortBy.Group. Group a page that arrives in another order and
        /// you get a header every few rows.
        /// </remarks>
        public ICollectionView ItemsView => _grouped.View;

        public ObservableCollection<CatalogCategoryDto> Categories { get; }

        public ObservableCollection<CountyDto> Counties { get; }

        /// <summary>Near-matches offered when the search found nothing.</summary>
        public ObservableCollection<CatalogSuggestionDto> Suggestions { get; }

        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand NextPageCommand { get; }
        public IAsyncRelayCommand PreviousPageCommand { get; }
        public IAsyncRelayCommand ClearFiltersCommand { get; }
        public IAsyncRelayCommand<CatalogSuggestionDto> UseSuggestionCommand { get; }

        // ---- Search state --------------------------------------------------------------

        private string? _searchText;

        /// <summary>
        /// What the user typed. The binding carries <c>Delay=250</c>, which is how this
        /// application debounces everywhere — never a DispatcherTimer.
        /// </summary>
        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _ = ReloadAsync(resetPage: true);
                }
            }
        }

        private CatalogCategoryDto? _selectedCategory;
        public CatalogCategoryDto? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    _ = ReloadAsync(resetPage: true);
                }
            }
        }

        private CountyDto? _selectedCounty;
        public CountyDto? SelectedCounty
        {
            get => _selectedCounty;
            set
            {
                if (SetProperty(ref _selectedCounty, value))
                {
                    _ = ReloadAsync(resetPage: true);
                }
            }
        }

        private bool? _inMyCompanyFilter;

        /// <summary>true shows only ours, false only the rest, null everything.</summary>
        public bool? InMyCompanyFilter
        {
            get => _inMyCompanyFilter;
            set
            {
                if (SetProperty(ref _inMyCompanyFilter, value))
                {
                    _ = ReloadAsync(resetPage: true);
                }
            }
        }

        private bool _includeInactive;
        public bool IncludeInactive
        {
            get => _includeInactive;
            set
            {
                if (SetProperty(ref _includeInactive, value))
                {
                    _ = ReloadAsync(resetPage: true);
                }
            }
        }

        private CatalogSortBy _sortBy = CatalogSortBy.Relevance;
        public CatalogSortBy SortBy
        {
            get => _sortBy;
            set
            {
                if (SetProperty(ref _sortBy, value))
                {
                    _ = ReloadAsync(resetPage: true);
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        // ---- Paging --------------------------------------------------------------------

        private int _pageIndex;
        private int _totalCount;

        public int PageSize { get; } = DefaultPageSize;

        /// <summary>Rows matching the search, not rows on screen.</summary>
        public int TotalCount
        {
            get => _totalCount;
            private set
            {
                if (SetProperty(ref _totalCount, value))
                {
                    RaisePagination();
                }
            }
        }

        public bool CanGoPreviousPage => _pageIndex > 0 && !IsLoading;

        public bool CanGoNextPage => (_pageIndex + 1) * PageSize < TotalCount && !IsLoading;

        public bool HasRows => TotalCount > 0;

        /// <summary>"1–50 of 997", or the empty message when there is nothing.</summary>
        public string RangeLabel
        {
            get
            {
                if (TotalCount == 0)
                {
                    return LocalizationService.Instance["catalog.NoResults"];
                }

                var first = (_pageIndex * PageSize) + 1;
                var last = Math.Min(first + PageSize - 1, TotalCount);

                return string.Format(
                    LocalizationService.Instance["catalog.Range"], first, last, TotalCount);
            }
        }

        // ---- Loading --------------------------------------------------------------------

        /// <summary>Loads the categories and counties the filters are built from.</summary>
        public abstract Task LoadLookupsAsync();

        /// <summary>Asks the server for one page.</summary>
        protected abstract Task<CatalogPageDto<TItem>> FetchAsync(
            CatalogSearchRequestDto request,
            CancellationToken cancellationToken);

        protected CatalogSearchRequestDto BuildRequest()
        {
            return new CatalogSearchRequestDto
            {
                Term = SearchText,
                CategoryId = SelectedCategory?.Id,
                CountyId = SelectedCounty?.Id,
                InMyCompany = InMyCompanyFilter,
                IncludeInactive = IncludeInactive,
                SortBy = SortBy,
                PageNumber = _pageIndex + 1,
                PageSize = PageSize
            };
        }

        public async Task ReloadAsync(bool resetPage)
        {
            if (resetPage)
            {
                _pageIndex = 0;
            }

            // Whatever was asked before no longer matches what is on screen.
            _inFlight?.Cancel();
            var cts = new CancellationTokenSource();
            _inFlight = cts;

            IsLoading = true;
            RaisePagination();

            try
            {
                var page = await FetchAsync(BuildRequest(), cts.Token);

                if (cts.IsCancellationRequested)
                {
                    return;
                }

                Items.Clear();

                foreach (var item in page.Items)
                {
                    item.GroupLabel = GroupLabelFor(item.CategoryId);
                    Items.Add(item);
                }

                Suggestions.Clear();

                foreach (var suggestion in page.Suggestions)
                {
                    Suggestions.Add(suggestion);
                }

                TotalCount = page.TotalCount;
                OnPropertyChanged(nameof(HasSuggestions));
            }
            catch (OperationCanceledException)
            {
                // A newer search replaced this one. Nothing to report.
            }
            catch (Exception exception)
            {
                // UiError rather than MessageBox: when a session expires every call in flight
                // fails at once, and this one stays quiet instead of stacking a second box on
                // top of "your session has ended".
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.LoadFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (ReferenceEquals(_inFlight, cts))
                {
                    IsLoading = false;
                    _inFlight = null;
                }

                RaisePagination();
            }
        }

        public bool HasSuggestions => Suggestions.Count > 0;

        /// <summary>
        /// The group's name in the user's language, from the categories already loaded.
        /// </summary>
        /// <remarks>
        /// The server sends the code and not the name, because it has no idea which language
        /// anybody is reading in. Falling back to the code is better than a blank header: it
        /// still tells you which group you are looking at.
        /// </remarks>
        private string GroupLabelFor(int categoryId)
        {
            foreach (var category in Categories)
            {
                if (category.Id == categoryId)
                {
                    return LocalizationService.Instance.CurrentLanguage == "es"
                        ? category.NameEs
                        : category.NameEn;
                }
            }

            return string.Empty;
        }

        private async Task NextPageAsync()
        {
            if (!CanGoNextPage)
            {
                return;
            }

            _pageIndex++;
            await ReloadAsync(resetPage: false);
        }

        private async Task PreviousPageAsync()
        {
            if (!CanGoPreviousPage)
            {
                return;
            }

            _pageIndex--;
            await ReloadAsync(resetPage: false);
        }

        private async Task ClearFiltersAsync()
        {
            // Set the fields directly: going through the properties would fire a reload each.
            _searchText = null;
            _selectedCategory = null;
            _selectedCounty = null;
            _inMyCompanyFilter = null;
            _includeInactive = false;
            _sortBy = CatalogSortBy.Relevance;

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(SelectedCounty));
            OnPropertyChanged(nameof(InMyCompanyFilter));
            OnPropertyChanged(nameof(IncludeInactive));
            OnPropertyChanged(nameof(SortBy));

            await ReloadAsync(resetPage: true);
        }

        private async Task UseSuggestionAsync(CatalogSuggestionDto? suggestion)
        {
            if (suggestion is null)
            {
                return;
            }

            SearchText = suggestion.Name;
            await Task.CompletedTask;
        }

        protected void RaisePagination()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(RangeLabel));
            OnPropertyChanged(nameof(CanGoNextPage));
            OnPropertyChanged(nameof(CanGoPreviousPage));

            NextPageCommand.NotifyCanExecuteChanged();
            PreviousPageCommand.NotifyCanExecuteChanged();
        }

        // ---- Translation -----------------------------------------------------------------

        /// <summary>
        /// ObservableObject does not hook the language change the way BaseViewModel does, so
        /// the texts are re-raised by hand when the user switches language.
        /// </summary>
        private void OnLanguageChanged()
        {
            OnPropertyChanged(string.Empty);
        }

        public string SearchHint => LocalizationService.Instance["catalog.SearchHint"];
        public string AllCategoriesText => LocalizationService.Instance["catalog.AllGroups"];
        public string AllCountiesText => LocalizationService.Instance["catalog.AllCounties"];
        public string InMyCompanyText => LocalizationService.Instance["catalog.InMyCompany"];
        public string NotInMyCompanyText => LocalizationService.Instance["catalog.NotInMyCompany"];
        public string IncludeInactiveText => LocalizationService.Instance["catalog.IncludeInactive"];
        public string ClearFiltersText => LocalizationService.Instance["catalog.ClearFilters"];
        public string AddText => LocalizationService.Instance["catalog.Add"];
        public string EditText => LocalizationService.Instance["catalog.Edit"];
        public string ImportText => LocalizationService.Instance["catalog.Import"];
        public string ImportShortText => LocalizationService.Instance["catalog.ImportShort"];
        public string ExportText => LocalizationService.Instance["catalog.Export"];
        public string ExportShortText => LocalizationService.Instance["catalog.ExportShort"];
        public string GeocodeText => LocalizationService.Instance["catalog.Geocode"];
        public string AddToMyCompanyText => LocalizationService.Instance["catalog.AddToMyCompany"];
        public string AlreadyInMyCompanyText => LocalizationService.Instance["catalog.AlreadyInMyCompany"];
        public string UncontractText => LocalizationService.Instance["catalog.Uncontract"];
        public string UncontractedText => LocalizationService.Instance["catalog.Uncontracted"];
        public string PreviousText => LocalizationService.Instance["catalog.Previous"];
        public string NextText => LocalizationService.Instance["catalog.Next"];
        public string DidYouMeanText => LocalizationService.Instance["catalog.DidYouMean"];

        public string ColumnName => LocalizationService.Instance["catalog.ColumnName"];
        public string ColumnGroup => LocalizationService.Instance["catalog.ColumnGroup"];
        public string ColumnCity => LocalizationService.Instance["catalog.ColumnCity"];
        public string ColumnCounty => LocalizationService.Instance["catalog.ColumnCounty"];
        public string ColumnZip => LocalizationService.Instance["catalog.ColumnZip"];
        public string ColumnPhone => LocalizationService.Instance["catalog.ColumnPhone"];
        public string ColumnEmail => LocalizationService.Instance["catalog.ColumnEmail"];
        public string ColumnContact => LocalizationService.Instance["catalog.ColumnContact"];
        public string ColumnInMyCompany => LocalizationService.Instance["catalog.ColumnInMyCompany"];
        public string ColumnWebsite => LocalizationService.Instance["catalog.LabelWebsite"];
        public string ColumnAddress => LocalizationService.Instance["catalog.LabelAddress"];
        public string ColumnState => LocalizationService.Instance["catalog.LabelState"];
        public string ColumnComments => LocalizationService.Instance["catalog.LabelComments"];
        public string ColumnChain => LocalizationService.Instance["catalog.LabelChain"];
        public string ColumnLatitude => LocalizationService.Instance["catalog.ColumnLatitude"];
        public string ColumnLongitude => LocalizationService.Instance["catalog.ColumnLongitude"];
        public string ColumnActive => LocalizationService.Instance["catalog.LabelIsActive"];
        public string ColumnProviderContact => LocalizationService.Instance["catalog.LabelProviderContact"];
        public string ColumnEmsLicense => LocalizationService.Instance["catalog.LabelEmsLicense"];
        public string ColumnLicenseExpires => LocalizationService.Instance["catalog.LabelLicenseExpires"];
        public string ColumnPrimaryNemt => LocalizationService.Instance["catalog.ColumnPrimaryNemt"];
        public string ColumnPlanSegment => LocalizationService.Instance["catalog.LabelPlanSegment"];
        public string ColumnCoverageArea => LocalizationService.Instance["catalog.LabelCoverageArea"];
        public string ColumnEvidence => LocalizationService.Instance["catalog.LabelEvidence"];
        public string ColumnSourceUpdated => LocalizationService.Instance["catalog.ColumnSourceUpdated"];

        // Tooltips that say what a control is for. Three of these exist because nothing on the
        // screen explained them and the user had to ask.
        public string InMyCompanyHint => LocalizationService.Instance["catalog.InMyCompanyHint"];
        public string IncludeInactiveHint => LocalizationService.Instance["catalog.IncludeInactiveHint"];
        public string GeocodeHint => LocalizationService.Instance["catalog.GeocodeHint"];
    }
}
