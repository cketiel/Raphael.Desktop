using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
        private bool _labels;            // Map · No labels by default
        private readonly IRoutingApiService _routing = new RoutingApiService();
        private readonly TripService _trips = new TripService();
        private readonly IProviderKeyStore _keys = new ProviderKeyStore();
        private readonly IRealtimeVoiceService _voice;
        private Dictionary<int, string?> _plates = new();
        private List<Raphael.Desktop.DTOs.TripReadDto>? _dayTrips;
        private DateTime _dayTripsAt;
        private int? _tripFocusRoute;
        private int? _framedRoute;

        public LiveMapViewModel ViewModel { get; }

        public LiveMapView(LiveMapViewModel viewModel)
        {
            InitializeComponent();

            _voice = new RealtimeVoiceService(_keys);
            ViewModel = viewModel;
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LiveMapViewModel.IsFollowing))
                    Post(new { type = "follow", id = viewModel.IsFollowing ? viewModel.Selected?.VehicleRouteId : null });
            };
            // The map's own-window button says "back to the tab" once the map is in its window.
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(LiveMapViewModel.ToggleWindowText) or nameof(LiveMapViewModel.FitText)) SendMapButtons();
            };
            DataContext = viewModel;

            viewModel.Vehicles.CollectionChanged += OnVehiclesChanged;
            viewModel.SnapshotLoaded += (_, _) => { SendVehicles(); SendLines(); Frame(); };
            viewModel.LayersChanged += (_, _) => SendLayers();
            viewModel.PropertyChanged += (_, e) =>
            {
                // The Trip Id label goes once its vehicle is no longer the selected one.
                if (e.PropertyName == nameof(LiveMapViewModel.Selected) && _tripFocusRoute.HasValue && ViewModel.Selected?.VehicleRouteId != _tripFocusRoute)
                {
                    _tripFocusRoute = null;
                    Post(new { type = "tripFocus", id = (int?)null });
                }
            };
            viewModel.RouteCard.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(LiveMapRouteCardViewModel.Detail)) await ShowEtaAsync();
            };
            viewModel.TrafficChanged += (_, _) => SendTraffic();
            viewModel.PaceChanged += (_, _) => SendVehicles();
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

            // Closing its own window while in full screen: the next window starts with a normal frame.
            Unloaded += (_, _) => _beforeFullScreen = null;

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
                // Voice control needs the microphone, and only this page may have it.
                Map.CoreWebView2.PermissionRequested += (_, e) =>
                {
                    if (e.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone
                        && e.Uri.StartsWith($"https://{MapWebViewHost.VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
                        e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
                };
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
                        _ = SendCitiesAsync();
                        SendVehicles();
                        foreach (var v in ViewModel.Vehicles) SendLegs(v.VehicleRouteId);
                        SendLayers();
                        SendKeysStatus();
                        SendMapButtons();
                        Frame();
                        break;

                    case "voiceToken":
                        _ = MintVoiceTokenAsync(root.TryGetProperty("tier", out var tierEl) ? tierEl.GetString() : null);
                        break;

                    case "voiceTool":
                        _ = RunVoiceToolAsync(root.GetProperty("callId").GetString() ?? string.Empty,
                            root.GetProperty("name").GetString() ?? string.Empty,
                            root.TryGetProperty("args", out var argsEl) ? argsEl.GetString() : null);
                        break;

                    case "cleanView":
                        // Only the map: the panels go, and come back with the same button (or H).
                        _cleanView = root.GetProperty("on").GetBoolean();
                        SetPanelsVisible(!_cleanView && !_streetView);
                        break;

                    case "streetView":
                    {
                        // Street View fills the map: the panels step aside so its controls can be reached.
                        _streetView = root.GetProperty("on").GetBoolean();
                        SetPanelsVisible(!_cleanView && !_streetView);
                        break;
                    }

                    case "followStopped":
                        ViewModel.IsFollowing = false;
                        break;

                    case "escapeKey":
                        if (_beforeFullScreen is not null) SetFullScreen(false);
                        break;

                    case "fullScreen":
                        ToggleFullScreen();
                        break;

                    case "fitFleet":
                        Frame();
                        break;

                    case "toggleWindow":
                        if (_beforeFullScreen is not null) SetFullScreen(false);
                        ViewModel.ToggleWindowCommand.Execute(null);
                        break;

                    case "saveKeys":
                        SaveKeys(root.GetProperty("keys"));
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

                    case "viewChanged":
                        // The dock's Map source chips: the page already switched; keep it for the next load.
                        _satellite = root.GetProperty("mapTypeId").GetString() == "satellite";
                        _labels = root.GetProperty("labels").GetBoolean();
                        break;

                    case "selectLine":
                    {
                        // The dock's line finder: the same as a click on its vehicle.
                        var id = root.GetProperty("id").GetInt32();
                        var vehicle = ViewModel.Vehicles.FirstOrDefault(v => v.VehicleRouteId == id);
                        if (vehicle is null) break;
                        Post(new { type = "tripFocus", id = (int?)null });
                        ViewModel.Select(vehicle);
                        Post(new { type = "fitRoute", id });
                        break;
                    }

                    case "tripSearch":
                        _ = SearchTripsAsync(root.GetProperty("q").GetString(), root.GetProperty("seq").GetInt32());
                        break;

                    case "selectTrip":
                        SelectTrip(root.GetProperty("id").GetInt32(), root.GetProperty("routeId").GetInt32());
                        break;

                    case "presetChanged":
                        ApplyPreset(root.GetProperty("style").GetString());
                        break;

                    case "openUrl":
                    {
                        var url = root.GetProperty("url").GetString();
                        if (url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                        break;
                    }

                    case "geocode":
                        _ = GeocodeAsync(root.GetProperty("q").GetString());
                        break;

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
            // The pace (green / yellow / red) colours the line's road and its vehicle's ring.
            routeColor = v.Pace,
            delay = v.DelayMinutes,
            speedMph = v.SpeedMph,
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
            delays = ViewModel.TrafficOn,
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
            Post(new { type = "view", mapTypeId = _satellite ? "satellite" : "roadmap", labels = _labels });

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
                moving = v.IsMoving, speeding = v.IsSpeeding, stale = v.IsStale, title = v.ToolTipText,
                speedMph = v.SpeedMph
            });
        }

        #endregion

        #region Moving between windows

        /// <summary>
        /// Lets go of both web views before the view changes window, and puts fresh ones in their
        /// place; the next Loaded starts the map page again and the page gets the fleet on 'ready'.
        /// </summary>
        /// <remarks>
        /// A WebView2CompositionControl draws by capturing its window. Moved to another window while
        /// alive, it froze WPF's rendering of both windows: the process still answered, nothing
        /// repainted, no button worked (2026-11-21). Recreating it costs one map load per move.
        /// </remarks>
        public void ReleaseWebViews()
        {
            Map = Replace(Map, () => new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl
            {
                DefaultBackgroundColor = System.Drawing.ColorTranslator.FromHtml("#E9ECEF")
            });
            _initialized = false;
            _pageReady = false;
            _framed = false;

            if (_etaStarted)
            {
                EtaView = Replace(EtaView, () =>
                {
                    var eta = new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl
                    {
                        Height = 232,
                        Margin = new Thickness(0, 14, 0, 0),
                        DefaultBackgroundColor = System.Drawing.Color.Transparent
                    };
                    eta.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("ProgressVisibility"));
                    return eta;
                });
                _etaStarted = false;
                _etaReady = false;
            }
        }

        private static Microsoft.Web.WebView2.Wpf.WebView2CompositionControl Replace(
            Microsoft.Web.WebView2.Wpf.WebView2CompositionControl old,
            Func<Microsoft.Web.WebView2.Wpf.WebView2CompositionControl> create)
        {
            var fresh = create();
            if (old.Parent is Panel parent)
            {
                var index = parent.Children.IndexOf(old);
                parent.Children.RemoveAt(index);
                parent.Children.Insert(index, fresh);
            }
            try { old.Dispose(); } catch (Exception ex) { FileLogger.Log($"Live map: a web view did not close cleanly ({ex.GetType().Name})."); }
            return fresh;
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

        private bool _cleanView, _streetView;

        /// <summary>The WPF windows over the map: shown, or out of the way (clean view, Street View).</summary>
        private void SetPanelsVisible(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            LeftPanel.Visibility = RightPanel.Visibility = LivePill.Visibility = v;
            // The bar's visibility is bound to the selection: hidden without breaking that binding.
            SelectionBar.Opacity = visible ? 1 : 0;
            SelectionBar.IsHitTestVisible = visible;
        }

        /// <summary>The map's own buttons (fit, own window), in the user's language.</summary>
        private void SendMapButtons() =>
            Post(new { type = "mapButtons", texts = ActionTexts() });

        /// <summary>The top-centre actions' card texts, in the user's language.</summary>
        private object ActionTexts()
        {
            var l = LocalizationService.Instance;
            var own = ViewModel.IsInOwnWindow;
            return new
            {
                streetview = new { title = l["livemap.ActStreetView"], desc = l["livemap.ActStreetViewDesc"] },
                camera = new { title = l["livemap.ActCamera"], desc = l["livemap.ActCameraDesc"] },
                fullscreen = new { title = l["livemap.FullScreen"], desc = l["livemap.ActFullScreenDesc"] },
                fit = new { title = l["livemap.Fit"], desc = l["livemap.ActFitDesc"] },
                window = new
                {
                    title = own ? l["livemap.ActBackToTab"] : l["livemap.ActWindow"],
                    desc = own ? l["livemap.ActBackToTabDesc"] : l["livemap.ActWindowDesc"],
                    icon = own ? "pip_exit" : "open_in_new"
                },
                panels = new { title = l["livemap.ActHidePanels"], desc = l["livemap.ActHidePanelsDesc"] },
                panelsShow = new { title = l["livemap.ActShowPanels"], desc = l["livemap.ActShowPanelsDesc"] },
                exitStreetView = l["livemap.ActExitStreetView"],
                pickSpot = l["livemap.ActPickSpot"],
                noImagery = l["livemap.ActNoImagery"]
            };
        }

        private (WindowStyle Style, WindowState State, ResizeMode Resize)? _beforeFullScreen;

        /// <summary>
        /// Full screen: the map goes to its own window first (from the tab it would keep the tab strip),
        /// and that window covers the screen with every panel and the dock. Esc or the button again leaves.
        /// </summary>
        private void ToggleFullScreen()
        {
            if (_beforeFullScreen is not null) { SetFullScreen(false); return; }
            if (!ViewModel.IsInOwnWindow)
            {
                ViewModel.ToggleWindowCommand.Execute(null);
                // The window is shown on this dispatcher turn; size it once it exists.
                Dispatcher.BeginInvoke(() => SetFullScreen(true), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            SetFullScreen(true);
        }

        private void SetFullScreen(bool on)
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            if (on && _beforeFullScreen is null)
            {
                _beforeFullScreen = (window.WindowStyle, window.WindowState, window.ResizeMode);
                window.WindowState = WindowState.Normal;      // a maximised window keeps its frame otherwise
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Maximized;
            }
            else if (!on && _beforeFullScreen is { } b)
            {
                _beforeFullScreen = null;
                window.WindowState = WindowState.Normal;
                window.WindowStyle = b.Style;
                window.ResizeMode = b.Resize;
                window.WindowState = b.State;
            }
        }

        /// <summary>
        /// The delays layer: before it starts, the cost is said in full; unticked, punctuality is back.
        /// </summary>
        private void DelaysCheck_Click(object sender, MouseButtonEventArgs e)
        {
            // The box never toggles itself: it shows ShowDelays, and only this decides (after the warning).
            e.Handled = true;
            if (ViewModel.ShowDelays) { ViewModel.ShowDelays = false; return; }
            var l = LocalizationService.Instance;
            var lines = ViewModel.Vehicles.Count(v => v.Group == Raphael.Desktop.DTOs.LiveMapGroups.OnRoute);
            var perHour = lines * 6;    // one call per line on route every 10 minutes
            var text = string.Format(l["livemap.DelaysCostText"], lines, perHour, perHour * 12, perHour * 12 * 30);
            var answer = MessageBox.Show(Window.GetWindow(this), text, l["livemap.DelaysCostTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) ViewModel.ShowDelays = true;
        }

        /// <summary>The TomTom delays layer (trial): the same switch as Google's, with TomTom's cost said first.</summary>
        private void DelaysTomTomCheck_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (ViewModel.ShowDelaysTomTom) { ViewModel.ShowDelaysTomTom = false; return; }
            var l = LocalizationService.Instance;
            var lines = ViewModel.Vehicles.Count(v => v.Group == Raphael.Desktop.DTOs.LiveMapGroups.OnRoute);
            var perHour = lines * 6;
            var text = string.Format(l["livemap.DelaysTomTomCostText"], lines, perHour, perHour * 12);
            var answer = MessageBox.Show(Window.GetWindow(this), text, l["livemap.DelaysTomTomCostTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) ViewModel.ShowDelaysTomTom = true;
        }

        /// <summary>A driver clicked in Drivers: the same as a click on their vehicle.</summary>
        private void DriverRow_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not LiveMapVehicleViewModel vehicle) return;
            Post(new { type = "tripFocus", id = (int?)null });
            ViewModel.Select(vehicle);
            Post(new { type = "fitRoute", id = vehicle.VehicleRouteId });
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => Post(new { type = "zoom", delta = 1 });

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => Post(new { type = "zoom", delta = -1 });

        /// <summary>
        /// The dock's Location pills: one per city the fleet works in, read from the routes' garage
        /// addresses ("street, city, state zip"), with that city's garages as its landmarks. No Google call.
        /// </summary>
        private async Task SendCitiesAsync()
        {
            try
            {
                var routes = await new RunService().GetAllAsync();
                _plates = routes.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Vehicle?.Plate);
                SendLines();
                var cities = routes
                    .Where(r => !string.IsNullOrWhiteSpace(r.Garage) && (r.GarageLatitude != 0 || r.GarageLongitude != 0))
                    .Select(r => new { Route = r, City = CityOf(r.Garage!) })
                    .Where(x => x.City != null)
                    .GroupBy(x => x.City!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                    .Take(12)
                    .Select(g => new
                    {
                        id = g.Key.ToLowerInvariant().Replace(' ', '-'),
                        name = g.Key,
                        lat = g.Average(x => x.Route.GarageLatitude),
                        lng = g.Average(x => x.Route.GarageLongitude),
                        pois = g.GroupBy(x => (Math.Round(x.Route.GarageLatitude, 4), Math.Round(x.Route.GarageLongitude, 4)))
                                .Take(5)
                                .Select(p => new { name = string.Join(" · ", p.Select(x => x.Route.Name).Distinct().Take(2)), lat = p.First().Route.GarageLatitude, lng = p.First().Route.GarageLongitude })
                                .ToList()
                    })
                    .ToList();
                Post(new { type = "cities", items = cities });
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: cities for the dock. {ex.Message}");
            }
        }

        private static string? CityOf(string address)
        {
            // "street, City, ST 12345[, USA]" or "City, ST[, USA]": the city is the part before the state.
            var parts = address.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            var state = parts.FindLastIndex(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^[A-Z]{2}(\s+\d{5}(-\d{4})?)?$"));
            if (state < 1) return null;
            var city = parts[state - 1];
            if (city.Any(char.IsDigit) || city.Length < 2) return null;
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(city.ToLowerInvariant());
        }


        // ------------------------------------------------------------------ dock: lines and trips

        /// <summary>Every line on the map, for the dock's line finder (name, driver, vehicle, plate).</summary>
        private void SendLines() => Post(new
        {
            type = "lines",
            items = ViewModel.Vehicles.Select(v => new
            {
                id = v.VehicleRouteId,
                name = v.Name,
                driver = v.DriverName,
                vehicle = v.VehicleName,
                plate = _plates.TryGetValue(v.VehicleRouteId, out var plate) ? plate : null,
                group = v.GroupName,
                onRoute = v.Group == Raphael.Desktop.DTOs.LiveMapGroups.OnRoute
            }).OrderBy(l => l.name).ToList()
        });

        private static int TripRank(string? status) => status switch
        {
            "InProgress" => 0,
            "Started" or "Arrived" or "Waiting" or "Late" => 1,
            "Scheduled" or "Accepted" or "Assigned" => 2,
            "Canceled" => 4,
            _ => 3
        };

        /// <summary>
        /// The dock's trip finder: today's trips by Trip Id (Raphael's or the broker's) or patient name,
        /// the one on board first. Patient names stay on screen; nothing here is logged.
        /// </summary>
        private async Task SearchTripsAsync(string? query, int seq)
        {
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0) return;
            try
            {
                if (_dayTrips is null || DateTime.UtcNow - _dayTripsAt > TimeSpan.FromSeconds(60))
                {
                    _dayTrips = await _trips.GetTripsByDateAsync(ViewModel.OperatingDate);
                    _dayTripsAt = DateTime.UtcNow;
                }

                var routes = ViewModel.Vehicles.ToDictionary(v => v.VehicleRouteId);
                var items = _dayTrips
                    .Where(t => t.Id.ToString() == q
                             || (t.TripId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (t.CustomerName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                    .OrderBy(t => TripRank(t.Status))
                    .ThenBy(t => routes.ContainsKey(t.VehicleRouteId) ? 0 : 1)
                    .ThenBy(t => t.FromTime)
                    .Take(12)
                    .Select(t =>
                    {
                        routes.TryGetValue(t.VehicleRouteId, out var line);
                        var rank = TripRank(t.Status);
                        return new
                        {
                            id = t.Id,
                            routeId = t.VehicleRouteId,
                            label = "TRIP " + (string.IsNullOrWhiteSpace(t.TripId) ? t.Id.ToString() : t.TripId),
                            sub = string.Join(" · ", new[] { t.CustomerName, line?.Name ?? "no line", t.FromTime?.ToString(@"hh\:mm") }.Where(x => !string.IsNullOrWhiteSpace(x))),
                            tag = rank == 0 ? "ON BOARD" : rank == 1 ? "EN ROUTE" : (t.Status ?? string.Empty).ToUpperInvariant(),
                            live = rank <= 1
                        };
                    })
                    .ToList();
                Post(new { type = "tripResults", seq, items });
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: trip search failed. {ex.Message}");
            }
        }

        /// <summary>A trip picked in the dock: its vehicle selected, as a click would, with the Trip Id on it.</summary>
        private void SelectTrip(int tripId, int routeId)
        {
            var vehicle = ViewModel.Vehicles.FirstOrDefault(v => v.VehicleRouteId == routeId);
            if (vehicle is null) return;
            var trip = _dayTrips?.FirstOrDefault(t => t.Id == tripId);
            var label = "TRIP " + (string.IsNullOrWhiteSpace(trip?.TripId) ? tripId.ToString() : trip!.TripId);
            _tripFocusRoute = routeId;
            ViewModel.Select(vehicle);
            Post(new { type = "fitRoute", id = routeId });
            Post(new { type = "tripFocus", id = routeId, label });
        }


        // ------------------------------------------------------------------ dock: provider keys and voice

        /// <summary>Every key the POWER UP dialog shows, by its variable name.</summary>
        private static readonly string[] KeyNames =
        {
            "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "XAI_API_KEY", "OPENROUTER_API_KEY", GoogleTrafficClient.KeyName,
            "TOMTOM_API_KEY", "MAPBOX_ACCESS_TOKEN", "HERE_API_KEY", "AWS_LOCATION_API_KEY"
        };

        /// <summary>Which keys exist; never their values.</summary>
        private void SendKeysStatus()
        {
            var status = _keys.Status(KeyNames);
            Post(new { type = "keysStatus", keys = status, file = ProviderKeyStore.UserSettingsPath });
        }

        private async void SaveKeys(JsonElement keys)
        {
            try
            {
                var values = new Dictionary<string, string?>();
                foreach (var p in keys.EnumerateObject())
                    if (KeyNames.Contains(p.Name)) values[p.Name] = p.Value.GetString();

                // A TomTom key likewise, against TomTom (the TomTom delays layer depends on it).
                if (values.TryGetValue(TomTomTrafficClient.KeyName, out var tomtom) && !string.IsNullOrWhiteSpace(tomtom))
                {
                    var (okT, reasonT) = await TomTomTrafficClient.ValidateAsync(tomtom.Trim());
                    if (!okT)
                    {
                        values.Remove(TomTomTrafficClient.KeyName);
                        Post(new { type = "keysSaved", ok = false, message = string.Format(LocalizationService.Instance["livemap.TomTomKeyInvalid"], reasonT) });
                        if (values.Count == 0) { SendKeysStatus(); return; }
                    }
                }

                // A Google key is kept only if Google answers to it: the delays layer depends on it.
                if (values.TryGetValue(GoogleTrafficClient.KeyName, out var google) && !string.IsNullOrWhiteSpace(google))
                {
                    var (ok, reason) = await GoogleTrafficClient.ValidateAsync(google.Trim());
                    if (!ok)
                    {
                        values.Remove(GoogleTrafficClient.KeyName);
                        Post(new { type = "keysSaved", ok = false, message = string.Format(LocalizationService.Instance["livemap.GoogleKeyInvalid"], reason) });
                        if (values.Count == 0) { SendKeysStatus(); return; }
                    }
                }

                _keys.Save(values);
                ViewModel.RefreshDelaysAvailability();
                Post(new { type = "keysSaved", ok = true, count = values.Count(v => !string.IsNullOrWhiteSpace(v.Value)) });
            }
            catch (Exception ex)
            {
                // The exception type only: a message could carry part of the file.
                FileLogger.Log($"Live map: the provider keys could not be saved ({ex.GetType().Name}).");
                Post(new { type = "keysSaved", ok = false });
            }
            SendKeysStatus();
        }

        private async Task MintVoiceTokenAsync(string? tier)
        {
            var token = await _voice.MintAsync(tier);
            Post(new { type = "voiceToken", value = token.Value, model = token.Model, error = token.Error });
        }

        /// <summary>
        /// Runs a tool the voice assistant called and answers the page with its result, which goes
        /// back to the model. No patient name, phone or address is ever part of a result.
        /// </summary>
        private async Task RunVoiceToolAsync(string callId, string name, string? argsJson)
        {
            object output;
            try
            {
                using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                var a = args.RootElement;
                string Arg(string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

                switch (name)
                {
                    case "select_line":
                    {
                        var q = Arg("query").Trim();
                        bool Has(string? f) => !string.IsNullOrEmpty(f) && f.Contains(q, StringComparison.OrdinalIgnoreCase);
                        var line = ViewModel.Vehicles.FirstOrDefault(v => string.Equals(v.Name, q, StringComparison.OrdinalIgnoreCase))
                                ?? ViewModel.Vehicles.FirstOrDefault(v => Has(v.Name) || Has(v.DriverName) || Has(v.VehicleName)
                                    || (_plates.TryGetValue(v.VehicleRouteId, out var plate) && Has(plate)));
                        if (q.Length == 0 || line is null) { output = new { ok = false, error = $"No line matches {q}" }; break; }
                        Post(new { type = "tripFocus", id = (int?)null });
                        ViewModel.Select(line);
                        Post(new { type = "fitRoute", id = line.VehicleRouteId });
                        output = new { ok = true, line = line.Name, driver = line.DriverName, vehicle = line.VehicleName, state = line.GroupName };
                        break;
                    }

                    case "find_trip":
                    {
                        var q = Arg("tripId").Trim().TrimStart('#');
                        if (_dayTrips is null || DateTime.UtcNow - _dayTripsAt > TimeSpan.FromSeconds(60))
                        {
                            _dayTrips = await _trips.GetTripsByDateAsync(ViewModel.OperatingDate);
                            _dayTripsAt = DateTime.UtcNow;
                        }
                        var routes = ViewModel.Vehicles.ToDictionary(v => v.VehicleRouteId);
                        var trip = _dayTrips
                            .Where(t => t.Id.ToString() == q || string.Equals(t.TripId, q, StringComparison.OrdinalIgnoreCase)
                                     || (q.Length >= 3 && (t.TripId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))
                            .OrderBy(t => TripRank(t.Status))
                            .ThenBy(t => routes.ContainsKey(t.VehicleRouteId) ? 0 : 1)
                            .FirstOrDefault();
                        if (q.Length == 0 || trip is null) { output = new { ok = false, error = $"No trip {q} today" }; break; }
                        if (!routes.TryGetValue(trip.VehicleRouteId, out var line))
                        {
                            output = new { ok = false, error = $"Trip {q} has no vehicle on the map", status = trip.Status };
                            break;
                        }
                        SelectTrip(trip.Id, trip.VehicleRouteId);
                        output = new { ok = true, tripId = string.IsNullOrWhiteSpace(trip.TripId) ? trip.Id.ToString() : trip.TripId,
                                       status = trip.Status, line = line.Name, driver = line.DriverName, pickupTime = trip.FromTime?.ToString(@"hh\:mm") };
                        break;
                    }

                    case "fly_to_place":
                    {
                        var result = await _routing.GeocodeAsync(Arg("query"));
                        if (result?.Latitude is double lat && result.Longitude is double lng)
                        {
                            Post(new { type = "geocoded", lat, lng });
                            output = new { ok = true };
                        }
                        else output = new { ok = false, error = "Place not found" };
                        break;
                    }

                    case "zoom_map":
                    {
                        var steps = a.TryGetProperty("steps", out var st) && st.TryGetInt32(out var n) ? Math.Clamp(n, 1, 4) : 2;
                        Post(new { type = "zoom", delta = Arg("direction") == "out" ? -steps : steps });
                        output = new { ok = true };
                        break;
                    }

                    case "fit_fleet":
                        Frame();
                        output = new { ok = true, vehicles = ViewModel.Vehicles.Count(v => v.IsShown) };
                        break;

                    case "clear_selection":
                        if (ViewModel.HasSelection) ViewModel.ClearSelectionCommand.Execute(null);
                        Post(new { type = "tripFocus", id = (int?)null });
                        output = new { ok = true };
                        break;

                    case "get_fleet_status":
                        output = new
                        {
                            ok = true,
                            byState = ViewModel.Vehicles.GroupBy(v => v.GroupName).ToDictionary(g => g.Key ?? "", g => g.Count()),
                            lines = ViewModel.Vehicles.Select(v => new { line = v.Name, driver = v.DriverName, state = v.GroupName }).ToList()
                        };
                        break;

                    default:
                        output = new { ok = false, error = $"Unknown tool {name}" };
                        break;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map voice: tool {name} failed ({ex.GetType().Name}).");
                output = new { ok = false, error = "The tool failed" };
            }
            Post(new { type = "voiceToolResult", callId, output });
        }

        // ------------------------------------------------------------------ dock: visual presets

        /// <summary>
        /// The accent of the panels follows the preset, as God's Eye's HUD does: cyan, amber on CRT,
        /// green on NVG, white on FLIR.
        /// </summary>
        private void ApplyPreset(string? style)
        {
            var hex = style switch
            {
                "retro" => "#FFAA00",
                "surveillance" => "#33FF33",
                "thermal" => "#FFFFFF",
                _ => "#00D4FF"
            };
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            Resources["LmAccent"] = new System.Windows.Media.SolidColorBrush(color);
            Resources["LmAccentDim"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, color.R, color.G, color.B));
            Resources["LmDivider"] = new System.Windows.Media.LinearGradientBrush(
                new System.Windows.Media.GradientStopCollection
                {
                    new(System.Windows.Media.Color.FromArgb(0x47, color.R, color.G, color.B), 0),
                    new(System.Windows.Media.Color.FromArgb(0x2E, color.R, color.G, color.B), 0.58),
                    new(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 1)
                }, 0);
        }

        /// <summary>The dock's search box, geocoded through the API's routing cache.</summary>
        private async Task GeocodeAsync(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            try
            {
                var result = await _routing.GeocodeAsync(query);
                if (result?.Latitude is double lat && result.Longitude is double lng)
                    Post(new { type = "geocoded", lat, lng });
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: dock search failed. {ex.Message}");
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _beforeFullScreen is not null && !ViewModel.RouteCard.IsOpen)
            {
                SetFullScreen(false);
                e.Handled = true;
                return;
            }
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
