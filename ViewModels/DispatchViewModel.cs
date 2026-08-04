using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Spreadsheet;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Dispatch;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Views.Schedules;

using GMap.NET;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;


namespace Raphael.Desktop.ViewModels
{

    public partial class DispatchViewModel : ObservableObject
    {
        private readonly ScheduleService _scheduleService;
        private readonly TripService _tripService;
        private readonly GoogleMapsService _googleMapsService;

        private DispatcherTimer _overviewLiveUpdateTimer;

        private readonly GpsService _gpsService = new GpsService();

        private ObservableCollection<VehicleRouteViewModel> _allVehicleRoutes;
        /// <summary>
        /// Event that is fired to request the view to zoom the map.
        /// </summary>
        public event EventHandler<ZoomAndCenterEventArgs> ZoomAndCenterRequest;

        //A flag to control that the auto-zoom is only done the first time.
        private bool _isInitialZoomDone = false;


        #region Overview Map Properties

        // Properties to control the center and zoom of the Overview map
        private PointLatLng _overviewMapCenter = new PointLatLng(26.6166, -81.8333); // Fort Myers
        public PointLatLng OverviewMapCenter
        {
            get => _overviewMapCenter;
            set => SetProperty(ref _overviewMapCenter, value);
        }

        private int _overviewMapZoom = 12;
        public int OverviewMapZoom
        {
            get => _overviewMapZoom;
            set => SetProperty(ref _overviewMapZoom, value);
        }

        #endregion

        #region Main Dispatch Properties

        private ObservableCollection<RunItemViewModel> _runs;
        public ObservableCollection<RunItemViewModel> Runs
        {
            get => _runs;
            set => SetProperty(ref _runs, value);
        }

        private ObservableCollection<TripItemViewModel> _allUnscheduledTripsFromService; // All unscheduled service trips
        private ObservableCollection<TripItemViewModel> _unscheduledTrips;
        private UnscheduledTripDto _selectedUnscheduledTrip;
        public ObservableCollection<TripItemViewModel> UnscheduledTrips
        {
            get => _unscheduledTrips;
            set => SetProperty(ref _unscheduledTrips, value);
        }

        public UnscheduledTripDto SelectedUnscheduledTrip
        {
            get => _selectedUnscheduledTrip;
            set => SetProperty(ref _selectedUnscheduledTrip, value);
        }

        private bool _displayWillCalls;
        public bool DisplayWillCalls
        {
            get => _displayWillCalls;
            set
            {
                if (SetProperty(ref _displayWillCalls, value))
                {
                    FilterUnscheduledTrips();
                }
            }
        }

        #endregion

        #region Visibility and State Properties

        private DispatchViewMode _currentViewMode;
        public DispatchViewMode CurrentViewMode
        {
            get => _currentViewMode;
            set => SetProperty(ref _currentViewMode, value);
        }

        /*private bool _isOverviewVisible;
        public bool IsOverviewVisible
        {
            get => _isOverviewVisible;
            set => SetProperty(ref _isOverviewVisible, value);
        }*/

        private DateTime _currentDispatchDate;
        public DateTime CurrentDispatchDate
        {
            get => _currentDispatchDate;
            set
            {
                if (SetProperty(ref _currentDispatchDate, value))
                {
                    // When the date changes, we reload all the data
                    LoadAllDataAsync();
                }
            }
        }

        #endregion

        #region Properties View Overview

        private List<ScheduleDto> _masterAllEvents;
        private ObservableCollection<ScheduleDto> _allEvents;
        public ObservableCollection<ScheduleDto> AllEvents
        {
            get => _allEvents;
            set => SetProperty(ref _allEvents, value);
        }

        // Filters for the "All Events" grid
        private bool _showPerformedEvents;
        public bool ShowPerformedEvents { get => _showPerformedEvents; set { if (SetProperty(ref _showPerformedEvents, value)) FilterAllEvents(); } }

        private bool _showPullEvents;
        public bool ShowPullEvents { get => _showPullEvents; set { if (SetProperty(ref _showPullEvents, value)) FilterAllEvents(); } }

