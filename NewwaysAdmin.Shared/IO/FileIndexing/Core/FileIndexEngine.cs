// NewwaysAdmin.Shared/IO/FileIndexing/Core/FileIndexEngine.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.FileIndexing.Models;
using NewwaysAdmin.Shared.IO.Structure;
using System.Security.Cryptography;

namespace NewwaysAdmin.Shared.IO.FileIndexing.Core
{
    public class FileIndexEngine
    {
        private readonly ILogger<FileIndexEngine> _logger;
        private readonly EnhancedStorageFactory _storageFactory;

        public FileIndexEngine(ILogger<FileIndexEngine> logger, EnhancedStorageFactory storageFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        }

        /// <summary>
        /// Scans a folder and builds/updates the file index
        /// </summary>
        public async Task<List<FileIndexEntry>> ScanFolderAsync(string folderPath, StorageFolder folder)
        {
            _logger.LogInformation("Scanning folder for indexing: {FolderPath}", folderPath);

            if (!Directory.Exists(folderPath))
            {
                _logger.LogWarning("Folder does not exist: {FolderPath}", folderPath);
                return new List<FileIndexEntry>();
            }

            var entries = new List<FileIndexEntry>();

            // Determine extensions to index
            var indexedExtensions = GetExtensionsToIndex(folder);

            // Get all files in the folder that match our extensions
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(file => indexedExtensions.Any(ext =>
                    Path.GetExtension(file).Equals(ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            _logger.LogInformation("Found {FileCount} files to index with extensions: {Extensions}",
                files.Length, string.Join(", ", indexedExtensions));

            foreach (var filePath in files)
            {
                try
                {
                    var entry = await CreateIndexEntryAsync(filePath, folderPath);
                    entries.Add(entry);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error indexing file: {FilePath}", filePath);
                }
            }

            return entries;
        }

        /// <summary>
        /// Creates a FileIndexEntry for a single file
        /// </summary>
        public async Task<FileIndexEntry> CreateIndexEntryAsync(string fullFilePath, string baseFolderPath)
        {
            var fileInfo = new FileInfo(fullFilePath);
            var relativePath = Path.GetRelativePath(baseFolderPath, fullFilePath);

            var entry = new FileIndexEntry
            {
                FilePath = relativePath,
                FileHash = await CalculateFileHashAsync(fullFilePath),
                Created = fileInfo.CreationTime,
                LastModified = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length,
                IndexedAt = DateTime.Now
            };

            _logger.LogDebug("Created index entry for: {FilePath}", relativePath);
            return entry;
        }

        /// <summary>
        /// Saves index data using the existing storage system
        /// </summary>
        public async Task SaveIndexAsync(string indexName, List<FileIndexEntry> entries)
        {
            var storage = _storageFactory.GetStorage<List<FileIndexEntry>>(indexName);
            await storage.SaveAsync("file-index", entries);

            _logger.LogInformation("Saved index with {EntryCount} entries to: {IndexName}", entries.Count, indexName);
        }

        /// <summary>
        /// Loads index data using the existing storage system
        /// </summary>
        public async Task<List<FileIndexEntry>> LoadIndexAsync(string indexName)
        {
            var storage = _storageFactory.GetStorage<List<FileIndexEntry>>(indexName);

            try
            {
                var entries = await storage.LoadAsync("file-index");
                _logger.LogInformation("Loaded index with {EntryCount} entries from: {IndexName}", entries.Count, indexName);
                return entries;
            }
            catch
            {
                _logger.LogInformation("No existing index found for: {IndexName}, returning empty list", indexName);
                return new List<FileIndexEntry>();
            }
        }

        /// <summary>
        /// Calculates SHA256 hash for a file
        /// </summary>
        private async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha256 = SHA256.Create();

            var hashBytes = await Task.Run(() => sha256.ComputeHash(fileStream));
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Determines which file extensions should be indexed based on folder configuration
        /// </summary>
        private string[] GetExtensionsToIndex(StorageFolder folder)
        {
            // If IndexedExtensions is specified, use it (for external folders)
            if (folder.IndexedExtensions != null && folder.IndexedExtensions.Length > 0)
            {
                return folder.IndexedExtensions;
            }

            // For internal folders, derive from StorageType
            return folder.Type switch
            {
                StorageType.Json => [".json"],
                StorageType.Binary => [".bin"],
                _ => throw new ArgumentException($"Unknown storage type: {folder.Type}")
            };
        }
    }
}