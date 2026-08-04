using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class TripLog
    {
        public int Id { get; set; }
        [Required]
        public int TripId { get; set; }
        public Trip Trip { get; set; } = new Trip();
        [Required]
        public string Status { get; set; } = string.Empty;
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public TimeSpan Time { get; set; }
    }
}
