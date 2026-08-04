using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class MapPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Type { get; set; } // "Pickup" or "Dropoff"
    }
}
