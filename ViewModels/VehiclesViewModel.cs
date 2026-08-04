using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Helpers;
using Microsoft.Win32;
using ClosedXML.Excel;
using System.IO;
using Raphael.Desktop.Views.Data.Scheduling.Vehicles;
using Raphael.Desktop.Commands;

namespace Raphael.Desktop.ViewModels
{
    public class VehiclesViewModel : BaseViewModel
    {
        
        #region Translation
        public string SelectVehicleGroupHint => LocalizationService.Instance["SelectVehicleGroupHint"];
        public string AddVehicleToolTip => LocalizationService.Instance["AddVehicleToolTip"];
        public string EditVehicleToolTip => LocalizationService.Instance["EditVehicleToolTip"];
        public string DeleteVehicleToolTip => LocalizationService.Instance["DeleteVehicleToolTip"];
        public string ExcelExportToolTip => LocalizationService.Instance["ExcelExportToolTip"];

        public string ColumnHeaderName => LocalizationService.Instance["Name"];
        public string ColumnHeaderCapacity => LocalizationService.Instance["CapacityType"]; // Capacity Type
        public string ColumnHeaderVIN => LocalizationService.Instance["VIN"]; // VIN
        public string ColumnHeaderPlate => LocalizationService.Instance["Plate"]; // License Plate
        public string ColumnHeaderInactive => LocalizationService.Instance["Inactive"];
        public string ColumnHeaderGroup => LocalizationService.Instance["Group"];
        public string ColumnHeaderMake => LocalizationService.Instance["Make"];
        public string ColumnHeaderModel => LocalizationService.Instance["Model"];
        public string ColumnHeaderColor => LocalizationService.Instance["Color"];
        public string ColumnHeaderYear => LocalizationService.Instance["Year"];

        public string LoadingDataMessage => LocalizationService.Instance["LoadingData"]; // Loading data...
        public string ExportingDataMessage => LocalizationService.Instance["ExportingData"]; // Exporting data...

        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"]; 
        public string ErrorSavingVehicle => LocalizationService.Instance["ErrorSavingVehicle"]; // Error saving vehicle: {0}
        public string InfoTitle => LocalizationService.Instance["InfoTitle"]; // Info

        public string InfoNoVehicleToExport => LocalizationService.Instance["InfoNoVehicleToExport"]; // There is no vehicle data to export.
        public string ExportCompleteTitle => LocalizationService.Instance["ExportCompleteTitle"]; // Export Complete
        

        #endregion

        #region Services
        private IVehicleService _vehicleService;
        private IVehicleGroupService _vehicleGroupService;
        private ICapacityDetailTypeService _capacityDetailTypeService;
        private IVehicleTypeService _vehicleTypeService;
        #endregion

        #region Properties
        private ObservableCollection<Vehicle> _allVehicles; 
        private ObservableCollection<Vehicle> _vehicles; 
        public ObservableCollection<Vehicle> Vehicles
        {
            get => _vehicles;
            set => SetProperty(ref _vehicles, value);
        }

        private Vehicle _selectedVehicle;
        public Vehicle SelectedVehicle
        {
            get => _selectedVehicle;
            set => SetProperty(ref _selectedVehicle, value);
        }

        public ObservableCollection<VehicleGroup> Groups { get; set; }

