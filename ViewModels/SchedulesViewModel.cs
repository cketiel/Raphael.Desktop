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

        // Lista privada para mantener todas las rutas cargadas inicialmente
        private List<VehicleRoute> _allVehicleRoutesMaster = new();
        public ObservableCollection<VehicleRoute> VehicleRoutes { get; } = new();
        public ObservableCollection<VehicleGroup> VehicleGroups { get; } = new();
        public ObservableCollection<ScheduleDto> Schedules { get; } = new();
        public ObservableCollection<UnscheduledTripDto> UnscheduledTrips { get; } = new();

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
            _googleMapsService = new GoogleMapsService();
            _gpsService = new GpsService();

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

            InitializeColumns();
            //_ = InitializeAsync();

            AttachNotifications(notificationService);
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
                UserConfigService _userConfigService = new UserConfigService();
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
                UserConfigService _userConfigService = new UserConfigService();
                _userConfigService.SaveColumnConfig(ColumnConfigurations);*/
            }
        }
        private void InitializeColumns()
        {
            // Try to load user saved settings
            UserConfigService _userConfigService = new UserConfigService();
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
            Schedules.Clear();

            // Aseguramos que el Master esté ordenado por secuencia antes de filtrar
            var orderedMaster = _masterSchedules.OrderBy(s => s.Sequence).ToList();

            foreach (var schedule in orderedMaster)
            {
                // Regla: Siempre mostrar Pull-out, Pull-in y lo que no esté realizado.
                // Si DisplayPerformedEvents es true, mostramos todo.
                bool isServiceEvent = schedule.Name == "Pull-out" || schedule.Name == "Pull-in";

                if (DisplayPerformedEvents || !schedule.Performed || isServiceEvent)
                {
                    Schedules.Add(schedule);
                }
            }

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

                // We recalculate
                await RecalculateScheduleAsync(startIndex);

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
            RunService _runService = new RunService();
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
            VehicleGroupService _vehicleGroupService = new VehicleGroupService();
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

            VehicleRoutes.Clear();
            foreach (var route in filtered) VehicleRoutes.Add(route);

            // Mantener selección si es posible
            SelectedVehicleRoute = VehicleRoutes.FirstOrDefault(r => r.Id == (previousSelected?.Id ?? -1))
                                   ?? VehicleRoutes.FirstOrDefault();
        }
        private async Task LoadInitialDataListsAsyncOld()
        {
            // Este método solo carga las listas de ComboBox, sin seleccionar nada.
            RunService _runService = new RunService();
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

            VehicleGroupService _vehicleGroupService = new VehicleGroupService();
            var groups = await _vehicleGroupService.GetGroupsAsync();
            VehicleGroups.Clear();
            foreach (var g in groups)
            {
                VehicleGroups.Add(g);
            }
        }

        private async Task LoadInitialDataAsync()
        {
            RunService _runService = new RunService();
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

            VehicleGroupService _vehicleGroupService = new VehicleGroupService();
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

            try
            {
                // 1. Limpiar colecciones
                _masterSchedules.Clear();
                UnscheduledTrips.Clear();
                SelectedUnscheduledTripPoints.Clear();
                SelectedUnscheduledTrip = null;

                // 2. Obtener datos del servidor
                var schedulesTask = _scheduleService.GetSchedulesAsync(SelectedVehicleRoute.Id, SelectedDate);
                var tripsTask = _scheduleService.GetUnscheduledTripsAsync(SelectedDate);

                // Ejecutamos ambas peticiones en paralelo para mayor velocidad
                await Task.WhenAll(schedulesTask, tripsTask);

                var schedules = await schedulesTask;
                var trips = await tripsTask;

                // 3. Llenar Master y filtrar Schedules
                _masterSchedules.AddRange(schedules);
                FilterSchedules();

                // 4. Llenar UnscheduledTrips
                foreach (var source in trips)
                {
                    if(source.IsCanceled != true)
                        UnscheduledTrips.Add(source);
                }

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
                _isDataLoading = false; // Liberar el bloqueo
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

                var pickupDetails = await _googleMapsService.GetRouteFullDetails(originLat, originLng, tripToSchedule.PickupLatitude, tripToSchedule.PickupLongitude);
                if (pickupDetails == null) throw new Exception("Could not calculate the route to the pickup point.");

                double pDistance = pickupDetails.DistanceMiles;
                TimeSpan pTravelTime = TimeSpan.FromSeconds(pickupDetails.DurationInTrafficSeconds);
                TimeSpan pCalculatedEta = previousEta + previousServiceTime + pTravelTime;
                TimeSpan pFinalEta = pCalculatedEta;
                if (previousSchedule == null || previousSchedule.EventType != ScheduleEventType.Pickup)
                {
                    TimeSpan pViolationLimit = (tripToSchedule.FromTime ?? TimeSpan.Zero) - TimeSpan.FromMinutes(15);
                    if (pCalculatedEta < pViolationLimit) pFinalEta = pViolationLimit;
                }

                var dropoffDetails = await _googleMapsService.GetRouteFullDetails(tripToSchedule.PickupLatitude, tripToSchedule.PickupLongitude, tripToSchedule.DropoffLatitude, tripToSchedule.DropoffLongitude);
                if (dropoffDetails == null) throw new Exception("Could not calculate the route to the dropoff point.");

                double dDistance = dropoffDetails.DistanceMiles;
                TimeSpan dTravelTime = TimeSpan.FromSeconds(dropoffDetails.DurationInTrafficSeconds);
                TimeSpan pickupServiceTime = TimeSpan.FromMinutes(15);
                TimeSpan dFinalEta = pFinalEta + pickupServiceTime + dTravelTime;

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

                // eliminar localmente el viaje ruteado
                var tripToRemove = UnscheduledTrips.FirstOrDefault(t => t.Id == SelectedUnscheduledTrip.Id);
                if (tripToRemove != null)
                {
                    UnscheduledTrips.Remove(tripToRemove);
                    // Al ser ObservableCollection, el Grid de viajes se actualiza solo aquí
                }
                SelectedUnscheduledTrip = null;

                BusyMessage = "Sending notification to Member...";
                HttpClient client = new HttpClient();
                ApiZonitelService _apiZonitelService = new ApiZonitelService(client, App.Configuration);

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

                try
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
                }

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
            // Only reload if the VM has already been fully initialized.
            if (IsInitialized && CanLoadSchedulesAndTrips())
                _ = LoadSchedulesAndTripsAsync();

            /*if (CanLoadSchedulesAndTrips())
                LoadSchedulesAndTripsCommand.Execute(null);*/
        }

        partial void OnSelectedVehicleRouteChanged(VehicleRoute value)
        {
            // Only reload if the VM has already been fully initialized.
            if (IsInitialized && CanLoadSchedulesAndTrips())
                _ = LoadSchedulesAsync(); // no es necesario volver a cargar los viajes


            /*if (CanLoadSchedulesAndTrips())
                LoadSchedulesAndTripsCommand.Execute(null);*/
        }

        // Logic to execute when the selection in the Schedules grid changes
        partial void OnSelectedScheduleChanged(ScheduleDto value)
        {          
            foreach (var schedule in Schedules)
            {
                schedule.IsSelectedForMap = false;
            }
           
            if (value != null)
            {
                //SelectedUnscheduledTrip = null; // This will clear the markers for the unscheduled trip

                value.IsSelectedForMap = true;
                var pairedEvent = Schedules.FirstOrDefault(s => s.TripId == value.TripId && s.Id != value.Id);
                if (pairedEvent != null)
                {
                    pairedEvent.IsSelectedForMap = true;
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

        public void ForceRefreshSchedules()
        {
            var items = new List<ScheduleDto>(Schedules);
            Schedules.Clear();
            foreach (var item in items)
            {
                Schedules.Add(item);
            }
          
            FilterSchedules();
        }

        private void CalculateVisualOffsets()
        {         
            foreach (var schedule in Schedules)
            {
                schedule.VisualOffsetIndex = 0;
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
                    scheduleInGroup.VisualOffsetIndex = index;
                    index++;
                }
            }
        }

        #region Drag and Drop Implementation

        // --- IDragSource: Controls the start of the drag ---

        public void StartDrag(IDragInfo dragInfo)
        {
            
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
           
        }

        public void DragCancelled()
        {
            // Called if the drag is canceled (e.g. by pressing ESC).
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

            // 2. A "Dropoff" cannot go BEFORE its corresponding "Pickup".
            if (sourceItem.EventType == ScheduleEventType.Dropoff)
            {
                // Find the Pickup for this trip
                var pickupItem = Schedules.FirstOrDefault(s => s.TripId == sourceItem.TripId && s.EventType == ScheduleEventType.Pickup);
                if (pickupItem != null)
                {
                    int pickupIndex = Schedules.IndexOf(pickupItem);
                    // If the index where you want to drop is less than or equal to that of the pickup, it is not valid.
                    if (dropInfo.InsertIndex <= pickupIndex)
                    {
                        dropInfo.Effects = DragDropEffects.None;
                        return;
                    }
                }
            }


            // 3. A "Pickup" cannot go AFTER its corresponding "Dropoff".
            if (sourceItem.EventType == ScheduleEventType.Pickup)
            {
                // Find the Dropoff for this trip
                var dropoffItem = Schedules.FirstOrDefault(s => s.TripId == sourceItem.TripId && s.EventType == ScheduleEventType.Dropoff);
                if (dropoffItem != null)
                {
                    int dropoffIndex = Schedules.IndexOf(dropoffItem);
                    // If the index where you want to drop is greater than or equal to the dropoff, it is not valid.
                    // dropInfo.InsertIndex gives us the position *before* which it will be inserted.
                    // If we move an element from a low index to a high index, the dropoff index may change.
                    // Therefore, the simple condition is the most effective.
                    if (dropInfo.InsertIndex >= dropoffIndex)
                    {
                        dropInfo.Effects = DragDropEffects.None;
                        return;
                    }
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
                // Actualizamos las secuencias según el nuevo orden visual
                for (int i = 0; i < Schedules.Count; i++)
                {
                    Schedules[i].Sequence = i;
                    // Enviamos la actualización de la secuencia a la DB inmediatamente
                    await _scheduleService.UpdateAsync(Schedules[i].Id, Schedules[i]);
                }

                // Ahora ejecutamos el cálculo de ETAs basado en este nuevo orden
                await RecalculateScheduleAsync(0);

                // Finalmente refrescamos para asegurar que todo esté sincronizado
                await LoadSchedulesAndTripsAsync();
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

        private async Task RecalculateScheduleAsync(int startIndex)
        {
            // Usamos Schedules (la lista visual ordenada) para el cálculo
            if (_isRecalculating || Schedules.Count <= 2) return;

            try
            {
                _isRecalculating = true;

                for (int i = 1; i < Schedules.Count; i++)
                {
                    var current = Schedules[i];

                    // BUSCAR EL PREVIO VÁLIDO (Lógica No-Show)
                    ScheduleDto validPrevious = null;
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var p = Schedules[j];
                        // Un punto es válido si: No está cancelado O (está cancelado pero el Driver llegó/Arrive)
                        bool isPhysicalStop = p.Status != "Canceled" || p.Arrive != null;
                        if (isPhysicalStop)
                        {
                            validPrevious = p;
                            break;
                        }
                    }
                    if (validPrevious == null) validPrevious = Schedules[0];

                    // ASIGNAR SECUENCIA
                    current.Sequence = i;

                    // LÓGICA DE CÁLCULO
                    if (current.Status == "Canceled" && current.Arrive == null)
                    {
                        // Es un viaje cancelado al que no se fue: distancia 0
                        current.Distance = 0;
                        current.Travel = TimeSpan.Zero;
                        current.ETA = validPrevious.ETA;
                    }
                    else
                    {
                        if (!current.Performed)
                        {
                            var routeDetails = await _googleMapsService.GetRouteFullDetails(
                                validPrevious.ScheduleLatitude, validPrevious.ScheduleLongitude,
                                current.ScheduleLatitude, current.ScheduleLongitude);

                            if (routeDetails != null)
                            {
                                current.Distance = routeDetails.DistanceMiles;
                                current.Travel = TimeSpan.FromSeconds(routeDetails.DurationInTrafficSeconds);
                            }

                            
                            TimeSpan travelToCurrent = current.Travel ?? TimeSpan.Zero;
                            if (validPrevious.Name.Equals("Pull-out"))
                            {
                                validPrevious.ETA = current.Pickup - (TimeSpan.FromMinutes(20) + travelToCurrent);
                                await _scheduleService.UpdateAsync(validPrevious.Id, validPrevious);
                            }

                            // CÁLCULO DE ETA SIGUIENTE
                            TimeSpan prevEta = validPrevious.ETA ?? TimeSpan.Zero;
                            TimeSpan prevService = (validPrevious.Name == "Pull-out") ? TimeSpan.Zero : TimeSpan.FromMinutes(validPrevious.On ?? 15);
                            TimeSpan calculatedEta = prevEta + prevService + travelToCurrent;

                            // Violaciones de tiempo
                            if (current.EventType == ScheduleEventType.Pickup && current.Pickup.HasValue)
                            {
                                TimeSpan margin = (current.TripType == "Return") ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15);
                                if (calculatedEta < (current.Pickup.Value - margin)) calculatedEta = current.Pickup.Value - margin;
                            }
                            current.ETA = calculatedEta;
                        }
                    }

                    // GUARDAR EN DB
                    await _scheduleService.UpdateAsync(current.Id, current);
                }
            }
            finally { _isRecalculating = false; }
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
        }

        private void OnNotificationReceived(object sender, NotificationDto notification)
        {
            if (notification is null)
                return;

            if (!string.Equals(
                    notification.BusinessEventCode,
                    NotificationKeys.Events.TripCancelled,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!NotificationKeys.TryGetTripId(notification, out var tripId))
                return;

            // Arrives on the SignalR thread; the grid lives on the interface one.
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null)
                return;

            if (dispatcher.CheckAccess())
                RetireCancelledTrip(tripId);
            else
                dispatcher.BeginInvoke(new Action(() => RetireCancelledTrip(tripId)));
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
