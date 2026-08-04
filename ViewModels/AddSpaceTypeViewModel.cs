using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Wordprocessing;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows; // Para MessageBox, aunque es mejor usar un servicio de diálogo

namespace Raphael.Desktop.ViewModels
{
    // Heredamos de ObservableObject para que [ObservableProperty] funcione
    public partial class AddSpaceTypeViewModel : ObservableObject
    {
        public event EventHandler<bool> RequestClose;

        #region Observable Properties
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))] // Notifica a SaveCommand cuando 'Name' cambia
        private string _name;

        [ObservableProperty]
        private string _description;

        [ObservableProperty]
        private float _loadTime;

        [ObservableProperty]
        private float _unloadTime;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))] // Notifica a SaveCommand cuando 'SelectedCapacityType' cambia
        private CapacityType _selectedCapacityType;

        // Propiedad para la lista de CapacityTypes (no necesita ObservableProperty en sí misma, pero su contenido sí)
        public ObservableCollection<CapacityType> CapacityTypes { get; set; } = new();

        public SpaceType NewSpaceType { get; private set; }
        #endregion

        public AddSpaceTypeViewModel()
        {
            // Carga asíncrona de datos en el constructor
            _ = LoadCapacityTypesAsync();
        }

        private async Task LoadCapacityTypesAsync()
        {
            try
            {
                var capacityTypeService = new CapacityTypeService();
                var types = await capacityTypeService.GetCapacityTypesAsync();
                foreach (var type in types)
                {
                    CapacityTypes.Add(type);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading capacity types: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Comando para guardar, automáticamente genera SaveCommand e implementa ICommand
        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Save()
        {
            NewSpaceType = new SpaceType
            {
                Name = Name,
                Description = Description,
                LoadTime = LoadTime,
                UnloadTime = UnloadTime,
                CapacityTypeId = SelectedCapacityType.Id,
                IsActive = true
            };
            OnRequestClose(true);
        }

        // Método que determina si el comando Save puede ejecutarse
        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(Name) && SelectedCapacityType != null;
        }

        // Comando para cancelar, automáticamente genera CancelCommand
        [RelayCommand]
        private void Cancel()
        {
            OnRequestClose(false);
        }

        private void OnRequestClose(bool dialogResult)
        {
            RequestClose?.Invoke(this, dialogResult);
        }
    }
}