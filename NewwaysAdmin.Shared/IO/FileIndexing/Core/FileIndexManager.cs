// NewwaysAdmin.Shared/IO/FileIndexing/Core/FileIndexManager.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.FileIndexing.Models;
using NewwaysAdmin.Shared.IO.FileIndexing.Extensions;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Shared.IO.FileIndexing.Core
{
    public class FileIndexManager
    {
        private readonly ILogger<FileIndexManager> _logger;
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly FileIndexEngine _indexEngine;

        public FileIndexManager(
            ILogger<FileIndexManager> logger,
            EnhancedStorageFactory storageFactory,
            FileIndexEngine indexEngine)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _indexEngine = indexEngine ?? throw new ArgumentNullException(nameof(indexEngine));
        }

        /// <summary>
        /// Creates or updates the index for a registered storage folder
        /// </summary>
        public async Task<bool> IndexFolderAsync(StorageFolder folder, string folderPath)
        {
            if (!folder.IsIndexingEnabled())
            {
                _logger.LogDebug("Indexing not enabled for folder: {FolderName}", folder.Name);
                return false;
            }

            try
            {
                _logger.LogInformation("Starting index creation for folder: {FolderName}", folder.Name);

                // Scan the folder and build index entries
                var entries = await _indexEngine.ScanFolderAsync(folderPath, folder);

                // Save the index using the existing storage system
                var indexStorageName = folder.GetIndexStorageName();
                await _indexEngine.SaveIndexAsync(indexStorageName, entries);

                _logger.LogInformation("Successfully indexed {EntryCount} files for folder: {FolderName}",
                    entries.Count, folder.Name);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index folder: {FolderName}", folder.Name);
                return false;
            }
        }

        /// <summary>
        /// Gets the file index for a folder
        /// </summary>
        public async Task<List<FileIndexEntry>> GetIndexAsync(string folderName)
        {
            try
            {
                var indexStorageName = $"{folderName}_Index";
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                _logger.LogDebug("Retrieved {EntryCount} index entries for folder: {FolderName}",
                    entries.Count, folderName);

                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load index for folder: {FolderName}", folderName);
                return new List<FileIndexEntry>();
            }
        }

        /// <summary>
        /// Adds a new file to the index
        /// </summary>
        public async Task<bool> AddFileToIndexAsync(StorageFolder folder, string folderPath, string filePath)
        {
            if (!folder.IsIndexingEnabled())
            {
                return false;
            }

            // Check if this file type should be indexed
            var fileExtension = Path.GetExtension(filePath);
            if (!folder.ShouldIndexExtension(fileExtension))
            {
                _logger.LogDebug("File extension {Extension} not configured for indexing in folder: {FolderName}",
                    fileExtension, folder.Name);
                return false;
            }

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Cannot add non-existent file to index: {FilePath}", filePath);
                return false;
            }

            try
            {
                // Create index entry for the new file
                var entry = await _indexEngine.CreateIndexEntryAsync(filePath, folderPath);

                // Load current index
                var indexStorageName = folder.GetIndexStorageName();
                var currentEntries = await _indexEngine.LoadIndexAsync(indexStorageName);

                // Add the new entry
                currentEntries.Add(entry);

                // Save updated index
                await _indexEngine.SaveIndexAsync(indexStorageName, currentEntries);

                _logger.LogDebug("Added new file to index: {FilePath}", entry.FilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add file to index: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Updates an existing file in the index
        /// </summary>
        public async Task<bool> UpdateFileInIndexAsync(StorageFolder folder, string folderPath, string changedFilePath)
        {
            if (!folder.IsIndexingEnabled())
            {
                return false;
            }

            // Check if this file type should be indexed
            var fileExtension = Path.GetExtension(changedFilePath);
            if (!folder.ShouldIndexExtension(fileExtension))
            {
                _logger.LogDebug("File extension {Extension} not configured for indexing in folder: {FolderName}",
                    fileExtension, folder.Name);
                return false;
            }

            if (!File.Exists(changedFilePath))
            {
                _logger.LogWarning("Cannot update non-existent file in index: {FilePath}", changedFilePath);
                return false;
            }

            try
            {
                // Create updated index entry
                var entry = await _indexEngine.CreateIndexEntryAsync(changedFilePath, folderPath);

                // Load current index
                var indexStorageName = folder.GetIndexStorageName();
                var currentEntries = await _indexEngine.LoadIndexAsync(indexStorageName);

                // Remove existing entry for this file path
                var relativePath = Path.GetRelativePath(folderPath, changedFilePath);
                currentEntries.RemoveAll(e => e.FilePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

                // Add the updated entry
                currentEntries.Add(entry);

                // Save updated index
                await _indexEngine.SaveIndexAsync(indexStorageName, currentEntries);

                _logger.LogDebug("Updated existing file in index: {FilePath}", entry.FilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update file in index: {FilePath}", changedFilePath);
                return false;
            }
        }

        /// <summary>
        /// Removes a file from the index
        /// </summary>
        public async Task<bool> RemoveFileFromIndexAsync(StorageFolder folder, string folderPath, string deletedFilePath)
        {
            if (!folder.IsIndexingEnabled())
            {
                return false;
            }

            try
            {
                var relativePath = Path.GetRelativePath(folderPath, deletedFilePath);
                var indexStorageName = folder.GetIndexStorageName();
                var currentEntries = await _indexEngine.LoadIndexAsync(indexStorageName);

                var removed = currentEntries.RemoveAll(e =>
                    e.FilePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

                if (removed > 0)
                {
                    await _indexEngine.SaveIndexAsync(indexStorageName, currentEntries);
                    _logger.LogDebug("Removed {RemovedCount} index entries for deleted file: {FilePath}",
                        removed, relativePath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove file from index: {FilePath}", deletedFilePath);
                return false;
            }
        }

        /// <summary>
        /// Checks if a file exists in the index (useful for duplicate detection)
        /// </summary>
        public async Task<bool> IsFileIndexedAsync(string folderName, string fileHash)
        {
            try
            {
                var entries = await GetIndexAsync(folderName);
                return entries.Any(e => e.FileHash.Equals(fileHash, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if file is indexed in folder: {FolderName}", folderName);
                return false;
            }
        }
    }
}