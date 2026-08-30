using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Dispatch;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    public partial class TripsViewModel : ObservableObject
    {
        // Services
        private readonly TripService _tripService;
        private readonly ScheduleService _scheduleService;
        private readonly IRoutingApiService _routingService;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _busyMessage;

        [ObservableProperty]
        private DateTime _selectedDate = DateTime.Today.AddDays(1);

        [ObservableProperty]
        private bool _hideCanceledTrips;

        [ObservableProperty]
        private string _selectedTripTypeFilter = "Both"; // Options: Both, Appointment, Return

        [ObservableProperty]
        private ScheduleDto _selectedSchedule;

        public List<string> TripTypeOptions { get; } = new() { "Both", "Appointment", "Return" };

        // Collections
        public ObservableCollection<TripReadDto> AllTrips { get; } = new();
        public ObservableCollection<VehicleRoute> VehicleRoutes { get; } = new();

        // Commands
        public IAsyncRelayCommand<TripReadDto> UpdateTripRunCommand { get; }
        public IAsyncRelayCommand<TripReadDto> CancelRouteCommand { get; }
        public IAsyncRelayCommand CancelTripCommand { get; }
        public IAsyncRelayCommand UncancelTripCommand { get; }
        public IAsyncRelayCommand EditTripCommand { get; }

        public IAsyncRelayCommand ShowHistoryCommand { get; }

        public TripsViewModel()
        {
            _tripService = new TripService();
            _scheduleService = new ScheduleService();
            _routingService = new RoutingApiService();

            CancelRouteCommand = new AsyncRelayCommand<TripReadDto>(ExecuteUnscheduleTripAsync);
            UpdateTripRunCommand = new AsyncRelayCommand<TripReadDto>(ExecuteUpdateTripRunAsync);

            CancelTripCommand = new AsyncRelayCommand<object>(ExecuteCancelTripAsync);
            UncancelTripCommand = new AsyncRelayCommand<object>(ExecuteUncancelTripAsync);
            EditTripCommand = new AsyncRelayCommand<object>(ExecuteEditTripAsync);

            ShowHistoryCommand = new AsyncRelayCommand<object>(ExecuteShowHistoryAsync);

            _ = LoadInitialDataAsync();
            _ = LoadAllTripsAsync();
        }

        private async Task ExecuteShowHistoryAsync(object parameter)
        {
            var trip = parameter as TripReadDto;
            if (trip == null) return;

            var viewModel = new TripHistoryViewModel(trip);

            var view = new Views.TripHistoryDialog
            {
                DataContext = viewModel
            };
           
            await MaterialDesignThemes.Wpf.DialogHost.Show(view, "TripsDialogHost");
        }
        public async Task LoadInitialDataAsync()
        {           
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
           
        }

        partial void OnSelectedDateChanged(DateTime value)
        {                        
            _ = LoadAllTripsAsync();          
        }

        // Listen to filter changes
        partial void OnHideCanceledTripsChanged(bool value) => _ = LoadAllTripsAsync();
        partial void OnSelectedTripTypeFilterChanged(string value) => _ = LoadAllTripsAsync();

        /*private async Task LoadAllTripsAsync()
        {
            try
            {
                var trips = await _tripService.GetTripsByDateAsync(SelectedDate);

                IEnumerable<TripReadDto> query = trips;

                // Filtro de Cancelados
                if (HideCanceledTrips)
                    query = query.Where(t => t.Status != "Canceled" && !t.IsCancelled);

                // Filtro de Tipo
                if (SelectedTripTypeFilter != "Both")
                    query = query.Where(t => t.Type == SelectedTripTypeFilter);

                AllTrips.Clear();
                foreach (var t in query) AllTrips.Add(t);
            }
            catch (Exception ex) {  }
        }*/

        private async Task LoadAllTripsAsync()
        {
            IsLoading = true;
            try
            {
                var trips = await _tripService.GetTripsByDateAsync(SelectedDate);
                IEnumerable<TripReadDto> query = trips;

                if (HideCanceledTrips)
                    query = query.Where(t => t.Status != "Canceled" && !t.IsCancelled);

                if (SelectedTripTypeFilter != "Both")
                    query = query.Where(t => t.Type == SelectedTripTypeFilter);

                AllTrips.Clear();
                foreach (var t in query) AllTrips.Add(t);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { IsLoading = false; }
        }


        // --- REASSIGNMENT LOGIC (UNSCHEDULE + ROUTE) ---

        private async Task ExecuteUpdateTripRunAsync(TripReadDto trip)
        {
            if (trip == null) return;

            IsBusy = true;
            try
            {
                // Obtain the current status of the trip in the database to know if you already have a Schedule
                // We look for if there are events in Schedule for this trip               
                // If the trip was already in a Run, you must remove it from the Schedule table first.                               

                BusyMessage = "Cleaning up old schedule...";

                // We look for any schedule associated with this trip to obtain its ID and call Unschedule
                var allSchedulesForDate = await GetAnyScheduleIdForTrip(trip.Id);
                if (allSchedulesForDate.HasValue)
                {
                    await _scheduleService.CancelRouteAsync(allSchedulesForDate.Value);
                }

                // If the new selected value is a Run (ID > 0), we proceed to Route it
                if (trip.VehicleRouteId > 0)
                {
                    BusyMessage = "Routing to new Run...";
                    await RouteTripToSelectedRunAsync(trip);
                }
                else
                {
                    // If you selected "None", we only update the field in the Trip table
                    await _tripService.AssignRunAsync(trip.Id, null);
                }

                await LoadAllTripsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reassigning run: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await LoadAllTripsAsync(); // Rollback visual
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RouteTripToSelectedRunAsync(TripReadDto trip)
        {
            // We need the destination route information
            var targetRoute = VehicleRoutes.FirstOrDefault(r => r.Id == trip.VehicleRouteId);
            if (targetRoute == null) return;

            // We obtain the current schedules of that route to calculate the insertion (at the end, before the Pull-in)
            var currentSchedules = await _scheduleService.GetSchedulesAsync(targetRoute.Id, SelectedDate);

            // Logic similar to SchedulesViewModel's RouteSelectedTripAsync
            var previousSchedule = currentSchedules.LastOrDefault(s => s.Name != "Pull-in");

            double originLat, originLng;
            TimeSpan previousEta, previousServiceTime;

            if (previousSchedule == null || previousSchedule.Name == "Pull-out")
            {
                originLat = targetRoute.GarageLatitude;
                originLng = targetRoute.GarageLongitude;
                previousEta = currentSchedules.FirstOrDefault(s => s.Name == "Pull-out")?.ETA
                              ?? (trip.FromTime ?? TimeSpan.Zero) - TimeSpan.FromMinutes(30);
                previousServiceTime = TimeSpan.Zero;
            }
            else
            {
                originLat = previousSchedule.ScheduleLatitude;
                originLng = previousSchedule.ScheduleLongitude;
                previousEta = previousSchedule.ETA ?? TimeSpan.Zero;
                previousServiceTime = TimeSpan.FromMinutes(previousSchedule.On ?? 15);
            }
           
            // The three legs of routing a trip, asked for together: to the pickup, on to the
            // dropoff, and home to the garage. Three separate calls to Google before.
            var legs = await _routingService.GetLegsAsync(new List<RouteLegRequestItemDto>
            {
                new RouteLegRequestItemDto
                {
                    OriginLat = originLat,
                    OriginLng = originLng,
                    DestLat = trip.PickupLatitude,
                    DestLng = trip.PickupLongitude,
                    Date = SelectedDate,
                    DepartureTime = previousEta
                },
                new RouteLegRequestItemDto
                {
                    OriginLat = trip.PickupLatitude,
                    OriginLng = trip.PickupLongitude,
                    DestLat = trip.DropoffLatitude,
                    DestLng = trip.DropoffLongitude,
                    Date = SelectedDate,
                    DepartureTime = trip.FromTime
                },
                new RouteLegRequestItemDto
                {
                    OriginLat = trip.DropoffLatitude,
                    OriginLng = trip.DropoffLongitude,
                    DestLat = targetRoute.GarageLatitude,
                    DestLng = targetRoute.GarageLongitude,
                    Date = SelectedDate,
                    DepartureTime = trip.ToTime ?? trip.FromTime
                }
            });

            var pickupLeg = legs[0];
            if (!pickupLeg.IsUsable) throw new Exception("The route to the pickup point could not be calculated.");

            double pDistance = pickupLeg.DistanceMiles;
            TimeSpan pTravelTime = TimeSpan.FromSeconds(pickupLeg.DurationInTrafficSeconds ?? pickupLeg.DurationSeconds);
            TimeSpan pFinalEta = previousEta + previousServiceTime + pTravelTime;

            var dropoffLeg = legs[1];
            if (!dropoffLeg.IsUsable) throw new Exception("The route to the dropoff point could not be calculated.");

            double dDistance = dropoffLeg.DistanceMiles;
            TimeSpan dTravelTime = TimeSpan.FromSeconds(dropoffLeg.DurationInTrafficSeconds ?? dropoffLeg.DurationSeconds);
            TimeSpan pickupServiceTime = TimeSpan.FromMinutes(15);
            TimeSpan dFinalEta = pFinalEta + pickupServiceTime + dTravelTime;

            // The drive home, for the Pull-in hour. Zero if it could not be priced: the Pull-in
            // is the vehicle coming back to the garage, and losing the reassignment over that
            // leg costs more than a Pull-in that is briefly early.
            var garageLeg = legs[2];
            TimeSpan returnTravelTime = garageLeg.IsUsable
                ? TimeSpan.FromSeconds(garageLeg.DurationInTrafficSeconds ?? garageLeg.DurationSeconds)
                : TimeSpan.Zero;

            // Send to Backend
            var request = new RouteTripRequest
            {
                VehicleRouteId = targetRoute.Id,
                TripId = trip.Id,
                PickupDistance = pDistance,
                PickupTravelTime = pTravelTime,
                PickupETA = pFinalEta,
                DropoffDistance = dDistance,
                DropoffTravelTime = dTravelTime,
                DropoffETA = dFinalEta,
                ReturnToGarageTravelTime = returnTravelTime,
                VehicleRouteName = targetRoute.Name
            };

            await _scheduleService.RouteTripsAsync(request);
        }

        private async Task<int?> GetAnyScheduleIdForTrip(int tripId)
        {
            // This method helps find an ID from the Schedule table associated with the trip
            // to be able to call the existing Unschedule method.
            foreach (var route in VehicleRoutes)
            {
                var schedules = await _scheduleService.GetSchedulesAsync(route.Id, SelectedDate);
                var match = schedules.FirstOrDefault(s => s.TripId == tripId);
                if (match != null) return match.Id;
            }
            return null;
        }

        /*private async Task ExecuteUpdateTripRunAsync(TripReadDto trip)
        {
            if (trip == null) return;

            try
            {
                // Si el ID es 0 o nulo, mandamos null para des-programar
                int? routeIdToSave = (trip.VehicleRouteId == 0) ? null : trip.VehicleRouteId;

                await _tripService.AssignRunAsync(trip.Id, routeIdToSave);

                // Opcional: Refrescar para ver nombres actualizados o cambios de estado
                // await LoadAllTripsAsync(); 
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating Run: {ex.Message}", "Error");
                // En caso de error, volvemos a cargar para resetear el combo a su valor real en DB
                await LoadAllTripsAsync();
            }
        }*/

        private async Task ExecuteUnscheduleTripAsync(TripReadDto trip)
        {
            if (trip == null) return;

            // Si no tiene Run, no hay nada que desprogramar
            if (trip.VehicleRouteId == null || trip.VehicleRouteId == 0) return;

            var result = MessageBox.Show(
                $"Are you sure you want to unschedule the trip for {trip.CustomerName} from Run {trip.RunName}?\nThis will remove the Pickup and Dropoff events from the schedule.",
                "Confirm Unschedule",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            IsBusy = true;
            BusyMessage = "Removing trip from schedule...";

            try
            {
                // We need to find the ID of a record in the SCHEDULE table that belongs to this trip.
                // The backend needs ONE scheduleId to identify the trip's "chain" of events.
                int? scheduleId = await FindScheduleIdForTrip(trip.Id);

                if (scheduleId.HasValue)
                {                   
                    await _scheduleService.CancelRouteAsync(scheduleId.Value);

                    // We refresh the list so that the Run appears empty and the state changes
                    await LoadAllTripsAsync();
                }
                else
                {
                    // If we do not find a record in Schedule but the Trip says it has RunId,
                    // we cleaned the inconsistency in the Trip table.
                    await _tripService.AssignRunAsync(trip.Id, null);
                    await LoadAllTripsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during unschedule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Helper to search the database for the Schedule event ID of this trip.
        /// </summary>
        private async Task<int?> FindScheduleIdForTrip(int tripId)
        {
            // We search the routes of the current day if this trip exists
            foreach (var route in VehicleRoutes)
            {
                var schedules = await _scheduleService.GetSchedulesAsync(route.Id, SelectedDate);
                var match = schedules.FirstOrDefault(s => s.TripId == tripId);
                if (match != null) return match.Id;
            }
            return null;
        }

        /*private async Task ExecuteUnscheduleTripAsync(TripReadDto trip)
        {
            if (trip == null) return;

            var result = MessageBox.Show($"Are you sure you want to unschedule this trip?", "Confirm", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                var scheduleId = await GetAnyScheduleIdForTrip(trip.Id);
                if (scheduleId.HasValue)
                {
                    await _scheduleService.CancelRouteAsync(scheduleId.Value);
                    await LoadAllTripsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
            finally { IsBusy = false; }
        }*/

        private async Task ExecuteCancelTripAsync(object parameter)
        {

            var tripToCancel = parameter as TripReadDto;
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
                    await LoadAllTripsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error canceling trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExecuteUncancelTripAsync(object parameter)
        {
            var tripToUncancel = parameter as TripReadDto;
            if (tripToUncancel == null) return;

            var confirmationText = $"Are you sure you want to restore trip '{tripToUncancel.Id}'?";
            var confirmationTitle = "Confirm Restoration";

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.UncancelTripAsync(tripToUncancel.Id);
                    MessageBox.Show("Trip restored successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    await LoadAllTripsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error restoring trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExecuteEditTripAsync(object parameter)
        {
            var tripToEdit = parameter as TripReadDto;
            if (tripToEdit == null) return;

            var dialogViewModel = new EditTripDialogViewModel(tripToEdit);
            var dialog = new EditTripDialog { DataContext = dialogViewModel };

            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(dialog, "TripsDialogHost");

            if (result is bool wasSaved && wasSaved)
            {
                try
                {
                    var updatedDto = dialogViewModel.GetUpdatedDto();
                    await _tripService.UpdateFromDispatchAsync(tripToEdit.Id, updatedDto);

                    // Reload to see changes
                    await LoadAllTripsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #region Translation

        // Schedules Grid
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
