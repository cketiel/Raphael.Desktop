using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Windows.Input;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Threading.Tasks;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Helpers;
using System.Windows;
using Raphael.Desktop.Views.Data;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.ViewModels
{
    public class CustomersViewModel : BaseViewModel
    {
        #region Translation

        // Filters
        public string FullNameText => LocalizationService.Instance["FullName"];
        public string PhoneText => LocalizationService.Instance["Phone"];
        public string ClientCode => LocalizationService.Instance["ClientCode"];
        public string SelectFundingSource => LocalizationService.Instance["SelectFundingSource"];
        public string Search => LocalizationService.Instance["Search"];

        // Actions
        public string ExcelExport => LocalizationService.Instance["ExcelExport"];
        public string Edit => LocalizationService.Instance["Edit"];

        // DataGrid Column Header
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

        public string NoDataToExport => LocalizationService.Instance["NoDataToExport"]; // There is no data to export.
        public string NoDataInfo => LocalizationService.Instance["NoDataInfo"]; // No Data
        #endregion

        private readonly ICustomerService _customerService;
    
        private ObservableCollection<Customer> _customers;
        private ObservableCollection<Customer> _filteredCustomers;
        public ObservableCollection<FundingSource> FundingSources { get; set; } = new();

        public ObservableCollection<Customer> Customers
        {
            get => _filteredCustomers;
            set { _filteredCustomers = value; OnPropertyChanged(); }
        }

        public string FilterFullName { get; set; }
        public string FilterPhone { get; set; }
        public string FilterClientCode { get; set; }

        private FundingSource _selectedFundingSource;
        public FundingSource SelectedFundingSource
        {
            get => _selectedFundingSource;
            set
            {
                _selectedFundingSource = value;
                OnPropertyChanged();
            }
        }


        public ICommand SearchCommand { get; }
        public ICommand EditCustomerCommand { get; }
        public ICommand ExportToExcelCommand { get; }

        public CustomersViewModel()
        {            
            _customers = new ObservableCollection<Customer>();
            _filteredCustomers = new ObservableCollection<Customer>();
            FundingSources = new ObservableCollection<FundingSource>();

            SearchCommand = new RelayCommandObject(async _ => await ApplyFilters());
            EditCustomerCommand = new RelayCommandObject(EditCustomer, _ => SelectedCustomer != null);
            ExportToExcelCommand = new RelayCommandObject(_ => ExportToExcel());
            _ = LoadDataAsync();
        }

        private Customer _selectedCustomer;
        public Customer SelectedCustomer
        {
            get => _selectedCustomer;
            set { _selectedCustomer = value; OnPropertyChanged(); }
        }

        private async Task LoadDataAsync()
        {
            /*CustomerService _customerService = new CustomerService();
            var list = await _customerService.LoadAllCustomersAsync();
            _customers = new ObservableCollection<Customer>(list);
            Customers = new ObservableCollection<Customer>(_customers);
            OnPropertyChanged(nameof(Customers));*/

            FundingSourceService _fundingSourceService = new FundingSourceService();
            var sources = await _fundingSourceService.GetFundingSourcesAsync(false);
            FundingSources.Clear();
            foreach (var source in sources)
                FundingSources.Add(source);
        }

        private Task ApplyFilters()
        {
            if (_customers.Count < 1)
                LoadAllCustomers();
            //if(_customers.Count > 0)
            //{
            var query = _customers.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(FilterFullName))
                    query = query.Where(c => c.FullName?.Contains(FilterFullName, System.StringComparison.OrdinalIgnoreCase) == true);
                if (!string.IsNullOrWhiteSpace(FilterPhone))
                    query = query.Where(c => c.Phone?.Contains(FilterPhone) == true);
                if (!string.IsNullOrWhiteSpace(FilterClientCode))
                    query = query.Where(c => c.ClientCode?.Contains(FilterClientCode, System.StringComparison.OrdinalIgnoreCase) == true);
                if (SelectedFundingSource != null)
                    query = query.Where(c => c.FundingSource?.Id == SelectedFundingSource.Id);

                Customers = new ObservableCollection<Customer>(query);
                OnPropertyChanged(nameof(Customers));
            //}
            /*else
            {
                LoadAllCustomers();
            }*/

            return Task.CompletedTask;
        }

        private async Task LoadAllCustomers()
        {
            CustomerService _customerService = new CustomerService();
            var list = await _customerService.GetAllCustomersAsync();
            //var list = await _customerService.LoadAllCustomersAsync();
            _customers = new ObservableCollection<Customer>(list);
            Customers = new ObservableCollection<Customer>(_customers);
            OnPropertyChanged(nameof(Customers));
        }

        private async void EditCustomer(object obj)
        {
            if (SelectedCustomer == null) return;

            var editViewModel = new EditCustomerViewModel(SelectedCustomer);
            var editView = new EditCustomerView
            {
                DataContext = editViewModel
            };

            var result = editView.ShowDialog();

            if (result == true)
            {
                try
                {
                    // User saved. We apply the clone changes to the original object.
                    editViewModel.ApplyChanges(SelectedCustomer);

                    CustomerService _customerService = new CustomerService();
                    var updatedCustomer = await _customerService.UpdateCustomerAsync(SelectedCustomer.Id, MapToCreateDto(SelectedCustomer));

                    await ApplyFilters();

                    //MessageBox.Show("Customer updated successfully!");
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(
                        $"Error {ex.StatusCode}:\n{ex.ErrorDetails}",
                        "Server error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Unexpected error: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

            }
            // If the result is false or null (the user canceled), nothing is done.
            // The original 'SelectedCustomer' was never modified.
        }

        private void ExportToExcel()
        {
            if (Customers == null || !Customers.Any())
            {
                MessageBox.Show(NoDataToExport, NoDataInfo, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                Title = "Save Customers to Excel",
                FileName = $"Customers_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("Customers");

                        var headers = new string[]
                        {
                            "ID", "Full Name", "Address", "City", "State", "Zip",
                            "Phone", "Mobile Phone", "Client Code", "Policy Number",
                            "Funding Source", "Space Type", "Email", "Date of Birth",
                            "Gender", "Created Date", "Created By", "Rider ID"
                        };

                        // Write headers
                        for (int i = 0; i < headers.Length; i++)
                        {
                            worksheet.Cell(1, i + 1).Value = headers[i];
                        }

                        // Style for headers
                        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        // --- Write the data ---
                        int currentRow = 2;
                        foreach (var customer in Customers)
                        {
                            int col = 1;
                            worksheet.Cell(currentRow, col++).Value = customer.Id;
                            worksheet.Cell(currentRow, col++).Value = customer.FullName;
                            worksheet.Cell(currentRow, col++).Value = customer.Address;
                            worksheet.Cell(currentRow, col++).Value = customer.City;
                            worksheet.Cell(currentRow, col++).Value = customer.State;
                            worksheet.Cell(currentRow, col++).Value = customer.Zip;
                            worksheet.Cell(currentRow, col++).Value = customer.Phone;
                            worksheet.Cell(currentRow, col++).Value = customer.MobilePhone;
                            worksheet.Cell(currentRow, col++).Value = customer.ClientCode;
                            worksheet.Cell(currentRow, col++).Value = customer.PolicyNumber;
                            worksheet.Cell(currentRow, col++).Value = customer.FundingSourceName; 
                            worksheet.Cell(currentRow, col++).Value = customer.SpaceTypeName;     
                            worksheet.Cell(currentRow, col++).Value = customer.Email;

                            // Format dates so Excel recognizes them correctly
                            var dobCell = worksheet.Cell(currentRow, col++);
                            if (customer.DOB.HasValue)
                            {
                                dobCell.Value = customer.DOB.Value;
                                // ISO 8601
                                dobCell.Style.DateFormat.Format = "yyyy-MM-dd";
                                //dobCell.Style.DateFormat.Format = "MM-dd-yyyy";
                            }

                            worksheet.Cell(currentRow, col++).Value = customer.Gender;

                            var createdCell = worksheet.Cell(currentRow, col++);
                            createdCell.Value = customer.Created;
                            // ISO 8601
                            createdCell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
                            //createdCell.Style.DateFormat.Format = "MM-dd-yyyy HH:mm";

                            worksheet.Cell(currentRow, col++).Value = customer.CreatedBy;
                            worksheet.Cell(currentRow, col++).Value = customer.RiderId;

                            currentRow++;
                        }

                        // Adjust column width to content
                        worksheet.Columns().AdjustToContents();

                        workbook.SaveAs(sfd.FileName);
                    }

                    MessageBox.Show($"Data exported successfully to:\n{sfd.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while exporting the data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static CustomerCreateDto MapToCreateDto(Customer customer) 
        {
            return new CustomerCreateDto
            {              
                FullName = customer.FullName,
                Address = customer.Address,
                City = customer.City,
                State = customer.State,
                Zip = customer.Zip,
                Phone = customer.Phone,
                MobilePhone = customer.MobilePhone,
                Email = customer.Email,
                FundingSourceId = customer.FundingSourceId,               
                SpaceTypeId = customer.SpaceTypeId,                
                Gender = customer.Gender,
                Created = customer.Created,
                CreatedBy = customer.CreatedBy,
                RiderId = customer.RiderId,
                DOB = customer.DOB
            };
        }

    }
}
