using NewwaysAdmin.WebAdmin.Models.Auth;

public class UserSession
{
    public required string Username { get; set; }
    public required string SessionId { get; set; }
    public List<UserPageAccess> PageAccess { get; set; } = new();  // Changed from AllowedNavigationIds
    public bool IsAdmin { get; set; }
    public required DateTime LoginTime { get; set; }
    public required string CircuitId { get; set; }
    public required string ConnectionId { get; set; }
}