using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Dispatch;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace Raphael.Desktop.ViewModels
{
    public partial class HomeViewModel : ObservableObject // BaseViewModel
    {
        // Properties for trip type
        [ObservableProperty] private bool _isRoundTrip;
        [ObservableProperty] private bool _isOneWay = true; // Default One Way
        [ObservableProperty] private bool _isAppointment = true; // Default Appt
        [ObservableProperty] private bool _isReturn;
        [ObservableProperty] private bool _isWillCall;

        public Action OnTripSavedSuccess { get; set; }

        [ObservableProperty] private string _pickupName;
        [ObservableProperty] private string _pickupPhone;
        [ObservableProperty] private string _pickupComment;

        [ObservableProperty] private string _dropoffName;
        [ObservableProperty] private string _dropoffPhone;
        [ObservableProperty] private string _dropoffComment;

        [ObservableProperty] private double _pickupLatitude;
        [ObservableProperty] private double _pickupLongitude;
        [ObservableProperty] private double _dropoffLatitude;
        [ObservableProperty] private double _dropoffLongitude;

        [ObservableProperty] private string _pickupAddress;
        [ObservableProperty] private string _dropoffAddress;

        [ObservableProperty] private DateTime? _pickupTimePicker;
        [ObservableProperty] private DateTime? _apptTimePicker;
        [ObservableProperty] private DateTime? _returnTimePicker;

        [ObservableProperty] private string _pickupCity;
        [ObservableProperty] private string _dropoffCity;

        //[ObservableProperty] private string _authorization;


        private readonly TripService _tripService;

        private bool _isLoadingTrips;
        public bool IsLoadingTrips
        {
            get => _isLoadingTrips;
            set
            {
                _isLoadingTrips = value;
                OnPropertyChanged(); 
            }
        }

        #region Translation

        // Client AutoSuggestBox
        public string AutoSuggestBoxHint => LocalizationService.Instance["AutoSuggestBoxHint"];// "Name, Client Code, or Phone Number"
        public string SaveCustomerToolTip => LocalizationService.Instance["SaveCustomerToolTip"]; // "Save customer information"
        public string NewCustomerToolTip => LocalizationService.Instance["NewCustomerToolTip"]; // "New customer"

        public string FullNameTextBoxHint => LocalizationService.Instance["FullNameTextBoxHint"];// "Full Name"
        public string ClientCodeTextBoxHint => LocalizationService.Instance["ClientCodeTextBoxHint"]; // "Client Code"
        public string PhoneTextBoxHint => LocalizationService.Instance["PhoneTextBoxHint"]; // "Phone"
        public string MobilePhoneTextBoxHint => LocalizationService.Instance["MobilePhoneTextBoxHint"]; // "Mobile Phone"
        public string SpaceTypeComboBoxHint => LocalizationService.Instance["SpaceTypeComboBoxHint"]; // "Select Space Type"
        public string FundingSourceComboBoxHint => LocalizationService.Instance["FundingSourceComboBoxHint"]; // "Select Funding Source"
        public string GeolocateFloatingButtonToolTip => LocalizationService.Instance["GeolocateFloatingButtonToolTip"]; // "Find Address"
        public string GooglePlacesInputHint => LocalizationService.Instance["GooglePlacesInputHint"]; // "Address"
        public string CityHint => LocalizationService.Instance["CityHint"]; // "City"
        public string StateHint => LocalizationService.Instance["StateHint"]; // "State"
        public string ZipHint => LocalizationService.Instance["ZipHint"]; // "ZIP"
        public string DOBDatePickerHint => LocalizationService.Instance["DOBDatePickerHint"]; // "Date of Birth"
        public string MaleRadioButtonContent => LocalizationService.Instance["MaleRadioButtonContent"]; // "Male"
        public string FemaleRadioButtonContent => LocalizationService.Instance["FemaleRadioButtonContent"]; // "Female"

        // Filter bar
        public string ForDatePickerHint => LocalizationService.Instance["ForDatePickerHint"]; // "Filter by Date"
        public string ShowCanceledCheckBoxContent => LocalizationService.Instance["ShowCanceledCheckBoxContent"]; // "Show Canceled"
        public string ImportButtonToolTip => LocalizationService.Instance["ImportButtonToolTip"]; // "Import Trips"
        public string ExportButtonToolTip => LocalizationService.Instance["ExportButtonToolTip"]; // "Export Trips"

        #region TripTabs
        public string TripTabsTabItem1Header => LocalizationService.Instance["TripTabsTabItem1Header"]; // "Location and Time"
        public string InverterFloatingButtonToolTip => LocalizationService.Instance["InverterFloatingButtonToolTip"]; // "Reverse Location"
        public string PickupAddressTextBoxHint => LocalizationService.Instance["PickupAddressTextBoxHint"]; // "Pickup Address"
        public string DropoffAddressTextBoxHint => LocalizationService.Instance["DropoffAddressTextBoxHint"]; // "Dropoff Address"
        public string SaveTab1FloatingButtonToolTip => LocalizationService.Instance["SaveTab1FloatingButtonToolTip"]; // "Save trip information"
        public string RoundTripRadioButtonContent => LocalizationService.Instance["RoundTripRadioButtonContent"]; // "Round Trip"
        public string OneWayRadioButtonContent => LocalizationService.Instance["OneWayRadioButtonContent"]; // "One Way"
        public string AppointmentRadioButtonContent => LocalizationService.Instance["AppointmentRadioButtonContent"]; // "Appt"
        public string ReturnRadioButtonContent => LocalizationService.Instance["ReturnRadioButtonContent"]; // "Return"
        public string PickupTimePickerHint => LocalizationService.Instance["PickupTimePickerHint"]; // "Pickup"
        public string ApptTimePickerHint => LocalizationService.Instance["ApptTimePickerHint"]; // "Appt"
        public string ReturnTimePickerHint => LocalizationService.Instance["ReturnTimePickerHint"]; // "Return"

        public string TripTabsTabItem2Header => LocalizationService.Instance["TripTabsTabItem2Header"]; // "Space Type"
        public string TripSpaceTypeComboBoxHint => LocalizationService.Instance["TripSpaceTypeComboBoxHint"]; // "Select Space Type"
        // AdditionalPassengersDataGrid

        public string TripTabsTabItem3Header => LocalizationService.Instance["TripTabsTabItem3Header"]; // "Pickup"
        public string PickupNameTextBoxHint => LocalizationService.Instance["PickupNameTextBoxHint"]; // "Name"
        public string PickupPhoneTextBoxHint => LocalizationService.Instance["PickupPhoneTextBoxHint"]; // "Phone"
        public string PickupCommentTextBoxHint => LocalizationService.Instance["PickupCommentTextBoxHint"]; // "Comment"


        public string TripTabsTabItem4Header => LocalizationService.Instance["TripTabsTabItem4Header"]; // "Dropoff"
        public string DropoffNameTextBoxHint => LocalizationService.Instance["DropoffNameTextBoxHint"]; // "Name"
        public string DropoffPhoneTextBoxHint => LocalizationService.Instance["DropoffPhoneTextBoxHint"]; // "Phone"
        public string DropoffCommentTextBoxHint => LocalizationService.Instance["DropoffCommentTextBoxHint"]; // "Comment"

        #endregion

        // Billing section
        public string Charges => LocalizationService.Instance["Charges"];
        public string History => LocalizationService.Instance["History"];
        public string Signature => LocalizationService.Instance["Signature"];
        public string TotalTripChargeTextLabel => LocalizationService.Instance["TotalTripChargeTextLabel"]; // Total Trip Charge
        public string DistanceTextLabel => LocalizationService.Instance["DistanceTextLabel"]; // Distance
        public string Imported => LocalizationService.Instance["Imported"];
        public string Routed => LocalizationService.Instance["Routed"];
        public string SelectedChargesTextLabel => LocalizationService.Instance["SelectedChargesTextLabel"]; // Selected Charges
        public string AllowUpdateText => LocalizationService.Instance["AllowUpdateText"]; // Allow Update

        public string ChargesDescriptionText => LocalizationService.Instance["ChargesDescriptionText"]; // Description
        public string ChargesRateText => LocalizationService.Instance["ChargesRateText"]; // Rate
        public string ChargesQtyText => LocalizationService.Instance["ChargesQtyText"]; // Qty
        public string ChargesPerText => LocalizationService.Instance["ChargesPerText"]; // Unit
        public string ChargesProcedureCodeText => LocalizationService.Instance["ChargesProcedureCodeText"]; // Code
        public string ChargesCostText => LocalizationService.Instance["ChargesCostText"]; // Cost
        public string NonDefaultChargesTextLabel => LocalizationService.Instance["NonDefaultChargesTextLabel"]; // Available, Non-default Charges
        public string IsDefaultText => LocalizationService.Instance["IsDefaultText"]; // Is Default
        public string FundingSourceText => LocalizationService.Instance["FundingSource"];
        public string AuthorizationText => LocalizationService.Instance["Authorization"];

        public string DropoffCommentText => LocalizationService.Instance["DropoffComment"];
        public string TypeText => LocalizationService.Instance["Type"];
        public string PickupText => LocalizationService.Instance["Pickup"]; 
        public string DropoffText => LocalizationService.Instance["Dropoff"];
        public string PickupPhoneText => LocalizationService.Instance["PickupPhone"];
        public string DropoffPhoneText => LocalizationService.Instance["DropoffPhone"];
        public string TripIdText => LocalizationService.Instance["TripId"];
        public string DistanceText => LocalizationService.Instance["Distance"];
        public string RunText => LocalizationService.Instance["Run"];
        public string PickupCityText => LocalizationService.Instance["PickupCity"];
        public string DropoffCityText => LocalizationService.Instance["DropoffCity"];
        public string DriverNoShowReasonText => LocalizationService.Instance["DriverNoShowReason"]; // Driver No-Show Reason

        #region DataGrid
        public string DayText => LocalizationService.Instance["DayText"]; // Day
        public string DateText => LocalizationService.Instance["DateText"]; // Date
        public string FromTimeText => LocalizationService.Instance["FromTimeText"]; // From Time
        public string ToTimeText => LocalizationService.Instance["ToTimeText"]; // To Time
        public string CustomerNameText => LocalizationService.Instance["CustomerNameText"]; // Patient
        public string PickupAddressText => LocalizationService.Instance["PickupAddressText"]; // Pickup Address
        public string DropoffAddressText => LocalizationService.Instance["DropoffAddressText"]; // Dropoff Address
        public string SpaceTypeNameText => LocalizationService.Instance["SpaceTypeNameText"]; // Space
        public string ChargeText => LocalizationService.Instance["ChargeText"]; // Charge
        public string PaidText => LocalizationService.Instance["PaidText"]; // Paid
        public string PickupCommentText => LocalizationService.Instance["PickupCommentText"]; // Pickup Comment

        #endregion

        #region Import Trips
        public string SelectCsvButtonContent => LocalizationService.Instance["SelectCsvButtonContent"]; // "Select CSV file"
        public string ProgressPanelText => LocalizationService.Instance["ProgressPanelText"]; // "Processing 0 of N trips..."

        #endregion

        #endregion

        #region Billing Section

        private string _textHeaderBillingSection;
        public string TextHeaderBillingSection
        {
            get => _textHeaderBillingSection;
            set
            {
                _textHeaderBillingSection = value;
                OnPropertyChanged();
            }
        }
       
        private double _totalTripCharge;  
        public double TotalTripCharge
        {
            //get => "$" + _textTotalTripCharge.ToString();
            get => _totalTripCharge;
            set
            {
                _totalTripCharge = value;
                OnPropertyChanged();
            }

        }
        
        private string _tripType;
        public string TripType
        {
            get => _tripType; // LocalizationService.Instance[_tripType]; // _tripType;
            set
            {
                _tripType = value;
                OnPropertyChanged();
            }
        }

        private string _distance;
        public string Distance
        {
            get => _distance;
            set
            {
                _distance = value;
                OnPropertyChanged();
                UpdateSelectedCharges();
            }
        }
        
        private string _eta;
        public string ETA
        {
            get => _eta;
            set
            {
                _eta = value;
                OnPropertyChanged();
            }
        }

        private bool _originImported;
        public bool OriginImported
        {
            get => _originImported;
            set
            {
                _originImported = value;
                OnPropertyChanged();
            }
        }
        
        private bool _originRouted;
        public bool OriginRouted
        {
            get => _originRouted;
            set
            {
                _originRouted = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<FundingSourceBillingItem> _allFundingSourceBillingItem;

        //private ObservableCollection<FundingSourceBillingItem> _charges;
        public ObservableCollection<FundingSourceBillingItem> SelectedCharges { get; set; } = new(); // Hay que armar Qty y Cost segun el tipo de BillingItem. Y Se debe actualizar cuando se editen: Distance, Pickup Address, Dropoof Address, (ya que en estos 2 ultimos se recalcula la distacia)


        //private ObservableCollection<FundingSourceBillingItem> _nonDefaultCharges;
        public ObservableCollection<FundingSourceBillingItem> NonDefaultCharges { get; set; } = new();


        private string _authorization;
        public string Authorization
        {
            get => _authorization;
            set
            {
                _authorization = value;
                OnPropertyChanged();
            }
        }

        public void UpdateSelectedCharges()
        {
            if (_allFundingSourceBillingItem != null) {
                var selected = (SelectedFundingSource == null && SelectedSpaceType == null) ? _allFundingSourceBillingItem : new ObservableCollection<FundingSourceBillingItem>(
                    _allFundingSourceBillingItem.Where(f => f.FundingSourceId == SelectedFundingSource?.Id && f.SpaceTypeId == SelectedSpaceType?.Id && f.IsDefault == true));

                if (selected != null)
                {
                    SelectedCharges.Clear();
                    foreach (var c in selected)
                    {
                        if (c.BillingItem.Unit.Abbreviation == "MILE")
                        {
                          string[] parts = Distance.Split(' '); // format => 11.5 mi
                            c.Qty = decimal.Parse(parts[0]);
                        }
                        else if (c.BillingItem.Unit.Abbreviation == "UNIT")
                        {
                            c.Qty = 1;
                        }
                        else
                        {
                            c.Qty = 1; // Despues ver todas las opciones, por el momento, para que no de error de referencia.
                        }

                        c.Cost = c.Rate * c.Qty;
                        SelectedCharges.Add(c);

                    }
                }
            }
                     
            
        }

        public void UpdateNonDefaultCharges()
        {
            if (_allFundingSourceBillingItem != null) {
                var nonDefault = (SelectedFundingSource == null && SelectedSpaceType == null) ? _allFundingSourceBillingItem : new ObservableCollection<FundingSourceBillingItem>(
                _allFundingSourceBillingItem.Where(f => f.FundingSourceId == SelectedFundingSource?.Id && f.SpaceTypeId == SelectedSpaceType?.Id && f.IsDefault != true));

                if (nonDefault != null)
                {
                    NonDefaultCharges.Clear();
                    foreach (var s in nonDefault)
                    {
                        NonDefaultCharges.Add(s);
                    }
                }
            }
            
            
        }

        #endregion

        #region Trip

        private string _gridSummary;
        public string GridSummary 
        {
            get => _gridSummary;
            set
            {
                _gridSummary = value;
                OnPropertyChanged();
            }
        }
        public TimeSpan FromTime { get; set; }

        private ObservableCollection<TripReadDto> _trips;
        public ObservableCollection<TripReadDto> Trips
        {
            get => _trips;
            set
            {
                _trips = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private TripReadDto _selectedTrip;
        /*public TripReadDto SelectedTrip
        {
            get => _selectedTrip;
            set
            {
                _selectedTrip = value;
                OnPropertyChanged();
            }
        }*/

        private ObservableCollection<TripReadDto> _tripsByDate;
        public ObservableCollection<TripReadDto> TripsByDate
        {
            get => _tripsByDate;
            set
            {
                _tripsByDate = value;              
                OnPropertyChanged();               
            }
        }

        private DateTime _filterDate;
        public DateTime FilterDate
        {
            get => _filterDate;
            set
            {
                _filterDate = value;
                OnPropertyChanged();

                if (_filterDate != null)
                {                   
                    LoadTripsByDateAsync(_filterDate);
                    //TripsByDate = Trips.FirstOrDefault(t => t.Date.Date == _filterDate.Date);                    
                }
                else
                {
                    SelectedSpaceType = null;
                    SelectedFundingSource = null;
                }
            }
        }

        public DateTime? TripFilterDate { get; set; } = DateTime.Today;
        public bool ShowCanceled { get; set; }

       

        // Trip data section

        /*private TimeSpan _pickupTimePicker;

        public TimeSpan PickupTimePicker
        {
            get => _pickupTimePicker;
            set
            {
                _pickupTimePicker = value;
                OnPropertyChanged();
            }
        }

        private TimeSpan _apptTimePicker;

        public TimeSpan ApptTimePicker
        {
            get => _apptTimePicker;
            set
            {
                _apptTimePicker = value;
                OnPropertyChanged();
            }
        }

        private TimeSpan _returnTimePicker;

        public TimeSpan ReturnTimePicker
        {
            get => _returnTimePicker;
            set
            {
                _returnTimePicker = value;
                OnPropertyChanged();
            }
        }*/

        /*private string? _name;
        public string? Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }*/

        #endregion

        #region Customer

        private ObservableCollection<Customer> _customers;
        private ObservableCollection<Customer> _filteredCustomers;
        private Customer _selectedCustomer;
        private string _searchText;

        public ObservableCollection<Customer> Customers
        {
            get => _customers;           
            set
            {
                _customers = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Customer> FilteredCustomers
        {
            get => _filteredCustomers;
            //set => SetProperty(ref _filteredCustomers, value);
            set
            {
                _filteredCustomers = value;
                OnPropertyChanged();
            }
        }

        public Customer SelectedCustomer
        {
            get => _selectedCustomer;           
            set
            {
                _selectedCustomer = value; 
                OnPropertyChanged();              
                if (value != null)
                {
                    SearchText = value.FullName; // Patch to autocomplete bug.
                    IdCustomer = value.Id;
                }
                if(_selectedCustomer != null)
                {
                    SelectedSpaceType = SpaceTypes.FirstOrDefault(s => s.Id == _selectedCustomer.SpaceTypeId);
                    SelectedFundingSource = FundingSources.FirstOrDefault(f => f.Id == _selectedCustomer.FundingSourceId);
                    // That doesn't work
                    //SelectedFundingSource = (FundingSource)_selectedCustomer.FundingSource;
                    GenderMale = _selectedCustomer.Gender == Gender.Male;
                    GenderFemale = _selectedCustomer.Gender == Gender.Female;
                }
                else
                {
                    SelectedSpaceType = null;
                    SelectedFundingSource = null;
                }
            }
        }
       // public bool IsCustomerSelected => SelectedCustomer != null;

        /*public Customer SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                _selectedCustomer = value;
                OnPropertyChanged();
                if (value != null)
                {
                    FillCustomerFields(value);
                }
            }
        }*/

        public string SearchText
        {
            get => _searchText;            
            set
            {
                _searchText = value;
                OnPropertyChanged();
                if (string.IsNullOrEmpty(value))
                {
                    SelectedCustomer = null; 
                } 
                FilterCustomers();              
            }
        }

        // Space Types
        public ObservableCollection<SpaceType> SpaceTypes { get; set; } = new();

        private SpaceType _selectedSpaceType;
        public SpaceType SelectedSpaceType
        {
            get => _selectedSpaceType;
            set
            {
                _selectedSpaceType = value;
                OnPropertyChanged();
                UpdateSelectedCharges();
                UpdateNonDefaultCharges();
            }
        }

        // Funding Sources
        public ObservableCollection<FundingSource> FundingSources { get; set; } = new();

        private FundingSource _selectedFundingSource;
        public FundingSource SelectedFundingSource
        {
            get => _selectedFundingSource;
            set
            {
                _selectedFundingSource = value;
                OnPropertyChanged();
                UpdateSelectedCharges();
                UpdateNonDefaultCharges();
            }
        }

        public FundingSource SelectedFundingSourceImport
        {
            get => _selectedFundingSource;
            set
            {
                _selectedFundingSource = value;
                OnPropertyChanged();             
            }
        }

        private bool _genderMale;
        public bool GenderMale
        {
            get => _genderMale;
            set
            {
                _genderMale = value;
                OnPropertyChanged();
            }
        }
        private bool _genderFemale;
        public bool GenderFemale
        {
            get => _genderFemale;
            set
            {
                _genderFemale = value;
                OnPropertyChanged();
            }
        }

        private int _idCustomer;
        public int IdCustomer
        {
            get => _idCustomer;
            set
            {
                _idCustomer = value;
                OnPropertyChanged();
            }
        }

        // Capacity Types
        public ObservableCollection<CapacityType> CapacityTypes { get; set; } = new();

        #endregion

        // === Commands ===
        public ICommand SaveCustomerCommand { get; }
        public ICommand NewCustomerCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }

        public ICommand SaveTripCommand { get; }

        public IAsyncRelayCommand EditTripCommand { get; }
        public IAsyncRelayCommand CancelTripCommand { get; }
        public IAsyncRelayCommand UncancelTripCommand { get; }

        public IAsyncRelayCommand ShowHistoryCommand { get; }

        private readonly GoogleMapsService _googleMapsService;
        public HomeViewModel() {

            _googleMapsService = new GoogleMapsService();
            _tripService = new TripService();

            _allFundingSourceBillingItem = new ObservableCollection<FundingSourceBillingItem>();
            Trips = new ObservableCollection<TripReadDto>();
            TripsByDate = new ObservableCollection<TripReadDto>();          
            
            //SaveCustomerCommand = new RelayCommand(SaveCustomer);
            NewCustomerCommand = new Helpers.RelayCommand(NewCustomer);
            ImportCommand = new Helpers.RelayCommand(ImportTrips);
            ExportCommand = new Helpers.RelayCommand(ExportTrips);
            SaveTripCommand = new Helpers.RelayCommand(SaveTrip);

            EditTripCommand = new AsyncRelayCommand<object>(ExecuteEditTripAsync);
            CancelTripCommand = new AsyncRelayCommand<object>(ExecuteCancelTripAsync);
            UncancelTripCommand = new AsyncRelayCommand<object>(ExecuteUncancelTripAsync);

            ShowHistoryCommand = new AsyncRelayCommand<object>(ExecuteShowHistoryAsync);

            LoadData();
            InitializeData();

            FilterDate = DateTime.Today; // DateTime.Now

            // Manual subscription for debug
            /*this.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectedCustomer))
                    MessageBox.Show($"Customer seleccionado: {SelectedCustomer?.FullName}");
                    //Debug.WriteLine($"Customer seleccionado: {SelectedCustomer?.FullName}");
            };*/

        }

        /// <summary>
        /// Will Call can only be decided while the trip is being booked.
        /// </summary>
        /// <remarks>
        /// ⚠️ On an existing trip the field moves only through Activate / Back to Will Call
        /// on the open-trips grid of Schedule, which records who did it and tells the
        /// patient. The server ignores it on every update route, so leaving the box live
        /// here would let a dispatcher tick it, save, and be told nothing had gone wrong.
        /// </remarks>
        public bool CanEditWillCall => SelectedTrip is null || SelectedTrip.Id <= 0;

        public string WillCallLockedToolTip =>
            LocalizationService.Instance["WillCallLockedHint"];

        // Este método se dispara automáticamente cuando cambia la propiedad SelectedTrip
        partial void OnSelectedTripChanged(TripReadDto value)
        {
            // Before the null check: it has to be raised when the selection is cleared too,
            // which is exactly when the form goes back to creating a trip.
            OnPropertyChanged(nameof(CanEditWillCall));

            if (value == null) return;

            // 1. Cargar el Cliente asociado para que se llenen los campos de la izquierda
            var customer = Customers.FirstOrDefault(c => c.Id == value.CustomerId);
            if (customer != null)
            {
                // Esto activará la lógica existente de búsqueda y visualización de cliente
                SelectedCustomer = customer;
            }

            // 2. Cargar datos de Localización y Tiempo (Tab 1)
            PickupAddress = value.PickupAddress;
            DropoffAddress = value.DropoffAddress;
            PickupLatitude = value.PickupLatitude;
            PickupLongitude = value.PickupLongitude;
            DropoffLatitude = value.DropoffLatitude;
            DropoffLongitude = value.DropoffLongitude;
            PickupCity = value.PickupCity;
            DropoffCity = value.DropoffCity;

            // Tiempos (Conversión segura de TimeSpan a DateTime para los Pickers)
            if (value.FromTime.HasValue)
                PickupTimePicker = DateTime.Today.Add(value.FromTime.Value);
            if (value.ToTime.HasValue)
                ApptTimePicker = DateTime.Today.Add(value.ToTime.Value);

            // Si hay un ReturnTime (usualmente se mapea de lógica de negocio o campos adicionales)
            // Nota: Como TripReadDto no tiene ReturnTime explícito, si es tipo Return, usamos FromTime
            if (value.Type == "Return")
                ReturnTimePicker = DateTime.Today.Add(value.FromTime ?? TimeSpan.Zero);

            // Tipos de Viaje
            IsOneWay = true;
            IsRoundTrip = false; // Por defecto al seleccionar uno del grid, editamos ese viaje individual
            IsWillCall = value.WillCall;
            IsAppointment = value.Type == "Appointment";
            IsReturn = value.Type == "Return";

            // 3. Cargar Datos de Espacio y Funding (Tab 2 y Billing)
            SelectedSpaceType = SpaceTypes.FirstOrDefault(s => s.Id == value.SpaceTypeId);
            SelectedFundingSource = FundingSources.FirstOrDefault(f => f.Id == value.FundingSourceId);
            Authorization = value.Authorization;
            Distance = $"{value.Distance} mi";

            // 4. Cargar Datos de Pickup/Dropoff (Tab 3 y 4)
            PickupName = value.Pickup;
            PickupPhone = value.PickupPhone;
            PickupComment = value.PickupComment;
            DropoffName = value.Dropoff;
            DropoffPhone = value.DropoffPhone;
            DropoffComment = value.DropoffComment;

            // 5. Notificar a la Vista que debe expandir el layout (esto lo haremos vía un evento o mensajería)
            // Para no romper nada, usaremos un truco simple: llamaremos a una acción si está definida
            //TriggerExpandLayout();
        }

        private void TriggerExpandLayout()
        {
            // Reutilizamos la lógica que ya tienes para cuando se salva un cliente
            // pero aplicada a la selección de un viaje.
            //OnTripSavedSuccess?.Invoke();
        }

        private async Task ExecuteShowHistoryAsync(object parameter)
        {
            var trip = parameter as TripReadDto;
            if (trip == null) return;
           
            var viewModel = new TripHistoryViewModel(trip);

            var view = new Views.TripHistoryDialog
            {
                DataContext = viewModel
            };

            await MaterialDesignThemes.Wpf.DialogHost.Show(view, "RootDialogHost");
        }

        #region API
        public async Task LoadFundingSourceBillingItemAsync()
        {
            IFundingSourceBillingItemService _fundingSourceService = new FundingSourceBillingItemService();
           
            var sourcesDto = await _fundingSourceService.GetAllAsync();

            _allFundingSourceBillingItem.Clear(); 
           
            foreach (var dto in sourcesDto)
            {
                var model = new FundingSourceBillingItem
                {
                    Id = dto.Id,
                    BillingItemId = dto.BillingItemId,
                    SpaceTypeId = dto.SpaceTypeId,
                    Rate = dto.Rate,
                    Per = dto.Per,
                    IsDefault = dto.IsDefault,
                    ProcedureCode = dto.ProcedureCode,
                    MinCharge = dto.MinCharge,
                    MaxCharge = dto.MaxCharge,
                    GreaterThanMinQty = dto.GreaterThanMinQty,
                    LessOrEqualMaxQty = dto.LessOrEqualMaxQty,
                    FreeQty = dto.FreeQty,
                    FromDate = dto.FromDate,
                    ToDate = dto.ToDate,
                  
                    BillingItem = new BillingItem
                    {
                        Description = dto.BillingItemDescription,
                        Unit = new Unit { Abbreviation = dto.BillingItemUnitAbbreviation }
                    },
                    SpaceType = new SpaceType
                    {
                        Name = dto.SpaceTypeName
                    }
                };
                _allFundingSourceBillingItem.Add(model);
            }
        }
        public async Task LoadSpaceTypesAsync()
        {
            SpaceTypeService _spaceTypeService = new SpaceTypeService();
            var sources = await _spaceTypeService.GetSpaceTypesAsync();
            SpaceTypes.Clear();
            foreach (var source in sources)
            {
                source.ShowNameDescription = source.Description + " " + source.Name;
                SpaceTypes.Add(source);
            }
                
        }
        public async Task LoadFundingSourcesAsync()
        {
            FundingSourceService _fundingSourceService = new FundingSourceService();
            var sources = await _fundingSourceService.GetFundingSourcesAsync(false);
            FundingSources.Clear();
            foreach (var source in sources)
                FundingSources.Add(source);
        }
        public async Task LoadCustomersFromApi()
        {
            _customers = new ObservableCollection<Customer>();           
            var customerService = new CustomerService();           
            try
            {
                var sources = await customerService.GetAllCustomersAsync();
                //var sources = await customerService.GetAllAsync();
                foreach (var source in sources)
                    _customers.Add(source);

                Customers = new ObservableCollection<Customer>(_customers);
                FilteredCustomers = new ObservableCollection<Customer>(_customers);
                
            }
            catch (ApiException ex)
            {
                MessageBox.Show(
                    $"Error {ex.StatusCode}:\n{ex.ErrorDetails}",
                    "Error del servidor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error inesperado: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

        }
        public async Task LoadTripsFromApi()
        {
            TripService _tripService = new TripService();
            var sources = await _tripService.GetAllTripsAsync();
            Trips.Clear();
            foreach (var source in sources)
                Trips.Add(source);
           
        }

        public async Task LoadCapacityTypesAsync()
        {
            CapacityTypeService _capacityTypeService = new CapacityTypeService();
            var listCapacityTypes = await _capacityTypeService.GetCapacityTypesAsync();
            CapacityTypes.Clear();
            foreach (var capacity in listCapacityTypes)
                CapacityTypes.Add(capacity);
        }

        public async Task LoadTripsByDateAsync(DateTime date)
        {
            IsLoadingTrips = true;
            TripsByDate.Clear();

            try
            {
                TripService _tripService = new TripService();
                var sources = await _tripService.GetTripsByDateAsync(date);
                GridSummary = sources.Count().ToString();


                //var geocodingTasks = sources.Select(trip => PopulateCitiesForTravel(trip)).ToList();
                //await Task.WhenAll(geocodingTasks);

                // Only consume the Google Maps service if the Trip object does not have PickupCity or DropoffCity
                foreach (var source in sources) {
                    /*if(source.PickupCity.Equals("") || source.PickupCity == null)
                        source.PickupCity = await _googleMapsService.GetCityFromCoordinates(source.PickupLatitude, source.PickupLongitude) ?? "N/A";
                    if (source.DropoffCity.Equals("") || source.DropoffCity == null)
                        source.DropoffCity = await _googleMapsService.GetCityFromCoordinates(source.DropoffLatitude, source.DropoffLongitude) ?? "N/A";*/
                    TripsByDate.Add(source);
                }
                    
            }
            catch (Exception ex)
            {

                MessageBox.Show($"An error occurred while loading trips: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoadingTrips = false; 
            }

        }

        private async Task PopulateCitiesForTravel(TripReadDto trip)
        {
            // Get the city of origin (Pickup)
            trip.PickupCity = await _googleMapsService.GetCityFromCoordinates(
                trip.PickupLatitude,
                trip.PickupLongitude) ?? "N/A"; // "N/A" = Not Available

            // Get the destination city (Dropoff)
            trip.DropoffCity = await _googleMapsService.GetCityFromCoordinates(
                trip.DropoffLatitude,
                trip.DropoffLongitude) ?? "N/A";
        }

        #endregion

        #region Filters
        private void FilterCustomers()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredCustomers = new ObservableCollection<Customer>(_customers);
                return;
            }

            var searchLower = SearchText.Trim().ToLower();  // Optimization: pre-process the text. Avoid multiple calls to Trim() and ToLower()
            //Other type of filter (c.FullName.StartsWith)
            var filtered = _customers
                .Where(c => (c.FullName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false)
                         || (c.ClientCode?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false)
                         || (c.Phone?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            FilteredCustomers = new ObservableCollection<Customer>(filtered);
            //MessageBox.Show(FilteredCustomers.Count().ToString() + " cantidad de customer");
        }

        #endregion

        #region Class Methods
        private async void InitializeData()
        {
            TripType = Raphael.Desktop.Models.TripType.Appointment;
        }
        private void LoadData()
        {
            LoadFundingSourcesAsync();
            LoadSpaceTypesAsync();
            LoadCustomersFromApi();
            LoadFundingSourceBillingItemAsync();
            LoadCapacityTypesAsync();
            LoadTripsFromApi();
        }

        private void SaveCustomer()
        {
            
        }

        private void NewCustomer()
        {
            
        }

        private void ImportTrips()
        {
            // Por implementar
        }

        private void ExportTrips()
        {
            // Por implementar
        }

        private async void SaveTrip()
        {
            try
            {
                // Basic validations before sending
                if (IdCustomer <= 0) { MessageBox.Show("Please select or save a customer first."); return; }
                if (SelectedSpaceType == null) { MessageBox.Show("Please select a Space Type."); return; }
                if (SelectedFundingSource == null) { MessageBox.Show("Please select a Funding Source."); return; }
                if (string.IsNullOrEmpty(DropoffAddress)) { MessageBox.Show("Dropoff Address is required."); return; }


                // We force UTC conversion not to be applied
                DateTime tripDate = DateTime.SpecifyKind(FilterDate.Date, DateTimeKind.Unspecified); // tells the system: "Don't touch the time, send it as is."

                // Si SelectedTrip tiene valor, es una EDICIÓN
                if (SelectedTrip != null && SelectedTrip.Id > 0)
                {
                    var tripReadDto = new TripReadDto
                    {
                        Id = SelectedTrip.Id,
                        TripId = SelectedTrip.TripId, // Mantener el ID externo original
                        Date = tripDate,
                        Day = tripDate.DayOfWeek.ToString(),

                        // Tiempos
                        FromTime = IsReturn ? ReturnTimePicker?.TimeOfDay : PickupTimePicker?.TimeOfDay,
                        ToTime = ApptTimePicker?.TimeOfDay,

                        // Cliente
                        CustomerId = IdCustomer,

                        // Pickup (Desde las propiedades vinculadas al mapa/formulario)
                        PickupAddress = PickupAddress,
                        PickupLatitude = PickupLatitude,
                        PickupLongitude = PickupLongitude,
                        PickupCity = PickupCity,
                        Pickup = PickupName,
                        PickupPhone = PickupPhone,
                        PickupComment = PickupComment,

                        // Dropoff (Desde las propiedades vinculadas al mapa/formulario)
                        DropoffAddress = DropoffAddress,
                        DropoffLatitude = DropoffLatitude,
                        DropoffLongitude = DropoffLongitude,
                        DropoffCity = DropoffCity,
                        Dropoff = DropoffName,
                        DropoffPhone = DropoffPhone,
                        DropoffComment = DropoffComment,

                        // Configuración
                        SpaceTypeId = SelectedSpaceType.Id,
                        FundingSourceId = SelectedFundingSource?.Id,
                        Authorization = Authorization,
                        Distance = double.TryParse(Distance?.Split(' ')[0], out var dist) ? dist : 0.0,

                        // Estado y Metadatos (IMPORTANTE: Enviar el status actual para no fallar validación)
                        Status = SelectedTrip.Status ?? TripStatus.Accepted,

                        // ⚠️ The trip's own value, not the checkbox. Editing a trip cannot
                        // move Will Call: it goes through Activate / Back to Will Call on
                        // the open-trips grid of Schedule, and the server ignores it here.
                        WillCall = SelectedTrip.WillCall,
                        Type = IsReturn ? "Return" : "Appointment",
                        Created = SelectedTrip.Created,
                        VehicleRouteId = SelectedTrip.VehicleRouteId // Mantener la ruta asignada si existe

                    };

                    // Creamos el objeto para actualizar
                    // Reutilizamos la estructura pero apuntando al ID existente
                    /*var tripUpdate = new Trip
                    {
                        Id = SelectedTrip.Id, // IMPORTANTE
                        Date = tripDate,
                        Day = tripDate.DayOfWeek.ToString(),
                        FromTime = IsReturn ? ReturnTimePicker?.TimeOfDay : PickupTimePicker?.TimeOfDay,
                        ToTime = ApptTimePicker?.TimeOfDay,
                        CustomerId = IdCustomer,
                        Authorization = Authorization,
                        PickupAddress = PickupAddress, 
                        PickupLatitude = PickupLatitude,
                        PickupLongitude = PickupLongitude,
                        PickupCity = PickupCity,
                        Pickup = PickupName,
                        PickupPhone = PickupPhone,
                        PickupComment = PickupComment,
                        DropoffAddress = DropoffAddress,
                        DropoffLatitude = DropoffLatitude,
                        DropoffLongitude = DropoffLongitude,
                        DropoffCity = DropoffCity,
                        Dropoff = DropoffName,
                        DropoffPhone = DropoffPhone,
                        DropoffComment = DropoffComment,
                        SpaceTypeId = SelectedSpaceType.Id,
                        FundingSourceId = SelectedFundingSource?.Id,
                        Distance = double.TryParse(Distance?.Split(' ')[0], out var dist) ? dist : 0.0,
                        Status = SelectedTrip.Status, // Mantener el estado actual
                        WillCall = IsWillCall,
                        Type = IsReturn ? "Return" : "Appointment",
                        Created = SelectedTrip.Created // Mantener fecha de creación
                    };*/

                    await _tripService.UpdateTripAsync(tripReadDto);

                    MessageBox.Show("Trip updated successfully!");
                }
                else
                {
                    Trip trip1 = new Trip
                    {
                        Date = tripDate,
                        Day = tripDate.DayOfWeek.ToString(),
                        FromTime = PickupTimePicker?.TimeOfDay ?? TimeSpan.Zero,
                        ToTime = ApptTimePicker?.TimeOfDay ?? TimeSpan.Zero,
                        CustomerId = IdCustomer,
                        Authorization = Authorization,

                        // Directions and Coordinates
                        PickupAddress = SelectedCustomer?.Address,
                        PickupLatitude = PickupLatitude,
                        PickupLongitude = PickupLongitude,
                        PickupCity = PickupCity,
                        Pickup = PickupName ?? SelectedCustomer?.FullName,
                        PickupPhone = PickupPhone ?? SelectedCustomer?.Phone,
                        PickupComment = PickupComment,

                        DropoffAddress = DropoffAddress,
                        DropoffLatitude = DropoffLatitude,
                        DropoffLongitude = DropoffLongitude,
                        DropoffCity = DropoffCity,
                        Dropoff = DropoffName,
                        DropoffPhone = DropoffPhone,
                        DropoffComment = DropoffComment,

                        SpaceTypeId = SelectedSpaceType?.Id ?? 0,
                        FundingSourceId = SelectedFundingSource?.Id ?? 0,

                        // "11.5 mi" -> parsear a double
                        Distance = double.TryParse(Distance?.Split(' ')[0], out var d) ? d : 0.0,

                        Status = TripStatus.Accepted,
                        Created = DateTime.Now,

                    };

                    trip1.WillCall = IsWillCall;
                    //trip1.WillCall = (trip1.ToTime == TimeSpan.Zero);             
                    trip1.Type = trip1.WillCall ? "Return" : "Appointment";

                    var result = await _tripService.CreateTripAsync(trip1);

                    // --- RETURN TRIP (ONLY IF IT IS ROUND TRIP) ---
                    if (IsRoundTrip)
                    {
                        Trip trip2 = new Trip
                        {
                            Date = tripDate,
                            Day = tripDate.DayOfWeek.ToString(),
                            //For return, the FromTime is usually the ReturnTimePicker
                            FromTime = ReturnTimePicker?.TimeOfDay ?? TimeSpan.Zero,
                            ToTime = TimeSpan.Zero,
                            CustomerId = IdCustomer,
                            Authorization = Authorization,

                            // DATOS INVERTIDOS: Pickup del regreso es el Dropoff de la ida
                            PickupAddress = trip1.DropoffAddress,
                            PickupLatitude = trip1.DropoffLatitude,
                            PickupLongitude = trip1.DropoffLongitude,
                            PickupCity = trip1.DropoffCity,
                            Pickup = trip1.Dropoff,
                            PickupPhone = trip1.DropoffPhone,
                            PickupComment = trip1.DropoffComment,

                            // DATOS INVERTIDOS: Dropoff del regreso es el Pickup de la ida
                            DropoffAddress = trip1.PickupAddress,
                            DropoffLatitude = trip1.PickupLatitude,
                            DropoffLongitude = trip1.PickupLongitude,
                            DropoffCity = trip1.PickupCity,
                            Dropoff = trip1.Pickup,
                            DropoffPhone = trip1.PickupPhone,
                            DropoffComment = trip1.PickupComment,

                            SpaceTypeId = trip1.SpaceTypeId,
                            FundingSourceId = trip1.FundingSourceId,
                            Distance = trip1.Distance,
                            Status = TripStatus.Accepted,
                            Created = DateTime.Now,
                            WillCall = IsWillCall,
                            Type = "Return"
                        };

                        await _tripService.CreateTripAsync(trip2);
                    }

                    MessageBox.Show(IsRoundTrip ? "Round Trip created successfully!" : "Trip created successfully!");
                }          

                await LoadTripsByDateAsync(FilterDate);

                ClearTripForm();
                SelectedTrip = null; // Limpiar selección después de guardar

                OnTripSavedSuccess?.Invoke();
            }
            catch (ApiException ex)
            {
                // Esto es vital: La API suele enviar un JSON explicando EXACTAMENTE qué campo falló
                // Ejemplo: "The DropoffLatitude field is required."
                MessageBox.Show($"Server Error: {ex.StatusCode}\nDetails: {ex.ErrorDetails}", "Validation Error");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}");
            }
        }

        private void ClearTripForm()
        {
            DropoffAddress = string.Empty;
            DropoffName = string.Empty;
            DropoffPhone = string.Empty;
            DropoffComment = string.Empty;
            PickupComment = string.Empty;
            Distance = "0.0 mi";
            ETA = string.Empty;

            // Si quieres limpiar también el nombre y teléfono del pickup 
            // (aunque a veces se dejan si es el mismo cliente)
            PickupName = string.Empty;
            PickupPhone = string.Empty;
            IsWillCall = false;
        }

        /*private async void SaveTrip()
        {
            MessageBox.Show("save trip");
            Trip trip = new Trip();
            trip.Date = FilterDate;
            trip.Day = FilterDate.Date.DayOfWeek.ToString();
            trip.FromTime = PickupTimePicker;
            trip.ToTime = ApptTimePicker;
            trip.CustomerId = IdCustomer;

            trip.PickupAddress = SelectedCustomer.Address;
            trip.PickupLatitude = PickupLatitude;   
            trip.PickupLongitude = PickupLongitude;
            trip.Pickup = PickupName;
            trip.PickupPhone = PickupPhone;
            trip.PickupComment = PickupComment;


            trip.DropoffAddress = DropoffAddress;
            trip.DropoffLatitude = DropoffLatitude;   
            trip.DropoffLongitude = DropoffLongitude;
            trip.Dropoff = DropoffName;
            trip.DropoffPhone = DropoffPhone;
            trip.DropoffComment = DropoffComment;

            trip.SpaceTypeId = SelectedSpaceType.Id;
            trip.FundingSourceId = SelectedFundingSource.Id;

            bool isWillCall = trip.ToTime == null;
            string tripType = isWillCall ? Models.TripType.Return : Models.TripType.Appointment;
            trip.Type = tripType;
            trip.WillCall = isWillCall;
            trip.Status = TripStatus.Accepted;
            trip.Created = DateTime.Now;

            trip.Distance = 0.00;
            //trip.Authorization = Authorization;

            
            TripService _tripService = new TripService();

            try
            {
                var createdCustomer = await _tripService.CreateTripAsync(trip);
                //success = true;
                //vm.IdCustomer = createdCustomer.Id;
            }
            catch (ApiException ex)
            {
                MessageBox.Show(
                    $"Error {ex.StatusCode}:\n{ex.ErrorDetails}",
                    "Error del servidor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error inesperado: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }*/

        private async Task ExecuteEditTripAsync(object parameter)
        {
            // The parameter comes as TripReadDto
            var tripToEdit = parameter as TripReadDto;
            if (tripToEdit == null) return;
           
            var dialogViewModel = new EditTripDialogViewModel(tripToEdit);
            var dialog = new EditTripDialog { DataContext = dialogViewModel };

            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(dialog, "RootDialogHost");

            if (result is bool wasSaved && wasSaved)
            {
                try
                {
                    var updatedDto = dialogViewModel.GetUpdatedDto();
                    await _tripService.UpdateFromDispatchAsync(tripToEdit.Id, updatedDto);

                    // We reload the trips from the current date to see the changes
                    await LoadTripsByDateAsync(this.FilterDate);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExecuteCancelTripAsync(object parameter)
        {
            var tripToCancel = parameter as TripReadDto;
            if (tripToCancel == null) return;

            var confirmationText = $"Are you sure you want to cancel trip '{tripToCancel.Id}'?";
            var confirmationTitle = "Confirm Cancellation";

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.CancelTripAsync(tripToCancel.Id);
                    MessageBox.Show("Trip canceled successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                  
                    await LoadTripsByDateAsync(this.FilterDate);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error canceling trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExecuteUncancelTripAsync(object parameter)
        {
            var tripToUncancel = parameter as TripReadDto;
            if (tripToUncancel == null) return;

            var confirmationText = $"Are you sure you want to restore trip '{tripToUncancel.Id}'?";
            var confirmationTitle = "Confirm Restoration";

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await _tripService.UncancelTripAsync(tripToUncancel.Id);
                    MessageBox.Show("Trip restored successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                  
                    await LoadTripsByDateAsync(this.FilterDate);
                }
                catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // --- HERE WE CAPTURE THE DUPLICATE (ERROR 409) ---                   
                    string duplicateMessage = !string.IsNullOrEmpty(ex.ErrorDetails)
                        ? ex.ErrorDetails
                        : "Cannot restore this trip because there is already another active trip for this customer with the same date, time, and addresses.";

                    MessageBox.Show(duplicateMessage, "Duplicate Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (ApiException ex)
                {
                    // Other API errors (400, 404, 500)
                    MessageBox.Show($"Server error ({ex.StatusCode}): {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex) // Connection errors or unexpected
                {
                    MessageBox.Show($"Error restoring trip: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Unexpected error
                }
            }
        }

        #endregion

    }
}
