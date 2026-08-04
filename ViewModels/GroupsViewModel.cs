using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services; 
using Raphael.Desktop.Views.Data.Scheduling.Vehicles; 
using System.Windows.Input;
using System.Windows; 

namespace Raphael.Desktop.ViewModels
{
    public class GroupsViewModel : BaseViewModel
    {
        #region Translation
        public string GroupNameText => LocalizationService.Instance["GroupNameText"];
        public string DescriptionText => LocalizationService.Instance["Description"];
        public string GroupColorText => LocalizationService.Instance["GroupColorText"];
        public string AddGroupToolTip => LocalizationService.Instance["AddGroupToolTip"];
        public string DeleteGroupToolTip => LocalizationService.Instance["DeleteGroupToolTip"];
   
        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"]; // ej: "Error"
        public string SuccessTitle => LocalizationService.Instance["SuccessTitle"]; // ej: "Éxito"
        public string GroupAddedSuccessfullyText => LocalizationService.Instance["GroupAddedSuccessfully"]; // ej: "Grupo añadido correctamente."
        public string ErrorAddingGroupText => LocalizationService.Instance["ErrorAddingGroup"]; // ej: "Ocurrió un error al añadir el grupo: {0}"
        #endregion

        public ObservableCollection<VehicleGroup> Groups { get; set; } = new ObservableCollection<VehicleGroup>();

        private VehicleGroup _selectedGroup;
        public VehicleGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                _selectedGroup = value;
                OnPropertyChanged();
            }
        }

        public ICommand AddGroupCommand { get; }
        public ICommand DeleteGroupCommand { get; }

        private readonly VehicleGroupService _vehicleGroupService;

        public GroupsViewModel()
        {
            _vehicleGroupService = new VehicleGroupService(); 
            AddGroupCommand = new RelayCommandObject(async _ => await AddGroupAsync());
            DeleteGroupCommand = new RelayCommandObject(DeleteGroup, _ => SelectedGroup != null); // Enabled if a group is selected
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            await LoadGroupsAsync();
        }

        public async Task LoadGroupsAsync()
        {
            try
            {
                var sources = await _vehicleGroupService.GetGroupsAsync();
                Groups.Clear();
                foreach (var source in sources)
                {
                    Groups.Add(source);
                }
            }
            catch (Exception ex)
            {              
                Console.WriteLine($"Error loading groups: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationService.Instance["ErrorLoadingData"], ex.Message), // ej: "Error al cargar datos: {0}"
                    ErrorTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task AddGroupAsync()
        {
            var addGroupVM = new AddEditGroupViewModel();
            var addGroupWindow = new AddEditGroupWindow(addGroupVM)
            {
                //Owner = Application.Current.MainWindow // Para que se centre sobre la ventana principal
            };

            if (addGroupWindow.ShowDialog() == true)
            {
                VehicleGroup newGroup = addGroupVM.CurrentGroup;
                try
                {                   
                    VehicleGroup addedGroup = await _vehicleGroupService.CreateGroupAsync(newGroup);
                  
                    Groups.Add(addedGroup); // Add to observable collection
                    SelectedGroup = addedGroup;

                    MessageBox.Show(
                        GroupAddedSuccessfullyText,
                        SuccessTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {                   
                    Console.WriteLine($"Error adding group: {ex.Message}");                   
                    MessageBox.Show(
                        string.Format(ErrorAddingGroupText, ex.Message),
                        ErrorTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteGroup(object groupObject)
        {
            if (groupObject is VehicleGroup groupToDelete)
            {
                // Confirmation before deletion
                var confirmationText = string.Format(LocalizationService.Instance["ConfirmDeleteGroupText"], groupToDelete.Name); // ej: "¿Está seguro de que desea eliminar el grupo '{0}'?"
                var confirmationTitle = LocalizationService.Instance["ConfirmDeleteTitle"]; // ej: "Confirmar eliminación"

                if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {                       
                        await _vehicleGroupService.DeleteGroupAsync(groupToDelete.Id);
                        Groups.Remove(groupToDelete);
                        MessageBox.Show(
                            LocalizationService.Instance["GroupDeletedSuccessfully"], // ej: "Grupo eliminado correctamente."
                            SuccessTitle,
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deleting group: {ex.Message}");
                        MessageBox.Show(
                            string.Format(LocalizationService.Instance["ErrorDeletingGroup"], ex.Message), // ej: "Error al eliminar el grupo: {0}"
                            ErrorTitle,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        } // end method
    } // end class
} // end namespace