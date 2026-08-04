using Raphael.Desktop.Models;
using System;
using System.Linq;

namespace Raphael.Desktop.ViewModels
{
    // This ViewModel wraps the VehicleRoute model to add presentation logic.
    public class VehicleRouteViewModel : BaseViewModel
    {
        private readonly VehicleRoute _route;
        public VehicleRoute Model => _route;

        public VehicleRouteViewModel(VehicleRoute route)
        {
            _route = route;
        }

        public int Id => _route.Id;
        public string Name => _route.Name;
        public string Description => _route.Description;
        public string SmartphoneLogin => _route.SmartphoneLogin;
        public string Driver => _route.Driver?.FullName;
        public string Vehicle => _route.Vehicle?.Name;
        public string Garage => _route.Garage;
        public DateTime FromDate => _route.FromDate;
        public DateTime? ToDate => _route.ToDate;
        public TimeSpan FromTime => _route.FromTime;
        public TimeSpan ToTime => _route.ToTime;
        public int VehicleGroupId => _route.Vehicle?.GroupId ?? 0;

        public double GarageLatitude => _route.GarageLatitude;
        public double GarageLongitude => _route.GarageLongitude;

        public bool IsActive
        {
            get
            {
                var now = DateTime.UtcNow;
                bool inDateRange = now >= _route.FromDate && (_route.ToDate == null || now <= _route.ToDate);
                bool isSuspended = _route.Suspensions.Any(s => now >= s.SuspensionStart && now <= s.SuspensionEnd);
                return inDateRange && !isSuspended;
            }
        }

        // Propiedades para las columnas de días de la semana
        private bool IsDayAvailable(DayOfWeek day)
        {
            var availability = _route.Availabilities?.FirstOrDefault(a => a.DayOfWeek == day);
            // Si hay una configuración específica para el día, usarla.
            // Si no hay ninguna configuración en Availabilities, se asume que está disponible por defecto.
            return availability?.IsActive ?? true;
        }

        public bool Sunday => IsDayAvailable(DayOfWeek.Sunday);
        public bool Monday => IsDayAvailable(DayOfWeek.Monday);
        public bool Tuesday => IsDayAvailable(DayOfWeek.Tuesday);
        public bool Wednesday => IsDayAvailable(DayOfWeek.Wednesday);
        public bool Thursday => IsDayAvailable(DayOfWeek.Thursday);
        public bool Friday => IsDayAvailable(DayOfWeek.Friday);
        public bool Saturday => IsDayAvailable(DayOfWeek.Saturday);

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsActive));
            // Notificar todos los cambios por si el modelo subyacente cambió
            OnPropertyChanged(string.Empty);
        }
    }
}