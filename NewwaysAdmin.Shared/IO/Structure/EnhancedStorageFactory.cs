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

        public EnhancedStorageFactory(ILogger logger)
        {
            _logger = logger;

            // Ensure base directory exists
            if (!Directory.Exists(StorageConfiguration.DEFAULT_BASE_DIRECTORY))
            {
                Directory.CreateDirectory(StorageConfiguration.DEFAULT_BASE_DIRECTORY);
                _logger.LogInformation("Created base directory: {Path}", StorageConfiguration.DEFAULT_BASE_DIRECTORY);
            }

            // Create Config folder if it doesn't exist
            var fullConfigPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, CONFIG_FOLDER);
            if (!Directory.Exists(fullConfigPath))
            {
                Directory.CreateDirectory(fullConfigPath);
                _logger.LogInformation("Created config directory: {Path}", fullConfigPath);
            }

            // Set config path in the Config folder
            _configPath = Path.Combine(fullConfigPath, CONFIG_FILENAME);
            _config = LoadConfiguration();
            _logger.LogInformation("Storage factory initialized");
        }

        public void RegisterFolder(StorageFolder folder)
        {
            try
            {
                _logger.LogInformation("Registering folder: {FolderName} at {Path}",
                    folder.Name, folder.Path);

                // Check if folder is already registered
                if (_config.RegisteredFolders.Any(f => f.Name == folder.Name))
                {
                    _logger.LogInformation("Folder {FolderName} is already registered, skipping registration",
                        folder.Name);
                    return; // Skip registration instead of throwing an error
                }

                _config.AddFolder(folder);
                SaveConfiguration();

                // Create the folder structure
                var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path, folder.Name);
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

        public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new()
        {
            var folder = _config.RegisteredFolders.FirstOrDefault(f => f.Name == folderName)
                ?? throw new StorageException($"Folder not found: {folderName}",
                    folderName, StorageOperation.Load);

            var cacheKey = $"{folderName}_{typeof(T).Name}";

            if (_storageCache.TryGetValue(cacheKey, out var cached))
            {
                return (IDataStorage<T>)cached;
            }

            var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path, folder.Name);
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

        public string GetDirectoryStructure()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Storage Directory Structure:");
            sb.AppendLine("===========================");

            foreach (var folder in _config.RegisteredFolders.OrderBy(f => f.Path))
            {
                sb.AppendLine($"\nFolder: {folder.Name}");
                sb.AppendLine($"Path: {Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path, folder.Name)}");
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
        /// <summary>
        /// Unregistearam>rs a folder from the storage system. This only removes the registration
        /// and cached storage instances - it does NOT delete any files or folders from disk.
        /// </summary>
        /// <param name="folderPath">The path of the folder to unregister</p
        public void UnregisterFolder(string folderPath)
        {
            try
            {
                _logger.LogInformation("Unregistering folder from storage system: {FolderPath}", folderPath);

                // Find folder in config
                var registeredFolders = _config.RegisteredFolders.ToList();
                var folder = registeredFolders.FirstOrDefault(f =>
                    Path.Combine(f.Path, f.Name).Replace('\\', '/') == folderPath.Replace('\\', '/'));

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