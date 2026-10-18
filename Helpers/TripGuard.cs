using System;
using System.Linq;
using System.Windows;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.TripLocks;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// What the dispatcher may still change on a trip, and the conversation when they may not (RE-051).
    /// </summary>
    /// <remarks>
    /// The server decides; this only says it first, and says why. Two rules:
    /// <list type="bullet">
    /// <item><b>Read-only</b> — a trip of a past day, or closed (Finished, Billed, Payed). It is the
    /// record reports and billing are built on. An admin may correct it, giving a reason that goes
    /// to the trip's history.</item>
    /// <item><b>Routed</b> — its date, hours, addresses, type, patient, space type, funding source,
    /// authorization and distance were copied onto its two stops. They stay locked until the trip
    /// goes back to the open list. Phones and comments are edited and copied across.</item>
    /// </list>
    /// "Today" is this machine's: a hint for the screen. The server measures it on the provider's clock.
    /// </remarks>
    public static class TripGuard
    {
        private static readonly string[] ClosedStatuses = { "Finished", "Billed", "Payed" };

        public static bool IsAdmin => SessionManager.Role == "1";

        public static bool IsClosed(string status) =>
            !string.IsNullOrEmpty(status) && ClosedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

        public static bool IsPastDay(DateTime? date) => date.HasValue && date.Value.Date < DateTime.Today;

        public static bool IsReadOnly(DateTime? date, string status) => IsPastDay(date) || IsClosed(status);

        /// <summary>Why this trip is read-only, in the user's language.</summary>
        public static string ReadOnlyReason(DateTime? date, string status)
        {
            var l = LocalizationService.Instance;
            return IsPastDay(date)
                ? string.Format(l["lock.ReadOnlyPastDay"], date.Value.ToString("d"))
                : string.Format(l["lock.ReadOnlyClosed"], status);
        }

        /// <summary>
        /// Asks before an operation on a trip. Null means stop; otherwise dispose the scope after the call.
        /// </summary>
        /// <remarks>
        /// A trip that is not read-only gets an empty scope and no question. A read-only one gets the
        /// reason and stops — unless the user is an admin, who is offered a correction and asked why.
        /// </remarks>
        public static IDisposable Enter(DateTime? date, string status, string operation)
        {
            if (!IsReadOnly(date, status)) return NoScope.Instance;

            var reason = AskForCorrection(ReadOnlyReason(date, status), operation);
            return reason == null ? null : CorrectionScope.Begin(reason);
        }

        /// <summary>
        /// Shows the read-only explanation, or — to an admin — asks for the reason of a correction.
        /// Null when the user stops there.
        /// </summary>
        public static string AskForCorrection(string why, string operation)
        {
            var l = LocalizationService.Instance;

            if (!IsAdmin)
            {
                MessageBox.Show(why + "\n\n" + l["lock.ReadOnlyExplanation"],
                    l["lock.ReadOnlyTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var dialog = new Views.ReasonDialog(
                l["lock.CorrectionTitle"],
                string.Format(l["lock.CorrectionPrompt"], why, operation),
                l["lock.CorrectionConfirm"],
                l["Cancel"],
                l["lock.CorrectionReasonRequired"]);

            return dialog.ShowDialog() == true ? dialog.Reason : null;
        }

        /// <summary>
        /// Explains a refusal the server made on its own — the clock may disagree with this machine's,
        /// or another dispatcher routed the trip a moment ago. True when it was one of ours.
        /// </summary>
        public static bool ExplainRefusal(Exception ex)
        {
            if (ex is not TripLockedApiException locked) return false;

            var l = LocalizationService.Instance;
            var text = locked.Code switch
            {
                TripLockCodes.RoutedFieldsLocked => l["lock.RoutedRefused"],
                TripLockCodes.CorrectionReasonRequired => l["lock.CorrectionReasonMissing"],
                _ => l["lock.ReadOnlyExplanation"]
            };

            MessageBox.Show(text, l["lock.ReadOnlyTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private sealed class NoScope : IDisposable
        {
            public static readonly NoScope Instance = new();
            public void Dispose() { }
        }
    }
}
