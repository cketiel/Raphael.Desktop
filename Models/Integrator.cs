using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class Integrator : ICloneable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime Created { get; set; } = DateTime.Now;

        public int? FundingSourceId { get; set; }
        public string? FundingSourceName { get; set; }

        //This property is not saved in the DB, it is only used for the API request
        public bool RegenerateApiKey { get; set; }

        // ---- Contact details, added in RE-027 --------------------------------------------
        // An integrator used to be a name and a key. That is everything the API needs and
        // nothing of what a person needs when they have to pick up the phone.

        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? ContactName { get; set; }
        public string? Comments { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        /// <summary>The catalog row this one was added from, when it was added from the catalog.</summary>
        public int? CatalogIntegratorId { get; set; }

        /// <summary>
        /// Which company owns this row; null is the general broker. Read-only here — the server
        /// takes it from the token and ignores whatever the client sends.
        /// </summary>
        public int? OwnerProviderId { get; set; }

        public string? OwnerProviderName { get; set; }

        public object Clone() => this.MemberwiseClone();
    }
}
