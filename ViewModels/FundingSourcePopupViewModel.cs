// Raphael.Desktop/ViewModels/FundingSourcePopupViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    public partial class FundingSourcePopupViewModel : ObservableObject
    {
        private readonly IFundingSourceService _fundingSourceService;

        [ObservableProperty]
        private FundingSource _currentFundingSource;

        public bool IsNewItem => CurrentFundingSource.Id == 0;

        public IAsyncRelayCommand SaveCommand { get; }
        public event System.Action<bool> RequestClose;

        public FundingSourcePopupViewModel(IFundingSourceService fundingSourceService, FundingSource fundingSource)
        {
            _fundingSourceService = fundingSourceService;
            // Clonamos el objeto para permitir cancelar la edición sin afectar el original
            _currentFundingSource = new FundingSource
            {
                Id = fundingSource.Id,
                Name = fundingSource.Name,
                AccountNumber = fundingSource.AccountNumber,
                Address = fundingSource.Address,
                Phone = fundingSource.Phone,
                FAX = fundingSource.FAX,
                Email = fundingSource.Email,
                ContactFirst = fundingSource.ContactFirst,
                ContactLast = fundingSource.ContactLast,
                SignaturePickup = fundingSource.SignaturePickup,
                SignatureDropoff = fundingSource.SignatureDropoff,
                DriverSignaturePickup = fundingSource.DriverSignaturePickup,
                DriverSignatureDropoff = fundingSource.DriverSignatureDropoff,
                RequireOdometer = fundingSource.RequireOdometer,
                BarcodeScanRequired = fundingSource.BarcodeScanRequired,
                VectorcareFacilityId = fundingSource.VectorcareFacilityId,
                IsActive = fundingSource.IsActive
            };
            SaveCommand = new AsyncRelayCommand(SaveAsync);
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentFundingSource.Name))
            {
                MessageBox.Show("Name is required.", "Validation Error");
                return;
            }

            // Mapeo completo desde el modelo de la vista al DTO
            var dto = new FundingSourceDto
            {
                Name = CurrentFundingSource.Name,
                AccountNumber = CurrentFundingSource.AccountNumber,
                Address = CurrentFundingSource.Address,
                Phone = CurrentFundingSource.Phone,
                FAX = CurrentFundingSource.FAX,
                Email = CurrentFundingSource.Email,
                ContactFirst = CurrentFundingSource.ContactFirst,
                ContactLast = CurrentFundingSource.ContactLast,
                SignaturePickup = CurrentFundingSource.SignaturePickup,
                SignatureDropoff = CurrentFundingSource.SignatureDropoff,
                DriverSignaturePickup = CurrentFundingSource.DriverSignaturePickup,
                DriverSignatureDropoff = CurrentFundingSource.DriverSignatureDropoff,
                RequireOdometer = CurrentFundingSource.RequireOdometer,
                BarcodeScanRequired = CurrentFundingSource.BarcodeScanRequired,
                VectorcareFacilityId = CurrentFundingSource.VectorcareFacilityId,
                IsActive = CurrentFundingSource.IsActive
            };

            try
            {
                if (IsNewItem)
                {
                    await _fundingSourceService.CreateFundingSourceAsync(dto);
                }
                else
                {
                    await _fundingSourceService.UpdateFundingSourceAsync(CurrentFundingSource.Id, dto);
                }
                RequestClose?.Invoke(true); // Cierra la ventana indicando éxito
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error saving data: {ex.Message}", "Error");
            }
        }
    }
}