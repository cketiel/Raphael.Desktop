using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// One row of a broker's CSV file, mapped here and stored by the server.
    /// </summary>
    /// <remarks>
    /// Mirror of <c>Raphael.Shared/DTOs/TripImportItemDto.cs</c>. Keep the two in step.
    ///
    /// <para>
    /// Names rather than foreign keys for the patient and the space type: this app cannot know
    /// the id of a patient it has not created yet, and asking would put back the round trip
    /// per row that the batch import exists to remove.
    /// </para>
    /// </remarks>
    public class TripImportItemDto
    {
        public string TripId { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public TimeSpan? FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }

        public string PickupAddress { get; set; } = string.Empty;
        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }
        public string DropoffAddress { get; set; } = string.Empty;
        public double DropoffLatitude { get; set; }
        public double DropoffLongitude { get; set; }
        public string? PickupCity { get; set; }
        public string? DropoffCity { get; set; }
        public double? Distance { get; set; }

        public string? Type { get; set; }

        /// <summary>Written by the server on creation only, never on an update.</summary>
        public bool WillCall { get; set; }

        public string? Pickup { get; set; }
        public string? Dropoff { get; set; }
        public string? PickupPhone { get; set; }
        public string? DropoffPhone { get; set; }
        public string? PickupComment { get; set; }
        public string? DropoffComment { get; set; }

        public string SpaceTypeName { get; set; } = string.Empty;
        public string? SpaceTypeDescription { get; set; }
        public string? CapacityTypeName { get; set; }

        public string? RiderId { get; set; }
        public string CustomerFullName { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public string? CustomerMobilePhone { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CustomerCity { get; set; }
        public string? CustomerState { get; set; }
        public string? CustomerZip { get; set; }
        public string? CustomerGender { get; set; }
        public DateTime? CustomerDOB { get; set; }
    }

    /// <summary>A chunk of an import: the funding source it was filed under, and its rows.</summary>
    public class TripImportRequestDto
    {
        public int FundingSourceId { get; set; }
        public List<TripImportItemDto> Items { get; set; } = new List<TripImportItemDto>();
    }

    /// <summary>What happened to one row of an imported file.</summary>
    public static class TripImportStatus
    {
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string Failed = "Failed";
    }

    /// <summary>Outcome of a single row inside an import chunk.</summary>
    public class TripImportItemResultDto
    {
        public string TripId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>Null unless the row failed.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>What is wrong with the row, in business terms. Null unless the row failed.</summary>
        public string? Message { get; set; }

        public bool? Retryable { get; set; }

        /// <summary>Key to the full server-side record of the failure, for support.</summary>
        public string? CorrelationId { get; set; }
    }

    /// <summary>Result of one import chunk.</summary>
    public class TripImportResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int FailedCount { get; set; }
        public DateTime Timestamp { get; set; }
        public List<TripImportItemResultDto> Results { get; set; } = new List<TripImportItemResultDto>();
    }
}
