using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// What the trip form lets the dispatcher change, and the banner that says why not (RE-051).
    /// </summary>
    /// <remarks>
    /// Two banners, never confused: grey for a trip that is history (past day or closed), amber for
    /// a trip on a route. The amber one names the fields, frames each of them in the same amber, and
    /// takes the dispatcher to the trip's two stops in Schedule, which is where the fix is. They float
    /// over the form, can be dragged off whatever they cover, and close down to a lock chip for
    /// someone who came to change a phone number and already knows all this.
    /// </remarks>
    public partial class HomeViewModel
    {
        private string _correctionReason;

        /// <summary>
        /// The reason of an admin's correction of a read-only trip; null when there is none.
        /// </summary>
        public string CorrectionReason
        {
            get => _correctionReason;
            private set
            {
                if (_correctionReason == value) return;
                _correctionReason = value;
                RaiseTripLockState();
            }
        }

        private bool _isTripNoticeOpen = true;

        /// <summary>
        /// Whether the floating notice over the trip form is open, or closed down to its lock chip.
        /// </summary>
        /// <remarks>
        /// ⚠️ The notice floats over the form and never takes room in it: the form is sized to what
        /// the divider leaves, and a banner inside it pushed the tabs and the calendar out of sight.
        /// Closing it leaves a chip, so nobody forgets why the fields do not answer.
        /// </remarks>
        public bool IsTripNoticeOpen
        {
            get => _isTripNoticeOpen;
            set
            {
                if (_isTripNoticeOpen == value) return;
                _isTripNoticeOpen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowRoutedLockHighlight));
                OnPropertyChanged(nameof(ShowTripNotice));
                OnPropertyChanged(nameof(ShowTripNoticeChip));
            }
        }

        /// <summary>The trip on the form has something to say: read-only, under correction, or on a route.</summary>
        public bool HasTripNotice => IsEditingReadOnlyTrip || IsCorrectingTrip || IsEditingRoutedTrip;

        public bool ShowTripNotice => HasTripNotice && IsTripNoticeOpen;

        public bool ShowTripNoticeChip => HasTripNotice && !IsTripNoticeOpen;

        /// <summary>What the chip says, the most binding state first.</summary>
        public string TripNoticeChipText =>
            IsEditingReadOnlyTrip ? LocalizationService.Instance["lock.ChipReadOnly"]
            : IsCorrectingTrip ? LocalizationService.Instance["lock.ChipCorrection"]
            : IsEditingRoutedTrip ? LocalizationService.Instance["lock.ChipRouted"]
            : null;

        private bool IsEditingExistingTrip =>
            CurrentMode == HomeMode.EditingTrip && TripBeingEdited != null && TripBeingEdited.Id > 0;

        /// <summary>The trip on the form is on a route: its stops hold a copy of the locked fields.</summary>
        public bool IsEditingRoutedTrip => IsEditingExistingTrip && TripBeingEdited.VehicleRouteId > 0;

        /// <summary>The trip on the form is history, and no correction has been opened on it.</summary>
        public bool IsEditingReadOnlyTrip =>
            IsEditingExistingTrip && TripGuard.IsReadOnly(TripBeingEdited.Date, TripBeingEdited.Status) && CorrectionReason == null;

        /// <summary>An admin is correcting a read-only trip; the save will carry their reason.</summary>
        public bool IsCorrectingTrip => IsEditingExistingTrip && CorrectionReason != null;

        public bool CanEditTripFields => !IsEditingReadOnlyTrip;

        /// <summary>Date, hours, addresses, type, patient, space type, funding source, authorization.</summary>
        public bool CanEditRoutedFields => CanEditTripFields && !IsEditingRoutedTrip;

        /// <summary>Frames are drawn while the notice is open, and go with it.</summary>
        public bool ShowRoutedLockHighlight => IsEditingRoutedTrip && IsTripNoticeOpen;

        public bool CanStartCorrection => IsEditingReadOnlyTrip && TripGuard.IsAdmin;

        /// <summary>What makes it read-only, in bold on the notice: a past day, or a closed trip.</summary>
        public string ReadOnlyReasonText => IsEditingExistingTrip
            ? TripGuard.ReadOnlyReason(TripBeingEdited.Date, TripBeingEdited.Status)
            : null;

        public string ReadOnlyExplanationText => LocalizationService.Instance["lock.ReadOnlyExplanation"];

        /// <summary>The calendar cannot move the trip's day: it is history, or on a route.</summary>
        public bool IsCalendarLocked => IsTripFormOpen && !CanEditRoutedFields;

        public double CalendarOpacity => IsCalendarLocked ? 0.6 : 1.0;

        public string CalendarLockedToolTip => IsEditingReadOnlyTrip
            ? LocalizationService.Instance["lock.SaveBlockedReadOnly"]
            : RoutedLockFieldToolTip;

        public string CorrectionBannerText => CorrectionReason == null
            ? null
            : string.Format(LocalizationService.Instance["lock.CorrectionActive"], CorrectionReason);

        public string RoutedLockTitle => IsEditingRoutedTrip
            ? string.Format(LocalizationService.Instance["lock.RoutedTitle"],
                string.IsNullOrWhiteSpace(TripBeingEdited.RunName) ? "#" + TripBeingEdited.VehicleRouteId : TripBeingEdited.RunName,
                TripBeingEdited.Date.ToString("d"))
            : null;

        public string RoutedLockWhy => LocalizationService.Instance["lock.RoutedWhy"];
        public string RoutedLockHowToFix => LocalizationService.Instance["lock.RoutedHowToFix"];
        public string RoutedLockStillEditable => LocalizationService.Instance["lock.RoutedStillEditable"];
        public string RoutedLockShowInSchedule => LocalizationService.Instance["lock.ShowInSchedule"];
        public string TripNoticeCloseToolTip => LocalizationService.Instance["lock.NoticeClose"];
        public string TripNoticeDragToolTip => LocalizationService.Instance["lock.NoticeDrag"];
        public string TripNoticeChipToolTip => LocalizationService.Instance["lock.ChipToolTip"];
        public string RoutedLockFieldToolTip => LocalizationService.Instance["lock.RoutedFieldToolTip"];
        public string StartCorrectionLabel => LocalizationService.Instance["lock.StartCorrection"];

        private void RaiseTripLockState()
        {
            OnPropertyChanged(nameof(CorrectionReason));
            OnPropertyChanged(nameof(IsEditingRoutedTrip));
            OnPropertyChanged(nameof(IsEditingReadOnlyTrip));
            OnPropertyChanged(nameof(IsCorrectingTrip));
            OnPropertyChanged(nameof(CanEditTripFields));
            OnPropertyChanged(nameof(CanEditRoutedFields));
            OnPropertyChanged(nameof(ShowRoutedLockHighlight));
            OnPropertyChanged(nameof(HasTripNotice));
            OnPropertyChanged(nameof(ShowTripNotice));
            OnPropertyChanged(nameof(ShowTripNoticeChip));
            OnPropertyChanged(nameof(TripNoticeChipText));
            OnPropertyChanged(nameof(CanStartCorrection));
            OnPropertyChanged(nameof(ReadOnlyReasonText));
            OnPropertyChanged(nameof(IsCalendarLocked));
            OnPropertyChanged(nameof(CalendarOpacity));
            OnPropertyChanged(nameof(CalendarLockedToolTip));
            OnPropertyChanged(nameof(CorrectionBannerText));
            OnPropertyChanged(nameof(RoutedLockTitle));
            OnPropertyChanged(nameof(CreateTripBlockedReason));
            OnPropertyChanged(nameof(CanCreateTrip));
            OnPropertyChanged(nameof(CreateTripHint));
        }

        [RelayCommand] private void CloseTripNotice() => IsTripNoticeOpen = false;

        [RelayCommand] private void OpenTripNotice() => IsTripNoticeOpen = true;

        /// <summary>Opens Schedule on the trip's route and day, with its two stops picked out.</summary>
        [RelayCommand]
        private void ShowEditedTripInSchedule()
        {
            if (!IsEditingRoutedTrip) return;
            AppNavigation.ShowTripInSchedule(TripBeingEdited.Date, TripBeingEdited.VehicleRouteId, TripBeingEdited.Id);
        }

        /// <summary>An admin opens a correction of a read-only trip, and is asked why first.</summary>
        [RelayCommand]
        private void StartCorrection()
        {
            if (!CanStartCorrection) return;

            var reason = TripGuard.AskForCorrection(
                TripGuard.ReadOnlyReason(TripBeingEdited.Date, TripBeingEdited.Status),
                LocalizationService.Instance["lock.OpEdit"]);

            if (reason == null) return;

            CorrectionReason = reason;
            // The notice turns purple: show it, even if the grey one had been closed.
            IsTripNoticeOpen = true;
        }

        /// <summary>
        /// Puts back, on an update of a routed trip, every field its stops were copied from.
        /// </summary>
        private static void KeepRoutedFields(TripReadDto dto, TripReadDto original)
        {
            dto.Date = original.Date;
            dto.Day = original.Day;
            dto.FromTime = original.FromTime;
            dto.ToTime = original.ToTime;
            dto.PickupAddress = original.PickupAddress;
            dto.PickupLatitude = original.PickupLatitude;
            dto.PickupLongitude = original.PickupLongitude;
            dto.PickupCity = original.PickupCity;
            dto.DropoffAddress = original.DropoffAddress;
            dto.DropoffLatitude = original.DropoffLatitude;
            dto.DropoffLongitude = original.DropoffLongitude;
            dto.DropoffCity = original.DropoffCity;
            dto.Type = original.Type;
            dto.CustomerId = original.CustomerId;
            dto.SpaceTypeId = original.SpaceTypeId;
            dto.FundingSourceId = original.FundingSourceId;
            dto.Authorization = original.Authorization;
            dto.Distance = original.Distance;
        }
    }
}
