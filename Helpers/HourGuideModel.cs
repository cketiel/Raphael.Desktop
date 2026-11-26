#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// RE-094: the hours of a trip being typed, judged as an import judges them. It says whether the
    /// appointment is reached, the pickup hours that work and why, and offers the right one. It never
    /// stops a save: the dispatcher decides. Used by the Home form and the edit dialog.
    /// </summary>
    public partial class HourGuideModel : ObservableObject
    {
        private readonly TripFeasibilityService _service = new();
        private readonly Func<FeasibilityPreviewRequestDto?> _request;
        private readonly Action<TimeSpan> _use;
        private readonly Func<bool> _canUse;
        private DispatcherTimer? _timer;
        private CancellationTokenSource? _cts;
        private TripFeasibilityDto? _hourGuide;
        private (TimeSpan From, TimeSpan? To)? _hourGuideFor;
        private bool _isHourGuideOpen;

        /// <param name="request">What to judge now; null when there is nothing to judge.</param>
        /// <param name="use">Writes the recommended pickup in the form.</param>
        public HourGuideModel(Func<FeasibilityPreviewRequestDto?> request, Action<TimeSpan> use, Func<bool> canUse)
        {
            _request = request;
            _use = use;
            _canUse = canUse;
        }

        /// <summary>Something the hours depend on changed: judged again once the typing stops.</summary>
        public void Changed()
        {
            if (_request() is null) { SetHourGuide(null, null); return; }
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, async (_, _) => { _timer!.Stop(); await LoadAsync(); }, Dispatcher.CurrentDispatcher);
            _timer.Stop();
            _timer.Start();
        }

        private async Task LoadAsync()
        {
            if (_request() is not { } request) { SetHourGuide(null, null); return; }
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            try
            {
                var answer = await _service.PreviewAsync(request, cts.Token);
                if (!cts.IsCancellationRequested) SetHourGuide(answer, (request.FromTime!.Value, request.ToTime));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Without it the form is the one of always.
                FileLogger.Log($"Hour guide: {ex.Message}");
                SetHourGuide(null, null);
            }
        }

        private void SetHourGuide(TripFeasibilityDto? guide, (TimeSpan, TimeSpan?)? hours)
        {
            var wasProblem = HourGuideIsProblem;
            _hourGuide = guide;
            _hourGuideFor = hours;
            // A problem opens the explanation by itself; a fine hour only leaves the chip.
            if (HourGuideIsProblem && !wasProblem) _isHourGuideOpen = true;
            if (!HasHourGuide) _isHourGuideOpen = false;
            foreach (var n in new[] { nameof(HasHourGuide), nameof(HourGuideKind), nameof(HourGuideChip), nameof(HourGuideTitle), nameof(HourGuideWhy), nameof(HourGuideLimits),
                nameof(HourGuideRoad), nameof(HourGuideIsProblem), nameof(CanUseHourGuide), nameof(UseHourGuideText), nameof(IsHourGuideOpen) })
                OnPropertyChanged(n);
        }

        private static string Tx(string key) => LocalizationService.Instance[key];
        private static string H(TimeSpan? t) => FeasibilityText.Hour(t);

        public bool HasHourGuide => _hourGuide is { Kind: >= 1 } g && (g.Kind == FeasibilityText.BadHours || g.DriveMinutes != null);
        /// <summary>1 fine, 2 tight, 3 impossible, 4 bad hours, 5 loose.</summary>
        public int HourGuideKind => _hourGuide?.Kind ?? 0;
        public bool HourGuideIsProblem => HasHourGuide && HourGuideKind is FeasibilityText.Impossible or FeasibilityText.BadHours or FeasibilityText.Loose;

        public bool IsHourGuideOpen
        {
            get => _isHourGuideOpen && HasHourGuide;
            set { if (_isHourGuideOpen == value) return; _isHourGuideOpen = value; OnPropertyChanged(); }
        }

        [RelayCommand] private void ToggleHourGuide() => IsHourGuideOpen = !IsHourGuideOpen;

        /// <summary>Minutes from the pickup to the appointment the trip needs: boarding and drive.</summary>
        private int Need => (_hourGuide?.BoardingMinutes ?? 0) + (_hourGuide?.DriveMinutes ?? 0) + (_hourGuide?.LeadMinutes ?? 0);
        /// <summary>The latest pickup that reaches the appointment, and the earliest that is not loose.</summary>
        private TimeSpan? Latest => _hourGuide?.ToTime is { } to ? to - TimeSpan.FromMinutes(Need) : null;
        private TimeSpan? Earliest => Latest is { } l ? l - TimeSpan.FromMinutes(30) : null;

        public string HourGuideChip => HourGuideKind switch
        {
            FeasibilityText.Impossible => string.Format(Tx("guide.ChipImpossible"), _hourGuide!.MissingMinutes),
            FeasibilityText.BadHours => Tx("guide.ChipBad"),
            FeasibilityText.Loose => string.Format(Tx("guide.ChipLoose"), _hourGuide!.SlackMinutes),
            FeasibilityText.Tight => Tx("guide.ChipTight"),
            _ => Tx("guide.ChipFine"),
        };

        public string HourGuideTitle => HourGuideKind switch
        {
            FeasibilityText.Impossible => Tx("guide.TitleImpossible"),
            FeasibilityText.BadHours => Tx("guide.TitleBad"),
            FeasibilityText.Loose => Tx("guide.TitleLoose"),
            FeasibilityText.Tight => Tx("guide.TitleTight"),
            _ => Tx("guide.TitleFine"),
        };

        /// <summary>What happens with the hours as typed.</summary>
        public string HourGuideWhy
        {
            get
            {
                if (_hourGuide is not { } g || g.FromTime is not { } from || g.ToTime is not { } to) return "";
                if (g.Kind == FeasibilityText.BadHours) return string.Format(Tx("guide.WhyBad"), H(to), H(from)) + (g.HintToTime is { } hint ? " " + string.Format(Tx("guide.WhyBadHint"), H(hint)) : "");
                var boarded = from + TimeSpan.FromMinutes(g.BoardingMinutes);
                var arrives = from + TimeSpan.FromMinutes(Need);
                return g.Kind switch
                {
                    FeasibilityText.Impossible => string.Format(Tx("guide.WhyImpossible"), H(from), H(boarded), H(arrives), g.MissingMinutes, H(to)),
                    FeasibilityText.Loose => string.Format(Tx("guide.WhyLoose"), H(from), H(arrives), g.SlackMinutes, H(to)),
                    FeasibilityText.Tight => string.Format(Tx("guide.WhyTight"), H(from), H(arrives), H(to)),
                    _ => string.Format(Tx("guide.WhyFine"), H(from), H(arrives), H(to)),
                };
            }
        }

        /// <summary>The limits and the reason for them.</summary>
        public string HourGuideLimits => HourGuideKind == FeasibilityText.BadHours || Latest is not { } latest || Earliest is not { } earliest ? ""
            : string.Format(Tx("guide.Limits"), H(earliest), H(latest), Need, _hourGuide!.BoardingMinutes, _hourGuide.DriveMinutes, H(_hourGuide.ToTime));

        public string HourGuideRoad => _hourGuide is { DriveMinutes: not null } g
            ? string.Format(Tx(g.DriveIsPriced ? "guide.RoadGoogle" : "guide.RoadEstimated"), g.DriveMiles is { } m ? m.ToString("0.0") : "?", g.DriveMinutes) : "";

        public bool CanUseHourGuide => HourGuideIsProblem && _hourGuide?.SuggestedFromTime != null && _canUse();
        public string UseHourGuideText => _hourGuide?.SuggestedFromTime is { } s ? string.Format(Tx("guide.Use"), H(s)) : "";

        [RelayCommand]
        private void UseHourGuide()
        {
            if (_hourGuide?.SuggestedFromTime is not { } hour) return;
            _use(hour);
        }

        /// <summary>
        /// Before saving: an impossible or loose hour is explained once more and the right one offered.
        /// Yes uses it, No keeps the typed one, Cancel goes back to the form. Uses what is already known.
        /// </summary>
        public bool ConfirmHourGuide()
        {
            if (!HourGuideIsProblem || _request() is not { } now || _hourGuideFor is not { } was || was.From != now.FromTime || (now.ToTime != null && was.To != now.ToTime)) return true;
            var text = HourGuideTitle + "\n\n" + HourGuideWhy + (HourGuideLimits.Length > 0 ? "\n\n" + HourGuideLimits : "");
            if (CanUseHourGuide)
            {
                var answer = MessageBox.Show(text + "\n\n" + string.Format(Tx("guide.AskUse"), H(_hourGuide!.SuggestedFromTime)), Tx("guide.AskTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Cancel) return false;
                if (answer == MessageBoxResult.Yes) UseHourGuide();
                return true;
            }
            return MessageBox.Show(text + "\n\n" + Tx("guide.AskKeep"), Tx("guide.AskTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        }
    }
}
