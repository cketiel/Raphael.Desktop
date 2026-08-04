using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class BillingItemDto
    {
        [Required]
        [StringLength(100)]
        public string Description { get; set; }

        [Required]
        public int UnitId { get; set; }

        public bool IsCopay { get; set; }

        [StringLength(50)]
        public string? ARAccount { get; set; }

        [StringLength(50)]
        public string? ARSubAccount { get; set; }

        [StringLength(50)]
        public string? ARCompany { get; set; }

        [StringLength(50)]
        public string? APAccount { get; set; }

        [StringLength(50)]
        public string? APSubAccount { get; set; }

        [StringLength(50)]
        public string? APCompany { get; set; }
    }
}
