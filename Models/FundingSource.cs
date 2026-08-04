using Raphael.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class FundingSource: BaseViewModel
    {
        public int Id { get; set; }
        [Required]
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
        public string? VectorcareFacilityId { get; set; }
        [Required]
        public bool IsActive { get; set; } = true;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public ICollection<Customer> Customers { get; set; }
        public ICollection<FundingSourceBillingItem> BillingItems { get; set; }
    }
}
