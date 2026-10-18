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


    }
}
