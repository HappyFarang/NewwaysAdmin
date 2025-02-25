using System.ComponentModel.DataAnnotations;

namespace NewwaysAdmin.WebAdmin.Models.Navigation
{

    public class NavigationItem
    {
        public required string Id { get; set; }  // Unique identifier for the nav item
        public required string Name { get; set; }  // Display name
        public required string Path { get; set; }  // Route path
        public string? Icon { get; set; }  // Optional Bootstrap icon class
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}