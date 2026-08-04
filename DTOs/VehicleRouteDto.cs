using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class VehicleRouteDto
    {
        [Required(ErrorMessage = "The route name is required.")]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "You must select a driver.")]
        [Range(1, int.MaxValue, ErrorMessage = "The driver ID is not valid.")]
        public int DriverId { get; set; }

        [Required(ErrorMessage = "You must select a vehicle.")]
        [Range(1, int.MaxValue, ErrorMessage = "The vehicle ID is not valid.")]
        public int VehicleId { get; set; }

        [MaxLength(100)]
        public string? Garage { get; set; }

        [Required]
        public double GarageLatitude { get; set; }
        [Required]
        public double GarageLongitude { get; set; }

        [MaxLength(50)]
        public string? SmartphoneLogin { get; set; }

        [Required]
        public DateTime FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        [Required]
        public TimeSpan FromTime { get; set; }

        [Required]
        public TimeSpan ToTime { get; set; }

        // Collections of DTOs children. They can be null or empty if not specified.
        public List<RouteSuspensionDto>? Suspensions { get; set; }
        public List<RouteAvailabilityDto>? Availabilities { get; set; }
        public List<RouteFundingSourceDto>? FundingSources { get; set; }
    }
}
