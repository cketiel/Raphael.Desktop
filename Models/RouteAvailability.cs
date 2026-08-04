using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class RouteAvailability
    {
        public int Id { get; set; }

        [Required]
        public int VehicleRouteId { get; set; }
        public VehicleRoute VehicleRoute { get; set; }

        [Required]
        public DayOfWeek DayOfWeek { get; set; }

        [Required]
        [Column(TypeName = "time")]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Column(TypeName = "time")]
        public TimeSpan EndTime { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