        private bool _showLatePickups;
        public bool ShowLatePickups { get => _showLatePickups; set { if (SetProperty(ref _showLatePickups, value)) FilterAllEvents(); } }

        // Properties for the information band
        [ObservableProperty]
        private int _totalVehicles;
        //public int TotalVehicles { get; set; } // = 24;
        public int ClearVehicles { get; set; } // = 11;
        public int LoadedVehicles { get; set; }  // = 5;
        public int EnRouteVehicles { get; set; } //  = 2;
        public int OnBreakVehicles { get; set; } // = 0;
        public int OfflineVehicles { get; set; } // = 3;

        private string _eventsSummaryText;
        public string EventsSummaryText { get => _eventsSummaryText; set => SetProperty(ref _eventsSummaryText, value); }

        #endregion

        #region Navigation Properties

        private SchedulesViewModel _currentScheduleViewModel;
        public SchedulesViewModel CurrentScheduleViewModel
        {
            get => _currentScheduleViewModel;
            set => SetProperty(ref _currentScheduleViewModel, value);
        }

        // La propiedad IsOverviewVisible ya existe, la reutilizaremos para ocultar la vista principal
        // y en su lugar mostraremos la vista de Schedule. 
        // Para que el nombre tenga más sentido, podrías renombrarla a "IsDetailViewVisible".
        // Por ahora, la usaremos tal cual.

        // Propiedad para el item seleccionado en el grid de Runs
        private RunItemViewModel _selectedRun;
        public RunItemViewModel SelectedRun
        {
            get => _selectedRun;
            set => SetProperty(ref _selectedRun, value);
        }

        #endregion

        #region Navigation Commands
        public ICommand ShowScheduleViewCommand { get; private set; }
        public ICommand ShowMainViewCommand { get; private set; }
        //public ICommand HideScheduleViewCommand { get; private set; }
        #endregion

        #region Loading Indicator
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        #endregion

        #region Commands
        public ICommand OverviewCommand { get; private set; }
        public ICommand ExportScheduleCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand OpenColumnSelectorCommand { get; }
        public ICommand ShowDispatchMainCommand { get; private set; }
        public ICommand CancelTripCommand { get; private set; }
        public ICommand EditTripCommand { get; private set; }
        public ICommand UncancelTripCommand { get; private set; }

        public ICommand ShowHistoryCommand { get; private set; }

        #endregion

        public DispatchViewModel()
        {
            _scheduleService = new ScheduleService();
            _googleMapsService = new GoogleMapsService();
            _tripService = new TripService();

            _allVehicleRoutes = new ObservableCollection<VehicleRouteViewModel>();// new ObservableCollection<VehicleRoute>(); 

            // Dispatch Principal
            Runs = new ObservableCollection<RunItemViewModel>();
            _allUnscheduledTripsFromService = new ObservableCollection<TripItemViewModel>();
            UnscheduledTrips = new ObservableCollection<TripItemViewModel>();

            // Overview
            _masterAllEvents = new List<ScheduleDto>();
            AllEvents = new ObservableCollection<ScheduleDto>();

            //CurrentDispatchDate = DateTime.Today;
            _currentDispatchDate = DateTime.Today; 
            OnPropertyChanged(nameof(CurrentDispatchDate));

            // Initialize commands

            ExportScheduleCommand = new AsyncRelayCommand(ExecuteExportScheduleAsync);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);          
            CancelTripCommand = new AsyncRelayCommand(ExecuteCancelTripAsync);
            EditTripCommand = new AsyncRelayCommand(ExecuteEditTripAsync);
            UncancelTripCommand = new AsyncRelayCommand(ExecuteUncancelTripAsync);

            OverviewCommand = new RelayCommandObject(ExecuteShowOverview);
            ShowDispatchMainCommand = new RelayCommandObject(ExecuteShowDispatchMain); 
            ShowScheduleViewCommand = new RelayCommandObject(ExecuteShowScheduleView, CanExecuteShowScheduleView);
            ShowMainViewCommand = new RelayCommandObject(ExecuteShowMainView);

            ShowHistoryCommand = new AsyncRelayCommand(ExecuteShowHistoryAsync);

