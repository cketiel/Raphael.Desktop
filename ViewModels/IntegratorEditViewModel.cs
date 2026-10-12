using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services.Catalog;
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

        // ---- Who works with whom (PROVIDER_ACCESS_MODEL.md §3.6). Only for a clinic that exists,
        // and applied at once: these are not part of the form, Save and Cancel do not touch them.

        private readonly IOfficeRelationsService _relations = new OfficeRelationsService();
        private readonly CatalogProviderService _catalogProviders = new CatalogProviderService();
        private readonly CatalogIntegratorService _catalogIntegrators = new CatalogIntegratorService();

        /// <summary>An existing clinic, seen by the office: the relations are every clinic's, not a Provider's to see.</summary>
        public bool IsExisting => !IsNew && Helpers.SessionManager.IsMilanesTransport;

        public ObservableCollection<IntegratorProviderRowDto> ContractedProviders { get; } = new();

        private string _catalogStatus = string.Empty;
        /// <summary>The catalog row this clinic is linked to, or that it has none (then no Provider sees it among its clinics).</summary>
        public string CatalogStatus { get => _catalogStatus; set => SetProperty(ref _catalogStatus, value); }

        private bool _isLinked;
        public bool IsLinked { get => _isLinked; set { SetProperty(ref _isLinked, value); CommandManager.InvalidateRequerySuggested(); } }

        private string _providerSearch = string.Empty;
        public string ProviderSearch { get => _providerSearch; set => SetProperty(ref _providerSearch, value); }
        public ObservableCollection<CatalogProviderListItemDto> ProviderCandidates { get; } = new();
        private CatalogProviderListItemDto? _selectedProviderCandidate;
        public CatalogProviderListItemDto? SelectedProviderCandidate { get => _selectedProviderCandidate; set { SetProperty(ref _selectedProviderCandidate, value); CommandManager.InvalidateRequerySuggested(); } }

        private string _catalogSearch = string.Empty;
        public string CatalogSearch { get => _catalogSearch; set => SetProperty(ref _catalogSearch, value); }
        public ObservableCollection<CatalogIntegratorListItemDto> CatalogCandidates { get; } = new();
        private CatalogIntegratorListItemDto? _selectedCatalogCandidate;
        public CatalogIntegratorListItemDto? SelectedCatalogCandidate { get => _selectedCatalogCandidate; set { SetProperty(ref _selectedCatalogCandidate, value); CommandManager.InvalidateRequerySuggested(); } }

        public ICommand SearchProvidersCommand { get; }
        public ICommand AddProviderCommand { get; }
        public ICommand RemoveProviderCommand { get; }
        public ICommand SearchCatalogCommand { get; }
        public ICommand LinkCatalogCommand { get; }
        public ICommand UnlinkCatalogCommand { get; }

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

            SearchProvidersCommand = new RelayCommandObject(async _ => await SearchProvidersAsync());
            AddProviderCommand = new RelayCommandObject(async _ => await Apply(() => _relations.AddProviderAsync(Integrator.Id, SelectedProviderCandidate!.Id)), _ => SelectedProviderCandidate != null);
            RemoveProviderCommand = new RelayCommandObject(async row => await RemoveProviderAsync(row as IntegratorProviderRowDto));
            SearchCatalogCommand = new RelayCommandObject(async _ => await SearchCatalogAsync());
            LinkCatalogCommand = new RelayCommandObject(async _ => await Apply(() => _relations.LinkCatalogAsync(Integrator.Id, SelectedCatalogCandidate!.Id)), _ => SelectedCatalogCandidate != null);
            UnlinkCatalogCommand = new RelayCommandObject(async _ => await Apply(() => _relations.LinkCatalogAsync(Integrator.Id, null)), _ => IsLinked);

            if (IsExisting) _ = LoadRelationsAsync();
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

        private async Task LoadRelationsAsync() => await Apply(() => _relations.GetIntegratorRelationsAsync(Integrator.Id));

        /// <summary>Runs one change of the relations and shows what the backend answers back.</summary>
        private async Task Apply(Func<Task<IntegratorRelationsDto>> change)
        {
            try
            {
                var relations = await change();
                ContractedProviders.Clear();
                foreach (var row in relations.Providers) ContractedProviders.Add(row);
                IsLinked = relations.CatalogIntegratorId != null;
                CatalogStatus = IsLinked
                    ? string.Format(LocalizationService.Instance["admin.CatalogLinkedTo"], relations.CatalogName)
                    : LocalizationService.Instance["admin.CatalogNotLinked"];
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, RelationsSectionText, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task RemoveProviderAsync(IntegratorProviderRowDto? row)
        {
            if (row == null) return;
            var question = string.Format(LocalizationService.Instance["admin.RemoveProviderConfirm"], row.Name);
            if (MessageBox.Show(question, RelationsSectionText, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await Apply(() => _relations.RemoveProviderAsync(Integrator.Id, row.CatalogProviderId));
        }

        private async Task SearchProvidersAsync()
        {
            try
            {
                var page = await _catalogProviders.SearchAsync(new CatalogSearchRequestDto { Term = ProviderSearch, PageSize = 25 });
                ProviderCandidates.Clear();
                foreach (var item in page.Items) ProviderCandidates.Add(item);
                SelectedProviderCandidate = ProviderCandidates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, RelationsSectionText, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task SearchCatalogAsync()
        {
            try
            {
                var page = await _catalogIntegrators.SearchAsync(new CatalogSearchRequestDto { Term = CatalogSearch, PageSize = 25 });
                CatalogCandidates.Clear();
                foreach (var item in page.Items) CatalogCandidates.Add(item);
                SelectedCatalogCandidate = CatalogCandidates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, RelationsSectionText, MessageBoxButton.OK, MessageBoxImage.Warning);
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

        public string RelationsSectionText => LocalizationService.Instance["admin.ContractedProviders"];
        public string RelationsHelpText => LocalizationService.Instance["admin.RelationsHelp"];
        public string CatalogSectionText => LocalizationService.Instance["admin.CatalogLink"];
        public string SearchCatalogProviderHint => LocalizationService.Instance["admin.SearchCatalogProvider"];
        public string SearchCatalogIntegratorHint => LocalizationService.Instance["admin.SearchCatalogIntegrator"];
        public string SearchText => LocalizationService.Instance["admin.Search"];
        public string AddText => LocalizationService.Instance["admin.ContractProvider"];
        public string RemoveText => LocalizationService.Instance["admin.RemoveProvider"];
        public string LinkText => LocalizationService.Instance["admin.LinkCatalog"];
        public string UnlinkText => LocalizationService.Instance["admin.UnlinkCatalog"];
        public string WorkModeText => LocalizationService.Instance["admin.WorkMode"];
        public string ActiveUsersText => LocalizationService.Instance["admin.ActiveUsers"];
        public string NameColumnText => LocalizationService.Instance["admin.Name"];

        public string SaveText => LocalizationService.Instance["Save"];
        public string CancelText => LocalizationService.Instance["Cancel"];
        public string NameRequiredText => LocalizationService.Instance["admin.NameRequired"];
        public string ValidationTitle => LocalizationService.Instance["admin.ValidationTitle"];

        #endregion

    }
}