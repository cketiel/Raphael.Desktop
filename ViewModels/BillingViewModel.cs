using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public class BillingViewModel : BaseViewModel
    {
        #region Translation

        public string BillingItems => LocalizationService.Instance["BillingItems"];
        public string FundingSources => LocalizationService.Instance["FundingSources"];

        #endregion
    }
}
