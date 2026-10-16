using Raphael.Desktop.Models;
using Raphael.Desktop.DTOs;
using System.Collections.ObjectModel;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Raphael.Desktop.ViewModels
{
    public class ProviderEditViewModel : BaseViewModel
    {
        public Provider Provider { get; set; }

        private string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                SetProperty(ref _selectedImagePath, value);
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        // If there is a local image, it shows the local one. If not, it shows the one from the server.
        public object PreviewImage
        {
            get
            {
                if (!string.IsNullOrEmpty(SelectedImagePath))
                    return SelectedImagePath; // Shows the selected local image

                return Provider.FullLogoUrl; // Show the image that is already on the server
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectLogoCommand { get; }

        private readonly DispatcherTimer _clock;

        /// <summary>The clinics that contracted this Provider (PROVIDER_ACCESS_MODEL.md §3.6). Corrected from each clinic's file.</summary>
        public ObservableCollection<ProviderClinicRowDto> Clinics { get; } = new();
        public bool IsExisting => Provider.Id > 0 && Helpers.SessionManager.IsMilanesTransport;
        public string ClinicsSectionText => LocalizationService.Instance["admin.ProviderClinics"];
        public string ClinicsHelpText => LocalizationService.Instance["admin.ProviderClinicsHelp"];
        public string NameColumnText => LocalizationService.Instance["admin.Name"];

        private async void LoadClinics()
        {
            try
            {
                foreach (var clinic in await new OfficeRelationsService().GetProviderClinicsAsync(Provider.Id)) Clinics.Add(clinic);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ClinicsSectionText, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public ProviderEditViewModel(Provider provider)
        {
            Provider = provider;
            SaveCommand = new RelayCommandObject(Save);
            CancelCommand = new RelayCommandObject(Cancel);
            SelectLogoCommand = new RelayCommandObject(_ => SelectLogo());

            TimeZones = BuildTimeZoneList();

            _selectedTimeZone = TimeZones.FirstOrDefault(
                zone => zone.Id == provider.TimeZoneId);

            // Keeps the preview honest while the window is open, so it cannot show a time
            // that was true a minute ago.
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _clock.Tick += (_, _) => OnPropertyChanged(nameof(TimeZonePreview));
            _clock.Start();

            if (IsExisting) LoadClinics();
        }

        #region Timezone

        /// <summary>
        /// Every timezone the machine knows, with the ones this operation uses first.
        /// </summary>
        /// <remarks>
        /// Nobody remembers whether their office is America/New_York or America/Detroit, so
        /// the list alone is not enough — see <see cref="TimeZonePreview"/>.
        /// </remarks>
        public List<TimeZoneInfo> TimeZones { get; }

        private TimeZoneInfo _selectedTimeZone;

        public TimeZoneInfo SelectedTimeZone
        {
            get => _selectedTimeZone;
            set
            {
                if (!SetProperty(ref _selectedTimeZone, value))
                    return;

                Provider.TimeZoneId = value?.Id;

                OnPropertyChanged(nameof(TimeZonePreview));
                OnPropertyChanged(nameof(HasTimeZonePreview));
            }
        }

        /// <summary>
        /// "It is 3:42 PM there right now."
        /// </summary>
        /// <remarks>
        /// The one thing that actually catches a wrong pick. A dispatcher who knows what
        /// time it is at that office sees the mismatch immediately; without it, a plausible
        /// looking identifier goes unnoticed until trips start arriving hours out.
        /// </remarks>
        public string TimeZonePreview
        {
            get
            {
                if (SelectedTimeZone is null)
                    return LocalizationService.Instance["ProviderTimeZoneMissing"];

                var there = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    SelectedTimeZone);

                return string.Format(
                    LocalizationService.Instance["ProviderTimeZonePreview"],
                    there.ToString("t"),
                    there.ToString("d"));
            }
        }

        public bool HasTimeZonePreview => SelectedTimeZone is not null;

        public string TimeZoneLabel => LocalizationService.Instance["ProviderTimeZone"];

        public string TimeZoneHelpText => LocalizationService.Instance["ProviderTimeZoneHelp"];

        /// <summary>
        /// The zones this operation actually uses, then everything else.
        /// </summary>
        /// <remarks>
        /// Six hundred entries in alphabetical order is a list nobody scrolls. The North
        /// American ones cover every provider this business has, and the rest stay reachable
        /// rather than being cut off for somebody we have not met yet.
        /// </remarks>
        private static List<TimeZoneInfo> BuildTimeZoneList()
        {
            var all = TimeZoneInfo.GetSystemTimeZones();

            bool IsLikely(TimeZoneInfo zone) =>
                zone.Id.StartsWith("America/", StringComparison.OrdinalIgnoreCase) ||
                zone.Id.Contains("Eastern", StringComparison.OrdinalIgnoreCase) ||
                zone.Id.Contains("Central", StringComparison.OrdinalIgnoreCase) ||
                zone.Id.Contains("Mountain", StringComparison.OrdinalIgnoreCase) ||
                zone.Id.Contains("Pacific", StringComparison.OrdinalIgnoreCase);

            return
            [
                .. all.Where(IsLikely).OrderBy(zone => zone.Id),
                .. all.Where(zone => !IsLikely(zone)).OrderBy(zone => zone.Id)
            ];
        }

        #endregion

        private void SelectLogo()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedImagePath = openFileDialog.FileName;
            }
        }

        private void Save(object obj)
        {
            if (string.IsNullOrWhiteSpace(Provider.Name))
            {
                MessageBox.Show(CompanyNameRequiredText, ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (obj is Window window)
            {
                _clock.Stop();
                window.DialogResult = true;
            }
        }

        private void Cancel(object obj)
        {
            if (obj is Window window)
            {
                _clock.Stop();
                window.DialogResult = false;
            }
        }

        #region Translation

        /// <summary>Only Raphael's admin turns a Provider into a client company, and never an External one.</summary>
        public bool CanSetTenant => Helpers.SessionManager.IsPlatformAdmin && !Provider.IsExternal;

        /// <summary>
        /// Only Raphael's admin moves an existing Provider in or out of Raphael (PROVIDER_ACCESS_MODEL.md
        /// §3.2). A new one is created working in Raphael.
        /// </summary>
        public bool CanSetWorkMode => Helpers.SessionManager.IsPlatformAdmin && Provider.Id > 0;

        public bool WorksInRaphael
        {
            get => !Provider.IsExternal;
            set
            {
                if (value == !Provider.IsExternal) return;
                Provider.WorkMode = value ? 0 : 1;
                // External cannot be a tenant; the server clears it too.
                if (!value) Provider.IsTenant = false;
                OnPropertyChanged(nameof(WorksInRaphael));
                OnPropertyChanged(nameof(IsExternal));
                OnPropertyChanged(nameof(CanSetTenant));
            }
        }

        public bool IsExternal
        {
            get => Provider.IsExternal;
            set => WorksInRaphael = !value;
        }

        public string WorkModeText => LocalizationService.Instance["admin.ProviderWorkMode"];
        public string WorksInRaphaelText => LocalizationService.Instance["admin.ProviderWorksInRaphael"];
        public string WorksExternalText => LocalizationService.Instance["admin.ProviderWorksExternal"];
        public string WorkModeHelpText => LocalizationService.Instance["admin.ProviderWorkModeHelp"];
        public string IsTenantText => LocalizationService.Instance["admin.ProviderIsTenant"];
        public string IsTenantHelpText => LocalizationService.Instance["admin.ProviderIsTenantHelp"];

        public string WindowTitle => LocalizationService.Instance["admin.ProviderDetails"];
        public string HeadingText => LocalizationService.Instance["admin.ProviderInformation"];
        public string CompanyNameHint => LocalizationService.Instance["admin.CompanyName"];
        public string AddressHint => LocalizationService.Instance["Address"];
        public string EmailHint => LocalizationService.Instance["Email"];
        public string PhoneHint => LocalizationService.Instance["Phone"];
        public string WebsiteHint => LocalizationService.Instance["Website"];
        public string ContactNameHint => LocalizationService.Instance["ContactName"];
        public string NotesHint => LocalizationService.Instance["Notes"];
        public string LatitudeHint => LocalizationService.Instance["Latitude"];
        public string LongitudeHint => LocalizationService.Instance["Longitude"];

        public string CompanyLogoText => LocalizationService.Instance["admin.CompanyLogo"];
        public string ChooseLogoText => LocalizationService.Instance["admin.ChooseLogo"];

        public string SaveText => LocalizationService.Instance["Save"];
        public string CancelText => LocalizationService.Instance["Cancel"];
        public string CompanyNameRequiredText => LocalizationService.Instance["admin.CompanyNameRequired"];
        public string ValidationTitle => LocalizationService.Instance["admin.ValidationTitle"];

        #endregion

    }
}