using System.Collections.ObjectModel;
using System.Windows.Input;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public class VehicleEditViewModel : BaseViewModel
    {
        #region Translation
        public string AddNewVehicleTitle => LocalizationService.Instance["AddNewVehicle"]; // Add Vehicle
        public string EditVehicleTitle => LocalizationService.Instance["EditVehicle"]; // Update Vehicle
        #endregion
        private Vehicle _vehicle;
        public Vehicle Vehicle
        {
            get => _vehicle;
            set => SetProperty(ref _vehicle, value);
        }

        public string Title { get; }
       
        public ObservableCollection<VehicleGroup> AllGroups { get; }
        public ObservableCollection<CapacityDetailType> AllCapacityTypes { get; }
        public ObservableCollection<VehicleType> AllVehicleTypes { get; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }      
        public VehicleEditViewModel(Vehicle vehicle,
                                    ObservableCollection<VehicleGroup> groups,
                                    ObservableCollection<CapacityDetailType> capacityTypes,
                                    ObservableCollection<VehicleType> vehicleTypes)
        {
            Vehicle = vehicle;
            AllGroups = groups;
            AllCapacityTypes = capacityTypes;
            AllVehicleTypes = vehicleTypes;

            Title = vehicle.Id == 0 ? AddNewVehicleTitle : EditVehicleTitle;

            SaveCommand = new RelayCommandObject(o => OnSave?.Invoke(), o => CanSave());
            CancelCommand = new RelayCommandObject(o => OnCancel?.Invoke());
        }

        public event System.Action OnSave;
        public event System.Action OnCancel;

        private bool CanSave()
        {
            // DataAnnotations can be used for more robust validation
            return !string.IsNullOrWhiteSpace(Vehicle.Name) &&
                   Vehicle.GroupId > 0 &&
                   Vehicle.CapacityDetailTypeId > 0 &&
                   Vehicle.VehicleTypeId > 0;
        }
    }
}