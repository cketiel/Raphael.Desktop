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
            // 'Buffered' used to live here and made this field unusable: in MaxSavings mode every
            // answer read Buffered whether it had been bought or cached, so nothing could tell
            // what had been paid for. It is now RouteLegResultDto.Buffered, a separate flag.
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

        /// <summary>
        /// Ask for the road's shape too, so a map can draw it. Only the map screens set this.
        /// </summary>
        public bool IncludePolyline { get; set; }
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

        /// <summary>
        /// The road's shape, encoded, when it was asked for. This is what the map draws now that
        /// the JavaScript <c>DirectionsService</c> is gone.
        /// </summary>
        public string EncodedPolyline { get; set; }

        /// <summary>Cache or Google: was anybody billed for this.</summary>
        public string Source { get; set; } = RoutingContract.Sources.Cache;

        /// <summary>
        /// True when the planning duration is our own free-flow-plus-margin figure rather than a
        /// traffic estimate from Google. Independent of <see cref="Source"/>.
        /// </summary>
        public bool Buffered { get; set; }

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

        /// <summary>The whole line as Google prints it, for the address box on the map.</summary>
        public string FormattedAddress { get; set; }

        public string Street { get; set; }

        public string State { get; set; }

        public string Zip { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public string Status { get; set; } = RoutingContract.Statuses.Ok;

        public string Source { get; set; } = RoutingContract.Sources.Cache;
    }

    /// <summary>
    /// A Google place, cached by the server so it is bought once for everybody.
    /// </summary>
    /// <remarks>
    /// The browser key is the only one with Places enabled, so the map stays the caller — but it
    /// asks the server first, and hands back whatever it had to buy.
    /// </remarks>
    public class PlaceDetailsDto
    {
        public string PlaceId { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public string FormattedAddress { get; set; }

        public string Street { get; set; }

        public string City { get; set; }

        public string State { get; set; }

        public string Zip { get; set; }

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
