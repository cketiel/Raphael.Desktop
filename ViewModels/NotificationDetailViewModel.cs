using System.Collections.ObjectModel;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// The open notice: everything known about it, and everything known about the trip it is
/// about — the journey, the trip's own record, the patient, the driver, the vehicle and
/// the line that was going to run it.
/// </summary>
/// <remarks>
/// The notification itself never carries patient data — only the trip identifier — because
/// its text also goes out as a push and ends up on a lock screen. The detail is loaded
/// here instead, over an authenticated call, once a dispatcher actually opens the notice.
///
/// <para>
/// ⚠️ Patient contact details and the route are fetched when a notice is <b>opened</b>, never
/// while the inbox is being scrolled. That distinction is the whole safeguard: a dispatcher
/// who opens a cancellation has a reason to see the patient's phone number, and one who
/// arrows past fifty rows does not — and would have cost three calls a row.
/// </para>
/// </remarks>
public sealed class NotificationDetailViewModel : BaseViewModel
{
    private readonly TripService _tripService;

    private readonly CustomerService _customerService;

    private readonly RunService _runService;

    private readonly ScheduleService _scheduleService;

    private readonly Dictionary<int, TripReadDto> _tripCache;

    private NotificationItemViewModel _notification;

    private TripReadDto _trip;

    private Customer _customer;

    private VehicleRoute _route;

    /// <summary>
    /// The trip's two schedule events: what the vehicle was supposed to do at each end and
    /// what it actually did.
    /// </summary>
    private ScheduleDto _pickupEvent;

    private ScheduleDto _dropoffEvent;

    private bool _isLoadingTrip;

    private bool _isLoadingCustomer;

    private bool _isLoadingRoute;

    private bool _isLoadingSchedules;

    private string _errorMessage;

    public NotificationDetailViewModel(
        TripService tripService,
        CustomerService customerService,
        RunService runService,
        ScheduleService scheduleService,
        Dictionary<int, TripReadDto> tripCache)
    {
        _tripService = tripService;
        _customerService = customerService;
        _runService = runService;
        _scheduleService = scheduleService;
        _tripCache = tripCache;

        // The labels are baked into the tiles when they are built, so changing language has
        // to build them again. Nothing else in the panel needs this: everything else reads
        // its text through a property.
        LocalizationService.Instance.LanguageChanged += RebuildFields;
    }

    #region Cards

    /// <summary>Where the vehicle was going to pick the patient up.</summary>
    public ObservableCollection<DetailFieldViewModel> PickupFields { get; } = [];

    /// <summary>Where it was going to leave them.</summary>
    public ObservableCollection<DetailFieldViewModel> DropoffFields { get; } = [];

    public ObservableCollection<DetailFieldViewModel> TripFields { get; } = [];

    public ObservableCollection<DetailFieldViewModel> BillingFields { get; } = [];

    public ObservableCollection<DetailFieldViewModel> PatientFields { get; } = [];

    public ObservableCollection<DetailFieldViewModel> DriverFields { get; } = [];

    public ObservableCollection<DetailFieldViewModel> VehicleFields { get; } = [];

    public ObservableCollection<DetailFieldViewModel> LineFields { get; } = [];

    public bool HasBilling => BillingFields.Count > 0;

    public bool HasPatient => PatientFields.Count > 0;

    public bool HasDriver => DriverFields.Count > 0;

    public bool HasVehicle => VehicleFields.Count > 0;

    public bool HasLine => LineFields.Count > 0;

    #endregion

    public NotificationItemViewModel Notification
    {
        get => _notification;
        private set => SetProperty(ref _notification, value);
    }

