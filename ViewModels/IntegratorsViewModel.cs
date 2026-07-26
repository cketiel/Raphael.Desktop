using Meditrans.Client.Models;
using Meditrans.Client.Services;
using Meditrans.Client.Commands;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Meditrans.Client.Views.Admin;

namespace Meditrans.Client.ViewModels
{
    public class IntegratorsViewModel : BaseViewModel
    {
        private readonly IIntegratorService _service;

        private ObservableCollection<Integrator> _integrators;
        public ObservableCollection<Integrator> Integrators
        {
            get => _integrators;
            set => SetProperty(ref _integrators, value);
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

        public IntegratorsViewModel()
        {
            _service = new IntegratorService();
            Integrators = new ObservableCollection<Integrator>();

            AddCommand = new RelayCommandObject(_ => OpenEditPopup(null));
            EditCommand = new RelayCommandObject(p => OpenEditPopup(p as Integrator));
            DeleteCommand = new RelayCommandObject(p => DeleteAsync(p as Integrator));

            LoadData();
        }

        public async void LoadData()
        {
            IsLoading = true;
            try
            {
                var list = await _service.GetIntegratorsAsync();
                Integrators = new ObservableCollection<Integrator>(list.OrderBy(x => x.Name));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading: {ex.Message}");
            }
            finally { IsLoading = false; }
        }

        private async void OpenEditPopup(Integrator item)
        {
            bool isNew = item == null;
            var clone = isNew ? new Integrator() : (Integrator)item.Clone();

            var vm = new IntegratorEditViewModel(clone);
            var view = new IntegratorEditView { DataContext = vm/*, Owner = Application.Current.MainWindow */};

            view.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (view.ShowDialog() == true)
            {
                try
                {
                    if (isNew)
                    {
                        await _service.AddIntegratorAsync(vm.Integrator);
                    }
                    else
                    {
                        await _service.UpdateIntegratorAsync(vm.Integrator.Id, vm.Integrator);
                    }
                    LoadData();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving: {ex.Message}");
                }
            }
        }

        private async void DeleteAsync(Integrator item)
        {
            if (item == null) return;

            if (MessageBox.Show($"Delete integrator {item.Name}?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    await _service.DeleteIntegratorAsync(item.Id);
                    LoadData();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }
    }
}