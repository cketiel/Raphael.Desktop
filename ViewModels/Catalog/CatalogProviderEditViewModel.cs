using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Catalog;

namespace Raphael.Desktop.ViewModels.Catalog
{
    /// <summary>
    /// Adding or correcting one catalog provider by hand.
    /// </summary>
    /// <remarks>See <see cref="CatalogIntegratorEditViewModel"/>: the same rules, the other side.</remarks>
    public class CatalogProviderEditViewModel : ObservableObject
    {
        private readonly ICatalogProviderService _catalog;
        private readonly int? _id;

        public CatalogProviderEditViewModel(
            ICatalogProviderService catalog,
            IEnumerable<CatalogCategoryDto> categories,
            IEnumerable<CountyDto> counties,
            CatalogProviderDto? entry)
        {
            _catalog = catalog;
            _id = entry?.Id;

            Categories = new ObservableCollection<CatalogCategoryDto>(categories);
            Counties = new ObservableCollection<CountyDto>(counties);

            IsNew = entry is null;

            if (entry is not null)
            {
                Name = entry.Name;
                Address = entry.Address;
                City = entry.City;
                State = entry.State;
                Zip = entry.Zip;
                Phone = entry.Phone;
                Email = entry.Email;
                Website = entry.Website;
                ContactName = entry.ContactName;
                Npi = entry.Npi;
                IsPrimaryNemt = entry.IsPrimaryNemt;
                SourceUpdatedOn = entry.SourceUpdatedOn;
                EmsLicense = entry.EmsLicense;
                ServiceLevel = entry.ServiceLevel;
                LicenseExpiresOn = entry.LicenseExpiresOn;
                PlanSegment = entry.PlanSegment;
                CoverageArea = entry.CoverageArea;
                ProviderContact = entry.ProviderContact;
                EvidenceNote = entry.EvidenceNote;
                Comments = entry.Comments;
                IsActive = entry.IsActive;

                SelectedCategory = Categories.FirstOrDefault(c => c.Id == entry.CategoryId);
                SelectedCounty = Counties.FirstOrDefault(c => c.Id == entry.CountyId);

                Provenance = BuildProvenance(entry);
            }
            else
            {
                SelectedCategory = Categories.FirstOrDefault();
                State = "FL";
            }

            SaveCommand = new AsyncRelayCommand<Window>(SaveAsync);
            CancelCommand = new RelayCommand<Window>(Cancel);
        }

        public ObservableCollection<CatalogCategoryDto> Categories { get; }
        public ObservableCollection<CountyDto> Counties { get; }

        public bool IsNew { get; }

        public IAsyncRelayCommand<Window> SaveCommand { get; }
        public IRelayCommand<Window> CancelCommand { get; }

