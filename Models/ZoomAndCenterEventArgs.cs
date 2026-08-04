using GMap.NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class ZoomAndCenterEventArgs : EventArgs
    {
        public RectLatLng BoundingBox { get; }

        public ZoomAndCenterEventArgs(RectLatLng boundingBox)
        {
            BoundingBox = boundingBox;
        }
    }
}
