using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public class EmployeesViewModel : BaseViewModel
    {
        #region Translation

        /// <summary>Roles belong to the product, not to each client company (RE-049).</summary>
        public bool IsPlatformAdmin => Helpers.SessionManager.IsPlatformAdmin;

        public string Users => LocalizationService.Instance["Users"];
        public string Roles => LocalizationService.Instance["Roles"];

        #endregion
    }
}
