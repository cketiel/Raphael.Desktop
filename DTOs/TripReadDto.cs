using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class TripReadDto
    {
        /// <summary>
        /// Client-only (RE-050): true when this update means to change the Provider. The server
        /// changes it only then, and only for Raphael's admin. Never read from the server.
        /// </summary>
        public bool SetProvider { get; set; }

        public int Id { get; set; }
        public string Day { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan? FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string PickupAddress { get; set; }
        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }
        public string DropoffAddress { get; set; }
        public double DropoffLatitude { get; set; }
        public double DropoffLongitude { get; set; }
        public int SpaceTypeId { get; set; }
        public string SpaceTypeName { get; set; }

        public bool IsCancelled { get; set; }

        // new
        public double? Charge { get; set; }
        public double? Paid { get; set; }
        public string? Type { get; set; } // (Appointment, Return)
        public string? Pickup { get; set; }
        public string? PickupPhone { get; set; }
        public string? PickupComment { get; set; }
        public string? Dropoff { get; set; }
        public string? DropoffPhone { get; set; }
        public string? DropoffComment { get; set; }
        public string? TripId { get; set; } // Funding Sources / Brokers Identifier
        public string? Authorization { get; set; }
        public double? Distance { get; set; } // Distance in miles, then make unit of measurement converters class.
        public double? ETA { get; set; } // ETA in minutes, then do a class converting units of time to decimal and vice versa.
        public int VehicleRouteId { get; set; }
        public string RunName { get; set; }
        public bool WillCall { get; set; }
        public string Status { get; set; }
        public string? DriverNoShowReason { get; set; }
        [Required]
        public DateTime Created { get; set; }
        public int? FundingSourceId { get; set; }
        public string? FundingSourceName { get; set; }
        public string PickupCity { get; set; }
        public string DropoffCity { get; set; }

        // Who carries it out, and what that Provider reported (backend TripReadDto, 2026-10-11).
        // Null Provider = Raphael's own trip.
        public int? ProviderId { get; set; }
        public string? ProviderName { get; set; }
        public string? IntegratorName { get; set; }
        public string? ProviderOutcomeReason { get; set; }
    }
}
