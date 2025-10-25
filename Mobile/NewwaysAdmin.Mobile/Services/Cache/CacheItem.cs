// File: Mobile/NewwaysAdmin.Mobile/Services/Cache/CacheItem.cs
namespace NewwaysAdmin.Mobile.Services.Cache
{
    /// <summary>
    /// Represents an item that needs to be synced with the server
    /// Single responsibility: Define cache item structure and retention policy
    /// </summary>
    public class CacheItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DataType { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string TargetApp { get; set; } = "Server";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SyncedAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;
        public CacheRetentionPolicy RetentionPolicy { get; set; } = CacheRetentionPolicy.DeleteAfterSync;
        public string? ErrorMessage { get; set; }

        // IO Manager references
        public string? DataFilePath { get; set; }      // Path to .bin file with actual data
        public string? MetadataFilePath { get; set; }  // Path to metadata .json file

        // Simple inline data for small items
        public string? InlineJsonData { get; set; }

        // Helper properties
        public bool IsSynced => SyncedAt.HasValue;
        public bool HasFailed => RetryCount >= MaxRetries;
        public bool ShouldRetry => !IsSynced && !HasFailed;
    }

    /// <summary>
    /// Defines what happens to cached data after successful sync
    /// </summary>
    public enum CacheRetentionPolicy
    {
        /// <summary>
        /// Delete from device after successful sync (e.g., receipt photos)
        /// </summary>
        DeleteAfterSync,

        /// <summary>
        /// Keep on device after sync (e.g., category data, settings)
        /// </summary>
        KeepAfterSync,

        /// <summary>
        /// Keep for a specific time period then delete
        /// </summary>
        KeepForPeriod
    }
}