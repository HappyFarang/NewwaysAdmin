namespace NewwaysAdmin.WebAdmin.Models.Auth
{
    public class User
    {
        public required string Username { get; set; }
        public required string PasswordHash { get; set; }
        public required string Salt { get; set; }
        public List<UserPageAccess> PageAccess { get; set; } = new();  // Changed from AllowedNavigationIds
        public bool IsAdmin { get; set; }
        public required DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UserPageAccess
    {
        public required string NavigationId { get; set; }
        public AccessLevel AccessLevel { get; set; }
    }

    public enum AccessLevel
    {
        None,
        Read,
        ReadWrite
    }
}