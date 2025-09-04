// NewwaysAdmin.Shared/IO/Structure/StorageFolder.cs

namespace NewwaysAdmin.Shared.IO.Structure
{
    public class StorageFolder
    {
        // Existing properties (unchanged for backwards compatibility)
        public required string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public required StorageType Type { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsShared { get; set; }
        public bool CreateBackups { get; set; } = true;
        public int MaxBackupCount { get; set; } = 5;
        public DateTime Created { get; set; } = DateTime.Now;
        public string CreatedBy { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;

        // NEW: File indexing properties (all with defaults for backwards compatibility)
        public bool IndexFiles { get; set; } = false;                          // Opt-in indexing
        public string[]? IndexedExtensions { get; set; }                       // [".pdf", ".jpg", ".bin", ".json"]
        public bool IndexContent { get; set; } = false;                        // For OCR/text search
        public TimeSpan? IndexCacheLifetime { get; set; }                      // Performance tuning

        // Existing computed property
        public string UniqueId => string.IsNullOrEmpty(Path)
            ? Name
            : $"{Path}/{Name}";
    }
}