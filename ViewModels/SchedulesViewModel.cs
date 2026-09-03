using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Office2016.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using GMap.NET;
using GMap.NET.WindowsPresentation;
using GongSolutions.Wpf.DragDrop;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;
using Raphael.Desktop.Views.Dispatch;
// Aliased, not imported: Raphael.Desktop.Helpers also declares a RelayCommand, and
// importing the whole namespace makes every command in this file ambiguous.
using NotificationKeys = Raphael.Desktop.Helpers.NotificationKeys;
using PerfLog = Raphael.Desktop.Helpers.PerfLog;
using Raphael.Desktop.Views.Schedules;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;


namespace Raphael.Desktop.ViewModels
{
    public partial class SchedulesViewModel : ObservableObject, IDragSource, IDropTarget
    {      
        // Flag to prevent concurrent recalculations.
        private bool _isRecalculating = false;

        private bool _isDataLoading = false; // New flag to avoid duplicates

        /// <summary>
        /// Stores the sequence of the last 'Performed' event that triggered a recalculation.
        /// It acts as a "seal" to prevent redundant recalculations.
        /// It is initialized to -1 to indicate that it has never been calculated.
        /// </summary>
        private int _lastRecalculatedSequence = -1;


        private readonly GpsService _gpsService;
        private readonly RunService _runService;
        private readonly VehicleGroupService _vehicleGroupService;
        private readonly UserConfigService _userConfigService;

        /// <summary>
        /// The SMS gateway, shared by every open Schedule tab. It talks to a third party and
        /// not to our API, so it does not go through ApiClientFactory; built once all the same,
        /// because it used to get a brand new HttpClient on every trip routed.
        /// </summary>
        /// <remarks>
        /// Lazy on purpose: a static initialiser that threw — because the configuration was not
        /// there yet — would take the whole screen down with it, over the SMS.
        /// </remarks>
        private static readonly Lazy<ApiZonitelService> LazySmsService =
            new(() => new ApiZonitelService(new HttpClient(), App.Configuration));

        private static ApiZonitelService SmsService => LazySmsService.Value; 
        private DispatcherTimer _liveUpdateTimer;
        private List<ScheduleDto> _masterSchedules = new List<ScheduleDto>();

        [ObservableProperty]
        private VehicleGroup? _selectedVehicleGroup;

        [ObservableProperty]
        private bool _allowUnperformAction = false;

        [ObservableProperty]
        private bool _showFilterControls = false; 

        [ObservableProperty]
        private bool _displayPerformedEvents = true; //By default, the checkbox will be checked.

        [ObservableProperty]
        private GpsDataDto _driverLastKnownLocation;

        [ObservableProperty]
        private bool _isLiveTrackingMode = false;

       

        [ObservableProperty]
        private GpsDataDto _liveGpsData;

        public bool IsInitialized { get; private set; } = false;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _routeSummaryText;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage;

        public event EventHandler<ZoomAndCenterEventArgs> ZoomAndCenterRequest;

        /// <summary>
        /// Asks the view to bring one open trip into view. The view owns the grid; the
        /// model only knows which row matters.
        /// </summary>
        public event EventHandler<UnscheduledTripEventArgs> ScrollUnscheduledTripIntoViewRequest;

        //private readonly UserConfigService _userConfigService;
        private readonly ScheduleService _scheduleService;
        private readonly TripService _tripService;
        private readonly GoogleMapsService _googleMapsService;

        /// <summary>
        /// Travel times through Raphael.Api. Prefer this over <see cref="_googleMapsService"/>:
        /// it takes the scheduled departure hour and prices a screen's legs in one request.
        /// </summary>
        private readonly IRoutingApiService _routingService;

