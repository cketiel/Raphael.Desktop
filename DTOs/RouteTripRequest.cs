using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    // For routing request
    public class RouteTripRequest
    {
        [Required]
        public int VehicleRouteId { get; set; }
        public string? VehicleRouteName { get; set; }

        [Required]
        public int TripId { get; set; }

        // --- Data for the PICKUP event ---
        [Required]
        public double PickupDistance { get; set; }

        [Required]
        public TimeSpan PickupTravelTime { get; set; } // "hh:mm:ss" format

        [Required]
        public TimeSpan PickupETA { get; set; } // "hh:mm" format


        // --- Data for the DROPOFF event ---
        [Required]
        public double DropoffDistance { get; set; }

        [Required]
        public TimeSpan DropoffTravelTime { get; set; } // "hh:mm:ss" format

        [Required]
        public TimeSpan DropoffETA { get; set; } // "hh:mm" format

        /// <summary>
        /// Travel from the dropoff back to the garage. Feeds the Pull-in hour.
        /// </summary>
        /// <remarks>
        /// Before this field existed the Pull-in was built from <see cref="DropoffTravelTime"/>,
        /// which is the pickup-to-dropoff leg: the vehicle's return to the garage was being
        /// charged the duration of the trip it had just finished, a second time. On a long
        /// trip that pushed the Pull-in past midnight, and a time-of-day column cannot hold
        /// that hour.
        ///
        /// <para>
        /// Zero when the caller does not send it, which is what an older Desktop does. The
        /// Pull-in then lands on the dropoff hour: too early, but a real hour, and the
        /// dispatcher's recalculation replaces it with the measured leg straight after.
        /// </para>
        /// </remarks>
        public TimeSpan ReturnToGarageTravelTime { get; set; }

        public int TargetSequence { get; set; }
    }
}
