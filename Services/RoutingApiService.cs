using Raphael.Desktop.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <inheritdoc cref="IRoutingApiService"/>
    public class RoutingApiService : IRoutingApiService
    {
        private const string EndPoint = "routing";

        /// <summary>
        /// A leg already priced in this session is not asked for again for a quarter of an hour.
        /// </summary>
        /// <remarks>
        /// This is not the cache that saves money — that one is in SQL Server, shared by everyone.
        /// This one saves the round trip, so a screen that redraws while the dispatcher drags a
        /// stop around does not send the same question to the API twenty times a minute. Short on
        /// purpose: fifteen minutes is well inside one dispatcher's sitting, and well short of the
        /// day drifting into a different traffic hour.
        /// </remarks>
        private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromMinutes(15);

        private static readonly ConcurrentDictionary<string, (DateTime StoredAt, RouteLegResultDto Result)>
            SessionCache = new ConcurrentDictionary<string, (DateTime, RouteLegResultDto)>();

        private readonly HttpClient _httpClient;

        public RoutingApiService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<RouteLegResultDto>> GetLegsAsync(List<RouteLegRequestItemDto> legs)
        {
            var answers = new RouteLegResultDto[legs?.Count ?? 0];

            if (legs == null || legs.Count == 0) return answers.ToList();

            var toAsk = new List<RouteLegRequestItemDto>();
            var positions = new List<int>();

            for (int i = 0; i < legs.Count; i++)
            {
                if (TryGetRemembered(legs[i], out var remembered))
                {
                    answers[i] = remembered;
                }
                else
                {
                    toAsk.Add(legs[i]);
                    positions.Add(i);
                }
            }

            if (toAsk.Count > 0)
            {
                var fetched = await PostLegsAsync(toAsk);

                for (int i = 0; i < positions.Count; i++)
                {
                    var result = i < fetched.Count ? fetched[i] : Unavailable();

                    answers[positions[i]] = result;

                    if (result.IsUsable) Remember(toAsk[i], result);
                }
            }

            // Nothing may be left null: a caller reading a list one shorter than it asked for
            // would silently pair the wrong duration with the wrong stop.
            for (int i = 0; i < answers.Length; i++)
            {
                answers[i] ??= Unavailable();
            }

            return answers.ToList();
        }

        public async Task<RouteLegResultDto> GetLegAsync(
            double originLat,
            double originLng,
            double destLat,
            double destLng,
            DateTime? date = null,
            TimeSpan? departureTime = null)
        {
            var results = await GetLegsAsync(new List<RouteLegRequestItemDto>
            {
                new RouteLegRequestItemDto
                {
                    OriginLat = originLat,
                    OriginLng = originLng,
                    DestLat = destLat,
                    DestLng = destLng,
                    Date = date,
                    DepartureTime = departureTime
                }
            });

            return results.Count > 0 ? results[0] : Unavailable();
        }

        public async Task<RouteLegResultDto> GetMapRouteAsync(
            double originLat,
            double originLng,
            double destLat,
            double destLng,
            DateTime? date = null,
            TimeSpan? departureTime = null)
        {
            var results = await GetLegsAsync(new List<RouteLegRequestItemDto>
            {
                new RouteLegRequestItemDto
                {
                    OriginLat = originLat,
                    OriginLng = originLng,
                    DestLat = destLat,
                    DestLng = destLng,
                    Date = date,
                    DepartureTime = departureTime,
                    IncludePolyline = true
                }
            });

            return results.Count > 0 ? results[0] : Unavailable();
        }

        public async Task<GeocodeResultDto> GeocodeAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return new GeocodeResultDto { Status = RoutingContract.Statuses.NotFound };
            }

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{EndPoint}/geocode",
                    new GeocodeRequestDto { Address = address });

                if (!response.IsSuccessStatusCode)
                {
                    return new GeocodeResultDto
                    {
                        Address = address,
                        Status = RoutingContract.Statuses.Unavailable
                    };
                }

                return await response.Content.ReadFromJsonAsync<GeocodeResultDto>()
                    ?? new GeocodeResultDto
                    {
                        Address = address,
                        Status = RoutingContract.Statuses.Unavailable
                    };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Geocode request failed: {ex.Message}");

                return new GeocodeResultDto
                {
                    Address = address,
                    Status = RoutingContract.Statuses.Unavailable
                };
            }
        }

        public Task<GeocodeResultDto> GeocodeAsync(string street, string city, string state, string zip)
        {
            var parts = new[] { street, city, state, zip }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim());

            return GeocodeAsync(string.Join(", ", parts));
        }

        public async Task<List<GeocodeResultDto>> GeocodeBatchAsync(List<string> addresses)
        {
            if (addresses == null || addresses.Count == 0) return new List<GeocodeResultDto>();

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{EndPoint}/geocode/batch",
                    new GeocodeBatchRequestDto { Addresses = addresses });

                if (!response.IsSuccessStatusCode)
                {
                    return addresses
                        .Select(a => new GeocodeResultDto
                        {
                            Address = a,
                            Status = RoutingContract.Statuses.Unavailable
                        })
                        .ToList();
                }

                var payload = await response.Content.ReadFromJsonAsync<GeocodeBatchResponseDto>();

                return payload?.Results ?? new List<GeocodeResultDto>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Geocode batch request failed: {ex.Message}");

                return addresses
                    .Select(a => new GeocodeResultDto
                    {
                        Address = a,
                        Status = RoutingContract.Statuses.Unavailable
                    })
                    .ToList();
            }
        }

        public async Task<ReverseGeocodeResultDto> ReverseGeocodeAsync(double latitude, double longitude)
        {
            if (latitude == 0 && longitude == 0) return null;

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{EndPoint}/reverse-geocode",
                    new ReverseGeocodeRequestDto { Latitude = latitude, Longitude = longitude });

                if (!response.IsSuccessStatusCode) return null;

                return await response.Content.ReadFromJsonAsync<ReverseGeocodeResultDto>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reverse geocode request failed: {ex.Message}");

                return null;
            }
        }

        public async Task<PlaceDetailsDto> GetPlaceAsync(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId)) return null;

            try
            {
                var response = await _httpClient.GetAsync(
                    $"{EndPoint}/place/{Uri.EscapeDataString(placeId)}");

                if (!response.IsSuccessStatusCode) return null;

                return await response.Content.ReadFromJsonAsync<PlaceDetailsDto>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Place lookup failed: {ex.Message}");

                return null;
            }
        }

        public async Task StorePlaceAsync(PlaceDetailsDto place)
        {
            if (place == null || string.IsNullOrWhiteSpace(place.PlaceId)) return;

            try
            {
                await _httpClient.PostAsJsonAsync($"{EndPoint}/place", place);
            }
            catch (Exception ex)
            {
                // A place we failed to remember gets bought again next time. That is the old
                // behaviour, and not worth interrupting a dispatcher over.
                System.Diagnostics.Debug.WriteLine($"Could not store a place: {ex.Message}");
            }
        }

        public async Task<string> GetCityFromCoordinatesAsync(double latitude, double longitude)
        {
            if (latitude == 0 && longitude == 0) return null;

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{EndPoint}/reverse-geocode",
                    new ReverseGeocodeRequestDto { Latitude = latitude, Longitude = longitude });

                if (!response.IsSuccessStatusCode) return null;

                var payload = await response.Content.ReadFromJsonAsync<ReverseGeocodeResultDto>();

                return payload?.City;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reverse geocode request failed: {ex.Message}");

                return null;
            }
        }

        private async Task<List<RouteLegResultDto>> PostLegsAsync(List<RouteLegRequestItemDto> legs)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{EndPoint}/legs",
                    new RouteLegsRequestDto { Legs = legs });

                if (!response.IsSuccessStatusCode)
                {
                    return legs.Select(_ => Unavailable()).ToList();
                }

                var payload = await response.Content.ReadFromJsonAsync<RouteLegsResponseDto>();

                return payload?.Legs ?? legs.Select(_ => Unavailable()).ToList();
            }
            catch (Exception ex)
            {
                // A dispatcher without a network keeps the times already on the grid. Throwing
                // here would take down the whole schedule view over one unreachable leg.
                System.Diagnostics.Debug.WriteLine($"Routing request failed: {ex.Message}");

                return legs.Select(_ => Unavailable()).ToList();
            }
        }

        private static RouteLegResultDto Unavailable() =>
            new RouteLegResultDto { Status = RoutingContract.Statuses.Unavailable };

        /// <summary>
        /// The same key shape the server uses: coordinates to four decimals, plus the departure
        /// hour. Rounding differently here would remember a leg the server would price separately.
        /// </summary>
        private static string KeyOf(RouteLegRequestItemDto leg)
        {
            var hour = leg.Date.HasValue
                ? ((leg.DepartureTime ?? TimeSpan.Zero).Hours).ToString()
                : "now";

            var day = leg.Date?.DayOfWeek.ToString() ?? "-";

            // The shape is part of the key: a leg remembered without one cannot answer a map that
            // needs to draw the road.
            var shape = leg.IncludePolyline ? "p" : "-";

            return string.Join("|",
                E4(leg.OriginLat), E4(leg.OriginLng), E4(leg.DestLat), E4(leg.DestLng), hour, day, shape);
        }

        private static int E4(double coordinate) =>
            (int)Math.Round(coordinate * 10000, MidpointRounding.AwayFromZero);

        private static bool TryGetRemembered(RouteLegRequestItemDto leg, out RouteLegResultDto result)
        {
            result = null;

            if (!SessionCache.TryGetValue(KeyOf(leg), out var entry)) return false;

            if (DateTime.UtcNow - entry.StoredAt > MemoryCacheLifetime) return false;

            result = entry.Result;

            return true;
        }

        private static void Remember(RouteLegRequestItemDto leg, RouteLegResultDto result)
        {
            SessionCache[KeyOf(leg)] = (DateTime.UtcNow, result);
        }
    }
}
