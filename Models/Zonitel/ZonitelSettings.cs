using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models.Zonitel
{
    public class ZonitelSettings
    {
        public string BaseUrl { get; set; }
        public int Version { get; set; }
        public string UserPrivateToken { get; set; }
        public string ClientId { get; set; }
        public string MilanesTransportPhone { get; set; }
    }
}
