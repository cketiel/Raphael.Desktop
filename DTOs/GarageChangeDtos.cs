using System;
using System.Collections.Generic;

// RE-052. Hand copy of Raphael.Shared/DTOs/GarageChangeDtos.cs: keep the two in step.
namespace Raphael.Desktop.DTOs
{
    /// <summary>The garage a route is about to be given (RE-052).</summary>
    public class GarageChangeRequestDto
    {
        public string? Garage { get; set; }
        public double GarageLatitude { get; set; }
        public double GarageLongitude { get; set; }
    }

    /// <summary>
    /// What changing a route's garage does to the days already planned on it (RE-052).
    /// </summary>
    /// <remarks>
    /// Answered by the preview and again, as it was really applied, by the save: the Desktop shows
    /// the first before asking, and the second only when it differs.
    /// </remarks>
    public class GarageChangePreviewDto
    {
        /// <summary>False when the address and coordinates are the ones the route already has.</summary>
        public bool GarageChanged { get; set; }

        /// <summary>Planned days from today on, one entry each, in date order.</summary>
        public List<GarageChangeDayDto> Days { get; set; } = new();

        /// <summary>Past days with stops on this route. They are history and are never touched.</summary>
        public int PastDaysUntouched { get; set; }

        /// <summary>
        /// The state of the route and its planned days that this answer was worked out from. Sent
        /// back with the save (header <c>X-Garage-Preview</c>): if anything moved in between, the
        /// save is refused untouched rather than applying something nobody confirmed.
        /// </summary>
        public string? Fingerprint { get; set; }
    }

    public class GarageChangeDayDto
    {
        public DateTime Date { get; set; }
        public bool IsToday { get; set; }

        /// <summary>The vehicle has left the garage: the Pull-out is history and keeps its address.</summary>
        public bool PullOutAlreadyLeft { get; set; }

        /// <summary>The vehicle is back: nothing on this day changes.</summary>
        public bool DayFinished { get; set; }

        public bool PullOutUpdated { get; set; }
        public bool PullInUpdated { get; set; }

        public TimeSpan? PullOutEtaBefore { get; set; }
        public TimeSpan? PullOutEtaAfter { get; set; }
        public TimeSpan? PullInEtaBefore { get; set; }
        public TimeSpan? PullInEtaAfter { get; set; }

        /// <summary>The drive from the garage to the first stop, before and after.</summary>
        public TimeSpan? FirstLegBefore { get; set; }
        public TimeSpan? FirstLegAfter { get; set; }

        /// <summary>The drive from the last stop back to the garage, before and after.</summary>
        public TimeSpan? LastLegBefore { get; set; }
        public TimeSpan? LastLegAfter { get; set; }

        /// <summary>Stops other than the garage whose arrival hour or wait moves.</summary>
        public List<GarageChangeStopDto> ChangedStops { get; set; } = new();
    }

    public class GarageChangeStopDto
    {
        public int ScheduleId { get; set; }
        public string? Name { get; set; }
        public TimeSpan? EtaBefore { get; set; }
        public TimeSpan? EtaAfter { get; set; }
        public TimeSpan? WaitBefore { get; set; }
        public TimeSpan? WaitAfter { get; set; }
    }
}
