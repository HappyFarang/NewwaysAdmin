// NewwaysAdmin.Shared/IO/FileIndexing/Models/FileIndexEntry.cs
namespace NewwaysAdmin.Shared.IO.FileIndexing.Models
{
    public class FileIndexEntry
    {
        public required string FilePath { get; set; }           // Relative path within folder
        public required string FileHash { get; set; }           // SHA256 for duplicates
        public required DateTime Created { get; set; }          // File creation time
        public required DateTime LastModified { get; set; }     // File modification time
        public required long FileSize { get; set; }             // File size in bytes
        public required DateTime IndexedAt { get; set; }        // When we indexed it

        // Keep it simple for now - we can add these later:
        // public Dictionary<string, object> Metadata { get; set; } = new();
        // public string? ProcessedContent { get; set; }
    }
}