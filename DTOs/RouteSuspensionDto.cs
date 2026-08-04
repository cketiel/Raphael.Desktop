using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class RouteSuspensionDto
    {
        public int Id { get; set; } // Id is 0 for new, >0 for existing
        [Required]
        public DateTime SuspensionStart { get; set; }
        [Required]
        public DateTime SuspensionEnd { get; set; }
        [MaxLength(200)]
        public string? Reason { get; set; }
    }
}
