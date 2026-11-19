using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// The Billing section of the trip form (RE-074): the trip's charge, line by line, its
    /// additional charges, where it came from and its change history.
    /// </summary>
    /// <remarks>
    /// No money is worked out here: the lines come from the server's single formula, either the
    /// trip's stored charge or a preview of the values on the form (POST simulate). Before RE-074
    /// this section read every rate of every funding source and filtered by a funding source id the
    /// API sent wrong, so it showed another funding source's rates or none; and the additional
    /// charges could be seen but never added or saved.
    ///
    /// Only the funding source's non-default rates can be added. On a saved trip an extra is saved
    /// at once; on a new trip it is kept and saved with the trip, on both legs of a round trip.
    /// A finished or cancelled trip's charge is frozen and shown as it was.
    ///
    /// Live: when anyone changes the rates of the trip's funding source, or the trip's stored
    /// charge is rewritten (another dispatcher's extra, a day's holiday fee, the trip ending), the
    /// section reads it again by itself, through <see cref="BillingBoard"/>.
    /// </remarks>
    public partial class HomeViewModel
    {
        private readonly ITripChargesService _tripCharges = new TripChargesService();
        private readonly IFundingSourceBillingItemService _chargeRates = new FundingSourceBillingItemService();
        private readonly TripHistoryService _tripHistory = new TripHistoryService();
        private readonly List<TripChargeExtraDto> _chargeExtras = new();
        private CancellationTokenSource? _chargePreview;
        private int? _chargesTripId;
        private bool _chargesLive;

        // What is on screen, kept so a language change can draw it again without asking the server.
        private ChargeSnapshot _shown = ChargeSnapshot.Empty;

        public ObservableCollection<ChargeLineRow> ChargeLines { get; } = new();
        public ObservableCollection<ChargeOptionRow> AvailableExtras { get; } = new();
        public ObservableCollection<TripHistoryCreateDto> TripHistoryRows { get; } = new();

        private string _chargeTotalText = "$0.00";
        public string ChargeTotalText { get => _chargeTotalText; set { _chargeTotalText = value; OnPropertyChanged(); } }

        private string _chargeWarningText = "";
        public string ChargeWarningText { get => _chargeWarningText; set { _chargeWarningText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChargeWarning)); } }
        public bool HasChargeWarning => !string.IsNullOrEmpty(ChargeWarningText);

        private bool _chargesFrozen;
        /// <summary>The trip finished or was cancelled: its charge is history and cannot change.</summary>
        public bool ChargesFrozen { get => _chargesFrozen; set { _chargesFrozen = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditExtras)); } }
        public bool CanEditExtras => !ChargesFrozen;

        public bool HasChargeLines => ChargeLines.Count > 0;
        public bool HasAvailableExtras => AvailableExtras.Count > 0;

        private string _tripOrigin = "New";
        /// <summary>Manual, Import, Booking, Integration — or New while the trip does not exist.</summary>
        public string TripOrigin { get => _tripOrigin; private set { _tripOrigin = value; OnPropertyChanged(); OnPropertyChanged(nameof(TripOriginText)); OnPropertyChanged(nameof(TripOriginIcon)); } }
        public string TripOriginText => LocalizationService.Instance["charges.Origin" + TripOrigin];
        public string TripOriginIcon => TripOrigin switch
        {
            "Import" => "FileImportOutline",
            "Booking" => "HospitalBuilding",
            "Integration" => "ApiIcon",
            "Manual" => "AccountTieOutline",
            _ => "PlusCircleOutline",
        };

        private int _billingTabIndex;
        /// <summary>0 = Charges, 1 = History. The history is read the first time its tab is opened.</summary>
        public int BillingTabIndex
        {
            get => _billingTabIndex;
            set { _billingTabIndex = value; OnPropertyChanged(); if (value == 1) _ = LoadTripHistoryAsync(); }
        }

        private int? _historyLoadedFor;
        private bool _historyLoading;
        public bool IsHistoryLoading { get => _historyLoading; private set { _historyLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(HistoryEmptyText)); OnPropertyChanged(nameof(HasHistoryEmptyText)); } }
        public string HistoryEmptyText => IsHistoryLoading ? "" :
            _chargesTripId == null ? LocalizationService.Instance["charges.HistoryNew"] :
            TripHistoryRows.Count == 0 ? LocalizationService.Instance["charges.HistoryEmpty"] : "";
        public bool HasHistoryEmptyText => HistoryEmptyText.Length > 0;

        public string TextChargesFrozen => LocalizationService.Instance["charges.Frozen"];
        public string TextAddExtra => LocalizationService.Instance["charges.Add"];
        public string TextRemoveExtra => LocalizationService.Instance["charges.Remove"];
        public string TextChargesTotal => LocalizationService.Instance["charges.Total"];
        public string TextLegendDefault => LocalizationService.Instance["charges.LegendDefault"];
        public string TextLegendExtra => LocalizationService.Instance["charges.LegendExtra"];
        public string TextDefaultLocked => LocalizationService.Instance["charges.DefaultLocked"];
        public string TextChargesApplied => LocalizationService.Instance["charges.Applied"];
        public string TextChargesAvailable => LocalizationService.Instance["charges.Available"];
        public string TextChargesNoLines => LocalizationService.Instance["charges.NoLines"];
        public string TextChargesNoAvailable => LocalizationService.Instance["charges.NoAvailable"];
        public string TextChargesOrigin => LocalizationService.Instance["charges.Origin"];
        public string TextHistoryDate => LocalizationService.Instance["charges.ColDate"];
        public string TextHistoryUser => LocalizationService.Instance["charges.ColUser"];
        public string TextHistoryField => LocalizationService.Instance["charges.ColField"];
        public string TextHistoryPrior => LocalizationService.Instance["charges.ColPrior"];
        public string TextHistoryNew => LocalizationService.Instance["charges.ColNew"];

        partial void OnTripDateChanged(DateTime value) => ScheduleChargePreview();

        // ------------------------------------------------------------------ live

        private void EnsureChargesLive()
        {
            if (_chargesLive) return;
            _chargesLive = true;
            BillingBoard.Instance.RatesChanged += OnRatesChangedElsewhere;
            BillingBoard.Instance.TripChargesChanged += OnTripChargesChangedElsewhere;
            BillingBoard.Instance.EnsureStarted();
        }


        private void OnRatesChangedElsewhere(BillingRatesChangedMessage? message)
        {
            if (!IsTripFormOpen) return;
            if (message != null && message.FundingSourceId != SelectedFundingSource?.Id) return;
            if (message?.SpaceTypeId is int st && st != SelectedSpaceType?.Id) return;

            if (_chargesTripId is int id) _ = LoadTripChargesAsync(id);
            else ScheduleChargePreview();
        }

        private void OnTripChargesChangedElsewhere(TripChargesChangedMessage? message)
        {
            if (!IsTripFormOpen || _chargesTripId is not int id) return;
            if (message != null && !message.TripIds.Contains(id)) return;

            _historyLoadedFor = null;          // its history may have a new row too
            _ = LoadTripChargesAsync(id);
            if (BillingTabIndex == 1) _ = LoadTripHistoryAsync();
        }

        /// <summary>Called on a language change: the lines are drawn again from what was last read.</summary>
        private void RefreshChargeTexts()
        {
            ShowCharges(_shown);
            OnPropertyChanged(nameof(TripOriginText));
            OnPropertyChanged(nameof(HistoryEmptyText));
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Reads a saved trip's charge: its extras, whether it is frozen, its origin and its lines.</summary>
        private async Task LoadTripChargesAsync(int tripId)
        {
            EnsureChargesLive();
            _chargesTripId = tripId;
            try
            {
                var charges = await _tripCharges.GetAsync(tripId);
                if (charges == null || _chargesTripId != tripId) return;

                _chargeExtras.Clear();
                _chargeExtras.AddRange(charges.Extras);
                ChargesFrozen = charges.IsFrozen;
                TripOrigin = string.IsNullOrWhiteSpace(charges.Origin) ? "Manual" : charges.Origin;

                if (charges.IsFrozen)
                    ShowCharges(new ChargeSnapshot(charges.Total, charges.Lines.Select(ChargeLineRow.From).ToList(),
                        charges.Available, charges.Warnings, charges.FreeNotes));
                else
                    ScheduleChargePreview();
            }
            catch (Exception)
            {
                ChargeWarningText = LocalizationService.Instance["charges.LoadError"];
            }
        }

        private void ResetCharges()
        {
            EnsureChargesLive();
            _chargePreview?.Cancel();
            _chargesTripId = null;
            _chargeExtras.Clear();
            ChargesFrozen = false;
            TripOrigin = "New";
            _historyLoadedFor = null;
            TripHistoryRows.Clear();
            BillingTabIndex = 0;
            ShowCharges(ChargeSnapshot.Empty);
            OnPropertyChanged(nameof(HistoryEmptyText));
            OnPropertyChanged(nameof(HasHistoryEmptyText));
        }

        /// <summary>Asks the server for the charge of what the form says, once typing pauses.</summary>
        private void ScheduleChargePreview()
        {
            if (ChargesFrozen) return;
            _chargePreview?.Cancel();
            var cts = _chargePreview = new CancellationTokenSource();
            _ = PreviewChargesAsync(cts.Token);
        }

        private async Task PreviewChargesAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(300, ct);

                var fs = SelectedFundingSource?.Id;
                var st = SelectedSpaceType?.Id;
                if (fs == null || st == null)
                {
                    ShowCharges(ChargeSnapshot.Empty);
                    return;
                }

                double.TryParse(Distance?.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var miles);

                var result = await _chargeRates.SimulateAsync(new BillingSimulationRequestDto
                {
                    FundingSourceId = fs.Value,
                    SpaceTypeId = st.Value,
                    Date = TripDate.Date,
                    Miles = Math.Max(0, miles),
                    Extras = _chargeExtras.ToList(),
                }, ct);
                if (ct.IsCancellationRequested) return;

                ShowCharges(new ChargeSnapshot(result.Total, result.Lines.Select(ChargeLineRow.From).ToList(),
                    result.Available, result.Warnings, result.FreeNotes));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (!ct.IsCancellationRequested) ChargeWarningText = LocalizationService.Instance["charges.LoadError"];
            }
        }

        private void ShowCharges(ChargeSnapshot snapshot)
        {
            _shown = snapshot;
            var canEdit = !ChargesFrozen && !IsEditingReadOnlyTrip;

            ChargeLines.Clear();
            foreach (var line in snapshot.Lines)
            {
                line.CanRemove = line.IsExtra && canEdit;
                line.Note = "";
                ChargeLines.Add(line);
            }

            // The free miles, said where the dispatcher looks: on the Miles line, or as a $0 line
            // when they cover the whole trip (a 6-mile trip with 7 free has no Miles line at all,
            // and that looked like an error).
            foreach (var note in snapshot.FreeNotes)
            {
                var text = FreeNoteText(note);
                var line = ChargeLines.FirstOrDefault(l => l.RuleId == note.RuleId);
                if (line != null)
                {
                    line.Note = text;
                    continue;
                }

                var free = ChargeLineRow.FreeOnly(note.RuleId, note.Description);
                free.Note = text;
                var at = ChargeLines.TakeWhile(l => !l.IsExtra).Count();
                ChargeLines.Insert(at, free);
            }

            AvailableExtras.Clear();
            foreach (var option in snapshot.Available.Where(a => _chargeExtras.All(x => x.RuleId != a.RuleId)))
                AvailableExtras.Add(new ChargeOptionRow(option) { CanAdd = canEdit });

            ChargeTotalText = Money(snapshot.Total);
            TotalTripCharge = (double)snapshot.Total;
            ChargeWarningText = string.Join("  ·  ", snapshot.Warnings.Select(WarningText));
            OnPropertyChanged(nameof(HasChargeLines));
            OnPropertyChanged(nameof(HasAvailableExtras));
        }

        private static string FreeNoteText(TripChargeFreeNoteDto n)
        {
            static string Mi(decimal v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            var key = (n.IsFree ? "charges.Free" : "charges.Min") + (n.BilledMiles > 0 ? "Billed" : "All");
            return string.Format(LocalizationService.Instance[key], Mi(n.FreeMiles), Mi(n.TripMiles), Mi(n.BilledMiles));
        }

        // ------------------------------------------------------------------ history

        /// <summary>The trip's change history, the same rows the Change History window shows.</summary>
        private async Task LoadTripHistoryAsync()
        {
            if (_chargesTripId is not int id || _historyLoadedFor == id) return;
            _historyLoadedFor = id;
            IsHistoryLoading = true;
            try
            {
                var rows = await _tripHistory.GetHistoryByTripAsync(id);
                if (_chargesTripId != id) return;
                TripHistoryRows.Clear();
                foreach (var row in rows.OrderByDescending(r => r.ChangeDate)) TripHistoryRows.Add(row);
            }
            catch (Exception ex)
            {
                _historyLoadedFor = null;
                Helpers.FileLogger.Log($"Trip history could not be read: {ex.Message}");
            }
            finally
            {
                IsHistoryLoading = false;
            }
        }

        // ------------------------------------------------------------------ extras

        [RelayCommand]
        private async Task AddExtraAsync(ChargeOptionRow? option)
        {
            if (option == null || ChargesFrozen) return;
            await ChangeExtrasAsync(list => list.Add(new TripChargeExtraDto { RuleId = option.RuleId, Quantity = 1 }));
        }

        [RelayCommand]
        private async Task RemoveExtraAsync(ChargeLineRow? line)
        {
            if (line?.RuleId == null || ChargesFrozen) return;
            await ChangeExtrasAsync(list => list.RemoveAll(x => x.RuleId == line.RuleId));
        }

        /// <summary>A saved trip saves the change at once; a new one keeps it for when it is created.</summary>
        private async Task ChangeExtrasAsync(Action<List<TripChargeExtraDto>> change)
        {
            var next = _chargeExtras.Select(x => new TripChargeExtraDto { RuleId = x.RuleId, Quantity = x.Quantity }).ToList();
            change(next);

            if (_chargesTripId is int tripId)
            {
                try
                {
                    // A past day's trip takes the admin's correction reason, like any other change to it.
                    TripChargesDto? saved;
                    using (CorrectionReason == null ? null : Services.TripLocks.CorrectionScope.Begin(CorrectionReason))
                    {
                        saved = await _tripCharges.SetExtrasAsync(tripId, next);
                    }
                    if (saved == null) return;
                    _chargeExtras.Clear();
                    _chargeExtras.AddRange(saved.Extras);
                    ChargesFrozen = saved.IsFrozen;
                }
                catch (Exception ex)
                {
                    Helpers.UiError.Show(ex.Message, LocalizationService.Instance["charges.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                _chargeExtras.Clear();
                _chargeExtras.AddRange(next);
            }

            ScheduleChargePreview();
        }

        /// <summary>After a new trip is created: its extras, chosen while it did not exist yet.</summary>
        private async Task SaveExtrasForNewTripAsync(int? tripId)
        {
            if (tripId is not int id || id <= 0 || _chargeExtras.Count == 0) return;
            try
            {
                await _tripCharges.SetExtrasAsync(id, _chargeExtras.ToList());
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(string.Format(LocalizationService.Instance["charges.SaveExtrasError"], ex.Message),
                    LocalizationService.Instance["charges.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string WarningText(string code) => code switch
        {
            "MissingCancellationRule" => LocalizationService.Instance["charges.WarnNoCancellationRule"],
            "NoRulesInForce" => LocalizationService.Instance["charges.WarnNoRules"],
            "NoFundingSource" => LocalizationService.Instance["charges.WarnNoFundingSource"],
            "OverlappingRules" => LocalizationService.Instance["charges.WarnOverlap"],
            "ExtraRuleNotInForce" => LocalizationService.Instance["charges.WarnExtraNotInForce"],
            _ => code,
        };

        internal static string Money(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);

        /// <summary>What the section last drew.</summary>
        private sealed record ChargeSnapshot(
            decimal Total,
            List<ChargeLineRow> Lines,
            List<TripChargeOptionDto> Available,
            List<string> Warnings,
            List<TripChargeFreeNoteDto> FreeNotes)
        {
            public static readonly ChargeSnapshot Empty = new(0m, new(), new(), new(), new());
        }
    }

    /// <summary>One line of a trip's charge.</summary>
    public class ChargeLineRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public int? RuleId { get; set; }
        public string Description { get; set; } = "";
        public string RateText { get; set; } = "";
        public string QuantityText { get; set; } = "";
        public string AmountText { get; set; } = "";
        public bool IsExtra { get; set; }
        public bool CanRemove { get; set; }

        /// <summary>A per-mile rate whose free miles cover the whole trip: shown at $0 to say why.</summary>
        public bool IsFreeOnly { get; set; }

        private string _note = "";
        /// <summary>The explanation under the description (free miles), or empty.</summary>
        public string Note { get => _note; set { SetProperty(ref _note, value); OnPropertyChanged(nameof(HasNote)); } }
        public bool HasNote => Note.Length > 0;

        public string KindText => IsExtra ? LocalizationService.Instance["charges.LegendExtra"] : LocalizationService.Instance["charges.LegendDefault"];

        private static bool IsPercent(string unit) => string.Equals(unit?.Trim(), "PERCENT", StringComparison.OrdinalIgnoreCase);

        public static ChargeLineRow From(BillingSimulationLineDto l) => WithCodes(Make(l.RuleId == 0 ? null : l.RuleId, l.Description, l.Unit, l.Rate, l.Quantity, l.Amount, l.IsExtra), l.ProcedureCode, l.Per);
        public static ChargeLineRow From(TripChargeLineDto l) => WithCodes(Make(l.RuleId, l.Description, l.Unit, l.Rate, l.Quantity, l.Amount, l.IsExtra), l.ProcedureCode, l.Per);

        /// <summary>The procedure code and "Per" the old grid showed in their own columns.</summary>
        public string CodesText { get; private set; } = "";
        public bool HasCodes => CodesText.Length > 0;

        private static ChargeLineRow WithCodes(ChargeLineRow row, string? procedureCode, string? per)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(per) && per.Trim() != "1") parts.Add(LocalizationService.Instance["ChargesPerText"] + " " + per.Trim());
            if (!string.IsNullOrWhiteSpace(procedureCode)) parts.Add(procedureCode.Trim());
            row.CodesText = string.Join(" · ", parts);
            return row;
        }

        public static ChargeLineRow FreeOnly(int ruleId, string description) => new()
        {
            RuleId = ruleId,
            Description = description,
            RateText = "",
            QuantityText = "0 mi",
            AmountText = HomeViewModel.Money(0m),
            IsFreeOnly = true,
        };

        private static ChargeLineRow Make(int? ruleId, string description, string unit, decimal rate, decimal quantity, decimal amount, bool isExtra) => new()
        {
            RuleId = ruleId,
            Description = description,
            RateText = IsPercent(unit) ? rate.ToString("0.##", CultureInfo.InvariantCulture) + " %" : HomeViewModel.Money(rate),
            QuantityText = IsPercent(unit) ? "—" : quantity.ToString("0.##", CultureInfo.InvariantCulture) + " " + UnitShort(unit),
            AmountText = HomeViewModel.Money(amount),
            IsExtra = isExtra,
        };

        private static string UnitShort(string? unit) => (unit ?? "").Trim().ToUpperInvariant() switch
        {
            "MILE" => "mi",
            "HOUR" => "h",
            "UNIT" => "u",
            var other => other.ToLowerInvariant(),
        };
    }

    /// <summary>An additional rate that may be added to the trip.</summary>
    public class ChargeOptionRow
    {
        public ChargeOptionRow(TripChargeOptionDto option)
        {
            RuleId = option.RuleId;
            Description = option.Description;
            RateText = string.Equals(option.Unit?.Trim(), "PERCENT", StringComparison.OrdinalIgnoreCase)
                ? option.Rate.ToString("0.##", CultureInfo.InvariantCulture) + " %"
                : HomeViewModel.Money(option.Rate) + " / " + (option.Unit ?? "").Trim().ToLowerInvariant();
        }

        public int RuleId { get; }
        public string Description { get; }
        public string RateText { get; }
        public bool CanAdd { get; set; }
    }
}
