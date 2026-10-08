using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    public partial class BillingItemPopupViewModel : ObservableObject
    {
        private readonly IBillingItemService _billingItemService;
        private readonly IUnitService _unitService;

        [ObservableProperty]
        private BillingItem _currentBillingItem;

        [ObservableProperty]
        private ObservableCollection<Unit> _availableUnits;

        public IAsyncRelayCommand SaveCommand { get; }
        public bool IsNewItem => CurrentBillingItem.Id == 0;

        /// <summary>The item belongs to a clinic, which keeps it from the Booking Portal (RE-029).</summary>
        public bool IsClinicOwned => !CurrentBillingItem.IsOfficeOwned;

        /// <summary>
        /// Said before the office edits a clinic's item: the clinic sees the change, and saving here
        /// does not take the item away from it.
        /// </summary>
        public string ClinicOwnedNotice => IsClinicOwned
            ? string.Format(LocalizationService.Instance["BillingItemManagedByClinic"], CurrentBillingItem.OwnerGroup)
            : string.Empty;

        public BillingItemPopupViewModel(IBillingItemService billingItemService, IUnitService unitService, BillingItem billingItem)
        {
            _billingItemService = billingItemService;
            _unitService = unitService; 

            _availableUnits = new ObservableCollection<Unit>();

            _currentBillingItem = new BillingItem
            {
                Id = billingItem.Id,
                Description = billingItem.Description,
                UnitId = billingItem.UnitId,
                IsCopay = billingItem.IsCopay,
                ARAccount = billingItem.ARAccount,
                ARSubAccount = billingItem.ARSubAccount,
                ARCompany = billingItem.ARCompany,
                APAccount = billingItem.APAccount,
                APSubAccount = billingItem.APSubAccount,
                APCompany = billingItem.APCompany,
                OwnerIntegratorId = billingItem.OwnerIntegratorId,
                OwnerIntegratorName = billingItem.OwnerIntegratorName
            };

            SaveCommand = new AsyncRelayCommand(SaveAsync);
           
            _ = LoadUnitsAsync();
        }

        private async Task LoadUnitsAsync()
        {
            try
            {
                var units = await _unitService.GetUnitsAsync();
                AvailableUnits.Clear();
                foreach (var unit in units)
                {
                    AvailableUnits.Add(unit);
                }
            }
            catch (System.Exception ex)
            {
                Helpers.UiError.Show($"Could not load units: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentBillingItem.Description))
            {
                MessageBox.Show("Description is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; 
            }

            if (CurrentBillingItem.UnitId <= 0)
            {
                MessageBox.Show("You must select a Unit.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; 
            }

            try
            {
                if (IsNewItem)
                {
                    await _billingItemService.CreateBillingItemAsync(CurrentBillingItem);
                }
                else
                {
                    await _billingItemService.UpdateBillingItemAsync(CurrentBillingItem);
                }

                // Closes the window indicating that the operation was successful.
                OnRequestClose(true);
            }
            catch (System.Exception ex)
            {
                Helpers.UiError.Show($"Error saving item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Event to communicate to the View (the window) that it should be closed.
        public event System.Action<bool> RequestClose;

        private void OnRequestClose(bool dialogResult)
        {
            RequestClose?.Invoke(dialogResult);
        }
    }
}