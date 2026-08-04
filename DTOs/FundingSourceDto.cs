using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class FundingSourceDto
    {
        public string Name { get; set; }
        public string? AccountNumber { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? FAX { get; set; }
        public string? Email { get; set; }
        public string? ContactFirst { get; set; }
        public string? ContactLast { get; set; }
        public bool? SignaturePickup { get; set; }
        public bool? SignatureDropoff { get; set; }
        public bool? DriverSignaturePickup { get; set; }
        public bool? DriverSignatureDropoff { get; set; }
        public bool? RequireOdometer { get; set; }
        public bool? BarcodeScanRequired { get; set; }
        public bool IsActive { get; set; }
        public string? VectorcareFacilityId { get; set; }
    }
}
