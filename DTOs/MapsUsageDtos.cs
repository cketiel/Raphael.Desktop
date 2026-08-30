using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// Hand-written copy of <c>Raphael.Shared/DTOs/Routing/MapsUsageDtos.cs</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ If the backend's copy changes, this one has to change with it. Nothing in the compiler
    /// will tell you: a renamed property simply arrives as null and the panel shows a zero, which
    /// is the most expensive kind of wrong for a screen whose whole job is to be believed.
    /// </remarks>
    public static class MapsUsageContract
    {
        public static class Skus
        {
            public const string RoutesEssentials = "RoutesEssentials";
            public const string RoutesPro = "RoutesPro";
            public const string Geocoding = "Geocoding";
            public const string DynamicMaps = "DynamicMaps";
            public const string PlacesAutocomplete = "PlacesAutocomplete";
            public const string PlaceDetails = "PlaceDetails";
        }
    }

    public class MapsSkuUsageDto
    {
        public string Sku { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public long Billed { get; set; }

        public long Cached { get; set; }

        /// <summary>Counted from a client's report rather than measured on the server.</summary>
        public bool ReportedByClient { get; set; }

        public decimal EstimatedCost { get; set; }

        public decimal AvoidedCost { get; set; }

        public int FreeCapPerMonth { get; set; }

        public long FreeRemainingThisMonth { get; set; }

        public long Total => Billed + Cached;

        /// <summary>Share the cache answered, as a percentage for display.</summary>
        public double CacheHitPercent => Total == 0 ? 0 : (double)Cached / Total * 100;
    }

    public class MapsUsageSummaryDto
    {
        public DateTime From { get; set; }

        public DateTime To { get; set; }

        public long TotalBilled { get; set; }

        public long TotalCached { get; set; }

        public double CacheHitRate { get; set; }

        public decimal EstimatedCost { get; set; }

        public decimal AvoidedCost { get; set; }

        public decimal? ProjectedMonthCost { get; set; }

        public List<MapsSkuUsageDto> BySku { get; set; } = new List<MapsSkuUsageDto>();
    }

    public class MapsUsagePointDto
    {
        public DateTime Day { get; set; }

        public string Sku { get; set; } = string.Empty;

        public long Billed { get; set; }

        public long Cached { get; set; }
    }

    public class MapsUsageTotalsDto
    {
        public long Billed { get; set; }

        public long Cached { get; set; }

        public double CacheHitRate { get; set; }

        public DateTime? FirstDay { get; set; }

        public DateTime? LastDay { get; set; }
    }

    public class MapsPricingTierDto
    {
        public int Id { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public int FreeCapPerMonth { get; set; }

        public int FromRequest { get; set; }

        public int? ToRequest { get; set; }

        public decimal PricePerThousand { get; set; }

        /// <summary>The band as a person reads it: "10.001 – 100.000" or "5.000.001 y más".</summary>
        public string Band => ToRequest.HasValue
            ? $"{FromRequest:N0} – {ToRequest.Value:N0}"
            : $"{FromRequest:N0} +";
    }

    public class MapsUsageReportDto
    {
        public List<MapsUsageReportItemDto> Items { get; set; } = new List<MapsUsageReportItemDto>();
    }

    public class MapsUsageReportItemDto
    {
        public string Sku { get; set; } = string.Empty;

        public int Count { get; set; } = 1;
    }

    // SystemSettingDto and SystemSettingUpdateDto are already mirrored in RoutingDtos.cs and are
    // deliberately not repeated here.
}
