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

        // NEW: PassThrough mode for external file synchronization
        /// <summary>
        /// When true, bypasses serialization and copies files directly.
        /// Useful for syncing external JSON files that are already properly serialized by another IO Manager.
        /// Only applicable to StorageType.Json folders.
        /// Default: false (backwards compatible)
        /// </summary>
        public bool PassThroughMode { get; set; } = false;

        // Existing computed property
        public string UniqueId => string.IsNullOrEmpty(Path)
            ? Name
            : $"{Path}/{Name}";
    }
}