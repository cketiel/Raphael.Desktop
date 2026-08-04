using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public class SchedulesTabControlViewModel : BaseViewModel
    {
        public SchedulesViewModel ScheduleContentViewModel { get; }

        public SchedulesTabControlViewModel()
        {            
            var scheduleService = new ScheduleService();
            ScheduleContentViewModel = new SchedulesViewModel(scheduleService);
   
        }

        #region Translation

        public string Schedule => LocalizationService.Instance["Schedule"];
        public string Trips => LocalizationService.Instance["Trips"];
        public string Revenue => LocalizationService.Instance["Revenue"];
        public string Graphs => LocalizationService.Instance["Graphs"];

        #endregion
    }
}
