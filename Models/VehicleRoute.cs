using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class VehicleRoute
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public int DriverId { get; set; }
        public User Driver { get; set; }

        [Required]
        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; }

        [MaxLength(100)]
        public string? Garage { get; set; }
        [Required]
        public double GarageLatitude { get; set; }
        [Required]
        public double GarageLongitude { get; set; }

        [MaxLength(50)]
        public string? SmartphoneLogin { get; set; }

        [Required]
        public DateTime FromDate { get; set; } = DateTime.UtcNow;

        public DateTime? ToDate { get; set; }

        [Required]
        [Column(TypeName = "time")]
        public TimeSpan FromTime { get; set; }

        [Required]
        [Column(TypeName = "time")]
        public TimeSpan ToTime { get; set; }

        //public ICollection<Schedule> Schedules { get; set; }
        public ICollection<RouteSuspension> Suspensions { get; set; }
        public ICollection<RouteAvailability> Availabilities { get; set; }
        public ICollection<RouteFundingSource> FundingSources { get; set; }
    }
}
