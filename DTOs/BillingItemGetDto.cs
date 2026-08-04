using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class BillingItemGetDto
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public bool IsCopay { get; set; }
        public string ARAccount { get; set; }
        public string ARSubAccount { get; set; }
        public string ARCompany { get; set; }
        public string APAccount { get; set; }
        public string APSubAccount { get; set; }
        public string APCompany { get; set; }
        public string UnitAbbreviation { get; set; }
        public int UnitId { get; set; }
    }
}
