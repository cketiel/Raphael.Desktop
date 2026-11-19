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

        /// <summary>The clinic that keeps this item in the Booking Portal; null for the office's own (RE-029).</summary>
        public int? OwnerIntegratorId { get; set; }

        /// <summary>That clinic's name, to group the items without another call.</summary>
        public string? OwnerIntegratorName { get; set; }

        /// <summary>0 extra, 1 Miles, 2 Loading Fee, 3 Cancellation (RE-071).</summary>
        public int Kind { get; set; }
        public int SortOrder { get; set; }

        /// <summary>How many funding sources have a rate for this item.</summary>
        public int FundingSourceCount { get; set; }
    }
}
