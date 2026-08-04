using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class Unit
    {
        public int Id { get; set; }
        public string Abbreviation { get; set; }
        public string Description { get; set; }
        public ICollection<BillingItem> BillingItems { get; set; }
    }
}
