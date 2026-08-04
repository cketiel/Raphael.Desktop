using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using System;

namespace Raphael.Desktop.ViewModels
{
    public class EditTripDialogViewModel : BaseViewModel
    {     
        public string Title { get; set; }
        public bool IsAppointmentType { get; set; }
        public bool IsReturnType { get; set; }
        public DateTime? FromTime { get; set; }
        public bool WillCall { get; set; }
        public string PickupPhone { get; set; }
        public string PickupComment { get; set; }
        public string DropoffPhone { get; set; }
        public string DropoffComment { get; set; }

        public bool WasSaved => true;
        public bool WasCancelled => false;

        public EditTripDialogViewModel(TripReadDto trip) {
            Title = "Trip Edit - " + trip.CustomerName;
            IsAppointmentType = trip.Type == TripType.Appointment;
            IsReturnType = trip.Type == TripType.Return;
            
            if (trip.FromTime.HasValue)
            {
                FromTime = DateTime.Today + trip.FromTime.Value;
            }
            WillCall = trip.WillCall;
            PickupPhone = trip.PickupPhone;
            PickupComment = trip.PickupComment;
            DropoffPhone = trip.DropoffPhone;
            DropoffComment = trip.DropoffComment;
        }
        public EditTripDialogViewModel(UnscheduledTripDto trip)
        {
            Title = "Trip Edit - " + trip.CustomerName;
            IsAppointmentType = trip.Type == TripType.Appointment;
            IsReturnType = trip.Type == TripType.Return;
            // MaterialDesign TimePicker usa DateTime?, así que convertimos el TimeSpan
            if (trip.FromTime.HasValue)
            {
                FromTime = DateTime.Today + trip.FromTime.Value;
            }
            WillCall = trip.WillCall;
            PickupPhone = trip.PickupPhone;
            PickupComment = trip.PickupComment;
            DropoffPhone = trip.DropoffPhone;
            DropoffComment = trip.DropoffComment;
        }      
        public TripDispatchUpdateDto GetUpdatedDto()
        {
            return new TripDispatchUpdateDto
            {
                Type = IsAppointmentType ? TripType.Appointment : TripType.Return,
                FromTime = FromTime?.TimeOfDay,
                WillCall = WillCall,
                PickupPhone = PickupPhone,
                PickupComment = PickupComment,
                DropoffPhone = DropoffPhone,
                DropoffComment = DropoffComment
            };
        }
    }
}