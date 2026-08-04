using Raphael.Desktop.Commands; 
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq; 
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input; 

namespace Raphael.Desktop.ViewModels
{    
    public class EditCustomerViewModel : BaseViewModel
    {
        #region Translation
        public string FullNameText => LocalizationService.Instance["FullName"];
        public string PhoneText => LocalizationService.Instance["Phone"];
        public string ClientCode => LocalizationService.Instance["ClientCode"];
        public string SelectFundingSource => LocalizationService.Instance["SelectFundingSource"];
        public string AddressText => LocalizationService.Instance["Address"];
        public string CityText => LocalizationService.Instance["City"];
        public string StateText => LocalizationService.Instance["State"];
        public string ZipText => LocalizationService.Instance["Zip"];
        public string MobilePhoneText => LocalizationService.Instance["MobilePhone"];
        public string ClientCodeText => LocalizationService.Instance["ClientCode"];
        public string PolicyNumberText => LocalizationService.Instance["PolicyNumber"];
        public string FundingSourceText => LocalizationService.Instance["FundingSource"];
        public string SpaceTypeText => LocalizationService.Instance["SpaceType"];
        public string EmailText => LocalizationService.Instance["Email"];
        public string DOBText => LocalizationService.Instance["DOB"];
        public string GenderText => LocalizationService.Instance["Gender"];
        public string CreatedText => LocalizationService.Instance["Created"];
        public string CreatedByText => LocalizationService.Instance["CreatedBy"];

        public string Save => LocalizationService.Instance["Save"];
        public string Cancel => LocalizationService.Instance["Cancel"];
        public string CustomerHeader => LocalizationService.Instance["CustomerHeader"]; // Customer
        public string HomeAddressHeader => LocalizationService.Instance["HomeAddressHeader"]; // Home Address
        #endregion

        private Customer _customerToEdit;
        public Customer CustomerToEdit
        {
            get => _customerToEdit;
            set
            {
                _customerToEdit = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<FundingSource> FundingSources { get; set; }
        public ObservableCollection<SpaceType> SpaceTypes { get; set; }
        public ObservableCollection<string> Genders { get; set; }

        public EditCustomerViewModel(Customer originalCustomer)
        {
            // A clone is used to allow changes to be undone.
            CustomerToEdit = originalCustomer.Clone();

            FundingSources = new ObservableCollection<FundingSource>();
            SpaceTypes = new ObservableCollection<SpaceType>();
            Genders = new ObservableCollection<string> { "Male", "Female" };

            _ = LoadAuxDataAsync();
        }

        private async Task LoadAuxDataAsync()
        {
            var fundingSourceService = new FundingSourceService();
            var sources = await fundingSourceService.GetFundingSourcesAsync(false);
            FundingSources.Clear();
            foreach (var source in sources)
            {
                FundingSources.Add(source);
            }
            // Select the client's current FundingSource in the ComboBox 
            if (CustomerToEdit.FundingSourceId > 0)
            {
                CustomerToEdit.FundingSource = FundingSources.FirstOrDefault(fs => fs.Id == CustomerToEdit.FundingSourceId);
            }

            var spaceTypeService = new SpaceTypeService(); 
            var types = await spaceTypeService.GetSpaceTypesAsync(); 
            SpaceTypes.Clear();
            foreach (var type in types)
            {
                SpaceTypes.Add(type);
            }
            // Select the current SpaceType of the client in the ComboBox 
            if (CustomerToEdit.SpaceTypeId > 0)
            {
                CustomerToEdit.SpaceType = SpaceTypes.FirstOrDefault(st => st.Id == CustomerToEdit.SpaceTypeId);
            }

            // Notify the UI that CustomerToEdit properties may have changed.
            OnPropertyChanged(nameof(CustomerToEdit));
        }

        public void ApplyChanges(Customer originalCustomer)
        {
            // Map changes from clone to original object before saving.
            // This ensures that the correct IDs are saved in the DB.
            if (CustomerToEdit.FundingSource != null)
            {
                CustomerToEdit.FundingSourceId = CustomerToEdit.FundingSource.Id;
            }
            if (CustomerToEdit.SpaceType != null)
            {
                CustomerToEdit.SpaceTypeId = CustomerToEdit.SpaceType.Id;
            }

            // Copy all properties from the clone to the original
            originalCustomer.FullName = CustomerToEdit.FullName;
            originalCustomer.Address = CustomerToEdit.Address;
            originalCustomer.City = CustomerToEdit.City;
            originalCustomer.State = CustomerToEdit.State;
            originalCustomer.Zip = CustomerToEdit.Zip;
            originalCustomer.Phone = CustomerToEdit.Phone;
            originalCustomer.MobilePhone = CustomerToEdit.MobilePhone;
            originalCustomer.ClientCode = CustomerToEdit.ClientCode;
            originalCustomer.PolicyNumber = CustomerToEdit.PolicyNumber;
            originalCustomer.Email = CustomerToEdit.Email;
            originalCustomer.DOB = CustomerToEdit.DOB;
            originalCustomer.Gender = CustomerToEdit.Gender;
            originalCustomer.FundingSourceId = CustomerToEdit.FundingSourceId;
            originalCustomer.SpaceTypeId = CustomerToEdit.SpaceTypeId;
            originalCustomer.Latitude = CustomerToEdit.Latitude;
            originalCustomer.Longitude = CustomerToEdit.Longitude;
        }
    }
}