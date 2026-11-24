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
        public string Configurations => LocalizationService.Instance["vehcfg.Tab"];

        /// <summary>RE-083: only an administrator changes what a configuration carries (the API agrees).</summary>
        public System.Windows.Visibility ConfigurationsVisibility =>
            Helpers.TripGuard.IsAdmin ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public string Speeds => LocalizationService.Instance["Speeds"];
        public string Groups => LocalizationService.Instance["Groups"];

        #endregion
    }
}
