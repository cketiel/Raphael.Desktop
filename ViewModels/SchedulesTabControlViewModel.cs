using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;

namespace Raphael.Desktop.ViewModels
{
    public class SchedulesTabControlViewModel : BaseViewModel
    {
        public SchedulesViewModel ScheduleContentViewModel { get; }

        /// <param name="notificationService">
        /// Passed straight through to the schedule panel, which is the one that has to
        /// know when a trip on screen gets cancelled somewhere else.
        /// </param>
        public SchedulesTabControlViewModel(
            INotificationService notificationService = null)
        {
            var scheduleService = new ScheduleService();
            ScheduleContentViewModel = new SchedulesViewModel(
                scheduleService,
                notificationService);

        }

        #region Translation

        public string Schedule => LocalizationService.Instance["Schedule"];
        public string Trips => LocalizationService.Instance["Trips"];
        public string Revenue => LocalizationService.Instance["Revenue"];
        public string Graphs => LocalizationService.Instance["Graphs"];

        #endregion
    }
}
