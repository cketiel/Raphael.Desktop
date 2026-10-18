using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    public class EditTripDialogViewModel : BaseViewModel
    {
        // ⚠️ No WillCall here. It is the one field of a trip that carries a promise of
        // an hour to a patient, so it moves only through Activate / Revert on the
        // open-trips grid of Schedule, where it leaves a history row and a notification.
        public string Title { get; set; }
        public bool IsAppointmentType { get; set; }
        public bool IsReturnType { get; set; }
        public DateTime? FromTime { get; set; }
        public string PickupPhone { get; set; }
        public string DropoffPhone { get; set; }

        // Notifying: the notes window (RE-054) writes back what the driver now reads.
        private string _pickupComment;
        public string PickupComment
        {
            get => _pickupComment;
            set { _pickupComment = value; OnPropertyChanged(); }
        }

        private string _dropoffComment;
        public string DropoffComment
        {
            get => _dropoffComment;
            set { _dropoffComment = value; OnPropertyChanged(); }
        }

        /// <summary>The trip being edited, for the notes window.</summary>
        public int TripId { get; }

        /// <summary>Opens the notes for the driver at one end ("Pickup" or "Dropoff"). Saved there, at once.</summary>
        public ICommand OpenNotesCommand { get; }
        public string NotesOpenText => LocalizationService.Instance["notes.Open"];

        private void OpenNotes(object side)
        {
            if (TripId == 0) return;
            var dialog = new Views.TripNotesDialog(TripId, side as string);
            dialog.ShowDialog();
            if (!dialog.Changed || dialog.Result is not { } notes) return;
            PickupComment = notes.Pickup.Composed;
            DropoffComment = notes.Dropoff.Composed;
        }

        public bool WasSaved => true;
        public bool WasCancelled => false;

        // RE-051: on a routed trip the type and the pickup hour are copied onto its stops — the
        // type sets the early-arrival margin and the hour is what the wait is measured from — so
        // both stay as they are, framed, with the reason and the way out above them.
        private readonly string _originalType;
        private readonly TimeSpan? _originalFromTime;

        public bool IsRouted { get; }
        public bool CanEditRoutedFields => !IsRouted;

        private bool _isRoutedLockExpanded = true;
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

        public bool ShowRoutedLockHighlight => IsRouted && IsRoutedLockExpanded;
        public bool IsRoutedLockCollapsed => IsRouted && !IsRoutedLockExpanded;

        public string RoutedLockTitle { get; }
        public string RoutedLockWhy => LocalizationService.Instance["lock.DialogRoutedWhy"];
        public string RoutedLockHowToFix => LocalizationService.Instance["lock.RoutedHowToFix"];
        public string RoutedLockShowInSchedule => LocalizationService.Instance["lock.ShowInSchedule"];
        public string RoutedLockHide => LocalizationService.Instance["lock.Hide"];
        public string RoutedLockShow => LocalizationService.Instance["lock.Show"];
        public string RoutedLockCollapsedText => LocalizationService.Instance["lock.DialogRoutedCollapsed"];
        public string RoutedLockFieldToolTip => LocalizationService.Instance["lock.RoutedFieldToolTip"];

        /// <summary>Raised by the link to the trip's stops; the screen that opened the dialog closes it.</summary>
        public event Action ShowInScheduleRequested;

        public ICommand ShowInScheduleCommand { get; }
        public ICommand HideRoutedLockCommand { get; }
        public ICommand ShowRoutedLockCommand { get; }

        public EditTripDialogViewModel(TripReadDto trip) {
            TripId = trip.Id;
            OpenNotesCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object>(OpenNotes);
            Title = "Trip Edit - " + trip.CustomerName;
            IsAppointmentType = trip.Type == TripType.Appointment;
            IsReturnType = trip.Type == TripType.Return;

            if (trip.FromTime.HasValue)
            {
                FromTime = DateTime.Today + trip.FromTime.Value;
            }
            PickupPhone = trip.PickupPhone;
            PickupComment = trip.PickupComment;
            DropoffPhone = trip.DropoffPhone;
            DropoffComment = trip.DropoffComment;

            _originalType = trip.Type;
            _originalFromTime = trip.FromTime;
            IsRouted = trip.VehicleRouteId > 0;
            if (IsRouted)
            {
                RoutedLockTitle = string.Format(LocalizationService.Instance["lock.RoutedTitle"],
                    string.IsNullOrWhiteSpace(trip.RunName) ? "#" + trip.VehicleRouteId : trip.RunName,
                    trip.Date.ToString("d"));
            }

            ShowInScheduleCommand = new Helpers.RelayCommand(() => ShowInScheduleRequested?.Invoke());
            HideRoutedLockCommand = new Helpers.RelayCommand(() => IsRoutedLockExpanded = false);
            ShowRoutedLockCommand = new Helpers.RelayCommand(() => IsRoutedLockExpanded = true);
        }
        public EditTripDialogViewModel(UnscheduledTripDto trip)
        {
            TripId = trip.Id;
            OpenNotesCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object>(OpenNotes);
            Title = "Trip Edit - " + trip.CustomerName;
            IsAppointmentType = trip.Type == TripType.Appointment;
            IsReturnType = trip.Type == TripType.Return;
            // MaterialDesign TimePicker usa DateTime?, así que convertimos el TimeSpan
            if (trip.FromTime.HasValue)
            {
                FromTime = DateTime.Today + trip.FromTime.Value;
            }
            PickupPhone = trip.PickupPhone;
            PickupComment = trip.PickupComment;
            DropoffPhone = trip.DropoffPhone;
            DropoffComment = trip.DropoffComment;

            // An open trip is not on a route: nothing here is locked.
            _originalType = trip.Type;
            _originalFromTime = trip.FromTime;
        }
        public TripDispatchUpdateDto GetUpdatedDto()
        {
            return new TripDispatchUpdateDto
            {
                Type = IsRouted ? _originalType : (IsAppointmentType ? TripType.Appointment : TripType.Return),
                FromTime = IsRouted ? _originalFromTime : FromTime?.TimeOfDay,
                PickupPhone = PickupPhone,
                PickupComment = PickupComment,
                DropoffPhone = DropoffPhone,
                DropoffComment = DropoffComment
            };
        }
    }
}
