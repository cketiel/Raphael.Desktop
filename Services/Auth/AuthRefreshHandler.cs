using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.Auth
{
    /// <summary>
    /// Attaches the current access token to every request, and renews it once on a 401 before
    /// giving up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a check that ran in <c>ApiClientFactory.Create()</c>, which is called from
    /// the constructor of about twenty-five services. Two things were wrong with it beyond the
    /// window-closing. It ran when the service was <i>built</i>, not when a request was
    /// <i>sent</i>, and it baked the token of that moment into the client — so a service
    /// constructed at eight in the morning went on sending the same expired token all day and
    /// never passed the check again. And on finding the token expired it still returned the
    /// client, without a credential, so the call went out anyway and came back 401 to nobody.
    /// </para>
    /// <para>
    /// A handler is the right place because it sees each request, not each construction.
    /// </para>
    /// </remarks>
    public sealed class AuthRefreshHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var tokenSent = SessionManager.Token;

            if (!string.IsNullOrEmpty(tokenSent))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenSent);
            }

            // Buffered so the request can be sent a second time after a renewal. Without this
            // the retry would post an already-consumed stream, and the failure would look like
            // the server rejecting an empty body.
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            }

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrEmpty(tokenSent))
            {
                return response;
            }

            if (!SessionManager.CanRenew)
            {
                SessionManager.SignalExpired();
                return response;
            }

            var outcome = await TokenRenewal
                .EnsureRenewedAsync(tokenSent, cancellationToken)
                .ConfigureAwait(false);

            // ⚠️ Only SessionOver ends the session. Unavailable means nothing was decided —
            // no network, a timeout, a 409 because another request renewed a second ago, a
            // 500 from a server still starting — and the request simply fails, which the
            // screen that made it can retry. Before this, ANY failure to renew signalled
            // expiry and threw the dispatcher back to the sign-in screen mid-assignment.
            if (outcome != TokenRenewal.RenewalOutcome.Renewed)
            {
                if (outcome == TokenRenewal.RenewalOutcome.SessionOver)
                {
                    SessionManager.SignalExpired();
                }

                return response;
            }

            response.Dispose();

            // Once, never in a loop. A 401 that survives a fresh token is not about the token,
            // and retrying it forever would turn an authorisation problem into a hang.
            var retry = Clone(request);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionManager.Token);

            return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// A request message cannot be sent twice, so the retry needs a copy. The content
        /// object is reused rather than copied — it was buffered above and is safe to read
        /// again.
        /// </summary>
        private static HttpRequestMessage Clone(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
                Content = request.Content
            };

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var option in request.Options)
            {
                clone.Options.Set(new HttpRequestOptionsKey<object>(option.Key), option.Value);
            }

            return clone;
        }
    }
}
