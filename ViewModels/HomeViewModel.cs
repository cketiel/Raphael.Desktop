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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;

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

        #region Screen mode

        /// <summary>
        /// What the tab is doing right now. Every panel the screen shows or hides is decided
        /// from here and nowhere else.
        /// </summary>
        /// <remarks>
        /// ⚠️ Set this at the *end* of a transition, once the fields it depends on are already
        /// filled: <c>OnCurrentModeChanged</c> photographs the trip form, and a photograph taken
        /// before the form is populated turns every later field into a phantom "unsaved change".
        /// </remarks>
        [ObservableProperty] private HomeMode _currentMode = HomeMode.Browsing;

        /// <summary>The trip form is on screen, for a new trip or for one being edited.</summary>
        public bool IsTripFormOpen => CurrentMode is HomeMode.CreatingTrip or HomeMode.EditingTrip;

        /// <summary>
        /// The trip the form is editing, or null when the form is booking a new one.
        /// </summary>
        /// <remarks>
        /// ⚠️ This is deliberately NOT <c>SelectedTrip</c>. The grid drops its selection every time
        /// the day is reloaded — saving, cancelling a trip, changing the day — and <c>SaveTrip</c>
        /// used to read create-or-update from it. A selection cleared underneath the form turned an
        /// edit into an insert: the trip was written a second time and the original left untouched
        /// on its old day, so the patient had two trips where they had booked one. Editing is
        /// editing; it never creates. What the form is editing is the form's own state, and no
        /// change in the grid may decide it.
        /// </remarks>
        private TripReadDto _tripBeingEdited;
        public TripReadDto TripBeingEdited
        {
            get => _tripBeingEdited;
            private set
            {
                _tripBeingEdited = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditWillCall));
            }
        }

        /// <summary>
        /// The day the trip on the form happens. This is what the calendar beside the form sets.
        /// </summary>
        /// <remarks>
        /// ⚠️ Not a filter, and it never reloads the grid. While the form is open nothing is being
        /// filtered — a trip is being booked or edited — so the two date controls of this tab mean
        /// two different things: <c>ForDatePicker</c>, only visible while browsing, is the filter
        /// (<see cref="FilterDate"/>); <c>ForDateCalendar</c>, only visible with the form open, is
        /// this. They shared one property until RE-010, which is what made setting a trip's date
        /// reload the day underneath the form.
        /// </remarks>
        [ObservableProperty] private DateTime _tripDate = DateTime.Today;

        /// <summary>The CSV import view has taken over the tab.</summary>
        public bool IsImporting => CurrentMode is HomeMode.Importing;

        partial void OnCurrentModeChanged(HomeMode value)
        {
            OnPropertyChanged(nameof(IsTripFormOpen));
            OnPropertyChanged(nameof(IsImporting));

            _tripFormOnEntry = IsTripFormOpen ? CaptureTripForm() : null;
        }

        /// <summary>
        /// Starts booking a trip for the patient the search box has just landed on.
        /// </summary>
        /// <remarks>
        /// The confirmation comes first, before <c>SelectedCustomer</c> moves: saying no has to
        /// leave the form exactly as the dispatcher left it, patient included. Returns false when
        /// they chose to stay, so the caller can put the search box back.
        /// </remarks>
        public bool TryBeginTripForCustomer(Customer customer)
        {
            if (IsTripFormOpen && !ConfirmDiscardTripChanges()) return false;

            SelectedCustomer = customer;
            SearchText = customer?.FullName;

            EnterCreateTripMode();
            return true;
        }

        /// <summary>
        /// The two ways a booking starts — picking a patient in the search box, or saving a
        /// brand new one — both land here, so the form opens the same way from either.
        /// </summary>
        public void EnterCreateTripMode()
        {
            // Moving on from a trip being edited is leaving it. TryBeginTripForCustomer has
            // already asked by the time it calls this, and a clean form never asks twice.
            if (CurrentMode == HomeMode.EditingTrip && !TryLeaveTripForm()) return;

            // Nothing is being edited, and a new booking is for the day being looked at until
            // the calendar beside the form says otherwise.
            TripBeingEdited = null;
            TripDate = FilterDate.Date;

            CurrentMode = HomeMode.CreatingTrip;

            // Choosing another patient starts the booking over, so the form as it stands now is
            // the baseline. Setting the mode it is already in changes nothing and would leave
            // the previous photograph in place, turning the new patient into an unsaved change.
            _tripFormOnEntry = CaptureTripForm();
        }

        /// <summary>
        /// Opens the CSV import view. It takes over the whole tab, so anything half-typed in
        /// the trip form has to be settled first.
        /// </summary>
        public void EnterImportMode()
        {
            if (IsTripFormOpen && !TryLeaveTripForm()) return;

            CurrentMode = HomeMode.Importing;
        }

        /// <summary>
        /// The single way out of the trip form: the ✕ button, Esc, or clearing the grid
        /// selection. Returns false when the dispatcher chose to stay.
        /// </summary>
        public bool TryLeaveTripForm()
        {
            if (!IsTripFormOpen) return true;
            if (!ConfirmDiscardTripChanges()) return false;

            _leavingTripForm = true;
            try
            {
                SelectedTrip = null;
                TripBeingEdited = null;
                ClearTripForm();
                CurrentMode = HomeMode.Browsing;
            }
            finally
            {
                _leavingTripForm = false;
            }

            return true;
        }

        /// <summary>Leaves the import view and goes back to the day's trips.</summary>
        public void LeaveImportMode() => CurrentMode = HomeMode.Browsing;

        #endregion

        #region Unsaved trip changes

        /// <summary>True while <see cref="TryLeaveTripForm"/> is unwinding, so it is not asked twice.</summary>
        private bool _leavingTripForm;

        /// <summary>True while the grid selection is being put back, so the trip is not reloaded over the dispatcher's edits.</summary>
        private bool _restoringSelection;

        /// <summary>
        /// The trip form as it stood when the form last opened. Null while the form is closed.
        /// </summary>
        private TripFormSnapshot _tripFormOnEntry;

        /// <summary>
        /// Every field of the trip form a dispatcher can type or pick. It is a record for one
        /// reason: value equality turns "did anything change?" into a single comparison.
        /// </summary>
        /// <remarks>
        /// ⚠️ Only what a person edits goes in here. **Derived values must stay out**, however
        /// much they look like form fields: <c>Distance</c>, <c>ETA</c> and the four coordinates
        /// are written by the map and the routing service, not typed. Distance in particular is
        /// a read-only TextBlock that <c>DrawTripRouteAsync</c> overwrites some hundreds of
        /// milliseconds after a trip is selected, and in another format — "12.3 mi" priced by the
        /// router against "12.34 mi" carried by the DTO. It was in this record until RE-010's
        /// review, and that is why the screen asked "discard changes?" on *every* exit, including
        /// the one where the dispatcher had only glanced at a trip and pressed Esc.
        ///
        /// A field a person edits and that is missing here is the opposite failure: a change the
        /// screen discards without asking. Add it in both places or not at all.
        /// </remarks>
        private sealed record TripFormSnapshot(
            int CustomerId,
            string PickupAddress, string DropoffAddress,
            string PickupCity, string DropoffCity,
            string PickupName, string PickupPhone, string PickupComment,
            string DropoffName, string DropoffPhone, string DropoffComment,
            string Authorization,
            DateTime? PickupTime, DateTime? ApptTime, DateTime? ReturnTime,
            bool IsRoundTrip, bool IsOneWay, bool IsAppointment, bool IsReturn, bool IsWillCall,
            int? SpaceTypeId, int? FundingSourceId,
            DateTime TripDate);

        private TripFormSnapshot CaptureTripForm() => new(
            IdCustomer,
            PickupAddress, DropoffAddress,
            PickupCity, DropoffCity,
            PickupName, PickupPhone, PickupComment,
            DropoffName, DropoffPhone, DropoffComment,
            Authorization,
            PickupTimePicker, ApptTimePicker, ReturnTimePicker,
            IsRoundTrip, IsOneWay, IsAppointment, IsReturn, IsWillCall,
            SelectedSpaceType?.Id, SelectedFundingSource?.Id,
            TripDate.Date);

        /// <summary>
        /// True when the form holds work the server has not been told about.
        /// </summary>
        public bool HasUnsavedTripChanges =>
            _tripFormOnEntry is not null && CaptureTripForm() != _tripFormOnEntry;

        /// <summary>
        /// Asks before throwing away a half-filled trip. Returns true when it is safe to leave.
        /// </summary>
        /// <remarks>
        /// The comparison is what keeps this bearable: opening a trip from the grid fills the
        /// form by itself, so prompting on every exit would ask a dispatcher who only wanted to
        /// look at a trip on the map, dozens of times a day.
        /// </remarks>
        private bool ConfirmDiscardTripChanges()
        {
            if (!HasUnsavedTripChanges) return true;

            return MessageBox.Show(
                LocalizationService.Instance["home.DiscardChangesMessage"],
                LocalizationService.Instance["home.DiscardChangesTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        #endregion

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
        public string CloseTripFormToolTip => LocalizationService.Instance["home.CloseTripForm"]; // "Close the trip form (Esc)"
        public string TripSearchHint => LocalizationService.Instance["home.TripSearchHint"]; // "Search patient, address, #id, TripId:..."
        public string DatePickerToolTip => LocalizationService.Instance["home.DatePickerToolTip"];
        public string SingleDayLabel => LocalizationService.Instance["home.SingleDay"];
        public string RangeLabel => LocalizationService.Instance["home.Range"];
        public string ClearRangeToolTip => LocalizationService.Instance["home.ClearRange"];
        public string RangeChipText => LocalizationService.Instance["home.RangeChip"];
        public string PresetTodayLabel => LocalizationService.Instance["home.PresetToday"];
        public string PresetTomorrowLabel => LocalizationService.Instance["home.PresetTomorrow"];
        public string PresetThisWeekLabel => LocalizationService.Instance["home.PresetThisWeek"];
        public string PresetNextSevenLabel => LocalizationService.Instance["home.PresetNextSeven"];
        public string FiltersHeader => LocalizationService.Instance["home.FiltersHeader"];
        public string ClearAllFiltersLabel => LocalizationService.Instance["home.ClearAllFilters"];
        public string SaveViewLabel => LocalizationService.Instance["home.SaveView"];
        public string MyFiltersHint => LocalizationService.Instance["home.MyFilters"];
        public string StatusFilterText => LocalizationService.Instance["home.StatusFilter"];
        public string CitiesFilterText => LocalizationService.Instance["home.CitiesFilter"];
        public string CityScopeText => LocalizationService.Instance["home.CityScope"];
        public string ScopeBothText => LocalizationService.Instance["home.ScopeBoth"];
        public string PickupWindowText => LocalizationService.Instance["home.PickupWindow"];
        public string OtherFiltersText => LocalizationService.Instance["home.OtherFilters"];
        public string FlagAnyText => LocalizationService.Instance["home.FlagAny"];
        public string FlagYesText => LocalizationService.Instance["home.FlagYes"];
        public string FlagNoText => LocalizationService.Instance["home.FlagNo"];
        public string MissingCoordinatesText => LocalizationService.Instance["home.MissingCoordinates"];
        public string MissingCoordinatesHint => LocalizationService.Instance["home.MissingCoordinatesHint"];
        public string ZipStateText => LocalizationService.Instance["home.ZipState"];
        public string ZipStateWarning => LocalizationService.Instance["home.ZipStateWarning"];
        public string ChooseColumnsToolTip => LocalizationService.Instance["home.ChooseColumns"];
        public string CompactGridToolTip => LocalizationService.Instance["home.CompactGrid"];
        public string Step1Label => LocalizationService.Instance["home.Step1"];
        public string Step2Label => LocalizationService.Instance["home.Step2"];
        public string Step3Label => LocalizationService.Instance["home.Step3"];
        public string Step4Label => LocalizationService.Instance["home.Step4"];
        public string BackToHomeLabel => LocalizationService.Instance["home.BackToHome"];
        public string ImportHeaderLabel => LocalizationService.Instance["home.ImportHeader"];

        /// <summary>
        /// ⚠️ "Results", not "preview". The grid under this label is filled <b>after</b> the trips
        /// have been sent, so calling it a preview told the dispatcher they still had a chance to
        /// look before anything happened.
        /// </summary>
        public string ImportResultsLabel => LocalizationService.Instance["home.ImportResults"];

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

        /// <summary>
        /// The three numbers under the grid.
        /// </summary>
        /// <remarks>
        /// Counted over what is shown, not over what was loaded: a total that ignores the filters
        /// contradicts the list it sits under, and the dispatcher believes the number.
        /// </remarks>
        public string GridTotals
        {
            get
            {
                var shown = TripsView?.Cast<TripReadDto>().ToList() ?? new List<TripReadDto>();

                return string.Format(
                    LocalizationService.Instance["home.GridTotals"],
                    shown.Count,
                    shown.Count(IsCanceled),
                    shown.Count(t => string.IsNullOrWhiteSpace(t.RunName)));
            }
        }

        /// <summary>Tighter rows, for a dispatcher who would rather see more of the day at once.</summary>
        [ObservableProperty] private bool _isCompactGrid;

        partial void OnIsCompactGridChanged(bool value)
        {
            _config.Save(CompactKey, value);
            OnPropertyChanged(nameof(GridRowHeight));
        }

        /// <summary>NaN is WPF's "as tall as the content needs", which is the comfortable setting.</summary>
        public double GridRowHeight => IsCompactGrid ? 24 : double.NaN;

        private readonly UserConfigService _config = new();

        private const string CompactKey = "HomeGridCompact";
        private const string ColumnsKey = "HomeGridColumns";


        #region Guidance (2.2)

        /// <summary>
        /// How far along booking a trip is.
        /// </summary>
        /// <remarks>
        /// One machine, three faces. The badges over the controls, the stepper above the form and
        /// the first-run tour all read this and hold no rules of their own, so whichever of the
        /// three turns out to be the one people use, the others can be switched off without
        /// touching any logic.
        /// </remarks>
        public TripCreationStep CurrentStep =>
            !Step1Done ? TripCreationStep.Patient
            : !Step2Done ? TripCreationStep.Addresses
            : !Step3Done ? TripCreationStep.Schedule
            : TripCreationStep.Create;

        public bool Step1Done => IdCustomer > 0;

        public bool Step2Done => Step1Done
            && !string.IsNullOrWhiteSpace(PickupAddress)
            && !string.IsNullOrWhiteSpace(DropoffAddress);

        /// <summary>
        /// A time, or Will Call — which is the deliberate absence of one — plus what the trip
        /// needs to be priced.
        /// </summary>
        public bool Step3Done => Step2Done
            && SelectedSpaceType != null
            && SelectedFundingSource != null
            && (IsWillCall || PickupTimePicker.HasValue || ApptTimePicker.HasValue || ReturnTimePicker.HasValue);

        public string Step1State => StateOf(TripCreationStep.Patient, Step1Done);
        public string Step2State => StateOf(TripCreationStep.Addresses, Step2Done);
        public string Step3State => StateOf(TripCreationStep.Schedule, Step3Done);
        public string Step4State => StateOf(TripCreationStep.Create, false);

        private string StateOf(TripCreationStep step, bool done) =>
            done ? "done" : CurrentStep == step ? "current" : "pending";

        /// <summary>
        /// Why the Create button is refusing, in the dispatcher's words, or null when it is not.
        /// </summary>
        /// <remarks>
        /// ⚠️ This has to say exactly what <c>SaveTrip</c> checks, in the same order. A button that
        /// claims to be ready and then opens a dialog saying otherwise is worse than one that was
        /// simply missing.
        ///
        /// The button stays visible and goes grey rather than disappearing: a control that is not
        /// there teaches nobody anything, and one that is there saying "Missing: dropoff address"
        /// teaches the whole screen.
        /// </remarks>
        public string CreateTripBlockedReason
        {
            get
            {
                if (IdCustomer <= 0) return Missing("home.StepPatient");
                if (string.IsNullOrWhiteSpace(DropoffAddress)) return Missing("home.StepDropoff");
                if (SelectedSpaceType == null) return Missing("home.StepSpaceType");
                if (SelectedFundingSource == null) return Missing("home.StepFundingSource");

                return null;
            }
        }

        private static string Missing(string key) => string.Format(
            LocalizationService.Instance["home.Missing"], LocalizationService.Instance[key]);

        public bool CanCreateTrip => CreateTripBlockedReason == null;

        /// <summary>What the label beside the Create button says: the blocker, or that it is ready.</summary>
        public string CreateTripHint =>
            CreateTripBlockedReason ?? LocalizationService.Instance["home.ReadyToCreate"];

        /// <summary>
        /// Re-reads everything the guidance is made of. Called from every field it depends on.
        /// </summary>
        private void RefreshGuidance()
        {
            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(Step1Done));
            OnPropertyChanged(nameof(Step2Done));
            OnPropertyChanged(nameof(Step3Done));
            OnPropertyChanged(nameof(Step1State));
            OnPropertyChanged(nameof(Step2State));
            OnPropertyChanged(nameof(Step3State));
            OnPropertyChanged(nameof(Step4State));
            OnPropertyChanged(nameof(CreateTripBlockedReason));
            OnPropertyChanged(nameof(CanCreateTrip));
            OnPropertyChanged(nameof(CreateTripHint));
        }

        partial void OnPickupAddressChanged(string value) => RefreshGuidance();
        partial void OnDropoffAddressChanged(string value) => RefreshGuidance();
        partial void OnPickupTimePickerChanged(DateTime? value) => RefreshGuidance();
        partial void OnApptTimePickerChanged(DateTime? value) => RefreshGuidance();
        partial void OnReturnTimePickerChanged(DateTime? value) => RefreshGuidance();
        partial void OnIsWillCallChanged(bool value) => RefreshGuidance();

        #endregion


        #region The patient panel says what it is holding (2.8)

        /// <summary>
        /// The fields of the patient panel, as they stand on screen.
        /// </summary>
        /// <remarks>
        /// The panel's boxes are bound to the Customer object itself and read back by name when
        /// saving, so there is nowhere else the current text lives. The view hands it over on
        /// every keystroke and this decides what it means.
        /// </remarks>
        public sealed record CustomerFields(
            string FullName, string ClientCode, string Phone, string MobilePhone,
            string Address, string City, string State, string Zip,
            DateTime? Dob, bool Male);

        private CustomerFields _customerOnEntry;
        private CustomerFields _customerNow;

        /// <summary>
        /// The patient exactly as the server has them, for the view to restore from.
        /// </summary>
        /// <remarks>
        /// ⚠️ Restoring has to come from here and never from <c>SelectedCustomer</c>. The panel's
        /// boxes are bound TwoWay straight into that object, so the first keystroke has already
        /// overwritten it: putting the boxes back from it puts back exactly what is on screen,
        /// which is why the Discard button appeared to do nothing at all.
        /// </remarks>
        public CustomerFields CustomerBaseline => _customerOnEntry;

        [ObservableProperty] private CustomerFormState _customerState = CustomerFormState.Empty;

        public string CustomerStateLabel => CustomerState switch
        {
            CustomerFormState.NewUnsaved => LocalizationService.Instance["home.PatientNew"],
            CustomerFormState.Existing => LocalizationService.Instance["home.PatientExisting"],
            CustomerFormState.ModifiedUnsaved => LocalizationService.Instance["home.PatientModified"],
            _ => string.Empty
        };

        /// <summary>Amber while anything is unsaved, plain grey once it matches the server.</summary>
        public string CustomerStateTag => CustomerState switch
        {
            CustomerFormState.NewUnsaved => "unsaved",
            CustomerFormState.ModifiedUnsaved => "unsaved",
            CustomerFormState.Existing => "saved",
            _ => "empty"
        };

        public bool HasCustomerChanges =>
            CustomerState is CustomerFormState.NewUnsaved or CustomerFormState.ModifiedUnsaved;

        /// <summary>
        /// The first thing wrong with the patient panel, or null when there is nothing.
        /// </summary>
        /// <remarks>
        /// Live and in one line, in place of the chain of MessageBoxes that used to fire one at a
        /// time on save — each of which sent the dispatcher back to fix one field and press save
        /// again to find the next.
        /// </remarks>
        public string CustomerValidationMessage
        {
            get
            {
                var f = _customerNow;
                if (f == null) return null;

                if (string.IsNullOrWhiteSpace(f.FullName)) return Missing("home.PatientName");
                if (string.IsNullOrWhiteSpace(f.Phone) && string.IsNullOrWhiteSpace(f.MobilePhone))
                    return Missing("home.PatientPhone");
                if (string.IsNullOrWhiteSpace(f.Address)) return Missing("home.PatientAddress");
                if (f.Dob == null) return Missing("home.PatientDob");

                return null;
            }
        }

        public bool CanSaveCustomer => CustomerValidationMessage == null && HasCustomerChanges;

        /// <summary>
        /// Told by the view whenever a box in the patient panel changes.
        /// </summary>
        public void ReportCustomerFields(CustomerFields fields)
        {
            _customerNow = fields;

            var empty = fields == null ||
                        (string.IsNullOrWhiteSpace(fields.FullName) &&
                         string.IsNullOrWhiteSpace(fields.Phone) &&
                         string.IsNullOrWhiteSpace(fields.Address));

            CustomerState =
                empty && SelectedCustomer == null ? CustomerFormState.Empty
                : SelectedCustomer == null ? CustomerFormState.NewUnsaved
                : _customerOnEntry != null && fields != _customerOnEntry ? CustomerFormState.ModifiedUnsaved
                : CustomerFormState.Existing;

            OnPropertyChanged(nameof(CustomerStateLabel));
            OnPropertyChanged(nameof(CustomerStateTag));
            OnPropertyChanged(nameof(HasCustomerChanges));
            OnPropertyChanged(nameof(CustomerValidationMessage));
            OnPropertyChanged(nameof(CanSaveCustomer));
            OnPropertyChanged(nameof(DuplicateCustomerWarning));
        }

        /// <summary>Photographs the patient as the server has them, for the comparison above.</summary>
        public void BaselineCustomer(CustomerFields fields)
        {
            _customerOnEntry = fields;

            ReportCustomerFields(fields);
        }

        /// <summary>
        /// A patient already on file with the same name and phone.
        /// </summary>
        /// <remarks>
        /// Only a warning, never a block: two people at the same address really do share a phone,
        /// and a dispatcher on the telephone cannot be stopped by a guess. What it prevents is the
        /// silent third and fourth copy of the same patient that nobody notices until billing.
        /// </remarks>
        public string DuplicateCustomerWarning
        {
            get
            {
                if (SelectedCustomer != null || _customerNow == null) return null;
                if (string.IsNullOrWhiteSpace(_customerNow.FullName)) return null;

                var phone = _customerNow.Phone ?? _customerNow.MobilePhone;

                var twin = Customers?.FirstOrDefault(c =>
                    string.Equals(c.FullName?.Trim(), _customerNow.FullName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(phone) &&
                    (string.Equals(c.Phone, phone, StringComparison.Ordinal) ||
                     string.Equals(c.MobilePhone, phone, StringComparison.Ordinal)));

                return twin == null
                    ? null
                    : string.Format(LocalizationService.Instance["home.PatientDuplicate"], twin.ClientCode);
            }
        }

        public bool HasDuplicateWarning => DuplicateCustomerWarning != null;

        /// <summary>Raised when the view should put the boxes back as the server has them.</summary>
        public event Action DiscardCustomerRequested;

        [RelayCommand]
        private void DiscardCustomerChanges() => DiscardCustomerRequested?.Invoke();

        public string PatientStateHeader => LocalizationService.Instance["home.PatientState"];
        public string DiscardChangesLabel => LocalizationService.Instance["home.DiscardPatient"];

        #endregion

        #region The first-run tour

        private const string TourSeenKey = "HomeTourSeen";

        [ObservableProperty] private bool _isTourOpen;

        /// <summary>Which balloon is showing, 1 to 4.</summary>
        [ObservableProperty] private int _tourStep = 1;

        partial void OnTourStepChanged(int value) => RefreshTourFlags();
        partial void OnIsTourOpenChanged(bool value) => RefreshTourFlags();

        public bool IsTourStep1 => IsTourOpen && TourStep == 1;
        public bool IsTourStep2 => IsTourOpen && TourStep == 2;
        public bool IsTourStep3 => IsTourOpen && TourStep == 3;
        public bool IsTourStep4 => IsTourOpen && TourStep == 4;

        public string TourBody1 => LocalizationService.Instance["home.Tour1"];
        public string TourBody2 => LocalizationService.Instance["home.Tour2"];
        public string TourBody3 => LocalizationService.Instance["home.Tour3"];
        public string TourBody4 => LocalizationService.Instance["home.Tour4"];
        public string TourNextLabel => LocalizationService.Instance["home.TourNext"];
        public string TourDoneLabel => LocalizationService.Instance["home.TourDone"];
        public string TourHelpToolTip => LocalizationService.Instance["home.TourHelp"];

        private void RefreshTourFlags()
        {
            OnPropertyChanged(nameof(IsTourStep1));
            OnPropertyChanged(nameof(IsTourStep2));
            OnPropertyChanged(nameof(IsTourStep3));
            OnPropertyChanged(nameof(IsTourStep4));
        }

        /// <summary>
        /// Shows the tour, from the ? button or from F1.
        /// </summary>
        /// <remarks>
        /// It runs by itself only once, ever. A tour that reappears is a tour people learn to
        /// dismiss without reading, which costs the one chance it had.
        /// </remarks>
        [RelayCommand]
        public void StartTour()
        {
            TourStep = 1;
            IsTourOpen = true;
        }

        [RelayCommand]
        private void NextTourStep()
        {
            if (TourStep >= 4)
            {
                EndTour();
                return;
            }

            TourStep++;
        }

        [RelayCommand]
        private void EndTour()
        {
            IsTourOpen = false;

            _config.Save(TourSeenKey, true);
        }

        private void ShowTourIfNeverSeen()
        {
            if (_config.Load<bool>(TourSeenKey)) return;

            StartTour();
        }

        #endregion

        #region Sort order that survives the session

        private const string SortKey = "HomeGridSort";

        private sealed class SavedSort
        {
            public string Property { get; set; }
            public bool Ascending { get; set; }
        }

        /// <summary>
        /// Remembers how the dispatcher sorted the grid.
        /// </summary>
        /// <remarks>
        /// Kept because the sort is how someone works, not what they are looking at: a dispatcher
        /// who reads the day by pickup time re-sorts on every single load without this.
        /// </remarks>
        public void RememberSort(string property, bool ascending)
        {
            if (string.IsNullOrWhiteSpace(property)) return;

            _config.Save(SortKey, new SavedSort { Property = property, Ascending = ascending });
        }

        /// <summary>Puts the remembered sort back on a view that has just been rebuilt.</summary>
        public void ApplySavedSort()
        {
            var saved = _config.Load<SavedSort>(SortKey);

            if (saved == null || string.IsNullOrWhiteSpace(saved.Property) || TripsView == null) return;

            TripsView.SortDescriptions.Clear();
            TripsView.SortDescriptions.Add(new SortDescription(
                saved.Property,
                saved.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending));
        }

        #endregion

        #region Which columns are on screen

        /// <summary>What each column asks before drawing itself.</summary>
        public ColumnVisibilityMap ColumnVisibility { get; } = new();

        /// <summary>
        /// The layout behind <see cref="ColumnVisibility"/>, in the order the dialog shows it.
        /// </summary>
        private ObservableCollection<ColumnConfig> _columnLayout = new();

        /// <summary>
        /// Reads back the layout kept for this grid, or builds the default one.
        /// </summary>
        /// <remarks>
        /// Its own key, not the Schedule tab's: the two grids show different columns, and one
        /// layout serving both means hiding a column here hides an unrelated one there.
        /// </remarks>
        private void InitializeColumns()
        {
            var defaults = new[]
            {
                ("Day", DayText), ("Date", DateText), ("FromTime", FromTimeText), ("ToTime", ToTimeText),
                ("CustomerName", CustomerNameText), ("PickupAddress", PickupAddressText),
                ("DropoffAddress", DropoffAddressText), ("PickupCity", PickupCityText),
                ("DropoffCity", DropoffCityText), ("SpaceTypeName", SpaceTypeNameText),
                ("FundingSourceName", FundingSourceText), ("Type", TypeText), ("TripId", TripIdText),
                ("RunName", RunText), ("Distance", DistanceText), ("Charge", ChargeText),
                ("Paid", PaidText), ("Authorization", AuthorizationText), ("Pickup", PickupText),
                ("PickupPhone", PickupPhoneText), ("PickupComment", PickupCommentText),
                ("Dropoff", DropoffText), ("DropoffPhone", DropoffPhoneText),
                ("DropoffComment", DropoffCommentText),
                ("DriverNoShowReason", DriverNoShowReasonText)
            };

            var saved = _config.LoadColumnConfig(ColumnsKey);

            _columnLayout = new ObservableCollection<ColumnConfig>(
                defaults.Select(d => new ColumnConfig
                {
                    PropertyName = d.Item1,
                    Header = d.Item2,
                    IsVisible = saved?.FirstOrDefault(s => s.PropertyName == d.Item1)?.IsVisible ?? true
                }));

            ColumnVisibility.Apply(_columnLayout);

            IsCompactGrid = _config.Load<bool>(CompactKey);
        }

        [RelayCommand]
        private void ChooseColumns()
        {
            Action close = null;

            var dialogViewModel = new ScheduleColumnSelectorViewModel(_columnLayout, () => close?.Invoke());
            var dialog = new Views.Schedules.ColumnSelectorView { DataContext = dialogViewModel };

            close = () => dialog.Close();

            dialog.ShowDialog();

            if (dialogViewModel.DialogResult != true) return;

            _columnLayout = new ObservableCollection<ColumnConfig>(dialogViewModel.Columns);

            ColumnVisibility.Apply(_columnLayout);
            _config.SaveColumnConfig(ColumnsKey, _columnLayout);
        }

        #endregion

        /// <summary>
        /// "N of M trips" — what the filters let through, against what the day actually holds.
        /// </summary>
        /// <remarks>
        /// It used to be the total alone, and the total alone cannot answer the question a
        /// dispatcher asks when the list looks wrong: whether the trips are missing or hidden.
        /// </remarks>
        public string GridSummary =>
            string.Format(
                LocalizationService.Instance["home.GridSummary"],
                TripsView?.Cast<object>().Count() ?? TripsByDate?.Count ?? 0,
                TripsByDate?.Count ?? 0);
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
                if (_tripsByDate != null)
                    _tripsByDate.CollectionChanged -= OnTripsByDateChanged;

                _tripsByDate = value;

                if (_tripsByDate != null)
                    _tripsByDate.CollectionChanged += OnTripsByDateChanged;

                // The grid binds to the view, not to this. Replacing the collection has to
                // replace the view with it or the grid keeps showing the old day forever.
                TripsView = _tripsByDate == null
                    ? null
                    : CollectionViewSource.GetDefaultView(_tripsByDate);

                if (TripsView != null) TripsView.Filter = PassesExpressFilters;

                OnPropertyChanged();
                OnPropertyChanged(nameof(TripsView));
                OnPropertyChanged(nameof(GridSummary));
            }
        }

        #region Express filters

        /// <summary>
        /// What the grid shows: <see cref="TripsByDate"/> seen through the filters above it.
        /// </summary>
        /// <remarks>
        /// The grid binds here and not to the collection. Filtering through an ICollectionView is
        /// what lets the checkbox and the search box hide rows without touching what was loaded,
        /// so nothing has to be fetched again to show a canceled trip.
        /// </remarks>
        public ICollectionView TripsView { get; private set; }

        /// <summary>
        /// Whether canceled trips stay in the list. On by default.
        /// </summary>
        /// <remarks>
        /// ⚠️ It was a plain auto-property with no notification and nothing reading it: the
        /// checkbox had been on the screen since 1.3.0 and had never done anything. On by default
        /// because a canceled trip is still information a dispatcher needs — someone will ring
        /// about it — and because a filter that starts by hiding rows is how a list ends up
        /// looking empty for no visible reason.
        /// </remarks>
        [ObservableProperty] private bool _showCanceled = true;

        partial void OnShowCanceledChanged(bool value) => RefreshTripsView();

        /// <summary>
        /// The broker's trip id, and only that.
        /// </summary>
        /// <remarks>
        /// It searched the patient and both addresses as well, and the box it needed for that ate
        /// the width of the row it sits in. <c>TripId</c> is the one a dispatcher is holding when
        /// they need this: it is the number the broker quotes on the telephone. Everything else
        /// is in the filter panel, which has room for it.
        /// </remarks>
        [ObservableProperty] private string _tripSearchText;

        partial void OnTripSearchTextChanged(string value) => RefreshTripsView();

        private void OnTripsByDateChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(GridSummary));
            OnPropertyChanged(nameof(GridTotals));
        }

        private void RefreshTripsView()
        {
            TripsView?.Refresh();
            OnPropertyChanged(nameof(GridSummary));
            OnPropertyChanged(nameof(GridTotals));
        }

        private bool PassesExpressFilters(object item)
        {
            if (item is not TripReadDto trip) return false;
            if (!ShowCanceled && IsCanceled(trip)) return false;
            if (!MatchesSearch(trip)) return false;

            return Filters.Matches(trip);
        }

        /// <summary>
        /// The filters behind the sliding panel. Everything they need is already in the loaded
        /// trips, so opening the panel and ticking things costs no request.
        /// </summary>
        public HomeFiltersViewModel Filters { get; } = new();

        /// <summary>
        /// Whether the panel is out. It slides over the grid rather than pushing it aside, so the
        /// list stays visible and refilters as the ticks change — which is the only way to tell
        /// whether a filter did what you meant.
        /// </summary>
        [ObservableProperty] private bool _isFilterPanelOpen;

        [RelayCommand] private void ToggleFilterPanel() => IsFilterPanelOpen = !IsFilterPanelOpen;

        [RelayCommand] private void CloseFilterPanel() => IsFilterPanelOpen = false;

        private static bool IsCanceled(TripReadDto trip) =>
            trip.IsCancelled ||
            string.Equals(trip.Status, TripStatus.Canceled, StringComparison.OrdinalIgnoreCase);

        private bool MatchesSearch(TripReadDto trip)
        {
            var query = TripSearchText?.Trim();

            return string.IsNullOrEmpty(query) || Holds(trip.TripId, query);
        }

        private static bool Holds(string text, string part) =>
            !string.IsNullOrEmpty(text) &&
            !string.IsNullOrEmpty(part) &&
            text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        private DateTime _filterDate;
        /// <summary>
        /// The day the grid is showing. A filter, and only that.
        /// </summary>
        /// <remarks>
        /// ⚠️ Until RE-010 this doubled as the date of the trip being booked, which is why the
        /// calendar inside the form was bound to it: setting a trip's date reloaded the day
        /// underneath the form and emptied the grid's selection. The trip's date is
        /// <see cref="TripDate"/> now. The old null branch below is gone with it — <c>DateTime</c>
        /// is never null, so it could not run.
        /// </remarks>
        public DateTime FilterDate
        {
            get => _filterDate;
            set
            {
                if (_filterDate == value) return;

                _filterDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DateSpanLabel));

                LoadTripsAsync();
            }
        }

        #region Date span

        private DateTime? _filterEndDate;
        /// <summary>
        /// The last day of the span, or null when the grid is on a single day.
        /// </summary>
        /// <remarks>
        /// Null is the normal state and the one the tab opens in. A range is something a
        /// dispatcher asks for; it is never where they are put by default, because a list holding
        /// a week of trips answers a different question from the one this screen exists for.
        /// </remarks>
        public DateTime? FilterEndDate
        {
            get => _filterEndDate;
            private set
            {
                _filterEndDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDateRange));
                OnPropertyChanged(nameof(DateSpanLabel));
            }
        }

        public bool IsDateRange => FilterEndDate.HasValue;

        /// <summary>What the date button reads: one day, or the two ends of a span.</summary>
        public string DateSpanLabel => IsDateRange
            ? $"{FilterDate:dd/MM} – {FilterEndDate:dd/MM/yyyy}"
            : FilterDate.ToString("dd/MM/yyyy");

        [ObservableProperty] private bool _isDatePopupOpen;

        /// <summary>
        /// Whether the popup is asking for a span rather than a day. Starts off, every time.
        /// </summary>
        [ObservableProperty] private bool _rangeMode;

        partial void OnRangeModeChanged(bool value)
        {
            _rangeAnchor = null;
            OnPropertyChanged(nameof(RangePickHint));
        }

        /// <summary>The first of the two clicks of a range, once it has been made.</summary>
        private DateTime? _rangeAnchor;

        /// <summary>
        /// The line under the calendar. A two-click control that does not say which click it is
        /// waiting for is a control people click twice and then undo.
        /// </summary>
        public string RangePickHint => !RangeMode
            ? LocalizationService.Instance["home.PickDay"]
            : _rangeAnchor == null
                ? LocalizationService.Instance["home.PickFirstDay"]
                : LocalizationService.Instance["home.PickLastDay"];

        /// <summary>
        /// The calendar's own selection. Its setter is the two-click flow.
        /// </summary>
        [ObservableProperty] private DateTime? _pickedDate;

        partial void OnPickedDateChanged(DateTime? value)
        {
            if (value == null) return;

            if (!RangeMode)
            {
                ApplyDateSpan(value.Value, null);
                IsDatePopupOpen = false;
                return;
            }

            if (_rangeAnchor == null)
            {
                _rangeAnchor = value.Value;
                OnPropertyChanged(nameof(RangePickHint));
                return;
            }

            // Clicked backwards on purpose or by accident: the earlier of the two is the start.
            var first = _rangeAnchor.Value;
            var second = value.Value;

            ApplyDateSpan(first <= second ? first : second, first <= second ? second : first);

            _rangeAnchor = null;
            IsDatePopupOpen = false;
        }

        /// <summary>
        /// Moves the grid to a day or a span and reloads it once.
        /// </summary>
        /// <remarks>
        /// It writes the fields rather than the properties so that changing both ends of a span
        /// fetches once instead of twice — the setters each reload on their own.
        /// </remarks>
        private void ApplyDateSpan(DateTime start, DateTime? end)
        {
            _filterDate = start.Date;
            _filterEndDate = end?.Date;

            OnPropertyChanged(nameof(FilterDate));
            OnPropertyChanged(nameof(FilterEndDate));
            OnPropertyChanged(nameof(IsDateRange));
            OnPropertyChanged(nameof(DateSpanLabel));

            LoadTripsAsync();
        }

        /// <summary>
        /// Opens the popup on a clean slate: whatever half-made range was abandoned last time is
        /// not what the dispatcher is asking for now.
        /// </summary>
        [RelayCommand]
        private void OpenDatePopup()
        {
            RangeMode = IsDateRange;
            _rangeAnchor = null;
            PickedDate = null;

            OnPropertyChanged(nameof(RangePickHint));

            IsDatePopupOpen = true;
        }

        [RelayCommand] private void UseSingleDay() => RangeMode = false;

        [RelayCommand] private void UseRange() => RangeMode = true;

        [RelayCommand] private void PresetToday() => ApplyDateSpan(DateTime.Today, null);

        [RelayCommand] private void PresetTomorrow() => ApplyDateSpan(DateTime.Today.AddDays(1), null);

        /// <summary>Monday to Sunday of the week the dispatcher is standing in.</summary>
        [RelayCommand]
        private void PresetThisWeek()
        {
            var today = DateTime.Today;
            var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);

            ApplyDateSpan(monday, monday.AddDays(6));
        }

        [RelayCommand]
        private void PresetNextSeven() => ApplyDateSpan(DateTime.Today, DateTime.Today.AddDays(6));

        /// <summary>Drops the span back to its first day. This is the chip's ✕.</summary>
        [RelayCommand] private void ClearDateRange() => ApplyDateSpan(FilterDate, null);

        #endregion

        /// <summary>
        /// Moves the grid to another day when the caller loads that day itself, so it is not
        /// fetched twice.
        /// </summary>
        private void MoveGridTo(DateTime date)
        {
            if (_filterDate == date) return;

            _filterDate = date;
            OnPropertyChanged(nameof(FilterDate));
        }

        public DateTime? TripFilterDate { get; set; } = DateTime.Today;

       

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
                RefreshGuidance();
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
                RefreshGuidance();
                UpdateSelectedCharges();
                UpdateNonDefaultCharges();
            }
        }

        /// <summary>
        /// The broker whose file is being imported.
        /// </summary>
        /// <remarks>
        /// ⚠️ This had its own name but not its own field: until RE-010 it read and wrote
        /// <c>_selectedFundingSource</c>, so choosing a broker to import from silently changed
        /// the Funding Source of the trip form — without repainting the combo that shows it,
        /// and without recalculating its charges. The next trip booked by hand was billed to
        /// whoever had last been imported.
        /// </remarks>
        private FundingSource _selectedFundingSourceImport;
        public FundingSource SelectedFundingSourceImport
        {
            get => _selectedFundingSourceImport;
            set
            {
                _selectedFundingSourceImport = value;
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
                RefreshGuidance();
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

            Filters.Changed += RefreshTripsView;

            InitializeColumns();
            ShowTourIfNeverSeen();

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
        public bool CanEditWillCall => TripBeingEdited is null || TripBeingEdited.Id <= 0;

        public string WillCallLockedToolTip =>
            LocalizationService.Instance["WillCallLockedHint"];

        /// <summary>
        /// The grid selection moved. Everything the form does about it is decided here.
        /// </summary>
        /// <remarks>
        /// ⚠️ The order of the guards is the whole method. Moving off the trip on the form is an
        /// exit **whatever the selection moved to** — to no row at all, or straight to another
        /// trip. RE-010 first shipped with the confirmation on the null branch only, so picking a
        /// second trip in the grid threw away the edits to the first without a word.
        /// </remarks>
        partial void OnSelectedTripChanged(TripReadDto oldValue, TripReadDto newValue)
        {
            // Putting a row back after the dispatcher declined to leave. The form already holds
            // their work; replaying the load would write the stored trip over it.
            if (_restoringSelection) return;

            // Reloading the day empties TripsByDate and the grid drops its selection with it.
            // That is not the dispatcher going anywhere — it happens on every save and on every
            // cancel — so the form keeps the trip it is editing, and LoadTripsByDateAsync lights
            // the row again when the day comes back down.
            if (IsLoadingTrips) return;

            // Already on the way out through TryLeaveTripForm, which asked once.
            if (_leavingTripForm) return;

            // The one confirmation, for every direction the selection can move.
            if (CurrentMode == HomeMode.EditingTrip && !ConfirmDiscardTripChanges())
            {
                RestoreSelection(oldValue);
                return;
            }

            if (newValue == null)
            {
                if (CurrentMode == HomeMode.EditingTrip)
                {
                    TripBeingEdited = null;
                    ClearTripForm();
                    CurrentMode = HomeMode.Browsing;
                }

                return;
            }

            // Selecting a row shows the trip on the map. Editing it is a double click, and the
            // form is only filled from there — see BeginEditSelectedTrip.
            if (CurrentMode != HomeMode.EditingTrip) return;

            LoadTripIntoForm(newValue);
        }

        /// <summary>
        /// Fills the trip form from a trip and puts the screen into editing.
        /// </summary>
        private void LoadTripIntoForm(TripReadDto value)
        {

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

            // 5. What the form is editing, and for what day. TripBeingEdited raises
            // CanEditWillCall by itself, which is why the old notification at the top of this
            // method is gone: the grid's selection is not what decides it any more.
            TripBeingEdited = value;
            TripDate = value.Date.Date;

            // Last, on purpose: OnCurrentModeChanged photographs the form for
            // HasUnsavedTripChanges, and it has to see it already filled.
            CurrentMode = HomeMode.EditingTrip;
        }

        /// <summary>
        /// Opens the selected trip for editing. This is what a double click means.
        /// </summary>
        /// <remarks>
        /// ⚠️ A single click deliberately does NOT come here. Clicking a row is how a dispatcher
        /// looks at a trip on the map, which is what they want almost every time; until RE-010 it
        /// also dropped them into full edit mode, and there was no way out of it. Selecting shows,
        /// double click edits.
        /// </remarks>
        [RelayCommand]
        public void BeginEditSelectedTrip()
        {
            var trip = SelectedTrip;
            if (trip == null || CurrentMode == HomeMode.EditingTrip) return;

            LoadTripIntoForm(trip);
        }

        /// <summary>
        /// Puts the grid selection back on a trip the dispatcher chose not to leave.
        /// </summary>
        /// <remarks>
        /// Posted rather than assigned: the grid is in the middle of its own selection change and
        /// refuses a new one until it is done. <c>_restoringSelection</c> is what stops the trip
        /// being loaded over the edits they just kept.
        /// </remarks>
        private void RestoreSelection(TripReadDto trip)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _restoringSelection = true;
                try { SelectedTrip = trip; }
                finally { _restoringSelection = false; }
            }));
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

        /// <summary>
        /// Reloads the grid for whatever the date control is currently asking for.
        /// </summary>
        /// <remarks>
        /// Everything that refreshes the list calls this rather than the single-day method, so a
        /// dispatcher who cancels a trip while looking at a week does not silently drop back to
        /// one day.
        /// </remarks>
        public Task LoadTripsAsync() => IsDateRange
            ? LoadTripsByDateRangeAsync(FilterDate, FilterEndDate.Value)
            : LoadTripsByDateAsync(FilterDate);

        public Task LoadTripsByDateAsync(DateTime date) =>
            FillGridAsync(service => service.GetTripsByDateAsync(date));

        public Task LoadTripsByDateRangeAsync(DateTime start, DateTime end) =>
            FillGridAsync(service => service.GetTripsByDateRangeAsync(start, end));

        private async Task FillGridAsync(Func<TripService, Task<List<TripReadDto>>> fetch)
        {
            IsLoadingTrips = true;
            TripsByDate.Clear();

            try
            {
                TripService _tripService = new TripService();
                var sources = await fetch(_tripService);


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

            // The panel offers what this day actually holds, with counts, and keeps whatever was
            // already ticked.
            Filters.Rebuild(TripsByDate);
            Filters.WatchOptions();
            RefreshTripsView();

            // The grid dropped its selection when the day was emptied. If the form is still on a
            // trip that belongs to this day, light its row again: a trip being edited with no row
            // selected is how the selection gets "lost for some other reason", and the next thing
            // that touches it reads as the dispatcher moving away from work they never left.
            if (TripBeingEdited != null)
            {
                var row = TripsByDate.FirstOrDefault(t => t.Id == TripBeingEdited.Id);
                if (row != null && !ReferenceEquals(row, SelectedTrip))
                {
                    _restoringSelection = true;
                    try { SelectedTrip = row; }
                    finally { _restoringSelection = false; }
                }
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

        private void ImportTrips() => EnterImportMode();

        /// <summary>
        /// Sends the trips on screen to a spreadsheet.
        /// </summary>
        /// <remarks>
        /// What leaves is what the dispatcher can see: the rows the filters let through, in the
        /// order the grid has them. Reading the view rather than the collection is the whole
        /// point — a file holding trips the screen was hiding is one nobody can check.
        /// </remarks>
        private void ExportTrips()
        {
            var onScreen = TripsView?.Cast<TripReadDto>().ToList() ?? new List<TripReadDto>();

            new TripExcelExportService().Export(onScreen, FilterDate);
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
                DateTime tripDate = DateTime.SpecifyKind(TripDate.Date, DateTimeKind.Unspecified); // tells the system: "Don't touch the time, send it as is."

                // An edit is an edit. TripBeingEdited, never SelectedTrip: the grid clears its
                // selection on every reload, and reading create-or-update from it is what used to
                // write the trip a second time instead of updating it.
                if (TripBeingEdited != null && TripBeingEdited.Id > 0)
                {
                    var tripReadDto = new TripReadDto
                    {
                        Id = TripBeingEdited.Id,
                        TripId = TripBeingEdited.TripId, // Mantener el ID externo original
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
                        Status = TripBeingEdited.Status ?? TripStatus.Accepted,

                        // ⚠️ The trip's own value, not the checkbox. Editing a trip cannot
                        // move Will Call: it goes through Activate / Back to Will Call on
                        // the open-trips grid of Schedule, and the server ignores it here.
                        WillCall = TripBeingEdited.WillCall,
                        Type = IsReturn ? "Return" : "Appointment",
                        Created = TripBeingEdited.Created,
                        VehicleRouteId = TripBeingEdited.VehicleRouteId // Mantener la ruta asignada si existe

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

                // The grid follows the trip: booked or moved to another day, it still has to be
                // in front of the dispatcher who just saved it.
                MoveGridTo(tripDate);
                await LoadTripsAsync();

                // Browsing first: the trip is on the server, so there is nothing unsaved left
                // and clearing the selection below must not stop to ask about it.
                TripBeingEdited = null;
                CurrentMode = HomeMode.Browsing;
                SelectedTrip = null; // Limpiar selección después de guardar
                ClearTripForm();
                SearchText = string.Empty;
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
                    await LoadTripsAsync();
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
                  
                    await LoadTripsAsync();
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
                  
                    await LoadTripsAsync();
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
