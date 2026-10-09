using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Models
{
    public class BillingItem
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public int UnitId { get; set; }
        public Unit Unit { get; set; }
        public bool IsCopay { get; set; }
        public string? ARAccount { get; set; }
        public string? ARSubAccount { get; set; }
        public string? ARCompany { get; set; }
        public string? APAccount { get; set; }
        public string? APSubAccount { get; set; }
        public string? APCompany { get; set; }
        public ICollection<FundingSourceBillingItem> FundingSourceBillingItems { get; set; }

        /// <summary>
        /// The clinic that keeps this item from the Booking Portal; null for the office's own (RE-029).
        /// </summary>
        /// <remarks>
        /// Read only here: the backend's office endpoints never change the owner, so saving an item
        /// from the Desktop keeps it with its clinic.
        /// </remarks>
        public int? OwnerIntegratorId { get; set; }

        public string? OwnerIntegratorName { get; set; }

        [JsonIgnore]
        public bool IsOfficeOwned => OwnerIntegratorId == null;

        /// <summary>The group this item is listed under: the office, or its clinic's name.</summary>
        [JsonIgnore]
        public string OwnerGroup => IsOfficeOwned
            ? LocalizationService.Instance["BillingItemOwnerOffice"]
            : OwnerIntegratorName ?? $"#{OwnerIntegratorId}";

        /// <summary>The office first, then the clinics by name.</summary>
        [JsonIgnore]
        public string OwnerSortKey => IsOfficeOwned ? "0" : "1" + OwnerGroup;

        /// <summary>How the item reads in a rate's drop-down: the clinic's name after its own items.</summary>
        [JsonIgnore]
        public string DisplayName => IsOfficeOwned ? Description : $"{Description} — {OwnerGroup}";
    }
}
