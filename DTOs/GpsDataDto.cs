using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class GpsDataDto
    {
        [Required]
        public int IdVehicleRoute { get; set; }

        [Required]
        public DateTime DateTime { get; set; }

        public double Speed { get; set; }
        public string? Address { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        public string? Direction { get; set; }
    }
}
