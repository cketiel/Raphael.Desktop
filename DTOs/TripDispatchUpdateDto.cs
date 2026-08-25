using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class TripDispatchUpdateDto
    {
        public string Type { get; set; }
        public TimeSpan? FromTime { get; set; }

        /// <summary>
        /// ⚠️ Ignored by the server on this route, and kept only so the two hand-copied
        /// versions of this DTO stay identical (see <c>_meta/CONTRACT_MAP.md</c>).
        /// The only writers of <c>Trip.WillCall</c> are the two Will Call endpoints.
        /// </summary>
        public bool WillCall { get; set; }
        public string? PickupPhone { get; set; }
        public string? PickupComment { get; set; }
        public string? DropoffPhone { get; set; }
        public string? DropoffComment { get; set; }
    }
}
