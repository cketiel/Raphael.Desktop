using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class CustomerCreateDto
    {
        [Required]
        public string FullName { get; set; }

        [Required]
        public string Address { get; set; }

        [Required]
        public string City { get; set; }

        [Required]
        public string State { get; set; }

        [Required]
        public string Zip { get; set; }

        //[Phone]
        public string? Phone { get; set; }

        //[Phone]
        public string? MobilePhone { get; set; }
        public string? ClientCode { get; set; }
        public string? PolicyNumber { get; set; }

        [EmailAddress]
        public string? Email { get; set; }
        public DateTime? DOB { get; set; }

        [Required]
        public int FundingSourceId { get; set; }

        [Required]
        public int SpaceTypeId { get; set; }

        [Required]
        public string Gender { get; set; }
        public DateTime Created { get; set; }

        [Required]
        public string CreatedBy { get; set; }

        public string? RiderId { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
