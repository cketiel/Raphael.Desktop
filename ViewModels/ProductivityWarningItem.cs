using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System.Globalization;
using System.Windows.Media;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>The words and colours of the warnings (RE-092), shared by the report, the legend and the odometer.</summary>
    public static class WarningLook
    {
        private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

        public static string T(string key) => LocalizationService.Instance[key];

        /// <summary>A code this version does not know still shows, as its own name.</summary>
        public static string Text(string code, string part) =>
            LocalizationService.Instance.TryGetValue($"warn.{code}.{part}", out var s) ? s : part == "Title" ? code : "";

        public static string When(string code, double? limit) => string.Format(Text(code, "When"), N(limit));

        public static string N(double? v, int decimals = 2) => v is double d ? Math.Round(d, decimals).ToString("#,##0.##", Us) : "—";
        public static string N(long? v) => v is long d ? d.ToString("N0", Us) : "—";

        public static string Icon(string level) => level switch { "error" => "AlertOctagonOutline", "info" => "InformationOutline", _ => "AlertOutline" };
        public static Brush Ink(string level) => level switch { "error" => Of("#B3261E"), "info" => Of("#0B67A3"), _ => Of("#C2410C") };
        public static Brush Fill(string level) => level switch { "error" => Of("#FBE3E1"), "info" => Of("#DFF0FB"), _ => Of("#FDEBD0") };
        public static Brush Line(string level) => level switch { "error" => Of("#F3B4AF"), "info" => Of("#A9D5F0"), _ => Of("#FBD38D") };
        public static int Rank(string level) => level switch { "error" => 0, "review" => 1, _ => 2 };

        public static SolidColorBrush Of(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static string Hour(TimeSpan? t) => t is TimeSpan v ? DateTime.Today.Add(v).ToString("HH:mm") : "—";
        public static string Hour(DateTime? utc) => utc is DateTime u
            ? DateTime.SpecifyKind(u, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm") : "—";
    }

    /// <summary>One warning of a driver-day: its line, and its cases once opened.</summary>
    public partial class ProductivityWarningItem : ObservableObject
    {
        private readonly Func<Task<ProductivityDayWarningsDto?>> _loadDetail;
        private bool _asked;

        public ProductivityWarningItem(ProductivityWarningDto data, int? vehicleId, Func<Task<ProductivityDayWarningsDto?>> loadDetail)
        {
            Data = data;
            VehicleId = vehicleId;
            _loadDetail = loadDetail;
        }

        public ProductivityWarningDto Data { get; }
        public int? VehicleId { get; }
        public string Code => Data.Code;
        public string Icon => WarningLook.Icon(Data.Level);
        public Brush Ink => WarningLook.Ink(Data.Level);
        public Brush Fill => WarningLook.Fill(Data.Level);
        public Brush Line => WarningLook.Line(Data.Level);

        public string Title => WarningLook.Text(Data.Code, "Title");

        /// <summary>How much or how many times: the figure the old report said, on every warning.</summary>
        public string Badge => Data.Code switch
        {
            "far_from_stop" => Data.Count == 1 ? WarningLook.T("warn.CaseOne") : string.Format(WarningLook.T("warn.CaseMany"), Data.Count),
            "outside_allowed_hours" => string.Format(WarningLook.T("warn.Badge.min"), ProductivityFormat.Hours((int)(Data.Value ?? 0))),
            "off_route_miles" or "pull_out_impossible" or "odometer_backwards" => Data.Value is null ? "" : $"{WarningLook.N(Data.Value)} mi",
            "odometer_vs_gps" => $"{WarningLook.N(Data.Value)} %",
            "pull_in_impossible" => string.Format(WarningLook.T("warn.Badge.x"), WarningLook.N(Data.Value)),
            "long_shift" => $"{WarningLook.N(Data.Value)} h",
            "zero_miles_with_trips" => "0 mi",
            _ => ""
        };
        public bool HasBadge => Badge.Length > 0;
        public string Why => WarningLook.Text(Data.Code, "Why");
        public string When => WarningLook.When(Data.Code, Data.Limit);
        public string WhenLabel => WarningLook.T("warn.WhenLabel");
        public string LegendText => WarningLook.T("warn.Legend");
        public string CorrectText => WarningLook.T("odo.Correct");
        public string HistoryText => WarningLook.T("odo.History");
        public string ToggleText => WarningLook.T(IsOpen ? "warn.Hide" : "warn.Detail");

        public bool CanCorrect => Data.CorrectScheduleId is not null && TripGuard.IsAdmin;
        public bool CanSeeHistory => VehicleId is not null
            && Data.Code is "pull_in_impossible" or "pull_out_impossible" or "odometer_backwards" or "odometer_vs_gps" or "zero_miles_with_trips" or "wrong_vehicle" or "off_route_miles";

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToggleText))] private bool _isOpen;
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private string _totalText = "";
        [ObservableProperty] private ShiftCalculationDto? _shift;
        public string ShiftTitle => WarningLook.T("shift.Title");
        public string DoLabel => WarningLook.T("warn.DoLabel");
        public string DoText => WarningLook.Text(Data.Code, "Do");
        [ObservableProperty] private IReadOnlyList<WarningCaseLine> _cases = Array.Empty<WarningCaseLine>();

        /// <summary>The single case behind an odometer warning, for the correction window.</summary>
        public ProductivityWarningCaseDto? FirstCase { get; private set; }

        private static string TotalOf(ProductivityWarningDto w)
        {
            if (w.Cases.Count == 0) return "";
            return w.Code switch
            {
                "far_from_stop" => string.Format(WarningLook.T("warn.MarksTotal"), w.Cases.Count, w.Cases.Select(c => c.ScheduleId).Distinct().Count(), WarningLook.N(w.Cases.Max(c => c.Value))),
                "outside_allowed_hours" => string.Format(WarningLook.T("warn.UnpaidTotal"), ProductivityFormat.Hours(w.Cases.Sum(c => c.Minutes ?? 0))),
                _ => w.Cases.Count == 1 ? "" : string.Format(WarningLook.T("warn.CasesTotal"), w.Cases.Count)
            };
        }

        [RelayCommand]
        private async Task ToggleAsync()
        {
            IsOpen = !IsOpen;
            if (IsOpen) await EnsureAsync();
        }

        public async Task EnsureAsync()
        {
            if (_asked) return;
            _asked = true;
            Message = WarningLook.T("warn.Loading");
            try
            {
                var day = await _loadDetail();
                var found = day?.Warnings.FirstOrDefault(w => w.Code == Code);
                FirstCase = found?.Cases.FirstOrDefault();
                Cases = found is null ? Array.Empty<WarningCaseLine>() : WarningCaseLine.Of(found);
                TotalText = found is null ? "" : TotalOf(found);
                Shift = Code == "outside_allowed_hours" ? day?.Shift : null;
                Message = found is null ? WarningLook.T("warn.DetailError") : "";
                if (found is null) _asked = false;
            }
            catch (Exception ex)
            {
                _asked = false;   // opening it again tries again
                FileLogger.Log($"Productivity warnings detail: {ex.Message}");
                Message = WarningLook.T("warn.DetailError");
            }
        }
    }

    /// <summary>One case, in words, and its drawing when it happened somewhere.</summary>
    public partial class WarningCaseLine : ObservableObject
    {
        private WarningCaseLine(string text, ProductivityWarningCaseDto? data = null) { Text = text; Data = data; }

        public string Text { get; }

        /// <summary>Its place in the list, when there is more than one.</summary>
        public string Number { get; private set; } = "";
        public bool HasNumber => Number.Length > 0;
        public ProductivityWarningCaseDto? Data { get; }

        public static IReadOnlyList<WarningCaseLine> Of(ProductivityWarningDto w)
        {
            string F(string key, params object[] args) => string.Format(WarningLook.T("warn." + key), args);
            static string N(double? v) => WarningLook.N(v);
            static string L(long? v) => WarningLook.N(v);
            static string Stop(string? s) => WarningLook.T(s == "Pickup" ? "warn.StopPickup" : "warn.StopDropoff");
            static long? Less(long? a, long? b) => a - b;

            if (w.Code == "no_return_leg" && w.Cases.Count == 0) return new[] { new WarningCaseLine(F("CaseNoPending")) };

            var lines = w.Cases.Select(c => w.Code switch
            {
                "far_from_stop" => new WarningCaseLine(F("CaseMark", WarningLook.Hour(c.Time), Stop(c.StopType), c.TripNumber ?? $"#{c.TripId}", c.Action ?? "", N(c.Value), N(w.Limit)), c),
                "outside_allowed_hours" => new WarningCaseLine(F(c.Action == "Start" ? "CaseStart" : "CaseEnd", WarningLook.Hour(c.RealUtc), WarningLook.Hour(c.AllowedUtc), c.Minutes ?? 0)),
                "off_route_miles" => new WarningCaseLine(c.PreviousReading is null ? F("CaseOffShort", N(c.Value)) : F("CaseOff", L(c.PreviousReading), L(c.OdometerOut), N(c.Value))),
                "odometer_vs_gps" => new WarningCaseLine(F("CaseGps", N(c.VehicleMiles), N(c.GpsMiles), N(c.Value))),
                "pull_in_impossible" => new WarningCaseLine(F("CaseIn", L(c.OdometerOut), L(c.OdometerIn), N(c.VehicleMiles), N(c.GpsMiles is > 1 ? c.GpsMiles : c.PlannedMiles), N(c.Value))),
                "pull_out_impossible" => new WarningCaseLine(F("CaseOut", L(c.OdometerOut), L(c.PreviousReading), N(c.Value))),
                "odometer_backwards" => new WarningCaseLine(c.OdometerIn < c.OdometerOut
                    ? F("CaseBackDay", L(c.OdometerOut), L(c.OdometerIn), L(Less(c.OdometerOut, c.OdometerIn)))
                    : F("CaseBack", L(c.OdometerOut), L(c.PreviousReading), L(Less(c.PreviousReading, c.OdometerOut)))),
                "zero_miles_with_trips" => new WarningCaseLine(F("CaseZero", L(c.OdometerIn), c.TripsCompleted ?? 0, N(c.GpsMiles))),
                "wrong_vehicle" => new WarningCaseLine(F("CaseRoute", c.RouteName ?? "—", L(c.OdometerOut), L(c.OdometerIn))),
                "long_shift" => new WarningCaseLine(F("CaseLong", ProductivityFormat.Hours(c.Minutes ?? 0))),
                "incomplete_gps" => new WarningCaseLine(F("CaseGpsGap")),
                "no_return_leg" => new WarningCaseLine(F("CasePending", WarningLook.Hour(c.Time), Stop(c.StopType), c.TripNumber ?? $"#{c.TripId}")),
                _ => new WarningCaseLine(N(c.Value))
            }).ToList();
            for (var i = 0; i < lines.Count; i++) lines[i].Number = lines.Count > 1 ? $"{i + 1}" : "";
            return lines;
        }

        // ---- The mark, drawn: the stop, where the GPS had the vehicle, and the way between ----
        public bool HasPlace => Data?.StopLatitude is not null;
        public bool HasFix => Data?.MarkLatitude is not null;
        public string DistanceText => $"{WarningLook.N(Data?.Value)} mi";
        public string StopText => Data?.StopType is null ? "" : WarningLook.T(Data.StopType == "Pickup" ? "warn.StopPickup" : "warn.StopDropoff");
        public string MarkText => $"{Data?.Action} {WarningLook.Hour(Data?.Time)}";
        public string PictureNote => WarningLook.T(HasFix ? "warn.PictureNote" : "warn.NoFix");

        private const double Cx = 170, Cy = 60, Reach = 46;

        /// <summary>Which way the vehicle was, north up; to the right when the GPS had nothing.</summary>
        private (double X, double Y) Direction
        {
            get
            {
                if (!HasFix || !HasPlace) return (1, 0);
                var dx = (Data!.MarkLongitude!.Value - Data.StopLongitude!.Value) * Math.Cos(Data.StopLatitude!.Value * Math.PI / 180);
                var dy = Data.StopLatitude.Value - Data.MarkLatitude!.Value;
                var n = Math.Sqrt(dx * dx + dy * dy);
                return n < 1e-9 ? (1, 0) : (dx / n, dy / n);
            }
        }

        public double MarkX => Cx + Direction.X * Reach * 2.2;
        public double MarkY => Cy + Direction.Y * Reach;
        public double MarkDotX => MarkX - 6;
        public double MarkDotY => MarkY - 6;
    }
}
