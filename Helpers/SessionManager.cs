using System;

namespace Raphael.Desktop.Helpers
{
    public static class SessionManager
    {
        public static string Token { get; set; }
        public static string Username { get; set; }
        public static string UserId { get; set; }
        public static string Role { get; set; }
        public static int? IntegratorId { get; set; }
        public static int? ProviderId { get; set; }

        /// <summary>
        /// Buys a new access token when the current one expires. Single use: every renewal
        /// replaces it.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>In memory only, and that is a decision rather than an omission.</b> Writing it
        /// to disk would mean reopening the application signs you straight back in without a
        /// password, on a workstation that sits in an office and gets shared between shifts.
        /// The problem this solves is a session dying in the middle of somebody's work, not the
        /// cost of signing in once at the start of it. The driver's phone is a different case
        /// and answers differently.
        /// </remarks>
        public static string RefreshToken { get; set; }

        /// <summary>
        /// When <see cref="Token"/> stops being accepted, as the server stated at sign-in.
        /// </summary>
        /// <remarks>
        /// Taken from the server's answer rather than decoded out of the JWT. The server also
        /// allows thirty seconds of clock skew, which is deliberately not modelled here: a
        /// client that renews slightly early is correct, one that renews slightly late gets a
        /// 401 and renews anyway.
        /// </remarks>
        public static DateTime? AccessTokenExpiresAtUtc { get; set; }

        public static bool IsMilanesTransport => IntegratorId == null && ProviderId == null;

        public static bool IsAuthenticated => !string.IsNullOrEmpty(Token);

        public static bool CanRenew => !string.IsNullOrEmpty(RefreshToken);

        /// <summary>
        /// Raised once when the session cannot be renewed and the user has to sign in again.
        /// </summary>
        /// <remarks>
        /// ⚠️ An event rather than the thing it replaced. <c>ApiClientFactory.Create()</c> used
        /// to show a message box and close every window, from inside the constructor of any of
        /// twenty-five services — on whatever thread happened to be running, taking unsaved
        /// work with it, and then returning a client with no credential anyway so the call went
        /// out unauthenticated. Signing out is one decision made in one place, on the UI
        /// thread; see App.OnSessionExpired.
        /// </remarks>
        public static event Action SessionExpired;

        private static int _expiredSignalled;

        /// <summary>
        /// Announces an unrecoverable session, exactly once however many requests fail together.
        /// </summary>
        public static void SignalExpired()
        {
            // Twenty parallel calls all get a 401 within the same second. Without this the user
            // is told twenty times.
            if (System.Threading.Interlocked.Exchange(ref _expiredSignalled, 1) != 0)
            {
                return;
            }

            SessionExpired?.Invoke();
        }

        public static void Clear()
        {
            Token = null;
            Username = null;
            UserId = null;
            Role = null;
            IntegratorId = null;
            ProviderId = null;
            RefreshToken = null;
            AccessTokenExpiresAtUtc = null;
            System.Threading.Interlocked.Exchange(ref _expiredSignalled, 0);
        }
    }
}
