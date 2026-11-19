// Copy of Raphael.Shared/DTOs/TripChargesDtos.cs (RE-074). Raphael.Shared is the source of truth.
namespace Raphael.Desktop.DTOs
{
    /// <summary>A trip's charge as Home shows it: its lines, its extras and what may still be added (RE-074).</summary>
    public class TripChargesDto
    {
        public int TripId { get; set; }
        public decimal Total { get; set; }

        /// <summary>The charge is frozen: the trip finished or was cancelled. Extras can no longer change.</summary>
        public bool IsFrozen { get; set; }

        /// <summary>True when the lines come from the stored history; false when priced on the fly (before RE-073, or queued).</summary>
        public bool IsStored { get; set; }

        public List<string> Warnings { get; set; } = new();
        public List<TripChargeLineDto> Lines { get; set; } = new();
        public List<TripChargeExtraDto> Extras { get; set; } = new();

        /// <summary>The funding source's non-default rates in force on the trip's date, which may be added.</summary>
        public List<TripChargeOptionDto> Available { get; set; } = new();

        /// <summary>Display only: per-mile rates with free miles (RE-074).</summary>
        public List<TripChargeFreeNoteDto> FreeNotes { get; set; } = new();

        /// <summary>
        /// Where the trip came from: <c>Manual</c> (the office), <c>Import</c>, <c>Booking</c> (a
        /// clinic) or <c>Integration</c> (an integrator's API key). Read from its history.
        /// </summary>
        public string Origin { get; set; } = "Manual";
    }

    /// <summary>
    /// Display only (RE-074): a per-mile rate with free miles, so Home can say "15.3 mi, 7 free,
    /// 8.3 billed", or why a 6-mile trip has no Miles line.
    /// </summary>
    public class TripChargeFreeNoteDto
    {
        public int RuleId { get; set; }
        public string Description { get; set; } = "";

        /// <summary>The lower edge of the band: Free Qty, or else &gt; Min Qty.</summary>
        public decimal FreeMiles { get; set; }

        /// <summary>True when it is Free Qty; false when it is &gt; Min Qty.</summary>
        public bool IsFree { get; set; }

        public decimal TripMiles { get; set; }
        public decimal BilledMiles { get; set; }
    }

    public class TripChargeLineDto
    {
        public int? RuleId { get; set; }
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Rate { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public bool IsExtra { get; set; }

        /// <summary>The rate's procedure code and "Per", shown as before RE-074 (display only).</summary>
        public string? ProcedureCode { get; set; }
        public string? Per { get; set; }
    }

    public class TripChargeExtraDto
    {
        public int RuleId { get; set; }
        public decimal Quantity { get; set; } = 1;
    }

    public class TripChargeOptionDto
    {
        public int RuleId { get; set; }
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Rate { get; set; }
    }

    /// <summary>How many trips of a day an extra would be added to or removed from (RE-074, bulk apply).</summary>
    public class ExtraBulkPreviewDto
    {
        public int RuleId { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; } = "";
        public string FundingSourceName { get; set; } = "";
        public string SpaceTypeName { get; set; } = "";

        /// <summary>Open trips of that funding source, space type and date that would change.</summary>
        public int Trips { get; set; }

        /// <summary>Trips skipped because their charge is frozen (finished or cancelled).</summary>
        public int Frozen { get; set; }

        /// <summary>Trips that already have it (when adding) or do not (when removing).</summary>
        public int Unchanged { get; set; }

        /// <summary>The money added (or removed) in total, at one unit per trip.</summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// The last time this rate was applied to that day in one go, if it can still be undone:
        /// when, by whom, and how many open trips still carry it from that application. Undoing
        /// takes it off those trips only, never off a trip that had it added by hand.
        /// </summary>
        public DateTime? LastBatchAtUtc { get; set; }
        public string? LastBatchBy { get; set; }
        public int LastBatchTrips { get; set; }
        public decimal LastBatchAmount { get; set; }
    }
}
