
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views;
using Raphael.Desktop.Views.Admin.Billing;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    public partial class BillingItemsViewModel : ObservableObject
    {
        private readonly IBillingItemService _billingItemService;
        private readonly IUnitService _unitService;

        [ObservableProperty]
        private ObservableCollection<BillingItem> _billingItems;

        public IAsyncRelayCommand LoadBillingItemsCommand { get; }
        public IRelayCommand AddBillingItemCommand { get; }
        public IRelayCommand<BillingItem> EditBillingItemCommand { get; }

        public BillingItemsViewModel(IBillingItemService billingItemService, IUnitService unitService)
        {
            _billingItemService = billingItemService;
            _unitService = unitService; 

            _billingItems = new ObservableCollection<BillingItem>();

            LoadBillingItemsCommand = new AsyncRelayCommand(LoadBillingItemsAsync);
            AddBillingItemCommand = new RelayCommand(ExecuteAddBillingItem);
            EditBillingItemCommand = new RelayCommand<BillingItem>(ExecuteEditBillingItem);
        }

        private async Task LoadBillingItemsAsync()
        {
            try
            {            
                var itemsDto = await _billingItemService.GetBillingItemsAsync();

                BillingItems.Clear();
                
                foreach (var dto in itemsDto)
                {
                    BillingItems.Add(new BillingItem
                    {
                        Id = dto.Id,
                        Description = dto.Description,
                        IsCopay = dto.IsCopay,
                        ARAccount = dto.ARAccount,
                        ARSubAccount = dto.ARSubAccount,
                        ARCompany = dto.ARCompany,
                        APAccount = dto.APAccount,
                        APSubAccount = dto.APSubAccount,
                        APCompany = dto.APCompany,
                        UnitId = dto.UnitId,
                        // We rebuild the Unit object so that the DataGrid binding works
                        Unit = new Unit { Abbreviation = dto.UnitAbbreviation }
                    });
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"An error occurred while loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /*private async Task LoadBillingItemsAsync()
        {
            try
            {
                var items = await _billingItemService.GetBillingItemsAsync();
                BillingItems.Clear();
                foreach (var item in items)
                {
                    BillingItems.Add(item);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"An error occurred while loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }*/

        private void ExecuteAddBillingItem()
        {
            var newBillingItem = new BillingItem();          
            var popupViewModel = new BillingItemPopupViewModel(_billingItemService, _unitService, newBillingItem);
            var popupWindow = new BillingItemPopupWindow(popupViewModel);

            if (popupWindow.ShowDialog() == true)
            {
                LoadBillingItemsCommand.Execute(null);
            }
        }

        private void ExecuteEditBillingItem(BillingItem billingItem)
        {
            if (billingItem == null) return;
         
            var popupViewModel = new BillingItemPopupViewModel(_billingItemService, _unitService, billingItem);
            var popupWindow = new BillingItemPopupWindow(popupViewModel);

            if (popupWindow.ShowDialog() == true)
            {
                LoadBillingItemsCommand.Execute(null);
            }
        }

        #region Translation

        public string AddBillingItemToolTip => LocalizationService.Instance["AddBillingItem"];
        public string EditBillingItemToolTip => LocalizationService.Instance["EditBillingItem"];      

        public string ColumnHeaderDescription => LocalizationService.Instance["Description"];
        public string ColumnHeaderUnit => LocalizationService.Instance["Unit"];
        public string ColumnHeaderIsCopay => LocalizationService.Instance["IsCopay"]; // "Is Copay"
        public string ColumnHeaderARAccount => LocalizationService.Instance["ARAccount"]; // "AR Account"
        public string ColumnHeaderARSubAccount => LocalizationService.Instance["ARSubAccount"];// "AR Sub-Account"
        public string ColumnHeaderARCompany => LocalizationService.Instance["ARCompany"]; // "AR Company"
        public string ColumnHeaderAPAccount => LocalizationService.Instance["APAccount"]; // "AP Account"
        public string ColumnHeaderAPSubAccount => LocalizationService.Instance["APSubAccount"]; // "AP Sub-Account"
        public string ColumnHeaderAPCompany => LocalizationService.Instance["APCompany"]; // "AP Company"

        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"];
        public string ConfirmDeleteBillingItemText => LocalizationService.Instance["ConfirmDeleteBillingItem"];
        public string ConfirmDeleteTitle => LocalizationService.Instance["ConfirmDeleteTitle"];

        #endregion
    }
}