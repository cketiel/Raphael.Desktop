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

    }
}
