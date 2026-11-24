using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class Vehicle : ICloneable
    {
        /// <summary>RE-053: the open Out of Service record, set by the screen. Never sent to the server.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DTOs.OutOfServiceRecordDto? OutOfServiceRecord { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsOutOfService => OutOfServiceRecord is not null;

        [System.Text.Json.Serialization.JsonIgnore]
        public string? OutOfServiceText => OutOfServiceRecord is null ? null : Helpers.OutOfServiceUi.Describe(new[] { OutOfServiceRecord });

        /// <summary>RE-065: where its compliance documents stand, set by the screen. Never sent to the server.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DTOs.ComplianceSummaryDto? DocsSummary { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string DocsText => Helpers.ComplianceUi.TextOf(DocsSummary);

        /// <summary>danger, warn, info, ok or none: the colour of the Documents badge.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string DocsState => Helpers.ComplianceUi.StateOf(DocsSummary);

        /// <summary>RE-083: what it can carry (0 not configured, 1 AMB, 2 WCH, 3 STR), set by the screen. Never sent to the server.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int Level { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string? Kind => Helpers.VehicleCapabilityUi.Kind(Level);

        [System.Text.Json.Serialization.JsonIgnore]
        public string KindText => Helpers.VehicleCapabilityUi.KindText(Level);

        /// <summary>The seats of its configuration, for the tooltip of its kind.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? SeatsText { get; set; }

        /// <summary>The company it belongs to, set by the screen from <see cref="OwnerProviderId"/>.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? ProviderName { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsActive => !IsInactive;

        /// <summary>Null for the platform's own fleet. Read only: the server sets it and ignores it on save.</summary>
        public int? OwnerProviderId { get; set; }

        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public string? VIN { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Color { get; set; }
        public int? Year { get; set; }
        public string? Plate { get; set; } // MAS Requirements
        public DateTime? ExpirationDate { get; set; } // MAS Requirements
        public bool IsInactive { get; set; }
        [Required]
        public int GroupId { get; set; }
        public VehicleGroup VehicleGroup { get; set; }
        [Required]
        public int CapacityDetailTypeId { get; set; }
        public CapacityDetailType CapacityDetailType { get; set; }
        [Required]
        public int VehicleTypeId { get; set; }
        public VehicleType VehicleType { get; set; }

        //public ICollection<VehicleRoute> VehicleRoutes { get; set; }

        public object Clone()
        {
            return this.MemberwiseClone(); 
        }

    }
}
