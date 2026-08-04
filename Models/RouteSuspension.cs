using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class RouteSuspension
    {
        public int Id { get; set; }

        [Required]
        public int VehicleRouteId { get; set; }
        public VehicleRoute VehicleRoute { get; set; }

        [Required]
        public DateTime SuspensionStart { get; set; }

        [Required]
        public DateTime SuspensionEnd { get; set; }

        [MaxLength(200)]
        public string? Reason { get; set; }
    }
}
