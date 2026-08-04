using DocumentFormat.OpenXml.Drawing.ChartDrawing;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Mappers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    public class VehicleRouteEditorViewModel : BaseViewModel
    {
        private readonly IRunService _runService;
        private readonly VehicleRoute _originalRoute;

        // Property to bind to all controls on the form.
        // We work on this copy and only save if the user confirms.
        public VehicleRoute Route { get; private set; }

        // Collections for ComboBoxes and Lists in Tabs
        public ObservableCollection<User> AllDrivers { get; }
        public ObservableCollection<Vehicle> AllVehicles { get; }
        public ObservableCollection<SelectableFundingSourceViewModel> AllFundingSources { get; }
        public ObservableCollection<DailyAvailabilityViewModel> DailyAvailabilities { get; }
        public ObservableCollection<RouteSuspension> Suspensions { get; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddSuspensionCommand { get; }
        public ICommand RemoveSuspensionCommand { get; }


        // Event to notify the view (the window) that it should be closed
        public event EventHandler<bool?> RequestClose;

        public VehicleRouteEditorViewModel(VehicleRoute route, IRunService runService)
        {
            _runService = runService;
            _originalRoute = route; // We keep the original in case we have to revert

            // We create a deep copy (simplified here) so as not to edit the original object directly.
            Route = CloneRoute(route);

            // Initialize collections
            AllDrivers = new ObservableCollection<User>();
            AllVehicles = new ObservableCollection<Vehicle>();
            AllFundingSources = new ObservableCollection<SelectableFundingSourceViewModel>();
            DailyAvailabilities = new ObservableCollection<DailyAvailabilityViewModel>();
            Suspensions = new ObservableCollection<RouteSuspension>(Route.Suspensions);

            // Initialize commands
            SaveCommand = new RelayCommandObject(async param => await SaveAsync(), param => CanSave());
            CancelCommand = new RelayCommandObject(param => Cancel());
            AddSuspensionCommand = new RelayCommandObject(param => AddSuspension());
            RemoveSuspensionCommand = new RelayCommandObject(param => RemoveSuspension(param as RouteSuspension), param => param != null);


            // Load data asynchronously
            LoadDependenciesAsync();
        }

        private async Task LoadDependenciesAsync()
        {
            UserService _userService = new UserService();
            //var drivers = await _userService.GetDriversAsync();
            var drivers = await _userService.GetAllAsync();
            drivers.ForEach(d => AllDrivers.Add(d));
            VehicleService _vehicleService = new VehicleService();
            var vehicles = await _vehicleService.GetVehiclesAsync();
            vehicles.ForEach(v => AllVehicles.Add(v));

            if (Route.DriverId > 0)
            {
                Route.Driver = AllDrivers.FirstOrDefault(d => d.Id == Route.DriverId);
            }
            if (Route.VehicleId > 0)
            {
                Route.Vehicle = AllVehicles.FirstOrDefault(v => v.Id == Route.VehicleId);
            }
            OnPropertyChanged(nameof(Route)); // Notify that the Route property has been updated

            FundingSourceService _fundingSourceService = new FundingSourceService();           
            var allFundingSources = await _fundingSourceService.GetFundingSourcesAsync(false);
            var exclusiveIds = new HashSet<int>(Route.FundingSources.Select(fs => fs.FundingSourceId));

            foreach (var fs in allFundingSources)
            {
                AllFundingSources.Add(new SelectableFundingSourceViewModel(fs)
                {
                    IsSelected = exclusiveIds.Contains(fs.Id)
                });
            }
            // If it is a new route and does not have exclusive sources, by default none is selected.
            // The business logic indicates that "none selected" means "all are valid."

            // Prepare the Daily Availability list
            var daysOfWeek = Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>();
            foreach (var day in daysOfWeek)
            {
                var existingAvailability = Route.Availabilities.FirstOrDefault(a => a.DayOfWeek == day);
                var dailyVM = new DailyAvailabilityViewModel(day);

                if (existingAvailability != null)
                {
                    // If a configuration already exists for this day, use it
                    dailyVM.IsActive = existingAvailability.IsActive;
                    dailyVM.StartTime = existingAvailability.StartTime;
                    dailyVM.EndTime = existingAvailability.EndTime;
                }
                else
                {
                    // If it does not exist, use the general route schedules
                    dailyVM.StartTime = Route.FromTime;
                    dailyVM.EndTime = Route.ToTime;
                }
                DailyAvailabilities.Add(dailyVM);
            }
        }

        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(Route.Name) &&
                   Route.DriverId > 0 &&
                   Route.VehicleId > 0; /* &&
                   Route.FromDate != default &&
                   Route.FromTime != default &&
                   Route.ToTime != default &&
                   Route.ToTime > Route.FromTime;*/
        }

        private async Task SaveAsync()
        {
            if (!CanSave())
            {
                MessageBox.Show("Please complete all required fields and ensure the times are valid.", "Incomplete data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update the Daily Availability collection in the model
            Route.Availabilities.Clear();
            foreach (var dailyVM in DailyAvailabilities)
            {
                // We only save the configuration if it is different from the default behavior
                // (active and with the general route schedules) to keep the data clean.                
                bool isDefault = dailyVM.IsActive && dailyVM.StartTime == Route.FromTime && dailyVM.EndTime == Route.ToTime;
                if (!isDefault)
                {
                    Route.Availabilities.Add(new RouteAvailability
                    {
                        VehicleRouteId = Route.Id,
                        DayOfWeek = dailyVM.DayOfWeek,
                        IsActive = dailyVM.IsActive,
                        StartTime = dailyVM.StartTime,
                        EndTime = dailyVM.EndTime
                    });
                }
            }

            // Update the Financing Sources collection in the model
            Route.FundingSources.Clear();
            var selectedFundingSources = AllFundingSources.Where(fsvm => fsvm.IsSelected);
            foreach (var fsvm in selectedFundingSources)
            {
                Route.FundingSources.Add(new RouteFundingSource
                {
                    VehicleRouteId = Route.Id,
                    FundingSourceId = fsvm.FundingSource.Id
                });
            }

            // Update Suspensions
            Route.Suspensions = new List<RouteSuspension>(Suspensions);


            // Use the mapper to convert Model to DTO
            var routeDto = Route.ToDto();

            try
            {
                if (Route.Id == 0) // It's a new route
                {
                    var createdRoute = await _runService.CreateAsync(routeDto);
                    // Update the original model with the API response (which includes the new ID)
                    UpdateOriginalRoute(createdRoute);
                }
                else // It's an update
                {
                    await _runService.UpdateAsync(Route.Id, routeDto);
                    // Update the original model with saved local data
                    UpdateOriginalRoute(this.Route);
                }

                // Close the window successfully
                RequestClose?.Invoke(this, true);
            }
            catch (ApiException ex)
            {
                MessageBox.Show($"Saving error: {ex.Message}", "API error", MessageBoxButton.OK, MessageBoxImage.Error);
                //MessageBox.Show($"Error al guardar: {ex.Message}\nDetalles: {ex.Details}", "Error de API", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
        }

        private void Cancel()
        {
            // Just close the window without saving
            RequestClose?.Invoke(this, false);
        }

        private void AddSuspension()
        {
            Suspensions.Add(new RouteSuspension
            {
                VehicleRouteId = Route.Id,
                SuspensionStart = DateTime.Today,
                SuspensionEnd = DateTime.Today.AddDays(1)
            });
        }

        private void RemoveSuspension(RouteSuspension suspension)
        {
            if (suspension != null)
            {
                Suspensions.Remove(suspension);
            }
        }

        #region Helper Methods

        private VehicleRoute CloneRoute(VehicleRoute source)
        {
            // Esto es una clonación superficial. Para este caso es suficiente porque los tipos primitivos se copian.
            // Las colecciones se manejarán por separado.
            var clone = new VehicleRoute
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                DriverId = source.DriverId,
                VehicleId = source.VehicleId,
                Garage = source.Garage,
                GarageLatitude = source.GarageLatitude,
                GarageLongitude = source.GarageLongitude,
                SmartphoneLogin = source.SmartphoneLogin,
                FromDate = source.Id == 0 ? DateTime.Today : source.FromDate, // Valor por defecto si es nuevo
                ToDate = source.ToDate,
                FromTime = source.Id == 0 ? new TimeSpan(8, 0, 0) : source.FromTime,
                ToTime = source.Id == 0 ? new TimeSpan(18, 0, 0) : source.ToTime,
                Availabilities = new List<RouteAvailability>(source.Availabilities ?? new List<RouteAvailability>()),
                FundingSources = new List<RouteFundingSource>(source.FundingSources ?? new List<RouteFundingSource>()),
                Suspensions = new List<RouteSuspension>(source.Suspensions ?? new List<RouteSuspension>())
            };
            return clone;
        }

        private void UpdateOriginalRoute(VehicleRoute savedRoute)
        {
            // Copia las propiedades del objeto guardado (que puede tener un nuevo Id) al objeto original.
            _originalRoute.Id = savedRoute.Id;
            _originalRoute.Name = savedRoute.Name;
            _originalRoute.Description = savedRoute.Description;
            _originalRoute.DriverId = savedRoute.DriverId;
            _originalRoute.Driver = savedRoute.Driver;
            _originalRoute.VehicleId = savedRoute.VehicleId;
            _originalRoute.Vehicle = savedRoute.Vehicle;
            _originalRoute.Garage = savedRoute.Garage;
            _originalRoute.GarageLatitude = savedRoute.GarageLatitude;
            _originalRoute.GarageLongitude = savedRoute.GarageLongitude;
            _originalRoute.SmartphoneLogin = savedRoute.SmartphoneLogin;
            _originalRoute.FromDate = savedRoute.FromDate;
            _originalRoute.ToDate = savedRoute.ToDate;
            _originalRoute.FromTime = savedRoute.FromTime;
            _originalRoute.ToTime = savedRoute.ToTime;
            _originalRoute.Availabilities = savedRoute.Availabilities;
            _originalRoute.FundingSources = savedRoute.FundingSources;
            _originalRoute.Suspensions = savedRoute.Suspensions;
        }

        #endregion

        #region Translation
        public string TabItemHeaderGeneral => LocalizationService.Instance["General"]; // General

        public string GroupBoxHeaderMainData => LocalizationService.Instance["MainData"]; // Main Data
        public string TextBlockName => LocalizationService.Instance["TextBlockName"]; // Name:  
        public string TextBlockDescription => LocalizationService.Instance["TextBlockDescription"]; // Description: 
        public string TextBlockDriver => LocalizationService.Instance["TextBlockDriver"]; // Driver:
        public string TextBlockVehicle => LocalizationService.Instance["TextBlockVehicle"]; // Vehicle:
        public string TextBlockLoginSmartphone => LocalizationService.Instance["TextBlockLoginSmartphone"]; // Mobile Device Login:
        public string TextBlockFromDate => LocalizationService.Instance["TextBlockFromDate"]; // From Date:
        public string TextBlockToDate => LocalizationService.Instance["TextBlockToDate"]; // To Date:
        public string TextBlockFromTime => LocalizationService.Instance["TextBlockFromTime"]; // From Time:
        public string TextBlockToTime => LocalizationService.Instance["TextBlockToTime"]; // To Time:

        public string GroupBoxHeaderSuspensions => LocalizationService.Instance["GroupBoxHeaderSuspensions"]; // Optional Suspend From-To Dates / Suspensiones de Ruta (Opcional)
        public string ColumnHeaderStartSuspension => LocalizationService.Instance["StartSuspension"]; // Start Suspension
        public string ColumnHeaderEndSuspension => LocalizationService.Instance["EndSuspension"]; // End Suspension
        public string ColumnHeaderReason => LocalizationService.Instance["Reason"]; // Reason
        public string ButtonContentAddSuspension => LocalizationService.Instance["AddSuspension"]; // Add Suspension

        //Garage Location Tab
        public string TabItemHeaderGarageLocation => LocalizationService.Instance["GarageLocation"]; // Garage Location
        public string GroupBoxHeaderGarageLocation => LocalizationService.Instance["GarageLocation"];  
        public string TextBlockGarageAddress => LocalizationService.Instance["TextBlockGarageAddress"]; // Garage/Address: 
        public string TextBlockGarageLatitude => LocalizationService.Instance["TextBlockGarageLatitude"]; // Latitude:
        public string TextBlockGarageLongitude => LocalizationService.Instance["TextBlockGarageLongitude"]; // Longitude:

        //Daily Times Tab
        public string TabItemHeaderDailyTimes => LocalizationService.Instance["DailyTimes"]; // Daily Times
        public string GroupBoxHeaderSchedulesPerDay => LocalizationService.Instance["SchedulesPerDay"]; // Schedules per Day (Optional) / Horarios por Día(Opcional)
        public string SchedulesPerDayExplanatoryText => LocalizationService.Instance["SchedulesPerDayExplanatoryText"]; // Define specific schedules here for each day of the week. If a day is not modified or left active with the general schedules, The schedules defined in the "General" tab will apply.
        public string Active => LocalizationService.Instance["Active"];

        //Exclusive Funding Sources Tab
        public string TabItemHeaderExclusiveFundingSources => LocalizationService.Instance["ExclusiveFundingSources"]; // Exclusive Funding Sources
        public string GroupBoxHeaderOptionalExclusiveFundingSources => LocalizationService.Instance["OptionalExclusiveFundingSources"]; // Exclusive Financing Sources (Optional) / Fuentes de Financiamiento Exclusivas (Opcional)
        public string OptionalExclusiveFundingSourcesExplanatoryText => LocalizationService.Instance["OptionalExclusiveFundingSourcesExplanatoryText"]; // 

        public string SaveText => LocalizationService.Instance["Save"];
        public string CancelText => LocalizationService.Instance["Cancel"];
        #endregion
    }
}