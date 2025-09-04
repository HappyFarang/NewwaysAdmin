// NewwaysAdmin.Shared/IO/FileIndexing/Models/ExternalIndexCollection.cs

namespace NewwaysAdmin.Shared.IO.FileIndexing.Models
{
    public class ExternalIndexCollection
    {
        public string Name { get; set; } = string.Empty;                            // Collection identifier
        public string ExternalPath { get; set; } = string.Empty;                    // Path to external folder (e.g., NAS)
        public string[] IndexedExtensions { get; set; } = Array.Empty<string>();    // File types to track
        public DateTime RegisteredAt { get; set; }                                  // When collection was created
        public DateTime? LastScanned { get; set; }                                  // Last time folder was scanned
        public string Description { get; set; } = string.Empty;                     // Optional description

        // Future properties can be added with defaults for backwards compatibility
        // public Dictionary<string, object> Metadata { get; set; } = new();
    }
}