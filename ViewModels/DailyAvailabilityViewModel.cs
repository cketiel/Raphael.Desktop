using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel para un item en la lista de Horarios Diarios.
    /// Representa la configuración de disponibilidad para un día específico de la semana.
    /// </summary>
    public class DailyAvailabilityViewModel : BaseViewModel
    {
        public DayOfWeek DayOfWeek { get; }
        public string DayName => DayOfWeek.ToString();

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        private TimeSpan _startTime;
        public TimeSpan StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(); }
        }

        private TimeSpan _endTime;
        public TimeSpan EndTime
        {
            get => _endTime;
            set { _endTime = value; OnPropertyChanged(); }
        }

        public DailyAvailabilityViewModel(DayOfWeek dayOfWeek)
        {
            DayOfWeek = dayOfWeek;
            IsActive = true; // Por defecto, el día está activo
            StartTime = new TimeSpan(8, 0, 0); // Hora por defecto
            EndTime = new TimeSpan(17, 0, 0);  // Hora por defecto
        }
    }
}
