using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class RouteFundingSourceDto
    {
        // Only the ID of the financing source to associate is necessary.
        [Required]
        [Range(1, int.MaxValue)]
        public int FundingSourceId { get; set; }
    }
}
