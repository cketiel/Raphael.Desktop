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

        public int TargetSequence { get; set; }
    }
}
