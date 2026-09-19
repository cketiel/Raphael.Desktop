using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.Auth
{
    /// <summary>
    /// Tells the server which application this is and which build, and listens for the answer
    /// that it is out of date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server has known how to read these since <c>v1.0.0</c> and no client has ever sent
    /// them, so its telemetry could not distinguish a dispatcher from a driver from a patient,
    /// and its compatibility floor never matched anybody.
    /// </para>
    /// <para>
    /// ⚠️ <c>X-Client-Id</c> already exists in this application and is <b>not</b> this. It
    /// belongs to Zonitel, goes to the telephone company, and never reaches Raphael.
    /// </para>
    /// </remarks>
    public sealed class ClientVersionHandler : DelegatingHandler
    {
        /// <summary>
        /// ⚠️ Matched without regard to case against two different sections of the server's
        /// configuration: <c>ClientCompatibility:MinimumVersions</c>, which decides whether
        /// this build is reported as outdated, and <c>SessionPolicy:Apps</c>, which decides how
        /// long its sessions last. A typo here is not an error anywhere — it silently means
        /// "never outdated" and "the default session policy".
        /// </summary>
        public const string ApplicationName = "Desktop";

        private const string AppHeader = "X-Client-App";
        private const string VersionHeader = "X-Client-Version";
        private const string StatusHeader = "X-Raphael-Client-Status";
        private const string MinimumHeader = "X-Raphael-Client-Minimum";

        /// <summary>True once the server has reported this build as below its floor.</summary>
        public static bool IsOutdated { get; private set; }

        /// <summary>The oldest build the server expects, when it has said so.</summary>
        public static string MinimumVersion { get; private set; }

        /// <summary>Raised the first time the server reports this build as outdated.</summary>
        public static event Action<string> OutdatedDetected;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation(AppHeader, ApplicationName);
            request.Headers.TryAddWithoutValidation(VersionHeader, VersionHelper.Version);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!IsOutdated &&
                response.Headers.TryGetValues(StatusHeader, out var status) &&
                status.Any(v => string.Equals(v, "outdated", StringComparison.OrdinalIgnoreCase)))
            {
                IsOutdated = true;

                MinimumVersion = response.Headers.TryGetValues(MinimumHeader, out var minimum)
                    ? minimum.FirstOrDefault()
                    : null;

                // ⚠️ Advice, never a gate. The application carries on exactly as before: the
                // server answered the request normally and refusing to show that answer would
                // be the client inventing an outage the server did not have.
                OutdatedDetected?.Invoke(MinimumVersion);
            }

            return response;
        }
    }
}
