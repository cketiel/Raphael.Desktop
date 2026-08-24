using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public static class TripType
    {
        public static string Appointment = "Appointment";
        public static string Return = "Return";
    }
    // Trip status changes will be stored in the TripLog table.
    public static class TripStatus
    {
        public static string Assigned = "Assigned";     // The Broker/Funding Source assigns the trip. The Router is notified.
        public static string Accepted = "Accepted";     // The Supplier accepts the trip. The Broker is notified. (Member may be notified.)
        public static string Scheduled = "Scheduled";   // The Router schedules the trip, designates a Driver and a Vehicle to carry out the trip. The Driver is notified.
        public static string Started = "Started";       // The Driver selects the trip and heads to the pick-up address. The Member waits and is notified that the Driver is on its way.
        public static string Arrived = "Arrived";       // The Driver has reached the pick-up address and is waiting for the Member to board.
        public static string Waiting = "Waiting";       // The Member reported being ready on a Will Call trip and is waiting for a vehicle to be dispatched. The office has one hour to get one there.
        public static string Late = "Late";             // The Driver is late to the pickup address with respect to the Pickup Time or is late with respect to the Appointment Time. Dispatcher is alerted. The Driver is notified.
        public static string InProgress = "InProgress"; // The Driver selects to start the trip and heads from the pick-up address to the drop-off address location. Dispatcher is notified.
        public static string Finished = "Finished";     // The Driver selects to end the trip. The Driver finishes the trip, leaving the Member at their destination. Dispatcher is notified. The Broker is notified.
        public static string Canceled = "Canceled";     // The Broker cancels the trip. All those involved in the process are alerted: the Provider, the Router, the Dispatcher, the Driver.
        public static string Billed = "Billed";         // Is when the FUNDING SOURCE was invoiced.
        public static string Payed = "Payed";           // Is when the FUNDING SOURCE paid.       
    }
    public class Trip
    {
        public int Id { get; set; }
        [Required]
        public string Day { get; set; } = string.Empty;
        [Required]
        public DateTime Date { get; set; }
        public TimeSpan? FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }
        [Required]
        public int CustomerId { get; set; }
        public Customer Customer { get; set; } = new Customer();
        [Required]
        public string PickupAddress { get; set; } = string.Empty;
        [Required]
        public double PickupLatitude { get; set; }
        [Required]
        public double PickupLongitude { get; set; }
        [Required]
        public string DropoffAddress { get; set; } = string.Empty;
        [Required]
        public double DropoffLatitude { get; set; }
        [Required]
        public double DropoffLongitude { get; set; }
        [Required]
        public int SpaceTypeId { get; set; }
        public SpaceType SpaceType { get; set; } = new SpaceType();
        public bool IsCancelled { get; set; }

        // new
        public double? Charge { get; set; }
        public double? Paid { get; set; }
        [Required]
        public string Type { get; set; } = TripType.Appointment; // (Appointment, Return)
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
        public int? VehicleRouteId { get; set; }
        public VehicleRoute? Run { get; set; }
        [Required]
        public bool WillCall { get; set; } = false;
        [Required]
        public string Status { get; set; } = TripStatus.Assigned;
        public string? DriverNoShowReason { get; set; }
        [Required]
        public DateTime Created { get; set; }

        public int? FundingSourceId { get; set; } // You have to save the Funding Source for the history. Because the Customer can change the Funding Source and the history is lost. Also to allow the Customer to not be required to have Funding Source and can make payments directly.
        public FundingSource FundingSource { get; set; } = new FundingSource();

        public ICollection<TripLog> TripLogs { get; set; }

        public string? PickupCity { get; set; } // The city for the pickup location
        public string? DropoffCity { get; set; } // The city for the dropoff location

    }
}
