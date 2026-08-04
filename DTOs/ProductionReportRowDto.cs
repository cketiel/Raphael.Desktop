using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class ChargeLineDto
    {
        public string ChargeName { get; set; }
        public double Quantity { get; set; }
        public double Rate { get; set; }
        public double Amount => Quantity * Rate;
    }
    public class ProductionReportRowDto
    {
        public DateTime Date { get; set; }
        public TimeSpan? ReqPickup { get; set; }
        public TimeSpan? Appointment { get; set; }
        public string? Patient { get; set; }
        public string? PickupAddress { get; set; }
        public string? DropoffAddress { get; set; }
        public string? Space { get; set; }
        public double? Charge { get; set; }
        public double? Paid { get; set; }
        public string? PickupComment { get; set; }
        public string? DropoffComment { get; set; }
        public string? Type { get; set; }
        public string? PickupPhone { get; set; }
        public string? DropoffPhone { get; set; }
        public string? Authorization { get; set; }
        public string? FundingSource { get; set; }
        public double? Distance { get; set; }
        public string? Run { get; set; }
        public string? Driver { get; set; }
        public TimeSpan? PickupArrive { get; set; }
        public TimeSpan? PickupPerform { get; set; }
        public TimeSpan? DropoffArrive { get; set; }
        public TimeSpan? DropoffPerform { get; set; }
        public bool WillCall { get; set; }
        public bool Canceled { get; set; }
        public string? VIN { get; set; }
        public long? PickupOdometer { get; set; }
        public long? DropoffOdometer { get; set; }
        public TimeSpan? WillCallTime { get; set; }
        public string? Vehicle { get; set; }
        public string? VehiclePlate { get; set; }
        public string? TripId { get; set; }
        public double? PickupGpsArriveDistance { get; set; }
        public double? DropoffGpsArriveDistance { get; set; }
        public string? PickupCity { get; set; }
        public string? PickupState { get; set; }
        public string? PickupZip { get; set; }
        public string? DropoffCity { get; set; }
        public string? DropoffState { get; set; }
        public string? DropoffZip { get; set; }
        public string? PatientAddress { get; set; }
        public DateTime? DOB { get; set; }
        public string? DriverNoShowReason { get; set; }
        public double PickupLat { get; set; }
        public double PickupLon { get; set; }
        public double DropoffLat { get; set; }
        public double DropoffLon { get; set; }
        public DateTime Created { get; set; }

        public byte[]? PickupSignature { get; set; }
        public List<ChargeLineDto> BillableLines { get; set; } = new List<ChargeLineDto>();
        public double TotalTripAmount => BillableLines.Sum(x => x.Amount);
    }

    /*public class ProductionReportRowDto
    {
        public DateTime Date { get; set; }
        public string? Authorization { get; set; }
        public TimeSpan? ReqPickup { get; set; }
        public TimeSpan? Appointment { get; set; }
        public string? Patient { get; set; }
        public string? PickupCity { get; set; }
        public string? Run { get; set; }
        public string? Space { get; set; }
        public TimeSpan? PickupArrive { get; set; }
        public TimeSpan? DropoffArrive { get; set; }
    }*/
}
