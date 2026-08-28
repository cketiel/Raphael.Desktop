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

        Task<GeocodeResultDto> GeocodeAsync(string address);

        Task<GeocodeResultDto> GeocodeAsync(string street, string city, string state, string zip);

        /// <summary>
        /// Resolves many addresses at once. What the CSV import should call: repeats inside the
        /// batch are resolved once, and the server pays only for what it has never seen.
        /// </summary>
        Task<List<GeocodeResultDto>> GeocodeBatchAsync(List<string> addresses);

        Task<string> GetCityFromCoordinatesAsync(double latitude, double longitude);
    }
}
