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
        /// Ensures the session holds an access token newer than <paramref name="staleToken"/>.
        /// </summary>
        /// <returns>
        /// True when there is a usable token afterwards — whether this call fetched it or
        /// another one did. False when the session is over and the user must sign in.
        /// </returns>
        public static async Task<bool> EnsureRenewedAsync(string staleToken, CancellationToken cancellationToken)
        {
            if (!SessionManager.CanRenew)
            {
                return false;
            }

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Somebody renewed while this request was queued at the gate. That is the
                // normal case for all but the first of a burst, and it is a success: the
                // caller only needs a token that is not the one that just failed.
                if (!string.Equals(SessionManager.Token, staleToken, StringComparison.Ordinal))
                {
                    return SessionManager.IsAuthenticated;
                }

                var refreshToken = SessionManager.RefreshToken;

                if (string.IsNullOrEmpty(refreshToken))
                {
                    return false;
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
                        return false;
                    }

                    SessionManager.Token = pair.Token;
                    SessionManager.RefreshToken = pair.RefreshToken;
                    SessionManager.AccessTokenExpiresAtUtc = pair.AccessTokenExpiresAtUtc;
                    return true;
                }

                // 409 means the server had already rotated this token. Inside the gate that
                // should not happen, and if it does we were not handed the replacement, so
                // there is nothing to recover with.
                System.Diagnostics.Debug.WriteLine(
                    $"TokenRenewal: the server refused to renew ({(int)response.StatusCode}). Signing out.");

                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // ⚠️ A renewal that failed because the network is down is NOT an expired
                // session, and must not sign anybody out: the office loses its connection for a
                // minute more often than a token is stolen. The caller gets false, the original
                // 401 is returned, and the next request tries again once there is a network.
                System.Diagnostics.Debug.WriteLine($"TokenRenewal: could not reach the server. {ex.Message}");
                return false;
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
