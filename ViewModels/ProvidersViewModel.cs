using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Views.Admin;

namespace Raphael.Desktop.ViewModels
{
    public class ProvidersViewModel : BaseViewModel
    {
        private readonly IProviderService _service;

        /// <summary>
        /// Shown against a provider that has not declared a timezone, which means its trips
        /// fall back to the server's configured default.
        /// </summary>
        public string NoTimeZoneText =>
            Services.LocalizationService.Instance["ProviderNoTimeZone"];

        private ObservableCollection<Provider> _providers;
        public ObservableCollection<Provider> Providers
        {
            get => _providers;
            set => SetProperty(ref _providers, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }

        public ProvidersViewModel()
        {
            _service = new ProviderService();
            Providers = new ObservableCollection<Provider>();

            AddCommand = new RelayCommandObject(_ => OpenEditPopup(null));
            EditCommand = new RelayCommandObject(p => OpenEditPopup(p as Provider));
            DeleteCommand = new RelayCommandObject(p => DeleteAsync(p as Provider));

            LoadData();
        }

        public async void LoadData()
        {
            IsLoading = true;
            try
            {
                var list = await _service.GetProvidersAsync();
                Providers = new ObservableCollection<Provider>(list.OrderBy(x => x.Name));
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show($"Error loading providers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsLoading = false; }
        }

        private async void OpenEditPopup(Provider item)
        {
            bool isNew = item == null;
            var clone = isNew ? new Provider() : (Provider)item.Clone();
            var vm = new ProviderEditViewModel(clone);
          
            var view = new ProviderEditView { DataContext = vm/*, Owner = Application.Current.MainWindow */};

            view.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (view.ShowDialog() == true)
            {
                try
                {
                    var id = vm.Provider.Id;
                    if (isNew)
                        id = (await _service.AddProviderAsync(vm.Provider, vm.SelectedImagePath))?.Id ?? 0;
                    else
                        await _service.UpdateProviderAsync(vm.Provider.Id, vm.Provider, vm.SelectedImagePath);

                    // Client company of Raphael (RE-049): its own call, only when it changed.
                    var wasTenant = !isNew && item.IsTenant;
                    if (id > 0 && vm.Provider.IsTenant != wasTenant)
                        await _service.SetTenantAsync(id, vm.Provider.IsTenant);

                    LoadData();
                }
                catch (Exception ex)
                {
                    Helpers.UiError.Show($"Error saving provider: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteAsync(Provider item)
        {
            if (item == null) return;

            if (MessageBox.Show(
                string.Format(ConfirmDeleteText, item.Name),
                ConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _service.DeleteProviderAsync(item.Id);
                    LoadData();
                }
                catch (Exception ex)
                {
                    Helpers.UiError.Show(ex.Message, DeleteFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #region Translation

        public string AddText => LocalizationService.Instance["admin.AddProvider"];
        public string EditText => LocalizationService.Instance["Edit"];
        public string DeleteText => LocalizationService.Instance["admin.DeleteProvider"];

        public string ColumnLogo => LocalizationService.Instance["admin.Logo"];
        public string ColumnCompany => LocalizationService.Instance["Company"];
        public string ColumnPhone => LocalizationService.Instance["Phone"];
        public string ColumnEmail => LocalizationService.Instance["Email"];
        public string ColumnAddress => LocalizationService.Instance["Address"];
        public string ColumnTimeZone => LocalizationService.Instance["admin.TimeZoneColumn"];

        public string ConfirmDeleteText => LocalizationService.Instance["admin.ConfirmDeleteProvider"];
        public string ConfirmTitle => LocalizationService.Instance["ConfirmDeleteTitle"];
        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"];
        public string DeleteFailedTitle => LocalizationService.Instance["admin.DeleteFailed"];

        #endregion

    }
}