// Raphael.Desktop/ViewModels/FundingSourceBillingItemPopupViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    public partial class FundingSourceBillingItemPopupViewModel : ObservableObject
    {
        // --- Servicios Inyectados ---
        private readonly IFundingSourceBillingItemService _fsbiService;
        private readonly IBillingItemService _billingItemService;
        private readonly ISpaceTypeService _spaceTypeService;

        // --- Propiedades para la Vista ---

        [ObservableProperty]
        private FundingSourceBillingItem _currentItem;

        [ObservableProperty]
        private ObservableCollection<BillingItem> _availableBillingItems;

        [ObservableProperty]
        private ObservableCollection<SpaceType> _availableSpaceTypes;

        public bool IsNewItem => CurrentItem.Id == 0;

        // --- Comandos ---
        public IAsyncRelayCommand SaveCommand { get; }

        // --- Evento para la Comunicación con la Vista ---
        public event Action<bool> RequestClose;

        public FundingSourceBillingItemPopupViewModel(
            IFundingSourceBillingItemService fsbiService,
            IBillingItemService billingItemService,  // Dependencia para el ComboBox
            ISpaceTypeService spaceTypeService,      // Dependencia para el ComboBox
            FundingSourceBillingItem itemToEdit)
        {
            _fsbiService = fsbiService;
            _billingItemService = billingItemService;
            _spaceTypeService = spaceTypeService;

            // 1. Clonar el objeto para una edición segura (evita cambios en el grid antes de guardar)
            _currentItem = new FundingSourceBillingItem
            {
                Id = itemToEdit.Id,
                FundingSourceId = itemToEdit.FundingSourceId,
                BillingItemId = itemToEdit.BillingItemId,
                SpaceTypeId = itemToEdit.SpaceTypeId,
                Rate = itemToEdit.Rate,
                Per = itemToEdit.Per,
                IsDefault = itemToEdit.IsDefault,
                ProcedureCode = itemToEdit.ProcedureCode,
                MinCharge = itemToEdit.MinCharge,
                MaxCharge = itemToEdit.MaxCharge,
                GreaterThanMinQty = itemToEdit.GreaterThanMinQty,
                LessOrEqualMaxQty = itemToEdit.LessOrEqualMaxQty,
                FreeQty = itemToEdit.FreeQty,
                FromDate = itemToEdit.FromDate,
                ToDate = itemToEdit.ToDate
            };

            _availableBillingItems = new ObservableCollection<BillingItem>();
            _availableSpaceTypes = new ObservableCollection<SpaceType>();

            SaveCommand = new AsyncRelayCommand(SaveAsync);

            // 2. Cargar los datos para los ComboBox de forma asíncrona
            _ = LoadDependenciesAsync();
        }

        private async Task LoadDependenciesAsync()
        {
            try
            {
                // Cargar los Billing Items disponibles (como DTOs)
                var billingItemsDto = await _billingItemService.GetBillingItemsAsync();
                AvailableBillingItems.Clear();

                // --- INICIO DE LA CORRECCIÓN ---
                // Iteramos sobre la lista de DTOs y los convertimos a Modelos
                foreach (var dto in billingItemsDto)
                {
                    // Creamos una nueva instancia del Modelo 'BillingItem' a partir del DTO
                    var model = new BillingItem
                    {
                        Id = dto.Id,
                        Description = dto.Description,
                        UnitId = dto.UnitId,
                        // Reconstruimos la parte mínima del objeto Unit que el DTO nos da, si es necesario.
                        // Para el ComboBox, con Id y Description en el objeto principal es suficiente.
                    };
                    AvailableBillingItems.Add(model);
                }
                // --- FIN DE LA CORRECCIÓN ---

                // Cargar los Space Types disponibles (estos probablemente ya devuelven el modelo completo, así que no necesitan cambios)
                var spaceTypes = await _spaceTypeService.GetSpaceTypesAsync();
                AvailableSpaceTypes.Clear();
                foreach (var type in spaceTypes)
                {
                    AvailableSpaceTypes.Add(type);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load required data for the form: {ex.Message}", "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveAsync()
        {
            // 3. Validación de los datos antes de enviar
            if (CurrentItem.BillingItemId <= 0)
            {
                MessageBox.Show("You must select a Billing Item.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (CurrentItem.SpaceTypeId <= 0)
            {
                MessageBox.Show("You must select a Space Type.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 4. Mapear el modelo de la vista a un DTO para la API
                var dto = new FundingSourceBillingItemDto
                {
                    Id = CurrentItem.Id,
                    FundingSourceId = CurrentItem.FundingSourceId,
                    BillingItemId = CurrentItem.BillingItemId,
                    SpaceTypeId = CurrentItem.SpaceTypeId,
                    Rate = CurrentItem.Rate,
                    Per = CurrentItem.Per,
                    IsDefault = CurrentItem.IsDefault,
                    ProcedureCode = CurrentItem.ProcedureCode,
                    MinCharge = CurrentItem.MinCharge,
                    MaxCharge = CurrentItem.MaxCharge,
                    GreaterThanMinQty = CurrentItem.GreaterThanMinQty,
                    LessOrEqualMaxQty = CurrentItem.LessOrEqualMaxQty,
                    FreeQty = CurrentItem.FreeQty,
                    FromDate = CurrentItem.FromDate,
                    ToDate = CurrentItem.ToDate
                };

                // 5. Llamar al método de crear o actualizar del servicio
                if (IsNewItem)
                {
                    await _fsbiService.CreateAsync(dto);
                }
                else
                {
                    await _fsbiService.UpdateAsync(CurrentItem.Id, dto);
                }

                // 6. Invocar el evento para cerrar la ventana indicando éxito
                RequestClose?.Invoke(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}