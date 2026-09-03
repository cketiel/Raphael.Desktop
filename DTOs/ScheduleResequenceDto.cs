using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // Mirror of Raphael.Shared/DTOs/ScheduleResequenceDto.cs. Copied by hand, like every other
    // DTO here; if the server's copy gains a field, this one has to gain it too.

    /// <summary>
    /// One stop's place in the route, and what the router worked out for it.
    /// </summary>
    public class ScheduleStopSequenceDto
    {
        public int Id { get; set; }

        public int? Sequence { get; set; }

        public TimeSpan? ETA { get; set; }

        public TimeSpan? Travel { get; set; }

        public double? Distance { get; set; }
    }

    /// <summary>
    /// A whole route's new order, sent in one piece.
    /// </summary>
    /// <remarks>
    /// Dragging one stop renumbers every stop after it, and each of those used to be its own
    /// PUT against a server on the internet: the dispatcher watched the grid sit still for a
    /// round trip per stop. The route and the date travel with the request because the server
    /// checks the stops against them rather than trusting the ids.
    /// </remarks>
    public class ScheduleResequenceRequest
    {
        public int VehicleRouteId { get; set; }

        public DateTime Date { get; set; }

        public List<ScheduleStopSequenceDto> Stops { get; set; } = new();
    }
}
