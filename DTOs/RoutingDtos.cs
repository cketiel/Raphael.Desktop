using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // ⚠️ Hand-written mirror of Raphael.Shared/DTOs/Routing/RoutingDtos.cs. Kept in one file for
    // the same reason the original is: a contract split across eight files gets copied
    // seven-eighths of the way. If the backend file changes, this one changes in the same slice.

    /// <summary>Values the routing endpoints answer with.</summary>
    public static class RoutingContract
    {
        public static class Sources
        {
            /// <summary>Served from the server's cache. Nobody was billed.</summary>
            public const string Cache = "Cache";

            /// <summary>Bought from Google on this request.</summary>
            public const string Google = "Google";

            /// <summary>
            /// Free-flow time with our own traffic buffer added. Not Google's traffic estimate,
            /// and no screen should label it as one.
            /// </summary>
            public const string Buffered = "Buffered";
        }

        public static class Statuses
        {
            public const string Ok = "Ok";

            /// <summary>
            /// No answer for this item. ⚠️ Keep whatever value was already on screen — writing a
            /// zero here hands a driver an arrival time of "now".
            /// </summary>
            public const string Unavailable = "Unavailable";

            public const string NotFound = "NotFound";
        }
    }

    public class RouteLegRequestItemDto
    {
        public double OriginLat { get; set; }
        public double OriginLng { get; set; }
        public double DestLat { get; set; }
        public double DestLng { get; set; }

        /// <summary>
        /// Service date and departure hour of the leg, in business wall-clock time. Send them:
        /// a trip planned the evening before and priced as leaving now is a wrong answer that
        /// looks right.
        /// </summary>
        public DateTime? Date { get; set; }

        public TimeSpan? DepartureTime { get; set; }
    }

    public class RouteLegsRequestDto
    {
        public List<RouteLegRequestItemDto> Legs { get; set; } = new List<RouteLegRequestItemDto>();
    }

    public class RouteLegResultDto
    {
        public int DurationSeconds { get; set; }

        /// <summary>The duration to plan against. Null only when <see cref="Status"/> is not Ok.</summary>
        public int? DurationInTrafficSeconds { get; set; }

        public int DistanceMeters { get; set; }

        public double DistanceMiles { get; set; }

        public string Source { get; set; } = RoutingContract.Sources.Cache;

        public string Status { get; set; } = RoutingContract.Statuses.Ok;

        public bool IsUsable => Status == RoutingContract.Statuses.Ok;
    }

    public class RouteLegsResponseDto
    {
        public string TrafficMode { get; set; } = string.Empty;

        public List<RouteLegResultDto> Legs { get; set; } = new List<RouteLegResultDto>();
    }

    public class GeocodeRequestDto
    {
        public string Address { get; set; }
        public string Street { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
    }

    public class GeocodeBatchRequestDto
    {
        public List<string> Addresses { get; set; } = new List<string>();
    }

    public class GeocodeResultDto
    {
        /// <summary>The address as it was asked for, so a batch answer can be matched up.</summary>
        public string Address { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string PlaceId { get; set; }
        public string FormattedAddress { get; set; }

        public string Status { get; set; } = RoutingContract.Statuses.Ok;
        public string Source { get; set; } = RoutingContract.Sources.Cache;

        public bool IsUsable =>
            Status == RoutingContract.Statuses.Ok && Latitude.HasValue && Longitude.HasValue;
    }

    public class GeocodeBatchResponseDto
    {
        public List<GeocodeResultDto> Results { get; set; } = new List<GeocodeResultDto>();
    }

    public class ReverseGeocodeRequestDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class ReverseGeocodeResultDto
    {
        public string City { get; set; }
        public string Status { get; set; } = RoutingContract.Statuses.Ok;
        public string Source { get; set; } = RoutingContract.Sources.Cache;
    }

    public class SystemSettingDto
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }
    }

    public class SystemSettingUpdateDto
    {
        public string Value { get; set; } = string.Empty;
    }
}
