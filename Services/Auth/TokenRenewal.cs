using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.Auth
{
    /// <summary>
    /// Buys a new access token with the refresh token, once, however many requests discover the
    /// expiry at the same moment.
    /// </summary>
    public static class TokenRenewal
    {
        /// <summary>
        /// ⚠️ The single most important line in this file. A dispatcher with the Schedule tab
        /// open has twenty or more services making calls; when the access token expires they
        /// all get a 401 within the same second. Without this gate that is twenty renewals, and
        /// because each one rotates the refresh token, nineteen of them present a token the
        /// server has just replaced — which is the server's definition of a stolen credential.
        /// Twenty concurrent calls would have signed the user out.
        /// </summary>
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>
        /// Deliberately not the shared client: this call must not pass through
        /// <see cref="AuthRefreshHandler"/>, or a failed renewal would try to renew itself.
        /// </summary>
        private static readonly Lazy<HttpClient> Client = new(() =>
            new HttpClient { BaseAddress = new Uri(ApiEnvironment.BaseUrl), Timeout = TimeSpan.FromSeconds(30) });

        private sealed class TokenPair
        {
            public string Token { get; set; }
            public DateTime AccessTokenExpiresAtUtc { get; set; }
            public string RefreshToken { get; set; }
        }

        /// <summary>
        /// What came of trying to renew. Three outcomes and not a <c>bool</c>, because two of
        /// them used to be the same value and the difference is somebody's session.
        /// </summary>
        /// <remarks>
        /// ⚠️ "The server said no" and "I could not reach the server" both returned false,
        /// and the caller ended the session on false. So a 409, a 500 or a timeout signed the
        /// dispatcher out — mid-assignment, with unsaved work on screen. Found on the Driver
        /// on 2026-09-19, where signing in against DEV threw the driver straight back to the
        /// sign-in screen; this application had the identical defect and the identical cause.
        /// </remarks>
        public enum RenewalOutcome
        {
            /// <summary>There is a token newer than the one that failed. Retry the request.</summary>
            Renewed,

            /// <summary>The server refused the refresh token. The session really is over.</summary>
            SessionOver,

            /// <summary>
            /// Nothing was decided: no network, a timeout, or the server having a bad minute.
            /// The request fails and the session is left exactly as it was.
            /// </summary>
            Unavailable
        }

        /// <summary>
        /// What a non-OK answer from <c>/api/Auth/refresh</c> means for the session. Pure, and
        /// separate from everything else, so it can be exercised case by case.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>409 is not a failure.</b> The backend answers <c>rotated_recently</c> with
        /// Conflict precisely so a client does not send the user to the sign-in screen —
        /// <c>AuthController.Refresh</c> says so in as many words: "401 tells a client to send
        /// the user back to the login screen, and this is the one failure where it must not".
        /// It means another request renewed with this same refresh token seconds ago. The
        /// gate catches that inside one process, but not when the other renewal finished
        /// after this one read the stored token.
        /// </para>
        /// <para>
        /// ⚠️ <b>Only an answer about the credential ends a session.</b> 401 and 403 are the
        /// server refusing the refresh token. A 500 while the server is still starting, a 429
        /// from the rate limiter, a 408 — none of those say anything about the session.
        /// </para>
        /// </remarks>
        internal static RenewalOutcome Classify(HttpStatusCode status, bool newerTokenStored) => status switch
        {
            HttpStatusCode.OK => RenewalOutcome.Renewed,

            HttpStatusCode.Conflict => newerTokenStored
                ? RenewalOutcome.Renewed
                : RenewalOutcome.Unavailable,

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => RenewalOutcome.SessionOver,

            _ => RenewalOutcome.Unavailable
        };

        /// <summary>
        /// Ensures the session holds an access token newer than <paramref name="staleToken"/>.
        /// </summary>
        /// <returns>
        /// <see cref="RenewalOutcome.Renewed"/> when there is a usable token afterwards,
        /// whether this call fetched it or another one did; <see cref="RenewalOutcome.SessionOver"/>
        /// only when the server refused the credential; <see cref="RenewalOutcome.Unavailable"/>
        /// when nothing was decided.
        /// </returns>
        public static async Task<RenewalOutcome> EnsureRenewedAsync(string staleToken, CancellationToken cancellationToken)
        {
            if (!SessionManager.CanRenew)
            {
                // Nothing to renew with. Typing a password is the only way on from here.
                return RenewalOutcome.SessionOver;
            }

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Somebody renewed while this request was queued at the gate. That is the
                // normal case for all but the first of a burst, and it is a success: the
                // caller only needs a token that is not the one that just failed.
                if (!string.Equals(SessionManager.Token, staleToken, StringComparison.Ordinal))
                {
                    return SessionManager.IsAuthenticated
                        ? RenewalOutcome.Renewed
                        : RenewalOutcome.SessionOver;
                }

                var refreshToken = SessionManager.RefreshToken;

                if (string.IsNullOrEmpty(refreshToken))
                {
                    return RenewalOutcome.SessionOver;
                }

                using var response = await Client.Value
                    .PostAsJsonAsync("api/Auth/refresh", new { refreshToken }, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var pair = await response.Content
                        .ReadFromJsonAsync<TokenPair>(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (pair is null || string.IsNullOrEmpty(pair.Token))
                    {
                        // A 200 with nothing usable in it is the server misbehaving, not the
                        // session ending.
                        return RenewalOutcome.Unavailable;
                    }

                    SessionManager.Token = pair.Token;
                    SessionManager.RefreshToken = pair.RefreshToken;
                    SessionManager.AccessTokenExpiresAtUtc = pair.AccessTokenExpiresAtUtc;
                    return RenewalOutcome.Renewed;
                }

                var newerTokenStored = !string.Equals(
                    SessionManager.Token, staleToken, StringComparison.Ordinal);

                var outcome = Classify(response.StatusCode, newerTokenStored);

                // ⚠️ This block used to say "409 means the server had already rotated this
                // token ... there is nothing to recover with", and returned false, and false
                // ended the session. That is precisely backwards: the backend chose 409 over
                // 401 so that a client would NOT do this. See Classify.
                System.Diagnostics.Debug.WriteLine(
                    $"TokenRenewal: renewal answered {(int)response.StatusCode} -> {outcome}.");

                return outcome;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // ⚠️ A renewal that failed because the network is down is NOT an expired
                // session, and must not sign anybody out: the office loses its connection for a
                // minute more often than a token is stolen. The caller gets Unavailable, the
                // original 401 is returned, and the next request tries again once there is a
                // network.
                //
                // This comment was already here and the code under it returned false, which
                // the caller treated as an expired session — the opposite of what it says.
                System.Diagnostics.Debug.WriteLine($"TokenRenewal: could not reach the server. {ex.Message}");
                return RenewalOutcome.Unavailable;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Tells the server to forget this session. Best effort: signing out of the application
        /// must never be blocked by the server being unreachable.
        /// </summary>
        /// <param name="refreshToken">
        /// Passed in rather than read from the session, because every caller clears the
        /// session immediately afterwards. Relying on this method to read it first would work
        /// only for as long as nobody adds an await above that line.
        /// </param>
        public static async Task RevokeAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return;
            }

            try
            {
                using var _ = await Client.Value
                    .PostAsJsonAsync("api/Auth/logout", new { refreshToken })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TokenRenewal: sign-out was not delivered. {ex.Message}");
            }
        }
    }
}
