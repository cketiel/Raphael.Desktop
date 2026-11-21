using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// A past day read-only, and the trip Home sends the dispatcher to look at (RE-051).
    /// </summary>
    public partial class SchedulesViewModel
    {
        /// <summary>The day on screen is before today: routing, reordering and recalculating stop.</summary>
        public bool IsDayReadOnly => TripGuard.IsPastDay(SelectedDate);

        public string DayReadOnlyText => IsDayReadOnly
            ? string.Format(LocalizationService.Instance["lock.ScheduleReadOnly"], SelectedDate.ToString("d")) +
              (TripGuard.IsAdmin ? " " + LocalizationService.Instance["lock.ScheduleReadOnlyAdmin"] : string.Empty)
            : null;

        private void RaiseDayReadOnly()
        {
            OnPropertyChanged(nameof(IsDayReadOnly));
            OnPropertyChanged(nameof(DayReadOnlyText));
        }

        private int? _guideTripId;

        /// <summary>The trip whose two stops are framed, or null.</summary>
        public int? GuideTripId
        {
            get => _guideTripId;
            private set
            {
                if (_guideTripId == value) return;
                _guideTripId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasGuide));
                OnPropertyChanged(nameof(GuideText));
            }
        }

        public bool HasGuide => GuideTripId.HasValue;

        public string GuideText => HasGuide ? LocalizationService.Instance["lock.ScheduleGuide"] : null;
        public string GuideDismissText => LocalizationService.Instance["lock.ScheduleGuideDismiss"];

        /// <summary>Asked of the view to bring a stop into sight; the grid is virtualised.</summary>
        public event EventHandler<ScheduleDto> ScrollScheduleIntoViewRequest;

        /// <summary>
        /// Opens a route on a day and frames one trip's pickup and dropoff — what Home's
        /// "show in Schedule" lands on.
        /// </summary>
        /// <param name="atDropoff">RE-080: a notice about the dropoff lands on the dropoff row.</param>
        public async Task ShowTripAsync(DateTime date, int vehicleRouteId, int tripId, bool atDropoff = false)
        {
            GuideTripId = tripId;
            await ShowRouteAsync(date, vehicleRouteId);
            ApplyGuideHighlight();

            var wanted = atDropoff ? Models.ScheduleEventType.Dropoff : Models.ScheduleEventType.Pickup;

            // A cancelled trip only shows its pickup, so the dropoff falls back to it.
            var stop = Schedules.FirstOrDefault(s => s.TripId == tripId && s.EventType == wanted)
                       ?? Schedules.FirstOrDefault(s => s.TripId == tripId);
            if (stop != null)
            {
                SelectedSchedule = stop;
                ScrollScheduleIntoViewRequest?.Invoke(this, stop);
            }
        }

        [RelayCommand]
        private void ClearGuideHighlight()
        {
            GuideTripId = null;
            ApplyGuideHighlight();
        }

        private void ApplyGuideHighlight()
        {
            foreach (var s in _masterSchedules)
                s.IsGuideHighlighted = GuideTripId.HasValue && s.TripId == GuideTripId;
        }
    }
}
