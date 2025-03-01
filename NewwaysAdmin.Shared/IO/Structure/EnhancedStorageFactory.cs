using System.Text;
using System.Text.Json;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Json;
using NewwaysAdmin.Shared.IO.Binary;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Shared.IO.Structure
{
    public class EnhancedStorageFactory
    {
        private const string CONFIG_FOLDER = "Config";
        private const string CONFIG_FILENAME = "storage-registry.json";
        private readonly ILogger _logger;
        private readonly StorageConfiguration _config;
        private readonly Dictionary<string, IDataStorageBase> _storageCache = new();
        private readonly string _configPath;
        private readonly SemaphoreSlim _lock = new(1, 1); // Ensure thread safety for async operations

        public EnhancedStorageFactory(ILogger logger)
        {
            _logger = logger;
            if (!Directory.Exists(StorageConfiguration.DEFAULT_BASE_DIRECTORY))
            {
                Directory.CreateDirectory(StorageConfiguration.DEFAULT_BASE_DIRECTORY);
                _logger.LogInformation("Created base directory: {Path}", StorageConfiguration.DEFAULT_BASE_DIRECTORY);
            }

            var fullConfigPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, CONFIG_FOLDER);
            if (!Directory.Exists(fullConfigPath))
            {
                Directory.CreateDirectory(fullConfigPath);
                _logger.LogInformation("Created config directory: {Path}", fullConfigPath);
            }

            _configPath = Path.Combine(fullConfigPath, CONFIG_FILENAME);
            _config = LoadConfiguration();
            _logger.LogInformation("Storage factory initialized");
        }

        // Existing synchronous GetStorage method (unchanged)
        public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new()
        {
            var folder = _config.RegisteredFolders.FirstOrDefault(f => f.Name == folderName)
                ?? throw new StorageException($"Folder not found: {folderName}", folderName, StorageOperation.Load);

            var cacheKey = $"{folderName}_{typeof(T).Name}";
            if (_storageCache.TryGetValue(cacheKey, out var cached))
            {
                return (IDataStorage<T>)cached;
            }

            var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path);
            _logger.LogDebug("Getting storage for {FolderName} at full path: {FullPath}", folderName, fullPath);

            var options = new StorageOptions
            {
                BasePath = fullPath,
                FileExtension = folder.Type == StorageType.Json ? ".json" : ".bin",
                CreateBackups = folder.CreateBackups,
                MaxBackupCount = folder.MaxBackupCount
            };

            IDataStorage<T> storage = folder.Type == StorageType.Json
                ? new JsonStorage<T>(options)
                : new BinaryStorage<T>(options);

            _storageCache[cacheKey] = storage;
            return storage;
        }

        // New async method
        public async Task<IDataStorage<T>> GetStorageAsync<T>(string folderName) where T : class, new()
        {
            await _lock.WaitAsync();
            try
            {
                var folder = _config.RegisteredFolders.FirstOrDefault(f => f.Name == folderName);
                if (folder == null)
                {
                    throw new StorageException($"Folder not found: {folderName}", folderName, StorageOperation.Load);
                }

                var cacheKey = $"{folderName}_{typeof(T).Name}";
                if (_storageCache.TryGetValue(cacheKey, out var cached))
                {
                    return (IDataStorage<T>)cached;
                }

                var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path);
                _logger.LogDebug("Getting storage async for {FolderName} at full path: {FullPath}", folderName, fullPath);

                if (!Directory.Exists(fullPath))
                {
                    await Task.Run(() => Directory.CreateDirectory(fullPath));
                    _logger.LogInformation("Created directory async: {Path}", fullPath);
                }

                var options = new StorageOptions
                {
                    BasePath = fullPath,
                    FileExtension = folder.Type == StorageType.Json ? ".json" : ".bin",
                    CreateBackups = folder.CreateBackups,
                    MaxBackupCount = folder.MaxBackupCount
                };

                IDataStorage<T> storage = folder.Type == StorageType.Json
                    ? new JsonStorage<T>(options)
                    : new BinaryStorage<T>(options);

                _storageCache[cacheKey] = storage;
                return storage;
            }
            finally
            {
                _lock.Release();
            }
        }
         public void RegisterFolder(StorageFolder folder)
        {
            try
            {
                _logger.LogInformation("Registering folder: {FolderName} at {Path}", folder.Name, folder.Path);

                if (_config.RegisteredFolders.Any(f => f.Name == folder.Name))
                {
                    _logger.LogInformation("Folder {FolderName} already registered, skipping", folder.Name);
                    return;
                }

                _config.AddFolder(folder);
                SaveConfiguration();

                var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path);
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    _logger.LogInformation("Created directory: {Path}", fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register folder: {FolderName}", folder.Name);
                throw;
            }
        }
        public string GetDirectoryStructure()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Storage Directory Structure:");
            sb.AppendLine("===========================");

            foreach (var folder in _config.RegisteredFolders.OrderBy(f => f.Path))
            {
                sb.AppendLine($"\nFolder ID: {folder.Name}");
                sb.AppendLine($"Physical Path: {Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path)}");
                sb.AppendLine($"Description: {folder.Description}");
                sb.AppendLine($"Type: {folder.Type}");
                sb.AppendLine($"Created: {folder.Created}");
                sb.AppendLine($"Created By: {folder.CreatedBy}");
                if (!string.IsNullOrEmpty(folder.LastModified))
                    sb.AppendLine($"Last Modified: {folder.LastModified}");
                sb.AppendLine($"Shared: {folder.IsShared}");
            }

            return sb.ToString();
        }

        private StorageConfiguration LoadConfiguration()
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<StorageConfiguration>(json)
                       ?? StorageConfiguration.CreateDefault();
            }

            var config = StorageConfiguration.CreateDefault();
            SaveConfiguration(config);
            return config;
        }

        private void SaveConfiguration()
        {
            SaveConfiguration(_config);
        }

        private void SaveConfiguration(StorageConfiguration config)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_configPath, json);
        }

        public void UnregisterFolder(string folderPath)
        {
            try
            {
                _logger.LogInformation("Unregistering folder from storage system: {FolderPath}", folderPath);

                // Find folder in config
                var registeredFolders = _config.RegisteredFolders.ToList();
                var folder = registeredFolders.FirstOrDefault(f =>
                    f.Path.Replace('\\', '/') == folderPath.Replace('\\', '/'));

                if (folder == null)
                {
                    _logger.LogWarning("Folder not found for unregistration: {FolderPath}", folderPath);
                    return;
                }

                // Remove from registered folders
                registeredFolders.Remove(folder);
                _config.RegisteredFolders = registeredFolders;

                // Clear cached storage instances
                var keysToRemove = _storageCache.Keys
                    .Where(k => k.StartsWith($"{folder.Name}_"))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _storageCache.Remove(key);
                }

                // Save updated configuration
                SaveConfiguration();

                _logger.LogInformation("Successfully unregistered folder from storage system: {FolderPath}", folderPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unregister folder: {FolderPath}", folderPath);
                throw;
            }
        }
    }
}