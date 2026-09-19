using System;
using System.Net.Http;
using Raphael.Desktop.Services.Auth;

namespace Raphael.Desktop.Services
{
    public static class ApiClientFactory
    {
        private const string Prefix = "api/";

        /// <summary>The API root every service talks to, including the <c>api/</c> segment.</summary>
        public static string URI => ApiEnvironment.BaseUrl + Prefix;

        /// <summary>
        /// One handler for the whole application, and therefore one connection pool.
        ///
        /// Every service in the app calls <see cref="Create"/> in its constructor, so a single
        /// open Schedule tab used to stand up eight or more HttpClients, each opening its own
        /// connections to a server that is on the internet and never releasing them. Sharing
        /// the handler keeps the TLS handshakes down to the ones actually needed.
        ///
        /// PooledConnectionLifetime is what stops a long-lived pool from pinning a stale DNS
        /// answer — the reason a static HttpClient is otherwise a bad idea. It also matters
        /// more than it used to: moving the backend is now a CNAME change, and a pool that
        /// pinned the old address would keep talking to the old server.
        /// </summary>
        private static readonly SocketsHttpHandler SharedHandler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        /// <summary>
        /// Signed-in traffic: identifies the build, carries the token, renews it on expiry.
        /// </summary>
        private static readonly HttpMessageHandler AuthenticatedPipeline =
            new AuthRefreshHandler
            {
                InnerHandler = new ClientVersionHandler { InnerHandler = SharedHandler }
            };

        /// <summary>
        /// Traffic that happens before there is a session — signing in, asking the server its
        /// version. Same connection pool, no credential.
        /// </summary>
        private static readonly HttpMessageHandler AnonymousPipeline =
            new ClientVersionHandler { InnerHandler = SharedHandler };

        /// <summary>
        /// A client for a signed-in service.
        /// </summary>
        /// <remarks>
        /// ⚠️ This method used to decide, on every construction, whether the session had
        /// expired — and if it had, show a message box, close every open window and then return
        /// the client anyway with no credential. It ran inside the constructor of roughly
        /// twenty-five services, on whatever thread that constructor happened to be on, and it
        /// took unsaved work with it. Expiry is now handled per request by
        /// <see cref="AuthRefreshHandler"/>, which renews instead of ending, and signing out is
        /// one decision in one place.
        /// </remarks>
        public static HttpClient Create() =>
            new(AuthenticatedPipeline, disposeHandler: false) { BaseAddress = new Uri(URI) };

        /// <summary>A client for calls made before sign-in.</summary>
        public static HttpClient CreateAnonymous() =>
            new(AnonymousPipeline, disposeHandler: false) { BaseAddress = new Uri(ApiEnvironment.BaseUrl) };
    }
}
