using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models.Zonitel
{
    public class SmsRequest
    {
        public string from { get; set; }
        public string to { get; set; }
        public string text { get; set; }
    }
}
