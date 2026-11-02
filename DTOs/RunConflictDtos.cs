using System;
using System.Collections.Generic;

// RE-063. Hand copy of Raphael.Shared/DTOs/RunConflictDtos.cs: keep the two in step.
namespace Raphael.Desktop.DTOs
{
    /// <summary>The route about to be saved, asked about before saving.</summary>
    public class RunConflictQueryDto
    {
        public int? RouteId { get; set; }
        public int DriverId { get; set; }
        public string SmartphoneLogin { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    /// <summary>A route in the way, from today on, with what can be done about it.</summary>
    public class RunConflictDto
    {
        /// <summary><c>driver</c> or <c>login</c>.</summary>
        public string Kind { get; set; }
        public int RouteId { get; set; }
        public string RouteName { get; set; }
        public int DriverId { get; set; }
        public string DriverName { get; set; }
        public int VehicleId { get; set; }
        public string VehicleName { get; set; }
        public string SmartphoneLogin { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public DateTime SuggestedEndDate { get; set; }
        public int TripStopsAfterSuggestedEnd { get; set; }
        public DateTime? FirstTripStopAfterSuggestedEnd { get; set; }
        public bool CanEnd { get; set; }
        public bool CanEndToday { get; set; }
        public int PendingTripStopsToday { get; set; }
        public bool CanCoverToday { get; set; }
    }

    public class RunEndRequestDto
    {
        public DateTime EndDate { get; set; }
    }
}
