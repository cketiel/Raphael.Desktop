using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; 
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Raphael.Desktop.ViewModels
{
    public partial class RunItemViewModel : ObservableObject
    {
        private VehicleRouteViewModel _vehicleRoute;
        public VehicleRouteViewModel VehicleRoute
        {
            get => _vehicleRoute;
            set => SetProperty(ref _vehicleRoute, value);
        }

        private ObservableCollection<ScheduleDto> _schedules;
        public ObservableCollection<ScheduleDto> Schedules
        {
            get => _schedules;
            set => SetProperty(ref _schedules, value);
        }

        [ObservableProperty]
        private GpsDataDto _lastKnownLocation;

        [ObservableProperty]
        private string _lastEventDescription = "N/A";

        [ObservableProperty]
        private string _nextEventDescription = "N/A";

        [ObservableProperty]
        private int _onBoardCount = 0;

        [ObservableProperty]
        private int _overlapIndex = 0;

        public RunItemViewModel(VehicleRouteViewModel vehicleRoute)
        {
            _vehicleRoute = vehicleRoute;
            Schedules = new ObservableCollection<ScheduleDto>();
        }
       
        public int IsOn { get; set; } = 0; // preguntar como se calcula
        public int MTE { get; set; } = 0; // preguntar como se calcula
        public int ME { get; set; } = 0; // preguntar como se calcula

        public int TripsCount => Schedules.Where(s => s.TripId.HasValue).GroupBy(s => s.TripId).Count();

        public int UnperformedCount => Schedules.Count(s => !s.Performed && s.TripId.HasValue);


        public string Name => VehicleRoute.Name;
        public string DriverFullName => VehicleRoute.Driver; //?.FullName;
        public string VehicleName => VehicleRoute.Vehicle; //?.Name;

        // Lógica para Last Event, Next Event
        public string LastNextEvent
        {
            get
            {
                if (!Schedules.Any()) return "No Events";

                var orderedEvents = Schedules.OrderBy(s => s.Pickup ?? s.Appt).ToList();

                var lastEvent = orderedEvents.LastOrDefault(s => s.Performed);
                var nextEvent = orderedEvents.FirstOrDefault(s => !s.Performed);

                string lastEventText = lastEvent != null ? $"{lastEvent.Name}" : "N/A";
                string nextEventText = nextEvent != null ? $"{nextEvent.Name}" : "N/A";

                //string lastEventText = lastEvent != null ? $"{lastEvent.PerformedTimeFormatted} - {lastEvent.EventType}" : "N/A";
                //string nextEventText = nextEvent != null ? $"{nextEvent.ScheduledTimeFormatted} - {nextEvent.EventType}" : "N/A";

                return $"Last: {lastEventText}\nNext: {nextEventText}";
            }
        }

        public void UpdateCalculatedProperties()
        {
            if (Schedules == null || !Schedules.Any())
            {
                LastEventDescription = "N/A";
                NextEventDescription = "N/A";
                OnBoardCount = 0;
                return;
            }

            // Calcular el último evento realizado
            var lastPerformedEvent = Schedules.Where(s => s.Performed).OrderBy(s => s.Sequence).LastOrDefault();
            LastEventDescription = lastPerformedEvent?.Name ?? "No events performed yet";

            // Calcular el próximo evento pendiente
            var nextEvent = Schedules.FirstOrDefault(s => !s.Performed);
            NextEventDescription = nextEvent?.Name ?? "Route completed";

            // Calcular pasajeros/items a bordo
            int performedPickups = Schedules.Count(s => s.EventType == ScheduleEventType.Pickup && s.Performed);
            int performedDropoffs = Schedules.Count(s => s.EventType == ScheduleEventType.Dropoff && s.Performed);
            OnBoardCount = performedPickups - performedDropoffs;
        }
    }

    // Extensión para ScheduleDto para facilitar la visualización de tiempos
    public static class ScheduleDtoExtensions
    {
        public static string ScheduledTimeFormatted(this ScheduleDto dto)
        {
            if (dto.Pickup.HasValue) return dto.Pickup.Value.ToString(@"hh\:mm");
            if (dto.Appt.HasValue) return dto.Appt.Value.ToString(@"hh\:mm");
            return "N/A";
        }

        public static string PerformedTimeFormatted(this ScheduleDto dto)
        {
            if (dto.Perform.HasValue) return dto.Perform.Value.ToString(@"hh\:mm");
            if (dto.Arrive.HasValue) return dto.Arrive.Value.ToString(@"hh\:mm");
            return "N/A";
        }
    }
}