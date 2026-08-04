using Raphael.Desktop.DTOs;
using System;

namespace Raphael.Desktop.ViewModels
{
    public class TripItemViewModel : BaseViewModel
    {
        private UnscheduledTripDto _tripDto;
        public UnscheduledTripDto TripDto
        {
            get => _tripDto;
            set => SetProperty(ref _tripDto, value);
        }

        public TripItemViewModel(UnscheduledTripDto tripDto)
        {
            _tripDto = tripDto;
        }

        // Propiedades para las columnas del DataGrid "Unscheduled"
        public int Id => _tripDto.Id;
        public DateTime Date => _tripDto.Date;
        public TimeSpan? FromTime => _tripDto.FromTime;
        public TimeSpan? ToTime => _tripDto.ToTime;
        public string PatientFullName => _tripDto.CustomerName;
        public string SpaceTypeName => _tripDto.SpaceType;
        public string PickupAddress => _tripDto.PickupAddress;
        public double? Charge => _tripDto.Charge;
        public double? Paid => _tripDto.Paid;
        public string PickupComment => _tripDto.PickupComment;
        public string DropoffComment => _tripDto.DropoffComment;
        public string Type => _tripDto.Type;
        public string PickupPhone => _tripDto.PickupPhone;
        public string DropoffPhone => _tripDto.DropoffPhone;

        public string? TripId => _tripDto.TripId;
        public string Authorization => _tripDto.Authorization;
        public string FundingSourceName => _tripDto.FundingSource;
        public double? Distance => _tripDto.Distance;
        public string? PickupCity => _tripDto.PickupCity;
        public string? DropoffCity => _tripDto.DropoffCity;
    }
}