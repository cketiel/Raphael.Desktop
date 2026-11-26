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
using System.Threading.Tasks;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>A trip whose pickup could be adjusted, and what was done with it.</summary>
    public partial class ImportHoursRow : ObservableObject
    {
        private static string T(string key) => LocalizationService.Instance[key];
        public ImportHoursRow(ImportHoursTripDto data)
        {
            Data = data;
            _isAdjusted = data.Check.Adjustment == FeasibilityText.Adjusted;
        }

        public ImportHoursTripDto Data { get; }
        public int TripId => Data.Check.TripId;
        public bool IsLoose => Data.Check.Kind == FeasibilityText.Loose;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAdjust), nameof(StateText), nameof(ButtonText))] private bool _isAdjusted;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAdjust))] private bool _isWorking;

        public string When => $"{Data.Date:MM/dd}  {Data.TripNumber}";
        public string Who => $"{Data.Patient} · {Data.SpaceType}";
        public string Hours => $"{FeasibilityText.Hour(Data.Check.OriginalFromTime)} → {FeasibilityText.Hour(Data.Check.OriginalToTime)}";
        public string Problem => Data.Check.Kind == FeasibilityText.BadHours ? T("feasrep.BadHours")
            : IsLoose ? string.Format(T("hours.Spare"), Data.Check.SlackMinutes) : string.Format(T("hours.Short"), Data.Check.MissingMinutes);
        public string NewHour => Data.Check.SuggestedFromTime is { } h ? FeasibilityText.Hour(h) : "—";
        public string Moves => Data.MovesMinutes is int m ? string.Format(T(m < 0 ? "hours.Earlier" : "hours.Later"), Math.Abs(m)) : T("hours.AskBroker");
        public bool HasHour => Data.Check.SuggestedFromTime != null;
        /// <summary>Half an hour or more earlier: the office will be told to call the patient.</summary>
        public bool TellsPatient => Data.MovesMinutes is <= -30;
        public bool CanAdjust => HasHour && !IsWorking;
        public string StateText => IsAdjusted ? T("hours.StateAdjusted") : Data.WasAdjustedBefore ? T("hours.StateWasAdjusted") : "";
        public string ButtonText => T(IsAdjusted ? "hours.Undo" : "hours.Adjust");
        public string ToolTip => FeasibilityText.Explain(Data.Check);
    }

    /// <summary>
    /// RE-095: what a file brought that does not fit, and the question of whether to adjust its pickup
    /// hours. Nothing moves until a button is pressed; the broker's hours are kept and can be put back.
    /// </summary>
    public partial class ImportHoursViewModel : ObservableObject
    {
        private readonly TripFeasibilityService _service = new();
        private readonly ImportHoursReviewDto _review;
        private static string T(string key) => LocalizationService.Instance[key];

        public ImportHoursViewModel(ImportHoursReviewDto review)
        {
            _review = review;
            foreach (var t in review.Trips) (t.Check.Kind == FeasibilityText.Loose ? Loose : Impossible).Add(new ImportHoursRow(t));
        }

        public LocalizationService L => LocalizationService.Instance;
        public ObservableCollection<ImportHoursRow> Impossible { get; } = new();
        public ObservableCollection<ImportHoursRow> Loose { get; } = new();

        /// <summary>What was done, for the import's log.</summary>
        public event Action<string>? Done;
        public event Action? CloseRequested;

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string? _message;
        public bool HasMessage => Message != null;

        private string Percent(int n) => _review.Imported == 0 ? "0%" : (n * 100.0 / _review.Imported).ToString("0.0") + "%";
        public string Headline => string.Format(T("hours.Headline"), Impossible.Count, Percent(Impossible.Count), Loose.Count, Percent(Loose.Count), _review.Imported);
        public string ImpossibleTitle => string.Format(T("hours.ImpossibleTitle"), Impossible.Count, Percent(Impossible.Count));
        public string LooseTitle => string.Format(T("hours.LooseTitle"), Loose.Count, Percent(Loose.Count));
        public bool HasImpossible => Impossible.Count > 0;
        public bool HasLoose => Loose.Count > 0;
        public bool HasCalls => Impossible.Any(r => r.TellsPatient);
        public string CallsText => string.Format(T("hours.Calls"), Impossible.Count(r => r.TellsPatient));
        public bool CanAdjustAny => !IsBusy;

        [RelayCommand] private Task AdjustAllAsync() => ApplyAsync(Impossible.Concat(Loose), true);
        [RelayCommand] private Task AdjustImpossibleAsync() => ApplyAsync(Impossible, true);
        [RelayCommand] private Task AdjustLooseAsync() => ApplyAsync(Loose, true);
        [RelayCommand] private Task UndoAllAsync() => ApplyAsync(Impossible.Concat(Loose), false);
        [RelayCommand] private Task ToggleAsync(ImportHoursRow? row) => row is null ? Task.CompletedTask : ApplyAsync(new[] { row }, !row.IsAdjusted);

        /// <summary>Leaves what was not adjusted as the broker sent it, and says so in the history.</summary>
        [RelayCommand]
        private async Task CloseAsync()
        {
            try
            {
                var left = Impossible.Concat(Loose).Where(r => !r.IsAdjusted).Select(r => r.TripId).ToList();
                if (left.Count > 0) await _service.HoursAsync(left, HoursActions.Decline);
            }
            catch (Exception ex) { FileLogger.Log($"Import hours, decline: {ex.Message}"); }
            var adjusted = Impossible.Concat(Loose).Count(r => r.IsAdjusted);
            Done?.Invoke(string.Format(T("hours.Finished"), adjusted, Impossible.Count + Loose.Count - adjusted));
            CloseRequested?.Invoke();
        }

        private async Task ApplyAsync(IEnumerable<ImportHoursRow> rows, bool adjust)
        {
            var target = rows.Where(r => r.HasHour && r.IsAdjusted != adjust).ToList();
            if (target.Count == 0 || IsBusy) return;
            IsBusy = true;
            foreach (var r in target) r.IsWorking = true;
            try
            {
                var result = await _service.HoursAsync(target.Select(r => r.TripId), adjust ? HoursActions.Adjust : HoursActions.Undo);
                foreach (var r in target) r.IsAdjusted = adjust;
                Message = string.Format(T(adjust ? "hours.AdjustedN" : "hours.UndoneN"), result.Adjusted)
                    + (result.Skipped > 0 ? " " + string.Format(T("hours.SkippedN"), result.Skipped) : "")
                    + (result.PatientsToTell > 0 ? " " + string.Format(T("hours.CallsN"), result.PatientsToTell) : "");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Import hours: {ex.Message}");
                Message = T("hours.Failed");
            }
            finally
            {
                foreach (var r in target) r.IsWorking = false;
                IsBusy = false;
            }
        }
    }
}
