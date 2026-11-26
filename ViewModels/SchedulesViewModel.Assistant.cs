#nullable enable
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Raphael.Desktop.ViewModels
{
    // RE-098/099: the panel of suggestions and the Overflow trips of the day's plan.
    public partial class SchedulesViewModel
    {
        private readonly RoutingAssistantService _planService = new();
        private Dictionary<int, PlannedTripDto> _plan = new();
        private DateTime _planDay;
        private DateTime _planAskedUtc;
        private DispatcherTimer? _planTimer;
        private bool _planning;
        private RoutingSuggestionsViewModel? _suggestions;

        /// <summary>Where the panel asked for the trip being routed to go; null is the button's own rule.</summary>
        private RoutingSuggestionDto? _placement;

        public RoutingSuggestionsViewModel Suggestions => _suggestions ??= CreateSuggestions();

        private RoutingSuggestionsViewModel CreateSuggestions()
        {
            var vm = new RoutingSuggestionsViewModel(
                () => ((SelectedUnscheduledTrip ?? FirstToRoute())?.Id ?? 0, SelectedVehicleRoute?.Id ?? 0, SelectedDate), RouteSuggestionAsync);
            vm.PlanChanged += plan => SuggestedPlanChanged?.Invoke(plan);
            return vm;
        }

        /// <summary>The suggested plan to draw on this screen's map; null to clear it.</summary>
        public event Action<RoutingSuggestionDto?>? SuggestedPlanChanged;

        /// <summary>The trip whose turn it is: the earliest one that has an hour.</summary>
        private UnscheduledTripDto? FirstToRoute() =>
            UnscheduledTrips.Where(t => !t.IsCanceled && !t.IsDeparting && !t.WillCall && t.FromTime is { } h && h < new TimeSpan(23, 59, 0))
                .OrderBy(t => t.IsOverflow && !t.IsOverflowUrgent ? 1 : 0).ThenBy(t => t.FromTime).FirstOrDefault();

        private IRelayCommand<UnscheduledTripDto>? _suggestLinesCommand;
        public IRelayCommand<UnscheduledTripDto> SuggestLinesCommand => _suggestLinesCommand ??= new RelayCommand<UnscheduledTripDto>(trip =>
        {
            if ((trip ?? SelectedUnscheduledTrip ?? FirstToRoute()) is { } target) SelectedUnscheduledTrip = target;
            Suggestions.ShowLinesCommand.Execute(null);
        });

        private IRelayCommand? _suggestTripsCommand;
        public IRelayCommand SuggestTripsCommand => _suggestTripsCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand(() => Suggestions.ShowTripsCommand.Execute(null));

        public string SuggestLinesToolTip => LocalizationService.Instance["assist.LinesTip"];
        public string SuggestTripsText => LocalizationService.Instance["assist.TripsButton"];
        public string SuggestTripsToolTip => LocalizationService.Instance["assist.TripsTip"];

        private void StartAssistant()
        {
            UnscheduledTrips.CollectionChanged -= OnBacklogChanged;
            UnscheduledTrips.CollectionChanged += OnBacklogChanged;
            AskPlan(2);
        }

        private void StopAssistant()
        {
            UnscheduledTrips.CollectionChanged -= OnBacklogChanged;
            _planTimer?.Stop();
        }

        private void OnBacklogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ApplyPlan();
            AskPlan(_planDay == SelectedDate.Date ? 30 : 2);
        }

        /// <summary>
        /// Asks for the day's plan in the background: soon for a day just opened, and once things are
        /// quiet after a change. Never on a fixed timer, and nothing on the screen waits for it.
        /// </summary>
        private void AskPlan(int seconds)
        {
            if (_planTimer is null)
            {
                _planTimer = new DispatcherTimer(DispatcherPriority.Background);
                _planTimer.Tick += async (_, _) => { _planTimer.Stop(); await LoadPlanAsync(); };
            }
            if (_planTimer.IsEnabled && _planTimer.Interval.TotalSeconds <= seconds) return;
            _planTimer.Stop();
            _planTimer.Interval = TimeSpan.FromSeconds(seconds);
            _planTimer.Start();
        }

        private async Task LoadPlanAsync()
        {
            var day = SelectedDate.Date;
            if (_planning || IsDayReadOnly || UnscheduledTrips.Count == 0) return;
            _planning = true;
            try
            {
                var plan = await _planService.PlanAsync(day, System.Threading.CancellationToken.None);
                if (plan is null || SelectedDate.Date != day) return;
                _plan = plan.Trips.GroupBy(t => t.TripId).ToDictionary(g => g.Key, g => g.First());
                _planDay = day;
                _planAskedUtc = DateTime.UtcNow;
                ApplyPlan();
            }
            catch (Exception ex)
            {
                // Without it the open list is the one of always.
                FileLogger.Log($"Day plan: {ex.Message}");
            }
            finally
            {
                _planning = false;
                if (SelectedDate.Date != day) AskPlan(2);
            }
        }

        private void ApplyPlan()
        {
            var known = _planDay == SelectedDate.Date ? _plan : new();
            foreach (var t in UnscheduledTrips) t.Planned = known.GetValueOrDefault(t.Id);
            RaiseOverflow();
            if (HideOverflow) { if (OverflowCount == 0) HideOverflow = false; else CollectionViewSource.GetDefaultView(UnscheduledTrips).Refresh(); }
        }

        private void RaiseOverflow()
        {
            foreach (var n in new[] { nameof(OverflowCount), nameof(HasOverflow), nameof(OverflowChipText), nameof(OverflowChipToolTip), nameof(HasSelectedOverflow), nameof(SelectedOverflowText) }) OnPropertyChanged(n);
        }

        public int OverflowCount => UnscheduledTrips.Count(t => t.IsOverflow);
        public bool HasOverflow => OverflowCount > 0;
        public string OverflowChipText => string.Format(LocalizationService.Instance[HideOverflow ? "overflow.ChipHidden" : "overflow.Chip"], OverflowCount, UnscheduledTrips.Count(t => t.IsOverflowUrgent));
        public string OverflowChipToolTip => LocalizationService.Instance["overflow.ChipTip"];

        private bool _hideOverflow;
        /// <summary>Leaves the Overflow trips out of sight, except the ones whose time is up.</summary>
        public bool HideOverflow
        {
            get => _hideOverflow;
            set
            {
                if (!SetProperty(ref _hideOverflow, value)) return;
                CollectionViewSource.GetDefaultView(UnscheduledTrips).Filter = value ? o => o is UnscheduledTripDto t && (!t.IsOverflow || t.IsOverflowUrgent) : null;
                RaiseOverflow();
            }
        }

        public bool HasSelectedOverflow => SelectedUnscheduledTrip?.IsOverflow == true;
        public string? SelectedOverflowText => SelectedUnscheduledTrip?.OverflowToolTip;

        private void OnAssistantTripChanged()
        {
            OnPropertyChanged(nameof(HasSelectedOverflow));
            OnPropertyChanged(nameof(SelectedOverflowText));
            if (_suggestions is { IsOpen: true, IsLines: true } && _placement is null && SelectedUnscheduledTrip != null) _suggestions.Refresh();
        }

        private void OnAssistantLineChanged()
        {
            if (_suggestions is { IsOpen: true, IsTrips: true } && _placement is null) _suggestions.Refresh();
        }

        private void RetranslateAssistant() { _suggestions?.Retranslate(); RaiseOverflow(); }

        // ---- Routing a suggestion ----

        /// <summary>
        /// Puts the suggested trip in the suggested line with the button's own process. The place is
        /// looked up again in the route as it is now: somebody may have moved it since.
        /// </summary>
        private async Task RouteSuggestionAsync(RoutingSuggestionDto suggestion)
        {
            if (IsBusy || _isRecalculating) return;
            var trip = UnscheduledTrips.FirstOrDefault(t => t.Id == suggestion.TripId);
            if (trip is null) { _suggestions?.Refresh(0); return; }

            if (suggestion.Group >= 3 && MessageBox.Show(string.Format(LocalizationService.Instance["assist.ConfirmAnyway"], suggestion.Line),
                    LocalizationService.Instance["assist.HeadingLines"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            _placement = suggestion;
            try
            {
                if (SelectedVehicleRoute?.Id != suggestion.VehicleRouteId)
                {
                    var line = _allVehicleRoutesMaster.FirstOrDefault(r => r.Id == suggestion.VehicleRouteId);
                    if (line is null) { UiError.Show(LocalizationService.Instance["assist.LineNotAvailable"], "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    await ShowRouteAsync(SelectedDate, line.Id);
                    trip = UnscheduledTrips.FirstOrDefault(t => t.Id == suggestion.TripId);
                    if (trip is null || SelectedVehicleRoute?.Id != suggestion.VehicleRouteId) return;
                }
                else await LoadSchedulesAsync();

                SelectedUnscheduledTrip = trip;
                if (suggestion.PickupAfterScheduleId is int p && _masterSchedules.All(s => s.Id != p)
                    || suggestion.DropoffAfterScheduleId is int d && _masterSchedules.All(s => s.Id != d))
                {
                    UiError.Show(LocalizationService.Instance["assist.RouteMoved"], LocalizationService.Instance["assist.HeadingLines"], MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (RouteTripCommand.CanExecute(null)) await RouteSelectedTripAsync();
            }
            finally
            {
                _placement = null;
            }

            // On to the next one, without a click.
            if (_suggestions is { IsOpen: true } panel)
            {
                if (panel.IsLines) SelectedUnscheduledTrip = FirstToRoute();
                panel.Refresh(200);
            }
        }

        /// <summary>The stop the pickup goes after, as the panel asked; null when it did not ask.</summary>
        private ScheduleDto? PlacementStop(int? scheduleId) =>
            scheduleId is int id ? _masterSchedules.FirstOrDefault(s => s.Id == id) : _masterSchedules.FirstOrDefault(s => s.Name == "Pull-out");
    }
}
