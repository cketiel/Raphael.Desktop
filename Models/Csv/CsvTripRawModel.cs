using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MaterialDesignColors;

namespace Raphael.Desktop.Models.Csv
{
    public class CsvTripRawModel
    {
        public string? RideId { get; set; }
        public string? RiderId { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string? FromSt { get; set; }
        public string? FromCity { get; set; }
        public string? FromState { get; set; }
        public string? FromZIP { get; set; }
        public string? ToST { get; set; }
        public string? ToCity { get; set; }
        public string? ToState { get; set; }
        public string? ToZip { get; set; }
        public string? Date { get; set; } // Considerar DateTime si es apropiado, con TypeConverter
        public string? PickupTime { get; set; }
        public string? Appointment { get; set; }
        public string? CancelledDate { get; set; }
        public string? PatientFirstName { get; set; }
        public string? PatientLastName { get; set; }
        public string? PatientPhoneNumber { get; set; }
        public string? AlternativePhoneNumber { get; set; }
        public string? Gender { get; set; }
        public string? Treatment { get; set; }
        public string? Distance { get; set; } // Considerar double/decimal
        public string? AdditionalNotes { get; set; }
        public string? OtherDetails { get; set; }
        public string? CancelledBy { get; set; }
        public string? CancelledReasonType { get; set; }
        public string? CancelledReasonMessage { get; set; }

        // Ride2md
        public string? PatientFullName { get; set; }
        public string? PatientDOB { get; set; }
        public string? PickupLatitude { get; set; }
        public string? PickupLongitude { get; set; }
        public string? DropoffLatitude { get; set; }
        public string? DropoffLongitude { get; set; }
        public string? DropoffPhone { get; set; }
        public string? Authorization { get; set; }             
        
    }

}
