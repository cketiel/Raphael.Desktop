using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System.Globalization;
using System.Net;
using System.Windows.Media;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>What the correction window needs to know about a reading.</summary>
    public sealed record OdometerTarget(int ScheduleId, string Title, bool IsPullIn, long Current, long? OtherReading, double? ExpectedMiles, long? Previous = null);

    /// <summary>RE-092: an administrator corrects a Pull-out or Pull-in reading.</summary>
    public partial class OdometerCorrectionViewModel : ObservableObject
    {
        private readonly IProductivityWarningService _service = new ProductivityWarningService();
        private readonly OdometerTarget _target;

        public OdometerCorrectionViewModel(OdometerTarget target)
        {
            _target = target;
            Reasons = new[] { "digit", "swapped", "vehicle", "confirmed", "other" }
                .Select(k => new OdometerReason(k, WarningLook.T("odo.Reason." + k))).ToList();
            _reason = Reasons[0];
        }

        public event Action? Saved;

        public string Title => _target.Title;
        public string CurrentText => WarningLook.N(_target.Current);
        public string OtherText => _target.OtherReading is long o ? $"{(_target.IsPullIn ? "Pull-out" : "Pull-in")} {WarningLook.N(o)}" : "—";
        public string PreviousText => WarningLook.N(_target.Previous);
        public bool HasPrevious => _target.Previous is not null;
        public bool HasOther => _target.OtherReading is not null;
        public bool HasExpected => _target.ExpectedMiles is not null;
        private long? Since => Reading - _target.Previous;
        public string ExpectedText => _target.ExpectedMiles is double e ? $"{WarningLook.N(e)} mi" : "—";
        public IReadOnlyList<OdometerReason> Reasons { get; }

        /// <summary>Only from the GPS of a day whose other reading is there.</summary>
        private long? Suggested => _target.OtherReading is long o && _target.ExpectedMiles is double e
            ? (_target.IsPullIn ? o + (long)Math.Round(e) : o - (long)Math.Round(e)) : null;
        public bool HasSuggestion => Suggested is > 0;
        public string SuggestionText => string.Format(WarningLook.T("odo.Suggestion"), WarningLook.N(Suggested));

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CheckText), nameof(CheckInk))] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string _readingText = "";
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOther))] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private OdometerReason _reason;
        [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string _otherReason = "";
        [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private bool _isBusy;
        [ObservableProperty] private string _error = "";

        public bool IsOther => Reason?.Key == "other";

        private long? Reading => long.TryParse((ReadingText ?? "").Replace(",", "").Replace(".", "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v <= 9_999_999 ? v : null;
        private double? Miles => Reading is long r && _target.OtherReading is long o ? (_target.IsPullIn ? r - o : o - r) : null;
        private bool IsFar => Miles is double m && _target.ExpectedMiles is double e && Math.Abs(m - e) > Math.Max(25, e * 0.2);

        public string CheckText => string.IsNullOrWhiteSpace(ReadingText) ? ""
            : Reading is null ? WarningLook.T("odo.Invalid")
            : Reading == _target.Current ? WarningLook.T("odo.Same")
            : Miles is < 0 ? WarningLook.T("odo.CheckBack")
            : Miles is double m && _target.ExpectedMiles is double e
                ? string.Format(WarningLook.T(IsFar ? "odo.CheckFar" : "odo.CheckOk"), WarningLook.N(m), WarningLook.N(e))
            : Since is < 0 ? WarningLook.T("odo.CheckSinceBack")
            : Since is long s ? string.Format(WarningLook.T("odo.CheckSince"), WarningLook.N(s)) : "";

        public Brush CheckInk => WarningLook.Of(Reading is null || Miles is < 0 || Since is < 0 || Reading == _target.Current ? "#B3261E" : IsFar ? "#C2410C" : "#1B7F3B");

        [RelayCommand] private void UseSuggestion() => ReadingText = Suggested?.ToString(CultureInfo.InvariantCulture) ?? "";

        private bool CanSave() => !IsBusy && Reading is long r && r != _target.Current && (!IsOther || !string.IsNullOrWhiteSpace(OtherReason));

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            IsBusy = true;
            Error = "";
            try
            {
                var status = await _service.CorrectAsync(_target.ScheduleId, new OdometerCorrectionRequestDto
                {
                    Reading = Reading!.Value, Expected = _target.Current, Reason = IsOther ? OtherReason.Trim() : Reason.Text
                });
                if (status == HttpStatusCode.NoContent) { Saved?.Invoke(); return; }
                Error = WarningLook.T(status == HttpStatusCode.Conflict ? "odo.Changed" : "odo.SaveError");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Odometer correction: {ex.Message}");
                Error = WarningLook.T("odo.SaveError");
            }
            finally { IsBusy = false; }
        }
    }

    public sealed record OdometerReason(string Key, string Text);

    /// <summary>RE-092: every reading of a vehicle, which ones do not fit, and their corrections.</summary>
    public partial class OdometerHistoryViewModel : ObservableObject
    {
        private readonly IProductivityWarningService _service = new ProductivityWarningService();
        private readonly int _vehicleId;
        private List<OdometerHistoryRow> _all = new();

        public OdometerHistoryViewModel(int vehicleId, string? vehicleName)
        {
            _vehicleId = vehicleId;
            VehicleName = vehicleName ?? "";
        }

        public string VehicleName { get; }
        public string Title => string.Format(WarningLook.T("odo.HistoryTitle"), VehicleName);
        public string HintText => WarningLook.T(TripGuard.IsAdmin ? "odo.HistoryHint" : "odo.HistoryReadOnly");

        /// <summary>Something was corrected: the screen behind loads again.</summary>
        public bool HasCorrected { get; private set; }

        [ObservableProperty] private string _lastText = "—";
        [ObservableProperty] private string _lastWhen = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private bool _onlyOdd;
        [ObservableProperty] private IReadOnlyList<OdometerHistoryRow> _rows = Array.Empty<OdometerHistoryRow>();

        partial void OnOnlyOddChanged(bool value) => Show();

        public async Task LoadAsync()
        {
            IsBusy = true;
            Message = "";
            try
            {
                var history = await _service.HistoryAsync(_vehicleId);
                LastText = history?.LastOdometer is long l ? WarningLook.N(l) : WarningLook.T("odo.NoReading");
                LastWhen = history?.LastOdometerAtUtc is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("g") : "";
                _all = history?.Readings.Select(r => new OdometerHistoryRow(r, history.Readings)).ToList() ?? new();
                Show();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Odometer history: {ex.Message}");
                Message = WarningLook.T("odo.LoadError");
            }
            finally { IsBusy = false; }
        }

        private void Show()
        {
            Rows = OnlyOdd ? _all.Where(r => !r.IsOk).ToList() : _all;
            Message = Rows.Count > 0 ? "" : WarningLook.T(OnlyOdd && _all.Count > 0 ? "odo.EmptyOdd" : "odo.Empty");
        }

        public async Task CorrectedAsync()
        {
            HasCorrected = true;
            await LoadAsync();
        }
    }

    public sealed class OdometerHistoryRow
    {
        private readonly OdometerHistoryRowDto _data;
        private readonly long? _other;

        public OdometerHistoryRow(OdometerHistoryRowDto data, IEnumerable<OdometerHistoryRowDto> all)
        {
            _data = data;
            _other = all.FirstOrDefault(r => r.OperatingDate == data.OperatingDate && r.VehicleRouteId == data.VehicleRouteId && r.Event != data.Event)?.Reading;
        }

        public string Date => $"{_data.OperatingDate:MM/dd/yyyy}";
        public string Event => $"{_data.Event} {(_data.Time is null ? "" : WarningLook.Hour(_data.Time))}".Trim();
        public string Reading => WarningLook.N(_data.Reading);
        public string Since => _data.MilesSincePrevious is long m ? (m > 0 ? "+" : "") + WarningLook.N(m) : "—";
        public string Driver => _data.DriverName ?? "—";
        public string Route => _data.RouteName ?? "—";
        public bool IsOk => _data.Status == "ok";
        public string StatusText => IsOk ? WarningLook.T("odo.Ok") : WarningLook.Text(_data.Status, "Title");
        public Brush StatusInk => WarningLook.Of(IsOk ? "#1B7F3B" : "#B3261E");
        public Brush StatusFill => WarningLook.Of(IsOk ? "#DCF2E3" : "#FBE3E1");
        public bool CanCorrect => TripGuard.IsAdmin;
        public string CorrectText => WarningLook.T("odo.Correct");
        public string ReadingTip => CanCorrect ? WarningLook.T("odo.ClickTip") : WarningLook.T("odo.HistoryReadOnly");
        public System.Windows.Input.Cursor ReadingCursor => CanCorrect ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        public bool HasCorrections => _data.Corrections.Count > 0;
        public string CorrectionsText => string.Join("\n", _data.Corrections.Select(c => string.Format(WarningLook.T("odo.CorrectedLine"),
            WarningLook.N(c.PreviousReading), c.CorrectedBy ?? "—", DateTime.SpecifyKind(c.AtUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"), c.Reason)));

        public OdometerTarget Target => new(_data.ScheduleId, $"{_data.Event} · {_data.OperatingDate:MM/dd/yyyy} · {Driver}", _data.Event == "Pull-in", _data.Reading, _other, null, _data.Reading - _data.MilesSincePrevious);
    }
}
