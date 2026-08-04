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
                MessageBox.Show($"Error loading providers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    if (isNew)
                        await _service.AddProviderAsync(vm.Provider, vm.SelectedImagePath);
                    else
                        await _service.UpdateProviderAsync(vm.Provider.Id, vm.Provider, vm.SelectedImagePath);

                    LoadData();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving provider: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteAsync(Provider item)
        {
            if (item == null) return;

            if (MessageBox.Show($"Are you sure you want to delete provider '{item.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _service.DeleteProviderAsync(item.Id);
                    LoadData();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}