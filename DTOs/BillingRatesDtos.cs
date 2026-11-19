// Copy of Raphael.Shared/DTOs/BillingRatesDtos.cs (RE-072). Raphael.Shared is the source of truth.
namespace Raphael.Desktop.DTOs
{
    /// <summary>A trip to price with a funding source's rules, without saving anything (RE-072 simulator).</summary>
    public class BillingSimulationRequestDto
    {
        public int FundingSourceId { get; set; }
        public int SpaceTypeId { get; set; }
        public DateTime Date { get; set; }
        public double Miles { get; set; }
        public bool IsCancelled { get; set; }

        /// <summary>Additional charges to include (RE-074): Home previews a trip before it is saved.</summary>
        public List<TripChargeExtraDto>? Extras { get; set; }
    }

    /// <summary>What the single charge formula answers for a simulated trip.</summary>
    public class BillingSimulationResultDto
    {
        public decimal Total { get; set; }
        public List<BillingSimulationLineDto> Lines { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        /// <summary>The non-default rates in force that day, which may be added as extras (RE-074).</summary>
        public List<TripChargeOptionDto> Available { get; set; } = new();

        /// <summary>Display only: per-mile rates with free miles (RE-074).</summary>
        public List<TripChargeFreeNoteDto> FreeNotes { get; set; } = new();
    }

    public class BillingSimulationLineDto
    {
        public int RuleId { get; set; }
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

    /// <summary>Copies the rules in force on <see cref="FromDate"/> from one funding source to another.</summary>
    public class BillingRatesCopyRequestDto
    {
        public int SourceFundingSourceId { get; set; }
        public int TargetFundingSourceId { get; set; }

        /// <summary>Only these space types; empty or null means all of them.</summary>
        public List<int>? SpaceTypeIds { get; set; }

        /// <summary>The copies start on this date and keep the source rule's end date.</summary>
        public DateTime FromDate { get; set; }
    }

    public class BillingRatesCopyResultDto
    {
        public int Created { get; set; }

        /// <summary>Rules not copied because the target already has one there, in plain words.</summary>
        public List<string> Skipped { get; set; } = new();
    }

    /// <summary>A default rate that ends soon and has no period after it.</summary>
    public class BillingRateExpiringDto
    {
        public int RuleId { get; set; }
        public int FundingSourceId { get; set; }
        public string FundingSourceName { get; set; } = "";
        public string SpaceTypeName { get; set; } = "";
        public string BillingItemDescription { get; set; } = "";
        public decimal Rate { get; set; }
        public DateTime ToDate { get; set; }
    }

    /// <summary>The state of one funding source's configuration, for the list's warning mark.</summary>
    public class BillingRatesHealthDto
    {
        public int FundingSourceId { get; set; }
        public int RulesInForce { get; set; }

        /// <summary>Plain-English problems, such as "STR has no Loading Fee in force today".</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
