#nullable enable
using GMap.NET;
using System.Collections.Generic;

namespace Raphael.Desktop.Helpers
{
    /// <summary>Google's encoded polyline, back to points.</summary>
    public static class EncodedPolyline
    {
        public static List<PointLatLng> Decode(string? text)
        {
            var points = new List<PointLatLng>();
            if (string.IsNullOrEmpty(text)) return points;
            int i = 0, lat = 0, lng = 0;
            bool Next(ref int value)
            {
                int shift = 0, sum = 0, b;
                do
                {
                    if (i >= text.Length) return false;
                    b = text[i++] - 63;
                    sum |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);
                value += (sum & 1) != 0 ? ~(sum >> 1) : sum >> 1;
                return true;
            }
            while (i < text.Length && Next(ref lat) && Next(ref lng)) points.Add(new PointLatLng(lat / 1e5, lng / 1e5));
            return points;
        }
    }
}
