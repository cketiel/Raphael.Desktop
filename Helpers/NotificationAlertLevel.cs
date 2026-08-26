using Raphael.Desktop.Models;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// How loudly a live notification is allowed to announce itself.
    /// </summary>
    /// <remarks>
    /// The dispatcher is on the phone with a patient, routing trips, typing an address. An
    /// alert that treats "trip completed" and "a patient is waiting with an hour on the
    /// clock" the same way trains them to ignore the corner of the screen, and then the one
    /// that mattered goes unseen too.
    /// </remarks>
    public enum NotificationAlertLevel
    {
        /// <summary>The operation breathing: started, arrived, on board, completed.</summary>
        Ambient = 0,

        /// <summary>Something went wrong and somebody should know. Cancellations.</summary>
        Attention = 1,

        /// <summary>Somebody has to do something, and there is a patient behind it.</summary>
        ActionRequired = 2
    }

    public static class NotificationAlertLevels
    {
        /// <summary>
        /// Reads the level off the notification itself.
        /// </summary>
        /// <remarks>
        /// From the type and the severity the server already sends, never from a list of
        /// event codes kept here: a notice declared as Action Required on the server lands
        /// in the right level without this client being taught about it first. Same rule
        /// the pending tab of the panel uses, so the two cannot disagree.
        /// </remarks>
        public static NotificationAlertLevel For(NotificationDto notification)
        {
            if (notification is null)
                return NotificationAlertLevel.Ambient;

            if (NotificationKeys.RequiresAction(
                    notification.Type,
                    notification.BusinessEventCode,
                    isAcknowledged: false))
            {
                return NotificationAlertLevel.ActionRequired;
            }

            return IsLoud(notification.Severity)
                ? NotificationAlertLevel.Attention
                : NotificationAlertLevel.Ambient;
        }

        private static bool IsLoud(string severity)
        {
            return string.Equals(severity, NotificationKeys.Severity.Warning, StringComparison.OrdinalIgnoreCase)
                || string.Equals(severity, NotificationKeys.Severity.Error, StringComparison.OrdinalIgnoreCase)
                || string.Equals(severity, NotificationKeys.Severity.Critical, StringComparison.OrdinalIgnoreCase);
        }
    }
}
