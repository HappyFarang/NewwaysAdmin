// NewwaysAdmin.Shared/IO/FileIndexing/Models/FileIndexEntry.cs
namespace NewwaysAdmin.Shared.IO.FileIndexing.Models
{
    public class FileIndexEntry
    {
        // Original properties
        public required string FilePath { get; set; }           // Relative path within folder
        public required string FileHash { get; set; }           // SHA256 for duplicates
        public required DateTime Created { get; set; }          // File creation time
        public required DateTime LastModified { get; set; }     // File modification time
        public required long FileSize { get; set; }             // File size in bytes
        public required DateTime IndexedAt { get; set; }        // When we indexed it

        // New processing status properties
        public bool IsProcessed { get; set; } = false;          // Has this file been fully processed?
        public DateTime? ProcessedAt { get; set; }              // When processing completed
        public ProcessingStage ProcessingStage { get; set; } = ProcessingStage.Detected;  // Current stage
        public Dictionary<string, object>? ProcessingMetadata { get; set; } // Custom data from processing steps

        // Helper methods
        public bool IsFullyProcessed => IsProcessed && ProcessedAt.HasValue && ProcessingStage == ProcessingStage.ProcessingCompleted;
        public bool NeedsProcessing => !IsProcessed && ProcessingStage != ProcessingStage.ProcessingCompleted;
        public bool HasFailed => ProcessingStage == ProcessingStage.ProcessingFailed;
        public bool IsInProgress => ProcessingStage == ProcessingStage.Processing || ProcessingStage == ProcessingStage.ProcessingStarted;
    }
}