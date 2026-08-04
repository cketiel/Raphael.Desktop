using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class CapacityDetail
    {
        public int Id { get; set; }
        [Required]
        public int SpaceTypeId { get; set; }
        public SpaceType SpaceType { get; set; }
        [Required]
        public int CapacityDetailTypeId { get; set; }
        public CapacityDetailType CapacityDetailType { get; set; }
        public int Quantity { get; set; } = 0;
    }
}
