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
    /// takes the dispatcher to the trip's two stops in Schedule, which is where the fix is. It can be
    /// folded away for someone who came to change a phone number and already knows all this.
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

        private bool _isRoutedLockExpanded = true;

        /// <summary>Whether the routed banner shows its explanation, or folds into one line.</summary>
        public bool IsRoutedLockExpanded
        {
            get => _isRoutedLockExpanded;
            set
            {
                if (_isRoutedLockExpanded == value) return;
                _isRoutedLockExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowRoutedLockHighlight));
                OnPropertyChanged(nameof(IsRoutedLockCollapsed));
            }
        }

        public bool IsRoutedLockCollapsed => IsEditingRoutedTrip && !IsRoutedLockExpanded;

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

        /// <summary>Frames are drawn while the explanation is open, and go with it.</summary>
        public bool ShowRoutedLockHighlight => IsEditingRoutedTrip && IsRoutedLockExpanded;

        public bool CanStartCorrection => IsEditingReadOnlyTrip && TripGuard.IsAdmin;

        public string ReadOnlyBannerText => IsEditingExistingTrip
            ? TripGuard.ReadOnlyReason(TripBeingEdited.Date, TripBeingEdited.Status) + " " +
              LocalizationService.Instance["lock.ReadOnlyExplanation"]
            : null;

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
        public string RoutedLockHide => LocalizationService.Instance["lock.Hide"];
        public string RoutedLockShow => LocalizationService.Instance["lock.Show"];
        public string RoutedLockCollapsedText => LocalizationService.Instance["lock.RoutedCollapsed"];
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
            OnPropertyChanged(nameof(IsRoutedLockCollapsed));
            OnPropertyChanged(nameof(CanStartCorrection));
            OnPropertyChanged(nameof(ReadOnlyBannerText));
            OnPropertyChanged(nameof(CorrectionBannerText));
            OnPropertyChanged(nameof(RoutedLockTitle));
            OnPropertyChanged(nameof(CreateTripBlockedReason));
            OnPropertyChanged(nameof(CanCreateTrip));
            OnPropertyChanged(nameof(CreateTripHint));
        }

        [RelayCommand] private void HideRoutedLock() => IsRoutedLockExpanded = false;

        [RelayCommand] private void ShowRoutedLock() => IsRoutedLockExpanded = true;

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

            if (reason != null) CorrectionReason = reason;
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
