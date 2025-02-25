namespace NewwaysAdmin.WebAdmin.Models.Auth
{
    public class UserAccessModel

    {
        public required string Username { get; set; }
        public required List<NavigationItemAccess> NavigationAccess { get; set; }
    }
    public class NavigationItemAccess
    {
        public required string NavigationId { get; set; }
        public required string Name { get; set; }
        public bool HasAccess { get; set; }
    }
}