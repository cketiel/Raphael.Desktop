using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>
    /// The live map's "Delays TomTom" layer (trial, 2026-11-22): TomTom Routing API v1 calculateRoute
    /// with live traffic, from a vehicle to the stop it is heading for, with the TomTom key the user
    /// saved in POWER UP (TOMTOM_API_KEY). One call is one non-tile Routing request, billed to that key.
    /// </summary>
    /// <remarks>
    /// Answers in the same shape as Google's (LiveMapRouteTrafficDto), so the map, the pace colours and
    /// the +N' badge do not care which provider spoke: the road as an encoded polyline, the time with
    /// and without traffic, and the jammed stretches as speed intervals (TomTom's magnitudeOfDelay
    /// 1-2 → SLOW, 3-4 → TRAFFIC_JAM).
    /// ⚠️ TomTom takes its key only in the query string: the request URL is never logged.
    /// </remarks>
    public sealed class TomTomTrafficClient
    {
        public const string KeyName = "TOMTOM_API_KEY";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly IProviderKeyStore _keys;

        public TomTomTrafficClient(IProviderKeyStore keys) => _keys = keys;

        public bool HasKey => _keys.Get(KeyName) is not null;

        public async Task<LiveMapRouteTrafficDto?> ComputeAsync(int vehicleRouteId, double fromLat, double fromLng, double toLat, double toLng)
        {
            var key = _keys.Get(KeyName);
            if (key is null) return null;
            var (status, body) = await SendAsync(key, fromLat, fromLng, toLat, toLng);
            if (status != 200)
            {
                Helpers.FileLogger.Log($"Live map: TomTom traffic refused (HTTP {status}).");
                return null;
            }

            var route = JsonNode.Parse(body)?["routes"]?[0];
            if (route is null) return null;
            var summary = route["summary"];
            var points = (route["legs"]?[0]?["points"] as JsonArray ?? new JsonArray())
                .Select(p => (Lat: p?["latitude"]?.GetValue<double>() ?? 0, Lng: p?["longitude"]?.GetValue<double>() ?? 0))
                .ToList();

            var withTraffic = summary?["travelTimeInSeconds"]?.GetValue<int>() ?? 0;
            var noTraffic = summary?["noTrafficTravelTimeInSeconds"]?.GetValue<int>()
                            ?? withTraffic - (summary?["trafficDelayInSeconds"]?.GetValue<int>() ?? 0);

            return new LiveMapRouteTrafficDto
            {
                VehicleRouteId = vehicleRouteId,
                EncodedPolyline = Encode(points),
                DurationInTrafficSeconds = withTraffic,
                DurationSeconds = Math.Max(0, noTraffic),
                Intervals = (route["sections"] as JsonArray ?? new JsonArray())
                    .Where(s => string.Equals(s?["sectionType"]?.GetValue<string>(), "TRAFFIC", StringComparison.OrdinalIgnoreCase))
                    .Select(s => new LiveMapSpeedIntervalDto
                    {
                        Start = s?["startPointIndex"]?.GetValue<int>() ?? 0,
                        End = s?["endPointIndex"]?.GetValue<int>() ?? 0,
                        Speed = (s?["magnitudeOfDelay"]?.GetValue<int>() ?? 1) >= 3 ? "TRAFFIC_JAM" : "SLOW"
                    }).ToList(),
                AtUtc = DateTime.UtcNow
            };
        }

        /// <summary>Whether a key answers: one short route with traffic (one Routing request).</summary>
        public static async Task<(bool Ok, string? Reason)> ValidateAsync(string key)
        {
            try
            {
                var (status, _) = await SendAsync(key, 26.6406, -81.8723, 26.6122, -81.8406);   // Fort Myers, two miles
                return status == 200 ? (true, null) : (false, $"TomTom answered HTTP {status}");
            }
            catch (Exception ex)
            {
                return (false, ex.GetType().Name);
            }
        }

        private static async Task<(int Status, string Body)> SendAsync(string key, double fromLat, double fromLng, double toLat, double toLng)
        {
            static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
            var url = $"https://api.tomtom.com/routing/1/calculateRoute/{F(fromLat)},{F(fromLng)}:{F(toLat)},{F(toLng)}/json" +
                      $"?traffic=true&travelMode=car&routeType=fastest&computeTravelTimeFor=all&sectionType=traffic&key={Uri.EscapeDataString(key)}";
            using var response = await Http.GetAsync(url);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>Google's encoded polyline format, so the page draws TomTom's road like Google's.</summary>
        private static string Encode(IReadOnlyList<(double Lat, double Lng)> points)
        {
            var sb = new StringBuilder();
            long lastLat = 0, lastLng = 0;
            foreach (var (lat, lng) in points)
            {
                long la = (long)Math.Round(lat * 1e5), ln = (long)Math.Round(lng * 1e5);
                Write(sb, la - lastLat); Write(sb, ln - lastLng);
                lastLat = la; lastLng = ln;
            }
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, long v)
        {
            v = v < 0 ? ~(v << 1) : v << 1;
            while (v >= 0x20) { sb.Append((char)((0x20 | (v & 0x1f)) + 63)); v >>= 5; }
            sb.Append((char)(v + 63));
        }
    }
}
