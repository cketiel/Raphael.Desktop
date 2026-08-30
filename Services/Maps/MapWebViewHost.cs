using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>
    /// Serves the map pages to WebView2 from a virtual host, and answers what they ask for.
    /// </summary>
    /// <remarks>
    /// The map pages used to be loaded with <c>NavigateToString</c>, or written to a file in
    /// <c>%TEMP%</c> and opened from there. Both give the document a <c>null</c> or <c>file://</c>
    /// origin, and a page with no origin sends no <c>Referer</c> — which means the Google key
    /// those pages carry <b>cannot be restricted by HTTP referrer</b>. The only available
    /// protection was to leave it unrestricted.
    ///
    /// <para>
    /// Mapping the Assets folder to <c>https://raphael.maps/</c> gives the pages a real origin, so
    /// the browser key can be locked to <c>raphael.maps/*</c> in Cloud Console and is worth
    /// nothing anywhere else. The same trick already serves the help panel; see
    /// <c>HelpService.VirtualHostName</c>.
    /// </para>
    ///
    /// <para>
    /// The key travels to the page through <c>AddScriptToExecuteOnDocumentCreatedAsync</c>, not
    /// through the URL or a placeholder in the file, so it is in nothing that lands on disk.
    /// </para>
    /// </remarks>
    public static class MapWebViewHost
    {
        /// <summary>The origin the map pages are served from, and the referrer to restrict to.</summary>
        public const string VirtualHostName = "raphael.maps";

        /// <summary>Folder served under that host. The map pages and their script live here.</summary>
        public static string AssetsRoot =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Brings a WebView2 up, maps the folder, and hands the page its configuration.
        /// </summary>
        /// <remarks>
        /// Call once per WebView2. The injected script survives navigations, so calling it again
        /// for the same control would stack a second copy of the configuration on every page.
        /// </remarks>
        public static async Task InitializeAsync(WebView2 webView, CoreWebView2Environment environment = null)
        {
            await webView.EnsureCoreWebView2Async(environment);

            var core = webView.CoreWebView2;

            core.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                AssetsRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            var config = new
            {
                apiKey = App.Configuration["GoogleMaps:ApiKey"] ?? string.Empty,

                // Biases the address suggestions to the country the operation runs in, which cuts
                // out the foreign streets that share a name with a local one.
                regionCode = "us"
            };

            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.RAPHAEL_MAPS = " + JsonSerializer.Serialize(config, Json) + ";");
        }

        /// <summary>Opens a map page, passing its coordinates in the query string.</summary>
        public static void Navigate(WebView2 webView, string page, params (string Key, double Value)[] coordinates)
        {
            if (webView?.CoreWebView2 == null) return;

            var url = new System.Text.StringBuilder("https://")
                .Append(VirtualHostName)
                .Append('/')
                .Append(page);

            var first = true;

            foreach (var (key, value) in coordinates)
            {
                if (double.IsNaN(value)) continue;

                url.Append(first ? '?' : '&')
                   .Append(key)
                   .Append('=')
                   .Append(value.ToString("R", CultureInfo.InvariantCulture));

                first = false;
            }

            webView.CoreWebView2.Navigate(url.ToString());
        }

        /// <summary>
        /// Forwards a page's report of its own Google spending, if that is what this message is.
        /// </summary>
        /// <remarks>
        /// The map pages call Google directly with the browser key — a map load, an address
        /// autocomplete, a geocode when a pin is dragged — and the server sees none of it. Without
        /// this the administrator's usage panel would show a figure that could never match the
        /// invoice. Fire and forget, and silent on failure.
        /// </remarks>
        public static bool TryForwardUsage(string json, IMapsUsageApiService usage)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("type", out var type)
                    || type.GetString() != "usage")
                {
                    return false;
                }

                if (!document.RootElement.TryGetProperty("sku", out var skuElement)) return true;

                var sku = skuElement.GetString();

                if (string.IsNullOrWhiteSpace(sku)) return true;

                var count = document.RootElement.TryGetProperty("count", out var countElement)
                    && countElement.TryGetInt32(out var parsed)
                        ? parsed
                        : 1;

                _ = usage.ReportUsageAsync(sku, count);

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Answers the questions a map page cannot answer itself, through Raphael.Api.
        /// </summary>
        /// <remarks>
        /// ⚠️ This is the cache the map pages were missing. Reverse geocoding and place lookups
        /// used to go straight from the browser to Google on every pin drag and every chosen
        /// suggestion — billed each time, and invisible to the cache the rest of the system was
        /// built around. They now come from our own database whenever anybody has asked before.
        ///
        /// <para>
        /// The page cannot call the API itself: that would put a session token in a document that
        /// also runs Google's script. So it asks, and the reply is delivered by calling
        /// <c>raphaelResolve</c> with the request id it sent.
        /// </para>
        ///
        /// <para>Returns true when the message was one of ours and has been dealt with.</para>
        /// </remarks>
        public static bool TryHandleLookup(
            string json,
            WebView2 webView,
            IRoutingApiService routing)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("type", out var typeElement)) return false;

                var type = typeElement.GetString();

                switch (type)
                {
                    case "reverseGeocode":
                        _ = AnswerReverseGeocodeAsync(document.RootElement, webView, routing);
                        return true;

                    case "placeLookup":
                        _ = AnswerPlaceLookupAsync(document.RootElement, webView, routing);
                        return true;

                    case "placeStore":
                        _ = StorePlaceAsync(document.RootElement, routing);
                        return true;

                    default:
                        return false;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static async Task AnswerReverseGeocodeAsync(
            JsonElement message,
            WebView2 webView,
            IRoutingApiService routing)
        {
            var id = message.TryGetProperty("requestId", out var requestId) ? requestId.GetInt32() : 0;

            try
            {
                var latitude = message.GetProperty("latitude").GetDouble();
                var longitude = message.GetProperty("longitude").GetDouble();

                var answer = await routing.ReverseGeocodeAsync(latitude, longitude);

                await ReplyAsync(webView, id, answer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Reverse geocode failed: " + ex.Message);

                // Always reply, even with nothing: a page left waiting would hold a dragged pin
                // in limbo until its own timeout.
                await ReplyAsync(webView, id, null);
            }
        }

        private static async Task AnswerPlaceLookupAsync(
            JsonElement message,
            WebView2 webView,
            IRoutingApiService routing)
        {
            var id = message.TryGetProperty("requestId", out var requestId) ? requestId.GetInt32() : 0;

            try
            {
                var placeId = message.GetProperty("placeId").GetString();

                var answer = await routing.GetPlaceAsync(placeId);

                await ReplyAsync(webView, id, answer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Place lookup failed: " + ex.Message);

                await ReplyAsync(webView, id, null);
            }
        }

        private static async Task StorePlaceAsync(JsonElement message, IRoutingApiService routing)
        {
            try
            {
                var place = JsonSerializer.Deserialize<PlaceDetailsDto>(message.GetRawText(), Json);

                if (place != null) await routing.StorePlaceAsync(place);
            }
            catch (Exception ex)
            {
                // Fire and forget. A place we failed to remember is bought again next time,
                // which is the old behaviour and not worth interrupting anybody over.
                Debug.WriteLine("Could not store a place: " + ex.Message);
            }
        }

        private static async Task ReplyAsync(WebView2 webView, int requestId, object payload)
        {
            if (requestId == 0 || webView?.CoreWebView2 == null) return;

            var body = payload == null ? "null" : JsonSerializer.Serialize(payload, Json);

            await webView.Dispatcher.InvokeAsync(async () =>
                await webView.ExecuteScriptAsync($"raphaelResolve({requestId}, {body})"));
        }

        /// <summary>What a page asks for when it needs the road between its two pins drawn.</summary>
        public sealed class MapRouteRequest
        {
            public double OriginLat { get; set; }

            public double OriginLng { get; set; }

            public double DestLat { get; set; }

            public double DestLng { get; set; }
        }

        /// <summary>
        /// Reads a <c>routeRequest</c> message. Returns false for every other message a page sends.
        /// </summary>
        public static bool TryReadRouteRequest(string json, out MapRouteRequest request)
        {
            request = null;

            try
            {
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("type", out var type)
                    || type.GetString() != "routeRequest")
                {
                    return false;
                }

                request = JsonSerializer.Deserialize<MapRouteRequest>(json, Json);

                return request != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Prices the leg through the routing proxy and tells the page to draw it.
        /// </summary>
        /// <remarks>
        /// The page cannot call Raphael.Api itself: it would need a session token, in a document
        /// that also runs Google's script. So the host makes the call and sends back a shape.
        ///
        /// <para>
        /// Pass the trip's own <paramref name="date"/> and <paramref name="departureTime"/>
        /// whenever the screen knows them. A leg priced without them is priced as leaving now,
        /// and a trip being planned the evening before does not leave now.
        /// </para>
        ///
        /// <para>Returns the leg, so a caller can also show the figures on its own screen.</para>
        /// </remarks>
        public static async Task<RouteLegResultDto> DrawRouteAsync(
            WebView2 webView,
            IRoutingApiService routing,
            MapRouteRequest request,
            DateTime? date = null,
            TimeSpan? departureTime = null)
        {
            var leg = await routing.GetMapRouteAsync(
                request.OriginLat,
                request.OriginLng,
                request.DestLat,
                request.DestLng,
                date,
                departureTime);

            if (leg == null || !leg.IsUsable || string.IsNullOrEmpty(leg.EncodedPolyline))
            {
                // Nothing to draw. The pins stay where they are rather than the map clearing
                // itself, which would read as "there is no route" instead of "we could not ask".
                return leg;
            }

            var payload = new
            {
                encodedPolyline = leg.EncodedPolyline,
                label = DescribeLeg(leg)
            };

            await webView.ExecuteScriptAsync(
                "showRoute(" + JsonSerializer.Serialize(payload, Json) + ")");

            return leg;
        }

        /// <summary>The one-line summary the map shows over the route.</summary>
        public static string DescribeLeg(RouteLegResultDto leg)
        {
            if (leg == null || !leg.IsUsable) return string.Empty;

            var seconds = leg.DurationInTrafficSeconds ?? leg.DurationSeconds;

            return "ETA: " + FormatDuration(seconds)
                + " — Distance: " + leg.DistanceMiles.ToString("0.0", CultureInfo.InvariantCulture) + " mi";
        }

        public static string FormatDuration(int seconds)
        {
            var minutes = (int)Math.Round(seconds / 60.0);

            if (minutes < 60) return minutes + " min";

            var hours = minutes / 60;
            var rest = minutes % 60;

            return rest == 0 ? hours + " h" : hours + " h " + rest + " min";
        }
    }
}
