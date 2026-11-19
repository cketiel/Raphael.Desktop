using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Admin.Billing;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// Admin → Billing → Funding Sources: the funding sources on the left, the selected one's rates
    /// on the right, one card per space type (RE-072).
    /// </summary>
    /// <remarks>
    /// The rates of a funding source are read once, expired ones included, and "Show history"
    /// filters them on screen: ticking a box never goes back to the server. The selection survives
    /// switching tabs, because the view reloads its list every time it is shown and the old screen
    /// lost the funding source the user was working on each time.
    ///
    /// No money is worked out here. The simulator asks the API, which prices with the same formula
    /// as every report (RE-071).
    /// </remarks>
    public partial class FundingSourcesViewModel : ObservableObject
    {
        private static string L(string key) => LocalizationService.Instance[key];

        // The funding source the user was looking at, across reloads of the view.
        private static int? _rememberedFundingSourceId;

        private readonly IFundingSourceService _fundingSourceService;
        private readonly IFundingSourceBillingItemService _fsBillingItemService;
        private readonly IBillingItemService _billingItemService;
        private readonly ISpaceTypeService _spaceTypeService;
        private readonly IIntegratorService _integratorService;

        private List<FundingSource> _allFundingSources = new();
        private List<FundingSourceBillingItemGetDto> _rates = new();
        private Dictionary<int, BillingRatesHealthDto> _health = new();

        #region Texts

        public string TextSearch => L("rates.Search");
        public string TextToggleCard => L("rates.ToggleCard");
        public string TextOnlyActive => L("rates.OnlyActive");
        public string TextShowHistory => L("rates.ShowHistory");
        public string TextExpiringButton => string.Format(L("rates.ExpiringButton"), ExpiringRates.Count);
        public string TextEditFundingSource => L("rates.EditFundingSource");
        public string TextAddFundingSource => L("AddFundingSource");
        public string TextCopyRates => L("rates.CopyFrom");
        public string TextAddRate => L("rates.AddRate");
        public string TextNoRatesTitle => L("rates.NoRatesTitle");
        public string TextNoRatesBody => L("rates.NoRatesBody");
        public string TextPickFundingSource => L("rates.PickFundingSource");
        public string TextColItem => L("rates.ColItem");
        public string TextColRate => L("rates.ColRate");
        public string TextColConditions => L("rates.ColConditions");
        public string TextColPeriod => L("rates.ColPeriod");
        public string TextExpiringTitle => L("rates.ExpiringTitle");
        public string TextExpiringBody => L("rates.ExpiringBody");
        public string TextExpiringEmpty => L("rates.ExpiringEmpty");
        public string TextRenew => L("rates.Renew");
        public string TextBack => L("rates.Back");
        public string TextEdit => L("rates.Edit");
        public string TextRetire => L("rates.Retire");
        public string TextApplyToDay => L("extraDay.Title");
        public string ExcelExportToolTip => L("ExcelExport");

        #endregion

        public ObservableCollection<FundingSourceListItem> FundingSources { get; } = new();
        public ObservableCollection<RateGroupViewModel> Groups { get; } = new();
        public ObservableCollection<BillingRateExpiringDto> ExpiringRates { get; } = new();

        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private bool _onlyActive = true;
        [ObservableProperty] private bool _displayExpired;
        [ObservableProperty] private FundingSourceListItem? _selectedItem;
        [ObservableProperty] private bool _isExpiringPanelOpen;
        [ObservableProperty] private bool _isBusy;

        public FundingSource? SelectedFundingSource => SelectedItem?.FundingSource;

        // Header figures (RE-072 design): what the user looks for first when opening a funding source.
        public int KpiRatesInForce => _rates.Count(r => r.FromDate.Date <= DateTime.Today && r.ToDate.Date >= DateTime.Today);
        public int KpiSpaceTypes => _rates.Select(r => r.SpaceTypeId).Distinct().Count();
        public int KpiEndingSoon => SelectedFundingSource == null ? 0 : ExpiringRates.Count(e => e.FundingSourceId == SelectedFundingSource.Id);
        public int KpiWarnings => SelectedFundingSource != null && _health.TryGetValue(SelectedFundingSource.Id, out var hh) ? hh.Warnings.Count : 0;
        public string TextKpiRates => L("rates.KpiRates");
        public string TextKpiSpaceTypes => L("rates.KpiSpaceTypes");
        public string TextKpiEnding => L("rates.KpiEnding");
        public string TextKpiWarnings => L("rates.KpiWarnings");
        public string TextFundingSources => L("rates.FundingSources");
        public string FundingSourcesCount => FundingSources.Count.ToString();
        public string SelectedInitials => FundingSourceListItem.InitialsOf(SelectedFundingSource?.Name);

        private void RaiseKpis()
        {
            OnPropertyChanged(nameof(KpiRatesInForce));
            OnPropertyChanged(nameof(KpiSpaceTypes));
            OnPropertyChanged(nameof(KpiEndingSoon));
            OnPropertyChanged(nameof(KpiWarnings));
            OnPropertyChanged(nameof(SelectedInitials));
        }
        public bool HasSelection => SelectedItem != null;
        public bool HasNoRates => SelectedItem != null && _rates.Count == 0 && !IsBusy;
        public bool ShowGroups => SelectedItem != null && !IsExpiringPanelOpen;
        public string SelectedSummary => SelectedFundingSource == null ? "" : string.Join("  ·  ",
            new[] { SelectedFundingSource.AccountNumber, SelectedFundingSource.Phone, SelectedFundingSource.Email }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        public FundingSourcesViewModel(
            IFundingSourceService fundingSourceService,
            IFundingSourceBillingItemService fsBillingItemService,
            IBillingItemService billingItemService,
            ISpaceTypeService spaceTypeService,
            IIntegratorService integratorService)
        {
            _fundingSourceService = fundingSourceService;
            _fsBillingItemService = fsBillingItemService;
            _billingItemService = billingItemService;
            _spaceTypeService = spaceTypeService;
            _integratorService = integratorService;
        }

        // ------------------------------------------------------------------ live (RE-072)

        private bool _attached;
        private CancellationTokenSource? _liveRefresh;

        /// <summary>
        /// While the screen is on: follows the language setting at once, and re-reads the rates when
        /// anyone (another dispatcher, a clinic in Booking) changes them. Called by the view on
        /// Loaded and Unloaded, so a closed tab is not kept alive by the singletons.
        /// </summary>
        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            BillingBoard.Instance.RatesChanged += OnRatesChangedElsewhere;
            BillingBoard.Instance.EnsureStarted();
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            BillingBoard.Instance.RatesChanged -= OnRatesChangedElsewhere;
        }

        /// <summary>Every text on screen, the cards' included (they are rebuilt from the data already read).</summary>
        private void OnLanguageChanged()
        {
            OnPropertyChanged(string.Empty);
            ApplyListFilter();
            BuildGroups();
        }

        /// <summary>
        /// Someone changed rates. A copy sends one message per rate, so they are gathered for a
        /// moment and read once. Nothing is asked of the server when it is another funding source,
        /// except the warnings and "ending soon", which cover all of them.
        /// </summary>
        private void OnRatesChangedElsewhere(DTOs.BillingRatesChangedMessage? message)
        {
            _liveRefresh?.Cancel();
            var cts = _liveRefresh = new CancellationTokenSource();
            _ = LiveRefreshAsync(cts.Token);
        }

        private async Task LiveRefreshAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(600, ct);
                await RefreshAfterChangeAsync(quiet: true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        partial void OnSearchTextChanged(string value) => ApplyListFilter();
        partial void OnOnlyActiveChanged(bool value) => ApplyListFilter();
        partial void OnDisplayExpiredChanged(bool value) => BuildGroups();

        partial void OnSelectedItemChanged(FundingSourceListItem? value)
        {
            if (value != null) _rememberedFundingSourceId = value.FundingSource.Id;
            IsExpiringPanelOpen = false;
            OnPropertyChanged(nameof(SelectedFundingSource));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedSummary));
            OnPropertyChanged(nameof(ShowGroups));
            _ = LoadRatesAsync();
        }

        partial void OnIsExpiringPanelOpenChanged(bool value) => OnPropertyChanged(nameof(ShowGroups));

        /// <summary>Loads the list, the warnings and the expiring rates, and puts the selection back.</summary>
        [RelayCommand]
        public async Task LoadAsync()
        {
            try
            {
                var keep = SelectedItem?.FundingSource.Id ?? _rememberedFundingSourceId;

                var sources = _fundingSourceService.GetFundingSourcesAsync(true);
                var health = _fsBillingItemService.GetHealthAsync();
                var expiring = _fsBillingItemService.GetExpiringAsync(6);
                await Task.WhenAll(sources, health, expiring);

                _allFundingSources = sources.Result.OrderBy(f => f.Name).ToList();
                _health = health.Result.ToDictionary(h => h.FundingSourceId);
                ExpiringRates.Clear();
                foreach (var e in expiring.Result) ExpiringRates.Add(e);
                OnPropertyChanged(nameof(TextExpiringButton));

                ApplyListFilter(keep);
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(string.Format(L("rates.LoadError"), ex.Message), L("rates.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyListFilter(int? keepId = null)
        {
            keepId ??= SelectedItem?.FundingSource.Id;
            var text = (SearchText ?? "").Trim();

            var shown = _allFundingSources
                .Where(f => !OnlyActive || f.IsActive)
                .Where(f => text.Length == 0 || (f.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(f => _health.TryGetValue(f.Id, out var h)
                    ? new FundingSourceListItem(f, h.Warnings, h.RulesInForce)
                    : new FundingSourceListItem(f, new List<string>(), 0))
                .ToList();

            FundingSources.Clear();
            foreach (var item in shown) FundingSources.Add(item);
            OnPropertyChanged(nameof(FundingSourcesCount));

            var again = keepId == null ? null : FundingSources.FirstOrDefault(i => i.FundingSource.Id == keepId);
            if (again != null && (SelectedItem == null || SelectedItem.FundingSource.Id != again.FundingSource.Id || !ReferenceEquals(SelectedItem, again)))
            {
                // Same funding source, new list item: keep the rates on screen, only re-point the selection.
                _selectedItem = again;
                OnPropertyChanged(nameof(SelectedItem));
                if (Groups.Count == 0 && _rates.Count == 0) OnSelectedItemChanged(again);
            }
        }

        private async Task LoadRatesAsync()
        {
            var fs = SelectedFundingSource;
            if (fs == null)
            {
                _rates = new();
                BuildGroups();
                return;
            }

            IsBusy = true;
            try
            {
                // Expired ones too: "Show history" filters on screen instead of asking again.
                _rates = await _fsBillingItemService.GetByFundingSourceIdAsync(fs.Id, includeExpired: true);
                if (SelectedFundingSource?.Id != fs.Id) return;
                BuildGroups();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(string.Format(L("rates.LoadError"), ex.Message), L("rates.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasNoRates));
            }
        }

        private void BuildGroups()
        {
            Groups.Clear();
            var fs = SelectedFundingSource;
            if (fs == null) { OnPropertyChanged(nameof(HasNoRates)); return; }

            var today = DateTime.Today;
            var warnings = _health.TryGetValue(fs.Id, out var h) ? h.Warnings : new List<string>();

            foreach (var group in _rates.GroupBy(r => new { r.SpaceTypeId, r.SpaceTypeName }).OrderBy(g => g.Key.SpaceTypeName))
            {
                var rows = group
                    .Select(r => new RateRowViewModel(r, today))
                    .Where(r => DisplayExpired || !r.IsExpired)
                    .OrderBy(r => r.KindOrder).ThenBy(r => r.Dto.BillingItemSortOrder).ThenBy(r => r.Description).ThenBy(r => r.Dto.FromDate)
                    .ToList();

                var name = group.Key.SpaceTypeName ?? "";
                var groupWarnings = warnings.Where(w => w.StartsWith(name + " ", StringComparison.Ordinal)).ToList();

                Groups.Add(new RateGroupViewModel(_fsBillingItemService, fs.Id, group.Key.SpaceTypeId, name, rows, groupWarnings));
            }

            OnPropertyChanged(nameof(HasNoRates));
            RaiseKpis();
        }

        /// <summary>After any change: the rates, the warnings and the expiring list, never the whole screen.</summary>
        private async Task RefreshAfterChangeAsync(bool quiet = false)
        {
            try
            {
                var health = _fsBillingItemService.GetHealthAsync();
                var expiring = _fsBillingItemService.GetExpiringAsync(6);
                await Task.WhenAll(LoadRatesAsync(), health, expiring);

                _health = health.Result.ToDictionary(x => x.FundingSourceId);
                ExpiringRates.Clear();
                foreach (var e in expiring.Result) ExpiringRates.Add(e);
                OnPropertyChanged(nameof(TextExpiringButton));

                foreach (var item in FundingSources)
                    item.SetWarnings(_health.TryGetValue(item.FundingSource.Id, out var x) ? x.Warnings : new List<string>(),
                        _health.TryGetValue(item.FundingSource.Id, out var y) ? y.RulesInForce : 0);
                BuildGroups();
            }
            catch (Exception ex)
            {
                // A live refresh that fails says nothing: the screen keeps what it had, and the next
                // change or visit reads it again.
                if (quiet) { Helpers.FileLogger.Log($"Rates live refresh failed: {ex.Message}"); return; }
                Helpers.UiError.Show(string.Format(L("rates.LoadError"), ex.Message), L("rates.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------ rates

        [RelayCommand]
        private async Task AddRateAsync(RateGroupViewModel? group)
        {
            var fs = SelectedFundingSource;
            if (fs == null) return;

            var item = new FundingSourceBillingItem
            {
                FundingSourceId = fs.Id,
                SpaceTypeId = group?.SpaceTypeId ?? 0,
                FromDate = DateTime.Today,
                ToDate = DateTime.Today.AddYears(1),
            };

            if (OpenRatePopup(item, null)) await RefreshAfterChangeAsync();
        }

        [RelayCommand]
        private async Task EditRateAsync(RateRowViewModel? row)
        {
            if (row == null) return;
            if (OpenRatePopup(row.ToModel(), row.Dto)) await RefreshAfterChangeAsync();
        }

        /// <summary>
        /// Retires or deletes a rate, after saying which: a rate that priced trips is retired (it stays
        /// as history and stops applying), one that never applied is deleted.
        /// </summary>
        [RelayCommand]
        private async Task RemoveRateAsync(RateRowViewModel? row)
        {
            if (row == null) return;

            var question = row.IsUsed
                ? string.Format(L("rates.ConfirmRetire"), row.Description, row.Dto.SpaceTypeName, row.Dto.LastUsedOn!.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture))
                : string.Format(L("rates.ConfirmDelete"), row.Description, row.Dto.SpaceTypeName);

            if (MessageBox.Show(question, row.IsUsed ? L("rates.RetireTitle") : L("rates.DeleteTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                var outcome = await _fsBillingItemService.RemoveAsync(row.Dto.Id);
                MessageBox.Show(outcome == "retired" ? L("rates.Retired") : L("rates.Deleted"), L("rates.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshAfterChangeAsync();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, L("rates.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Adds an additional rate to every open trip of a day (RE-074): the holiday nobody remembered.</summary>
        [RelayCommand]
        private async Task ApplyToDayAsync(RateRowViewModel? row)
        {
            if (row == null || !row.CanApplyToDay) return;
            var vm = new ApplyExtraDayViewModel(new TripChargesService(), row.Dto.Id, row.Description, row.Dto.SpaceTypeName);
            var window = new ApplyExtraDayWindow(vm) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task RenewAsync(int ruleId)
        {
            var rate = ExpiringRates.FirstOrDefault(e => e.RuleId == ruleId);
            if (rate == null) return;

            var text = string.Format(L("rates.ConfirmRenew"), rate.FundingSourceName.Trim(), rate.SpaceTypeName,
                rate.BillingItemDescription.Trim(), rate.ToDate.AddDays(1).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
            if (MessageBox.Show(text, TextRenew, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                await _fsBillingItemService.RenewAsync(ruleId);
                await RefreshAfterChangeAsync();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, L("rates.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task CopyRatesAsync()
        {
            var fs = SelectedFundingSource;
            if (fs == null) return;

            var vm = new CopyRatesViewModel(_allFundingSources.Where(f => f.Id != fs.Id).OrderBy(f => f.Name).ToList(), fs.Name);
            var window = new CopyRatesWindow(vm) { Owner = Application.Current.MainWindow };
            if (window.ShowDialog() != true || vm.Source == null) return;

            try
            {
                var result = await _fsBillingItemService.CopyAsync(new BillingRatesCopyRequestDto
                {
                    SourceFundingSourceId = vm.Source.Id,
                    TargetFundingSourceId = fs.Id,
                    FromDate = vm.FromDate.Date,
                });

                var message = string.Format(L("rates.CopyDone"), result.Created);
                if (result.Skipped.Count > 0)
                    message += Environment.NewLine + Environment.NewLine + L("rates.CopySkipped") + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", result.Skipped);
                MessageBox.Show(message, TextCopyRates, MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshAfterChangeAsync();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, TextCopyRates, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void ToggleExpiringPanel() => IsExpiringPanelOpen = !IsExpiringPanelOpen;

        [RelayCommand]
        private async Task ExportRatesAsync()
        {
            var rows = Groups.SelectMany(g => g.Rows).Select(r => r.ToModel()).ToList();
            if (rows.Count == 0) return;
            await _fsBillingItemService.ExportToExcelAsync(rows);
        }

        /// <summary>Opens the rate form; true when something was saved.</summary>
        private bool OpenRatePopup(FundingSourceBillingItem item, FundingSourceBillingItemGetDto? existing)
        {
            var vm = new FundingSourceBillingItemPopupViewModel(_fsBillingItemService, _billingItemService, _spaceTypeService, _integratorService, item, existing?.LastUsedOn);
            var window = new FundingSourceBillingItemPopupWindow(vm) { Owner = Application.Current.MainWindow };
            return window.ShowDialog() == true;
        }

        // ------------------------------------------------------------------ funding sources

        [RelayCommand]
        private async Task AddFundingSourceAsync()
        {
            if (OpenFundingSourcePopup(new FundingSource { IsActive = true })) await LoadAsync();
        }

        [RelayCommand]
        private async Task EditFundingSourceAsync()
        {
            if (SelectedFundingSource != null && OpenFundingSourcePopup(SelectedFundingSource)) await LoadAsync();
        }

        [RelayCommand]
        private async Task ExportFundingSourcesAsync()
        {
            try
            {
                await _fundingSourceService.ExportToExcelAsync(FundingSources.Select(i => i.FundingSource).ToList());
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, L("rates.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool OpenFundingSourcePopup(FundingSource fundingSource)
        {
            var popupViewModel = new FundingSourcePopupViewModel(_fundingSourceService, fundingSource);
            var popupWindow = new FundingSourcePopupWindow(popupViewModel);
            return popupWindow.ShowDialog() == true;
        }
    }

    /// <summary>One funding source in the list, with its configuration warnings.</summary>
    public partial class FundingSourceListItem : ObservableObject
    {
        public FundingSourceListItem(FundingSource fundingSource, List<string> warnings, int rulesInForce)
        {
            FundingSource = fundingSource;
            SetWarnings(warnings, rulesInForce);
        }

        public FundingSource FundingSource { get; }
        public string Name => FundingSource.Name?.Trim() ?? "";
        public string Initials => InitialsOf(FundingSource.Name);

        [ObservableProperty] private string _subtitle = "";

        internal static string InitialsOf(string? name)
        {
            var words = (name ?? "").Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).ToList();
            if (words.Count == 0) return "?";
            return (words.Count == 1 ? words[0][..Math.Min(2, words[0].Length)] : $"{words[0][0]}{words[1][0]}").ToUpperInvariant();
        }
        public bool IsInactive => !FundingSource.IsActive;

        [ObservableProperty] private bool _hasWarnings;
        [ObservableProperty] private string _warningText = "";

        public void SetWarnings(List<string> warnings, int rulesInForce = -1)
        {
            HasWarnings = warnings.Count > 0;
            WarningText = string.Join(Environment.NewLine, warnings);
            if (rulesInForce >= 0)
                Subtitle = rulesInForce == 0
                    ? LocalizationService.Instance["rates.NoRatesShort"]
                    : string.Format(LocalizationService.Instance["rates.RatesInForceShort"], rulesInForce);
            if (IsInactive) Subtitle = LocalizationService.Instance["rates.Inactive"] + (Subtitle.Length > 0 ? " · " + Subtitle : "");
        }
    }

    /// <summary>The rates of one space type, with its simulator.</summary>
    public partial class RateGroupViewModel : ObservableObject
    {
        private readonly IFundingSourceBillingItemService _service;
        private readonly int _fundingSourceId;
        private CancellationTokenSource? _pending;

        public RateGroupViewModel(IFundingSourceBillingItemService service, int fundingSourceId, int spaceTypeId,
            string spaceTypeName, List<RateRowViewModel> rows, List<string> warnings)
        {
            _service = service;
            _fundingSourceId = fundingSourceId;
            SpaceTypeId = spaceTypeId;
            SpaceTypeName = spaceTypeName;
            Rows = new ObservableCollection<RateRowViewModel>(rows);
            WarningText = string.Join("  ·  ", warnings);
            _ = SimulateAsync();
        }

        public int SpaceTypeId { get; }
        public string SpaceTypeName { get; }
        public ObservableCollection<RateRowViewModel> Rows { get; }

        // Collapsed cards stay collapsed while the application runs: the cards are rebuilt on every
        // refresh (live updates included) and must not spring open under the user.
        private static readonly HashSet<(int FundingSourceId, int SpaceTypeId)> Collapsed = new();

        public bool IsExpanded
        {
            get => !Collapsed.Contains((_fundingSourceId, SpaceTypeId));
            set
            {
                if (value) Collapsed.Remove((_fundingSourceId, SpaceTypeId));
                else Collapsed.Add((_fundingSourceId, SpaceTypeId));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChevronIcon));
            }
        }

        public string ChevronIcon => IsExpanded ? "ChevronDown" : "ChevronRight";

        /// <summary>Clicking the card's header folds it, to save room (RE-072).</summary>
        [RelayCommand]
        private void ToggleExpanded() => IsExpanded = !IsExpanded;
        public string WarningText { get; }
        public bool HasWarnings => WarningText.Length > 0;
        public string RateCountText => string.Format(LocalizationService.Instance["rates.GroupCount"],
            Rows.Count(r => r.IsActiveNow && r.Dto.IsDefault), Rows.Count(r => r.IsActiveNow && !r.Dto.IsDefault));

        public string TextSimulate => LocalizationService.Instance["rates.Simulate"];
        public string TextMiles => LocalizationService.Instance["rates.Miles"];
        public string TextCancelled => LocalizationService.Instance["rates.SimCancelled"];

        [ObservableProperty] private string _simMiles = "12";
        [ObservableProperty] private string _simTotal = "…";
        [ObservableProperty] private string _simCancelledTotal = "…";
        [ObservableProperty] private string _simDetail = "";

        partial void OnSimMilesChanged(string value) => _ = SimulateAsync();

        /// <summary>
        /// Asks the API what a trip of that many miles costs today, and what it costs cancelled. Waits
        /// for typing to pause so each key press is not a call.
        /// </summary>
        private async Task SimulateAsync()
        {
            _pending?.Cancel();
            var cts = _pending = new CancellationTokenSource();

            if (!double.TryParse(SimMiles, NumberStyles.Float, CultureInfo.CurrentCulture, out var miles)
                && !double.TryParse(SimMiles, NumberStyles.Float, CultureInfo.InvariantCulture, out miles)
                || miles < 0)
            {
                SimTotal = "—";
                SimCancelledTotal = "—";
                SimDetail = "";
                return;
            }

            try
            {
                await Task.Delay(350, cts.Token);

                var request = new BillingSimulationRequestDto
                {
                    FundingSourceId = _fundingSourceId,
                    SpaceTypeId = SpaceTypeId,
                    Date = DateTime.Today,
                    Miles = miles,
                };
                var normal = _service.SimulateAsync(request, cts.Token);
                var cancelled = _service.SimulateAsync(new BillingSimulationRequestDto
                {
                    FundingSourceId = _fundingSourceId,
                    SpaceTypeId = SpaceTypeId,
                    Date = DateTime.Today,
                    Miles = miles,
                    IsCancelled = true,
                }, cts.Token);
                await Task.WhenAll(normal, cancelled);
                if (cts.IsCancellationRequested) return;

                SimTotal = Money(normal.Result.Total);
                SimCancelledTotal = Money(cancelled.Result.Total);
                SimDetail = string.Join("  +  ", normal.Result.Lines.Select(l => $"{l.Description} {Money(l.Amount)}"));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (!cts.IsCancellationRequested) SimTotal = "—";
            }
        }

        internal static string Money(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
    }

    /// <summary>One rate as a line of its space type's card.</summary>
    public class RateRowViewModel
    {
        public RateRowViewModel(FundingSourceBillingItemGetDto dto, DateTime today)
        {
            Dto = dto;
            IsExpired = dto.ToDate.Date < today;
            IsFuture = dto.FromDate.Date > today;
        }

        private static string L(string key) => LocalizationService.Instance[key];

        public FundingSourceBillingItemGetDto Dto { get; }
        public bool IsExpired { get; }
        public bool IsFuture { get; }
        public bool IsUsed => Dto.LastUsedOn != null;
        public bool IsActiveNow => !IsExpired && !IsFuture;
        public bool CanRemove => !IsExpired;

        /// <summary>An additional rate still in force: it can be added to all the trips of a day at once (RE-074).</summary>
        public bool CanApplyToDay => !Dto.IsDefault && Dto.BillingItemKind != 3 && !IsExpired;

        public string Description => Dto.BillingItemDescription?.Trim() ?? "";
        private bool IsPercent => string.Equals(Dto.BillingItemUnitAbbreviation?.Trim(), "PERCENT", StringComparison.OrdinalIgnoreCase);

        /// <summary>Loading Fee, Miles, Cancelation, then the extras: the same order in every card.</summary>
        public int KindOrder => Dto.BillingItemKind switch { 2 => 0, 1 => 1, 3 => 2, _ => 3 };

        /// <summary>The icon of the item's kind (MaterialDesign PackIconKind name).</summary>
        public string KindIcon => Dto.BillingItemKind switch
        {
            1 => "MapMarkerDistance",
            2 => "AccountArrowUpOutline",
            3 => "CancelOctagonOutline",
            _ => "PlusCircleOutline",
        };

        public string StatusText => IsExpired ? L("rates.StatusExpired") : IsFuture ? L("rates.StatusFuture")
            : Dto.IsDefault ? L("rates.StatusDefault") : L("rates.StatusExtra");

        /// <summary>default, extra, expired or future, for the badge colour.</summary>
        public string StatusKind => IsExpired ? "expired" : IsFuture ? "future" : Dto.IsDefault ? "default" : "extra";

        public string RateText => IsPercent
            ? Dto.Rate.ToString("0.00", CultureInfo.InvariantCulture) + " %"
            : RateGroupViewModel.Money(Dto.Rate) + (string.IsNullOrWhiteSpace(Dto.Per) || Dto.Per.Trim() == "1" ? "" : $" / {Dto.Per.Trim()}");

        /// <summary>The rule's fields in plain words, only the ones that are filled in.</summary>
        public string ConditionsText
        {
            get
            {
                var parts = new List<string>();
                var unit = (Dto.BillingItemUnitAbbreviation ?? "").Trim().ToUpperInvariant() switch
                {
                    "MILE" => L("rates.UnitMiles"),
                    "HOUR" => L("rates.UnitHours"),
                    _ => L("rates.UnitUnits"),
                };
                if (IsPercent) parts.Add(Dto.BillingItemKind == 3 && Dto.Rate == 0 ? L("rates.CondNotCharged") : L("rates.CondPercentOfTrip"));
                if (Dto.FreeQty is int free) parts.Add(string.Format(L("rates.CondFree"), free, unit));
                if (Dto.GreaterThanMinQty is int min) parts.Add(string.Format(L("rates.CondAbove"), min, unit));
                if (Dto.LessOrEqualMaxQty is int max) parts.Add(string.Format(L("rates.CondUpTo"), max, unit));
                if (Dto.MinCharge is decimal minC) parts.Add(string.Format(L("rates.CondMinCharge"), RateGroupViewModel.Money(minC)));
                if (Dto.MaxCharge is decimal maxC) parts.Add(string.Format(L("rates.CondMaxCharge"), RateGroupViewModel.Money(maxC)));
                if (!string.IsNullOrWhiteSpace(Dto.ProcedureCode)) parts.Add(Dto.ProcedureCode.Trim());
                return parts.Count == 0 ? "—" : string.Join(" · ", parts);
            }
        }

        public string PeriodText =>
            Dto.FromDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) + " – " + Dto.ToDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

        public string UsedText => IsUsed
            ? string.Format(L("rates.UsedUntil"), Dto.LastUsedOn!.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture))
            : L("rates.NeverUsed");

        public FundingSourceBillingItem ToModel() => new()
        {
            Id = Dto.Id,
            FundingSourceId = Dto.FundingSourceId,
            BillingItemId = Dto.BillingItemId,
            SpaceTypeId = Dto.SpaceTypeId,
            Rate = Dto.Rate,
            Per = Dto.Per,
            IsDefault = Dto.IsDefault,
            ProcedureCode = Dto.ProcedureCode,
            MinCharge = Dto.MinCharge,
            MaxCharge = Dto.MaxCharge,
            GreaterThanMinQty = Dto.GreaterThanMinQty,
            LessOrEqualMaxQty = Dto.LessOrEqualMaxQty,
            FreeQty = Dto.FreeQty,
            FromDate = Dto.FromDate,
            ToDate = Dto.ToDate,
            BillingItem = new BillingItem { Id = Dto.BillingItemId, Description = Description, Unit = new Unit { Abbreviation = Dto.BillingItemUnitAbbreviation } },
            SpaceType = new SpaceType { Id = Dto.SpaceTypeId, Name = Dto.SpaceTypeName },
        };
    }
}
