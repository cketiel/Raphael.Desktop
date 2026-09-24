using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;
using Raphael.Desktop.ViewModels.CallRequests;

namespace Raphael.Desktop.ViewModels
{
    public class SchedulesTabControlViewModel : BaseViewModel
    {
        private CallCardViewModel? _activeCall;

        public SchedulesViewModel ScheduleContentViewModel { get; }

        /// <param name="notificationService">
        /// Passed straight through to the schedule panel, which is the one that has to
        /// know when a trip on screen gets cancelled somewhere else.
        /// </param>
        public SchedulesTabControlViewModel(
            INotificationService? notificationService = null)
        {
            var scheduleService = new ScheduleService();
            ScheduleContentViewModel = new SchedulesViewModel(
                scheduleService,
                notificationService);

            ScheduleContentViewModel.RouteUnavailable += (_, _) => _activeCall?.MarkRouteUnavailable();
        }

        /// <summary>
        /// The driver's call being handled in this tab, shown above their route. Null in an
        /// ordinary Schedule tab.
        /// </summary>
        public CallCardViewModel? ActiveCall
        {
            get => _activeCall;
            private set
            {
                if (SetProperty(ref _activeCall, value))
                    OnPropertyChanged(nameof(HasActiveCall));
            }
        }

        public bool HasActiveCall => ActiveCall != null;

        /// <summary>Puts a call on top of this tab and opens the driver's route on the day of the request.</summary>
        public async Task AttendCallAsync(CallCardViewModel card, DateTime date, int vehicleRouteId)
        {
            CloseCall();

            card.CloseRequested += OnCardCloseRequested;
            ActiveCall = card;

            await ScheduleContentViewModel.ShowRouteAsync(date, vehicleRouteId);
        }

        /// <summary>Takes the card down and lets go of the queue it listens to.</summary>
        public void CloseCall()
        {
            if (_activeCall == null)
                return;

            _activeCall.CloseRequested -= OnCardCloseRequested;
            _activeCall.Dispose();

            ActiveCall = null;
        }

        private void OnCardCloseRequested(object? sender, EventArgs e) => CloseCall();

        #region Translation

        public string Schedule => LocalizationService.Instance["Schedule"];
        public string Trips => LocalizationService.Instance["Trips"];
        public string Revenue => LocalizationService.Instance["Revenue"];
        public string Graphs => LocalizationService.Instance["Graphs"];

        #endregion
    }
}
