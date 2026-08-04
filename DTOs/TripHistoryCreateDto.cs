using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class TripHistoryCreateDto
    {
        public int TripId { get; set; }
        public DateTime ChangeDate { get; set; }
        public string User { get; set; }
        public string Field { get; set; }
        public string? PriorValue { get; set; }
        public string? NewValue { get; set; }
    }
}
