#nullable enable
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using System;
using System.ComponentModel;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>RE-094: the guide of the hours on the Home trip form.</summary>
    public partial class HomeViewModel
    {
        private static readonly string[] HourGuideInputs =
        {
            nameof(PickupTimePicker), nameof(ApptTimePicker), nameof(IsAppointment), nameof(IsReturn), nameof(IsWillCall), nameof(SelectedSpaceType),
            nameof(PickupLatitude), nameof(PickupLongitude), nameof(DropoffLatitude), nameof(DropoffLongitude), nameof(TripDate), nameof(Distance),
            nameof(IsTripFormOpen), nameof(TripBeingEdited),
        };

        private HourGuideModel? _hourGuideModel;
        public HourGuideModel HourGuide => _hourGuideModel ??= new HourGuideModel(HourGuideRequest, h => PickupTimePicker = DateTime.Today.Add(h), () => CanEditRoutedFields);

        private void HourGuide_InputChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Array.IndexOf(HourGuideInputs, e.PropertyName) >= 0) HourGuide.Changed();
        }

        /// <summary>Only an appointment with both hours, a space type and both places is judged.</summary>
        private FeasibilityPreviewRequestDto? HourGuideRequest()
        {
            if (!IsTripFormOpen || IsWillCall || IsReturn || PickupTimePicker is not { } from || ApptTimePicker is not { } to || SelectedSpaceType is null) return null;
            if (PickupLatitude == 0 || DropoffLatitude == 0) return null;
            return new FeasibilityPreviewRequestDto
            {
                Type = "Appointment", Date = TripDate.Date, FromTime = from.TimeOfDay, ToTime = to.TimeOfDay, SpaceTypeId = SelectedSpaceType.Id,
                PickupLatitude = PickupLatitude, PickupLongitude = PickupLongitude, DropoffLatitude = DropoffLatitude, DropoffLongitude = DropoffLongitude,
                Distance = double.TryParse(Distance?.Split(' ')[0], out var miles) && miles > 0 ? miles : null,
            };
        }

        private bool ConfirmHourGuide() => HourGuide.ConfirmHourGuide();
    }
}
