using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>
    /// The live map's "Delays on each route" layer, with the Google Maps key the user saved in POWER UP
    /// (GOOGLE_MAPS_API_KEY): Routes API computeRoutes, traffic aware, from a vehicle to the stop it is
    /// heading for. One call is one Routes Pro request, billed to that key.
    /// </summary>
    /// <remarks>
    /// ⚠️ An exception to MAPS_POLICY's "no client calls Google", decided by the user on 2026-11-22:
    /// the layer is opt-in, needs the user's own key and warns of its cost before it starts. The
    /// default colours (punctuality) come from Raphael.Api and call no Google at all.
    /// </remarks>
    public sealed class GoogleTrafficClient
    {
        public const string KeyName = "GOOGLE_MAPS_API_KEY";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly IProviderKeyStore _keys;

        public GoogleTrafficClient(IProviderKeyStore keys) => _keys = keys;

        public bool HasKey => _keys.Get(KeyName) is not null;

        /// <summary>Traffic from one point to another; null when Google gave no route.</summary>
        public Task<LiveMapRouteTrafficDto?> ComputeAsync(int vehicleRouteId, double fromLat, double fromLng, double toLat, double toLng) =>
            ComputeAsync(_keys.Get(KeyName), vehicleRouteId, fromLat, fromLng, toLat, toLng);

        /// <summary>
        /// Whether a key answers: one short traffic-aware route (one Routes Pro request). Returns the
        /// reason when it does not.
        /// </summary>
        public static async Task<(bool Ok, string? Reason)> ValidateAsync(string key)
        {
            try
            {
                var r = await SendAsync(key, 26.6406, -81.8723, 26.6122, -81.8406);   // Fort Myers, two miles
                return r.Status == 200 ? (true, null) : (false, $"Google answered HTTP {r.Status}");
            }
            catch (Exception ex)
            {
                return (false, ex.GetType().Name);
            }
        }

        private static async Task<LiveMapRouteTrafficDto?> ComputeAsync(string? key, int vehicleRouteId, double fromLat, double fromLng, double toLat, double toLng)
        {
            if (key is null) return null;
            var (status, body) = await SendAsync(key, fromLat, fromLng, toLat, toLng);
            if (status != 200)
            {
                Helpers.FileLogger.Log($"Live map: Google traffic refused (HTTP {status}).");
                return null;
            }

            var route = JsonNode.Parse(body)?["routes"]?[0];
            if (route is null) return null;
            return new LiveMapRouteTrafficDto
            {
                VehicleRouteId = vehicleRouteId,
                EncodedPolyline = route["polyline"]?["encodedPolyline"]?.GetValue<string>(),
                DurationInTrafficSeconds = Seconds(route["duration"]),
                DurationSeconds = Seconds(route["staticDuration"]),
                Intervals = (route["travelAdvisory"]?["speedReadingIntervals"] as JsonArray ?? new JsonArray())
                    .Select(i => new LiveMapSpeedIntervalDto
                    {
                        Start = i?["startPolylinePointIndex"]?.GetValue<int>() ?? 0,
                        End = i?["endPolylinePointIndex"]?.GetValue<int>() ?? 0,
                        Speed = i?["speed"]?.GetValue<string>() ?? "NORMAL"
                    }).ToList(),
                AtUtc = DateTime.UtcNow
            };
        }

        private static int Seconds(JsonNode? n) =>
            int.TryParse(n?.GetValue<string>()?.TrimEnd('s'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;

        private static async Task<(int Status, string Body)> SendAsync(string key, double fromLat, double fromLng, double toLat, double toLng)
        {
            var body = new JsonObject
            {
                ["origin"] = new JsonObject { ["location"] = new JsonObject { ["latLng"] = new JsonObject { ["latitude"] = fromLat, ["longitude"] = fromLng } } },
                ["destination"] = new JsonObject { ["location"] = new JsonObject { ["latLng"] = new JsonObject { ["latitude"] = toLat, ["longitude"] = toLng } } },
                ["travelMode"] = "DRIVE",
                ["routingPreference"] = "TRAFFIC_AWARE",
                ["extraComputations"] = new JsonArray("TRAFFIC_ON_POLYLINE")
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            // The key travels in a header, never in the URL.
            request.Headers.Add("X-Goog-Api-Key", key);
            request.Headers.Add("X-Goog-FieldMask", "routes.duration,routes.staticDuration,routes.polyline.encodedPolyline,routes.travelAdvisory.speedReadingIntervals");
            using var response = await Http.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
