using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>
    /// The live map (RE-080): every line of the fleet, by group, moving as its driver reports.
    /// </summary>
    /// <remarks>
    /// <para>Opens with one snapshot (<c>GET api/live-map</c>) and from then on is told: positions
    /// through <c>WatchMap</c>, and a fresh snapshot only when a group may have changed (a trip
    /// routed or unrouted today, a route out of service or back, a reconnection). There is no polling.</para>
    /// <para>One instance for the whole session, like the Notification Center: moving it between a
    /// tab and its own window, or closing and reopening the tab, keeps the vehicles where they are.</para>
    /// </remarks>
    public partial class LiveMapViewModel : ObservableObject
    {
        private readonly ILiveMapService _service;
        private readonly IDispatchBoardService _board;
        private readonly Dictionary<int, LiveMapVehicleViewModel> _byRoute = new();

        /// <summary>The stop each line's vehicle is standing at (route id → stop number), from its fixes.</summary>
        private readonly Dictionary<int, int> _atStop = new();

        /// <summary>How close a fix must be to the stop it is heading to for the vehicle to be there.</summary>
        private const double AtStopMetres = 120;
        private readonly DispatcherTimer _animator;
        private readonly DispatcherTimer _clock;
        private readonly DispatcherTimer _reloadDebounce;
        private bool _started;
        private DateTime? _lastFixUtc;

        public ObservableCollection<LiveMapVehicleViewModel> Vehicles { get; } = new();

        public ObservableCollection<LiveMapGroupFilter> Groups { get; } = new();

        #region Layers and selection (RE-080)

        /// <summary>Layer: the current leg of every vehicle on route.</summary>
        [ObservableProperty] private bool _showRoutes;

        /// <summary>Layer: Google's live traffic on the streets.</summary>
        [ObservableProperty] private bool _showTraffic;

        /// <summary>Layer: each vehicle's road coloured by congestion, with the delay it adds (Routes Pro).</summary>
        [ObservableProperty] private bool _showDelays;

        /// <summary>The vehicle clicked on: its route is drawn whatever the layers say.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionVisibility), nameof(SelectionTitle))]
        private LiveMapVehicleViewModel _selected;

        /// <summary>For the selected vehicle: the whole day's stops, or only the trip in progress.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TripModeTag), nameof(DayModeTag))]
        private bool _showWholeDay;

        public bool HasSelection => Selected is not null;
        public Visibility SelectionVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;
        public string SelectionTitle => Selected is null ? null : $"{Selected.Name} · {Selected.DriverName}";
        public string TripModeTag => ShowWholeDay ? null : "On";
        public string DayModeTag => ShowWholeDay ? "On" : null;

        /// <summary>Raised when the layers or the selection change, for the map page.</summary>
        public event EventHandler LayersChanged;

        partial void OnShowRoutesChanged(bool value)
        {
            LayersChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(RoutesStatus));
        }
        partial void OnShowTrafficChanged(bool value) => LayersChanged?.Invoke(this, EventArgs.Empty);
        partial void OnShowWholeDayChanged(bool value) => LayersChanged?.Invoke(this, EventArgs.Empty);

        partial void OnShowDelaysChanged(bool value)
        {
            LayersChanged?.Invoke(this, EventArgs.Empty);
            if (value) { DelaysStatus = LocalizationService.Instance["livemap.DelaysAsking"]; _ = RefreshTrafficAsync(); _trafficTimer.Start(); }
            else { _trafficTimer.Stop(); _traffic.Clear(); TrafficChanged?.Invoke(this, EventArgs.Empty); }
        }

        partial void OnSelectedChanged(LiveMapVehicleViewModel value)
        {
            LayersChanged?.Invoke(this, EventArgs.Empty);
            _ = Legs.SelectAsync(value?.VehicleRouteId);
        }

        /// <summary>A click on a vehicle: select it (its route); a second click on the same one keeps it.</summary>
        public void Select(LiveMapVehicleViewModel vehicle) => Selected = vehicle;

        [RelayCommand]
        private void ClearSelection()
        {
            Selected = null;
            RouteCard.IsOpen = false;
        }

        [RelayCommand]
        private void ShowTripOnly() => ShowWholeDay = false;

        [RelayCommand]
        private void ShowDay() => ShowWholeDay = true;

        [RelayCommand]
        private async Task OpenDetailsAsync()
        {
            if (Selected is not null) await RouteCard.OpenAsync(Selected);
        }

        // ---- delays layer: traffic per vehicle, asked every 3 minutes while the layer is on ----

        private readonly Dictionary<int, LiveMapRouteTrafficDto> _traffic = new();
        private readonly DispatcherTimer _trafficTimer = new() { Interval = TimeSpan.FromMinutes(3) };

        public IReadOnlyDictionary<int, LiveMapRouteTrafficDto> Traffic => _traffic;

        /// <summary>What the delays layer found, in one line, so an empty map is not mistaken for a broken one.</summary>
        [ObservableProperty] private string _delaysStatus;

        /// <summary>What the routes layer is showing, in one line (how many lines draw, how many wait).</summary>
        public string RoutesStatus
        {
            get
            {
                if (!ShowRoutes) return null;
                var onRoute = Vehicles.Where(v => v.IsShown && v.Group == LiveMapGroups.OnRoute).ToList();
                var drawing = onRoute.Count(v => Legs.For(v.VehicleRouteId)?.Legs.Any(l => l.Current && l.Visible) == true);
                return string.Format(LocalizationService.Instance["livemap.RoutesStatus"], drawing, onRoute.Count - drawing);
            }
        }

        public event EventHandler TrafficChanged;

        private async Task RefreshTrafficAsync()
        {
            foreach (var v in Vehicles.Where(v => v.IsShown && v.Group == LiveMapGroups.OnRoute).ToList())
            {
                if (!ShowDelays) return;
                try
                {
                    var t = await _service.GetRouteTrafficAsync(v.VehicleRouteId);
                    if (t is null) _traffic.Remove(v.VehicleRouteId); else _traffic[v.VehicleRouteId] = t;
                    v.DelayMinutes = t is null ? 0 : (int)Math.Round(t.DelaySeconds / 60.0);
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"Live map: no traffic for route {v.VehicleRouteId}. {ex.Message}");
                }
            }
            var delayed = _traffic.Values.Count(t => t.DelaySeconds >= 60);
            DelaysStatus = _traffic.Count == 0
                ? LocalizationService.Instance["livemap.DelaysNone"]
                : delayed == 0
                    ? string.Format(LocalizationService.Instance["livemap.DelaysClear"], _traffic.Count)
                    : string.Format(LocalizationService.Instance["livemap.DelaysSome"], delayed, _traffic.Count);
            TrafficChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        /// <summary>Each line's legs, for the route lines on the map.</summary>
        public LiveMapLegsStore Legs { get; }

        /// <summary>The route card, opened by clicking a vehicle.</summary>
        public LiveMapRouteCardViewModel RouteCard { get; }

        /// <summary>The "Today" panel.</summary>
        public LiveMapDayViewModel Day { get; } = new();

        /// <summary>The Drivers panel: lines whose vehicle has reported today, by driver name.</summary>
        public ObservableCollection<LiveMapVehicleViewModel> Drivers { get; } = new();

        /// <summary>Raised after a snapshot, so the view can frame the visible vehicles once.</summary>
        public event EventHandler SnapshotLoaded;

        /// <summary>Set by the host: moves the screen between a tab and its own window.</summary>
        public Action ToggleWindowRequested { get; set; }

        public LiveMapViewModel(ILiveMapService service, IDispatchBoardService board)
        {
            _service = service;
            _board = board;
            RouteCard = new LiveMapRouteCardViewModel(service);
            Legs = new LiveMapLegsStore(service, new RoutingApiService());
            Legs.Changed += _ => OnPropertyChanged(nameof(RoutesStatus));
            _trafficTimer.Tick += async (_, _) => await RefreshTrafficAsync();

            // Opening a card selects its vehicle too: what the card describes is what the map shows.
            RouteCard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LiveMapRouteCardViewModel.Vehicle) && RouteCard.Vehicle is not null)
                    Selected = RouteCard.Vehicle;
            };

            foreach (var group in LiveMapPalette.Order)
            {
                var filter = new LiveMapGroupFilter(group, isOn: group == LiveMapGroups.OnRoute);
                filter.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LiveMapGroupFilter.IsOn)) ApplyFilters();
                };
                Groups.Add(filter);
            }

            _animator = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _animator.Tick += (_, _) => StepAnimations();

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
            {
                OnPropertyChanged(nameof(LastFixText));
                OnPropertyChanged(nameof(IsLive));
                RouteCard.Tick();
                foreach (var v in Vehicles) { v.RefreshStale(); if (v.HasShift && !v.IsShiftClosed) v.RefreshShift(); }
                OnPropertyChanged(nameof(Summary));
            };

            _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _reloadDebounce.Tick += async (_, _) =>
            {
                _reloadDebounce.Stop();
                await LoadSnapshotAsync(frame: false);
            };

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        #region Texts

        public string Title => LocalizationService.Instance["livemap.Title"];
        public string FiltersHeader => LocalizationService.Instance["livemap.Filters"];
        public string LegendHeader => LocalizationService.Instance["livemap.Legend"];
        public string FleetHeader => LocalizationService.Instance["livemap.Fleet"];
        public string LegendMoving => LocalizationService.Instance["livemap.LegendMoving"];
        public string LegendStopped => LocalizationService.Instance["livemap.LegendStopped"];
        public string LegendSpeeding => LocalizationService.Instance["livemap.LegendSpeeding"];
        public string LegendStale => LocalizationService.Instance["livemap.LegendStale"];
        public string FitText => LocalizationService.Instance["livemap.Fit"];
        public string LayersHeader => LocalizationService.Instance["livemap.Layers"];
        public string RoutesLegend1 => LocalizationService.Instance["livemap.RoutesLegend1"];
        public string RoutesLegend2 => LocalizationService.Instance["livemap.RoutesLegend2"];
        public string RoutesLegend3 => LocalizationService.Instance["livemap.RoutesLegend3"];
        public string TrafficLegend => LocalizationService.Instance["livemap.TrafficLegend"];
        public string DelaysLegend => LocalizationService.Instance["livemap.DelaysLegend"];
        public string SelLegendDriven => LocalizationService.Instance["livemap.SelLegendDriven"];
        public string SelLegendAhead => LocalizationService.Instance["livemap.SelLegendAhead"];
        public string SelLegendPlan => LocalizationService.Instance["livemap.SelLegendPlan"];
        public string SelLegendPickup => LocalizationService.Instance["livemap.SelLegendPickup"];
        public string SelLegendDropoff => LocalizationService.Instance["livemap.SelLegendDropoff"];
        public string SelLegendArrived => LocalizationService.Instance["livemap.SelLegendArrived"];
        public string SelLegendDirection => LocalizationService.Instance["livemap.SelLegendDirection"];
        public string SelLegendApproach => LocalizationService.Instance["livemap.SelLegendApproach"];
        public string LayerRoutesText => LocalizationService.Instance["livemap.LayerRoutes"];
        public string LayerTrafficText => LocalizationService.Instance["livemap.LayerTraffic"];
        public string LayerDelaysText => LocalizationService.Instance["livemap.LayerDelays"];
        public string LayerDelaysHint => LocalizationService.Instance["livemap.LayerDelaysHint"];
        public string SelTripText => LocalizationService.Instance["livemap.SelTrip"];
        public string SelDayText => LocalizationService.Instance["livemap.SelDay"];
        public string SelDetailsText => LocalizationService.Instance["livemap.SelDetails"];
        public string SelCloseText => LocalizationService.Instance["livemap.SelClose"];
        public string TodayHeader => LocalizationService.Instance["livemap.Today"];
        public string DriversHeader => LocalizationService.Instance["livemap.Drivers"];
        public string DriversLegend => LocalizationService.Instance["livemap.DriversLegend"];
        public string NoDriversText => LocalizationService.Instance["livemap.NoDrivers"];
        public string MapRoadText => LocalizationService.Instance["livemap.MapRoad"];
        public string MapSatelliteText => LocalizationService.Instance["livemap.MapSatellite"];
        public string MapLabelsText => LocalizationService.Instance["livemap.MapLabels"];
        public string ZoomInText => LocalizationService.Instance["livemap.ZoomIn"];
        public string ZoomOutText => LocalizationService.Instance["livemap.ZoomOut"];

        public string ToggleWindowText => IsInOwnWindow
            ? LocalizationService.Instance["livemap.BackToTab"]
            : LocalizationService.Instance["livemap.OwnWindow"];

        public PackIconKind ToggleWindowIcon => IsInOwnWindow ? PackIconKind.DockWindow : PackIconKind.OpenInNew;

        public string LastFixText
        {
            get
            {
                var live = _board.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
                var head = live ? LocalizationService.Instance["livemap.Live"] : LocalizationService.Instance["livemap.Offline"];
                var shown = Vehicles.Count(v => v.IsShown);

                var age = _lastFixUtc is null
                    ? LocalizationService.Instance["livemap.NoFixYet"]
                    : string.Format(LocalizationService.Instance["livemap.LastFix"], AgeText(DateTime.UtcNow - _lastFixUtc.Value));

                return $"{head} · {string.Format(LocalizationService.Instance["livemap.VehiclesOnMap"], shown)} · {age}";
            }
        }

        public bool IsLive => _board.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;

        /// <summary>The fleet panel: what is on the map right now.</summary>
        public IReadOnlyList<LiveMapSummaryLine> Summary
        {
            get
            {
                var shown = Vehicles.Where(v => v.IsShown).ToList();
                return new[]
                {
                    new LiveMapSummaryLine(LocalizationService.Instance["livemap.SumMoving"], shown.Count(v => v.IsMoving && !v.IsStale)),
                    new LiveMapSummaryLine(LocalizationService.Instance["livemap.SumStopped"], shown.Count(v => !v.IsMoving && !v.IsStale)),
                    new LiveMapSummaryLine(LocalizationService.Instance["livemap.SumSpeeding"], shown.Count(v => v.IsSpeeding && !v.IsStale)),
                    new LiveMapSummaryLine(LocalizationService.Instance["livemap.SumStale"], shown.Count(v => v.IsStale)),
                    new LiveMapSummaryLine(LocalizationService.Instance["livemap.SumNoPosition"],
                        Vehicles.Count(v => !v.HasPosition && IsGroupOn(v.Group))),
                    new LiveMapSummaryLine(LocalizationService.Instance["livemap.SumTripsToday"],
                        Vehicles.Sum(v => v.TripsToday))
                };
            }
        }

        private static string AgeText(TimeSpan age) =>
            age.TotalSeconds < 60 ? $"{(int)age.TotalSeconds} s"
            : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min"
            : $"{(int)age.TotalHours} h";

        #endregion

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToggleWindowText), nameof(ToggleWindowIcon))]
        private bool _isInOwnWindow;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _errorText;

        [RelayCommand]
        private void ToggleWindow() => ToggleWindowRequested?.Invoke();

        [RelayCommand]
        private async Task ReloadAsync() => await LoadSnapshotAsync(frame: true);

        /// <summary>Starts the channel and loads the snapshot. Safe to call every time the screen opens.</summary>
        public async Task StartAsync()
        {
            if (_started) return;
            _started = true;

            _board.VehiclePosition += OnVehiclePosition;
            _board.TripRouted += (_, m) => { QueueReload(); RefreshLegs(m?.VehicleRouteId); };
            _board.TripUnrouted += (_, m) => { QueueReload(); RefreshLegs(m?.VehicleRouteId); };
            _board.RouteChanged += (_, m) => RefreshLegs(m?.VehicleRouteId);
            _board.RouteServiceChanged += (_, _) => QueueReload();
            _board.Reconnected += (_, _) => QueueReload();

            await _board.StartAsync();
            await _board.WatchMapAsync();
            await _board.WatchFleetAsync();

            _clock.Start();

            await LoadSnapshotAsync(frame: true);

            // Routed/unrouted today can move a line between On route and Idle.
            await _board.WatchDayAsync(_operatingDate ?? DateTime.Today);
        }

        public async Task StopAsync()
        {
            _animator.Stop();
            _clock.Stop();
            _reloadDebounce.Stop();
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            await _board.StopAsync();
        }

        private DateTime? _operatingDate;

        private void QueueReload() =>
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                // A burst (an Out of Service moving a dozen trips) becomes one snapshot.
                _reloadDebounce.Stop();
                _reloadDebounce.Start();
            });

        private async Task LoadSnapshotAsync(bool frame)
        {
            IsLoading = true;
            ErrorText = null;

            try
            {
                var snapshot = await _service.GetSnapshotAsync();
                _operatingDate = snapshot.OperatingDate;
                ApplySnapshot(snapshot);
                Day.Apply(snapshot.Day);
                RebuildDrivers();

                // The legs of the lines that run today: one read each, then kept until something changes.
                foreach (var v in Vehicles.Where(v => v.Group == LiveMapGroups.OnRoute))
                    _ = Legs.LoadAsync(v.VehicleRouteId, force: true);

                if (frame) SnapshotLoaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorText = LocalizationService.Instance["livemap.LoadError"];
                FileLogger.Log($"Live map: could not load the snapshot. {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplySnapshot(LiveMapSnapshotDto snapshot)
        {
            var seen = new HashSet<int>();

            foreach (var route in snapshot.Routes)
            {
                seen.Add(route.VehicleRouteId);

                if (_byRoute.TryGetValue(route.VehicleRouteId, out var existing))
                {
                    // Keep the drawn position: a reload must not make a moving vehicle jump.
                    existing.UpdateInfo(route);
                    if (!existing.HasPosition && route.Position is not null)
                        existing.Place(route.Position);
                }
                else
                {
                    var vehicle = new LiveMapVehicleViewModel(route);
                    _byRoute[route.VehicleRouteId] = vehicle;
                    Vehicles.Add(vehicle);
                }

                if (route.Position is not null && (_lastFixUtc is null || route.Position.AtUtc > _lastFixUtc))
                    _lastFixUtc = route.Position.AtUtc;
            }

            foreach (var gone in _byRoute.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Vehicles.Remove(_byRoute[gone]);
                _byRoute.Remove(gone);
            }

            // Each line keeps its colour: by name order, so the same line is the same colour every day.
            var ordered = Vehicles.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].RouteColor = LiveMapPalette.RouteColors[i % LiveMapPalette.RouteColors.Count];

            ApplyFilters();
        }

        private void OnVehiclePosition(object sender, VehiclePositionMessage message)
        {
            if (message is null) return;

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!_byRoute.TryGetValue(message.VehicleRouteId, out var vehicle))
                    return; // a line created after the snapshot: the next reload brings it

                _lastFixUtc = message.AtUtc;
                var hadPosition = vehicle.HasPosition;
                var hadShift = vehicle.HasShift;
                vehicle.MoveTo(message);

                // The open card's line: its Live ETA follows the fixes.
                if (RouteCard.IsOpen && ReferenceEquals(RouteCard.Vehicle, vehicle)) _ = RouteCard.RefreshProgressAsync();

                // A stop may have been performed since: the line's legs are read again, at most every 2 minutes,
                // and at once when the vehicle reaches the stop it was heading to or leaves it — the moments
                // the driver arrives and performs. Performing a stop sends nothing on the live channel, and
                // two minutes of a map pointing at a stop already done is what the dispatcher would see.
                if (vehicle.Group == LiveMapGroups.OnRoute)
                {
                    if (StopMomentPassed(vehicle.VehicleRouteId, message.Latitude, message.Longitude))
                        _ = Legs.LoadAsync(vehicle.VehicleRouteId, force: true);
                    else
                        _ = Legs.LoadAsync(vehicle.VehicleRouteId);
                }

                // First report of the day: the driver joins the Drivers panel now, not at the next snapshot.
                if (!hadShift && vehicle.HasShift) RebuildDrivers();

                // First fix of a line that had none: it may now belong on the map.
                if (!hadPosition) ApplyFilters();

                if (vehicle.IsAnimating && !_animator.IsEnabled)
                    _animator.Start();

                OnPropertyChanged(nameof(LastFixText));
                OnPropertyChanged(nameof(Summary));
            });
        }

        /// <summary>
        /// True on the fix that brings a vehicle to the stop its current leg ends at, and on the fix that
        /// takes it away from the stop it was standing at.
        /// </summary>
        private bool StopMomentPassed(int vehicleRouteId, double lat, double lng)
        {
            var current = Legs.For(vehicleRouteId)?.Legs.FirstOrDefault(l => l.Current);

            if (_atStop.TryGetValue(vehicleRouteId, out var standingAt))
            {
                var stop = Legs.For(vehicleRouteId)?.Legs.FirstOrDefault(l => l.ToStop == standingAt);
                if (stop is not null && Metres(lat, lng, stop.ToLatitude, stop.ToLongitude) <= AtStopMetres) return false;
                _atStop.Remove(vehicleRouteId);
                return true;   // left it
            }

            if (current is null || Metres(lat, lng, current.ToLatitude, current.ToLongitude) > AtStopMetres) return false;
            _atStop[vehicleRouteId] = current.ToStop;
            return true;       // got there
        }

        private static double Metres(double lat1, double lng1, double lat2, double lng2)
        {
            const double r = 6371000;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLng = (lng2 - lng1) * Math.PI / 180;
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * r * Math.Asin(Math.Sqrt(h));
        }

        private void RefreshLegs(int? vehicleRouteId)
        {
            if (vehicleRouteId is not int id) return;
            Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                Legs.Invalidate(id);
                await Legs.LoadAsync(id, force: true);
            });
        }

        private void RebuildDrivers()
        {
            Drivers.Clear();
            // Follows the filters, like the map: a group switched off leaves the list too.
            foreach (var v in Vehicles.Where(v => v.HasShift && IsGroupOn(v.Group)).OrderBy(v => v.DriverName ?? v.Name))
                Drivers.Add(v);
        }

        private void StepAnimations()
        {
            var now = DateTime.UtcNow;
            var any = false;

            foreach (var vehicle in Vehicles)
                any |= vehicle.Step(now);

            if (!any) _animator.Stop();
        }

        private bool IsGroupOn(string group) => Groups.FirstOrDefault(g => g.Group == group)?.IsOn == true;

        private void ApplyFilters()
        {
            foreach (var g in Groups)
                g.Count = Vehicles.Count(v => v.Group == g.Group);

            foreach (var v in Vehicles)
                v.IsShown = v.HasPosition && IsGroupOn(v.Group);

            RebuildDrivers();

            OnPropertyChanged(nameof(LastFixText));
            OnPropertyChanged(nameof(Summary));
        }

        private void OnLanguageChanged()
        {
            foreach (var name in new[]
                     {
                         nameof(Title), nameof(FiltersHeader), nameof(LegendHeader), nameof(FleetHeader),
                         nameof(LegendMoving), nameof(LegendStopped), nameof(LegendSpeeding), nameof(LegendStale),
                         nameof(FitText), nameof(LayersHeader), nameof(RoutesLegend1), nameof(RoutesLegend2), nameof(RoutesLegend3), nameof(TrafficLegend), nameof(DelaysLegend), nameof(SelLegendDriven), nameof(SelLegendAhead), nameof(SelLegendPlan), nameof(SelLegendPickup), nameof(SelLegendDropoff), nameof(SelLegendArrived), nameof(SelLegendDirection), nameof(SelLegendApproach), nameof(RoutesStatus), nameof(LayerRoutesText), nameof(LayerTrafficText), nameof(LayerDelaysText), nameof(LayerDelaysHint), nameof(SelTripText), nameof(SelDayText), nameof(SelDetailsText), nameof(SelCloseText), nameof(TodayHeader), nameof(DriversHeader), nameof(DriversLegend), nameof(NoDriversText), nameof(MapRoadText), nameof(MapSatelliteText), nameof(MapLabelsText), nameof(ZoomInText), nameof(ZoomOutText), nameof(ToggleWindowText), nameof(LastFixText), nameof(Summary)
                     })
                OnPropertyChanged(name);

            foreach (var g in Groups) g.RefreshText();
            Day.Refresh();
            foreach (var v in Vehicles) v.RefreshText();
        }
    }

    /// <summary>One line of the fleet panel.</summary>
    public record LiveMapSummaryLine(string Label, int Value);
}
