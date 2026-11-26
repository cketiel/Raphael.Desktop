#nullable enable
using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // RE-094: copy of Raphael.Shared/DTOs/TripFeasibilityDtos.cs.

    /// <summary>
    /// RE-094: one trip's appointment check, for the icons and their explanation. Kinds: 2 tight,
    /// 3 impossible, 4 bad hours, 5 loose, always of the hours as received. Strength: 1 arguable, 2 strong, 3 proven.
    /// </summary>
    public class TripFeasibilityDto
    {
        public int TripId { get; set; }
        public int Kind { get; set; }
        public int Strength { get; set; }
        public TimeSpan? FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }
        public TimeSpan? OriginalFromTime { get; set; }
        public TimeSpan? OriginalToTime { get; set; }
        public bool OriginalConfirmed { get; set; }
        public int? AvailableMinutes { get; set; }
        public int BoardingMinutes { get; set; }
        public int LeadMinutes { get; set; }
        public int? DriveMinutes { get; set; }
        public double? DriveMiles { get; set; }
        public bool DriveIsPriced { get; set; }
        public DateTime? DrivePricedAtUtc { get; set; }
        public int MissingMinutes { get; set; }
        /// <summary>Only a suggestion: no screen changes the trip's hour with it.</summary>
        public TimeSpan? SuggestedFromTime { get; set; }
        public int? SuggestedDriveMinutes { get; set; }
        public TimeSpan? LatestPickup { get; set; }
        public TimeSpan? HintToTime { get; set; }
        /// <summary>0 none, 1 adjusted, 2 left by the user, 3 put back, 4 changed by hand.</summary>
        public int Adjustment { get; set; }
        public TimeSpan? AdjustedFromTime { get; set; }
        public DateTime? AdjustedAtUtc { get; set; }
        public string? AdjustedBy { get; set; }
        public bool FromFile { get; set; }
        public bool PatientNotice { get; set; }
        /// <summary>Minutes to spare of a loose trip.</summary>
        public int? SlackMinutes { get; set; }
    }

    /// <summary>RE-095: what a file has just brought, by the broker's trip numbers and its days.</summary>
    public class ImportHoursRequestDto
    {
        public List<string> TripNumbers { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }

    public class ImportHoursReviewDto
    {
        /// <summary>Trips of the file, the base of the percentages.</summary>
        public int Imported { get; set; }
        public List<ImportHoursTripDto> Trips { get; set; } = new();
    }

    public class ImportHoursTripDto
    {
        public TripFeasibilityDto Check { get; set; } = new();
        public string? TripNumber { get; set; }
        public DateTime Date { get; set; }
        public string? Patient { get; set; }
        public string? SpaceType { get; set; }
        /// <summary>An earlier import of it was adjusted.</summary>
        public bool WasAdjustedBefore { get; set; }
        /// <summary>How much the pickup would move; negative is earlier. Null when there is no hour to suggest.</summary>
        public int? MovesMinutes { get; set; }
    }

    public static class HoursActions
    {
        public const string Adjust = "ADJUST";
        public const string Decline = "DECLINE";
        public const string Undo = "UNDO";
    }

    /// <summary>RE-095: adjust, leave or put back the pickup hour of these trips.</summary>
    public class HoursAdjustRequestDto
    {
        public List<int> TripIds { get; set; } = new();
        public string Action { get; set; } = HoursActions.Adjust;
    }

    public class HoursAdjustResultDto
    {
        public int Adjusted { get; set; }
        public int Declined { get; set; }
        /// <summary>Routed meanwhile, past, or with nothing to move.</summary>
        public int Skipped { get; set; }
        public int PatientsToTell { get; set; }
    }

    /// <summary>RE-094: check hours being typed, before the trip exists. Never buys a road.</summary>
    public class FeasibilityPreviewRequestDto
    {
        /// <summary>An existing trip: its places, space type and appointment are taken from it; only the hours and type sent here are judged.</summary>
        public int? TripId { get; set; }
        public string? Type { get; set; }
        public bool WillCall { get; set; }
        public DateTime? Date { get; set; }
        public TimeSpan? FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }
        public int SpaceTypeId { get; set; }
        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }
        public double DropoffLatitude { get; set; }
        public double DropoffLongitude { get; set; }
        public double? Distance { get; set; }
    }

    /// <summary>RE-096: the report of impossible appointments.</summary>
    public class ImpossibleAppointmentsReportDto
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        /// <summary>Appointments not cancelled in the range, the base of every percentage.</summary>
        public int Appointments { get; set; }
        /// <summary>Of those, how many have been checked.</summary>
        public int Checked { get; set; }
        public List<ImpossibleFundingSourceDto> FundingSources { get; set; } = new();
        public List<ImpossibleAppointmentDto> Trips { get; set; } = new();
    }

    public class ImpossibleFundingSourceDto
    {
        public int FundingSourceId { get; set; }
        public string FundingSource { get; set; } = "";
        public int Appointments { get; set; }
        public int Impossible { get; set; }
        public int BadHours { get; set; }
        public int Loose { get; set; }
        public int Adjusted { get; set; }
        /// <summary>Those the report would put in a claim: proven by facts or by calculation.</summary>
        public int Claimable { get; set; }
        public int ArrivedLate { get; set; }
    }

    public class ImpossibleAppointmentDto
    {
        public TripFeasibilityDto Check { get; set; } = new();
        /// <summary>FACTS, CALCULATION, ARGUABLE, DO_NOT_CLAIM, LOOSE or MANUAL (typed in the office: nothing to claim).</summary>
        public string Verdict { get; set; } = "";
        public string? TripNumber { get; set; }
        public string? Authorization { get; set; }
        public DateTime Date { get; set; }
        public int FundingSourceId { get; set; }
        public string? FundingSource { get; set; }
        public string? SpaceType { get; set; }
        public string? Patient { get; set; }
        public string? Status { get; set; }
        public bool IsCancelled { get; set; }
        public string PickupAddress { get; set; } = "";
        public string DropoffAddress { get; set; } = "";
        public double? Distance { get; set; }
        public DateTime? CreatedUtc { get; set; }
        /// <summary>INTEGRATION, FILE, PROVIDER, MANUAL or UNKNOWN (before 2026-09-05 a file and the office cannot be told apart).</summary>
        public string Source { get; set; } = "";
        public string? Line { get; set; }
        public string? Driver { get; set; }
        public string? Vehicle { get; set; }
        public TimeSpan? PickupArrived { get; set; }
        public TimeSpan? PickupPerformed { get; set; }
        public TimeSpan? DropoffArrived { get; set; }
        public TimeSpan? DropoffPerformed { get; set; }
        public string? PickupGps { get; set; }
        public string? DropoffGps { get; set; }
    }
}
