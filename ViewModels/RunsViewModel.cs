using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Data.Scheduling;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.ViewModels
{
    public class RunsViewModel : BaseViewModel
    {
        private readonly IRunService _runService;

        public ObservableCollection<VehicleRouteViewModel> AllRoutes { get; }
        public ICollectionView FilteredRoutes { get; }

        private VehicleRouteViewModel _selectedRoute;
        public VehicleRouteViewModel SelectedRoute
        {
            get => _selectedRoute;
            set { _selectedRoute = value; OnPropertyChanged(); }
        }

        private bool _showOnlyActive = false;
        public bool ShowOnlyActive
        {
            get => _showOnlyActive;
            set { _showOnlyActive = value; OnPropertyChanged(); FilteredRoutes.Refresh(); }
        }

        public ObservableCollection<VehicleGroup> VehicleGroups { get; }
        private VehicleGroup _selectedVehicleGroup;
        public VehicleGroup SelectedVehicleGroup
        {
            get => _selectedVehicleGroup;
            set { _selectedVehicleGroup = value; OnPropertyChanged(); FilteredRoutes.Refresh(); }
        }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand LoadDataCommand { get; }

        /// <summary>RE-053: take the route's vehicle, driver or both out of service.</summary>
        public ICommand OutOfServiceCommand { get; }

        public ICommand ReturnToServiceCommand { get; }

        private bool _showOnlyOutOfService;
        public bool ShowOnlyOutOfService
        {
            get => _showOnlyOutOfService;
            set { _showOnlyOutOfService = value; OnPropertyChanged(); FilteredRoutes.Refresh(); }
        }


        public RunsViewModel( IRunService runService)
        {
            _runService = runService;
            AllRoutes = new ObservableCollection<VehicleRouteViewModel>();
            VehicleGroups = new ObservableCollection<VehicleGroup>();

            FilteredRoutes = CollectionViewSource.GetDefaultView(AllRoutes);
            FilteredRoutes.Filter = FilterPredicate;

            AddCommand = new RelayCommandObject(param => AddRoute());
            EditCommand = new RelayCommandObject(param => EditRoute(), param => SelectedRoute != null);
            DeleteCommand = new RelayCommandObject(param => DeleteRoute(), param => SelectedRoute != null);
            LoadDataCommand = new RelayCommandObject(async param => await LoadData());
            OutOfServiceCommand = new RelayCommandObject(async param => await TakeOutOfServiceAsync(param as VehicleRouteViewModel),
                param => param is VehicleRouteViewModel r && !r.IsOutOfService);
            ReturnToServiceCommand = new RelayCommandObject(async param => await ReturnToServiceAsync(param as VehicleRouteViewModel),
                param => param is VehicleRouteViewModel r && r.IsOutOfService);

            LoadDataCommand.Execute(null);
        }

        private async Task LoadData()
        {
            VehicleGroupService _vehicleGroupService = new VehicleGroupService();
            //var groups = await _dataService.GetVehicleGroupsAsync();
            var groups = await _vehicleGroupService.GetGroupsAsync();
            VehicleGroups.Clear();
            VehicleGroups.Add(new VehicleGroup { Id = 0, Name = "Todos los Grupos" }); // Option to deselect filter
            foreach (var group in groups)
            {
                VehicleGroups.Add(group);
            }

            RunService _runService = new RunService();   
            //var routes = await _dataService.GetVehicleRoutesAsync();
            var routes = await _runService.GetAllAsync();
            AllRoutes.Clear();
            foreach (var route in routes)
            {
                AllRoutes.Add(new VehicleRouteViewModel(route));
            }

            await LoadOutOfServiceAsync();
        }

        /// <summary>RE-053: one read of the open records, spread over the routes they affect.</summary>
        private async Task LoadOutOfServiceAsync()
        {
            List<DTOs.OutOfServiceRecordDto> open;
            try
            {
                open = await new OutOfServiceService().GetActiveAsync();
            }
            catch (System.Exception ex)
            {
                Helpers.FileLogger.Log($"Runs: could not read the routes out of service. {ex.Message}");
                return;
            }

            foreach (var route in AllRoutes)
            {
                route.OutOfService = open.Where(r =>
                        (r.Subject == Views.OutOfServiceDialog.Vehicle && r.SubjectId == route.Model.VehicleId)
                        || (r.Subject == Views.OutOfServiceDialog.Driver && r.SubjectId == route.Model.DriverId))
                    .ToList();
            }

            FilteredRoutes.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task TakeOutOfServiceAsync(VehicleRouteViewModel route)
        {
            if (route == null) return;
            if (Helpers.OutOfServiceUi.Show(Views.OutOfServiceDialog.ForRoute(route.Model)))
                await LoadData();
        }

        private async Task ReturnToServiceAsync(VehicleRouteViewModel route)
        {
            if (route == null) return;
            if (await Helpers.OutOfServiceUi.ReturnToServiceAsync(route.OutOfService, route.Name))
                await LoadOutOfServiceAsync();
        }

        private void AddRoute()
        {
            var newRoute = new VehicleRoute(); 
            var editorViewModel = new VehicleRouteEditorViewModel(newRoute, _runService);

            var editorWindow = new VehicleRouteEditorWindow
            {
                DataContext = editorViewModel//,
                //Owner = Application.Current.MainWindow
            };

            if (editorWindow.ShowDialog() == true)
            {
                // The window was closed with "Save"
                var savedRouteVM = new VehicleRouteViewModel(newRoute);
                AllRoutes.Add(savedRouteVM);
                SelectedRoute = savedRouteVM;                          
            }
        }

        private void EditRoute()
        {
            if (SelectedRoute == null) return;

            // We pass a copy so as not to modify the original until saving
            var routeToEdit = SelectedRoute.Model;
            var editorViewModel = new VehicleRouteEditorViewModel(routeToEdit, _runService);

            var editorWindow = new VehicleRouteEditorWindow
            {
                DataContext = editorViewModel//,
                //Owner = Application.Current.MainWindow
            };

            if (editorWindow.ShowDialog() == true)
            {
                // Update existing item view
                SelectedRoute.Refresh();
                FilteredRoutes.Refresh();
            }
        }
        private async void DeleteRoute()
        {
            if (SelectedRoute == null) return;

            if (MessageBox.Show($"Are you sure you want to cancel the route '{SelectedRoute.Name}'?", "Confirm Cancellation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    RunService _runService = new RunService();
                    await _runService.CancelAsync(SelectedRoute.Id);
                    var routeToCancel = SelectedRoute.Model;
                    routeToCancel.ToDate = DateTime.Now;
                    SelectedRoute.Refresh();
                    FilteredRoutes.Refresh();
                    //await _runService.DeleteAsync(SelectedRoute.Id);
                    //AllRoutes.Remove(SelectedRoute);

                    /*bool success = await _runService.CancelAsync(SelectedRoute.Id);

                    if (success)
                    {
                        MessageBox.Show("Ruta cancelada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                        SelectedRoute.ToDate = DateTime.Now; // Actualiza el objeto en la UI
                        OnPropertyChanged(nameof(Routes)); // Notifica a la vista que la colección pudo haber cambiado
                    }
                    else
                    {
                        Helpers.UiError.Show("No se pudo cancelar la ruta. Revisa la consola para más detalles.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }*/

                }
                catch (ApiException ex)
                {
                    Helpers.UiError.Show($"Error canceling: {ex.Message}", "API error", MessageBoxButton.OK, MessageBoxImage.Error);
                    //Helpers.UiError.Show($"Error al eliminar: {ex.Message}\nDetalles: {ex.Details}", "Error de API", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private bool FilterPredicate(object item)
        {
            if (item is VehicleRouteViewModel route)
            {
                bool isActiveMatch = ShowOnlyActive || route.IsActive;
                bool groupMatch = SelectedVehicleGroup == null || SelectedVehicleGroup.Id == 0 || route.VehicleGroupId == SelectedVehicleGroup.Id;
                bool outOfServiceMatch = !ShowOnlyOutOfService || route.IsOutOfService;
                return isActiveMatch && groupMatch && outOfServiceMatch;
            }
            return false;
        }

        #region Translation

        // Filters
        public string SelectVehicleGroupHint => LocalizationService.Instance["SelectVehicleGroupHint"];
        public string AddRunToolTip => LocalizationService.Instance["AddRunToolTip"]; // Add new Run

        public string EditRunToolTip => LocalizationService.Instance["EditRunToolTip"]; // Edit
        public string DeleteRunToolTip => LocalizationService.Instance["DeleteRunToolTip"];

        public string OutOfServiceToolTip => LocalizationService.Instance["oos.ActionRoute"];
        public string ReturnToServiceToolTip => LocalizationService.Instance["oos.ActionReturn"];
        public string ShowOnlyOutOfServiceText => LocalizationService.Instance["oos.FilterOnly"];
        public string ColumnHeaderOutOfService => LocalizationService.Instance["oos.Column"];
        public string OutOfServiceBadge => LocalizationService.Instance["oos.Badge"]; // Cancel run by changing 'To Date' to today

        // Column headers
        public string ColumnHeaderName => LocalizationService.Instance["Name"];
        public string ColumnHeaderDescription => LocalizationService.Instance["Description"];
        public string ColumnHeaderSmartphoneLogin => LocalizationService.Instance["SmartphoneLogin"]; // Smartphone Login
        public string ColumnHeaderDriver => LocalizationService.Instance["Driver"]; // Driver / Conductor
        public string ColumnHeaderVehicle => LocalizationService.Instance["Vehicle"]; // Vehicle
        public string ColumnHeaderGarage => LocalizationService.Instance["Garage"]; // Garage
        public string ColumnHeaderFromDate => LocalizationService.Instance["FromDate"]; // From Date
        public string ColumnHeaderToDate => LocalizationService.Instance["ToDate"]; // To Date
        public string ColumnHeaderFromTime => LocalizationService.Instance["FromTime"]; // From Time
        public string ColumnHeaderToTime => LocalizationService.Instance["ToTime"]; // To Time 

        // Day of Week
        public string Sunday => LocalizationService.Instance["Sunday"];
        public string Monday => LocalizationService.Instance["Monday"];
        public string Tuesday => LocalizationService.Instance["Tuesday"];
        public string Wednesday => LocalizationService.Instance["Wednesday"];
        public string Thursday => LocalizationService.Instance["Thursday"];
        public string Friday => LocalizationService.Instance["Friday"];
        public string Saturday => LocalizationService.Instance["Saturday"];

        #endregion
    }
}