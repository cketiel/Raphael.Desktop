using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Data.Scheduling.Vehicles;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows; // Para MessageBox, aunque es mejor usar un servicio de diálogo

namespace Raphael.Desktop.ViewModels
{
    // Heredamos de ObservableObject y marcamos como partial
    public partial class SpaceTypesViewModel : ObservableObject
    {
        #region Services
        private readonly SpaceTypeService _spaceTypeService;
        #endregion

        #region Translation
        public string SpaceTypeNameText => LocalizationService.Instance["SpaceTypeName"];
        public string DescriptionText => LocalizationService.Instance["Description"];
        public string LoadTimeText => LocalizationService.Instance["LoadTime"];
        public string UnloadTimeText => LocalizationService.Instance["UnloadTime"];
        public string CapacityTypeText => LocalizationService.Instance["CapacityType"];
        public string InactiveText => LocalizationService.Instance["Inactive"];
        public string AddSpaceTypeToolTip => LocalizationService.Instance["AddSpaceType"];
        public string DeleteSpaceTypeToolTip => LocalizationService.Instance["DeleteSpaceType"];
        #endregion

        // Propiedad ObservableCollection (no necesita [ObservableProperty] directamente)
        public ObservableCollection<SpaceType> SpaceTypes { get; set; } = new();

        // Propiedad observable para el SpaceType seleccionado
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSpaceTypeCommand))] // Notifica a DeleteSpaceTypeCommand cuando cambia
        private SpaceType _selectedSpaceType;

        public SpaceTypesViewModel()
        {
            _spaceTypeService = new SpaceTypeService();
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            await LoadSpaceTypesAsync();
        }

        public async Task LoadSpaceTypesAsync()
        {
            try
            {
                var sources = await _spaceTypeService.GetSpaceTypesAsync();
                SpaceTypes.Clear();
                foreach (var source in sources)
                {
                    SpaceTypes.Add(source);
                }
            }
            catch (ApiException ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Comando asíncrono para añadir
        [RelayCommand] // No necesita CanExecute, ya que siempre se puede añadir
        private async Task AddSpaceTypeAsync()
        {
            var addVM = new AddSpaceTypeViewModel();
            var addView = new AddSpaceTypeView(addVM);

            if (addView.ShowDialog() == true)
            {
                try
                {
                    var createdSpaceType = await _spaceTypeService.CreateSpaceTypeAsync(addVM.NewSpaceType);
                    SpaceTypes.Add(createdSpaceType);
                }
                catch (ApiException ex)
                {
                    MessageBox.Show($"Error saving the new Space Type: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Comando asíncrono para eliminar, con condición CanExecute
        [RelayCommand(CanExecute = nameof(CanDeleteSpaceType))]
        private async Task DeleteSpaceTypeAsync()
        {
            // La condición CanExecute ya previene que esto se llame si SelectedSpaceType es null,
            // pero una doble verificación no hace daño.
            if (SelectedSpaceType == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete '{SelectedSpaceType.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _spaceTypeService.DeleteSpaceTypeAsync(SelectedSpaceType.Id);
                    SpaceTypes.Remove(SelectedSpaceType);
                }
                catch (ApiException ex)
                {
                    MessageBox.Show($"Error deleting Space Type: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Método que determina si el comando Delete puede ejecutarse
        private bool CanDeleteSpaceType()
        {
            return SelectedSpaceType != null;
        }
    }
}