using Raphael.Desktop.Commands;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    public class IntegratorEditViewModel : BaseViewModel
    {
        private readonly IFundingSourceService _fundingService;
        public Integrator Integrator { get; set; }
        public bool IsNew { get; }

        private ObservableCollection<FundingSource> _fundingSources;
        public ObservableCollection<FundingSource> FundingSources
        {
            get => _fundingSources;
            set => SetProperty(ref _fundingSources, value);
        }

        private string _apiKeyDisplay;
        public string ApiKeyDisplay
        {
            get => _apiKeyDisplay;
            set => SetProperty(ref _apiKeyDisplay, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RegenerateKeyCommand { get; }

        public IntegratorEditViewModel(Integrator integrator)
        {
            _fundingService = new FundingSourceService();
            Integrator = integrator;
            IsNew = integrator.Id == 0;

            ApiKeyDisplay = IsNew ? "Generated automatically on save" : Integrator.ApiKey;

            LoadFundingSources();

            SaveCommand = new RelayCommandObject(Save);
            CancelCommand = new RelayCommandObject(Cancel);
            RegenerateKeyCommand = new RelayCommandObject(Regenerate, _ => !IsNew);
        }

        private async void LoadFundingSources()
        {
            try
            {
                // We only load the active ones.
                var list = await _fundingService.GetFundingSourcesAsync(false);
                FundingSources = new ObservableCollection<FundingSource>(list.OrderBy(x => x.Name));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading Funding Sources: " + ex.Message);
            }
        }

        private void Regenerate(object obj)
        {
            Integrator.RegenerateApiKey = true;
            ApiKeyDisplay = "Will be regenerated on save...";
        }

        private void Save(object obj)
        {
            if (string.IsNullOrWhiteSpace(Integrator.Name))
            {
                MessageBox.Show("Name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (obj is Window window) window.DialogResult = true;
        }

        private void Cancel(object obj)
        {
            if (obj is Window window) window.DialogResult = false;
        }
    }
}