    public TripReadDto Trip
    {
        get => _trip;
        private set
        {
            if (!SetProperty(ref _trip, value))
                return;

            OnPropertyChanged(nameof(HasTrip));
            OnPropertyChanged(nameof(TripNumberDisplay));
            OnPropertyChanged(nameof(ScheduleDisplay));
            OnPropertyChanged(nameof(PickupDisplay));
            OnPropertyChanged(nameof(DropoffDisplay));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(HasWillCall));

            BuildTripFields();
        }
    }

    public Customer Customer
    {
        get => _customer;
        private set
        {
            if (!SetProperty(ref _customer, value))
                return;

            OnPropertyChanged(nameof(HasCustomer));

            BuildPatientFields();
        }
    }

    public VehicleRoute Route
    {
        get => _route;
        private set
        {
            if (!SetProperty(ref _route, value))
                return;

            OnPropertyChanged(nameof(HasRoute));

            BuildRouteFields();
        }
    }

    public bool IsLoadingTrip
    {
        get => _isLoadingTrip;
        private set => SetProperty(ref _isLoadingTrip, value);
    }

    public bool IsLoadingCustomer
    {
        get => _isLoadingCustomer;
        private set => SetProperty(ref _isLoadingCustomer, value);
    }

    public bool IsLoadingRoute
    {
        get => _isLoadingRoute;
        private set => SetProperty(ref _isLoadingRoute, value);
    }

    public bool IsLoadingSchedules
    {
        get => _isLoadingSchedules;
        private set => SetProperty(ref _isLoadingSchedules, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasTrip => Trip is not null;

    public bool HasCustomer => Customer is not null;

    public bool HasRoute => Route is not null;

    /// <summary>
    /// Both numbers, because they are not the same thing and both get used. The internal
    /// identifier is what the notification text cites; <c>TripId</c> is the number a broker
    /// or a funding source quotes on the phone.
    /// </summary>
    public string TripNumberDisplay
    {
        get
        {
            if (Trip is null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(Trip.TripId)
                ? Trip.Id.ToString()
                : $"{Trip.Id} · {Trip.TripId}";
        }
    }

    /// <summary>
    /// The date and the pickup window, as wall-clock time where the trip is operated.
    /// </summary>
    /// <remarks>
    /// ⚠️ Never converted to the reader's zone. A trip at 09:15 is at 09:15 at the pickup
    /// address; showing a dispatcher in another region their own 06:15 is how a vehicle
    /// gets sent three hours early. See <c>_meta/TIME_POLICY.md</c> §2B.
    /// </remarks>
    public string ScheduleDisplay
    {
        get
        {
            if (Trip is null)
                return string.Empty;

            var date = Trip.Date.ToString("D");

            var window = Window();

            return string.IsNullOrEmpty(window)
                ? date
                : $"{date} · {window}";
        }
    }

    public string StatusDisplay => Trip?.Status ?? string.Empty;

    public bool HasWillCall => Trip?.WillCall == true;

    /// <summary>
    /// What the vehicle covered between the two ends of the trip.
    /// </summary>
    /// <remarks>
    /// The dropoff event carries the leg that starts at the pickup: its distance is the one
    /// travelled with the patient on board, and its travel time is how long that took. Drawn
    /// on the line joining the two points, because that is exactly what it measures.
    /// </remarks>
    public string LegDistanceDisplay => Miles(_dropoffEvent?.Distance);

    public string LegTravelDisplay => Duration(_dropoffEvent?.Travel);

    public bool HasLegDistance => !string.IsNullOrEmpty(LegDistanceDisplay);

    public bool HasLegTravel => !string.IsNullOrEmpty(LegTravelDisplay);

    public bool HasLeg => HasLegDistance || HasLegTravel;

    public string PickupDisplay => Compose(Trip?.PickupAddress, Trip?.PickupCity);

    public string DropoffDisplay => Compose(Trip?.DropoffAddress, Trip?.DropoffCity);

    /// <summary>
    /// Shows a notification: its trip, and then the patient and the line behind it.
    /// </summary>
    public async Task ShowAsync(NotificationItemViewModel notification)
    {
        Notification = notification;

        Trip = null;
        Customer = null;
        Route = null;
        ErrorMessage = null;

        ClearSchedules();

        var tripId = notification?.TripId;

        if (!tripId.HasValue)
            return;

        if (_tripCache.TryGetValue(tripId.Value, out var cached))
        {
            Trip = cached;

            await LoadAroundTripAsync(notification);

            return;
        }

        IsLoadingTrip = true;

        try
        {
            var trip = await _tripService.GetTripByIdAsync(tripId.Value);

            if (trip is null)
            {
                // A trip deleted after the notice was raised. Say so instead of leaving
                // an empty card that reads like a loading failure.
                ErrorMessage = LocalizationService.Instance["NotificationTripNotFound"];
                return;
            }

            _tripCache[tripId.Value] = trip;

            // Only visible once the trip is known, but the search box has to find it, so
            // it goes back onto the row.
            if (notification is not null)
                notification.BrokerTripNumber = trip.TripId;

            // The selection may have moved on while this was in flight.
            if (Notification?.NotificationId != notification?.NotificationId)
                return;

            Trip = trip;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;

            return;
        }
        finally
        {
            IsLoadingTrip = false;
        }

        await LoadAroundTripAsync(notification);
    }

    /// <summary>
    /// The patient and the line, once the trip is known. Both are optional: a trip nobody
    /// has scheduled yet has no line, and the card simply does not appear.
    /// </summary>
    private async Task LoadAroundTripAsync(NotificationItemViewModel notification)
    {
        await LoadSchedulesAsync();

        // The selection can move while a call is in flight.
        if (Notification?.NotificationId != notification?.NotificationId)
            return;

        await LoadCustomerAsync();

        if (Notification?.NotificationId != notification?.NotificationId)
            return;

        await LoadRouteAsync();
    }

    /// <summary>
    /// The trip's two schedule events, taken from the day of the line that was running it.
    /// </summary>
    /// <remarks>
    /// There is no endpoint that returns the events of one trip, so this asks for the line's
    /// day — the same call the dispatch board makes — and keeps the two rows that belong to
    /// this trip. A trip nobody has scheduled has no line and no events, and the journey
    /// card simply shows the addresses.
    /// </remarks>
    private async Task LoadSchedulesAsync()
    {
        var trip = Trip;

        if (trip is null || trip.VehicleRouteId <= 0 || IsLoadingSchedules)
            return;

        IsLoadingSchedules = true;

        try
        {
            var schedules = await _scheduleService.GetSchedulesAsync(
                trip.VehicleRouteId,
                trip.Date);

            var mine = schedules?
                .Where(schedule => schedule.TripId == trip.Id)
                .ToList();

            if (mine is null || mine.Count == 0)
                return;

            _pickupEvent = mine.FirstOrDefault(
                schedule => schedule.EventType == ScheduleEventType.Pickup);

            _dropoffEvent = mine.FirstOrDefault(
                schedule => schedule.EventType == ScheduleEventType.Dropoff);

            BuildJourneyFields();

            OnPropertyChanged(nameof(LegDistanceDisplay));
            OnPropertyChanged(nameof(LegTravelDisplay));
            OnPropertyChanged(nameof(HasLeg));
        OnPropertyChanged(nameof(HasLegDistance));
        OnPropertyChanged(nameof(HasLegTravel));
        }
        catch (Exception ex)
        {
            // The journey still reads without them. A failure here must not take down the
            // rest of the notice.
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingSchedules = false;
        }
    }

    private void ClearSchedules()
    {
        _pickupEvent = null;
        _dropoffEvent = null;

        OnPropertyChanged(nameof(LegDistanceDisplay));
        OnPropertyChanged(nameof(LegTravelDisplay));
        OnPropertyChanged(nameof(HasLeg));
        OnPropertyChanged(nameof(HasLegDistance));
        OnPropertyChanged(nameof(HasLegTravel));
    }

    private async Task LoadCustomerAsync()
    {
        if (Customer is not null || IsLoadingCustomer)
            return;

        var customerId = Trip?.CustomerId ?? Notification?.RiderId ?? 0;

        if (customerId <= 0)
            return;

        IsLoadingCustomer = true;

        try
        {
            Customer = await _customerService.GetCustomerByIdAsync(customerId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingCustomer = false;
        }
    }

    private async Task LoadRouteAsync()
    {
        if (Route is not null || IsLoadingRoute)
            return;

        var routeId = Trip?.VehicleRouteId ?? 0;

        if (routeId <= 0)
            return;

        IsLoadingRoute = true;

        try
        {
            Route = await _runService.GetByIdAsync(routeId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingRoute = false;
        }
    }

    #region Building the cards

    private void RebuildFields()
    {
        BuildTripFields();
        BuildPatientFields();
        BuildRouteFields();
    }

    /// <summary>
    /// Each end of the trip: how it was contacted, and what the vehicle actually did there.
    /// </summary>
    /// <remarks>
    /// ⚠️ Every hour here is wall-clock time where the trip is operated and is formatted,
    /// never converted. A pickup performed at 09:15 was performed at 09:15 at the pickup
    /// address. See <c>_meta/TIME_POLICY.md</c> §2B.
    /// </remarks>
    private void BuildJourneyFields()
    {
        PickupFields.Clear();
        DropoffFields.Clear();

        var trip = Trip;

        if (trip is null)
            return;

        Add(PickupFields, "PickupPhone", trip.PickupPhone);
        Add(PickupFields, "PickupComment", trip.PickupComment, isWide: true);

        if (_pickupEvent is not null)
        {
            Add(PickupFields, "NotificationFieldScheduled", Clock(_pickupEvent.Pickup));
            Add(PickupFields, "ETA", Clock(_pickupEvent.ETA));
            Add(PickupFields, "NotificationFieldArrived", Clock(_pickupEvent.Arrive));
            Add(PickupFields, "NotificationFieldCompleted", Clock(_pickupEvent.Perform));
            Add(PickupFields, "NotificationFieldTravelTime", Duration(_pickupEvent.Travel));
            Add(PickupFields, "NotificationFieldLegDistance", Miles(_pickupEvent.Distance));
        }

        Add(DropoffFields, "DropoffPhone", trip.DropoffPhone);
        Add(DropoffFields, "DropoffComment", trip.DropoffComment, isWide: true);

        if (_dropoffEvent is not null)
        {
            // The appointment is what the dropoff end is scheduled against; the pickup time
            // belongs to the other end.
            Add(DropoffFields, "NotificationFieldScheduled", Clock(_dropoffEvent.Appt));
            Add(DropoffFields, "ETA", Clock(_dropoffEvent.ETA));
            Add(DropoffFields, "NotificationFieldArrived", Clock(_dropoffEvent.Arrive));
            Add(DropoffFields, "NotificationFieldCompleted", Clock(_dropoffEvent.Perform));

            // Its distance and travel time are the leg from the pickup, and they are drawn
            // on the line joining the two points. Repeating them here would be the same two
            // numbers twice, four centimetres apart.
        }
    }

    private void BuildTripFields()
    {
        TripFields.Clear();
        BillingFields.Clear();

        BuildJourneyFields();

        var trip = Trip;

        if (trip is not null)
        {
            Add(TripFields, "NotificationFieldInternalId", trip.Id.ToString());

            // ⚠️ The number the business calls "trip number" is Trip.TripId — the one a
            // broker or a funding source quotes. The internal identifier above is not it.
            Add(TripFields, "NotificationFieldTripNumber", trip.TripId);
            Add(TripFields, "Type", trip.Type);
            Add(TripFields, "NotificationStateColumn", trip.Status);
            Add(TripFields, "NotificationFieldDay", trip.Day);
            Add(TripFields, "Date", trip.Date.ToString("d"));
            Add(TripFields, "NotificationFieldWindow", Window());
            Add(TripFields, "Space", trip.SpaceTypeName);
            Add(TripFields, "Distance", Miles(trip.Distance));
            Add(TripFields, "ETA", Minutes(trip.ETA));
            Add(TripFields, "Authorization", trip.Authorization);
            Add(TripFields, "FundingSource", trip.FundingSourceName);
            Add(TripFields, "NotificationRoute", trip.RunName);

            // Only when true. A "No" against every trip in the list is noise that pushes
            // the fields that do say something off the card.
            if (trip.WillCall)
                Add(TripFields, "NotificationFieldWillCall", Yes());

            if (trip.IsCancelled)
                Add(TripFields, "NotificationFieldCancelled", Yes());

            Add(TripFields, "NotificationFieldNoShow", trip.DriverNoShowReason, isWide: true);

            // ⚠️ Shown as stored. Trips created before 2026-08-25 carry the clock of
            // whoever created them, so converting this would move half the rows and leave
            // the other half where they are. See _meta/TIME_POLICY.md §5.
            Add(TripFields, "Created", trip.Created.ToString("g"));

            Add(BillingFields, "Charge", Money(trip.Charge));
            Add(BillingFields, "Paid", Money(trip.Paid));
        }

        RaiseCardFlags();
    }

    private void BuildPatientFields()
    {
        PatientFields.Clear();

        var customer = Customer;

        if (customer is not null)
        {
            Add(PatientFields, "Name", customer.FullName);
            Add(PatientFields, "ClientCode", customer.ClientCode);
            Add(PatientFields, "NotificationFieldRiderId", customer.RiderId);
            Add(PatientFields, "DOB", customer.DOB?.ToString("d"));
            Add(PatientFields, "Gender", customer.Gender);
            Add(PatientFields, "Phone", customer.Phone);
            Add(PatientFields, "MobilePhone", customer.MobilePhone);
            Add(PatientFields, "Email", customer.Email);
            Add(PatientFields, "Address", customer.Address, isWide: true);
            Add(PatientFields, "City", customer.City);
            Add(PatientFields, "State", customer.State);
            Add(PatientFields, "Zip", customer.Zip);
            Add(PatientFields, "PolicyNumber", customer.PolicyNumber);

            Add(PatientFields, "FundingSource",
                customer.FundingSourceName ?? customer.FundingSource?.Name);

            Add(PatientFields, "SpaceType", customer.SpaceTypeName);
        }

        RaiseCardFlags();
    }

    private void BuildRouteFields()
    {
        DriverFields.Clear();
        VehicleFields.Clear();
        LineFields.Clear();

        var route = Route;

        if (route is not null)
        {
            var driver = route.Driver;

            if (driver is not null)
            {
                Add(DriverFields, "Name", driver.FullName);
                Add(DriverFields, "NotificationFieldUsername", driver.Username);
                Add(DriverFields, "Phone", driver.PhoneNumber);
                Add(DriverFields, "Email", driver.Email);
                Add(DriverFields, "NotificationFieldLicense", driver.DriverLicense);
                Add(DriverFields, "Address", driver.Address, isWide: true);
            }

            var vehicle = route.Vehicle;

            if (vehicle is not null)
            {
                Add(VehicleFields, "Name", vehicle.Name);
                Add(VehicleFields, "Plate", vehicle.Plate);
                Add(VehicleFields, "Make", vehicle.Make);
                Add(VehicleFields, "Model", vehicle.Model);
                Add(VehicleFields, "Year", vehicle.Year?.ToString());
                Add(VehicleFields, "Color", vehicle.Color);
                Add(VehicleFields, "VIN", vehicle.VIN);
            }

            Add(LineFields, "Name", route.Name);
            Add(LineFields, "Description", route.Description, isWide: true);
            Add(LineFields, "Garage", route.Garage);
            Add(LineFields, "FromDate", route.FromDate.ToString("d"));
            Add(LineFields, "ToDate", route.ToDate?.ToString("d"));
            Add(LineFields, "FromTime", Clock(route.FromTime));
            Add(LineFields, "ToTime", Clock(route.ToTime));
            Add(LineFields, "SmartphoneLogin", route.SmartphoneLogin);
        }

        RaiseCardFlags();
    }

    private void RaiseCardFlags()
    {
        OnPropertyChanged(nameof(HasBilling));
        OnPropertyChanged(nameof(HasPatient));
        OnPropertyChanged(nameof(HasDriver));
        OnPropertyChanged(nameof(HasVehicle));
        OnPropertyChanged(nameof(HasLine));
    }

    /// <summary>Adds a tile, unless there is nothing to put in it.</summary>
    private static void Add(
        ICollection<DetailFieldViewModel> target,
        string labelKey,
        string value,
        bool isWide = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        target.Add(new DetailFieldViewModel(
            LocalizationService.Instance[labelKey],
            value.Trim(),
            isWide));
    }

    #endregion

    #region Formatting

    /// <summary>"09:15 – 10:00", or just the pickup time, or nothing.</summary>
    private string Window()
    {
        if (Trip?.FromTime is null)
            return string.Empty;

        var from = Clock(Trip.FromTime.Value);

        return Trip.ToTime.HasValue
            ? $"{from} – {Clock(Trip.ToTime.Value)}"
            : from;
    }

    /// <summary>
    /// A time of day as it is written on the trip, formatted and never shifted.
    /// </summary>
    private static string Clock(TimeSpan time) =>
        DateTime.Today.Add(time).ToString("t");

    private static string Clock(TimeSpan? time) =>
        time.HasValue ? Clock(time.Value) : null;

    /// <summary>"25 min", or "1 h 20 min" once it stops being readable in minutes.</summary>
    private static string Duration(TimeSpan? span)
    {
        if (!span.HasValue || span.Value <= TimeSpan.Zero)
            return null;

        var total = span.Value;

        return total.TotalMinutes < 60
            ? $"{total.TotalMinutes:0} min"
            : $"{(int)total.TotalHours} h {total.Minutes:00} min";
    }

    private static string Miles(double? distance) =>
        distance.HasValue ? $"{distance.Value:0.0} mi" : null;

    private static string Minutes(double? eta) =>
        eta.HasValue ? $"{eta.Value:0} min" : null;

    private static string Money(double? amount) =>
        amount.HasValue ? amount.Value.ToString("C2") : null;

    private static string Yes() =>
        LocalizationService.Instance["NotificationFieldYes"];

    private static string Compose(string address, string city)
    {
        if (string.IsNullOrWhiteSpace(address))
            return city ?? string.Empty;

        return string.IsNullOrWhiteSpace(city)
            ? address
            : $"{address}, {city}";
    }

    #endregion
}
