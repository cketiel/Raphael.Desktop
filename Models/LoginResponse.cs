namespace Raphael.Desktop.Models
{
    public enum UserRole
    {
        Admin,
        Driver,
        Client,
        User
    }
    public class LoginResponse
    {
        public bool IsSuccess { get; set; }
        public string Token { get; set; }
        public string Message { get; set; }

        public string UserId { get; set; }

        public String Username { get; set; }

        public String Role { get; set; }
        //public Guid Role { get; set; }
        //public UserRole Role { get; set; }

        public int? IntegratorId { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>When <see cref="Token"/> stops being accepted, as the server states it.</summary>
        /// <remarks>
        /// Read from the answer rather than decoded out of the JWT. A client that parses tokens
        /// to find out when to renew them ends up maintaining its own half-correct JWT parser.
        /// </remarks>
        public System.DateTime AccessTokenExpiresAtUtc { get; set; }

        /// <summary>Buys a new access token. Single use: every renewal replaces it.</summary>
        public string RefreshToken { get; set; }

        public System.DateTime RefreshTokenExpiresAtUtc { get; set; }
    }
}
