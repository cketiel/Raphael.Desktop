#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuestPDF.Fluent;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models.Pdf;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>One trip of the report and its words.</summary>
    public sealed class ImpossibleRow
    {
        private static string T(string key) => LocalizationService.Instance[key];
        public ImpossibleRow(ImpossibleAppointmentDto data) => Data = data;
        public ImpossibleAppointmentDto Data { get; }
        public DateTime Date => Data.Date;
        public string? TripNumber => Data.TripNumber;
        public string? FundingSource => Data.FundingSource;
        public string OriginText => T("feasrep.S_" + Data.Source);
        public bool IsManual => Data.Source == "MANUAL";
        public string? SpaceType => Data.SpaceType;
        public string? Patient => Data.Patient;
        public TimeSpan? From => Data.Check.OriginalFromTime;
        public bool IsLoose => Data.Check.Kind == FeasibilityText.Loose;
        public string KindText => T(IsLoose ? "feasrep.KindLoose" : "feasrep.KindImpossible");
        public string FromText => FeasibilityText.Hour(Data.Check.OriginalFromTime ?? Data.Check.FromTime);
        public string ToText => FeasibilityText.Hour(Data.Check.OriginalToTime ?? Data.Check.ToTime);
        public string NowText => FeasibilityText.Hour(Data.Check.FromTime);
        public int State => Data.Check.Adjustment;
        public string StateText => T("feasrep.A" + Data.Check.Adjustment);
        public string StateDetail => FeasibilityText.AdjustmentText(Data.Check) + (Data.Check.AdjustedAtUtc is { } at && Data.Check.Adjustment != 0 ? "  " + DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("MM/dd/yyyy h:mm tt") : "");
        public string DifferenceText => Data.Check.OriginalFromTime is { } o && Data.Check.FromTime is { } n && o != n ? $"{(n > o ? "+" : "−")}{Math.Abs((int)(n - o).TotalMinutes)} min" : "—";
        public string BrokerText => Data.DropoffArrived is { } d && (Data.Check.OriginalToTime ?? Data.Check.ToTime) is { } to && to > TimeSpan.Zero
            ? (int)Math.Floor(d.TotalMinutes) - (int)Math.Floor(to.TotalMinutes) is var late && late > 15 ? string.Format(T("feasrep.BrokerLate"), late) : late > 0 ? string.Format(T("feasrep.StrictLate"), late) : T("feasrep.OnTime") : "—";
        public int Missing => Data.Check.Kind == FeasibilityText.BadHours ? int.MaxValue : Data.Check.MissingMinutes;
        public string MissingText => IsLoose ? string.Format(T("hours.Spare"), Data.Check.SlackMinutes) : Data.Check.Kind == FeasibilityText.BadHours ? T("feasrep.BadHours") : string.Format(T("feasrep.MinutesShort"), Data.Check.MissingMinutes);
        public string SuggestedText => Data.Check.SuggestedFromTime is { } h ? FeasibilityText.Hour(h) : "—";
        public string HappenedText => Data.PickupArrived is null && Data.DropoffArrived is null ? "—"
            : $"{FeasibilityText.Hour(Data.PickupArrived)} → {FeasibilityText.Hour(Data.DropoffArrived)}";
        public string Verdict => Data.Verdict;
        public string VerdictText => T("feasrep.V_" + Data.Verdict);
        public string VerdictTip => T("feasrep.VT_" + Data.Verdict);
        public string Explain => FeasibilityText.Explain(Data.Check);
        public double AvailableWeight => Math.Max(0.001, Math.Max(0, Data.Check.AvailableMinutes ?? 0));
        public double MissingWeight => Math.Max(0.001, IsLoose ? 0 : Data.Check.MissingMinutes);
        public string Received => string.Format(T("feasrep.Received"), FeasibilityText.Hour(Data.Check.OriginalFromTime), FeasibilityText.Hour(Data.Check.OriginalToTime), T("feasrep.S_" + Data.Source))
            + (Data.Check.OriginalConfirmed ? "" : " " + T("feasrep.NotConfirmed"));
        public string Places => $"{Data.PickupAddress}\n{Data.DropoffAddress}";
        public string Happened => Data.PickupArrived is null && Data.DropoffArrived is null ? T("feasrep.NothingYet")
            : string.Format(T("feasrep.Happened"), FeasibilityText.Hour(Data.PickupArrived), FeasibilityText.Hour(Data.PickupPerformed), FeasibilityText.Hour(Data.DropoffArrived), Data.Line, Data.Driver, Data.Vehicle);
    }

    public sealed record ImpossibleChoice(string Key, string Name);

    /// <summary>RE-096: the report of impossible appointments, for the funding sources.</summary>
    public partial class ImpossibleAppointmentsViewModel : ObservableObject
    {
        private readonly TripFeasibilityService _service = new();
        private CancellationTokenSource? _loading;
        private ImpossibleAppointmentsReportDto? _report;
        private static string T(string key) => LocalizationService.Instance[key];

        public ImpossibleAppointmentsViewModel() => LocalizationService.Instance.LanguageChanged += Retranslate;

        public LocalizationService L => LocalizationService.Instance;
        public string TitleText => T("feasrep.Title");
        public ObservableCollection<ImpossibleRow> Rows { get; } = new();
        public ObservableCollection<ImpossibleFundingSourceDto> Sources { get; } = new();

        // The operation's own calendar day is the office's; only the default of a date box.
        [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-30);
        [ObservableProperty] private DateTime _endDate = DateTime.Today;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string? _message;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private ImpossibleRow? _selected;
        [ObservableProperty] private bool _withNames;
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SavePdfCommand), nameof(SaveExcelCommand))] private bool _hasReport;
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SavePdfCommand))] private ImpossibleFundingSourceDto? _selectedSource;
        [ObservableProperty] private string _verdictFilter = "";
        [ObservableProperty] private string? _summary;
        [ObservableProperty] private string _kindFilter = "";
        [ObservableProperty] private string _originFilter = "";
        public List<ImpossibleChoice> Origins => new() { new("", T("feasrep.AllOrigins")), new("B", T("feasrep.OriginBroker")), new("MANUAL", T("feasrep.S_MANUAL")), new("UNKNOWN", T("feasrep.S_UNKNOWN")) };
        partial void OnOriginFilterChanged(string value) => Fill();
        [ObservableProperty] private string _stateFilter = "";

        public List<ImpossibleChoice> Kinds => new() { new("", T("feasrep.AllKinds")), new("I", T("feasrep.KindImpossible")), new("L", T("feasrep.KindLoose")) };
        public List<ImpossibleChoice> States => new() { new("", T("feasrep.AllStates")), new("Y", T("feasrep.StateYes")), new("N", T("feasrep.StateNo")) };
        partial void OnKindFilterChanged(string value) => Fill();
        partial void OnStateFilterChanged(string value) => Fill();

        /// <summary>The live map asks for one day.</summary>
        public Task ShowDayAsync(DateTime date)
        {
            (StartDate, EndDate) = (date, date);
            return GenerateAsync();
        }

        public bool HasMessage => Message != null;
        public bool IsAdmin => TripGuard.IsAdmin;
        public List<ImpossibleChoice> Verdicts => new[] { "", "FACTS", "CALCULATION", "ARGUABLE", "DO_NOT_CLAIM", "MANUAL" }
            .Select(v => new ImpossibleChoice(v, v.Length == 0 ? T("feasrep.AllVerdicts") : T("feasrep.V_" + v))).ToList();

        partial void OnSelectedSourceChanged(ImpossibleFundingSourceDto? value) => Fill();
        partial void OnVerdictFilterChanged(string value) => Fill();

        [RelayCommand]
        private async Task GenerateAsync()
        {
            _loading?.Cancel();
            var ct = (_loading = new()).Token;
            Message = null;
            if (EndDate < StartDate || (EndDate - StartDate).TotalDays > 92) { Message = T("feasrep.BadRange"); return; }
            IsBusy = true;
            try
            {
                _report = await _service.ReportAsync(StartDate, EndDate, null, ct);
                Sources.Clear();
                foreach (var s in _report.FundingSources) Sources.Add(s);
                SelectedSource = null;
                Fill();
                HasReport = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileLogger.Log($"Impossible appointments: {ex.Message}");
                Message = T("feasrep.Failed");
            }
            finally { IsBusy = false; }
        }

        private void Fill()
        {
            Rows.Clear();
            if (_report is null) return;
            foreach (var t in _report.Trips)
                if ((SelectedSource is null || t.FundingSourceId == SelectedSource.FundingSourceId) && (VerdictFilter.Length == 0 || t.Verdict == VerdictFilter)
                    && (OriginFilter.Length == 0 || (OriginFilter == "B" ? t.Source is "FILE" or "INTEGRATION" or "PROVIDER" : t.Source == OriginFilter))
                    && (KindFilter.Length == 0 || (t.Check.Kind == FeasibilityText.Loose) == (KindFilter == "L"))
                    && (StateFilter.Length == 0 || (t.Check.Adjustment is FeasibilityText.Adjusted or FeasibilityText.ByHand) == (StateFilter == "Y")))
                    Rows.Add(new ImpossibleRow(t));
            var total = SelectedSource?.Appointments ?? _report.Appointments;
            int N(bool loose, bool moved) => Rows.Count(r => r.IsLoose == loose && (r.State is FeasibilityText.Adjusted or FeasibilityText.ByHand) == moved);
            Summary = string.Format(T("feasrep.Summary2"), total, Rows.Count(r => !r.IsLoose), N(false, true), N(false, false), Rows.Count(r => r.IsLoose), N(true, true), N(true, false),
                Rows.Count(r => r.Verdict == "FACTS"), Rows.Count(r => r.Verdict == "CALCULATION"))
                + (Rows.Any(r => r.IsManual) ? " " + string.Format(T("feasrep.ManualNote"), Rows.Count(r => r.IsManual)) : "")
                + (_report.Checked < _report.Appointments ? " " + string.Format(T("feasrep.Unchecked"), _report.Appointments - _report.Checked) : "");
            OnPropertyChanged(nameof(HasUnchecked));
        }

        public bool HasUnchecked => IsAdmin && _report is { } r && r.Checked < r.Appointments;

        /// <summary>Checks the days before this existed. The server works them in the background.</summary>
        [RelayCommand]
        private async Task CheckPastAsync()
        {
            try { Message = string.Format(T("feasrep.Queued"), await _service.CheckAsync(StartDate, EndDate)); }
            catch (Exception ex) { FileLogger.Log($"Impossible appointments check: {ex.Message}"); Message = T("feasrep.Failed"); }
        }

        private bool CanPdf() => HasReport && IsAdmin && SelectedSource != null;

        /// <summary>The claim: one funding source, and by default only what can be proven.</summary>
        [RelayCommand(CanExecute = nameof(CanPdf))]
        private void SavePdf()
        {
            if (_report is null || SelectedSource is not { } source) return;
            var trips = _report.Trips.Where(t => t.FundingSourceId == source.FundingSourceId
                && (VerdictFilter.Length > 0 ? t.Verdict == VerdictFilter : t.Verdict is "FACTS" or "CALCULATION"))
                .Where(t => t.Check.Kind != FeasibilityText.Loose && t.Verdict != "MANUAL" && t.Source != "MANUAL").OrderBy(t => t.Verdict == "FACTS" ? 0 : 1).ThenBy(t => t.Date).ThenBy(t => t.Check.FromTime).ToList();
            if (trips.Count == 0) { Message = T("feasrep.NothingToClaim"); return; }
            if (AskPath("pdf", "PDF|*.pdf", source.FundingSource) is not { } path) return;
            var (from, to, names) = (_report.From, _report.To, WithNames);
            Views.Reports.SaveFileOverlay.Run(path, file => new ImpossibleAppointmentsDocument(source, trips, from, to, names).GeneratePdf(file));
        }

        [RelayCommand(CanExecute = nameof(HasReport))]
        private void SaveExcel()
        {
            if (_report is null || AskPath("xlsx", "Excel|*.xlsx", SelectedSource?.FundingSource) is not { } path) return;
            var trips = Rows.Select(r => r.Data).ToList();
            // A file of one funding source names no other.
            var sources = SelectedSource is { } one ? new List<ImpossibleFundingSourceDto> { one } : _report.FundingSources;
            var (from, to, names) = (_report.From, _report.To, WithNames);
            Views.Reports.SaveFileOverlay.Run(path, file => ImpossibleAppointmentsWorkbook.Save(trips, sources, from, to, names, file));
        }

        private string? AskPath(string ext, string filter, string? source)
        {
            var name = string.IsNullOrWhiteSpace(source) ? "" : "_" + string.Concat(source.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '-');
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"Pickup-hours{name}_{StartDate:yyyy-MM-dd}_{EndDate:yyyy-MM-dd}.{ext}", Filter = filter, DefaultExt = ext };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void Retranslate()
        {
            OnPropertyChanged(string.Empty);
            Fill();
        }
    }
}
