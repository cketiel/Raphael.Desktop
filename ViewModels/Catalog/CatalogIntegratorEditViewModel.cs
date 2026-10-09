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
    /// Adding or correcting one catalog entry by hand, for the entities nobody imported.
    /// </summary>
    /// <remarks>
    /// The dialog edits a copy and commits on save, like the rest of this application's edit
    /// dialogs. What it does not carry is who changed it and when: the server reads that off
    /// the token, because a client that could set it could rewrite the record's history.
    /// </remarks>
    public class CatalogIntegratorEditViewModel : ObservableObject
    {
        private readonly ICatalogIntegratorService _catalog;
        private readonly int? _id;

        public CatalogIntegratorEditViewModel(
            ICatalogIntegratorService catalog,
            IEnumerable<CatalogCategoryDto> categories,
            IEnumerable<CountyDto> counties,
            CatalogIntegratorDto? entry)
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
                FacilityType = entry.FacilityType;
                Beds = entry.Beds;
                ChainName = entry.ChainName;
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
        public string? FacilityType { get; set; }
        public int? Beds { get; set; }
        public string? ChainName { get; set; }
        public string? Comments { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Where this row came from and who last touched it, in one line. Read-only: it is a
        /// record of what happened, not a field.
        /// </summary>
        public string? Provenance { get; }

        private static string BuildProvenance(CatalogIntegratorDto entry)
        {
            var who = entry.UpdatedByUserName ?? entry.CreatedByUserName;
            var when = entry.UpdatedAtUtc ?? entry.CreatedAtUtc;

            // A null provider is the general broker, and saying so is more useful than a blank.
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

            var request = new CatalogIntegratorSaveDto
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
                FacilityType = FacilityType,
                Beds = Beds,
                ChainName = ChainName,
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
            ? LocalizationService.Instance["catalog.NewIntegrator"]
            : LocalizationService.Instance["catalog.EditIntegrator"];

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
        public string LabelFacilityType => LocalizationService.Instance["catalog.ColumnFacilityType"];
        public string LabelBeds => LocalizationService.Instance["catalog.ColumnBeds"];
        public string LabelChain => LocalizationService.Instance["catalog.LabelChain"];
        public string LabelComments => LocalizationService.Instance["catalog.LabelComments"];
        public string LabelIsActive => LocalizationService.Instance["catalog.LabelIsActive"];
        public string SaveText => LocalizationService.Instance["catalog.Save"];
        public string CancelText => LocalizationService.Instance["catalog.Cancel"];
    }
}
