using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.DTOs
{
    public class UserUpdateDto
    {
        public int Id { get; set; } 
        public string FullName { get; set; }
        public string Username { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }

        /// <summary>
        /// Sent back as it came. The server writes every field of this DTO, so leaving it out
        /// erased the driver's licence each time a user was saved from this screen.
        /// </summary>
        public string? DriverLicense { get; set; }
        public bool IsActive { get; set; }
        public int RoleId { get; set; }
        // Note: The password is changed on a separate endpoint, so we do not include it here.

        public int? IntegratorId { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>
        /// RE-066: MCP access to set (0 None, 1 Summary, 2 Detail). Null leaves it as it is, which
        /// is what anybody but Raphael's administrator sends. The server refuses 3 (Full).
        /// </summary>
        public int? McpAccess { get; set; }

        /// <summary>RE-066: required by the server when setting Detail.</summary>
        public string? McpBaaReference { get; set; }
    }
}