        public CatalogCategoryDto? SelectedCategory { get; set; }
        public CountyDto? SelectedCounty { get; set; }

        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }
        public string? Npi { get; set; }
        public bool? IsPrimaryNemt { get; set; }
        public DateTime? SourceUpdatedOn { get; set; }
        public string? EmsLicense { get; set; }
        public string? ServiceLevel { get; set; }
        public DateTime? LicenseExpiresOn { get; set; }
        public string? PlanSegment { get; set; }
        public string? CoverageArea { get; set; }
        public string? ProviderContact { get; set; }
        public string? EvidenceNote { get; set; }
        public string? Comments { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Where this row came from and who last touched it. Read-only.</summary>
        public string? Provenance { get; }

        private static string BuildProvenance(CatalogProviderDto entry)
        {
            var who = entry.UpdatedByUserName ?? entry.CreatedByUserName;
            var when = entry.UpdatedAtUtc ?? entry.CreatedAtUtc;

            var company = (entry.UpdatedAtUtc.HasValue
                              ? entry.UpdatedByProviderName
                              : entry.CreatedByProviderName)
                          ?? LocalizationService.Instance["catalog.GeneralBroker"];

            var source = string.IsNullOrWhiteSpace(entry.SourceFile)
                ? LocalizationService.Instance["catalog.SourceManual"]
                : entry.SourceFile;

            return string.Format(
                LocalizationService.Instance["catalog.Provenance"],
                source, who ?? "-", company, when.ToLocalTime());
        }

        private async System.Threading.Tasks.Task SaveAsync(Window? window)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                MessageBox.Show(
                    LocalizationService.Instance["catalog.NameRequired"],
                    LocalizationService.Instance["catalog.ValidationTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (SelectedCategory is null)
            {
                MessageBox.Show(
                    LocalizationService.Instance["catalog.GroupRequired"],
                    LocalizationService.Instance["catalog.ValidationTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var request = new CatalogProviderSaveDto
            {
                CategoryId = SelectedCategory.Id,
                Name = Name.Trim(),
                Address = Address,
                City = City,
                CountyId = SelectedCounty?.Id,
                State = State,
                Zip = Zip,
                Phone = Phone,
                Email = Email,
                Website = Website,
                ContactName = ContactName,
                Npi = Npi,
                IsPrimaryNemt = IsPrimaryNemt,
                SourceUpdatedOn = SourceUpdatedOn,
                EmsLicense = EmsLicense,
                ServiceLevel = ServiceLevel,
                LicenseExpiresOn = LicenseExpiresOn,
                PlanSegment = PlanSegment,
                CoverageArea = CoverageArea,
                ProviderContact = ProviderContact,
                EvidenceNote = EvidenceNote,
                Comments = Comments,
                IsActive = IsActive
            };

            try
            {
                if (_id.HasValue)
                {
                    await _catalog.UpdateAsync(_id.Value, request);
                }
                else
                {
                    await _catalog.CreateAsync(request);
                }

                if (window is not null)
                {
                    window.DialogResult = true;
                }
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.SaveFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void Cancel(Window? window)
        {
            if (window is not null)
            {
                window.DialogResult = false;
            }
        }

        // ---- Translation -----------------------------------------------------------------

        public string WindowTitle => IsNew
            ? LocalizationService.Instance["catalog.NewProvider"]
            : LocalizationService.Instance["catalog.EditProvider"];

        public string LabelGroup => LocalizationService.Instance["catalog.ColumnGroup"];
        public string LabelName => LocalizationService.Instance["catalog.ColumnName"];
        public string LabelAddress => LocalizationService.Instance["catalog.LabelAddress"];
        public string LabelCity => LocalizationService.Instance["catalog.ColumnCity"];
        public string LabelCounty => LocalizationService.Instance["catalog.ColumnCounty"];
        public string LabelState => LocalizationService.Instance["catalog.LabelState"];
        public string LabelZip => LocalizationService.Instance["catalog.ColumnZip"];
        public string LabelPhone => LocalizationService.Instance["catalog.ColumnPhone"];
        public string LabelEmail => LocalizationService.Instance["catalog.ColumnEmail"];
        public string LabelWebsite => LocalizationService.Instance["catalog.LabelWebsite"];
        public string LabelContact => LocalizationService.Instance["catalog.ColumnContact"];
        public string LabelNpi => LocalizationService.Instance["catalog.ColumnNpi"];
        public string LabelServiceLevel => LocalizationService.Instance["catalog.ColumnServiceLevel"];
        public string LabelEmsLicense => LocalizationService.Instance["catalog.LabelEmsLicense"];
        public string LabelLicenseExpires => LocalizationService.Instance["catalog.LabelLicenseExpires"];
        public string LabelPlanSegment => LocalizationService.Instance["catalog.LabelPlanSegment"];
        public string LabelCoverageArea => LocalizationService.Instance["catalog.LabelCoverageArea"];
        public string LabelProviderContact => LocalizationService.Instance["catalog.LabelProviderContact"];
        public string LabelEvidence => LocalizationService.Instance["catalog.LabelEvidence"];
        public string LabelComments => LocalizationService.Instance["catalog.LabelComments"];
        public string LabelIsActive => LocalizationService.Instance["catalog.LabelIsActive"];
        public string SaveText => LocalizationService.Instance["catalog.Save"];
        public string CancelText => LocalizationService.Instance["catalog.Cancel"];
    }
}