            //CancelTripCommand = new AsyncRelayCommand<TripItemViewModel>(ExecuteCancelTripAsync);
            //EditTripCommand = new AsyncRelayCommand<TripItemViewModel>(ExecuteEditTripAsync);

            /*OverviewCommand = new AsyncRelayCommand(ExecuteOverviewAsync); // Use AsyncRelayCommand for asynchronous operations
            ExportScheduleCommand = new AsyncRelayCommand(ExecuteExportScheduleAsync);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);
            ShowDispatchMainCommand = new RelayCommand(ExecuteShowDispatchMain);*/

            // Initial data loading
            //LoadAllDataAsync();

            _ = InitializeDataAsync();
        }

        private async Task ExecuteShowHistoryAsync(object parameter)
        {
            TripReadDto tripToView = null;

            // En esta vista, el parámetro suele ser TripItemViewModel
            if (parameter is TripItemViewModel itemViewModel)
            {
                //tripToView = itemViewModel.TripDto;
                tripToView = new TripReadDto();
                tripToView.Id = itemViewModel.TripDto.Id;
                tripToView.PickupAddress = itemViewModel.TripDto.PickupAddress;
                tripToView.DropoffAddress = itemViewModel.TripDto.DropoffAddress;
                tripToView.CustomerName = itemViewModel.TripDto.CustomerName;
            }
            else if (parameter is TripReadDto dto)
            {
                tripToView = dto;
            }

            if (tripToView == null) return;

            // Crear el ViewModel del historial con el DTO del viaje
            var viewModel = new TripHistoryViewModel(tripToView);

            var view = new Views.TripHistoryDialog
            {
                DataContext = viewModel
            };

            // Usamos "RootDialogHost" que es el identificador en DispatchView.xaml
            await MaterialDesignThemes.Wpf.DialogHost.Show(view, "RootDialogHost");
        }

        private async Task InitializeDataAsync()
        {
            try
            {
                await LoadAllDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inicializando datos: {ex.Message}");
            }
        }