        private VehicleGroup _selectedGroup;
        public VehicleGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                    FilterVehicles();
            }
        }

        private bool _showInactive = false;
        public bool ShowInactive
        {
            get => _showInactive;
            set
            {
                if (SetProperty(ref _showInactive, value))
                    FilterVehicles();
            }
        }

        public ObservableCollection<VehicleGroup> AllGroups { get; set; }
        public ObservableCollection<CapacityDetailType> AllCapacityTypes { get; set; }
        public ObservableCollection<VehicleType> AllVehicleTypes { get; set; }

        #endregion

        #region UI State Properties

        private bool _isLoading; 
        public bool IsLoading
        {
            get => _isLoading;
            //set => SetProperty(ref _isLoading, value);
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set => SetProperty(ref _isExporting, value);
        }

        #endregion

        #region Commands
        public ICommand LoadDataCommand { get; }
        public ICommand AddVehicleCommand { get; }
        public ICommand EditVehicleCommand { get; }
        public ICommand DeleteVehicleCommand { get; }
        public ICommand ExportToExcelCommand { get; }

        #endregion

        // Property for the initialization task
        public Task Initialization { get; private set; }
        public VehiclesViewModel()
        {
            // 1. Initialize everything that is synchronous and safe
            _allVehicles = new ObservableCollection<Vehicle>();
            Vehicles = new ObservableCollection<Vehicle>();
            Groups = new ObservableCollection<VehicleGroup>();
            AllCapacityTypes = new ObservableCollection<CapacityDetailType>();
            AllVehicleTypes = new ObservableCollection<VehicleType>();

            AddVehicleCommand = new RelayCommandObject(param => OpenVehiclePopup(null), param => !IsLoading);
            EditVehicleCommand = new RelayCommandObject(param => OpenVehiclePopup(param as Vehicle), param => !IsLoading && param is Vehicle);
            DeleteVehicleCommand = new RelayCommandObject(param => DeleteVehicleAsync(param as Vehicle), param => !IsLoading && param is Vehicle);
            ExportToExcelCommand = new RelayCommandObject(async param => await ExportToExcelAsync(), param => !IsExporting && !IsLoading);

            // 2. Launch asynchronous initialization
            Initialization = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            // The UI thread is here.
            IsLoading = true;
            try
            {
                // Initialize the services
                _vehicleService = new VehicleService();
                _vehicleGroupService = new VehicleGroupService();
                _capacityDetailTypeService = new CapacityDetailTypeService();
                _vehicleTypeService = new VehicleTypeService();

                // Call the service methods. Since we are in the UI thread,
                // `await` will release the thread while waiting for network response
                // and will return to it when finished, avoiding deadlocks.
                var groupsTask = _vehicleGroupService.GetGroupsAsync();
                var capacityTypesTask = _capacityDetailTypeService.GetCapacityDetailTypesAsync();
                var vehicleTypesTask = _vehicleTypeService.GetVehicleTypesAsync();

                // Wait for all lookup tasks to finish
                await Task.WhenAll(groupsTask, capacityTypesTask, vehicleTypesTask);

                // Now that we are back in the UI thread, it can be modified
                // collections directly, without the need for Dispatcher.
                var groupsResult = await groupsTask;
                Groups.Clear();
                foreach (var group in groupsResult) Groups.Add(group);
                AllGroups = Groups;

                var capacityTypesResult = await capacityTypesTask;
                AllCapacityTypes.Clear();
                foreach (var type in capacityTypesResult) AllCapacityTypes.Add(type);

                var vehicleTypesResult = await vehicleTypesTask;
                AllVehicleTypes.Clear();
                foreach (var type in vehicleTypesResult) AllVehicleTypes.Add(type);
                
                var vehiclesList = await _vehicleService.GetVehiclesAsync();
                _allVehicles = new ObservableCollection<Vehicle>(vehiclesList);

                FilterVehicles();
            }
            catch (Exception ex)
            {               
                MessageBox.Show($"Failed to load data: {ex.Message}\n\n{ex.InnerException?.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {              
                IsLoading = false;
            }
        }

        private void FilterVehicles()
        {
            IEnumerable<Vehicle> filtered = _allVehicles;

            if (SelectedGroup != null)
            {
                filtered = filtered.Where(v => v.GroupId == SelectedGroup.Id);
            }

            if (!ShowInactive)
            {
                filtered = filtered.Where(v => !v.IsInactive);
            }

            Vehicles = new ObservableCollection<Vehicle>(filtered.OrderBy(v => v.Name));
        }

        private async void OpenVehiclePopup(Vehicle vehicle)
        {
            bool isNew = vehicle == null;
            var vehicleToEdit = isNew ? new Vehicle() : (Vehicle)vehicle.Clone();

            var vm = new VehicleEditViewModel(vehicleToEdit, AllGroups, AllCapacityTypes, AllVehicleTypes);
            var view = new VehicleEditView { DataContext = vm};

            //var view = new VehicleEditView { DataContext = vm, Owner = Application.Current.MainWindow };          

            if (view.ShowDialog() == true)
            {
                try
                {
                    if (isNew)
                    {
                        var addedVehicle = await _vehicleService.AddVehicleAsync(vm.Vehicle);
                        _allVehicles.Add(addedVehicle);
                    }
                    else
                    {
                        await _vehicleService.UpdateVehicleAsync(vm.Vehicle.Id, vm.Vehicle);
                        var originalVehicle = _allVehicles.FirstOrDefault(v => v.Id == vm.Vehicle.Id);
                        if (originalVehicle != null)
                        {
                            var index = _allVehicles.IndexOf(originalVehicle);
                            _allVehicles[index] = vm.Vehicle; 
                            //_allVehicles[index] = await _vehicleService.GetVehicleAsync(vm.Vehicle.Id);
                        }
                    }
                    InitializeAsync();
                    //FilterVehicles(); // 
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        string.Format(ErrorSavingVehicle, ex.Message),
                        ErrorTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    //MessageBox.Show($"Error saving vehicle: {ex.Message}", "Error");
                }
            }
        }

        /*private async void AddVehicle()
        {
            var newVehicle = new Vehicle();
            var vm = new VehicleEditViewModel(newVehicle, Groups);
            var view = new VehicleEditView { DataContext = vm, Owner = Application.Current.MainWindow };

            if (view.ShowDialog() == true)
            {
                try
                {
                    var addedVehicle = await _vehicleService.AddVehicleAsync(vm.Vehicle);
                    _allVehicles.Add(addedVehicle);
                    FilterVehicles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error adding vehicle: {ex.Message}", "Error");
                }
            }
        }*/

        /*private async void EditVehicle(Vehicle vehicleToEdit)
         {
             if (vehicleToEdit == null) return;

             // Clonar el objeto para no modificar la lista original hasta guardar
             var vehicleClone = (Vehicle)vehicleToEdit.Clone(); // Necesitarás implementar ICloneable en tu modelo

             var vm = new VehicleEditViewModel(vehicleClone, Groups);
             var view = new VehicleEditView { DataContext = vm, Owner = Application.Current.MainWindow };

             if (view.ShowDialog() == true)
             {
                 try
                 {
                     await _vehicleService.UpdateVehicleAsync(vm.Vehicle.Id, vm.Vehicle);
                     // Actualizar el objeto original en la colección maestra
                     var originalVehicle = _allVehicles.FirstOrDefault(v => v.Id == vm.Vehicle.Id);
                     if (originalVehicle != null)
                     {
                         var index = _allVehicles.IndexOf(originalVehicle);
                         _allVehicles[index] = vm.Vehicle;
                     }
                     FilterVehicles();
                 }
                 catch (Exception ex)
                 {
                     MessageBox.Show($"Error updating vehicle: {ex.Message}", "Error");
                 }
             }
         }*/

        private async void DeleteVehicleAsync(Vehicle vehicleToDelete)
        {
            if (vehicleToDelete == null) return;

            var confirmationText = string.Format(LocalizationService.Instance["ConfirmDeleteVehicleText"], vehicleToDelete.Name); // ej: "Are you sure you want to delete the vehicle '{0}'?"
            var confirmationTitle = LocalizationService.Instance["ConfirmDeleteTitle"]; // ej: "Confirm Delete"

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _vehicleService.DeleteVehicleAsync(vehicleToDelete.Id);
                    _allVehicles.Remove(vehicleToDelete);
                    FilterVehicles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                            string.Format(LocalizationService.Instance["ErrorDeletingVehicle"], ex.Message), // ej: "Error deleting vehicle: {0}"
                            ErrorTitle,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    //MessageBox.Show($"Error deleting vehicle: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExportToExcelAsync()
        {
            if (!_allVehicles.Any())
            {
                MessageBox.Show(InfoNoVehicleToExport, InfoTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"AllVehicles_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (sfd.ShowDialog() != true)
            {
                return; // User canceled
            }

            IsExporting = true;
            // Force re-evaluation of the CanExecute command to disable the button
            CommandManager.InvalidateRequerySuggested();

            try
            {
                // Run the CPU/IO intensive task in a background thread to avoid freezing the UI.
                await Task.Run(() =>
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("Vehicles");

                        // Headers for all properties of the Vehicle object
                        int col = 1;
                        worksheet.Cell(1, col++).Value = "ID";
                        worksheet.Cell(1, col++).Value = "Name";
                        worksheet.Cell(1, col++).Value = "VIN";
                        worksheet.Cell(1, col++).Value = "Make";
                        worksheet.Cell(1, col++).Value = "Model";
                        worksheet.Cell(1, col++).Value = "Color";
                        worksheet.Cell(1, col++).Value = "Year";
                        worksheet.Cell(1, col++).Value = "Plate";
                        worksheet.Cell(1, col++).Value = "Expiration Date";
                        worksheet.Cell(1, col++).Value = "Is Inactive";
                        worksheet.Cell(1, col++).Value = "Group";
                        worksheet.Cell(1, col++).Value = "Capacity Type";
                        worksheet.Cell(1, col++).Value = "Vehicle Type";


                        var headerRow = worksheet.Row(1);
                        headerRow.Style.Font.Bold = true;
                        headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;
                        worksheet.SheetView.FreezeRows(1);

                        int currentRow = 2;
                        foreach (var vehicle in _allVehicles.OrderBy(v => v.Name))
                        {
                            col = 1;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Id;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Name;
                            worksheet.Cell(currentRow, col++).Value = vehicle.VIN;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Make;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Model;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Color;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Year;
                            worksheet.Cell(currentRow, col++).Value = vehicle.Plate;

                            // Handling nulls for date
                            var expDateCell = worksheet.Cell(currentRow, col++);
                            if (vehicle.ExpirationDate.HasValue)
                                expDateCell.Value = vehicle.ExpirationDate.Value;
                            else
                                expDateCell.Value = ""; 

                            // ISO 8601
                            expDateCell.Style.DateFormat.Format = "yyyy-MM-dd";

                            worksheet.Cell(currentRow, col++).Value = vehicle.IsInactive;

                            // Null handling for related objects
                            worksheet.Cell(currentRow, col++).Value = vehicle.VehicleGroup?.Name;
                            worksheet.Cell(currentRow, col++).Value = vehicle.CapacityDetailType?.Name;
                            worksheet.Cell(currentRow, col++).Value = vehicle.VehicleType?.Name;
                            currentRow++;
                        }

                        worksheet.Columns().AdjustToContents();
                        workbook.SaveAs(sfd.FileName);
                    }
                });

                IsExporting = false;
                // Return to the UI thread to show the success message
                var InfoSuccessExportedVehicles = string.Format(LocalizationService.Instance["InfoSuccessExportedVehicles"], _allVehicles.Count); // "Successfully exported {0} vehicles"
                MessageBox.Show(InfoSuccessExportedVehicles, ExportCompleteTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                //MessageBox.Show($"Successfully exported {(_allVehicles.Count)} vehicles.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show($"Error exporting to Excel: The file '{Path.GetFileName(sfd.FileName)}' might be open. Please close it and try again.\n\nDetails: {ioEx.Message}", "File In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // This block is ALWAYS executed, whether there is success or error.
                IsExporting = false;
                // We enable the button again
                CommandManager.InvalidateRequerySuggested();
            }
        }
        
    }
}