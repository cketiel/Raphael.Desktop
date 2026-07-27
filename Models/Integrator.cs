using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meditrans.Client.Models
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

        public object Clone() => this.MemberwiseClone();
    }
}