        [ObservableProperty]
        private DateTime _selectedDate = DateTime.Today.AddDays(1);

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadSchedulesAndTripsCommand))] 
        [NotifyCanExecuteChangedFor(nameof(RouteTripCommand))]
        private VehicleRoute _selectedVehicleRoute;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RouteTripCommand))]
        private UnscheduledTripDto _selectedUnscheduledTrip;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelRouteCommand))]
        private ScheduleDto _selectedSchedule;

        /// <summary>
        /// Bumped to tell the map's marker layers to reposition. It is a counter and not a
        /// boolean because what matters is that the value changed, not what it is.
        /// </summary>
        [ObservableProperty]
        private int _mapRefreshTick;

        // Lista privada para mantener todas las rutas cargadas inicialmente
        private List<VehicleRoute> _allVehicleRoutesMaster = new();
        // Refilled wholesale on every load, so they notify once instead of once per row.
        // Schedules keeps Move() for the drag-and-drop reorder: that is a one-row change and
        // a Reset there would throw away the selection the dispatcher just made.
        public Helpers.RangeObservableCollection<VehicleRoute> VehicleRoutes { get; } = new();
        public ObservableCollection<VehicleGroup> VehicleGroups { get; } = new();
        public Helpers.RangeObservableCollection<ScheduleDto> Schedules { get; } = new();
        public Helpers.RangeObservableCollection<UnscheduledTripDto> UnscheduledTrips { get; } = new();

        // The code generator will create a public ColumnConfigurations property.
        // Every time you assign a new value to ColumnConfigurations,
        // OnPropertyChanged(nameof(ColumnConfigurations)) will be called,
        // which will force the UI to re-evaluate all bindings that depend on this property.
        [ObservableProperty]
        private ObservableCollection<ColumnConfig> _columnConfigurations = new();
        // public ObservableCollection<ColumnConfig> ColumnConfigurations { get; set; } = new();

        #region Map Properties      

        [ObservableProperty]
        private PointLatLng _mapCenter = new PointLatLng(26.616666666667, -81.833333333333); // Fort Myers, Florida

        [ObservableProperty]
        private int _mapZoom = 12;
      
        [ObservableProperty]
        private ObservableCollection<MapPoint> _selectedUnscheduledTripPoints = new();

        #endregion

        public IAsyncRelayCommand ManualRefreshCommand { get; }
        public IAsyncRelayCommand UnperformEventCommand { get; }

        public IAsyncRelayCommand ShowHistoryCommand { get; }

        /// <param name="notificationService">
        /// The office inbox, when this tab is one the dispatcher works in. Optional
        /// because Dispatch builds a schedule model of its own for its inner panel, and
        /// nothing there routes trips.
        /// </param>
        public SchedulesViewModel(
            ScheduleService scheduleService,
            INotificationService notificationService = null)
        {
            AllowUnperformAction = true;

            //UserConfigService _userConfigService = new UserConfigService();
            _scheduleService = scheduleService;
            _tripService = new TripService();
            _routingService = new RoutingApiService();
            _googleMapsService = new GoogleMapsService(_routingService);
            _gpsService = new GpsService();

            // Built once. These were being constructed inside the methods that use them, and
            // each construction stands up an HttpClient and re-checks the session token.
            _runService = new RunService();
            _vehicleGroupService = new VehicleGroupService();
            _userConfigService = new UserConfigService();

            LoadInitialDataCommand = new AsyncRelayCommand(LoadInitialDataAsync);
            LoadSchedulesAndTripsCommand = new AsyncRelayCommand(LoadSchedulesAndTripsAsync, CanLoadSchedulesAndTrips);
            RouteTripCommand = new AsyncRelayCommand(RouteSelectedTripAsync, CanRouteSelectedTrip);
            CancelRouteCommand = new AsyncRelayCommand<ScheduleDto>(CancelSelectedRouteAsync/*, CanCancelSelectedRoute*/);
            OpenColumnSelectorCommand = new RelayCommand(OpenColumnSelector);

            CancelTripCommand = new AsyncRelayCommand<object>(ExecuteCancelTripAsync);
            UncancelTripCommand = new AsyncRelayCommand<object>(ExecuteUncancelTripAsync);
            EditTripCommand = new AsyncRelayCommand<object>(ExecuteEditTripAsync);
            WillCallCommand = new AsyncRelayCommand<object>(ExecuteWillCallAsync);

            UnperformEventCommand = new AsyncRelayCommand<ScheduleDto>(ExecuteUnperformEventAsync);

            ManualRefreshCommand = new AsyncRelayCommand(ManualRefreshAsync); // RefreshLiveDataAsync

            ShowHistoryCommand = new AsyncRelayCommand<object>(ExecuteShowHistoryAsync);

            // The counter follows the collection rather than every place that touches it: rows
            // leave from a reload, from a routing, and from a cancellation arriving over the hub.
            UnscheduledTrips.CollectionChanged += (_, __) => UpdateUnscheduledSummary();

            InitializeColumns();
            //_ = InitializeAsync();

            AttachNotifications(notificationService);
            AttachBoard(new DispatchBoardService());
        }

        private async Task ExecuteShowHistoryAsync(object parameter)
        {
            TripReadDto tripToView = null;

            if (parameter is UnscheduledTripDto unscheduled)
            {               
                tripToView = new TripReadDto
                {
                    Id = unscheduled.Id,
                    CustomerName = unscheduled.CustomerName,
                    PickupAddress = unscheduled.PickupAddress,
                    DropoffAddress = unscheduled.DropoffAddress
                };
            }
            else if (parameter is ScheduleDto schedule && schedule.TripId.HasValue)
            {
                
                IsBusy = true;
                try
                {
                    
                    tripToView = new TripReadDto
                    {
                        Id = schedule.TripId.Value,
                        CustomerName = schedule.Patient,
                        PickupAddress = "See trip details...", 
                        DropoffAddress = "See trip details..."
                    };
                }
                finally { IsBusy = false; }
            }

            if (tripToView == null) return;

            var viewModel = new TripHistoryViewModel(tripToView);
            var view = new Views.TripHistoryDialog { DataContext = viewModel };
           
            await MaterialDesignThemes.Wpf.DialogHost.Show(view, "ScheduleRootDialogHost");
        }

        private async Task ExecuteUnperformEventAsync(ScheduleDto schedule)
        {
            if (schedule == null) return;

            // Show a confirmation dialog
            var result = MessageBox.Show(
                "Are you sure you want to undo the performed status for this event?",
                "Confirm Un-perform",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No) return;

            try
            {
                // Apply undo logic
                schedule.Arrive = null;
                schedule.ArriveDist = null;
                schedule.GPSArrive = null;
                schedule.Perform = null;
                schedule.PerformDist = null;
                schedule.Performed = false;
              
                await _scheduleService.UpdateAsync(schedule.Id, schedule);
               
                int eventIndex = Schedules.IndexOf(schedule);
                if (eventIndex >= 0)
                {
                    
                    await LoadSchedulesAndTripsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error undoing the event status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public IAsyncRelayCommand LoadInitialDataCommand { get; }
        public IAsyncRelayCommand LoadSchedulesAndTripsCommand { get; }
        public IAsyncRelayCommand RouteTripCommand { get; }
        public IAsyncRelayCommand<ScheduleDto> CancelRouteCommand { get; }
        public ICommand OpenColumnSelectorCommand { get; }
        public IAsyncRelayCommand CancelTripCommand { get; }
        public IAsyncRelayCommand UncancelTripCommand { get; }
        public IAsyncRelayCommand EditTripCommand { get; }

        /// <summary>
        /// ⚠️ The only door to <c>Trip.WillCall</c> in the application.
        /// </summary>
        public IAsyncRelayCommand WillCallCommand { get; }

        private void OpenColumnSelector()
        {
            // Action function that the popup ViewModel will use to close.
            Action closeAction = null;

            var viewModel = new ScheduleColumnSelectorViewModel(ColumnConfigurations, () => closeAction?.Invoke());
            var view = new ColumnSelectorView
            {
                DataContext = viewModel,
                //Owner = Application.Current.MainWindow // Assign the main window as the owner
            };

            closeAction = () => view.Close();

            view.ShowDialog();

            if (viewModel.DialogResult == true)
            {
                // 1. Replaces the entire collection. This will trigger the UI update.
                ColumnConfigurations = new ObservableCollection<ColumnConfig>(viewModel.Columns);

                // The new configuration is saved for future sessions.

                _userConfigService.SaveColumnConfig(ColumnConfigurations);

                // The user pressed OK. The main configuration is updated.
                /*foreach (var updatedConfig in viewModel.Columns)
                {
                    var originalConfig = ColumnConfigurations.FirstOrDefault(c => c.PropertyName == updatedConfig.PropertyName);
                    if (originalConfig != null)
                    {
                        originalConfig.IsVisible = updatedConfig.IsVisible;
                    }
                }

                // The new configuration is saved for future sessions.

                _userConfigService.SaveColumnConfig(ColumnConfigurations);*/
            }
        }
        private void InitializeColumns()
        {
            // Try to load user saved settings

            var savedConfig = _userConfigService.LoadColumnConfig();

            if (savedConfig != null)
            {
                foreach (var config in savedConfig)
                {
                    ColumnConfigurations.Add(config);
                }
            }
            else
            {
                // If there is no saved configuration, it creates the default configuration.
                // The PropertyName MUST match the property name in ScheduleDto.
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Action", Header = "Action", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Name", Header = "Name", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Pickup", Header = "Pickup", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Appt", Header = "Appt", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "ETA", Header = "ETA", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Distance", Header = "Distance", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Travel", Header = "Travel", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "On", Header = "On", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "SpaceTypeName", Header = "Space", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Address", Header = "Address", IsVisible = true });

                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Comment", Header = "Comment", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Phone", Header = "Phone", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Arrive", Header = "Arrive", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Perform", Header = "Perform", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "ArriveDist", Header = "ArriveDist", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "PerformDist", Header = "PerformDist", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Driver", Header = "Driver", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "GPSArrive", Header = "GPS Arrive", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "Odometer", Header = "Odometer", IsVisible = true });
                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "AuthNo", Header = "AuthNo", IsVisible = true });

                ColumnConfigurations.Add(new ColumnConfig { PropertyName = "FundingSource", Header = "Funding Source", IsVisible = true });
            }
        }

        public async Task InitializeAsync(DateTime? date = null, VehicleRoute route = null, bool isLiveTracking = false)
        {
            if (IsInitialized) return;

            IsLoading = true;

            try
            {
                await LoadInitialDataListsAsync();

                if (date.HasValue)
                {
                    //SelectedDate = date.Value;
                    _selectedDate = date.Value; 
                    OnPropertyChanged(nameof(SelectedDate)); 
                }

                if (route != null)
                {                   
                    _selectedVehicleRoute = VehicleRoutes.FirstOrDefault(r => r.Id == route.Id) ?? VehicleRoutes.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedVehicleRoute)); 
                }
                else if (VehicleRoutes.Any() && SelectedVehicleRoute == null)
                {
                    _selectedVehicleRoute = VehicleRoutes.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedVehicleRoute));
                }

                if (CanLoadSchedulesAndTrips())
                {
                    await LoadSchedulesAndTripsAsync();
                }

                // Now that the screen knows which day and which route it is showing, it can say
                // so and start being told what the others do to them.
                UpdateBoardWatches();

                // After the initial loading, we check if a recalculation is needed.
                await CheckForPendingRecalculation();

                if (isLiveTracking)
                {
                    IsLiveTrackingMode = true;

                    StartLiveTracking();

                    /*try
                    {
                        
                        DriverLastKnownLocation = await _gpsService.GetLatestGpsDataAsync(_selectedVehicleRoute.Id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al obtener la ubicación inicial del conductor: {ex.Message}");
                        
                    }*/                   
                }

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error fatal durante la inicialización: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnDisplayPerformedEventsChanged(bool value)
        {
            FilterSchedules();
        }

        /*private void FilterSchedules()
        {
            Schedules.Clear();
            IEnumerable<ScheduleDto> filteredEvents = _masterSchedules;

            // Si el checkbox NO está marcado, filtramos los eventos que ya fueron realizados (Performed = true)
            if (!DisplayPerformedEvents)
            {
                filteredEvents = filteredEvents.Where(s => !s.Performed);
            }

            foreach (var schedule in filteredEvents)
            {
                Schedules.Add(schedule);
            }

            // Opcional: Si quieres re-calcular la secuencia visual después de filtrar
            for (int i = 0; i < Schedules.Count; i++)
            {
                Schedules[i].Sequence = i;
            }
        }*/

        private void FilterSchedules()
        {
            // Regla: Siempre mostrar Pull-out, Pull-in y lo que no esté realizado.
            // Si DisplayPerformedEvents es true, mostramos todo.
            // One Reset for the whole grid instead of a Clear plus one notification per row.
            var visible = _masterSchedules
                .OrderBy(s => s.Sequence)
                .Where(s => DisplayPerformedEvents
                            || !s.Performed
                            || s.Name == "Pull-out"
                            || s.Name == "Pull-in");

            Schedules.ReplaceAll(visible);

            CalculateVisualOffsets();
        }

        /*private void FilterSchedules()
        {
            Schedules.Clear();
            IEnumerable<ScheduleDto> filteredEvents = _masterSchedules;

            // To display the sequence correctly taking into account canceled trips since only the Pickup event is displayed
            for (int i = 0; i < _masterSchedules.Count; i++)
            {
                _masterSchedules[i].Sequence = i;
                
            }

            if (!DisplayPerformedEvents)
            {
                filteredEvents = filteredEvents.Where(s => !s.Performed);
            }

            foreach (var schedule in filteredEvents)
            {
                Schedules.Add(schedule);
            }

            CalculateVisualOffsets();
        }*/

        private void StartLiveTracking()
        {
            _liveUpdateTimer = new DispatcherTimer
            {
                // Defines the update interval.
                Interval = TimeSpan.FromSeconds(5) // Update every 5 seconds
            };
            // Subscribe the Tick event to our update method.
            _liveUpdateTimer.Tick += async (s, e) => await RefreshLiveDataAsync();
            // Start the timer.
            _liveUpdateTimer.Start();

            // Make an immediate first call so as not to wait 5 seconds
            _ = RefreshLiveDataAsync();
        }

        // It is forced to update, refresh the map and grid data
        private async Task ManualRefreshAsync()
        {
            await RefreshLiveDataAsync(); // refresh map
            await LoadSchedulesAndTripsAsync(); // refresh grids
        }

        private async Task RefreshLiveDataAsync()
        {
            // Additional security measure: if a recalculation is already in progress,
            // We skip this refresh cycle so as not to interfere.
            if (_isRecalculating) return;

            if (SelectedVehicleRoute == null) return;

            try
            {
                var gpsData = await _gpsService.GetLatestGpsDataAsync(SelectedVehicleRoute.Id);
                if (gpsData != null)
                {
                    // CommunityToolkit notifies the UI, and the marker moves.
                    DriverLastKnownLocation = gpsData;
                }

                var latestSchedules = await _scheduleService.GetSchedulesAsync(SelectedVehicleRoute.Id, SelectedDate);

                // Only events that changed will be updated
                bool stateChanged = MergeScheduleUpdates(latestSchedules);

                // If the status of an event changed to 'Performed', we need to recalculate.
                /*if (stateChanged)
                {
                    // We look up the index of the latest event which is now marked 'Performed'.
                    var lastPerformedIndex = Schedules
                        .Select((schedule, index) => new { schedule, index })
                        .Where(x => x.schedule.Performed)
                        .OrderByDescending(x => x.schedule.Sequence)
                        .Select(x => (int?)x.index)
                        .FirstOrDefault();

                    // If we find an event and it is not the last one in the list, we recalculate from the next one.
                    if (lastPerformedIndex.HasValue && lastPerformedIndex.Value < Schedules.Count - 1)
                    {
                        // We call our method recalculation!
                        await RecalculateScheduleAsync(lastPerformedIndex.Value + 1);
                    }
                }*/

                await CheckForPendingRecalculation();

                CalculateVisualOffsets();
                UpdateRouteSummary();
                //await LoadSchedulesAndTripsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching live GPS data: {ex.Message}");
                
            }
        }

        /// <summary>
        /// Checks if a new event has completed since the last recalculation, and
        /// If so, a new ETA recalculation begins.
        /// </summary>
        private async Task CheckForPendingRecalculation()
        {
            // We look for the last event that is marked as 'Performed'.
            var lastPerformedEvent = Schedules
                .Where(s => s.Performed && s.Sequence.HasValue)
                .OrderByDescending(s => s.Sequence.Value)
                .FirstOrDefault();

            // If there is no event held, there is nothing to do.
            if (lastPerformedEvent == null) return;

            // If the sequence of the last completed event is GREATER than our "stamp",
            // It means there has been progress on the route and we need to recalculate.
            if (lastPerformedEvent.Sequence.Value > _lastRecalculatedSequence)
            {
                // The start index is that of the event NEXT to the one that just completed.
                int startIndex = lastPerformedEvent.Sequence.Value + 1;

                // Arithmetic only. Completing a stop moves no road: the legs ahead are the same
                // ones, and what changed is the hour the vehicle actually left. This used to
                // re-price the entire remaining route through Google, on a five-second timer,
                // on every screen that had the route open.
                await ResequenceEtasFromAsync(startIndex);

                // IMPORTANT! We updated our "seal" to not recalculate for this same event.
                _lastRecalculatedSequence = lastPerformedEvent.Sequence.Value;
            }
        }

        /// <summary>
        /// Updates the existing 'Schedules' collection with data from a new list,
        /// modifying properties instead of replacing objects. This preserves the UI selection.
        /// </summary>
        /// <param name="latestSchedules">The list of schedules just obtained from the API.</param>
        private bool MergeScheduleUpdates(List<ScheduleDto> latestSchedules)
        {
            bool performanceStateChanged = false; // Flag to track whether an event completed.

            var existingSchedulesDict = Schedules.ToDictionary(s => s.Id);

            foreach (var latestSchedule in latestSchedules)
            {
                // We look to see if the newly obtained schedule already exists in our visible collection.
                if (existingSchedulesDict.TryGetValue(latestSchedule.Id, out var existingSchedule))
                {
                    // We detect if the 'Performed' state changed from false to true.
                    if (!existingSchedule.Performed && latestSchedule.Performed)
                    {
                        performanceStateChanged = true;
                    }

                    // If it exists, we update its properties.
                    // Since ScheduleDto is an ObservableObject, the UI will react to every change.
                    existingSchedule.ETA = latestSchedule.ETA;
                    existingSchedule.Arrive = latestSchedule.Arrive;
                    existingSchedule.Perform = latestSchedule.Perform;
                    existingSchedule.Performed = latestSchedule.Performed;
                    existingSchedule.ArriveDist = latestSchedule.ArriveDist;
                    existingSchedule.PerformDist = latestSchedule.PerformDist;
                    existingSchedule.GPSArrive = latestSchedule.GPSArrive;
                    existingSchedule.Status = latestSchedule.Status;                  
                }
                // Note: This simple implementation does not handle schedules that are added or removed

            }
            return performanceStateChanged;
        }

        public void Cleanup()
        {
            // Stops the timer and frees resources to prevent memory leaks.
            _liveUpdateTimer?.Stop();
            if (_liveUpdateTimer != null)
            {
                _liveUpdateTimer.Tick -= async (s, e) => await RefreshLiveDataAsync();
            }
            _liveUpdateTimer = null;
        }

        // Load initial data (route and group lists)

        private async Task LoadInitialDataListsAsync()
        {

            var routes = await _runService.GetAllAsync();

            // Guardamos en la lista maestra
            _allVehicleRoutesMaster = routes.Where(r => {
                var now = DateTime.UtcNow;
                bool inDateRange = now >= r.FromDate && (r.ToDate == null || now <= r.ToDate);
                bool isSuspended = r.Suspensions.Any(s => now >= s.SuspensionStart && now <= s.SuspensionEnd);
                return inDateRange && !isSuspended;
            }).ToList();

            // Llenamos la colección observable que ve la UI por primera vez
            //ApplyVehicleRouteFilter();

            // Cargar grupos

            var groups = await _vehicleGroupService.GetGroupsAsync();
            VehicleGroups.Clear();

            var allGroupsOption = new VehicleGroup
            {
                Id = 0,
                Name = "All Groups",
                Color = "Transparent" // O un color neutro como #FFFFFF
            };
            VehicleGroups.Add(allGroupsOption);

            foreach (var g in groups) VehicleGroups.Add(g);

            // Seleccionar "Todos" por defecto
            SelectedVehicleGroup = allGroupsOption;

            // Ejecutar filtro inicial
            ApplyVehicleRouteFilter();
        }

        partial void OnSelectedVehicleGroupChanged(VehicleGroup? value)
        {
            ApplyVehicleRouteFilter();
        }

        private void ApplyVehicleRouteFilter()
        {
            var previousSelected = SelectedVehicleRoute;
            var filtered = _allVehicleRoutesMaster.AsEnumerable();

            // SI el grupo seleccionado NO es nulo Y su ID no es 0, filtramos.
            // SI el ID es 0, mostramos todos (no entra en el IF).
            if (SelectedVehicleGroup != null && SelectedVehicleGroup.Id != 0)
            {
                filtered = filtered.Where(r => r.Vehicle?.VehicleGroup?.Id == SelectedVehicleGroup.Id);
            }

            VehicleRoutes.ReplaceAll(filtered);

            // Mantener selección si es posible
            SelectedVehicleRoute = VehicleRoutes.FirstOrDefault(r => r.Id == (previousSelected?.Id ?? -1))
                                   ?? VehicleRoutes.FirstOrDefault();
        }
        private async Task LoadInitialDataListsAsyncOld()
        {
            // Este método solo carga las listas de ComboBox, sin seleccionar nada.

            var routes = await _runService.GetAllAsync();
            VehicleRoutes.Clear();
            foreach (var r in routes)
            {
                var now = DateTime.UtcNow;
                bool inDateRange = now >= r.FromDate && (r.ToDate == null || now <= r.ToDate);
                bool isSuspended = r.Suspensions.Any(s => now >= s.SuspensionStart && now <= s.SuspensionEnd);
                if (inDateRange && !isSuspended)
                    VehicleRoutes.Add(r);
            }


            var groups = await _vehicleGroupService.GetGroupsAsync();
            VehicleGroups.Clear();
            foreach (var g in groups)
            {
                VehicleGroups.Add(g);
            }
        }

        private async Task LoadInitialDataAsync()
        {

            var routes = await _runService.GetAllAsync();

            VehicleRoutes.Clear();
            foreach (var route in routes)
            {
                var now = DateTime.UtcNow;
                bool inDateRange = now >= route.FromDate && (route.ToDate == null || now <= route.ToDate);
                bool isSuspended = route.Suspensions.Any(s => now >= s.SuspensionStart && now <= s.SuspensionEnd);
                bool isActive = inDateRange && !isSuspended;
                if (isActive == true)
                    VehicleRoutes.Add(route);
            }

            if (VehicleRoutes.Any() && this.SelectedVehicleRoute == null)
            {
                SelectedVehicleRoute = VehicleRoutes[0];
            }


            var groups = await _vehicleGroupService.GetGroupsAsync();
            VehicleGroups.Clear();
            foreach (var group in groups)
            {
                VehicleGroups.Add(group);
            }
        }

        public async Task LoadDataAsync()
        {          
            if (LoadSchedulesAndTripsCommand.CanExecute(null))
            {
                await LoadSchedulesAndTripsCommand.ExecuteAsync(null);
            }
        }

        private async Task LoadSchedulesAsync()
        {
            // Si ya se está cargando, no permitimos otra carga simultánea
            if (_isDataLoading) return;

            _isDataLoading = true;
            IsLoading = true;

            try
            {               
                _masterSchedules.Clear();              
                                           
                var schedules = await _scheduleService.GetSchedulesAsync(SelectedVehicleRoute.Id, SelectedDate);
                                                          
                _masterSchedules.AddRange(schedules);
                FilterSchedules();
              
                // UpdateMapViewForAllPoints();
                UpdateRouteSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading schedule data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _isDataLoading = false; // Liberar el bloqueo
            }
        }
        private async Task LoadSchedulesAndTripsAsync()
        {
            // Si ya se está cargando, no permitimos otra carga simultánea
            if (_isDataLoading) return;

            _isDataLoading = true;
            IsLoading = true;

            using var _total = PerfLog.Measure("Schedule.LoadSchedulesAndTrips.TOTAL");

            try
            {
                // 1. Limpiar colecciones
                // The bound grids are not emptied here on purpose: ReplaceAll below does it in
                // the same notification that refills them, and the loading overlay hides the old
                // content meanwhile. Clearing first would cost the grids one extra layout pass
                // over the day that is being replaced.
                _masterSchedules.Clear();
                SelectedUnscheduledTripPoints.Clear();
                SelectedUnscheduledTrip = null;

                // 2. Obtener datos del servidor
                // Timed one by one and not with a scope around the WhenAll: they run in
                // parallel, so a single scope would only report the slower of the two and
                // hide which endpoint is the one to fix.
                var schedulesTask = TimedAsync(
                    "Schedule.Net.GetSchedules",
                    () => _scheduleService.GetSchedulesAsync(SelectedVehicleRoute.Id, SelectedDate));
                var tripsTask = TimedAsync(
                    "Schedule.Net.GetUnscheduledTrips",
                    () => _scheduleService.GetUnscheduledTripsAsync(SelectedDate));

                // Ejecutamos ambas peticiones en paralelo para mayor velocidad
                await Task.WhenAll(schedulesTask, tripsTask);

                var schedules = await schedulesTask;
                var trips = await tripsTask;

                // 3. Llenar Master y filtrar Schedules
                using (PerfLog.Measure("Schedule.Bind.Schedules"))
                {
                    _masterSchedules.AddRange(schedules);
                    FilterSchedules();
                }

                // 4. Llenar UnscheduledTrips
                using (PerfLog.Measure("Schedule.Bind.UnscheduledTrips"))
                {
                    UnscheduledTrips.ReplaceAll(trips.Where(t => t.IsCanceled != true));
                }

                PerfLog.Mark("Schedule.Rows.Schedules", 0, _masterSchedules.Count);
                PerfLog.Mark("Schedule.Rows.UnscheduledTrips", 0, UnscheduledTrips.Count);

                using (PerfLog.Measure("Schedule.Map.UpdateAllPoints"))
                {
                    UpdateMapViewForAllPoints();
                }
                UpdateRouteSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading schedule data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                _isDataLoading = false; // Liberar el bloqueo
            }
        }

        /// <summary>
        /// Runs a call and reports how long it took, returning what it returned. Used for the
        /// requests that are launched together: each one gets its own line in the trace.
        /// </summary>
        private static async Task<T> TimedAsync<T>(string label, Func<Task<T>> call)
        {
            using (PerfLog.Measure(label))
            {
                return await call();
            }
        }

        // Load the main grids
        private async Task LoadSchedulesAndTripsAsyncOld()
        {
            IsLoading = true;

            try
            {
                _masterSchedules.Clear();
                //Schedules.Clear();
                UnscheduledTrips.Clear();
                SelectedUnscheduledTripPoints.Clear();

                var schedules = await _scheduleService.GetSchedulesAsync(SelectedVehicleRoute.Id, SelectedDate);
                _masterSchedules.AddRange(schedules);
                // To display the sequence correctly taking into account canceled trips since only the Pickup event is displayed
                /*for (int i = 0; i < schedules.Count; i++)
                {
                    schedules[i].Sequence = i;
                    Schedules.Add(schedules[i]);
                }*/

        FilterSchedules();

                //foreach (var s in schedules) Schedules.Add(s);

                var trips = await _scheduleService.GetUnscheduledTripsAsync(SelectedDate);

                //var geocodingTasks = trips.Select(trip => PopulateCitiesForTravel(trip)).ToList();
                //await Task.WhenAll(geocodingTasks);

                // Only consume the Google Maps service if the Trip object does not have PickupCity or DropoffCity
                foreach (var source in trips)
                {
                    /*if (source.PickupCity.Equals("") || source.PickupCity == null)
                        source.PickupCity = await _googleMapsService.GetCityFromCoordinates(source.PickupLatitude, source.PickupLongitude) ?? "N/A";
                    if (source.DropoffCity.Equals("") || source.DropoffCity == null)
                        source.DropoffCity = await _googleMapsService.GetCityFromCoordinates(source.DropoffLatitude, source.DropoffLongitude) ?? "N/A";*/
                    UnscheduledTrips.Add(source);
                }
                //foreach (var t in trips) UnscheduledTrips.Add(t);

                UpdateMapViewForAllPoints();
                UpdateRouteSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading schedule data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {              
                IsLoading = false;
            }

        }
        public void TriggerZoomToFit()
        {
            UpdateMapViewForAllPoints();

            
        }


        private async Task PopulateCitiesForTravel(UnscheduledTripDto trip)
        {
            // Get the city of origin (Pickup)
            trip.PickupCity = await _googleMapsService.GetCityFromCoordinates(
                trip.PickupLatitude,
                trip.PickupLongitude) ?? "N/A"; // "N/A" = Not Available

            // Get the destination city (Dropoff)
            trip.DropoffCity = await _googleMapsService.GetCityFromCoordinates(
                trip.DropoffLatitude,
                trip.DropoffLongitude) ?? "N/A";
        }

        private bool CanLoadSchedulesAndTrips() => SelectedVehicleRoute != null;

        // GetReturnToGarageTravelTimeAsync lived here: a separate call for the drive home, made
        // after the other two had come back. The garage leg now travels in the same batch as
        // the pickup and dropoff legs, so there is nothing left to ask for on its own.

        // Routing logic
        private async Task RouteSelectedTripAsync()
        {
            var tripToSchedule = SelectedUnscheduledTrip;
            var vehicleRoute = SelectedVehicleRoute;

            if (tripToSchedule == null || vehicleRoute == null)
            {
                MessageBox.Show("Please select a trip and a vehicle route before routing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (await IsAlreadyCancelledAsync(tripToSchedule))
                return;

            IsBusy = true;
            BusyMessage = "Calculating optimal route...";

            try
            {
                
                //var previousSchedule = Schedules.LastOrDefault(s => s.ETA < tripToSchedule.FromTime && s.Name != "Pull-in");
                var previousSchedule = _masterSchedules
                    .OrderBy(s => s.Sequence)
                    .LastOrDefault(s => s.ETA <= tripToSchedule.FromTime && s.Name != "Pull-in");

                // Si hay un previo, insertamos justo después. Si no, después del Pull-out (que es seq 0).
                int targetSequence = (previousSchedule != null) ? (previousSchedule.Sequence ?? 0) + 1 : 1;

                double originLat, originLng;
                TimeSpan previousEta, previousServiceTime;

                if (previousSchedule == null || previousSchedule.Name == "Pull-out")
                {
                    originLat = vehicleRoute.GarageLatitude;
                    originLng = vehicleRoute.GarageLongitude;
                    previousEta = Schedules.FirstOrDefault(s => s.Name == "Pull-out")?.ETA ?? (tripToSchedule.FromTime ?? TimeSpan.Zero) - TimeSpan.FromMinutes(30);
                    previousServiceTime = TimeSpan.Zero;
                }
                else
                {
                    originLat = previousSchedule.ScheduleLatitude;
                    originLng = previousSchedule.ScheduleLongitude;
                    previousEta = previousSchedule.ETA ?? TimeSpan.Zero;
                    previousServiceTime = TimeSpan.FromMinutes(previousSchedule.On ?? 15);
                }

                // The three legs of routing a trip — to the pickup, on to the dropoff, and home to
                // the garage — asked for together. They used to be three separate calls to
                // Google, made one after the other while the dispatcher waited.
                var legs = await _routingService.GetLegsAsync(new List<RouteLegRequestItemDto>
                {
                    new RouteLegRequestItemDto
                    {
                        OriginLat = originLat,
                        OriginLng = originLng,
                        DestLat = tripToSchedule.PickupLatitude,
                        DestLng = tripToSchedule.PickupLongitude,
                        Date = SelectedDate,
                        DepartureTime = previousEta
                    },
                    new RouteLegRequestItemDto
                    {
                        OriginLat = tripToSchedule.PickupLatitude,
                        OriginLng = tripToSchedule.PickupLongitude,
                        DestLat = tripToSchedule.DropoffLatitude,
                        DestLng = tripToSchedule.DropoffLongitude,
                        Date = SelectedDate,

                        // The scheduled pickup hour rather than the computed one: it is known
                        // before the first leg comes back, and an hour-wide bucket does not care
                        // about the difference.
                        DepartureTime = tripToSchedule.FromTime
                    },
                    new RouteLegRequestItemDto
                    {
                        OriginLat = tripToSchedule.DropoffLatitude,
                        OriginLng = tripToSchedule.DropoffLongitude,
                        DestLat = vehicleRoute.GarageLatitude,
                        DestLng = vehicleRoute.GarageLongitude,
                        Date = SelectedDate,
                        DepartureTime = tripToSchedule.ToTime ?? tripToSchedule.FromTime
                    }
                });

                var pickupLeg = legs[0];
                if (!pickupLeg.IsUsable) throw new Exception("Could not calculate the route to the pickup point.");

                double pDistance = pickupLeg.DistanceMiles;
                TimeSpan pTravelTime = TimeSpan.FromSeconds(pickupLeg.DurationInTrafficSeconds ?? pickupLeg.DurationSeconds);
                TimeSpan pCalculatedEta = previousEta + previousServiceTime + pTravelTime;
                TimeSpan pFinalEta = pCalculatedEta;
                if (previousSchedule == null || previousSchedule.EventType != ScheduleEventType.Pickup)
                {
                    TimeSpan pViolationLimit = (tripToSchedule.FromTime ?? TimeSpan.Zero) - TimeSpan.FromMinutes(15);
                    if (pCalculatedEta < pViolationLimit) pFinalEta = pViolationLimit;
                }

                var dropoffLeg = legs[1];
                if (!dropoffLeg.IsUsable) throw new Exception("Could not calculate the route to the dropoff point.");

                double dDistance = dropoffLeg.DistanceMiles;
                TimeSpan dTravelTime = TimeSpan.FromSeconds(dropoffLeg.DurationInTrafficSeconds ?? dropoffLeg.DurationSeconds);
                TimeSpan pickupServiceTime = TimeSpan.FromMinutes(15);
                TimeSpan dFinalEta = pFinalEta + pickupServiceTime + dTravelTime;

                // The drive home. The Pull-in hour is built from this leg; it used to be
                // built from dTravelTime, the trip's own pickup-to-dropoff leg, which billed
                // the return to the garage for the trip all over again.
                //
                // Unusable here is not fatal, unlike the two legs above: the Pull-in is the
                // vehicle coming home, not a patient being collected, and the recalculation
                // that runs straight after routing measures it again anyway.
                var garageLeg = legs[2];
                TimeSpan returnTravelTime = garageLeg.IsUsable
                    ? TimeSpan.FromSeconds(garageLeg.DurationInTrafficSeconds ?? garageLeg.DurationSeconds)
                    : TimeSpan.Zero;

                var request = new RouteTripRequest
                {
                    VehicleRouteId = vehicleRoute.Id,
                    TripId = tripToSchedule.Id,
                    PickupDistance = pDistance,
                    PickupTravelTime = pTravelTime,
                    PickupETA = pFinalEta,
                    DropoffDistance = dDistance,
                    DropoffTravelTime = dTravelTime,
                    DropoffETA = dFinalEta,
                    ReturnToGarageTravelTime = returnTravelTime,
                    VehicleRouteName = vehicleRoute.Name,
                    TargetSequence = targetSequence
                };

                await _scheduleService.RouteTripsAsync(request);
              

                BusyMessage = "Route updated. Finalizing calculations...";

                // Recargamos los datos para tener los IDs nuevos
                // Solo recargar los objetos schedules, el grid de viajes se actualiza localmente, eliminando el viaje a rutear de la lista del grid y sin tener que hacer otra llamada innecesaria al backend
                await LoadSchedulesAsync(); // await LoadSchedulesAndTripsAsync();
                await RecalculateScheduleAsync(0); // Recalcular todo para ajustar Pull-out y demás.
                FilterSchedules();

                // Eliminar localmente el viaje ruteado.
                //
                // ⚠️ Contra tripToSchedule, la referencia capturada al empezar, y NO contra
                // SelectedUnscheduledTrip. Entre aquel momento y este hay varios await, y desde
                // que existe el tablero la selección puede haber desaparecido sola: el servidor
                // emite TripRouted a todo el grupo — incluido quien acaba de rutear — así que el
                // manejador de esta misma pantalla ya quitó la fila y vació la selección. Leer
                // SelectedUnscheduledTrip aquí lanzaba una referencia nula justo después de un
                // ruteo que había salido bien.
                var tripToRemove = UnscheduledTrips.FirstOrDefault(t => t.Id == tripToSchedule.Id);
                if (tripToRemove != null)
                {
                    UnscheduledTrips.Remove(tripToRemove);
                    // Al ser ObservableCollection, el Grid de viajes se actualiza solo aquí
                }
                SelectedUnscheduledTrip = null;

                BusyMessage = "Sending notification to Member...";
                var _apiZonitelService = SmsService;

                // Validar si el teléfono es nulo o está vacío
                if (string.IsNullOrWhiteSpace(tripToSchedule.CustomerPhone))
                {
                    MessageBox.Show("Cannot send SMS notification: The passenger does not have a registered phone number.",
                                    "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Limpiar el teléfono (Quitar espacios, guiones y el prefijo +1 si existe)
                string cleanPhone = tripToSchedule.CustomerPhone.Trim();

                // Si empieza con +1, quitamos los primeros 2 caracteres
                if (cleanPhone.StartsWith("+1"))
                {
                    cleanPhone = cleanPhone.Substring(2);
                }
                // Si por casualidad empieza con 1 (sin el +), quitamos el primer caracter
                else if (cleanPhone.StartsWith("1") && cleanPhone.Length > 10)
                {
                    cleanPhone = cleanPhone.Substring(1);
                }

                // string cleanPhone = tripToSchedule.CustomerPhone.Replace("+1", "").Trim();

                // Limpieza adicional por si el número viene con formato (786) 483-6314
                cleanPhone = new string(cleanPhone.Where(char.IsDigit).ToArray());

                // Formatear la hora (de TimeSpan? a string legible)
                // Usamos el campo Date combinado con FromTime si existe
                string pickupTimeDisplay = "Not set";
                if (tripToSchedule.FromTime.HasValue)
                {
                    // Convertimos TimeSpan a un formato 12h (AM/PM)
                    pickupTimeDisplay = DateTime.Today.Add(tripToSchedule.FromTime.Value).ToString("hh:mm tt");
                }

                /*try
                {
                    // Llamar al servicio
                    // Usamos TripId (si existe) o el Id de la base de datos como respaldo
                    string tripNumber = !string.IsNullOrEmpty(tripToSchedule.TripId)
                                        ? tripToSchedule.TripId
                                        : tripToSchedule.Id.ToString();

                    bool isSent = await _apiZonitelService.SendSMSMessageRideHasBeenScheduled(
                        cleanPhone,
                        tripToSchedule.Date.ToString("M/d/yyyy"),
                        tripToSchedule.CustomerName ?? "Valued Customer",
                        tripNumber,
                        tripToSchedule.PickupAddress ?? "Not specified",
                        pickupTimeDisplay
                    );

                    if (isSent)
                    {
                        //MessageBox.Show("Notification sent successfully to the passenger.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("The SMS could not be sent. Please check the API logs or connection.",
                                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while sending the SMS: {ex.Message}",
                                    "Exception", MessageBoxButton.OK, MessageBoxImage.Error);
                }*/

                /*bool resultado = await _apiZonitelService.SendSMSMessageRiderHasBeenScheduled(
                    "7860000000",
                    "Nombre Pasajero",
                    "12345",
                    "Direccion 123",
                    "10:00 AM"
                );*/


                // Buscamos el nuevo Pickup insertado para saber desde dónde empezar a recalcular
                /*var newEvent = _masterSchedules.FirstOrDefault(s => s.TripId == tripToSchedule.Id && s.EventType == ScheduleEventType.Pickup);
                if (newEvent != null)
                {
                    // Recalculamos desde el principio para asegurar que Pull-out y todo lo demás esté perfecto
                    await RecalculateScheduleAsync(0);

                    // Ya no se necesita llamar a LoadSchedulesAndTripsAsync aquí 
                    // porque RecalculateScheduleAsync ya actualiza los objetos en memoria
                    // y FilterSchedules() mantiene el orden.
                    FilterSchedules();

                    // Volvemos a cargar para refrescar la UI con los ETAs finales
                    //await LoadSchedulesAndTripsAsync();
                }*/

                /*
                // We load the list, which may come with the wrong order from the backend.
                await LoadSchedulesAndTripsAsync();

                // Before recalculating, we make sure that the "Pull-in" is at the end of the in-memory collection.
                var pullInEvent = Schedules.FirstOrDefault(s => s.Name == "Pull-in");
                if (pullInEvent != null)
                {
                    int pullInIndex = Schedules.IndexOf(pullInEvent);
                    if (pullInIndex != Schedules.Count - 1)
                    {
                        // If the Pull-in is not in the last position, we move it there.
                        // This fixes the backend sort error in our local view.
                        Schedules.Move(pullInIndex, Schedules.Count - 1);
                    }
                }

                // Now that the collection is in the correct order, we proceed with the recalculation.
                var newPickupEvent = Schedules.FirstOrDefault(s => s.TripId == tripToSchedule.Id && s.EventType == ScheduleEventType.Pickup);
                if (newPickupEvent != null)
                {
                    int startIndex = Schedules.IndexOf(newPickupEvent);
                    // We pass the already corrected list to the recalculation method.
                    await RecalculateScheduleAsync(startIndex);
                }

                // We reload to ensure that the UI reflects the final state saved in the DB.
                await LoadSchedulesAndTripsAsync();*/
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Trip routing error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                //await LoadSchedulesAndTripsAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Asks the server whether this trip is still routable, and takes the row out if
        /// it is not.
        /// </summary>
        /// <remarks>
        /// The live alert is what normally keeps this grid true, but it travels over a
        /// hub that drops — which is why the panel carries a channel health band at all.
        /// When it was down, the row on screen is the last thing anybody told this
        /// dispatcher, and routing a cancelled trip hands a driver a patient who was told
        /// no vehicle was coming.
        ///
        /// <para>
        /// One read before an operation that already makes two round trips to Google Maps.
        /// A failed read is not treated as a cancellation: the server refuses the routing
        /// itself, so the worst case is the dispatcher getting the refusal a moment later.
        /// </para>
        /// </remarks>
        private async Task<bool> IsAlreadyCancelledAsync(UnscheduledTripDto trip)
        {
            TripReadDto current;

            try
            {
                current = await _tripService.GetTripByIdAsync(trip.Id);
            }
            catch
            {
                return false;
            }

            if (current is null)
                return false;

            bool isCancelled =
                current.IsCancelled
                || string.Equals(
                       current.Status,
                       TripStatus.Canceled,
                       StringComparison.OrdinalIgnoreCase);

            if (!isCancelled)
                return false;

            MessageBox.Show(
                LocalizationService.Instance["RouteCancelledTripMessage"],
                LocalizationService.Instance["RouteCancelledTripTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            // Same departure the live alert would have run, so the grid ends up in the
            // same state whichever of the two noticed first.
            RetireCancelledTrip(trip.Id);

            return true;
        }

        private bool CanRouteSelectedTrip() => SelectedVehicleRoute != null && SelectedUnscheduledTrip != null;

        // Logic to cancel
        private async Task CancelSelectedRouteAsync(ScheduleDto schedule)
        {
            if (schedule == null) return;

            try
            {
                await _scheduleService.CancelRouteAsync(schedule.Id);

                // Refresh data
                // No volver a cargar todos los viajes, insertar el viaje correscondiente de forma local.
                await LoadSchedulesAsync(); // await LoadSchedulesAndTripsAsync();

                if (schedule.TripId.HasValue)
                {
                    var tripRead = await _tripService.GetTripByIdAsync(schedule.TripId.Value);
                    UnscheduledTripDto tripToInsert = ToUnscheduledDto(tripRead);
                    if (tripRead != null)
                    {
                        // ACTUALIZACIÓN LOCAL DE LA LISTA DE VIAJES
                        // Insertamos en la posición 0 para que aparezca de primero en el Grid
                        UnscheduledTrips.Insert(0, tripToInsert);
                       
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                        string.Format(CancelScheduleError, ex.Message),
                        ErrorTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                //MessageBox.Show($"Error canceling Schedule: {ex.Message}", "Error");
            }

        }
        public static UnscheduledTripDto ToUnscheduledDto(TripReadDto readDto)
        {
            if (readDto == null) return null;

            return new UnscheduledTripDto
            {
                Id = readDto.Id,
                Date = readDto.Date,
                CustomerName = readDto.CustomerName,
                // TripReadDto no parece tener CustomerPhone, usamos PickupPhone o null
                CustomerPhone = readDto.PickupPhone,
                FromTime = readDto.FromTime,
                ToTime = readDto.ToTime,
                PickupAddress = readDto.PickupAddress,
                DropoffAddress = readDto.DropoffAddress,
                // MAPEOS CLAVE (Nombres diferentes)
                SpaceType = readDto.SpaceTypeName,
                FundingSource = readDto.FundingSourceName,
                IsCanceled = readDto.IsCancelled, // Nota la diferencia de 'll'
                                                  // FIN MAPEOS CLAVE
                PickupLatitude = readDto.PickupLatitude,
                PickupLongitude = readDto.PickupLongitude,
                DropoffLatitude = readDto.DropoffLatitude,
                DropoffLongitude = readDto.DropoffLongitude,
                Distance = readDto.Distance,
                Charge = readDto.Charge,
                Paid = readDto.Paid,
                Type = readDto.Type,
                Pickup = readDto.Pickup,
                PickupPhone = readDto.PickupPhone,
                PickupComment = readDto.PickupComment,
                Dropoff = readDto.Dropoff,
                DropoffPhone = readDto.DropoffPhone,
                DropoffComment = readDto.DropoffComment,
                TripId = readDto.TripId,
                Authorization = readDto.Authorization,
                WillCall = readDto.WillCall,
                Status = readDto.Status,
                FundingSourceId = readDto.FundingSourceId,
                DriverNoShowReason = readDto.DriverNoShowReason,
                PickupCity = readDto.PickupCity,
                DropoffCity = readDto.DropoffCity
            };
        }

        private bool CanCancelSelectedRoute() => SelectedSchedule != null;

        // Observe changes to automatically refresh data
        partial void OnSelectedDateChanged(DateTime value)
        {
            // The board follows the screen: a dispatcher working Tuesday is not sent Wednesday.
            UpdateBoardWatches();

            // Only reload if the VM has already been fully initialized.
            if (IsInitialized && CanLoadSchedulesAndTrips())
                _ = LoadSchedulesAndTripsAsync();

            /*if (CanLoadSchedulesAndTrips())
                LoadSchedulesAndTripsCommand.Execute(null);*/
        }

        partial void OnSelectedVehicleRouteChanged(VehicleRoute value)
        {
            // A different route means a different vehicle to follow, so the one on the map is
            // forgotten rather than left sitting where the previous one was.
            StopVehicleAnimator();
            HasVehiclePosition = false;
            _lastVehicleReportUtc = null;

            UpdateBoardWatches();

            // Only reload if the VM has already been fully initialized.
            if (IsInitialized && CanLoadSchedulesAndTrips())
                _ = LoadSchedulesAsync(); // no es necesario volver a cargar los viajes


            /*if (CanLoadSchedulesAndTrips())
                LoadSchedulesAndTripsCommand.Execute(null);*/
        }

        // The stops highlighted on the map by the current selection: the clicked event and the
        // other leg of the same trip. Remembered so that changing the selection only has to
        // clear two rows instead of walking the whole route.
        private readonly List<ScheduleDto> _highlightedOnMap = new();

        // Logic to execute when the selection in the Schedules grid changes
        partial void OnSelectedScheduleChanged(ScheduleDto value)
        {
            // IsSelectedForMap is an observable property, so clearing it on every row of the
            // route made one click cost N notifications — and N marker repositions with it.
            foreach (var schedule in _highlightedOnMap)
            {
                schedule.IsSelectedForMap = false;
            }
            _highlightedOnMap.Clear();

            if (value != null)
            {
                //SelectedUnscheduledTrip = null; // This will clear the markers for the unscheduled trip

                value.IsSelectedForMap = true;
                _highlightedOnMap.Add(value);

                var pairedEvent = Schedules.FirstOrDefault(s => s.TripId == value.TripId && s.Id != value.Id);
                if (pairedEvent != null)
                {
                    pairedEvent.IsSelectedForMap = true;
                    _highlightedOnMap.Add(pairedEvent);
                }
            }

            UpdateMapViewForAllPoints();
        }

        partial void OnSelectedUnscheduledTripChanged(UnscheduledTripDto value)
        {          
            /*foreach (var schedule in Schedules)
            {
                schedule.IsSelectedForMap = false;
            }*/
            SelectedUnscheduledTripPoints.Clear();
           
            if (value != null)
            {
                //SelectedSchedule = null; // This will unhighlight the pair in the schedule grid.

                SelectedUnscheduledTripPoints.Add(new MapPoint
                {
                    Latitude = value.PickupLatitude,
                    Longitude = value.PickupLongitude,
                    Type = "Pickup"
                });
                SelectedUnscheduledTripPoints.Add(new MapPoint
                {
                    Latitude = value.DropoffLatitude,
                    Longitude = value.DropoffLongitude,
                    Type = "Dropoff"
                });
            }
          
            UpdateMapViewForAllPoints();
        }


        private void ZoomAndCenterOnPoints(List<PointLatLng> points)
        {
            if (points == null || !points.Any()) return;

            if (points.Count == 1)
            {
                // If there is only one point, we simply center with a fixed zoom.
                MapCenter = points.First();
                MapZoom = 14;
            }
            else
            {
                // If there are multiple points, we calculate the rectangle and fire the event.
                double maxLat = points.Max(p => p.Lat);
                double minLat = points.Min(p => p.Lat);
                double maxLng = points.Max(p => p.Lng);
                double minLng = points.Min(p => p.Lng);

                // We create the RectLatLng with the min/max coordinates
                var rect = new RectLatLng(maxLat, minLng, Math.Abs(maxLng - minLng), Math.Abs(maxLat - minLat));

                // We add a small margin (padding) so that it is not right on the edge.
                rect.Inflate(0.1, 0.1);

                // We throw the event for the View to handle.
                ZoomAndCenterRequest?.Invoke(this, new ZoomAndCenterEventArgs(rect));
            }
        }

        private void UpdateMapViewForAllPoints()
        {
            var allPoints = new List<PointLatLng>();

            // 1. Add all points of the scheduled route (Schedules)
            if (Schedules != null)
            {
                allPoints.AddRange(Schedules.Select(s => new PointLatLng(s.ScheduleLatitude, s.ScheduleLongitude)));
            }

            // 2. If there is an unscheduled trip selected, also add your points
            if (SelectedUnscheduledTrip != null)
            {
                allPoints.Add(new PointLatLng(SelectedUnscheduledTrip.PickupLatitude, SelectedUnscheduledTrip.PickupLongitude));
                allPoints.Add(new PointLatLng(SelectedUnscheduledTrip.DropoffLatitude, SelectedUnscheduledTrip.DropoffLongitude));
            }

            // 3. If we have the driver's location, we add it to the list for the zoom calculation.
            if (DriverLastKnownLocation != null)
            {
                allPoints.Add(new PointLatLng(DriverLastKnownLocation.Latitude, DriverLastKnownLocation.Longitude));
            }

            // 4. Call the existing method that calculates the rectangle and raises the event
            ZoomAndCenterOnPoints(allPoints);
        }

        /// <summary>
        /// Asks the marker layers to reposition themselves. Used when the data behind the
        /// markers moved but the map did not — after a zoom-to-fit, or after the overlap
        /// offsets were recalculated.
        /// </summary>
        /// <remarks>
        /// This replaces ForceRefreshSchedules, which emptied and refilled the whole Schedules
        /// collection to force the old per-marker bindings to re-evaluate. It rebuilt every row
        /// of the grid — on every zoom-to-fit — to move some dots on a map.
        /// </remarks>
        public void InvalidateMapMarkers()
        {
            MapRefreshTick++;
        }

        private void CalculateVisualOffsets()
        {
            // Written only where it actually changes: VisualOffsetIndex is an observable
            // property, so a blind pass raises PropertyChanged on every stop of the route.
            var moved = false;

            foreach (var schedule in Schedules)
            {
                if (schedule.VisualOffsetIndex == 0) continue;

                schedule.VisualOffsetIndex = 0;
                moved = true;
            }

            // We group events by their coordinates and filter out only groups with more than one member (overlaps).
            var overlappingGroups = Schedules
                .GroupBy(s => (s.ScheduleLatitude, s.ScheduleLongitude))
                .Where(g => g.Count() > 1);

            foreach (var group in overlappingGroups)
            {
                int index = 0;
                // We assign an incremental index to each event within the group.
                // Sorting by sequence ensures that scrolling is consistent.
                foreach (var scheduleInGroup in group.OrderBy(s => s.Sequence))
                {
                    if (scheduleInGroup.VisualOffsetIndex != index)
                    {
                        scheduleInGroup.VisualOffsetIndex = index;
                        moved = true;
                    }
                    index++;
                }
            }

            if (moved) InvalidateMapMarkers();
        }

        #region Drag and Drop Implementation

        // --- IDragSource: Controls the start of the drag ---

        // Where the other leg of the dragged trip sits, worked out once when the drag starts.
        // DragOver runs on every mouse move, and it used to answer this question with a
        // FirstOrDefault plus an IndexOf over the whole route each time. The list does not
        // change between StartDrag and Drop, so once is enough.
        private int _dragPairedLegIndex = -1;

        public void StartDrag(IDragInfo dragInfo)
        {
            _dragPairedLegIndex = -1;

            if (dragInfo.SourceItem is not ScheduleDto source) return;

            // Only a Pickup or a Dropoff has a leg to stay on the right side of. Anything else
            // is left unconstrained, exactly as before.
            if (source.EventType != ScheduleEventType.Pickup &&
                source.EventType != ScheduleEventType.Dropoff) return;

            // A Dropoff looks for its Pickup and a Pickup for its Dropoff — the leg it is not
            // allowed to cross.
            var pairedType = source.EventType == ScheduleEventType.Dropoff
                ? ScheduleEventType.Pickup
                : ScheduleEventType.Dropoff;

            for (var i = 0; i < Schedules.Count; i++)
            {
                var candidate = Schedules[i];
                if (candidate.TripId == source.TripId && candidate.EventType == pairedType)
                {
                    _dragPairedLegIndex = i;
                    return;
                }
            }
        }

        public bool CanStartDrag(IDragInfo dragInfo)
        {
            // Validation to NOT allow "Pull-out" or "Pull-in" dragging.
            if (dragInfo.SourceItem is ScheduleDto schedule)
            {
                return schedule.Name != "Pull-out" && schedule.Name != "Pull-in";
            }
            return false;
        }

        public void Dropped(IDropInfo dropInfo)
        {
            // This is called after the drop operation has been completed
            _dragPairedLegIndex = -1;
        }

        public void DragCancelled()
        {
            // Called if the drag is canceled (e.g. by pressing ESC).
            _dragPairedLegIndex = -1;
        }

        public bool TryCatchOccurredException(Exception exception)
        {
            // Allows you to handle exceptions that may occur during the drag-drop.
            MessageBox.Show($"An error occurred during drag and drop: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return true; // true to indicate that the exception has been handled.
        }


        // --- IDropTarget: Control the validation and execution of the drop ---

        public void DragOver(IDropInfo dropInfo)
        {
            var sourceItem = dropInfo.Data as ScheduleDto;
            var targetItem = dropInfo.TargetItem as ScheduleDto;

            if (sourceItem == null || targetItem == null)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            // --- VALIDATIONS IN REAL TIME ---

            // 1. You cannot release it on "Pull-out" or "Pull-in".
            if (targetItem.Name == "Pull-out" || targetItem.Name == "Pull-in")
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            // 2 and 3. The two legs of a trip cannot cross: a Dropoff may not land before its
            // Pickup, and a Pickup may not land after its Dropoff. The index of the other leg
            // was resolved in StartDrag; the list does not move until Drop.
            if (_dragPairedLegIndex >= 0)
            {
                // dropInfo.InsertIndex gives us the position *before* which it will be inserted.
                var crossesItsOwnLeg = sourceItem.EventType == ScheduleEventType.Dropoff
                    ? dropInfo.InsertIndex <= _dragPairedLegIndex
                    : dropInfo.InsertIndex >= _dragPairedLegIndex;

                if (crossesItsOwnLeg)
                {
                    dropInfo.Effects = DragDropEffects.None;
                    return;
                }
            }

            // If all validations pass, we display the "Move" visual effect.
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
        }

        public async void Drop(IDropInfo dropInfo)
        {
            var sourceItem = dropInfo.Data as ScheduleDto;
            if (sourceItem == null) return;

            int oldIndex = Schedules.IndexOf(sourceItem);
            int newIndex = dropInfo.InsertIndex;

            if (oldIndex < newIndex) newIndex--;

            // 1. Mover visualmente
            Schedules.Move(oldIndex, newIndex);

            // 2. Sincronizar Master (Para que contenga el orden que el usuario ve)
            // Esto es vital para manejar eventos ocultos por el filtro
            var targetList = Schedules.ToList();

            // 3. Recalcular y Persistir
            IsBusy = true;
            BusyMessage = "Saving route order...";
            try
            {
                // Actualizamos las secuencias según el nuevo orden visual, y las mandamos
                // TODAS en una sola petición. Antes era un PUT por parada, esperado uno tras
                // otro contra un servidor que está en internet: mover una parada en una ruta
                // de veinte costaba veinte viajes de ida y vuelta, y si uno fallaba la ruta
                // quedaba a medio renumerar.
                for (int i = 0; i < Schedules.Count; i++)
                {
                    Schedules[i].Sequence = i;
                }

                await PersistSequenceAsync(Schedules);

                // Ahora ejecutamos el cálculo de ETAs basado en este nuevo orden
                await RecalculateScheduleAsync(0);

                // Refrescamos solo la ruta. Reordenar paradas no toca la lista de viajes sin
                // ruta: recargarla aquí volvía a traer y a repintar los ~400 viajes del día
                // por cada arrastre.
                await LoadSchedulesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving order: {ex.Message}");
            }
            finally { IsBusy = false; }
        }

        /*public async void Drop(IDropInfo dropInfo)
         {
            var sourceItem = dropInfo.Data as ScheduleDto;
            if (sourceItem == null) return;

            int oldIndex = Schedules.IndexOf(sourceItem);
            int newIndex = dropInfo.InsertIndex;

            // Adjust index if item moves down list
            if (oldIndex < newIndex)
            {
                newIndex--;
            }

            //Move element in observable collection so UI updates
            Schedules.Move(oldIndex, newIndex);

            // --- RECALCULATION AND PERSISTENCE ---
            IsBusy = true;
            BusyMessage = "Recalculating and saving route...";
            try
            {
                // The first element affected is the one in the earliest position
                int startIndex = Math.Min(oldIndex, newIndex);
                await RecalculateScheduleAsync(startIndex);               
                await LoadSchedulesAndTripsAsync();
                UpdateRouteSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update the schedule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Optional: Reload data to undo visual changes if save fails
                await LoadSchedulesAndTripsAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }*/

        /// <summary>
        /// Which pair of points each stop's travel time was measured over.
        /// </summary>
        /// <remarks>
        /// A travel time only stops being valid when one of its two endpoints changes. Remembering
        /// the pair is what lets a reorder ask for the two or three legs that actually moved
        /// instead of the whole route.
        /// </remarks>
        private readonly Dictionary<int, string> _legKeyByScheduleId = new Dictionary<int, string>();

        /// <summary>
        /// Recalculates the route after its <b>shape</b> changed — a stop moved, was added,
        /// removed or cancelled.
        /// </summary>
        /// <remarks>
        /// ⚠️ Only for changes of shape. When a driver simply performs a stop, no leg has moved
        /// and nothing needs pricing: use <see cref="ResequenceEtasFromAsync"/>, which is free.
        ///
        /// <para>
        /// This method used to ignore <paramref name="startIndex"/> and walk the whole route from
        /// index 1, asking Google for every pending stop, one request at a time. On a
        /// thirty-stop route that was thirty billed requests per drag of the mouse, and the same
        /// again every time the driver completed anything. Now it prices only the legs whose two
        /// endpoints changed, in a single batched request.
        /// </para>
        /// </remarks>
        private async Task RecalculateScheduleAsync(int startIndex)
        {
            // Usamos Schedules (la lista visual ordenada) para el cálculo
            if (_isRecalculating || Schedules.Count <= 2) return;

            try
            {
                _isRecalculating = true;

                await FetchChangedTravelTimesAsync(startIndex);

                await ResequenceAndPersistAsync(startIndex);
            }
            finally { _isRecalculating = false; }
        }

        /// <summary>
        /// Re-chains the arrival times from <paramref name="startIndex"/> on, without pricing
        /// anything.
        /// </summary>
        /// <remarks>
        /// This is what a driver completing a stop calls for. The remaining legs are the same
        /// roads they were a minute ago, so their travel times still hold; what changed is the
        /// hour the vehicle actually left, and that is arithmetic. It runs every few seconds on
        /// every open schedule screen, and it costs nothing.
        /// </remarks>
        private async Task ResequenceEtasFromAsync(int startIndex)
        {
            if (_isRecalculating || Schedules.Count <= 2) return;

            try
            {
                _isRecalculating = true;

                await ResequenceAndPersistAsync(startIndex);
            }
            finally { _isRecalculating = false; }
        }

        /// <summary>
        /// Prices the legs whose endpoints changed since they were last measured. One request.
        /// </summary>
        private async Task FetchChangedTravelTimesAsync(int startIndex)
        {
            int from = Math.Max(1, startIndex);

            var wanted = new List<RouteLegRequestItemDto>();
            var targets = new List<ScheduleDto>();

            for (int i = from; i < Schedules.Count; i++)
            {
                var current = Schedules[i];

                if (current.Performed) continue;

                // A cancelled stop nobody drove to is not a leg: it costs no time and no distance.
                if (current.Status == "Canceled" && current.Arrive == null) continue;

                var previous = FindValidPrevious(i);

                if (previous == null) continue;

                var key = LegKeyOf(previous, current);

                // Already measured over exactly this pair of points: the road has not moved.
                if (current.Travel.HasValue
                    && _legKeyByScheduleId.TryGetValue(current.Id, out var known)
                    && known == key)
                {
                    continue;
                }

                targets.Add(current);

                wanted.Add(new RouteLegRequestItemDto
                {
                    OriginLat = previous.ScheduleLatitude,
                    OriginLng = previous.ScheduleLongitude,
                    DestLat = current.ScheduleLatitude,
                    DestLng = current.ScheduleLongitude,

                    // The hour the vehicle is planned to leave, not the hour the dispatcher is
                    // looking at the screen. A route built the evening before was being priced
                    // against last night's traffic.
                    Date = current.Date ?? SelectedDate,

                    // Falling back to this stop's own scheduled hour keeps the leg in the right
                    // hour of the day when the chain ahead has no estimate yet — otherwise it
                    // would be priced as leaving at midnight.
                    DepartureTime = previous.ETA ?? current.Pickup ?? current.Appt
                });
            }

            if (wanted.Count == 0) return;

            var legs = await _routingService.GetLegsAsync(wanted);

            for (int i = 0; i < targets.Count; i++)
            {
                var leg = i < legs.Count ? legs[i] : null;

                // ⚠️ A leg nobody could price keeps whatever it had. Writing a zero here would
                // tell the dispatcher the vehicle arrives the instant it leaves.
                if (leg == null || !leg.IsUsable) continue;

                targets[i].Distance = leg.DistanceMiles;
                targets[i].Travel = TimeSpan.FromSeconds(leg.DurationInTrafficSeconds ?? leg.DurationSeconds);

                _legKeyByScheduleId[targets[i].Id] = LegKeyOf(FindValidPrevious(Schedules.IndexOf(targets[i])), targets[i]);
            }
        }

        /// <summary>
        /// Walks the route from <paramref name="startIndex"/>, chaining arrival times and saving
        /// only the rows that actually changed.
        /// </summary>
        private async Task ResequenceAndPersistAsync(int startIndex)
        {
            int from = Math.Max(1, startIndex);

            // Collected as we walk and written once at the end. The walk used to await a PUT
            // inside itself — twice per stop in the worst case — so a route of twenty stops
            // held the interface for up to forty round trips, and this runs on a timer.
            var moved = new List<ScheduleDto>();

            for (int i = from; i < Schedules.Count; i++)
            {
                var current = Schedules[i];

                var before = (current.Sequence, current.ETA, current.Travel, current.Distance);

                var validPrevious = FindValidPrevious(i) ?? Schedules[0];

                current.Sequence = i;

                if (current.Status == "Canceled" && current.Arrive == null)
                {
                    // Es un viaje cancelado al que no se fue: distancia 0
                    current.Distance = 0;
                    current.Travel = TimeSpan.Zero;
                    current.ETA = DepartureTimeOf(validPrevious);
                }
                else if (!current.Performed)
                {
                    TimeSpan travelToCurrent = current.Travel ?? TimeSpan.Zero;

                    if (validPrevious.Name != null && validPrevious.Name.Equals("Pull-out"))
                    {
                        var pullOutEta = current.Pickup - (TimeSpan.FromMinutes(20) + travelToCurrent);

                        if (validPrevious.ETA != pullOutEta)
                        {
                            validPrevious.ETA = pullOutEta;
                            if (!moved.Contains(validPrevious)) moved.Add(validPrevious);
                        }
                    }

                    // CÁLCULO DE ETA SIGUIENTE
                    TimeSpan prevService = (validPrevious.Name == "Pull-out")
                        ? TimeSpan.Zero
                        : TimeSpan.FromMinutes(validPrevious.On ?? 15);

                    TimeSpan calculatedEta = DepartureTimeOf(validPrevious) + prevService + travelToCurrent;

                    // Violaciones de tiempo
                    if (current.EventType == ScheduleEventType.Pickup && current.Pickup.HasValue)
                    {
                        TimeSpan margin = (current.TripType == "Return") ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15);
                        if (calculatedEta < (current.Pickup.Value - margin)) calculatedEta = current.Pickup.Value - margin;
                    }

                    current.ETA = calculatedEta;
                }

                // Only what moved. This loop runs on a five-second timer with the screen open,
                // and it used to write every row of the route on every pass.
                if (before != (current.Sequence, current.ETA, current.Travel, current.Distance))
                {
                    if (!moved.Contains(current)) moved.Add(current);
                }
            }

            await PersistSequenceAsync(moved);
        }

        /// <summary>
        /// Writes the order and the recalculated hours of the given stops in one request.
        /// </summary>
        /// <remarks>
        /// Nothing happens when the list is empty, which is the common case on the refresh
        /// timer: most passes find the route exactly as they left it.
        /// </remarks>
        private async Task PersistSequenceAsync(IEnumerable<ScheduleDto> stops)
        {
            if (SelectedVehicleRoute == null) return;

            var payload = stops
                .Select(s => new ScheduleStopSequenceDto
                {
                    Id = s.Id,
                    Sequence = s.Sequence,
                    ETA = s.ETA,
                    Travel = s.Travel,
                    Distance = s.Distance
                })
                .ToList();

            if (payload.Count == 0) return;

            await _scheduleService.ResequenceAsync(new ScheduleResequenceRequest
            {
                VehicleRouteId = SelectedVehicleRoute.Id,
                Date = SelectedDate,
                Stops = payload
            });
        }

        /// <summary>
        /// The stop the vehicle left to reach the next one: the most recent one before
        /// <paramref name="index"/> that is not a cancellation nobody drove to.
        /// </summary>
        private ScheduleDto FindValidPrevious(int index)
        {
            for (int j = index - 1; j >= 0; j--)
            {
                var p = Schedules[j];

                // Un punto es válido si: No está cancelado O (está cancelado pero el Driver llegó/Arrive)
                bool isPhysicalStop = p.Status != "Canceled" || p.Arrive != null;

                if (isPhysicalStop) return p;
            }

            return Schedules.Count > 0 ? Schedules[0] : null;
        }

        /// <summary>
        /// When the vehicle left a stop: the hour it really happened if it has, the estimate if
        /// it has not.
        /// </summary>
        /// <remarks>
        /// ⚠️ This is what makes a delay travel down the route. The chain used to be built out of
        /// estimates all the way, including for stops already completed, so a driver running
        /// twenty minutes late still showed every later arrival at its original hour — and a
        /// dispatcher reading that screen had no reason to call the clinic.
        /// </remarks>
        private static TimeSpan DepartureTimeOf(ScheduleDto stop)
        {
            if (stop == null) return TimeSpan.Zero;

            if (stop.Performed && stop.Perform.HasValue) return stop.Perform.Value;

            if (stop.Arrive.HasValue) return stop.Arrive.Value;

            return stop.ETA ?? TimeSpan.Zero;
        }

        /// <summary>The pair of points a travel time was measured over, to four decimals.</summary>
        private static string LegKeyOf(ScheduleDto from, ScheduleDto to)
        {
            if (from == null || to == null) return string.Empty;

            return string.Join("|",
                (int)Math.Round(from.ScheduleLatitude * 10000),
                (int)Math.Round(from.ScheduleLongitude * 10000),
                (int)Math.Round(to.ScheduleLatitude * 10000),
                (int)Math.Round(to.ScheduleLongitude * 10000));
        }

        private async Task RecalculateScheduleAsyncOld(int startIndex)
        {
            // If we are already recalculating, we leave to avoid problems.
            if (_isRecalculating) return;

            // If the collection is empty or only has the Pull-out, there is nothing to do.
            if (Schedules.Count <= 1) return;

            // If the collection is very small or the index is invalid, there is nothing to do.
            // startIndex should point to an actual event, not the Pull-in.
            if (Schedules.Count <= 2 || startIndex <= 0 || startIndex >= Schedules.Count - 1) return;

            try
            {
                _isRecalculating = true;
                IsBusy = true; 
                BusyMessage = "Recalculating route ETAs...";

                for (int i = startIndex; i < Schedules.Count - 1; i++)
                {
                    var currentSchedule = Schedules[i];
                    ScheduleDto previousSchedule = Schedules[i - 1];

                    // If the current event is already 'Performed', we do not need to recalculate its ETA.
                    // We just skip to the next one.
                    if (currentSchedule.Performed && !currentSchedule.Status.Equals("Canceled")) 
                    {
                        continue;
                    }

                    currentSchedule.Sequence = i;
              
                    var routeDetails = await _googleMapsService.GetRouteFullDetails(
                        previousSchedule.ScheduleLatitude,
                        previousSchedule.ScheduleLongitude,
                        currentSchedule.ScheduleLatitude,
                        currentSchedule.ScheduleLongitude);

                    if (routeDetails != null)
                    {
                        currentSchedule.Distance = routeDetails.DistanceMiles;
                        currentSchedule.Travel = TimeSpan.FromSeconds(routeDetails.DurationInTrafficSeconds);
                    }
                    else
                    {
                        currentSchedule.Distance = 0;
                        currentSchedule.Travel = TimeSpan.Zero;
                    }

                    TimeSpan travelToCurrent = currentSchedule.Travel ?? TimeSpan.Zero;

                    if (previousSchedule.Name.Equals("Pull-out")) // Update Pull-out ETA based on the first real stop.
                    {
                        // ETATime = tripToRoute.FromTime - (TimeSpan.FromMinutes(20) + request.PickupTravelTime)
                        previousSchedule.ETA = currentSchedule.Pickup - (TimeSpan.FromMinutes(20) + travelToCurrent);
                        await _scheduleService.UpdateAsync(previousSchedule.Id, previousSchedule);
                    }

                    TimeSpan previousEta = previousSchedule.ETA ?? TimeSpan.Zero;
                    TimeSpan previousServiceTime = TimeSpan.FromMinutes(previousSchedule.On ?? 15);
                    
                    TimeSpan calculatedEta = previousEta + previousServiceTime + travelToCurrent;
              
                    TimeSpan finalEta = calculatedEta;
                    if (currentSchedule.EventType == ScheduleEventType.Pickup && previousSchedule.EventType != ScheduleEventType.Pickup)
                    {
                        TimeSpan? scheduledTime = currentSchedule.Pickup;
                        if (scheduledTime.HasValue)
                        {
                            TimeSpan earlyArrivalWindow = (currentSchedule.TripType == "Return")
                                ? TimeSpan.FromMinutes(5)
                                : TimeSpan.FromMinutes(15);
                            TimeSpan violationLimit = scheduledTime.Value - earlyArrivalWindow;
                            if (calculatedEta < violationLimit)
                            {
                                finalEta = violationLimit;
                            }
                        }
                    }
               
                    currentSchedule.ETA = finalEta;
                    await _scheduleService.UpdateAsync(currentSchedule.Id, currentSchedule);
                }
           
                if (Schedules.Count > 1)
                {
                    var pullInEvent = Schedules.Last();
                    var lastRealStop = Schedules[Schedules.Count - 2]; // The last event BEFORE the Pull-in
              
                    pullInEvent.Sequence = Schedules.Count - 1;
              
                    var finalRouteDetails = await _googleMapsService.GetRouteFullDetails(
                        lastRealStop.ScheduleLatitude, lastRealStop.ScheduleLongitude,
                        pullInEvent.ScheduleLatitude, pullInEvent.ScheduleLongitude);

                    if (finalRouteDetails != null)
                    {
                        pullInEvent.Distance = finalRouteDetails.DistanceMiles;
                        pullInEvent.Travel = TimeSpan.FromSeconds(finalRouteDetails.DurationInTrafficSeconds);
                    }
                    else
                    {
                        pullInEvent.Distance = 0;
                        pullInEvent.Travel = TimeSpan.Zero;
                    }
               
                    TimeSpan lastStopEta = lastRealStop.ETA ?? TimeSpan.Zero;
                    TimeSpan lastStopServiceTime = TimeSpan.FromMinutes(lastRealStop.On ?? 15);
                    TimeSpan travelToPullIn = pullInEvent.Travel ?? TimeSpan.Zero;
                    pullInEvent.ETA = lastStopEta + lastStopServiceTime + travelToPullIn;
              
                    await _scheduleService.UpdateAsync(pullInEvent.Id, pullInEvent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to recalculate schedule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isRecalculating = false;
                IsBusy = false;
            }
        }
        public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo)
        {
            // Called when the entire drag-drop operation has finished.
            // It is useful for cleaning if necessary.
            
        }

        #endregion

        /// <summary>
        /// How many trips are waiting for a route, and how many of those are Will Calls.
        /// </summary>
        /// <remarks>
        /// The Will Call count is not decoration: those are the trips whose hour is not fixed
        /// yet, so they are the part of the backlog that cannot simply be planned in order.
        /// </remarks>
        [ObservableProperty]
        private string _unscheduledSummaryText = "0 trips";

        private void UpdateUnscheduledSummary()
        {
            var total = UnscheduledTrips.Count;
            var willCall = UnscheduledTrips.Count(t => t.WillCall);

            var tripLabel = total == 1 ? "trip" : "trips";
            UnscheduledSummaryText = $"{total} {tripLabel} ({willCall} will call)";
        }

        private void UpdateRouteSummary()
        {
            if (Schedules == null || !Schedules.Any())
            {
                RouteSummaryText = string.Empty;
                return;
            }

            // Calculate the number of unique trips (ignoring Pull-in/out that do not have TripId)
            int tripCount = Schedules.Where(s => s.TripId.HasValue)
                                     .Select(s => s.TripId)
                                     .Distinct()
                                     .Count();

            // Calculate the total distance (handling possible null values ​​in Distance)
            double totalDistance = Schedules.Sum(s => s.Distance ?? 0.0);

            // Format the final text, handling the plural of "trip"
            string tripLabel = (tripCount == 1) ? "trip" : "trips";
            RouteSummaryText = $"{tripCount} {tripLabel}, estimated distance: {totalDistance:N1} miles"; // N1 formatea a 1 decimal
        }

        private async Task ExecuteCancelTripAsync(object parameter)
        {
            
            var tripToCancel = parameter as UnscheduledTripDto;
            if (tripToCancel == null) return;

            var confirmationText = $"Are you sure you want to cancel trip '{tripToCancel.Id}'?";
            var confirmationTitle = "Confirm Cancellation";

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.CancelTripAsync(tripToCancel.Id);
                    MessageBox.Show("Trip canceled successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Reload the list so that the grid is updated with the new state
                    await LoadSchedulesAndTripsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error canceling trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExecuteUncancelTripAsync(object parameter)
        {
            var tripToUncancel = parameter as UnscheduledTripDto;
            if (tripToUncancel == null) return;

            var confirmationText = $"Are you sure you want to restore trip '{tripToUncancel.Id}'?";
            var confirmationTitle = "Confirm Restoration";

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.UncancelTripAsync(tripToUncancel.Id);
                    MessageBox.Show("Trip restored successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                   
                    await LoadSchedulesAndTripsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error restoring trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Moves a trip's Will Call state, in either direction.
        /// </summary>
        /// <remarks>
        /// ⚠️ The only door in the whole application to <c>Trip.WillCall</c>. Which way it
        /// goes is read off the trip itself, so the dispatcher cannot pick the wrong one:
        /// a trip waiting on its patient can only be activated, and one that is not can
        /// only be turned back into a Will Call.
        ///
        /// <para>
        /// Cancelled trips are refused here as well as on the server. The buttons are
        /// hidden for them, but a grid that has not been refreshed still holds rows whose
        /// state moved on.
        /// </para>
        /// </remarks>
        private async Task ExecuteWillCallAsync(object parameter)
        {
            var trip = parameter as UnscheduledTripDto;

            if (trip == null)
                return;

            if (string.Equals(trip.Status, "Canceled", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    LocalizationService.Instance["WillCallCancelledTrip"],
                    LocalizationService.Instance["WillCallActivateTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var dialogViewModel = new WillCallDialogViewModel(trip);
            var dialog = new WillCallDialog { DataContext = dialogViewModel };

            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(dialog, "ScheduleRootDialogHost");

            if (result is not bool confirmed || !confirmed)
                return;

            try
            {
                if (dialogViewModel.IsActivating)
                {
                    await _tripService.ActivateWillCallAsync(trip.Id, dialogViewModel.SelectedFromTime);
                }
                else
                {
                    await _tripService.RevertToWillCallAsync(trip.Id, dialogViewModel.SelectedFromTime);
                }

                await LoadSchedulesAndTripsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    LocalizationService.Instance["WillCallActivateTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task ExecuteEditTripAsync(object parameter)
        {
            var tripToEdit = parameter as UnscheduledTripDto;
            if (tripToEdit == null) return;
           
            var dialogViewModel = new EditTripDialogViewModel(tripToEdit);
            var dialog = new EditTripDialog { DataContext = dialogViewModel };
           
            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(dialog, "ScheduleRootDialogHost");

            if (result is bool wasSaved && wasSaved)
            {
                try
                {
                    var updatedDto = dialogViewModel.GetUpdatedDto();
                    await _tripService.UpdateFromDispatchAsync(tripToEdit.Id, updatedDto);

                    // Reload to see changes
                    await LoadSchedulesAndTripsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #region Cancelled while the dispatcher was looking

        /// <summary>
        /// How long a cancelled row stays on screen before it is taken out of the grid.
        /// </summary>
        /// <remarks>
        /// Deliberately shorter than the ten seconds the live alert lasts
        /// (<c>NotificationToastItemViewModel.AttentionLife</c>): the row has to go while
        /// the alert explaining it is still on screen. The other way round, a dispatcher
        /// who looked up a moment too late would see a row evaporate with nothing left to
        /// say why.
        ///
        /// <para>
        /// ⚠️ The departure storyboard in <c>ScheduleView.xaml</c> is timed to this. Move
        /// one and the other has to move with it.
        /// </para>
        /// </remarks>
        private static readonly TimeSpan DepartureLife = TimeSpan.FromMilliseconds(8200);

        private INotificationService _notifications;

        private readonly List<DispatcherTimer> _departureTimers = new();

        private void AttachNotifications(INotificationService notificationService)
        {
            if (notificationService is null)
                return;

            _notifications = notificationService;

            _notifications.NotificationReceived += OnNotificationReceived;
        }

        #endregion

        #region The board: what the other dispatchers are doing

        private IDispatchBoardService _board;

        /// <summary>
        /// Starts listening to the live board and follows what this screen is looking at.
        /// </summary>
        private void AttachBoard(IDispatchBoardService board)
        {
            if (board is null) return;

            _board = board;

            _board.TripRouted += OnBoardTripRouted;
            _board.TripUnrouted += OnBoardTripUnrouted;
            _board.RouteChanged += OnBoardRouteChanged;
            _board.VehiclePosition += OnBoardVehiclePosition;

            _ = _board.StartAsync();
        }

        /// <summary>
        /// Tells the server which day and which route this screen is showing, so it is only sent
        /// what it can act on.
        /// </summary>
        private void UpdateBoardWatches()
        {
            if (_board is null)
            {
                BoardTrace("no board service attached");
                return;
            }

            BoardTrace($"watching day {SelectedDate:yyyy-MM-dd}, route {SelectedVehicleRoute?.Id.ToString() ?? "<none selected>"}");

            _ = _board.WatchDayAsync(SelectedDate);

            if (SelectedVehicleRoute != null)
                _ = _board.WatchRouteAsync(SelectedVehicleRoute.Id, SelectedDate);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void BoardTrace(string message) =>
            System.Diagnostics.Debug.WriteLine($"[board/vm] {message}");

        /// <summary>
        /// Stops listening. Called when the tab is really closed, for the same reason as
        /// <see cref="ReleaseNotifications"/>: a tab switch is not a close.
        /// </summary>
        private void ReleaseBoard()
        {
            if (_board is null) return;

            _board.TripRouted -= OnBoardTripRouted;
            _board.TripUnrouted -= OnBoardTripUnrouted;
            _board.RouteChanged -= OnBoardRouteChanged;
            _board.VehiclePosition -= OnBoardVehiclePosition;

            _ = _board.StopAsync();
            _board = null;
        }

        private void OnBoardTripRouted(object sender, TripRoutedMessage message)
        {
            if (message is null) return;

            if (message.Date.Date != SelectedDate.Date)
            {
                BoardTrace($"TripRouted ignored: message is for {message.Date:yyyy-MM-dd}, screen shows {SelectedDate:yyyy-MM-dd}");
                return;
            }

            // Another dispatcher took it. The row is an offer to route a trip, and that trip is
            // on a vehicle now, so the offer has to go — quietly and without a reload: nobody
            // needs telling, they need the list to be true.
            OnUiThread(() => RemoveUnscheduledTrip(message.TripId));
        }

        private void OnBoardTripUnrouted(object sender, TripUnroutedMessage message)
        {
            if (message is null || message.Date.Date != SelectedDate.Date) return;

            // It is waiting again, and this screen never had it in the backlog. The only honest
            // way to show it is to ask the server for the day's list.
            OnUiThread(() => _ = ReloadUnscheduledTripsAsync());
        }

        private void OnBoardRouteChanged(object sender, RouteChangedMessage message)
        {
            if (message is null) return;

            if (SelectedVehicleRoute == null || message.VehicleRouteId != SelectedVehicleRoute.Id)
            {
                // Expected and correct: the top grid shows ONE route. A change to a route this
                // dispatcher is not looking at is for whoever has that one open.
                BoardTrace($"RouteChanged ignored: message is for route {message.VehicleRouteId}, screen shows {SelectedVehicleRoute?.Id.ToString() ?? "<none>"}");
                return;
            }

            if (message.Date.Date != SelectedDate.Date)
            {
                BoardTrace($"RouteChanged ignored: message is for {message.Date:yyyy-MM-dd}, screen shows {SelectedDate:yyyy-MM-dd}");
                return;
            }

            // ⚠️ Not while this dispatcher is mid-operation. Reloading the route under someone
            // who is dragging a stop would move the ground beneath them, and their own save is
            // about to arrive anyway.
            if (_isRecalculating || _isDataLoading || IsBusy)
            {
                BoardTrace("RouteChanged deferred: this dispatcher is mid-operation");
                return;
            }

            BoardTrace($"RouteChanged applied: reloading route {message.VehicleRouteId}");
            OnUiThread(() => _ = LoadSchedulesAsync());
        }

        private void OnBoardVehiclePosition(object sender, VehiclePositionMessage message)
        {
            if (message is null) return;
            if (SelectedVehicleRoute == null || message.VehicleRouteId != SelectedVehicleRoute.Id) return;

            OnUiThread(() => ApplyVehiclePosition(message));
        }

        /// <summary>
        /// Runs the action on the interface thread. Hub callbacks arrive on a background one and
        /// everything they touch here is bound to a grid or a map.
        /// </summary>
        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// Takes a trip out of the backlog without a word, for when it is no longer on offer.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="RetireCancelledTrip"/>, which marks the row and lets it
        /// leave with an animation. A cancellation is news the dispatcher has to notice; a trip
        /// routed by a colleague is not, and drawing attention to each one would turn a busy
        /// morning into a light show.
        /// </remarks>
        private void RemoveUnscheduledTrip(int tripId)
        {
            var row = UnscheduledTrips.FirstOrDefault(t => t.Id == tripId);
            if (row is null) return;

            if (SelectedUnscheduledTrip == row)
            {
                SelectedUnscheduledTrip = null;
            }

            UnscheduledTrips.Remove(row);
        }

        /// <summary>
        /// Reloads only the backlog, leaving the route on screen and the map alone.
        /// </summary>
        private async Task ReloadUnscheduledTripsAsync()
        {
            if (_isDataLoading) return;

            try
            {
                var trips = await _scheduleService.GetUnscheduledTripsAsync(SelectedDate);
                var selectedId = SelectedUnscheduledTrip?.Id;

                UnscheduledTrips.ReplaceAll(trips.Where(t => t.IsCanceled != true));

                if (selectedId.HasValue)
                {
                    SelectedUnscheduledTrip =
                        UnscheduledTrips.FirstOrDefault(t => t.Id == selectedId.Value);
                }
            }
            catch (Exception ex)
            {
                // Silent on purpose: this is a background correction, not something the
                // dispatcher asked for. A modal here would interrupt them over nothing.
                System.Diagnostics.Debug.WriteLine($"[board] backlog reload failed: {ex.Message}");
            }
        }

        #endregion

        #region The vehicle on the map

        /// <summary>
        /// Where the vehicle is drawn right now — not where it last reported.
        /// </summary>
        /// <remarks>
        /// The driver's app reports about every thirty seconds. Drawn straight from each report,
        /// the vehicle stands still for half a minute and then teleports, which reads as a
        /// broken map rather than a moving van. These two carry the interpolated position and
        /// are what the marker binds to; <c>DriverLastKnownLocation</c> stays as the last thing
        /// actually reported.
        /// </remarks>
        [ObservableProperty]
        private double _vehicleDisplayLatitude;

        [ObservableProperty]
        private double _vehicleDisplayLongitude;

        /// <summary>Where the vehicle is pointing, in degrees, so the car icon faces its road.</summary>
        [ObservableProperty]
        private double _vehicleHeading;

        [ObservableProperty]
        private bool _hasVehiclePosition;

        // The interpolation in flight.
        private DispatcherTimer _vehicleAnimator;
        private DateTime _vehicleAnimStartedUtc;
        private TimeSpan _vehicleAnimDuration;
        private double _vehicleFromLat, _vehicleFromLng, _vehicleFromHeading;
        private double _vehicleToLat, _vehicleToLng, _vehicleToHeading;
        private DateTime? _lastVehicleReportUtc;

        /// <summary>How long a step is allowed to take when the reports are irregular.</summary>
        private static readonly TimeSpan MinVehicleStep = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MaxVehicleStep = TimeSpan.FromSeconds(35);

        /// <summary>
        /// Movement below this is the GPS talking, not the vehicle: about eleven metres, the
        /// same precision the routing cache rounds coordinates to.
        /// </summary>
        private const double GpsNoiseDegrees = 0.0001;

        private void ApplyVehiclePosition(VehiclePositionMessage message)
        {
            // ⚠️ An unknown heading is not north.
            //
            // The driver's app sends "N/A" when the vehicle is not moving, and the compass
            // converter answers 0 for anything it does not recognise — so a van standing at a
            // patient's door would have swung its arrow to point north every thirty seconds.
            // A vehicle that stops keeps the heading it last drove.
            var heading = IsUnknownDirection(message.Direction)
                ? VehicleHeading
                : HeadingFromDirection(message.Direction);

            // The last thing actually reported, kept apart from the drawn position: the tooltip
            // and the speed colour must say what the driver sent, not where the animation has
            // got to halfway between two reports.
            DriverLastKnownLocation = new GpsDataDto
            {
                IdVehicleRoute = message.VehicleRouteId,
                Latitude = message.Latitude,
                Longitude = message.Longitude,
                Speed = message.Speed,
                Direction = message.Direction,
                DateTime = message.AtUtc.ToLocalTime()
            };

            if (!HasVehiclePosition)
            {
                // First fix of the session: place it, do not fly it in from the middle of nowhere.
                VehicleDisplayLatitude = message.Latitude;
                VehicleDisplayLongitude = message.Longitude;
                VehicleHeading = heading;
                HasVehiclePosition = true;
                _lastVehicleReportUtc = message.AtUtc;
                return;
            }

            // The step lasts as long as the gap between this report and the last one really was.
            // A fixed thirty seconds would make the vehicle sprint whenever a report arrived
            // late and crawl whenever two arrived close together.
            var gap = _lastVehicleReportUtc.HasValue
                ? message.AtUtc - _lastVehicleReportUtc.Value
                : MaxVehicleStep;

            _lastVehicleReportUtc = message.AtUtc;

            if (Math.Abs(message.Latitude - VehicleDisplayLatitude) < GpsNoiseDegrees &&
                Math.Abs(message.Longitude - VehicleDisplayLongitude) < GpsNoiseDegrees)
            {
                // Parked, or drifting on the spot. Animating this would make a stationary
                // vehicle appear to twitch all shift.
                VehicleHeading = heading;
                return;
            }

            _vehicleFromLat = VehicleDisplayLatitude;
            _vehicleFromLng = VehicleDisplayLongitude;
            _vehicleFromHeading = VehicleHeading;

            _vehicleToLat = message.Latitude;
            _vehicleToLng = message.Longitude;
            _vehicleToHeading = ShortestTurnTarget(VehicleHeading, heading);

            _vehicleAnimStartedUtc = DateTime.UtcNow;
            _vehicleAnimDuration = Clamp(gap, MinVehicleStep, MaxVehicleStep);

            StartVehicleAnimator();
        }

        private void StartVehicleAnimator()
        {
            if (_vehicleAnimator == null)
            {
                // Ten frames a second. A vehicle crosses a few pixels in half a minute at any
                // useful zoom, so this is smooth to the eye and a rounding error next to the
                // five-second full refresh it replaces.
                _vehicleAnimator = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _vehicleAnimator.Tick += OnVehicleAnimatorTick;
            }

            _vehicleAnimator.Start();
        }

        private void OnVehicleAnimatorTick(object sender, EventArgs e)
        {
            var elapsed = DateTime.UtcNow - _vehicleAnimStartedUtc;
            var t = _vehicleAnimDuration <= TimeSpan.Zero
                ? 1.0
                : Math.Min(1.0, elapsed.TotalMilliseconds / _vehicleAnimDuration.TotalMilliseconds);

            VehicleDisplayLatitude = _vehicleFromLat + ((_vehicleToLat - _vehicleFromLat) * t);
            VehicleDisplayLongitude = _vehicleFromLng + ((_vehicleToLng - _vehicleFromLng) * t);

            // The turn is given a quarter of the step: a vehicle changes heading at a corner,
            // it does not rotate slowly along a straight road.
            var turn = Math.Min(1.0, t * 4);
            VehicleHeading = _vehicleFromHeading + ((_vehicleToHeading - _vehicleFromHeading) * turn);

            if (t >= 1.0)
            {
                _vehicleAnimator.Stop();

                // Normalised once the turn is over, so the angle does not wander past a full
                // circle over a shift of left turns.
                VehicleHeading = ((VehicleHeading % 360) + 360) % 360;
            }

            InvalidateMapMarkers();
        }

        /// <summary>
        /// The equivalent of <paramref name="target"/> reached by the shorter way round, so a
        /// vehicle correcting ten degrees does not spin three hundred and fifty.
        /// </summary>
        private static double ShortestTurnTarget(double current, double target)
        {
            var difference = ((target - current) % 360 + 540) % 360 - 180;
            return current + difference;
        }

        /// <summary>
        /// Whether the driver's app is saying "I do not know", rather than naming a direction.
        /// </summary>
        private static bool IsUnknownDirection(string direction) =>
            string.IsNullOrWhiteSpace(direction) ||
            string.Equals(direction.Trim(), "N/A", StringComparison.OrdinalIgnoreCase);

        private static double HeadingFromDirection(string direction) =>
            _directionConverter.Convert(direction, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture)
                is double angle
                ? angle
                : 0.0;

        // The same table the map marker already used. Reused rather than repeated: two copies of
        // a compass would eventually disagree.
        private static readonly Converters.DirectionToAngleConverter _directionConverter = new();

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
            value < min ? min : value > max ? max : value;

        private void StopVehicleAnimator()
        {
            if (_vehicleAnimator == null) return;

            _vehicleAnimator.Stop();
            _vehicleAnimator.Tick -= OnVehicleAnimatorTick;
            _vehicleAnimator = null;
        }

        #endregion

        #region Cancelled while the dispatcher was looking (continued)

        /// <summary>
        /// Stops listening. Called when the tab is really closed, never on a tab switch.
        /// </summary>
        /// <remarks>
        /// ⚠️ Not hung off <c>Cleanup</c>/<c>Unloaded</c> on purpose. In a
        /// <see cref="System.Windows.Controls.TabControl"/> those fire when the dispatcher
        /// merely moves to another tab, and a screen that stops hearing about
        /// cancellations after the first tab switch would go back to offering a cancelled
        /// trip as routable — the exact failure this whole path exists to prevent.
        /// </remarks>
        public void ReleaseNotifications()
        {
            if (_notifications is not null)
            {
                _notifications.NotificationReceived -= OnNotificationReceived;
                _notifications = null;
            }

            foreach (var timer in _departureTimers)
                timer.Stop();

            _departureTimers.Clear();

            // The board goes with them, and for the same reason: this runs on a real tab close,
            // never on a tab switch.
            ReleaseBoard();
            StopVehicleAnimator();
        }

        private void OnNotificationReceived(object sender, NotificationDto notification)
        {
            if (notification is null)
                return;

            if (!NotificationKeys.TryGetTripId(notification, out var tripId))
                return;

            var code = notification.BusinessEventCode;

            if (string.Equals(code, NotificationKeys.Events.TripCancelled, StringComparison.OrdinalIgnoreCase))
            {
                // Arrives on the SignalR thread; the grid lives on the interface one.
                OnUiThread(() => RetireCancelledTrip(tripId));
                return;
            }

            // ===== The driver's flags =====
            //
            // ⚠️ No new notifications were invented for these. The three events already exist,
            // are already published, and already reach this application over the inbox hub —
            // they were simply not being listened to here. Adding events for them would have
            // meant a second copy of the same fact and a retention policy to go with it.
            //
            // The route grid shows the driver's progress: a stop turns green when they arrive
            // and purple when they perform it. Until now that only appeared after a reload, so
            // a dispatcher watching a route saw nothing happen for minutes at a time.
            if (string.Equals(code, NotificationKeys.Events.DriverArrivedPickup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, NotificationKeys.Events.DriverPickedUpPassenger, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, NotificationKeys.Events.DriverCompletedTrip, StringComparison.OrdinalIgnoreCase))
            {
                OnUiThread(() => RefreshRouteForTrip(tripId));
            }
        }

        /// <summary>
        /// Reloads the route on screen when its driver has just reached or performed a stop.
        /// </summary>
        /// <remarks>
        /// The route is refetched rather than patched in place, deliberately. The message
        /// carries the trip and nothing else — no hour, no distance, no odometer — and those
        /// columns are read as measured facts. Writing a guess into them would be worse than
        /// showing nothing, so the one true source is asked. It is a single indexed query, and
        /// it only happens when the driver of the route this dispatcher is watching does
        /// something: a handful of times an hour, not on a timer.
        ///
        /// A message about any other route belongs to whoever has that one open.
        /// </remarks>
        private void RefreshRouteForTrip(int tripId)
        {
            if (Schedules.All(s => s.TripId != tripId)) return;
            if (_isRecalculating || _isDataLoading || IsBusy) return;

            _ = LoadSchedulesAsync();
        }

        /// <summary>
        /// Takes a trip that was just cancelled somewhere else out of the open-trips grid,
        /// slowly enough for the dispatcher to see which one left.
        /// </summary>
        /// <remarks>
        /// A trip can be cancelled from the driver's app, the patient's, the Booking
        /// Portal, an integrator or the bot. Until this existed, none of that reached a
        /// dispatcher with the tab already open: the row stayed, and the row is an offer
        /// to route a trip that no longer exists.
        /// </remarks>
        private void RetireCancelledTrip(int tripId)
        {
            var row = UnscheduledTrips.FirstOrDefault(t => t.Id == tripId);

            // Not on this date, or already routed, or already gone. Nothing to do.
            if (row is null || row.IsDeparting)
                return;

            MarkCancelled(row);

            // Nobody can act on a row they cannot see. Asked for before the animation
            // starts so the grid is already looking at it when the row lights up.
            ScrollUnscheduledTripIntoViewRequest?.Invoke(
                this,
                new UnscheduledTripEventArgs(row));

            row.IsDeparting = true;

            // A timer rather than an awaited delay: it ticks on the interface thread,
            // which is the only one allowed to touch the collection. It also runs whether
            // or not this tab is the one on screen, so a hidden tab still ends up true.
            var timer = new DispatcherTimer { Interval = DepartureLife };

            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _departureTimers.Remove(timer);
                RemoveDepartedTrip(row);
            };

            _departureTimers.Add(timer);

            timer.Start();
        }

        /// <summary>
        /// Puts the cancellation on the row itself.
        /// </summary>
        /// <remarks>
        /// This alone turns the row red, kills its schedule button and hides both Will
        /// Call buttons: the grid was already built to render a cancelled trip, it just
        /// never received one. See the row style and the button triggers in
        /// <c>ScheduleView.xaml</c>.
        /// </remarks>
        private static void MarkCancelled(UnscheduledTripDto trip)
        {
            trip.Status = TripStatus.Canceled;
            trip.IsCanceled = true;
        }

        private void RemoveDepartedTrip(UnscheduledTripDto trip)
        {
            // Routing reads the selection, not the clicked row, so a departing row left
            // selected would still be what the schedule button acts on.
            if (ReferenceEquals(SelectedUnscheduledTrip, trip))
                SelectedUnscheduledTrip = null;

            UnscheduledTrips.Remove(trip);
        }

        #endregion


        #region Translation

        // Schedules Grid
        public string ActivateWillCallToolTip => LocalizationService.Instance["WillCallActivateToolTip"];

        public string RevertWillCallToolTip => LocalizationService.Instance["WillCallRevertToolTip"];

        public string ColumnHeaderName => LocalizationService.Instance["Name"];
        public string ColumnHeaderPickup => LocalizationService.Instance["Pickup"];
        public string ColumnHeaderAppt => LocalizationService.Instance["Appt"];
        public string ColumnHeaderETA => LocalizationService.Instance["ETA"];
        public string ColumnHeaderDistance => LocalizationService.Instance["Distance"];
        public string ColumnHeaderTravel => LocalizationService.Instance["Travel"];
        public string ColumnHeaderOn => LocalizationService.Instance["On"];
        public string ColumnHeaderSpace => LocalizationService.Instance["Space"];
        public string ColumnHeaderAddress => LocalizationService.Instance["Address"];
        public string ColumnHeaderComment => LocalizationService.Instance["Comment"];
        public string ColumnHeaderPhone => LocalizationService.Instance["Phone"];
        public string ColumnHeaderArrive => LocalizationService.Instance["Arrive"];
        public string ColumnHeaderPerform => LocalizationService.Instance["Perform"];
        public string ColumnHeaderArriveDist => LocalizationService.Instance["ArriveDist"];
        public string ColumnHeaderPerformDist => LocalizationService.Instance["PerformDist"];
        public string ColumnHeaderDriver => LocalizationService.Instance["Driver"];
        public string ColumnHeaderGPSArrive => LocalizationService.Instance["GPSArrive"];
        public string ColumnHeaderOdometer => LocalizationService.Instance["Odometer"];
        public string ColumnHeaderAuthNo => LocalizationService.Instance["AuthNo"];
        public string ColumnHeaderFundingSource => LocalizationService.Instance["FundingSource"];

        // Actions
        public string UnscheduleToolTip => LocalizationService.Instance["Unschedule"];
        public string SelectFieldsToDisplayToolTip => LocalizationService.Instance["SelectFieldsToDisplay"];

        // Unscheduled Trips
        public string ColumnHeaderDate => LocalizationService.Instance["Date"];
        public string ColumnHeaderFromTime => LocalizationService.Instance["FromTime"];
        public string ColumnHeaderToTime => LocalizationService.Instance["ToTime"];
        public string ColumnHeaderNotificationStatus => LocalizationService.Instance["NotificationStatus"];
        public string ColumnHeaderPatient => LocalizationService.Instance["Patient"]; // Customer
        public string ColumnHeaderPickupAddress => LocalizationService.Instance["PickupAddress"];
        public string ColumnHeaderDropoffAddress => LocalizationService.Instance["DropoffAddress"];
        public string ColumnHeaderCharge => LocalizationService.Instance["Charge"];
        public string ColumnHeaderPaid => LocalizationService.Instance["Paid"];
        //public string ColumnHeaderSpace => LocalizationService.Instance["Space"];
        public string ColumnHeaderPickupComment => LocalizationService.Instance["PickupComment"];
        public string ColumnHeaderDropoffComment => LocalizationService.Instance["DropoffComment"];
        public string ColumnHeaderType => LocalizationService.Instance["Type"];
        //public string ColumnHeaderPickup => LocalizationService.Instance["Pickup"];
        public string ColumnHeaderDropoff => LocalizationService.Instance["Dropoff"];
        public string ColumnHeaderPickupPhone => LocalizationService.Instance["PickupPhone"];
        public string ColumnHeaderDropoffPhone => LocalizationService.Instance["DropoffPhone"];
        public string ColumnHeaderTripId => LocalizationService.Instance["TripId"];
        public string ColumnHeaderAuthorization => LocalizationService.Instance["Authorization"];
        //public string ColumnHeaderFundingSource => LocalizationService.Instance["FundingSource"];
        //public string ColumnHeaderDistance => LocalizationService.Instance["Distance"];
        public string ColumnHeaderPickupCity => LocalizationService.Instance["PickupCity"];
        public string ColumnHeaderDropoffCity => LocalizationService.Instance["DropoffCity"];

        // A trip cancelled somewhere else while this tab was open
        public string TripCancelledRowLabel => LocalizationService.Instance["TripCancelledRowLabel"];

        // Actions
        public string CancelTripToolTip => LocalizationService.Instance["CancelTrip"];
        public string EditTripToolTip => LocalizationService.Instance["Edit"]; 
        public string ScheduleTripToolTip => LocalizationService.Instance["ScheduleTrip"];


        // MSSG
        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"];
        public string TripRoutingError => LocalizationService.Instance["TripRoutingError"]; // Trip routing error
        public string CancelScheduleError => LocalizationService.Instance["CancelScheduleError"]; // Error canceling Schedule

        #endregion
    }
}
