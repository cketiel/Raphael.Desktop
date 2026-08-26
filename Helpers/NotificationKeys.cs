namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// The strings the notification payload speaks, mirrored from the server.
    /// </summary>
    /// <remarks>
    /// Raphael.Shared is not shared with client applications, so these constants are a
    /// hand-kept copy of <c>NotificationMetadataKeys</c>, <c>BusinessEventCodes</c> and
    /// the enumeration names. Gathering them in one file means a rename on the server
    /// breaks in one place instead of a dozen scattered literals.
    /// See <c>_meta/CONTRACT_MAP.md</c>.
    /// </remarks>
    public static class NotificationKeys
    {
        /// <summary>Keys carried in <c>NotificationDto.Metadata</c>.</summary>
        public static class Metadata
        {
            public const string MessageKey = "MessageKey";
            public const string TripId = "TripId";
            public const string RiderId = "RiderId";
            public const string TripDate = "TripDate";
            public const string TripTime = "TripTime";
            public const string CancelledBy = "CancelledBy";
            public const string PerformedByUserId = "PerformedByUserId";
            public const string EtaMinutes = "EtaMinutes";
            public const string WillCallActivatedAtUtc = "WillCallActivatedAtUtc";
            public const string WillCallDeadlineUtc = "WillCallDeadlineUtc";
        }

        /// <summary>Business events that reach the dispatch office today.</summary>
        public static class Events
        {
            public const string TripScheduled = "TRIP_SCHEDULED";
            public const string TripCancelled = "TRIP_CANCELLED";

            /// <summary>A cancelled trip was put back in service.</summary>
            public const string TripReactivated = "TRIP_REACTIVATED";

            /// <summary>
            /// A trip became a Will Call and now waits for the patient to say they are
            /// ready. ⚠️ Not a "deactivation": nothing is switched off.
            /// </summary>
            public const string WillCallCreated = "WILL_CALL_CREATED";
            public const string DriverStartedTrip = "DRIVER_STARTED_TRIP";
            public const string DriverArrivedPickup = "DRIVER_ARRIVED_PICKUP";
            public const string DriverPickedUpPassenger = "DRIVER_PICKED_UP_PASSENGER";
            public const string DriverCompletedTrip = "DRIVER_COMPLETED_TRIP";
            public const string WillCallActivated = "WILL_CALL_ACTIVATED";
            public const string WillCallAcknowledged = "WILL_CALL_ACKNOWLEDGED";
        }

        /// <summary>
        /// Severity as the API sends it. ⚠️ The server serialises the enumeration
        /// <c>Name</c>, not its <c>Code</c>: "Information", never "INFORMATION".
        /// Compared case-insensitively so a change of convention does not go silent.
        /// </summary>
        public static class Severity
        {
            public const string Information = "Information";
            public const string Success = "Success";
            public const string Warning = "Warning";
            public const string Error = "Error";
            public const string Critical = "Critical";
        }

        /// <summary>
        /// Notification type. <c>ActionRequired</c> is the hook the pending queue filters
        /// on, so any future notice declared as such lands there without touching the panel.
        /// </summary>
        public static class Type
        {
            public const string ActionRequired = "Action Required";
        }

        /// <summary>
        /// Notification status, as the API sends it — the enumeration <c>Name</c>.
        /// </summary>
        public static class Status
        {
            /// <summary>
            /// The one state the cleanup never touches: neither expired nor deleted.
            /// </summary>
            public const string Archived = "Archived";
        }

        /// <summary>
        /// Whether a notice is still waiting for somebody in the office to take charge.
        /// </summary>
        /// <remarks>
        /// Driven by the notification type, not by a list of event codes, so a notice
        /// declared as action required on the server lands in the pending queue without
        /// this client being taught about it first. The Will Call is named explicitly as
        /// well because it is the one with a patient waiting behind it and it must not
        /// depend on the catalog being typed correctly.
        /// </remarks>
        public static bool RequiresAction(
            string notificationType,
            string businessEventCode,
            bool isAcknowledged)
        {
            if (isAcknowledged)
                return false;

            return string.Equals(
                       notificationType,
                       Type.ActionRequired,
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       businessEventCode,
                       Events.WillCallActivated,
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The trip a notification is about, or null when it is not about one.
        /// </summary>
        /// <remarks>
        /// The metadata carries every value as text. Reading it in one place means a
        /// screen that reacts to a notification and the panel that lists it cannot
        /// disagree about which trip it names.
        /// </remarks>
        public static bool TryGetTripId(
            Models.NotificationDto notification,
            out int tripId)
        {
            tripId = 0;

            return notification is not null
                   && notification.Metadata.TryGetValue(Metadata.TripId, out var raw)
                   && int.TryParse(raw, out tripId);
        }

        /// <summary>Who cancelled a trip, as carried in the metadata.</summary>
        public static class CancelledBy
        {
            public const string Dispatcher = "DISPATCHER";
            public const string Driver = "DRIVER";
            public const string Rider = "RIDER";
            public const string Facility = "FACILITY";
            public const string Integrator = "INTEGRATOR";
            public const string Bot = "BOT";
        }
    }
}
