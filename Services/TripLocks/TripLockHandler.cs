using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.Services.TripLocks
{
    /// <summary>
    /// The codes the API answers with when a trip refuses a change (RE-051). Mirrors
    /// <c>Raphael.Api.Services.Trips.TripLockCodes</c>.
    /// </summary>
    public static class TripLockCodes
    {
        public const string ReadOnly = "trip_read_only";
        public const string CorrectionReasonRequired = "correction_reason_required";
        public const string RoutedFieldsLocked = "trip_routed_locked";

        /// <summary>RE-052: a drive between a route's new garage and its stops could not be measured.</summary>
        public const string GarageLegUnavailable = "garage_leg_unavailable";

        /// <summary>RE-052: the route moved since the garage-change preview the user confirmed.</summary>
        public const string RouteChangedSincePreview = "route_changed_since_preview";
    }

    /// <summary>A change the server refused because the trip is past, closed or routed.</summary>
    public sealed class TripLockedApiException : ApiException
    {
        public TripLockedApiException(string code, string message, IReadOnlyList<string> fields)
            : base(message, HttpStatusCode.Conflict, message)
        {
            Code = code;
            Fields = fields ?? Array.Empty<string>();
        }

        public string Code { get; }
        public IReadOnlyList<string> Fields { get; }
    }

    /// <summary>
    /// An admin's correction of a past or closed trip: the reason travels with every request made
    /// while the scope is open, and the server writes it to the trip's history.
    /// </summary>
    /// <remarks>
    /// AsyncLocal, not a static field: the Schedule tab keeps refreshing in the background, and a
    /// timer's request must never go out carrying somebody's correction.
    /// </remarks>
    public static class CorrectionScope
    {
        private static readonly AsyncLocal<string> _reason = new();

        public static string CurrentReason => _reason.Value;

        public static IDisposable Begin(string reason)
        {
            var previous = _reason.Value;
            _reason.Value = reason;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly string _previous;
            private bool _done;
            public Restore(string previous) => _previous = previous;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _reason.Value = _previous;
            }
        }
    }

    /// <summary>
    /// Puts an open correction's reason on the request, and turns the API's "this trip is locked"
    /// answer into a <see cref="TripLockedApiException"/> whichever service made the call.
    /// </summary>
    /// <remarks>
    /// Here and not in each service because half of them still call EnsureSuccessStatusCode, which
    /// throws the body away — and the body is the only place that says why.
    /// </remarks>
    public sealed class TripLockHandler : DelegatingHandler
    {
        public const string CorrectionReasonHeader = "X-Correction-Reason";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var reason = CorrectionScope.CurrentReason;
            request.Headers.Remove(CorrectionReasonHeader);
            if (!string.IsNullOrWhiteSpace(reason))
                request.Headers.TryAddWithoutValidation(CorrectionReasonHeader, Uri.EscapeDataString(reason));

            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Conflict || response.Content == null)
                return response;

            await response.Content.LoadIntoBufferAsync();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (TryRead(body, out var code, out var title, out var fields))
            {
                response.Dispose();
                throw new TripLockedApiException(code, title, fields);
            }

            return response;
        }

        private static bool TryRead(string body, out string code, out string title, out IReadOnlyList<string> fields)
        {
            code = null; title = null; fields = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(body)) return false;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (!root.TryGetProperty("code", out var c) || c.ValueKind != JsonValueKind.String) return false;

                code = c.GetString();
                if (code != TripLockCodes.ReadOnly && code != TripLockCodes.CorrectionReasonRequired && code != TripLockCodes.RoutedFieldsLocked
                    && code != TripLockCodes.GarageLegUnavailable && code != TripLockCodes.RouteChangedSincePreview)
                    return false;

                title = root.TryGetProperty("title", out var t) ? t.GetString() : code;

                var list = new List<string>();
                if (root.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                    foreach (var item in f.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString());
                fields = list;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
