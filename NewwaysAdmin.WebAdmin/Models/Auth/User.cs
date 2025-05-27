namespace NewwaysAdmin.WebAdmin.Models.Auth
{
    public class User
    {
        public required string Username { get; set; }
        public required string PasswordHash { get; set; }
        public required string Salt { get; set; }
        public List<UserPageAccess> PageAccess { get; set; } = new();
        public Dictionary<string, UserModuleConfig> ModuleConfigs { get; set; } = new(); // NEW: Module-specific configs
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

    public class UserModuleConfig
    {
        public string ModuleId { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new();
        public DateTime ConfiguredAt { get; set; } = DateTime.UtcNow;
        public string ConfiguredBy { get; set; } = string.Empty;
    }

    public enum AccessLevel
    {
        None,
        Read,
        ReadWrite
    }
}