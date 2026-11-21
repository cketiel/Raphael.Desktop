using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Raphael.Desktop.Converters;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>One line on the live map, and how its vehicle is drawn (RE-080).</summary>
    /// <remarks>
    /// The drawn position (<see cref="Latitude"/>/<see cref="Longitude"/>) and the reported one are
    /// kept apart: the tooltip and the speed state say what the driver sent, not where the glide has
    /// got to. A new fix glides there in <see cref="Glide"/> and turns the short way round. It never
    /// runs ahead of the last fix: drawing the vehicle where it might be by now would show it on
    /// streets it has not driven.
    /// </remarks>
    public partial class LiveMapVehicleViewModel : ObservableObject
    {
        /// <summary>Same thresholds as Map Overview and Schedule.</summary>
        public const double StoppedMph = 2.0;
        public const double SpeedingMph = 65.0;

        /// <summary>A fix older than this is faded: the vehicle may well be somewhere else.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(1500);
        private const double GpsNoiseDegrees = 0.0001;
        private static readonly DirectionToAngleConverter Compass = new();

        private double _fromLat, _fromLng, _fromHeading, _toLat, _toLng, _toHeading;
        private DateTime _glideStartedUtc;

        public LiveMapVehicleViewModel(LiveMapRouteDto route)
        {
            UpdateInfo(route);
            if (route.Position is not null) Place(route.Position);
        }

        public int VehicleRouteId { get; private set; }

        [ObservableProperty] private string _name;
        [ObservableProperty] private string _driverName;
        [ObservableProperty] private string _vehicleName;
        [ObservableProperty] private int _tripsToday;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GroupBrush), nameof(GroupName), nameof(ToolTipText))]
        private string _group;

        public Brush GroupBrush => LiveMapPalette.For(Group);
        public string GroupName => LiveMapPalette.NameOf(Group);

        [ObservableProperty] private double _latitude;
        [ObservableProperty] private double _longitude;
        [ObservableProperty] private double _heading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MovingVisibility), nameof(StoppedVisibility), nameof(ToolTipText))]
        private bool _isMoving;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SpeedingVisibility))]
        private bool _isSpeeding;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MarkerOpacity))]
        private bool _isStale;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MarkerVisibility))]
        private bool _isShown;

        public bool HasPosition { get; private set; }

        /// <summary>The line's own colour for its route on the map (LiveMapPalette.RouteColors).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RouteBrush))]
        private string _routeColor = "#2563EB";

        public Brush RouteBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(RouteColor));

        /// <summary>Minutes the traffic adds to the road ahead (delays layer); 0 when none or unknown.</summary>
        [ObservableProperty] private int _delayMinutes;

        /// <summary>A live fix was applied: the map page glides the vehicle to it.</summary>
        public event EventHandler Moved;

        public bool IsAnimating { get; private set; }

        public double SpeedMph { get; private set; }

        public DateTime? LastFixUtc { get; private set; }

        public Visibility MovingVisibility => IsMoving ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StoppedVisibility => IsMoving ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SpeedingVisibility => IsSpeeding ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MarkerVisibility => IsShown ? Visibility.Visible : Visibility.Collapsed;
        public double MarkerOpacity => IsStale ? 0.45 : 1.0;

        public string ToolTipText
        {
            get
            {
                var l = LocalizationService.Instance;
                var when = LastFixUtc.HasValue ? LastFixUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "—";
                return $"{Name}\n{l["livemap.Driver"]}: {DriverName ?? "—"}\n{l["livemap.Vehicle"]}: {VehicleName ?? "—"}\n" +
                       $"{GroupName} · {string.Format(l["livemap.TripsToday"], TripsToday)}\n" +
                       $"{Math.Round(SpeedMph)} mph · {when}";
            }
        }

        #region Shift (RE-080: Drivers panel)

        public DateTime? ShiftStartUtc { get; private set; }
        public DateTime? ShiftLastUtc { get; private set; }
        public double GpsMiles { get; private set; }
        public int TripsDone { get; private set; }
        public LiveMapRouteMetricsDto Metrics { get; private set; }

        /// <summary>A line is in the Drivers panel once its vehicle has reported today.</summary>
        public bool HasShift => ShiftStartUtc.HasValue;

        /// <summary>Closed day: the RE-062 figure. Open: from the first fix to now.</summary>
        public TimeSpan ShiftLength =>
            Metrics is not null ? TimeSpan.FromMinutes(Metrics.ShiftMinutes)
            : ShiftStartUtc.HasValue ? DateTime.UtcNow - ShiftStartUtc.Value
            : TimeSpan.Zero;

        public string ShiftText => $"{(int)ShiftLength.TotalHours}h{ShiftLength.Minutes:00}";

        public double ShiftMiles => Metrics?.VehicleMiles ?? GpsMiles;

        public string MilesText => $"{Math.Round(ShiftMiles)} mi";

        public string TripsText => $"{TripsDone}/{TripsToday}";

        public string ShiftStartText => ShiftStartUtc.HasValue
            ? string.Format(LocalizationService.Instance["livemap.ShiftSince"], ShiftStartUtc.Value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
            : string.Empty;

        /// <summary>
        /// The bar under each driver. Closed day: loaded / waiting / the rest (deadhead and other),
        /// as shares of the shift. Open day: trips done out of the day's trips.
        /// </summary>
        public double LoadedShare => Metrics is { ShiftMinutes: > 0 } m ? (double)m.LoadedMinutes / m.ShiftMinutes
            : TripsToday == 0 ? 0 : (double)TripsDone / TripsToday;

        public double WaitShare => Metrics is { ShiftMinutes: > 0 } m ? (double)m.WaitMinutes / m.ShiftMinutes : 0;

        public bool IsShiftClosed => Metrics is not null;

        public void ApplyShift(LiveMapShiftDto shift)
        {
            shift ??= new LiveMapShiftDto();
            ShiftStartUtc = shift.StartUtc;
            ShiftLastUtc = shift.LastUtc;
            GpsMiles = shift.GpsMiles;
            TripsDone = shift.TripsDone;
            Metrics = shift.Metrics;
            RefreshShift();
        }

        public void RefreshShift()
        {
            foreach (var name in new[] { nameof(HasShift), nameof(ShiftText), nameof(MilesText), nameof(TripsText),
                         nameof(ShiftStartText), nameof(LoadedShare), nameof(WaitShare), nameof(IsShiftClosed) })
                OnPropertyChanged(name);
        }

        /// <summary>Miles between two live fixes, so the panel grows between snapshots. Same 5-mile glitch rule as the API.</summary>
        private void AddTrack(double lat, double lng, DateTime atUtc)
        {
            if (!HasPosition) return;

            const double R = 3958.8, rad = Math.PI / 180;
            var dLat = (lat - _lastLat) * rad;
            var dLng = (lng - _lastLng) * rad;
            var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(_lastLat * rad) * Math.Cos(lat * rad) * Math.Pow(Math.Sin(dLng / 2), 2);
            var miles = 2 * R * Math.Asin(Math.Sqrt(a));

            if (miles < 5) GpsMiles += miles;
            ShiftStartUtc ??= atUtc;
            ShiftLastUtc = atUtc;
        }

        private double _lastLat, _lastLng;

        #endregion

        public void UpdateInfo(LiveMapRouteDto route)
        {
            VehicleRouteId = route.VehicleRouteId;
            Name = route.Name;
            DriverName = route.DriverName;
            VehicleName = route.VehicleName;
            TripsToday = route.TripsToday;
            Group = route.Group;
            ApplyShift(route.Shift);
            OnPropertyChanged(nameof(ToolTipText));
        }

        /// <summary>First position: placed, not flown in from nowhere.</summary>
        public void Place(LiveMapPositionDto fix)
        {
            _lastLat = fix.Latitude; _lastLng = fix.Longitude;
            Latitude = fix.Latitude;
            Longitude = fix.Longitude;
            ApplyReport(fix.Speed, fix.Direction, fix.AtUtc);
            Heading = _toHeading;
            HasPosition = true;
        }

        /// <summary>A live fix: glide to it.</summary>
        public void MoveTo(VehiclePositionMessage fix)
        {
            if (!HasPosition)
            {
                Place(new LiveMapPositionDto { Latitude = fix.Latitude, Longitude = fix.Longitude, Speed = fix.Speed, Direction = fix.Direction, AtUtc = fix.AtUtc });
                return;
            }

            ApplyReport(fix.Speed, fix.Direction, fix.AtUtc);

            AddTrack(fix.Latitude, fix.Longitude, fix.AtUtc);
            _lastLat = fix.Latitude; _lastLng = fix.Longitude;
            RefreshShift();

            // Parked or drifting on the spot: turning only, so a standing van does not twitch.
            var still = Math.Abs(fix.Latitude - Latitude) < GpsNoiseDegrees && Math.Abs(fix.Longitude - Longitude) < GpsNoiseDegrees;

            _fromLat = Latitude; _fromLng = Longitude; _fromHeading = Heading;
            _toLat = still ? Latitude : fix.Latitude;
            _toLng = still ? Longitude : fix.Longitude;
            _toHeading = ShortestTurnTarget(Heading, _toHeading);
            // RE-080: the glide is drawn by the map page now; here the vehicle is where it was reported.
            Latitude = _toLat;
            Longitude = _toLng;
            Heading = _toHeading;
            IsAnimating = false;
            Moved?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One frame of the glide. False once it has arrived.</summary>
        public bool Step(DateTime nowUtc)
        {
            if (!IsAnimating) return false;

            var t = Math.Clamp((nowUtc - _glideStartedUtc).TotalMilliseconds / Glide.TotalMilliseconds, 0, 1);
            var e = 1 - Math.Pow(1 - t, 3); // ease out: arrives softly

            Latitude = _fromLat + (_toLat - _fromLat) * e;
            Longitude = _fromLng + (_toLng - _fromLng) * e;
            Heading = _fromHeading + (_toHeading - _fromHeading) * e;

            if (t >= 1) IsAnimating = false;
            return IsAnimating;
        }

        public void RefreshStale() =>
            IsStale = LastFixUtc.HasValue && DateTime.UtcNow - LastFixUtc.Value > StaleAfter;

        public void RefreshText()
        {
            OnPropertyChanged(nameof(GroupName));
            OnPropertyChanged(nameof(ToolTipText));
        }

        private void ApplyReport(double metresPerSecond, string direction, DateTime atUtc)
        {
            SpeedMph = SpeedUnits.ToMph(metresPerSecond);
            IsMoving = SpeedMph > StoppedMph;
            IsSpeeding = SpeedMph >= SpeedingMph;
            LastFixUtc = atUtc;
            RefreshStale();

            // ⚠️ "N/A" means "not moving", not north: a stopped vehicle keeps the heading it drove.
            _toHeading = IsUnknown(direction)
                ? Heading
                : Compass.Convert(direction, typeof(double), null, CultureInfo.InvariantCulture) is double angle ? angle : Heading;

            OnPropertyChanged(nameof(ToolTipText));
        }

        private static bool IsUnknown(string direction) =>
            string.IsNullOrWhiteSpace(direction) || string.Equals(direction.Trim(), "N/A", StringComparison.OrdinalIgnoreCase);

        private static double ShortestTurnTarget(double current, double target) =>
            current + (((target - current) % 360 + 540) % 360 - 180);
    }
}
