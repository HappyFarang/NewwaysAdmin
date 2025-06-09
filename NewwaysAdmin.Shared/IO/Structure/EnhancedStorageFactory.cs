using System.Text;
using Newtonsoft.Json;
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

        // Add network base folder path - aligns with IOManager
        public static readonly string NetworkBaseFolder = @"X:/NewwaysAdmin";
        // Dictionary to speed up definition lookups
        private readonly Dictionary<string, string> _folderDefinitionPaths = new();

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

            // Initialize definition paths dictionary for faster lookups
            InitializeDefinitionPathsIndex();

            _logger.LogInformation("Storage factory initialized with {Count} registered folders", _config.RegisteredFolders.Count);
        }

        // Initialize the definition paths index from the existing filesystem
        private void InitializeDefinitionPathsIndex()
        {
            try
            {
                // Clear existing entries
                _folderDefinitionPaths.Clear();

                // Index local definitions
                string definitionsRoot = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, "Config", "Definitions");
                if (Directory.Exists(definitionsRoot))
                {
                    foreach (var appDir in Directory.GetDirectories(definitionsRoot))
                    {
                        foreach (var file in Directory.GetFiles(appDir, "*.json"))
                        {
                            try
                            {
                                string folderName = Path.GetFileNameWithoutExtension(file);
                                _folderDefinitionPaths[folderName] = file;
                                _logger.LogDebug("Indexed local definition path: {Folder} -> {Path}", folderName, file);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Error indexing definition file: {Path}", file);
                            }
                        }
                    }
                }

                _logger.LogInformation("Initialized definition paths index with {Count} entries", _folderDefinitionPaths.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error initializing definition paths index");
            }
        }

        // Existing synchronous GetStorage method
        public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new()
        {
            var folder = _config.RegisteredFolders.FirstOrDefault(f => f.Name == folderName);

            // If not found in memory, try to load from definition
            if (folder == null)
            {
                folder = LoadFolderDefinition(folderName);

                if (folder == null)
                {
                    throw new StorageException($"Folder not found: {folderName}", folderName, StorageOperation.Load);
                }

                // Add to in-memory configuration
                _config.AddFolder(folder);
                SaveConfiguration();
                _logger.LogInformation("Loaded and registered folder definition: {FolderName}", folderName);
            }

            var cacheKey = $"{folderName}_{typeof(T).FullName}";
            if (_storageCache.TryGetValue(cacheKey, out var cached))
            {
                return (IDataStorage<T>)cached;
            }

            var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path);
            _logger.LogDebug("Getting storage for {FolderName} at full path: {FullPath}", folderName, fullPath);

            // Create directory if it doesn't exist
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                _logger.LogInformation("Created directory: {Path}", fullPath);
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

        // Async method
        public async Task<IDataStorage<T>> GetStorageAsync<T>(string folderName) where T : class, new()
        {
            await _lock.WaitAsync();
            try
            {
                var folder = _config.RegisteredFolders.FirstOrDefault(f => f.Name == folderName);

                // If not found in memory, try to load from definition
                if (folder == null)
                {
                    folder = await LoadFolderDefinitionAsync(folderName);

                    if (folder == null)
                    {
                        throw new StorageException($"Folder not found: {folderName}", folderName, StorageOperation.Load);
                    }

                    // Add to in-memory configuration
                    _config.AddFolder(folder);
                    SaveConfiguration();
                    _logger.LogInformation("Loaded and registered folder definition: {FolderName}", folderName);
                }

                var cacheKey = $"{folderName}_{typeof(T).FullName}";
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

        // Simple overload to support the original method signature
        public void RegisterFolder(StorageFolder folder)
        {
            RegisterFolder(folder, null);
        }

        /// <summary>
        /// Registers a folder with the storage factory and optionally saves the definition to the server
        /// </summary>
        /// <param name="folder">The folder to register</param>
        /// <param name="applicationName">Optional application name for saving definition to server</param>
        public void RegisterFolder(StorageFolder folder, string? applicationName = null)
        {
            try
            {
                _logger.LogInformation("Registering folder: {FolderName} at {Path}", folder.Name, folder.Path);

                // Check if folder is already registered locally
                if (_config.RegisteredFolders.Any(f => f.Name == folder.Name))
                {
                    _logger.LogInformation("Folder {FolderName} already registered, skipping", folder.Name);
                    return;
                }

                // Set created date and creator application if not already set
                if (folder.Created == default)
                {
                    folder.Created = DateTime.Now;
                }

                if (string.IsNullOrEmpty(folder.CreatedBy) && !string.IsNullOrEmpty(applicationName))
                {
                    folder.CreatedBy = applicationName;
                }

                // Add to in-memory configuration
                _config.AddFolder(folder);
                SaveConfiguration();

                // Create the local directory for the folder
                var fullPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folder.Path);
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    _logger.LogInformation("Created local directory: {Path}", fullPath);
                }

                // Save the folder definition to the server if application name is provided
                if (!string.IsNullOrEmpty(applicationName))
                {
                    try
                    {
                        // Ensure application name is valid for a directory
                        string safeName = new string(applicationName.Select(c =>
                            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

                        // Save definition locally first
                        string localDefinitionsPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, "Config", "Definitions", safeName);
                        Directory.CreateDirectory(localDefinitionsPath);

                        string localDefinitionFile = Path.Combine(localDefinitionsPath, $"{folder.Name}.json");
                        string json = JsonConvert.SerializeObject(folder, Formatting.Indented);
                        File.WriteAllText(localDefinitionFile, json);
                        _logger.LogInformation("Saved folder definition locally: {Path}", localDefinitionFile);

                        // Add to definition paths index
                        _folderDefinitionPaths[folder.Name] = localDefinitionFile;

                        // Try to save to server as well
                        try
                        {
                            string serverDefinitionsPath = Path.Combine(NetworkBaseFolder, "Definitions", safeName);
                            if (!Directory.Exists(serverDefinitionsPath))
                            {
                                Directory.CreateDirectory(serverDefinitionsPath);
                                _logger.LogInformation("Created server definitions directory: {Path}", serverDefinitionsPath);
                            }

                            string serverDefinitionFile = Path.Combine(serverDefinitionsPath, $"{folder.Name}.json");
                            File.WriteAllText(serverDefinitionFile, json);
                            _logger.LogInformation("Saved folder definition to server: {Path}", serverDefinitionFile);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not save folder definition to server. This is expected for client machines.");
                            // Queue for sync later
                            QueueDefinitionForServerSync(folder, applicationName);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't throw - the folder is already registered in memory
                        _logger.LogWarning(ex, "Could not save folder definition: {Error}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register folder: {FolderName}", folder.Name);
                throw;
            }
        }

        /// <summary>
        /// Queues a folder definition to be synced to the server later when connectivity is restored
        /// </summary>
        private void QueueDefinitionForServerSync(StorageFolder folder, string applicationName)
        {
            try
            {
                string safeAppName = new string(applicationName.Select(c =>
                    Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

                // Save to a pending sync folder
                string pendingSyncPath = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, "Outgoing", "Definitions", safeAppName);
                Directory.CreateDirectory(pendingSyncPath);

                string pendingFile = Path.Combine(pendingSyncPath, $"{folder.Name}.json");
                string json = JsonConvert.SerializeObject(folder, Formatting.Indented);
                File.WriteAllText(pendingFile, json);

                _logger.LogInformation("Queued folder definition for server sync: {Path}", pendingFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue folder definition for server sync");
                // Not rethrowing as this is a best-effort operation
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

        /// <summary>
        /// Attempts to load a folder definition from local index, files, or the server
        /// </summary>
        private StorageFolder? LoadFolderDefinition(string folderName)
        {
            _logger.LogInformation("Searching for folder definition: {FolderName}", folderName);

            // First check our index for faster lookup
            if (_folderDefinitionPaths.TryGetValue(folderName, out var definitionPath))
            {
                try
                {
                    if (File.Exists(definitionPath))
                    {
                        var json = File.ReadAllText(definitionPath);
                        var folder = JsonConvert.DeserializeObject<StorageFolder>(json);

                        if (folder != null)
                        {
                            _logger.LogInformation("Found folder definition through index: {Path}", definitionPath);
                            return folder;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading folder definition from indexed path: {Path}", definitionPath);
                    // Index might be stale, remove it
                    _folderDefinitionPaths.Remove(folderName);
                }
            }

            // Check all application folders in the Definitions directory if index lookup failed
            string definitionsRoot = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, "Config", "Definitions");

            if (Directory.Exists(definitionsRoot))
            {
                // Search all application subfolders
                foreach (var appDir in Directory.GetDirectories(definitionsRoot))
                {
                    string localFile = Path.Combine(appDir, $"{folderName}.json");

                    if (File.Exists(localFile))
                    {
                        try
                        {
                            var json = File.ReadAllText(localFile);
                            var folder = JsonConvert.DeserializeObject<StorageFolder>(json);

                            if (folder != null)
                            {
                                _logger.LogInformation("Found local folder definition: {Path}", localFile);
                                // Update the index for future lookups
                                _folderDefinitionPaths[folderName] = localFile;
                                return folder;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error reading local folder definition: {Path}", localFile);
                        }
                    }
                }
            }

            // If not found locally, try the server
            try
            {
                string serverDefinitionsRoot = Path.Combine(NetworkBaseFolder, "Definitions");

                if (Directory.Exists(serverDefinitionsRoot))
                {
                    // Search all application subfolders on server
                    foreach (var appDir in Directory.GetDirectories(serverDefinitionsRoot))
                    {
                        string serverFile = Path.Combine(appDir, $"{folderName}.json");

                        if (File.Exists(serverFile))
                        {
                            try
                            {
                                var json = File.ReadAllText(serverFile);
                                var folder = JsonConvert.DeserializeObject<StorageFolder>(json);

                                if (folder != null)
                                {
                                    _logger.LogInformation("Found server folder definition: {Path}", serverFile);

                                    // Copy to local folder for future use
                                    string appName = Path.GetFileName(appDir);
                                    string localDir = Path.Combine(definitionsRoot, appName);
                                    Directory.CreateDirectory(localDir);

                                    string localFile = Path.Combine(localDir, $"{folderName}.json");
                                    File.WriteAllText(localFile, json);
                                    _logger.LogInformation("Copied server definition to local path: {Path}", localFile);

                                    // Update index
                                    _folderDefinitionPaths[folderName] = localFile;

                                    return folder;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error reading server folder definition: {Path}", serverFile);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accessing server definitions");
            }

            _logger.LogWarning("Could not find folder definition: {FolderName}", folderName);
            return null;
        }

        /// <summary>
        /// Asynchronous version of LoadFolderDefinition
        /// </summary>
        private async Task<StorageFolder?> LoadFolderDefinitionAsync(string folderName)
        {
            _logger.LogInformation("Searching for folder definition asynchronously: {FolderName}", folderName);

            // First check our index for faster lookup
            if (_folderDefinitionPaths.TryGetValue(folderName, out var definitionPath))
            {
                try
                {
                    if (File.Exists(definitionPath))
                    {
                        var json = await File.ReadAllTextAsync(definitionPath);
                        var folder = JsonConvert.DeserializeObject<StorageFolder>(json);

                        if (folder != null)
                        {
                            _logger.LogInformation("Found folder definition through index: {Path}", definitionPath);
                            return folder;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading folder definition from indexed path: {Path}", definitionPath);
                    // Index might be stale, remove it
                    _folderDefinitionPaths.Remove(folderName);
                }
            }

            // Check all application folders in the Definitions directory if index lookup failed
            string definitionsRoot = Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, "Config", "Definitions");

            if (Directory.Exists(definitionsRoot))
            {
                // Search all application subfolders
                foreach (var appDir in Directory.GetDirectories(definitionsRoot))
                {
                    string localFile = Path.Combine(appDir, $"{folderName}.json");

                    if (File.Exists(localFile))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(localFile);
                            var folder = JsonConvert.DeserializeObject<StorageFolder>(json);

                            if (folder != null)
                            {
                                _logger.LogInformation("Found local folder definition: {Path}", localFile);
                                // Update the index for future lookups
                                _folderDefinitionPaths[folderName] = localFile;
                                return folder;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error reading local folder definition: {Path}", localFile);
                        }
                    }
                }
            }

            // If not found locally, try the server
            try
            {
                string serverDefinitionsRoot = Path.Combine(NetworkBaseFolder, "Definitions");

                if (Directory.Exists(serverDefinitionsRoot))
                {
                    // Search all application subfolders on server
                    foreach (var appDir in Directory.GetDirectories(serverDefinitionsRoot))
                    {
                        string serverFile = Path.Combine(appDir, $"{folderName}.json");

                        if (File.Exists(serverFile))
                        {
                            try
                            {
                                var json = await File.ReadAllTextAsync(serverFile);
                                var folder = JsonConvert.DeserializeObject<StorageFolder>(json);

                                if (folder != null)
                                {
                                    _logger.LogInformation("Found server folder definition: {Path}", serverFile);

                                    // Copy to local folder for future use
                                    string appName = Path.GetFileName(appDir);
                                    string localDir = Path.Combine(definitionsRoot, appName);
                                    Directory.CreateDirectory(localDir);

                                    string localFile = Path.Combine(localDir, $"{folderName}.json");
                                    await File.WriteAllTextAsync(localFile, json);
                                    _logger.LogInformation("Copied server definition to local path: {Path}", localFile);

                                    // Update index
                                    _folderDefinitionPaths[folderName] = localFile;

                                    return folder;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error reading server folder definition: {Path}", serverFile);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accessing server definitions");
            }

            _logger.LogWarning("Could not find folder definition: {FolderName}", folderName);
            return null;
        }

        private StorageConfiguration LoadConfiguration()
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                try
                {
                    return JsonConvert.DeserializeObject<StorageConfiguration>(json)
                           ?? StorageConfiguration.CreateDefault();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize configuration, creating default");
                    return StorageConfiguration.CreateDefault();
                }
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
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
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

                // Remove from definition paths index
                if (_folderDefinitionPaths.ContainsKey(folder.Name))
                {
                    _folderDefinitionPaths.Remove(folder.Name);
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