using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Maps;
using Raphael.Desktop.ViewModels.LiveMap;

namespace Raphael.Desktop.Views.LiveMap
{
    /// <summary>
    /// The live map screen (RE-080). The view model decides what exists and where; the map is a
    /// Maps JavaScript page (<c>Assets/live-map.html</c>) in a composition WebView2, and this class
    /// only translates between the two.
    /// </summary>
    /// <remarks>
    /// To the page: the vehicles (on every change of group, filter or info), each live fix (the page
    /// glides it), fit, zoom and the map type. From the page: a click on a vehicle, which opens the
    /// route card, plus the shared usage and map-type messages every map page sends.
    /// </remarks>
    public partial class LiveMapView : UserControl
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly IMapsUsageApiService _usage = new MapsUsageApiService();
        private readonly HashSet<LiveMapVehicleViewModel> _watched = new();
        private bool _pageReady;
        private bool _framed;
        private bool _initialized;
        private bool _satellite;
        private int? _framedRoute;

        public LiveMapViewModel ViewModel { get; }

        public LiveMapView(LiveMapViewModel viewModel)
        {
            InitializeComponent();

            ViewModel = viewModel;
            DataContext = viewModel;

            viewModel.Vehicles.CollectionChanged += OnVehiclesChanged;
            viewModel.SnapshotLoaded += (_, _) => { SendVehicles(); Frame(); };
            viewModel.LayersChanged += (_, _) => SendLayers();
            viewModel.RouteCard.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(LiveMapRouteCardViewModel.Detail)) await ShowEtaAsync();
            };
            viewModel.TrafficChanged += (_, _) => SendTraffic();
            viewModel.Legs.Changed += id =>
            {
                SendLegs(id);
                SendLayers();

                // Selecting a line frames its day, once, leaving room for the card on the right.
                if (viewModel.Legs.Selected == id && _framedRoute != id)
                {
                    _framedRoute = id;
                    Post(new { type = "fitRoute", id });
                }
                if (viewModel.Legs.Selected is null) _framedRoute = null;
            };
            foreach (var v in viewModel.Vehicles) Watch(v);

            Loaded += async (_, _) =>
            {
                await InitializeMapAsync();
                await viewModel.StartAsync();
            };
        }

        private async System.Threading.Tasks.Task InitializeMapAsync()
        {
            // The control is moved between a tab and its own window; the page survives the move.
            if (_initialized) return;
            _initialized = true;

            try
            {
                await MapWebViewHost.InitializeAsync(Map);
                Map.CoreWebView2.WebMessageReceived += (_, e) => OnPageMessage(e.WebMessageAsJson);
                Map.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess) FileLogger.Log($"Live map: the map page did not load ({e.WebErrorStatus}).");
                };
                Map.CoreWebView2.Navigate($"https://{MapWebViewHost.VirtualHostName}/live-map.html?lang={LocalizationService.Instance.CurrentLanguage}");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: the map page could not start. {ex.Message}");
            }
        }

        #region Page → view

        private void OnPageMessage(string json)
        {
            if (MapWebViewHost.TryForwardUsage(json, _usage)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type)) return;

                switch (type.GetString())
                {
                    case "ready":
                        _pageReady = true;
                        SendView();
                        SendVehicles();
                        foreach (var v in ViewModel.Vehicles) SendLegs(v.VehicleRouteId);
                        SendLayers();
                        Frame();
                        break;

                    case "mapClick":
                        // A click on the map, away from any vehicle: let go of the selection.
                        if (ViewModel.HasSelection) ViewModel.ClearSelectionCommand.Execute(null);
                        break;

                    case "vehicleClick":
                    {
                        // One click selects the vehicle (its route); a double click opens its card.
                        var id = root.GetProperty("id").GetInt32();
                        var vehicle = ViewModel.Vehicles.FirstOrDefault(v => v.VehicleRouteId == id);
                        if (vehicle is null) break;
                        ViewModel.Select(vehicle);
                        if (root.TryGetProperty("double", out var dbl) && dbl.GetBoolean()) _ = ViewModel.RouteCard.OpenAsync(vehicle);
                        break;
                    }

                    case "error":
                        FileLogger.Log($"Live map page: {root.GetProperty("message").GetString()}");
                        break;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: unreadable page message. {ex.Message}");
            }
        }

        #endregion

        #region View → page

        private void Post(object message)
        {
            if (!_pageReady || Map.CoreWebView2 is null) return;
            Map.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
        }

        private static string Hex(System.Windows.Media.Brush brush) =>
            brush is System.Windows.Media.SolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : "#8A96A3";

        private static object Describe(LiveMapVehicleViewModel v) => new
        {
            id = v.VehicleRouteId,
            name = v.Name,
            routeColor = v.RouteColor,
            lat = v.Latitude,
            lng = v.Longitude,
            heading = v.Heading,
            color = Hex(v.GroupBrush),
            moving = v.IsMoving,
            speeding = v.IsSpeeding,
            stale = v.IsStale,
            shown = v.IsShown,
            title = v.ToolTipText
        };

        private void SendVehicles() =>
            Post(new { type = "vehicles", items = ViewModel.Vehicles.Where(v => v.HasPosition).Select(Describe).ToList() });

        /// <summary>The layers and the selection, as the page draws them.</summary>
        private void SendLayers() => Post(new
        {
            type = "layers",
            routes = ViewModel.ShowRoutes,
            traffic = ViewModel.ShowTraffic,
            delays = ViewModel.ShowDelays,
            selected = ViewModel.Selected?.VehicleRouteId,
            wholeDay = ViewModel.ShowWholeDay,
            lang = LocalizationService.Instance.CurrentLanguage
        });

        /// <summary>The delays layer: each vehicle's road with its speed per stretch.</summary>
        private void SendTraffic() => Post(new
        {
            type = "traffic",
            items = ViewModel.Traffic.Values.Select(t => new
            {
                id = t.VehicleRouteId,
                path = t.EncodedPolyline,
                intervals = t.Intervals.Select(i => new { s = i.Start, e = i.End, speed = i.Speed }).ToList(),
                delay = (int)Math.Round(t.DelaySeconds / 60.0)
            }).ToList()
        });

        /// <summary>A line's legs and stops, as the page draws them.</summary>
        private void SendLegs(int vehicleRouteId)
        {
            var legs = ViewModel.Legs.For(vehicleRouteId);
            Post(new
            {
                type = "legs",
                id = vehicleRouteId,
                legs = legs?.Legs.Select(l => new
                {
                    path = l.EncodedPolyline,
                    from = new[] { l.FromLatitude, l.FromLongitude },
                    to = new[] { l.ToLatitude, l.ToLongitude },
                    onboard = l.Onboard,
                    driven = l.Driven,
                    current = l.Current,
                    visible = l.Visible,
                    toStop = l.ToStop,
                    fromGarage = l.FromGarage
                }).ToList(),
                stops = legs?.Stops.Select(st => new { n = st.Number, kind = st.Kind, lat = st.Latitude, lng = st.Longitude, performed = st.Performed, arrived = st.Arrived, trip = st.TripId, time = st.Time.HasValue ? st.Time.Value.ToString(@"hh\:mm") : null }).ToList()
            });
        }

        private void SendView() =>
            Post(new { type = "view", mapTypeId = _satellite ? "satellite" : "roadmap", labels = LabelsToggle?.IsChecked == true });

        private void Frame()
        {
            if (!_pageReady) return;
            var ids = ViewModel.Vehicles.Where(v => v.IsShown).Select(v => v.VehicleRouteId).ToList();
            if (ids.Count == 0) return;
            _framed = true;
            Post(new { type = "fit", ids });
        }

        #endregion

        #region Vehicles

        private void OnVehiclesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (LiveMapVehicleViewModel v in e.OldItems) Unwatch(v);
            if (e.NewItems != null) foreach (LiveMapVehicleViewModel v in e.NewItems) Watch(v);
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var v in _watched.ToList()) Unwatch(v);
                foreach (var v in ViewModel.Vehicles) Watch(v);
            }
            SendVehicles();
        }

        private void Watch(LiveMapVehicleViewModel v)
        {
            if (_watched.Add(v)) { v.PropertyChanged += OnVehiclePropertyChanged; v.Moved += OnVehicleMoved; }
        }

        private void Unwatch(LiveMapVehicleViewModel v)
        {
            if (_watched.Remove(v)) { v.PropertyChanged -= OnVehiclePropertyChanged; v.Moved -= OnVehicleMoved; }
        }

        /// <summary>
        /// A new position is a glide; a change of group, filter, staleness or motion is a redraw.
        /// Both are cheap: a few dozen vehicles, one fix each per half minute.
        /// </summary>
        private void OnVehiclePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var v = (LiveMapVehicleViewModel)sender;

            switch (e.PropertyName)
            {
                case nameof(LiveMapVehicleViewModel.IsShown):
                case nameof(LiveMapVehicleViewModel.IsStale):
                case nameof(LiveMapVehicleViewModel.Group):
                case nameof(LiveMapVehicleViewModel.RouteColor):
                    SendVehicles();
                    if (e.PropertyName == nameof(LiveMapVehicleViewModel.IsShown) && !_framed) Frame();
                    break;
            }
        }

        private void OnVehicleMoved(object sender, EventArgs e)
        {
            var v = (LiveMapVehicleViewModel)sender;
            Post(new
            {
                type = "move", id = v.VehicleRouteId, lat = v.Latitude, lng = v.Longitude, heading = v.Heading,
                moving = v.IsMoving, speeding = v.IsSpeeding, stale = v.IsStale, title = v.ToolTipText
            });
        }

        #endregion

        #region Booking's pieces in the card (live-eta.html)

        private bool _etaStarted;
        private bool _etaReady;

        /// <summary>
        /// Hands the card's trip in progress to live-eta.html, Booking's TripProgressBar and LiveEta
        /// copied as they are. The page is started the first time a card with a trip in progress opens.
        /// </summary>
        private async System.Threading.Tasks.Task ShowEtaAsync()
        {
            if (ViewModel.RouteCard.Detail?.Progress is null) return;

            if (!_etaStarted)
            {
                _etaStarted = true;
                try
                {
                    await MapWebViewHost.InitializeAsync(EtaView);
                    EtaView.CoreWebView2.WebMessageReceived += (_, e) =>
                    {
                        if (e.WebMessageAsJson.Contains("etaReady")) { _etaReady = true; PostEta(); }
                    };
                    EtaView.CoreWebView2.Navigate($"https://{MapWebViewHost.VirtualHostName}/live-eta.html");
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"Live map: the ETA view could not start. {ex.Message}");
                }
                return;
            }

            PostEta();
        }

        private static string Clock(TimeSpan? t) => t?.ToString(@"hh\:mm\:ss");

        private void PostEta()
        {
            var detail = ViewModel.RouteCard.Detail;
            var p = detail?.Progress;
            if (!_etaReady || p is null || EtaView.CoreWebView2 is null) return;

            EtaView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "eta",
                lang = LocalizationService.Instance.CurrentLanguage,
                tripId = detail.CurrentTrip?.TripId ?? 0,
                phase = p.Phase,
                remainingMiles = p.RemainingMiles,
                legMiles = p.TripMiles,
                pickupEta = Clock(p.PickupEta),
                dropoffEta = Clock(p.DropoffEta),
                appointment = Clock(p.PromisedAppointment),
                promisedPickup = Clock(p.PromisedPickup),
                pickedUpAt = Clock(p.PickedUpAt),
                positionAtUtc = DateTime.SpecifyKind(p.AtUtc, DateTimeKind.Utc).ToString("o"),
                businessNow = Clock(p.BusinessNow)
            }, Json));
        }

        #endregion

        #region Controls

        private void Fit_Click(object sender, RoutedEventArgs e) => Frame();

        private void CardBackdrop_MouseDown(object sender, MouseButtonEventArgs e) => ViewModel.RouteCard.IsOpen = false;

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => Post(new { type = "zoom", delta = 1 });

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => Post(new { type = "zoom", delta = -1 });

        private void Road_Click(object sender, RoutedEventArgs e) => SetSatellite(false);

        private void Satellite_Click(object sender, RoutedEventArgs e) => SetSatellite(true);

        private void SetSatellite(bool satellite)
        {
            _satellite = satellite;
            RoadMode.Tag = satellite ? null : "On";
            SatelliteMode.Tag = satellite ? "On" : null;
            SendView();
        }

        /// <summary>Labels on or off, on the street map as on the satellite. Fires while the XAML is still being built.</summary>
        private void MapType_Changed(object sender, RoutedEventArgs e)
        {
            if (LabelsToggle is null) return;
            SendView();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && (ViewModel.RouteCard.IsOpen || ViewModel.HasSelection))
            {
                // Esc closes the card first, then lets go of the selection.
                if (ViewModel.RouteCard.IsOpen) ViewModel.RouteCard.IsOpen = false;
                else ViewModel.ClearSelectionCommand.Execute(null);
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        #endregion
    }
}
