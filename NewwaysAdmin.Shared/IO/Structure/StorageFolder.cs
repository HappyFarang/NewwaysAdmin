// NewwaysAdmin.Shared/IO/Structure/StorageFolder.cs
// UPDATED: Added RawFileMode property for direct byte[] storage

namespace NewwaysAdmin.Shared.IO.Structure
{
    public class StorageFolder
    {
        // ===== EXISTING PROPERTIES (unchanged for backwards compatibility) =====

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

        // ===== FILE INDEXING PROPERTIES =====

        public bool IndexFiles { get; set; } = false;
        public string[]? IndexedExtensions { get; set; }
        public bool IndexContent { get; set; } = false;
        public TimeSpan? IndexCacheLifetime { get; set; }

        // ===== PASSTHROUGH MODE (for syncing pre-serialized files) =====

        /// <summary>
        /// When true, bypasses serialization and copies files directly.
        /// Useful for syncing external JSON files that are already properly serialized by another IO Manager.
        /// Only applicable to StorageType.Json folders.
        /// Default: false (backwards compatible)
        /// </summary>
        public bool PassThroughMode { get; set; } = false;

        // ===== NEW: RAW FILE MODE (for direct byte[] storage) =====

        /// <summary>
        /// When true, this folder stores raw files (images, PDFs, etc.) without serialization wrapper.
        /// Use SaveRawAsync/LoadRawAsync methods with this mode.
        /// Files are stored exactly as provided - identifier includes the extension.
        /// Example: SaveRawAsync("photo_001.jpg", imageBytes) saves as photo_001.jpg
        /// Default: false (backwards compatible - uses typed serialization)
        /// </summary>
        public bool RawFileMode { get; set; } = false;

        // ===== COMPUTED PROPERTY =====

        public string UniqueId => string.IsNullOrEmpty(Path)
            ? Name
            : $"{Path}/{Name}";
    }
}