using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Travel times and coordinates. Despite the name, this no longer talks to Google.
    /// </summary>
    /// <remarks>
    /// It used to: a static HttpClient, the API key out of a versioned <c>appsettings.json</c>, and
    /// <c>departure_time=now&amp;traffic_model=best_guess</c> on every request — the expensive
    /// billing tier, asked afresh every time, for a trip that was usually being planned the evening
    /// before. Now it forwards to <see cref="IRoutingApiService"/>, which asks Raphael.Api, which
    /// answers from a cache shared by every dispatcher and driver.
    ///
    /// <para>
    /// The class and its method names are kept so the call sites did not all have to change at
    /// once. New code should use <see cref="IRoutingApiService"/> directly — it takes the departure
    /// time, which is the whole difference between an estimate and a guess, and it can price a
    /// screen's legs in one request.
    /// </para>
    /// </remarks>
    public class GoogleMapsService
    {
        private readonly IRoutingApiService _routing;

        public GoogleMapsService()
            : this(new RoutingApiService())
        {
        }

        public GoogleMapsService(IRoutingApiService routing)
        {
            _routing = routing;
        }

        /// <summary>The town a point sits in, or null.</summary>
        public Task<string> GetCityFromCoordinates(double latitude, double longitude) =>
            _routing.GetCityFromCoordinatesAsync(latitude, longitude);

        /// <summary>
        /// Time and distance for one leg.
        /// </summary>
        /// <remarks>
        /// ⚠️ Returns null when the leg could not be priced, where the old version threw a
        /// <c>NullReferenceException</c> from inside its own parsing. Callers that already guard
        /// with <c>if (details == null)</c> — most of them do — now have that guard actually run.
        ///
        /// <para>
        /// No departure time, so the answer is priced as leaving now. Prefer
        /// <see cref="IRoutingApiService.GetLegAsync"/> with the trip's scheduled hour, and prefer
        /// the batch call when a screen needs more than one leg.
        /// </para>
        /// </remarks>
        public async Task<RouteFullDetail> GetRouteFullDetails(
            double originLat,
            double originLng,
            double destLat,
            double destLng)
        {
            var leg = await _routing.GetLegAsync(originLat, originLng, destLat, destLng);

            return RouteFullDetail.From(leg);
        }

        /// <summary>Coordinates for a whole address line.</summary>
        public async Task<Coordinates> GetCoordinatesFromAddress(string address)
        {
            var result = await _routing.GeocodeAsync(address);

            return result.IsUsable
                ? new Coordinates { Latitude = result.Latitude.Value, Longitude = result.Longitude.Value }
                : null;
        }

        /// <summary>Coordinates for an address given in parts.</summary>
        public async Task<Coordinates> GetCoordinates(string street, string city, string state, string zip)
        {
            var result = await _routing.GeocodeAsync(street, city, state, zip);

            return result.IsUsable
                ? new Coordinates { Latitude = result.Latitude.Value, Longitude = result.Longitude.Value }
                : null;
        }
    }

    /// <summary>Time and distance for one leg, in the shape the older screens expect.</summary>
    public class RouteFullDetail
    {
        public double DistanceMiles { get; set; }
        public double DurationMinutes { get; set; }
        public double DurationInTrafficMinutes { get; set; }
        public double DistanceMeters { get; set; }
        public double DurationSeconds { get; set; }
        public double DurationInTrafficSeconds { get; set; }

        public string DistanceString { get; set; } = "";
        public string DurationString { get; set; } = "";
        public string DurationInTrafficString { get; set; } = "";

        /// <summary>
        /// Builds one from an API answer, or null when the leg could not be priced.
        /// </summary>
        /// <remarks>
        /// The numbers come from seconds and metres and are converted here. The old version parsed
        /// Google's display strings — <c>"12.3 mi"</c> with the machine's locale, so a Spanish
        /// Windows read it as 123 miles, and <c>"1 hour 5 mins"</c> became one minute.
        /// </remarks>
        public static RouteFullDetail From(DTOs.RouteLegResultDto leg)
        {
            if (leg == null || !leg.IsUsable) return null;

            var trafficSeconds = leg.DurationInTrafficSeconds ?? leg.DurationSeconds;

            return new RouteFullDetail
            {
                DistanceMeters = leg.DistanceMeters,
                DistanceMiles = leg.DistanceMiles,
                DurationSeconds = leg.DurationSeconds,
                DurationInTrafficSeconds = trafficSeconds,
                DurationMinutes = Math.Round(leg.DurationSeconds / 60.0, 1),
                DurationInTrafficMinutes = Math.Round(trafficSeconds / 60.0, 1),
                DistanceString = $"{leg.DistanceMiles:0.0} mi",
                DurationString = Humanize(leg.DurationSeconds),
                DurationInTrafficString = Humanize(trafficSeconds)
            };
        }

        private static string Humanize(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);

            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours} h {span.Minutes} min"
                : $"{span.Minutes} min";
        }
    }

    public class Coordinates
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
