using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class Role
    {
        public int Id { get; set; }
        [Required]
        public string RoleName { get; set; }
        public string? Description { get; set; }
        public ICollection<User> Users { get; set; }
    }
}
