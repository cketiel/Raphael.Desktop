using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Travel times, distances and coordinates, from Raphael.Api rather than from Google.
    /// </summary>
    /// <remarks>
    /// This application no longer talks to Google. Everything goes through the API, which serves
    /// what it already knows and buys only what nobody has asked for yet — so one dispatcher's
    /// answer serves the next dispatcher and the driver on the same route.
    ///
    /// <para>
    /// ⚠️ Ask for a screen's legs in one call. A loop calling this once per stop is the shape of
    /// the code this replaced, and it is what made a route recalculation cost thirty requests.
    /// </para>
    /// </remarks>
    public interface IRoutingApiService
    {
        /// <summary>
        /// Prices a batch of legs. One answer per leg, in the order asked, always.
        /// </summary>
        /// <remarks>
        /// A leg the server could not price comes back with <c>Status = Unavailable</c>. Keep the
        /// value already on screen for it; do not write a zero.
        /// </remarks>
        Task<List<RouteLegResultDto>> GetLegsAsync(List<RouteLegRequestItemDto> legs);

        /// <summary>One leg. A convenience over <see cref="GetLegsAsync"/>, not a shortcut past it.</summary>
        Task<RouteLegResultDto> GetLegAsync(
            double originLat,
            double originLng,
            double destLat,
            double destLng,
            DateTime? date = null,
            TimeSpan? departureTime = null);

        /// <summary>
        /// One leg with the road's shape, for a screen that draws a map.
        /// </summary>
        /// <remarks>
        /// This is what replaced the map's own <c>DirectionsService</c>: Google stopped serving
        /// that class to projects created after March 2025, and it was a billed request the cache
        /// never saw. Now the shape comes back with the leg, cached like everything else.
        /// </remarks>
        Task<RouteLegResultDto> GetMapRouteAsync(
            double originLat,
            double originLng,
            double destLat,
            double destLng,
            DateTime? date = null,
            TimeSpan? departureTime = null);

        Task<GeocodeResultDto> GeocodeAsync(string address);

        Task<GeocodeResultDto> GeocodeAsync(string street, string city, string state, string zip);

        /// <summary>
        /// Resolves many addresses at once. What the CSV import should call: repeats inside the
        /// batch are resolved once, and the server pays only for what it has never seen.
        /// </summary>
        Task<List<GeocodeResultDto>> GeocodeBatchAsync(List<string> addresses);

        Task<string> GetCityFromCoordinatesAsync(double latitude, double longitude);

        /// <summary>
        /// The address at a point, from the cache when anyone has asked about that spot before.
        /// </summary>
        /// <remarks>
        /// What the map calls when a dispatcher drags a pin. It used to call Google itself, on
        /// every drag, with nothing remembering the answer.
        /// </remarks>
        Task<ReverseGeocodeResultDto> ReverseGeocodeAsync(double latitude, double longitude);

        /// <summary>
        /// What we already know about a Google place. Status <c>NotFound</c> means nobody has
        /// bought it yet and the caller should fetch it and hand it back.
        /// </summary>
        Task<PlaceDetailsDto> GetPlaceAsync(string placeId);

        /// <summary>Remembers a place the map had to buy, so nobody buys it twice.</summary>
        Task StorePlaceAsync(PlaceDetailsDto place);
    }
}
