using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class FundingSourceBillingItemGetDto
    {
        public int Id { get; set; }
        public int FundingSourceId { get; set; }
        public int BillingItemId { get; set; }
        public int SpaceTypeId { get; set; }
        public decimal Rate { get; set; }
        public string Per { get; set; }
        public bool IsDefault { get; set; }
        public string ProcedureCode { get; set; }
        public decimal? MinCharge { get; set; }
        public decimal? MaxCharge { get; set; }
        public int? GreaterThanMinQty { get; set; }
        public int? LessOrEqualMaxQty { get; set; }
        public int? FreeQty { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string BillingItemDescription { get; set; }
        public string BillingItemUnitAbbreviation { get; set; }
        public string SpaceTypeName { get; set; }
    }
}
