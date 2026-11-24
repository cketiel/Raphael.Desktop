using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>
    /// The route card (RE-080): everything useful about one line, opened by clicking its vehicle.
    /// </summary>
    /// <remarks>
    /// The detail is read once on open (<c>GET api/live-map/routes/{id}</c>); speed, heading and the
    /// age of the fix come from the live vehicle, so they keep moving while the card is open.
    /// ⚠️ About the line, never a patient: the next stop shows its kind and times only.
    /// </remarks>
    public partial class LiveMapRouteCardViewModel : ObservableObject
    {
        private readonly ILiveMapService _service;

        public LiveMapRouteCardViewModel(ILiveMapService service)
        {
            _service = service;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOpenVisibility))]
        private bool _isOpen;

        public Visibility IsOpenVisibility => IsOpen ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _errorText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasDetail), nameof(Title), nameof(VehicleLine), nameof(CapacityText),
            nameof(Kind), nameof(DriverName), nameof(DriverLine), nameof(TripsLine), nameof(ChargesLine), nameof(NextStopLine),
            nameof(VehicleComplianceLine), nameof(DriverComplianceLine), nameof(HasCurrentTrip), nameof(GroupName),
            nameof(GroupBrush), nameof(CurrentTripLine), nameof(HasPhone),
            nameof(HasProgress), nameof(ProgressVisibility), nameof(EtaMinutesText), nameof(EtaMilesText), nameof(EtaPhaseText),
            nameof(EtaClockText), nameof(IsLate), nameof(BarRatio), nameof(BarPickupText), nameof(BarDropoffText), nameof(BarCaption),
            nameof(IsDropoffPhase))]
        private LiveMapRouteDetailDto _detail;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NowLine))]
        private LiveMapVehicleViewModel _vehicle;

        public bool HasDetail => Detail is not null;

        private static LocalizationService L => LocalizationService.Instance;

        public string Title => Detail?.Name ?? Vehicle?.Name;

        public string GroupName => Detail is null ? null : LiveMapPalette.NameOf(Detail.Group);

        public System.Windows.Media.Brush GroupBrush => LiveMapPalette.For(Detail?.Group);

        /// <summary><c>STR</c>, <c>WCH</c> or <c>AMB</c>: picks the illustration.</summary>
        public string Kind => Detail?.Kind ?? "AMB";

        public string VehicleLine => Detail is null ? null :
            string.Join(" · ", new[]
            {
                Detail.VehicleName,
                string.Join(" ", new[] { Detail.Make, Detail.Model, Detail.Year?.ToString(CultureInfo.InvariantCulture) }.Where(x => !string.IsNullOrWhiteSpace(x))),
                Detail.Plate
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        public string CapacityText => Detail is null ? null :
            string.Join(" · ", Detail.Capacities.Select(c => $"{c.SpaceType} {c.Quantity}"));

        /// <summary>The driver's name for the card's header ("—" when the line has none).</summary>
        public string DriverName => Detail is null ? null : string.IsNullOrWhiteSpace(Detail.DriverName) ? "—" : Detail.DriverName;

        public string DriverLine => Detail is null ? null :
            string.IsNullOrWhiteSpace(Detail.DriverPhone) ? Detail.DriverName : $"{Detail.DriverName} · {FormatPhone(Detail.DriverPhone)}";

        public bool HasPhone => !string.IsNullOrWhiteSpace(Detail?.DriverPhone);

        public string NowLine
        {
            get
            {
                if (Vehicle is null || !Vehicle.HasPosition) return L["livemap.CardNoPosition"];
                var age = Vehicle.LastFixUtc.HasValue ? DateTime.UtcNow - Vehicle.LastFixUtc.Value : TimeSpan.Zero;
                var ago = age.TotalSeconds < 60 ? $"{(int)age.TotalSeconds} s" : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min" : $"{(int)age.TotalHours} h";
                var motion = Vehicle.IsMoving ? $"{Math.Round(Vehicle.SpeedMph)} mph" : L["livemap.CardStopped"];
                return string.Format(L["livemap.CardNow"], motion, ago);
            }
        }

        public string TripsLine => Detail is null ? null :
            string.Format(L["livemap.CardTrips"], Detail.TripsDone, Detail.TripsToday, Detail.TripsCanceled);

        public string ChargesLine => Detail is null ? null :
            "$" + Detail.ChargesToday.ToString("N2", CultureInfo.InvariantCulture);

        public string NextStopLine
        {
            get
            {
                var n = Detail?.NextStop;
                if (n is null) return L["livemap.CardNoNextStop"];
                // The stop's own name, patient included, as Dispatch lists it; an older server sends only the kind.
                var name = n.Kind switch
                {
                    "PullOut" => "Pull-out",
                    "PullIn" => "Pull-in",
                    _ => !string.IsNullOrWhiteSpace(n.Name) ? n.Name : n.Kind == "Dropoff" ? L["livemap.CardDropoff"] : L["livemap.CardPickup"]
                };
                return n.Promised is null
                    ? string.Format(L["livemap.CardNextStopEta"], name, Clock(n.Eta))
                    : string.Format(L["livemap.CardNextStop"], name, Clock(n.Promised), Clock(n.Eta));
            }
        }

        public bool HasCurrentTrip => Detail?.CurrentTrip is not null;

        public string CurrentTripLine => Detail?.CurrentTrip is null ? null :
            string.Format(L["livemap.CardCurrentTrip"], Detail.CurrentTrip.TripNumber ?? Detail.CurrentTrip.TripId.ToString(CultureInfo.InvariantCulture), Detail.CurrentTrip.Status);

        #region Booking's pieces: Live ETA tile and trip bar

        private LiveMapTripProgressDto P => Detail?.Progress;

        public bool HasProgress => P is not null;

        public Visibility ProgressVisibility => HasProgress ? Visibility.Visible : Visibility.Collapsed;

        public bool IsDropoffPhase => P?.Phase == "Dropoff";

        private TimeSpan? TargetEta => IsDropoffPhase ? P?.DropoffEta : P?.PickupEta;

        private TimeSpan? TargetPromised => IsDropoffPhase ? P?.PromisedAppointment : P?.PromisedPickup;

        /// <summary>Minutes to the stop of this phase, from the live ETA (business wall clock).</summary>
        public string EtaMinutesText
        {
            get
            {
                if (TargetEta is not TimeSpan eta) return "—";
                // The ETAs are business wall clock: count in that clock (sent by the server), advanced
                // by the time since it was measured, never in this PC's time zone.
                var businessNow = P.BusinessNow + (DateTime.UtcNow - P.AtUtc);
                var minutes = (int)Math.Round((eta - businessNow).TotalMinutes);
                return minutes <= 1 ? L["livemap.EtaArriving"] : $"{minutes} min";
            }
        }

        public string EtaMilesText => P?.RemainingMiles is double m ? $"{m:0.0} mi" : string.Empty;

        public string EtaPhaseText => IsDropoffPhase ? L["livemap.EtaToDropoff"] : L["livemap.EtaToPickup"];

        public string EtaClockText => TargetEta is TimeSpan eta ? string.Format(L["livemap.EtaAt"], Clock(eta)) : string.Empty;

        /// <summary>Running late: the live ETA is past the promised time of this phase.</summary>
        public bool IsLate => TargetEta is TimeSpan eta && TargetPromised is TimeSpan promised && eta > promised;

        /// <summary>
        /// Where the van sits on the bar, 0..1. Heading to the pickup it waits at the start; with the
        /// patient on board it advances with the miles already driven of the trip's own distance.
        /// </summary>
        public double BarRatio
        {
            get
            {
                if (!IsDropoffPhase) return 0;
                if (P?.RemainingMiles is not double left || P.TripMiles is not double total || total <= 0) return 0.5;
                return Math.Clamp(1 - left / total, 0, 1);
            }
        }

        public string BarPickupText => P is null ? null : $"{L["livemap.CardPickup"]} {Clock(P.PromisedPickup)}";
        public string BarDropoffText => P is null ? null : $"{L["livemap.CardDropoff"]} {Clock(P.PromisedAppointment)}";
        public string BarCaption => IsLate ? L["livemap.EtaLate"] : EtaPhaseText;

        #endregion

        public string VehicleComplianceLine => ComplianceLine(Detail?.VehicleCompliance);
        public string DriverComplianceLine => ComplianceLine(Detail?.DriverCompliance);

        #region Labels

        public string LabelDriver => L["livemap.Driver"];
        public string LabelNow => L["livemap.CardLabelNow"];
        public string LabelTrips => L["livemap.CardLabelTrips"];
        public string LabelNext => L["livemap.CardLabelNext"];
        public string LabelShift => L["livemap.CardLabelShift"];
        public string LabelCharges => L["livemap.CardLabelCharges"];
        public string LabelVehicleDocs => L["livemap.CardLabelVehicleDocs"];
        public string LabelDriverDocs => L["livemap.CardLabelDriverDocs"];
        public string LinkRoute => L["livemap.CardLinkRoute"];
        public string LinkTrip => L["livemap.CardLinkTrip"];
        public string LinkVehicle => L["livemap.CardLinkVehicle"];
        public string LinkDriver => L["livemap.CardLinkDriver"];
        public string LinkCopyPhone => L["livemap.CardCopyPhone"];
        public string CloseText => L["livemap.CardClose"];

        #endregion

        /// <summary>Opens the card on a line: shows what is known at once, then the detail.</summary>
        public async Task OpenAsync(LiveMapVehicleViewModel vehicle)
        {
            Vehicle = vehicle;
            Detail = null;
            ErrorText = null;
            IsOpen = true;
            IsLoading = true;

            try
            {
                var detail = await _service.GetRouteDetailAsync(vehicle.VehicleRouteId);

                // The user may have clicked another vehicle while this one was loading.
                if (!ReferenceEquals(Vehicle, vehicle)) return;

                Detail = detail;
                if (detail is null) ErrorText = L["livemap.CardNotFound"];
            }
            catch (Exception ex)
            {
                ErrorText = L["livemap.CardError"];
                FileLogger.Log($"Live map: could not load route {vehicle.VehicleRouteId}. {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Called every second by the map, so "now" and the minutes to go stay current.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            OnPropertyChanged(nameof(NowLine));
            OnPropertyChanged(nameof(EtaMinutesText));
        }

        /// <summary>
        /// A new fix of the open card's line: its progress (miles, ETAs) is read again, at most every
        /// 20 seconds. Only while the card is open, and only for that line.
        /// </summary>
        public async Task RefreshProgressAsync()
        {
            if (!IsOpen || Vehicle is null || DateTime.UtcNow - _progressReadAt < TimeSpan.FromSeconds(20)) return;
            _progressReadAt = DateTime.UtcNow;
            try
            {
                var fresh = await _service.GetRouteDetailAsync(Vehicle.VehicleRouteId);
                if (fresh is not null && IsOpen && Vehicle?.VehicleRouteId == fresh.VehicleRouteId) Detail = fresh;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: could not refresh the card. {ex.Message}");
            }
        }

        private DateTime _progressReadAt = DateTime.MinValue;

        [RelayCommand]
        private void Close() => IsOpen = false;

        [RelayCommand]
        private void OpenRoute()
        {
            if (Detail is null) return;
            if (Detail.CurrentTrip is not null)
                AppNavigation.ShowTripInSchedule(Detail.OperatingDate, Detail.VehicleRouteId, Detail.CurrentTrip.TripId);
            else
                AppNavigation.ShowRouteInSchedule(Detail.OperatingDate, Detail.VehicleRouteId);
        }

        [RelayCommand]
        private void OpenTrip()
        {
            if (Detail?.CurrentTrip is { } trip)
                AppNavigation.ShowTripInHome(trip.TripNumber ?? trip.TripId.ToString(CultureInfo.InvariantCulture));
        }

        [RelayCommand]
        private void OpenVehicleDocs()
        {
            if (Detail is not null) ComplianceUi.Open("VEHICLE", Detail.VehicleId);
        }

        [RelayCommand]
        private void OpenDriverDocs()
        {
            if (Detail?.DriverId is int id) ComplianceUi.Open("USER", id);
        }

        [RelayCommand]
        private void CopyPhone()
        {
            if (!HasPhone) return;
            try { Clipboard.SetText(Detail.DriverPhone); } catch { /* the clipboard can be busy; nothing to lose */ }
        }

        private static string Clock(TimeSpan? t) =>
            t.HasValue ? DateTime.Today.Add(t.Value).ToString("t", CultureInfo.CurrentCulture) : "—";

        private static string FormatPhone(string phone) =>
            phone.Length == 10 && phone.All(char.IsDigit) ? $"({phone[..3]}) {phone[3..6]}-{phone[6..]}" : phone;

        private static string ComplianceLine(LiveMapComplianceDto c)
        {
            if (c is null) return null;
            if (c.Expired > 0) return string.Format(L["livemap.CardDocsExpired"], c.Expired);
            if (c.NextExpiresOn is null) return L["livemap.CardDocsNone"];

            var days = (c.NextExpiresOn.Value.Date - DateTime.Today).Days;
            return days <= 30
                ? string.Format(L["livemap.CardDocsSoon"], c.NextDocument, days)
                : L["livemap.CardDocsOk"];
        }
    }
}
