using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class SpaceType
    {
        // RE-083: the vehicle a trip of this type needs, set by the screen. Never sent to the server.
        [System.Text.Json.Serialization.JsonIgnore]
        public int Level { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public int? ClassId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool CanEditClass { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string? ClassWarning { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasClassWarning => ClassWarning != null;
        [System.Text.Json.Serialization.JsonIgnore]
        public string? Kind => Helpers.VehicleCapabilityUi.Kind(Level);
        [System.Text.Json.Serialization.JsonIgnore]
        public string KindText => Level > 0 ? Helpers.VehicleCapabilityUi.KindText(Level) : Services.LocalizationService.Instance["spacetypes.NoClass"];

        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public string Description { get; set; }
        [Required]
        public float LoadTime { get; set; }
        [Required]
        public float UnloadTime { get; set; }
        [Required]
        public int CapacityTypeId { get; set; }
        public CapacityType CapacityType { get; set; }
        public bool IsActive { get; set; }
        public string? ShowNameDescription { get; set; }
        public ICollection<Customer> Customers { get; set; }
        public ICollection<Trip> Trips { get; set; }
        public ICollection<CapacityDetail> CapacityDetails { get; set; }
        public ICollection<FundingSourceBillingItem> FundingSourceBillingItems { get; set; }

        public SpaceType() {
            this.ShowNameDescription = this.Description + " " + this.Name;
        }
    }
}
