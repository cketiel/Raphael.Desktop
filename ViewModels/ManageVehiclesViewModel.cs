using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public class ManageVehiclesViewModel: BaseViewModel
    {
        #region Translation

        public string Vehicles => LocalizationService.Instance["Vehicles"];
        public string SpaceTypes => LocalizationService.Instance["SpaceTypes"];
        public string Capacities => LocalizationService.Instance["Capacities"];
        public string Speeds => LocalizationService.Instance["Speeds"];
        public string Groups => LocalizationService.Instance["Groups"];

        #endregion
    }
}
