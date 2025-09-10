// NewwaysAdmin.Shared/IO/FileIndexing/Core/FileIndexProcessingManager.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.FileIndexing.Models;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Shared.IO.FileIndexing.Core
{
    /// <summary>
    /// Handles processing status updates for file index entries
    /// Keeps processing logic separate from basic file indexing
    /// </summary>
    public class FileIndexProcessingManager
    {
        private readonly ILogger<FileIndexProcessingManager> _logger;
        private readonly FileIndexEngine _indexEngine;

        public FileIndexProcessingManager(
            ILogger<FileIndexProcessingManager> logger,
            FileIndexEngine indexEngine)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _indexEngine = indexEngine ?? throw new ArgumentNullException(nameof(indexEngine));
        }

        /// <summary>
        /// Marks a file as processed and updates processing metadata
        /// </summary>
        public async Task<bool> MarkFileAsProcessedAsync(
            string folderName,
            string filePath,
            ProcessingStage stage = ProcessingStage.ProcessingCompleted,
            Dictionary<string, object>? metadata = null)
        {
            try
            {
                var indexStorageName = $"{folderName}_Index";
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                var entry = entries.FirstOrDefault(e =>
                    e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    _logger.LogWarning("File not found in index for processing update: {FilePath}", filePath);
                    return false;
                }

                // Update processing status
                entry.IsProcessed = true;
                entry.ProcessedAt = DateTime.Now;
                entry.ProcessingStage = stage;

                // Merge metadata if provided
                if (metadata != null)
                {
                    entry.ProcessingMetadata ??= new Dictionary<string, object>();
                    foreach (var kvp in metadata)
                    {
                        entry.ProcessingMetadata[kvp.Key] = kvp.Value;
                    }
                }

                // Save updated index
                await _indexEngine.SaveIndexAsync(indexStorageName, entries);

                _logger.LogDebug("Marked file as processed: {FilePath} in folder: {FolderName}",
                    filePath, folderName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark file as processed: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Updates the processing stage for a file
        /// </summary>
        public async Task<bool> UpdateProcessingStageAsync(
            string folderName,
            string filePath,
            ProcessingStage stage,
            Dictionary<string, object>? stageMetadata = null)
        {
            try
            {
                var indexStorageName = $"{folderName}_Index";
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                var entry = entries.FirstOrDefault(e =>
                    e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    _logger.LogWarning("File not found in index for stage update: {FilePath}", filePath);
                    return false;
                }

                // Update stage
                entry.ProcessingStage = stage;

                // Add stage-specific metadata
                if (stageMetadata != null)
                {
                    entry.ProcessingMetadata ??= new Dictionary<string, object>();
                    foreach (var kvp in stageMetadata)
                    {
                        entry.ProcessingMetadata[kvp.Key] = kvp.Value;
                    }
                }

                // If this is the final stage, mark as processed
                if (stage == ProcessingStage.ProcessingCompleted)
                {
                    entry.IsProcessed = true;
                    entry.ProcessedAt = DateTime.Now;
                }

                await _indexEngine.SaveIndexAsync(indexStorageName, entries);

                _logger.LogDebug("Updated processing stage to '{Stage}' for file: {FilePath}",
                    stage.ToString(), filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update processing stage: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Gets all unprocessed files from a folder index
        /// </summary>
        public async Task<List<FileIndexEntry>> GetUnprocessedFilesAsync(string folderName)
        {
            try
            {
                var indexStorageName = $"{folderName}_Index";
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                var unprocessed = entries.Where(e => !e.IsProcessed).ToList();

                _logger.LogDebug("Found {UnprocessedCount} unprocessed files in folder: {FolderName}",
                    unprocessed.Count, folderName);

                return unprocessed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get unprocessed files for folder: {FolderName}", folderName);
                return new List<FileIndexEntry>();
            }
        }

        /// <summary>
        /// Gets files at a specific processing stage
        /// </summary>
        public async Task<List<FileIndexEntry>> GetFilesByStageAsync(string folderName, ProcessingStage stage)
        {
            try
            {
                var indexStorageName = $"{folderName}_Index";
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                var stageFiles = entries.Where(e => e.ProcessingStage == stage).ToList();

                _logger.LogDebug("Found {FileCount} files at stage '{Stage}' in folder: {FolderName}",
                    stageFiles.Count, stage.ToString(), folderName);

                return stageFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get files by stage for folder: {FolderName}", folderName);
                return new List<FileIndexEntry>();
            }
        }

        /// <summary>
        /// Resets processing status for a file (useful for reprocessing)
        /// </summary>
        public async Task<bool> ResetProcessingStatusAsync(string folderName, string filePath)
        {
            try
            {
                var indexStorageName = $"{folderName}_Index";
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                var entry = entries.FirstOrDefault(e =>
                    e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    _logger.LogWarning("File not found in index for processing reset: {FilePath}", filePath);
                    return false;
                }

                // Reset processing status
                entry.IsProcessed = false;
                entry.ProcessedAt = null;
                entry.ProcessingStage = ProcessingStage.Detected;
                entry.ProcessingMetadata?.Clear();

                await _indexEngine.SaveIndexAsync(indexStorageName, entries);

                _logger.LogDebug("Reset processing status for file: {FilePath}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset processing status: {FilePath}", filePath);
                return false;
            }
        }
    }
}