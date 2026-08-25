using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// The one place where a trip's Will Call state is decided.
    /// </summary>
    /// <remarks>
    /// Two operations on one screen, because they are the same decision read from either
    /// side: a trip either waits for its patient to call, or it does not.
    ///
    /// <para>
    /// ⚠️ Every hour here is wall-clock time where the trip is operated, taken from the
    /// provider's zone that came with the trip — never the clock of the dispatcher's
    /// machine. A dispatcher covering a shift from another region would otherwise start the
    /// one-hour promise at an hour that does not exist at the pickup address.
    /// See <c>_meta/TIME_POLICY.md</c> §2B.
    /// </para>
    /// </remarks>
    public sealed class WillCallDialogViewModel : BaseViewModel
    {
        /// <summary>
        /// What the office has, by agreement, to get a vehicle to a patient who says they
        /// are ready.
        /// </summary>
        private static readonly TimeSpan Commitment = TimeSpan.FromHours(1);

        /// <summary>
        /// The pickup time that means "waiting on the patient". A convention the whole
        /// office reads, not a computation.
        /// </summary>
        private static readonly TimeSpan WillCallPickupTime = new(23, 59, 0);

        private DateTime? _pickupTime;

        public WillCallDialogViewModel(UnscheduledTripDto trip)
        {
            Trip = trip;

            // Activating is for a trip that is still waiting on its patient.
            IsActivating = trip.WillCall;

            OperationNow = ResolveOperationNow(trip.ProviderTimeZoneId);

            _pickupTime = DateTime.Today + (IsActivating
                ? OperationNow
                : WillCallPickupTime);
        }

        public UnscheduledTripDto Trip { get; }

        /// <summary>True for activate, false for the reverse.</summary>
        public bool IsActivating { get; }

        /// <summary>Time of day where the trip is operated, at the moment this opened.</summary>
        public TimeSpan OperationNow { get; }

        public bool WasSaved => true;

        public bool WasCancelled => false;

        /// <summary>
        /// The pickup time being proposed. A <see cref="DateTime"/> because that is what the
        /// MaterialDesign time picker binds to; only its time of day is ever used.
        /// </summary>
        public DateTime? PickupTime
        {
            get => _pickupTime;
            set
            {
                if (!SetProperty(ref _pickupTime, value))
                    return;

                OnPropertyChanged(nameof(HasWarning));
                OnPropertyChanged(nameof(WarningText));
            }
        }

        /// <summary>The time to send, or null to let the server use the operation's clock.</summary>
        public TimeSpan? SelectedFromTime => PickupTime?.TimeOfDay;

        #region What the dispatcher reads

        public string Title => IsActivating
            ? LocalizationService.Instance["WillCallActivateTitle"]
            : LocalizationService.Instance["WillCallRevertTitle"];

        public string Explanation => IsActivating
            ? LocalizationService.Instance["WillCallActivateExplain"]
            : LocalizationService.Instance["WillCallRevertExplain"];

        public string ConfirmText => IsActivating
            ? LocalizationService.Instance["WillCallActivateAction"]
            : LocalizationService.Instance["WillCallRevertAction"];

        public string PickupTimeHint => LocalizationService.Instance["WillCallPickupTime"];

        public string PatientLabel => Trip?.CustomerName ?? string.Empty;

        /// <summary>"Viaje 418 · 09:15", so the decision is not taken blind.</summary>
        public string TripLabel
        {
            get
            {
                if (Trip is null)
                    return string.Empty;

                var number = string.IsNullOrWhiteSpace(Trip.TripId)
                    ? Trip.Id.ToString()
                    : Trip.TripId;

                var current = Trip.FromTime.HasValue
                    ? $" · {Format(Trip.FromTime.Value)}"
                    : string.Empty;

                return $"{LocalizationService.Instance["NotificationTrip"]} {number}{current}";
            }
        }

        /// <summary>"Allí son las 3:42 PM ahora mismo", the clock the decision is made on.</summary>
        public string OperationNowText => string.Format(
            LocalizationService.Instance["WillCallOperationNow"],
            Format(OperationNow));

        #endregion

        #region The warning

        /// <summary>
        /// True when the hour chosen breaks the office's own convention.
        /// </summary>
        /// <remarks>
        /// It only warns. A dispatcher on the phone with a clinic knows things the rule does
        /// not, so the rule explains itself and then gets out of the way.
        /// </remarks>
        public bool HasWarning
        {
            get
            {
                if (!SelectedFromTime.HasValue)
                    return false;

                return IsActivating
                    ? SelectedFromTime.Value > OperationNow.Add(Commitment)
                    : SelectedFromTime.Value != WillCallPickupTime;
            }
        }

        public string WarningText => IsActivating
            ? string.Format(
                LocalizationService.Instance["WillCallActivateWarning"],
                Format(OperationNow.Add(Commitment)))
            : string.Format(
                LocalizationService.Instance["WillCallRevertWarning"],
                Format(WillCallPickupTime));

        #endregion

        /// <summary>
        /// The current time of day where the trip is operated.
        /// </summary>
        /// <remarks>
        /// Falls back to this machine's clock only when the server sent no zone, which it
        /// should never do: it resolves the provider's fallback chain before answering. The
        /// fallback is here so a missing zone costs an hour of accuracy, not the feature.
        /// </remarks>
        private static TimeSpan ResolveOperationNow(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return DateTime.Now.TimeOfDay;

            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).TimeOfDay;
            }
            catch
            {
                return DateTime.Now.TimeOfDay;
            }
        }

        private static string Format(TimeSpan time) =>
            (DateTime.Today + time).ToString("t");
    }
}
