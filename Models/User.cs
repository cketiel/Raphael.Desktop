using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class User
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

        /// <summary>RE-082: the pay rate in force today, set by the screen for an administrator. Never sent.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DTOs.DriverPayRateDto? PayRate { get; set; }

        /// <summary>"$15.00/h", "$1.00/mi", or empty when there is none (or the viewer is not an admin).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string PayRateText => PayRate?.Label ?? string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsPerMile => PayRate?.RateType == DTOs.DriverPayRateType.PerMile;

        /// <summary>
        /// A driver works a route from Raphael.Driver. Role 2 everywhere today (the API's
        /// DriverRoleIds); the name check keeps a renamed or second driver role counted.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsDriver => RoleId == 2 || (RoleName ?? string.Empty).Contains("Driver", StringComparison.OrdinalIgnoreCase);

        /// <summary>Signs in through an integrator: an outside company's account, not Raphael's staff.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsExternal => IntegratorId != null;

        [System.Text.Json.Serialization.JsonIgnore]
        public string Initials
        {
            get
            {
                var parts = (FullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length switch
                {
                    0 => "?",
                    1 => parts[0][..1].ToUpperInvariant(),
                    _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
                };
            }
        }

        public int Id { get; set; }
        [Required]
        public string FullName { get; set; }
        [Required]
        public string Username { get; set; }
        [Required]
        public string PasswordHash { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? DriverLicense { get; set; }
        public bool IsActive { get; set; }
        [Required]
        public int RoleId { get; set; }
        public string RoleName { get; set; }
        public Role Role { get; set; }
        //public ICollection<VehicleRoute> VehicleRoutes { get; set; }
        public int? IntegratorId { get; set; }
        public int? ProviderId { get; set; }
        public string? ProviderName { get; set; }
        public string? IntegratorName { get; set; }

        /// <summary>RE-066: MCP access level. 0 None, 1 Summary, 2 Detail, 3 Full (set only in the database).</summary>
        public int McpAccess { get; set; }

        /// <summary>RE-066: the written BAA evidence recorded for a Detail user.</summary>
        public string? McpBaaReference { get; set; }


    }
}
