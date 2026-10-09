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

            ApiKeyDisplay = IsNew ? ApiKeyGeneratedText : Integrator.ApiKey;

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
            ApiKeyDisplay = ApiKeyWillRegenerateText;
        }

        private void Save(object obj)
        {
            if (string.IsNullOrWhiteSpace(Integrator.Name))
            {
                MessageBox.Show(NameRequiredText, ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (obj is Window window) window.DialogResult = true;
        }

        private void Cancel(object obj)
        {
            if (obj is Window window) window.DialogResult = false;
        }

        #region Translation

        public string WindowTitle => LocalizationService.Instance["admin.IntegratorDetails"];
        public string HeadingText => LocalizationService.Instance["admin.ManageIntegrator"];
        public string NameHint => LocalizationService.Instance["admin.IntegratorName"];
        public string FundingSourceHint => LocalizationService.Instance["admin.FundingSource"];
        public string IsActiveText => LocalizationService.Instance["Active"];

        public string ContactSectionText => LocalizationService.Instance["admin.ContactSection"];
        public string PhoneHint => LocalizationService.Instance["Phone"];
        public string EmailHint => LocalizationService.Instance["Email"];
        public string WebsiteHint => LocalizationService.Instance["Website"];
        public string AddressHint => LocalizationService.Instance["Address"];
        public string ContactNameHint => LocalizationService.Instance["ContactName"];
        public string NotesHint => LocalizationService.Instance["Notes"];

        public string SecuritySectionText => LocalizationService.Instance["admin.SecuritySection"];
        public string ApiKeyHint => LocalizationService.Instance["admin.ApiKey"];
        public string RegenerateApiKeyText => LocalizationService.Instance["admin.RegenerateApiKey"];
        public string ApiKeyGeneratedText => LocalizationService.Instance["admin.ApiKeyGenerated"];
        public string ApiKeyWillRegenerateText => LocalizationService.Instance["admin.ApiKeyWillRegenerate"];

        public string SaveText => LocalizationService.Instance["Save"];
        public string CancelText => LocalizationService.Instance["Cancel"];
        public string NameRequiredText => LocalizationService.Instance["admin.NameRequired"];
        public string ValidationTitle => LocalizationService.Instance["admin.ValidationTitle"];

        #endregion

    }
}