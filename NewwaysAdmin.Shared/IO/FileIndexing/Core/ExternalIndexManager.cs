// NewwaysAdmin.Shared/IO/FileIndexing/Core/ExternalIndexManager.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.FileIndexing.Models;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Shared.IO.FileIndexing.Core
{
    public class ExternalIndexManager
    {
        private readonly ILogger<ExternalIndexManager> _logger;
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly FileIndexEngine _indexEngine;
        private readonly Dictionary<string, ExternalIndexCollection> _registeredCollections = new();

        public ExternalIndexManager(
            ILogger<ExternalIndexManager> logger,
            EnhancedStorageFactory storageFactory,
            FileIndexEngine indexEngine)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _indexEngine = indexEngine ?? throw new ArgumentNullException(nameof(indexEngine));

            // Ensure the ExternalIndexes storage folder exists and load existing collections
            EnsureExternalIndexStorageFolder();
            Task.Run(LoadExistingCollectionsAsync); // Load on startup
        }

        /// <summary>
        /// Registers a new external folder for indexing (like NAS bank slip folders)
        /// </summary>
        public async Task<bool> RegisterExternalFolderAsync(string collectionName, string externalPath, string[] indexedExtensions)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));
            }

            if (!Directory.Exists(externalPath))
            {
                _logger.LogWarning("External path does not exist: {Path}", externalPath);
                return false;
            }

            try
            {
                var collection = new ExternalIndexCollection
                {
                    Name = collectionName,
                    ExternalPath = externalPath,
                    IndexedExtensions = indexedExtensions,
                    RegisteredAt = DateTime.Now
                };

                // Store the collection configuration (not in registry, but as individual file)
                _registeredCollections[collectionName] = collection;
                await SaveCollectionConfigAsync(collection);

                // Create initial index (stored as separate file)
                await ScanExternalFolderAsync(collectionName);

                _logger.LogInformation("Successfully registered external folder collection: {CollectionName} -> {Path}",
                    collectionName, externalPath);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register external folder: {CollectionName}", collectionName);
                return false;
            }
        }

        /// <summary>
        /// Scans an external folder and updates its index
        /// </summary>
        public async Task<bool> ScanExternalFolderAsync(string collectionName)
        {
            if (!_registeredCollections.TryGetValue(collectionName, out var collection))
            {
                _logger.LogWarning("External collection not found: {CollectionName}", collectionName);
                return false;
            }

            if (!Directory.Exists(collection.ExternalPath))
            {
                _logger.LogWarning("External path no longer exists: {Path}", collection.ExternalPath);
                return false;
            }

            try
            {
                _logger.LogInformation("Scanning external folder: {CollectionName} at {Path}",
                    collectionName, collection.ExternalPath);

                // Create a temporary StorageFolder for the scanning process
                var tempFolder = new StorageFolder
                {
                    Name = collectionName,
                    Type = StorageType.Json, // External indexes are always JSON for simplicity
                    IndexedExtensions = collection.IndexedExtensions
                };

                // Scan the external folder
                var entries = await _indexEngine.ScanFolderAsync(collection.ExternalPath, tempFolder);

                // Save to external index storage
                var indexStorageName = GetExternalIndexStorageName(collectionName);
                await _indexEngine.SaveIndexAsync(indexStorageName, entries);

                // Update last scan time and save collection config
                collection.LastScanned = DateTime.Now;
                await SaveCollectionConfigAsync(collection);

                _logger.LogInformation("Successfully scanned {FileCount} files in external collection: {CollectionName}",
                    entries.Count, collectionName);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan external folder: {CollectionName}", collectionName);
                return false;
            }
        }

        /// <summary>
        /// Gets the index entries for an external collection
        /// </summary>
        public async Task<List<FileIndexEntry>> GetExternalIndexAsync(string collectionName)
        {
            try
            {
                var indexStorageName = GetExternalIndexStorageName(collectionName);
                var entries = await _indexEngine.LoadIndexAsync(indexStorageName);

                _logger.LogDebug("Retrieved {EntryCount} entries for external collection: {CollectionName}",
                    entries.Count, collectionName);

                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load external index: {CollectionName}", collectionName);
                return new List<FileIndexEntry>();
            }
        }

        /// <summary>
        /// Checks if a file hash exists in an external collection (duplicate detection)
        /// </summary>
        public async Task<bool> IsFileProcessedAsync(string collectionName, string fileHash)
        {
            try
            {
                var entries = await GetExternalIndexAsync(collectionName);
                return entries.Any(e => e.FileHash.Equals(fileHash, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if file is processed in collection: {CollectionName}", collectionName);
                return false;
            }
        }

        /// <summary>
        /// Gets all registered external collections
        /// </summary>
        public IEnumerable<ExternalIndexCollection> GetRegisteredCollections()
        {
            return _registeredCollections.Values.ToList();
        }

        /// <summary>
        /// Removes an external collection registration and its files
        /// </summary>
        public async Task<bool> UnregisterExternalFolderAsync(string collectionName)
        {
            if (_registeredCollections.Remove(collectionName))
            {
                await DeleteCollectionConfigAsync(collectionName);
                await DeleteCollectionIndexAsync(collectionName);
                _logger.LogInformation("Unregistered external collection: {CollectionName}", collectionName);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Loads existing external collections from storage on startup
        /// </summary>
        private async Task LoadExistingCollectionsAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<ExternalIndexCollection>("ExternalIndexes");
                var identifiers = await storage.ListIdentifiersAsync();

                foreach (var identifier in identifiers)
                {
                    try
                    {
                        // Only load collection config files (not index files)
                        if (identifier.StartsWith("collection_"))
                        {
                            var collection = await storage.LoadAsync(identifier);
                            _registeredCollections[collection.Name] = collection;
                            _logger.LogInformation("Loaded existing external collection: {CollectionName}", collection.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load collection from identifier: {Identifier}", identifier);
                    }
                }

                _logger.LogInformation("Loaded {CollectionCount} existing external collections", _registeredCollections.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load existing external collections");
            }
        }

        private void EnsureExternalIndexStorageFolder()
        {
            try
            {
                var externalIndexFolder = new StorageFolder
                {
                    Name = "ExternalIndexes",
                    Description = "Storage for external file indexing collections",
                    Type = StorageType.Json,
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 10
                };

                _storageFactory.RegisterFolder(externalIndexFolder);
                _logger.LogDebug("Ensured ExternalIndexes storage folder exists");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure ExternalIndexes storage folder");
            }
        }

        private async Task SaveCollectionConfigAsync(ExternalIndexCollection collection)
        {
            try
            {
                var storage = _storageFactory.GetStorage<ExternalIndexCollection>("ExternalIndexes");
                var identifier = $"collection_{collection.Name}";
                await storage.SaveAsync(identifier, collection);
                _logger.LogDebug("Saved collection configuration: {CollectionName}", collection.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save collection configuration: {CollectionName}", collection.Name);
            }
        }

        private async Task DeleteCollectionConfigAsync(string collectionName)
        {
            try
            {
                var storage = _storageFactory.GetStorage<ExternalIndexCollection>("ExternalIndexes");
                var identifier = $"collection_{collectionName}";
                await storage.DeleteAsync(identifier);
                _logger.LogDebug("Deleted collection configuration: {CollectionName}", collectionName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete collection configuration: {CollectionName}", collectionName);
            }
        }

        private async Task DeleteCollectionIndexAsync(string collectionName)
        {
            try
            {
                var indexStorageName = GetExternalIndexStorageName(collectionName);
                var storage = _storageFactory.GetStorage<List<FileIndexEntry>>(indexStorageName);
                await storage.DeleteAsync("file-index");
                _logger.LogDebug("Deleted collection index: {CollectionName}", collectionName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete collection index: {CollectionName}", collectionName);
            }
        }

        private string GetExternalIndexStorageName(string collectionName)
        {
            return $"External_{collectionName}_Index";
        }
    }
}