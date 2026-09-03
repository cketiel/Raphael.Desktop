using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class MapPoint : Helpers.Maps.IMapMarker
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Type { get; set; } // "Pickup" or "Dropoff"

        // --- IMapMarker. A selected trip only ever puts two points on the map, its pickup and
        // its dropoff, and they are never stacked on one another: no offset to apply.
        double Helpers.Maps.IMapMarker.MarkerLatitude => Latitude;
        double Helpers.Maps.IMapMarker.MarkerLongitude => Longitude;
        int Helpers.Maps.IMapMarker.MarkerOffsetIndex => 0;
    }
}
