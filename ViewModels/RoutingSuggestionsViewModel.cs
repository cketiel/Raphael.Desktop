#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>One stop of the plan, for the list under the suggestion.</summary>
    public sealed record SuggestionStopRow(string Kind, string Name, string Eta, string Change, string Detail, bool IsNew, bool IsDone, bool IsWorse);

    /// <summary>One line of what would happen: good, a warning, or a problem.</summary>
    public sealed record SuggestionFact(string Icon, Brush Color, string Text);

    /// <summary>A suggestion and its words.</summary>
    public partial class RoutingSuggestionRow : ObservableObject
    {
        private static readonly Brush Good = Frozen("#2E7D32"), Warn = Frozen("#B26A00"), Bad = Frozen("#C62828"), Quiet = Frozen("#5B6B7C");
        private static readonly Brush GoodBack = Frozen("#E8F5E9"), WarnBack = Frozen("#FFF3E0"), BadBack = Frozen("#FFEBEE"), QuietBack = Frozen("#ECEFF1");
        private static string T(string key) => LocalizationService.Instance[key];

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private readonly bool _isLineList;

        public RoutingSuggestionRow(RoutingSuggestionDto dto, bool isLineList)
        {
            Dto = dto;
            _isLineList = isLineList;
        }

        public RoutingSuggestionDto Dto { get; private set; }

        [ObservableProperty] private bool _isWorking;

        /// <summary>The same suggestion, now with Google's times.</summary>
        public void Update(RoutingSuggestionDto dto)
        {
            dto.Position = Dto.Position;
            dto.AboveBecause = Dto.AboveBecause;
            dto.AboveByMiles = Dto.AboveByMiles;
            dto.OtherLinesThatFit = Dto.OtherLinesThatFit;
            Dto = dto;
            OnPropertyChanged(string.Empty);
        }

        public string Position => Dto.Position.ToString();
        public string Title => _isLineList ? Dto.Line : $"{FeasibilityText.Hour(Dto.FromTime)}  {Dto.Patient}";
        public string Subtitle => _isLineList
            ? string.Join(" · ", new[] { Dto.Driver, Dto.VehicleKind ?? T("vehcap.NotConfigured"), Dto.IsPlanned ? T("assist.Planned") : null, Dto.OpensLine ? T("assist.OpensLine") : null }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Join(" · ", new[] { Dto.SpaceType, Dto.TripType == "Return" ? T("assist.Return") : Dto.ToTime is { } to ? string.Format(T("assist.Appointment"), FeasibilityText.Hour(to)) : null,
                Dto.OtherLinesThatFit is int n ? string.Format(T(n == 0 ? "assist.NoOtherLine" : "assist.OtherLines"), n) : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
        public bool IsUrgent => !_isLineList && Dto.OtherLinesThatFit == 0 && Dto.Group <= 2;
        public bool IsPlanned => _isLineList && Dto.IsPlanned;

        public string Arrives => FeasibilityText.Hour(Dto.PickupEta);
        public string ArrivesDetail => Dto.PickupOffsetMinutes == 0 ? T("assist.OnTheHour")
            : string.Format(T(Dto.PickupOffsetMinutes < 0 ? "assist.Before" : "assist.After"), Math.Abs(Dto.PickupOffsetMinutes))
              + (Dto.WaitMinutes > 0 ? string.Format(T("assist.PlusWait"), Dto.WaitMinutes) : "");
        public string Miles => $"{(Dto.AddedMiles >= 0 ? "+" : "")}{Dto.AddedMiles:0.0} mi";
        public string MilesDetail => $"{(Dto.AddedPaidMinutes >= 0 ? "+" : "−")}{Math.Abs(Dto.AddedPaidMinutes)} min";
        public string Precision => T(Dto.Confirmed ? "assist.Confirmed" : "assist.Estimated");
        public string PrecisionIcon => Dto.Confirmed ? "CheckDecagramOutline" : "ApproximatelyEqual";
        public Brush PrecisionColor => Dto.Confirmed ? Good : Quiet;

        public string GroupText => Dto.Group switch { 1 => T("assist.Fits"), 2 => T("assist.WithMargin"), 3 => T("assist.WithProblem"), _ => T("assist.Cannot") };
        public Brush GroupColor => Dto.Group switch { 1 => Good, 2 => Warn, 3 => Bad, _ => Quiet };
        public Brush GroupBack => Dto.Group switch { 1 => GoodBack, 2 => WarnBack, 3 => BadBack, _ => QuietBack };

        /// <summary>Why it is above the next one.</summary>
        public string? Why => Dto.AboveBecause switch
        {
            "GROUP" => T("assist.WhyGroup" + Dto.Group),
            "PLAN" => T("assist.WhyPlan"),
            "HOUR" => T("assist.WhyHour"),
            "LATE" => T("assist.WhyLate"),
            "COST" => string.Format(T("assist.WhyCost"), Signed(Dto.AboveByMiles ?? 0), SignedMinutes(Dto.AboveByMinutes ?? 0)),
            "EARLIER" => T("assist.WhyEarlier"),
            _ => null,
        };
        private static string Signed(double v) => v >= 0 ? string.Format(T("assist.MilesLess"), v.ToString("0.0")) : string.Format(T("assist.MilesMore"), (-v).ToString("0.0"));
        private static string SignedMinutes(int v) => v >= 0 ? string.Format(T("assist.MinutesLess"), v) : string.Format(T("assist.MinutesMore"), -v);
        public bool HasWhy => Why != null;

        public string Where => Dto.NextStop is { Length: > 0 } && Dto.NextStop != "Pull-in"
            ? string.Format(T("assist.Between"), Dto.PreviousStop, Dto.NextStop) : string.Format(T("assist.AfterStop"), Dto.PreviousStop);

        public List<SuggestionFact> Facts
        {
            get
            {
                var d = Dto;
                var facts = new List<SuggestionFact>();
                void Add(string icon, Brush color, string text) => facts.Add(new(icon, color, text));

                var late = d.Problems.FirstOrDefault(p => p.Kind == "LATE_PICKUP");
                Add(late is null ? "CheckCircleOutline" : "AlertCircleOutline", late is null ? Good : Bad,
                    string.Format(T("assist.FactPickup"), FeasibilityText.Hour(d.PickupEta), ArrivesDetail, FeasibilityText.Hour(d.FromTime), FeasibilityText.Hour(d.PickupDeparture)));
                Add("MapMarkerDistance", Quiet, string.Format(T("assist.FactReach"), d.MilesToPickup.ToString("0.0"), d.MinutesToPickup));

                if (d.AppointmentMarginMinutes is int margin)
                    Add(margin >= 0 ? "CheckCircleOutline" : margin >= -15 ? "ClockAlertOutline" : "AlertCircleOutline", margin >= 0 ? Good : margin >= -15 ? Warn : Bad,
                        string.Format(T(margin >= 0 ? "assist.FactAppointment" : margin >= -15 ? "assist.FactMargin" : "assist.FactMissed"), FeasibilityText.Hour(d.DropoffEta), Math.Abs(margin), FeasibilityText.Hour(d.ToTime)));
                else Add("MapMarkerCheckOutline", Quiet, string.Format(T("assist.FactDropoff"), FeasibilityText.Hour(d.DropoffEta)));

                if (d.LatestPickup is { } latest) Add("ClockOutline", Quiet, string.Format(T("assist.FactLatest"), FeasibilityText.Hour(latest)));
                if (d.WaitMinutes > 0) Add("TimerSand", Quiet, string.Format(T("assist.FactWait"), d.WaitMinutes));

                var others = d.Problems.Where(p => p.Kind is "DELAYS_PICKUP" or "DELAYS_APPOINTMENT").ToList();
                if (others.Count == 0) Add("CheckCircleOutline", Good, T("assist.FactNobodyLate"));
                var otherMargin = d.MarginPatients - (d.AppointmentMarginMinutes is < 0 and >= -15 ? 1 : 0);
                if (otherMargin > 0) Add("ClockAlertOutline", Warn, string.Format(T("assist.FactOthersMargin"), otherMargin));
                foreach (var p in others)
                    Add("AlertCircleOutline", Bad, string.Format(T(p.Kind == "DELAYS_PICKUP" ? "assist.FactDelaysPickup" : "assist.FactDelaysAppt"), p.Stop, p.Minutes));

                Add("SwapHorizontal", Quiet, string.Format(T("assist.FactMiles"), d.AddedMiles.ToString("0.0"), d.AddedEmptyMiles.ToString("0.0"), d.LineMilesBefore.ToString("0.0"), d.LineMilesAfter.ToString("0.0")));
                Add("AccountClockOutline", Quiet, string.Format(T("assist.FactPaid"), d.AddedPaidMinutes));
                Add(d.VehicleKind is null || d.Group == 4 ? "AlertCircleOutline" : "SeatPassenger", d.Group == 4 ? Bad : Good,
                    d.Group == 4 ? string.Format(T("assist.FactCannot"), d.VehicleKind ?? T("vehcap.NotConfigured"), d.SpaceType)
                    : d.Shared ? string.Format(T("assist.FactShared"), string.Join(", ", d.RidesWith), d.ExtraRideMinutes) : string.Format(T("assist.FactAlone"), d.VehicleKind, d.SpaceType));
                if (d.OpensLine) Add("GarageOpenVariant", Warn, T("assist.FactOpens"));
                return facts;
            }
        }

        public List<SuggestionStopRow> Stops => Dto.Stops.Where(s => !s.Done).Select(s =>
        {
            var change = s.EtaBefore is { } was && Math.Abs((s.Eta - was).TotalMinutes) >= 1
                ? string.Format("{0}{1} min", s.Eta > was ? "+" : "−", (int)Math.Abs(Math.Round((s.Eta - was).TotalMinutes))) : "";
            var worse = s.EtaBefore is { } w && s.Eta > w && s.Appointment is { } a && a > TimeSpan.Zero && s.Eta > a && s.Kind == "DROPOFF";
            return new SuggestionStopRow(s.Kind, s.Kind switch { "PULL_OUT" => "Pull-out", "PULL_IN" => "Pull-in", _ => s.Name }, FeasibilityText.Hour(s.Eta), change,
                $"{s.Miles:0.0} mi · {s.TravelMinutes} min" + (s.WaitMinutes > 0 ? " · " + string.Format(T("assist.WaitShort"), s.WaitMinutes) : "") + (s.OnBoardAfter > 1 ? " · " + string.Format(T("assist.OnBoard"), s.OnBoardAfter) : ""),
                s.IsNew, s.Done, worse);
        }).ToList();

        public bool CanRoute => true;
        public string RouteText => T(Dto.Group <= 2 ? "assist.RouteHere" : "assist.RouteAnyway");
    }

    /// <summary>
    /// RE-098/099: the panel of suggestions beside the map. It reads; routing stays in the dispatch
    /// screen, which is asked for it.
    /// </summary>
    public partial class RoutingSuggestionsViewModel : ObservableObject
    {
        private readonly RoutingAssistantService _service = new();
        private readonly Func<RoutingSuggestionDto, Task> _route;
        private readonly Func<(int TripId, int RouteId, DateTime Date)> _subject;
        private CancellationTokenSource? _loading;
        private CancellationTokenSource? _detail;
        private static string T(string key) => LocalizationService.Instance[key];

        public RoutingSuggestionsViewModel(Func<(int, int, DateTime)> subject, Func<RoutingSuggestionDto, Task> route)
        {
            _subject = subject;
            _route = route;
        }

        public LocalizationService L => LocalizationService.Instance;
        public ObservableCollection<RoutingSuggestionRow> Rows { get; } = new();

        /// <summary>The plan to draw on the map; null to clear it.</summary>
        public event Action<RoutingSuggestionDto?>? PlanChanged;
        /// <summary>The panel was closed or opened.</summary>
        public event Action? OpenChanged;

        [ObservableProperty] private bool _isOpen;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTrips), nameof(Heading))] private bool _isLines = true;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string? _message;
        [ObservableProperty] private string? _subjectText;
        [ObservableProperty] private string? _budgetText;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelected))] private RoutingSuggestionRow? _selected;

        public bool IsTrips => !IsLines;
        public bool HasMessage => Message != null;
        public bool HasSelected => Selected != null;
        public string Heading => T(IsLines ? "assist.HeadingLines" : "assist.HeadingTrips");

        partial void OnIsOpenChanged(bool value)
        {
            OpenChanged?.Invoke();
            if (value) Refresh();
            else { _loading?.Cancel(); _detail?.Cancel(); Rows.Clear(); Selected = null; PlanChanged?.Invoke(null); }
        }

        partial void OnSelectedChanged(RoutingSuggestionRow? value)
        {
            PlanChanged?.Invoke(value?.Dto);
            _detail?.Cancel();
            if (value is null || value.Dto.Confirmed || value.Dto.Group == 4) return;
            _ = ConfirmAsync(value, (_detail = new()).Token);
        }

        [RelayCommand] private void Close() => IsOpen = false;
        [RelayCommand] private void ShowLines() { IsLines = true; if (IsOpen) Refresh(); else IsOpen = true; }
        [RelayCommand] private void ShowTrips() { IsLines = false; if (IsOpen) Refresh(); else IsOpen = true; }
        [RelayCommand] private void Reload() => Refresh(0);

        [RelayCommand]
        private async Task RouteAsync(RoutingSuggestionRow? row)
        {
            if ((row ?? Selected) is not { } target || target.IsWorking) return;
            target.IsWorking = true;
            try { await _route(target.Dto); }
            finally { target.IsWorking = false; }
        }

        /// <summary>The trip or the line the panel speaks of changed. Waits a moment: a selection that only passes by asks for nothing.</summary>
        public void Refresh(int delay = 500)
        {
            if (!IsOpen) return;
            _loading?.Cancel();
            _ = LoadAsync(delay, (_loading = new()).Token);
        }

        private async Task LoadAsync(int delay, CancellationToken ct)
        {
            try
            {
                if (delay > 0) await Task.Delay(delay, ct);
                var (tripId, routeId, date) = _subject();
                Message = null;
                if (IsLines ? tripId <= 0 : routeId <= 0) { Clear(T(IsLines ? "assist.PickTrip" : "assist.PickLine")); return; }

                IsBusy = true;
                var answer = IsLines ? await _service.LinesAsync(tripId, ct) : await _service.TripsAsync(routeId, date, ct);
                ct.ThrowIfCancellationRequested();
                if (answer is null) { Clear(T(IsLines ? "assist.NotOpen" : "assist.LineNotAvailable")); return; }

                Rows.Clear();
                foreach (var s in answer.Suggestions) Rows.Add(new RoutingSuggestionRow(s, IsLines));
                SubjectText = IsLines && answer.Suggestions.FirstOrDefault() is { } first
                    ? $"{FeasibilityText.Hour(first.FromTime)}  {first.Patient} · {first.SpaceType}" + (first.ToTime is { } to ? " · " + string.Format(T("assist.Appointment"), FeasibilityText.Hour(to)) : "")
                    : answer.Suggestions.FirstOrDefault()?.Line;
                BudgetText = answer.ConfirmationsLeft <= 0 ? T("assist.BudgetOver") : string.Format(T("assist.BudgetLeft"), answer.ConfirmationsLeft);
                if (Rows.Count == 0) { Clear(T(IsLines ? "assist.NoLines" : "assist.NoTrips")); return; }
                Selected = Rows[0];
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileLogger.Log($"Routing suggestions: {ex.Message}");
                Clear(T("assist.Failed"));
            }
            finally
            {
                if (!ct.IsCancellationRequested) IsBusy = false;
            }
        }

        private void Clear(string message)
        {
            Rows.Clear();
            Selected = null;
            SubjectText = null;
            Message = message;
        }

        private async Task ConfirmAsync(RoutingSuggestionRow row, CancellationToken ct)
        {
            try
            {
                // Arrowing down the list does not buy a road per row.
                await Task.Delay(700, ct);
                row.IsWorking = true;
                var detail = await _service.DetailAsync(row.Dto, ct);
                if (detail is null || ct.IsCancellationRequested) return;
                row.Update(detail);
                if (ReferenceEquals(Selected, row)) PlanChanged?.Invoke(row.Dto);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { FileLogger.Log($"Routing suggestion detail: {ex.Message}"); }
            finally { row.IsWorking = false; }
        }

        public void Retranslate()
        {
            OnPropertyChanged(string.Empty);
            foreach (var r in Rows) r.Update(r.Dto);
        }
    }
}