        private void StartOverviewLiveTracking()
        {         
            if (_overviewLiveUpdateTimer != null && _overviewLiveUpdateTimer.IsEnabled) return;

            _overviewLiveUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) 
            };
            _overviewLiveUpdateTimer.Tick += async (s, e) => await UpdateAllRunsLocationAsync();
            _overviewLiveUpdateTimer.Start();
          
            _ = UpdateAllRunsLocationAsync();
        }

        private void StopOverviewLiveTracking()
        {
            _overviewLiveUpdateTimer?.Stop();
            _overviewLiveUpdateTimer = null;
            _isInitialZoomDone = false;
        }

        private async Task UpdateAllRunsLocationAsync()
        {
            if (Runs == null || !Runs.Any()) return;

            double defaultLat = 26.6166;
            double defaultLng = -81.8333;

            // We make a COPY of the list to iterate. 
            // This prevents the "Collection was modified" error if the user hits Refresh
            // just while this method was executed by the Timer.
            var runsSnapshot = Runs.ToList();

            // We create a task list to obtain the location of all runs in parallel
            var updateTasks = runsSnapshot.Select(async run =>
            {
                try
                {
                    var gpsData = await _gpsService.GetLatestGpsDataAsync(run.VehicleRoute.Id);
                    // We update the property in the RunItemViewModel.
                    // The UI will automatically react to this change.
                    //run.LastKnownLocation = gpsData;

                    if (gpsData != null && gpsData.Latitude != 0 && gpsData.Longitude != 0)
                    {
                        // CASE 1: We have real GPS data
                        run.LastKnownLocation = gpsData;
                    }
                    else
                    {
                        // CASE 2: The API returned null (no signal/data).
                        // FIX: Using the Garage location as a fallback
                        // so that the vehicle DOES NOT disappear from the map.

                        double garageLat = (run.VehicleRoute.Model?.GarageLatitude != 0)
                                    ? run.VehicleRoute.Model.GarageLatitude
                                    : defaultLat;

                        double garageLng = (run.VehicleRoute.Model?.GarageLongitude != 0)
                                            ? run.VehicleRoute.Model.GarageLongitude
                                            : defaultLng;

                        run.LastKnownLocation = new GpsDataDto
                        {
                            IdVehicleRoute = run.VehicleRoute.Id,
                            Latitude = garageLat,
                            Longitude = garageLng,
                            Direction = "N/A", // This will activate your red Stop icon
                            Speed = 0,
                            DateTime = DateTime.Now,
                            Address = "No GPS Signal - At Garage"
                        };
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating location for run {run.Name}: {ex.Message}");
                    //run.LastKnownLocation = null; 

                    // CASE 3: Call error (timeout, server error).
                    // We keep the last known location if it exists, or use the garage fallback
                    if (run.LastKnownLocation == null)
                    {
                        double garageLat = (run.VehicleRoute.Model?.GarageLatitude != 0)
                                    ? run.VehicleRoute.Model.GarageLatitude
                                    : defaultLat;

                        double garageLng = (run.VehicleRoute.Model?.GarageLongitude != 0)
                                            ? run.VehicleRoute.Model.GarageLongitude
                                            : defaultLng;

                        run.LastKnownLocation = new GpsDataDto
                        {
                            IdVehicleRoute = run.VehicleRoute.Id,
                            Latitude = garageLat,
                            Longitude = garageLng,
                            Direction = "N/A",
                            DateTime = DateTime.Now,
                            Address = "GPS Error"
                        };
                    }
                }
            });

            // We wait for all the tasks to finish
            await Task.WhenAll(updateTasks);

            // We reset indexes
            foreach (var run in Runs) run.OverlapIndex = 0;

            // We group by coordinates (only those that have a location)
            var groupedRuns = Runs
                .Where(r => r.LastKnownLocation != null)
                .GroupBy(r => new { r.LastKnownLocation.Latitude, r.LastKnownLocation.Longitude })
                .Where(g => g.Count() > 1); // We are only interested in groups with more than 1 element

            // We assign incremental index to those that collide
            foreach (var group in groupedRuns)
            {
                int index = 0;
                foreach (var run in group)
                {
                    run.OverlapIndex = index;
                    index++;
                }
            }

            if (!_isInitialZoomDone)
            {
                // We collect all valid run locations.
                var allPoints = Runs
                    .Where(r => r.LastKnownLocation != null && r.LastKnownLocation.Latitude != 0)
                    .Select(r => new PointLatLng(r.LastKnownLocation.Latitude, r.LastKnownLocation.Longitude))
                    .ToList();

                if (allPoints.Any())
                {
                    // If there is only one point, we simply center it with a fixed zoom.
                    if (allPoints.Count == 1)
                    {
                        OverviewMapCenter = allPoints.First();
                        OverviewMapZoom = 14;
                    }
                    else
                    {
                        // If there are several points, we calculate the rectangle that contains them.
                        double maxLat = allPoints.Max(p => p.Lat);
                        double minLat = allPoints.Min(p => p.Lat);
                        double maxLng = allPoints.Max(p => p.Lng);
                        double minLng = allPoints.Min(p => p.Lng);

                        var rect = new RectLatLng(maxLat, minLng, Math.Abs(maxLng - minLng), Math.Abs(maxLat - minLat));

                        // We add a small margin so that the points do not remain on the edge.
                        rect.Inflate(0.05, 0.05);

                        // We fire the event for the view to handle.
                        ZoomAndCenterRequest?.Invoke(this, new ZoomAndCenterEventArgs(rect));
                    }

                    _isInitialZoomDone = true; // We mark that the initial zoom has already been done.
                }
            }

        }
        private void ExecuteShowOverview(object parameter)
        {
            CurrentViewMode = DispatchViewMode.Overview;
            // We start the timer when the view is shown
            StartOverviewLiveTracking();
        }

        private void ExecuteShowDispatchMain(object parameter)
        {           
            StopOverviewLiveTracking();
            CurrentViewMode = DispatchViewMode.Main;
        }

        private void ExecuteShowMainView(object parameter)
        {
            StopOverviewLiveTracking();
            CurrentViewMode = DispatchViewMode.Main;
            CurrentScheduleViewModel = null; 
        }

        private bool CanExecuteShowScheduleView(object parameter)
        {
            return parameter is RunItemViewModel;
        }

        // Revisar el codigo con calma pues esta dando problemas: await CheckForPendingRecalculation(); (en InitializeAsync del SchedulesViewModel) esta desordenando las rutas.
        private async void ExecuteShowScheduleView(object parameter)
        {
            /*StopOverviewLiveTracking();
            if (parameter is RunItemViewModel selectedRun && selectedRun.VehicleRoute?.Model != null)
            {
                IsLoading = true;
                try
                {
                    var scheduleService = new ScheduleService();
                    var scheduleVM = new SchedulesViewModel(scheduleService);

                    scheduleVM.ShowFilterControls = true;
                    scheduleVM.AllowUnperformAction = true;

                    await scheduleVM.InitializeAsync(this.CurrentDispatchDate, selectedRun.VehicleRoute.Model, isLiveTracking: true);

                    CurrentScheduleViewModel = scheduleVM;
                    CurrentViewMode = DispatchViewMode.ScheduleDetail; 
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open Schedule view: {ex.Message}", "Error");
                }
                finally
                {
                    IsLoading = false;
                }
            }*/
        }               

        private async Task ExecuteUncancelTripAsync(object parameter)
        {
            var tripToUncancel = parameter as TripItemViewModel;
            if (tripToUncancel == null) return;

            // Confirmación del usuario
            var confirmationText = $"Are you sure you want to restore trip '{tripToUncancel.Id}'?";
            var confirmationTitle = "Confirm Restoration";

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.UncancelTripAsync(tripToUncancel.Id);

                    MessageBox.Show(
                        "Trip restored successfully.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                   
                    await LoadUnscheduledTripsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error restoring trip: {ex.Message}");
                    MessageBox.Show(
                        $"Error restoring trip: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private async Task ExecuteCancelTripAsync(object parameter)
        {
            var tripToCancel = parameter as TripItemViewModel;
            if (tripToCancel == null) return;

            var confirmationText = string.Format(LocalizationService.Instance["ConfirmCancelTripText"], tripToCancel.Id); // ej: "¿Está seguro de que desea cancelar el viaje '{0}'?"
            var confirmationTitle = LocalizationService.Instance["ConfirmCancelTitle"]; // ej: "Confirmar cancelación"

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.CancelTripAsync(tripToCancel.Id);

                    
                    //UnscheduledTrips.Remove(tripToCancel);                   
                    //_allUnscheduledTripsFromService.Remove(tripToCancel);

                    MessageBox.Show(
                        LocalizationService.Instance["TripCanceledSuccessfully"], // ej: "Viaje cancelado correctamente."
                        SuccessTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await LoadUnscheduledTripsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error canceling trip: {ex.Message}");
                    MessageBox.Show(
                        string.Format(LocalizationService.Instance["ErrorCancelingTrip"], ex.Message), // ej: "Error al cancelar el viaje: {0}"
                        ErrorTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            
        }

        private async Task ExecuteEditTripAsync(object parameter)
        {
            var tripToEdit = parameter as TripItemViewModel;
            if (tripToEdit == null) return;

            var dialogViewModel = new EditTripDialogViewModel(tripToEdit.TripDto);
            var dialog = new EditTripDialog { DataContext = dialogViewModel };

            var result = await DialogHost.Show(dialog, "RootDialogHost");

            // If the result is 'true' (the user pressed Save)
            if (result is bool wasSaved && wasSaved)
            {
                try
                {
                    var updatedDto = dialogViewModel.GetUpdatedDto();
                    await _tripService.UpdateFromDispatchAsync(tripToEdit.Id, updatedDto);

                    // Refresh data to see changes
                    await LoadUnscheduledTripsAsync();
                }
                catch (Exception ex)
                {

                    Console.WriteLine($"Error updating trip: {ex.Message}");
                }
            }
        }

        private async Task LoadAllDataAsync()
        {
            IsLoading = true;
            try
            {
                _allVehicleRoutes.Clear();
                RunService _runService = new RunService();
                var routes = await _runService.GetAllAsync();

                foreach (var route in routes)
                {
                    _allVehicleRoutes.Add(new VehicleRouteViewModel(route));
                }

                // We use Task.WhenAll to run both loads in parallel and improve performance
                await Task.WhenAll(
                    LoadRunsAndEventsAsync(),
                    LoadUnscheduledTripsAsync()
                );
            }
            catch (Exception ex)
            {
               
                MessageBox.Show($"An error occurred while loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                
                IsLoading = false;
            }
            
        }

        private async Task LoadRunsAndEventsAsync()
        {
            StopOverviewLiveTracking();

            Runs.Clear();
            _masterAllEvents.Clear();
            foreach (var route in _allVehicleRoutes)
            {
                var runItem = new RunItemViewModel(route);
                try
                {
                    var schedules = await _scheduleService.GetSchedulesAsync(route.Id, CurrentDispatchDate);

                    if (schedules != null && schedules.Any())
                    {
                        foreach (var schedule in schedules)
                        {
                            /*schedule.Run = route.Name;
                            schedule.Driver = route.DriverFullName;
                            schedule.Vehicle = route.VehicleName;*/
                            _masterAllEvents.Add(schedule);
                        }

                        runItem.Schedules = new ObservableCollection<ScheduleDto>(schedules);
                        runItem.UpdateCalculatedProperties();

                        // We add the Run to the visible list only because it has trips
                        Runs.Add(runItem);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading schedules for route {route.Name}: {ex.Message}");
                }
                //Runs.Add(runItem);
            }

            TotalVehicles = Runs.Count();
            ClearVehicles = 0;   
            LoadedVehicles = 0;
            EnRouteVehicles = 0;
            OnBreakVehicles = 0;
            OfflineVehicles = 0;

            FilterAllEvents(); // Call the filter to populate the visible collection

            await UpdateAllRunsLocationAsync();

            StartOverviewLiveTracking();
        }

        private async Task LoadUnscheduledTripsAsync()
        {
            IsLoading = true;
            try
            {
                var unscheduledDtoList = await _scheduleService.GetUnscheduledTripsAsync(CurrentDispatchDate);

                //var geocodingTasks = unscheduledDtoList.Select(trip => PopulateCitiesForTravel(trip)).ToList();
                //await Task.WhenAll(geocodingTasks);

                // Only consume the Google Maps service if the Trip object does not have PickupCity or DropoffCity
                _allUnscheduledTripsFromService.Clear();
                foreach (var source in unscheduledDtoList)
                {
                    /*if (source.PickupCity.Equals("") || source.PickupCity == null)
                        source.PickupCity = await _googleMapsService.GetCityFromCoordinates(source.PickupLatitude, source.PickupLongitude) ?? "N/A";
                    if (source.DropoffCity.Equals("") || source.DropoffCity == null)
                        source.DropoffCity = await _googleMapsService.GetCityFromCoordinates(source.DropoffLatitude, source.DropoffLongitude) ?? "N/A";*/
                    _allUnscheduledTripsFromService.Add(new TripItemViewModel(source));
                }

                /*_allUnscheduledTripsFromService.Clear();
                foreach (var dto in unscheduledDtoList)
                {
                    _allUnscheduledTripsFromService.Add(new TripItemViewModel(dto));
                }*/

                FilterUnscheduledTrips(); // Apply filter after loading
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error loading unscheduled trips: {ex.Message}");
            }
            finally
            {

                IsLoading = false;
            }
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

        /*private void FilterUnscheduledTrips()
        {
            var filteredTrips = _allUnscheduledTripsFromService.AsEnumerable();

            if (DisplayWillCalls)
            {
                filteredTrips = filteredTrips.Where(t => t.TripDto.WillCall);
            }

            // Actualizar la colección observable para la UI
            UnscheduledTrips.Clear();
            foreach (var trip in filteredTrips)
            {
                UnscheduledTrips.Add(trip);
            }
        }*/

        private void FilterUnscheduledTrips()
        {
            var filteredTrips = DisplayWillCalls
                ? _allUnscheduledTripsFromService.Where(t => t.TripDto.WillCall)
                : _allUnscheduledTripsFromService;

            UnscheduledTrips.Clear();
            foreach (var trip in filteredTrips)
            {
                UnscheduledTrips.Add(trip);
            }
        }

        private void FilterAllEvents()
        {
            IEnumerable<ScheduleDto> filtered = _masterAllEvents;

            if (!ShowPerformedEvents)
            {
                filtered = filtered.Where(e => !e.Performed);
            }
            // Aquí se puede añadir la lógica para los otros checkboxes cuando sea necesario
            // if (!ShowPullEvents) { ... }
            // if (ShowLatePickups) { ... }

            AllEvents.Clear();
            foreach (var item in filtered)
            {
                AllEvents.Add(item);
            }
            UpdateEventsSummary();
        }

        private void UpdateEventsSummary()
        {
            int total = _masterAllEvents.Count;
            int unperformed = _masterAllEvents.Count(e => !e.Performed);
            EventsSummaryText = $"{total} total events, {unperformed} unperformed";
        }

        #region Command Execution Methods      

        private async Task ExecuteExportScheduleAsync(object parameter)
        {
            Console.WriteLine("Export Schedule Clicked!");
            await Task.Delay(1);
        }

        private async Task ExecuteRefreshAsync(object parameter)
        {
            Console.WriteLine("Refresh Clicked!");
            await LoadAllDataAsync();
        }
        #endregion

        #region Translation 
        public string HeaderRuns => LocalizationService.Instance["Runs"]; 
        public string HeaderSelect => LocalizationService.Instance["Select"];
        public string HeaderUnscheduled => LocalizationService.Instance["Unscheduled"];
        public string HeaderDispatchMain => LocalizationService.Instance["DispatchMain"];

        public string SelectFieldsToDisplayToolTip => LocalizationService.Instance["SelectFieldsToDisplay"];

        public string ColumnHeaderOn => LocalizationService.Instance["On"];
        public string ColumnHeaderMTE => LocalizationService.Instance["MTE"];
        public string ColumnHeaderME => LocalizationService.Instance["ME"];
        public string ColumnHeaderTrips => LocalizationService.Instance["Trips"];
        public string ColumnHeaderUnperf => LocalizationService.Instance["Unperf"]; // Unperf.
        public string ColumnHeaderRun => LocalizationService.Instance["Run"];
        public string ColumnHeaderDriver => LocalizationService.Instance["Driver"];
        public string ColumnHeaderVehicle => LocalizationService.Instance["Vehicle"];
        public string ColumnHeaderLastNextEvent => LocalizationService.Instance["LastNextEvent"]; // "Last Event, Next Event"

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
        public string ColumnHeaderSpace => LocalizationService.Instance["SpaceType"];
        public string ColumnHeaderPickupComment => LocalizationService.Instance["PickupComment"];
        public string ColumnHeaderDropoffComment => LocalizationService.Instance["DropoffComment"];
        public string ColumnHeaderType => LocalizationService.Instance["Type"];
        public string ColumnHeaderDropoff => LocalizationService.Instance["Dropoff"];
        public string ColumnHeaderPickupPhone => LocalizationService.Instance["PickupPhone"];
        public string ColumnHeaderDropoffPhone => LocalizationService.Instance["DropoffPhone"];
        public string ColumnHeaderTripId => LocalizationService.Instance["TripId"];
        public string ColumnHeaderAuthorization => LocalizationService.Instance["Authorization"];
        public string ColumnHeaderFundingSource => LocalizationService.Instance["FundingSource"];
        public string ColumnHeaderDistance => LocalizationService.Instance["Distance"];
        public string ColumnHeaderPickupCity => LocalizationService.Instance["PickupCity"];
        public string ColumnHeaderDropoffCity => LocalizationService.Instance["DropoffCity"];

        // Actions     
        public string CancelTripToolTip => LocalizationService.Instance["CancelTrip"];
        public string EditTripToolTip => LocalizationService.Instance["Edit"];

        public string SuccessTitle => LocalizationService.Instance["SuccessTitle"]; // ej: "Éxito"
        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"]; // ej: "Error"

        #endregion


    }
}