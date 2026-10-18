using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class Vehicle : ICloneable
    {
        /// <summary>RE-053: the open Out of Service record, set by the screen. Never sent to the server.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DTOs.OutOfServiceRecordDto? OutOfServiceRecord { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsOutOfService => OutOfServiceRecord is not null;

        [System.Text.Json.Serialization.JsonIgnore]
        public string? OutOfServiceText => OutOfServiceRecord is null ? null : Helpers.OutOfServiceUi.Describe(new[] { OutOfServiceRecord });

        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public string? VIN { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Color { get; set; }
        public int? Year { get; set; }
        public string? Plate { get; set; } // MAS Requirements
        public DateTime? ExpirationDate { get; set; } // MAS Requirements
        public bool IsInactive { get; set; }
        [Required]
        public int GroupId { get; set; }
        public VehicleGroup VehicleGroup { get; set; }
        [Required]
        public int CapacityDetailTypeId { get; set; }
        public CapacityDetailType CapacityDetailType { get; set; }
        [Required]
        public int VehicleTypeId { get; set; }
        public VehicleType VehicleType { get; set; }

        //public ICollection<VehicleRoute> VehicleRoutes { get; set; }

        public object Clone()
        {
            return this.MemberwiseClone(); 
        }

    }
}
