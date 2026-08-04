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
