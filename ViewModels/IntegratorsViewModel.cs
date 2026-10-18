using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Commands;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Views.Admin;

namespace Raphael.Desktop.ViewModels
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
        public ICommand ToggleActiveCommand { get; }

        public IntegratorsViewModel()
        {
            _service = new IntegratorService();
            Integrators = new ObservableCollection<Integrator>();

            AddCommand = new RelayCommandObject(_ => OpenEditPopup(null));
            EditCommand = new RelayCommandObject(p => OpenEditPopup(p as Integrator));
            DeleteCommand = new RelayCommandObject(p => DeleteAsync(p as Integrator));
            ToggleActiveCommand = new RelayCommandObject(p => ToggleActiveAsync(p as Integrator));

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

        /// <summary>
        /// Uncontracts an Integrator or contracts it again (RE-056), the only way to change IsActive.
        /// Off, its API key stops working and its users cannot sign in. Nothing is deleted.
        /// </summary>
        private async void ToggleActiveAsync(Integrator item)
        {
            if (item == null) return;

            var activate = !item.IsActive;
            var title = LocalizationService.Instance[activate ? "catalog.Recontract" : "catalog.Uncontract"];
            var message = string.Format(
                LocalizationService.Instance[activate ? "catalog.ConfirmRecontract" : "catalog.ConfirmUncontractIntegrator"],
                item.Name);

            if (MessageBox.Show(message, title, MessageBoxButton.YesNo,
                    activate ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                await _service.SetActiveAsync(item.Id, activate);
                LoadData();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public string UncontractText => LocalizationService.Instance["catalog.Uncontract"];
        public string RecontractText => LocalizationService.Instance["catalog.Recontract"];

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

            if (MessageBox.Show(
                string.Format(ConfirmDeleteText, item.Name),
                ConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
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

        #region Translation

        public string AddText => LocalizationService.Instance["admin.AddIntegrator"];
        public string EditText => LocalizationService.Instance["Edit"];
        public string DeleteText => LocalizationService.Instance["admin.DeleteIntegrator"];

        public string ColumnName => LocalizationService.Instance["Name"];
        public string ColumnFundingSource => LocalizationService.Instance["admin.FundingSource"];
        public string ColumnApiKey => LocalizationService.Instance["admin.ApiKey"];
        public string ColumnActive => LocalizationService.Instance["Active"];
        public string ColumnCreated => LocalizationService.Instance["Created"];

        public string ConfirmDeleteText => LocalizationService.Instance["admin.ConfirmDeleteIntegrator"];
        public string ConfirmTitle => LocalizationService.Instance["ConfirmDeleteTitle"];
        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"];

        #endregion

    }
}