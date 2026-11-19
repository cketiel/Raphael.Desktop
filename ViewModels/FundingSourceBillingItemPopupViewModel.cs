using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// The form for one billing rate (RE-072).
    /// </summary>
    /// <remarks>
    /// It checks what it can before calling, so an obvious mistake costs no request; the API checks
    /// everything again and has the last word (overlaps with other rates, history).
    ///
    /// ⚠️ <b>A rate that has priced trips is history.</b> Its amounts and its start cannot be
    /// rewritten, or the trips it billed would change price after the fact. On such a rate the form
    /// still lets the user type the new values, but saving creates a <b>new period</b> from a chosen
    /// date and closes the current one the day before. Only the end date and the procedure code are
    /// edited in place.
    /// </remarks>
    public partial class FundingSourceBillingItemPopupViewModel : ObservableObject
    {
        private static string L(string key) => LocalizationService.Instance[key];

        private readonly IFundingSourceBillingItemService _fsbiService;
        private readonly IBillingItemService _billingItemService;
        private readonly ISpaceTypeService _spaceTypeService;
        private readonly IIntegratorService _integratorService;
        private readonly FundingSourceBillingItem _original;
        private readonly Dictionary<int, (int Kind, string Unit)> _itemInfo = new();

        public FundingSourceBillingItemPopupViewModel(
            IFundingSourceBillingItemService fsbiService,
            IBillingItemService billingItemService,
            ISpaceTypeService spaceTypeService,
            IIntegratorService integratorService,
            FundingSourceBillingItem itemToEdit,
            DateTime? lastUsedOn = null)
        {
            _fsbiService = fsbiService;
            _billingItemService = billingItemService;
            _spaceTypeService = spaceTypeService;
            _integratorService = integratorService;
            _original = itemToEdit;
            LastUsedOn = lastUsedOn?.Date;

            _billingItemId = itemToEdit.BillingItemId;
            _spaceTypeId = itemToEdit.SpaceTypeId;
            _rate = Text(itemToEdit.Rate);
            _per = itemToEdit.Per ?? "";
            _minCharge = Text(itemToEdit.MinCharge);
            _maxCharge = Text(itemToEdit.MaxCharge);
            _procedureCode = itemToEdit.ProcedureCode ?? "";
            _minQty = Text(itemToEdit.GreaterThanMinQty);
            _maxQty = Text(itemToEdit.LessOrEqualMaxQty);
            _freeQty = Text(itemToEdit.FreeQty);
            _fromDate = itemToEdit.FromDate == default ? DateTime.Today : itemToEdit.FromDate.Date;
            _toDate = itemToEdit.ToDate == default ? DateTime.Today.AddYears(1) : itemToEdit.ToDate.Date;
            _isDefault = itemToEdit.IsDefault;
            _newPeriodFrom = Max(DateTime.Today, (LastUsedOn ?? DateTime.Today).AddDays(1));

            _ = LoadDependenciesAsync();
        }

        public event Action<bool>? RequestClose;

        public ObservableCollection<BillingItem> AvailableBillingItems { get; } = new();
        public ObservableCollection<SpaceType> AvailableSpaceTypes { get; } = new();

        public bool IsNewItem => _original.Id == 0;
        public DateTime? LastUsedOn { get; }

        /// <summary>The rate has priced trips: saving new amounts makes a new period.</summary>
        public bool IsHistory => LastUsedOn != null;
        public bool CanEditCore => !IsHistory || IsNewItem;

        #region Texts
        public string TextTitle => IsNewItem ? L("rates.FormNew") : L("rates.FormEdit");
        public string TextItem => L("rates.FormItem");
        public string TextSpaceType => L("rates.FormSpaceType");
        public string TextRate => L("rates.FormRate");
        public string TextPer => L("rates.FormPer");
        public string TextMinCharge => L("rates.FormMinCharge");
        public string TextMaxCharge => L("rates.FormMaxCharge");
        public string TextProcedureCode => L("rates.FormProcedureCode");
        public string TextFreeQty => L("rates.FormFreeQty");
        public string TextMinQty => L("rates.FormMinQty");
        public string TextMaxQty => L("rates.FormMaxQty");
        public string TextQtyHint => L("rates.FormQtyHint");
        public string TextFrom => L("rates.FormFrom");
        public string TextTo => L("rates.FormTo");
        public string TextIsDefault => L("rates.FormIsDefault");
        public string TextIsDefaultHint => L("rates.FormIsDefaultHint");
        public string TextSave => L("Save");
        public string TextCancel => L("Cancel");
        public string TextGroupPrice => L("rates.FormGroupPrice");
        public string TextGroupQty => L("rates.FormGroupQty");
        public string TextGroupPeriod => L("rates.FormGroupPeriod");
        public string TextNewPeriodFrom => L("rates.FormNewPeriodFrom");
        public string TextHistoryBanner => IsHistory
            ? string.Format(L("rates.FormHistoryBanner"), LastUsedOn!.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture))
            : "";
        #endregion

        [ObservableProperty] private int _billingItemId;
        [ObservableProperty] private int _spaceTypeId;
        [ObservableProperty] private string _rate;
        [ObservableProperty] private string _per;
        [ObservableProperty] private string _minCharge;
        [ObservableProperty] private string _maxCharge;
        [ObservableProperty] private string _procedureCode;
        [ObservableProperty] private string _minQty;
        [ObservableProperty] private string _maxQty;
        [ObservableProperty] private string _freeQty;
        [ObservableProperty] private DateTime _fromDate;
        [ObservableProperty] private DateTime _toDate;
        [ObservableProperty] private bool _isDefault;
        [ObservableProperty] private DateTime _newPeriodFrom;
        [ObservableProperty] private string _errors = "";

        /// <summary>Free Qty and &gt; Min Qty mean the same edge: filling one locks the other.</summary>
        public bool IsFreeQtyEnabled => string.IsNullOrWhiteSpace(MinQty);
        public bool IsMinQtyEnabled => string.IsNullOrWhiteSpace(FreeQty);

        partial void OnMinQtyChanged(string value) { OnPropertyChanged(nameof(IsFreeQtyEnabled)); Errors = ""; }
        partial void OnFreeQtyChanged(string value) { OnPropertyChanged(nameof(IsMinQtyEnabled)); Errors = ""; }
        partial void OnRateChanged(string value) => Errors = "";
        partial void OnBillingItemIdChanged(int value) => Errors = "";

        /// <summary>True while the new values only touch what history allows: the end date and the procedure code.</summary>
        public bool WillCreateNewPeriod => IsHistory && !IsNewItem && AmountsChanged();

        private bool AmountsChanged() =>
            BillingItemId != _original.BillingItemId
            || SpaceTypeId != _original.SpaceTypeId
            || Dec(Rate) != _original.Rate
            || (Per ?? "").Trim() != (_original.Per ?? "").Trim()
            || NDec(MinCharge) != _original.MinCharge
            || NDec(MaxCharge) != _original.MaxCharge
            || NInt(MinQty) != _original.GreaterThanMinQty
            || NInt(MaxQty) != _original.LessOrEqualMaxQty
            || NInt(FreeQty) != _original.FreeQty
            || IsDefault != _original.IsDefault
            || FromDate.Date != _original.FromDate.Date;

        private async Task LoadDependenciesAsync()
        {
            try
            {
                var items = await _billingItemService.GetBillingItemsAsync();

                // The office's items, plus those of the clinics that use this funding source (RE-029).
                var clinics = await ClinicsOnThisFundingSourceAsync();

                AvailableBillingItems.Clear();
                foreach (var dto in items
                    .Where(d => d.OwnerIntegratorId == null || clinics.Contains(d.OwnerIntegratorId.Value) || d.Id == BillingItemId)
                    .OrderBy(d => d.OwnerIntegratorId == null ? 0 : 1)
                    .ThenBy(d => d.SortOrder)
                    .ThenBy(d => d.Description))
                {
                    _itemInfo[dto.Id] = (dto.Kind, (dto.UnitAbbreviation ?? "").Trim().ToUpperInvariant());
                    AvailableBillingItems.Add(new BillingItem
                    {
                        Id = dto.Id,
                        Description = dto.Description,
                        UnitId = dto.UnitId,
                        OwnerIntegratorId = dto.OwnerIntegratorId,
                        OwnerIntegratorName = dto.OwnerIntegratorName,
                    });
                }

                var spaceTypes = await _spaceTypeService.GetSpaceTypesAsync();
                AvailableSpaceTypes.Clear();
                foreach (var type in spaceTypes) AvailableSpaceTypes.Add(type);

                // The combos were filled after the values were set: re-announce them so they select.
                OnPropertyChanged(nameof(BillingItemId));
                OnPropertyChanged(nameof(SpaceTypeId));
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(string.Format(L("rates.LoadError"), ex.Message), TextTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<HashSet<int>> ClinicsOnThisFundingSourceAsync()
        {
            try
            {
                return (await _integratorService.GetIntegratorsAsync())
                    .Where(i => i.FundingSourceId == _original.FundingSourceId)
                    .Select(i => i.Id)
                    .ToHashSet();
            }
            catch (Exception)
            {
                return new HashSet<int>();
            }
        }

        /// <summary>The checks that need nothing from the server. The API repeats them all.</summary>
        private List<string> Check()
        {
            var problems = new List<string>();

            if (BillingItemId <= 0) problems.Add(L("rates.ErrItem"));
            if (SpaceTypeId <= 0) problems.Add(L("rates.ErrSpaceType"));

            if (!TryDec(Rate, out var rate) || rate < 0) problems.Add(L("rates.ErrRate"));
            if (!OptionalDec(MinCharge, out var minCharge) || !OptionalDec(MaxCharge, out var maxCharge)) problems.Add(L("rates.ErrCharge"));
            else if (minCharge is decimal a && maxCharge is decimal b && a > b) problems.Add(L("rates.ErrMinAboveMax"));

            if (!string.IsNullOrWhiteSpace(Per) && (!TryDec(Per, out var per) || per <= 0)) problems.Add(L("rates.ErrPer"));

            if (!OptionalInt(FreeQty, out var free) || !OptionalInt(MinQty, out var min) || !OptionalInt(MaxQty, out var max))
                problems.Add(L("rates.ErrQty"));
            else
            {
                if (free != null && min != null) problems.Add(L("rates.ErrFreeAndMin"));
                var lower = free ?? min ?? 0;
                if (max != null && max <= lower) problems.Add(L("rates.ErrMaxQty"));
            }

            if (ToDate.Date < FromDate.Date) problems.Add(L("rates.ErrDates"));

            if (_itemInfo.TryGetValue(BillingItemId, out var info))
            {
                if (info.Unit == "PERCENT" && (rate < 0 || rate > 100)) problems.Add(L("rates.ErrPercent"));
                if (info.Kind == 3 && info.Unit != "PERCENT") problems.Add(L("rates.ErrCancelPercent"));
                if (info.Kind == 3 && IsDefault) problems.Add(L("rates.ErrCancelDefault"));
            }

            if (WillCreateNewPeriod && LastUsedOn is DateTime last && NewPeriodFrom.Date <= last)
                problems.Add(string.Format(L("rates.ErrNewPeriodStart"), last.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)));

            if (IsHistory && !IsNewItem && !AmountsChanged() && LastUsedOn is DateTime used && ToDate.Date < used)
                problems.Add(string.Format(L("rates.ErrEndBeforeUse"), used.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)));

            return problems;
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            var problems = Check();
            if (problems.Count > 0)
            {
                Errors = "• " + string.Join(Environment.NewLine + "• ", problems);
                return;
            }

            var dto = new FundingSourceBillingItemDto
            {
                Id = _original.Id,
                FundingSourceId = _original.FundingSourceId,
                BillingItemId = BillingItemId,
                SpaceTypeId = SpaceTypeId,
                Rate = Dec(Rate),
                Per = string.IsNullOrWhiteSpace(Per) ? null : Per.Trim(),
                IsDefault = IsDefault,
                ProcedureCode = string.IsNullOrWhiteSpace(ProcedureCode) ? null : ProcedureCode.Trim(),
                MinCharge = NDec(MinCharge),
                MaxCharge = NDec(MaxCharge),
                GreaterThanMinQty = NInt(MinQty),
                LessOrEqualMaxQty = NInt(MaxQty),
                FreeQty = NInt(FreeQty),
                FromDate = FromDate.Date,
                ToDate = ToDate.Date,
            };

            try
            {
                if (IsNewItem)
                {
                    await _fsbiService.CreateAsync(dto);
                }
                else if (WillCreateNewPeriod)
                {
                    var question = string.Format(L("rates.ConfirmNewPeriod"),
                        NewPeriodFrom.AddDays(-1).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                        NewPeriodFrom.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
                    if (MessageBox.Show(question, TextTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;

                    // The new period keeps the end the user chose; it starts on NewPeriodFrom.
                    dto.FromDate = NewPeriodFrom.Date;
                    if (dto.ToDate < dto.FromDate) dto.ToDate = _original.ToDate.Date;
                    await _fsbiService.NewPeriodAsync(_original.Id, dto);
                }
                else
                {
                    await _fsbiService.UpdateAsync(_original.Id, dto);
                }

                RequestClose?.Invoke(true);
            }
            catch (BillingRuleApiException ex)
            {
                Errors = ex.Message;
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, TextTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void Cancel() => RequestClose?.Invoke(false);

        // ------------------------------------------------------------------ parsing

        private static readonly CultureInfo[] Cultures = { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture };

        private static bool TryDec(string? text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var c in Cultures)
                if (decimal.TryParse(text.Trim(), NumberStyles.Number, c, out value)) return true;
            return false;
        }

        private static bool OptionalDec(string? text, out decimal? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!TryDec(text, out var v) || v < 0) return false;
            value = v;
            return true;
        }

        private static bool OptionalInt(string? text, out int? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < 0) return false;
            value = v;
            return true;
        }

        private static decimal Dec(string? text) => TryDec(text, out var v) ? v : 0m;
        private static decimal? NDec(string? text) => OptionalDec(text, out var v) ? v : null;
        private static int? NInt(string? text) => OptionalInt(text, out var v) ? v : null;
        private static string Text(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        private static string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    }
}
