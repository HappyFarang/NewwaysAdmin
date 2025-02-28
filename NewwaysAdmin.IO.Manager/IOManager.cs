using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using Newtonsoft.Json;
using NewwaysAdmin.Shared.Configuration;

namespace NewwaysAdmin.IO.Manager
{
    public class IOManager
    {
        private readonly ILogger<IOManager> _logger;
        private readonly IOManagerOptions _options;
        private readonly MachineConfigProvider _machineConfigProvider;
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly Dictionary<string, StorageFolder> _loadedFolders = new();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private readonly ConfigSyncTracker _configSyncTracker;
        private readonly string _machineId;
        private readonly bool _isServer;
        private readonly string _applicationName;
        private readonly string _localDefinitionsPath;
        private readonly string _serverDefinitionsPath;
        private readonly string LocalBaseFolder = @"C:\NewwaysData";
        private readonly string NetworkBaseFolder = @"X:\NewwaysAdmin";

        private readonly MachineConfig _machineConfig;

        public event EventHandler<NewDataEventArgs>? NewDataArrived;
        public bool IsServer => _isServer;
        public IOManager(
         ILogger<IOManager> logger,
          IOManagerOptions options,
          MachineConfigProvider machineConfigProvider)
        {
            _logger = logger;
            _options = options;
            _machineConfigProvider = machineConfigProvider;
            _applicationName = options.ApplicationName;
            _storageFactory = new EnhancedStorageFactory(logger);

            // Load the shared machine config
            var sharedConfig = machineConfigProvider.LoadConfigAsync().Result;
            _machineId = sharedConfig.MachineName;
            _isServer = sharedConfig.MachineRole == "SERVER";

            // Create IO-specific machine config
            _machineConfig = new MachineConfig
            {
                MachineRole = sharedConfig.MachineRole,
                ExcludedFolders = new List<string> { "Machine" },  // Exclude machine config folder
                OneWayFolders = new List<string>(),
                OneWayFoldersPush = new List<string>(),
                CopyOnUpdate = new List<string> { "Config" }
            };

            _localDefinitionsPath = Path.Combine(LocalBaseFolder, "Config", "Definitions", _applicationName);
            _serverDefinitionsPath = Path.Combine(NetworkBaseFolder, "Definitions", _applicationName);

            Directory.CreateDirectory(_localDefinitionsPath);
            Directory.CreateDirectory(_serverDefinitionsPath);

            _configSyncTracker = new ConfigSyncTracker(LocalBaseFolder, logger);

            SetupWatchers();

            // Only start config sync for clients
            if (!_isServer)
            {
                Task.Run(() => StartConfigSyncMonitorAsync());
            }

            _logger.LogInformation("IO Manager initialized successfully for application: {AppName}", _applicationName);
        }

        private void SetupWatchers()
        {
            if (_isServer)
            {
                // Server watches network folder for incoming files
                SetupRecursiveWatcher(NetworkBaseFolder, isLocal: false);
            }
            else
            {
                // Clients watch local folders for outgoing files
                SetupRecursiveWatcher(LocalBaseFolder, isLocal: true);
            }
        }

        private void SetupRecursiveWatcher(string baseFolder, bool isLocal)
        {
            foreach (var dir in Directory.GetDirectories(baseFolder, "*", SearchOption.AllDirectories))
            {
                // Skip excluded folders
                if (_machineConfig.ExcludedFolders.Any(ex =>
                    dir.StartsWith(Path.Combine(baseFolder, ex), StringComparison.OrdinalIgnoreCase)))
                    continue;

                var watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                watcher.Created += async (s, e) => await OnFileCreatedAsync(e.FullPath, isLocal);
                _watchers[dir] = watcher;
            }
        }

        private async Task OnFileCreatedAsync(string filePath, bool isLocal)
        {
            try
            {
                var relativePath = GetRelativePath(filePath, isLocal ? LocalBaseFolder : NetworkBaseFolder);
                var folderPath = Path.GetDirectoryName(relativePath);

                // Handle config files differently
                if (IsConfigPath(relativePath))
                {
                    await HandleConfigFileAsync(filePath, relativePath, isLocal);
                    return;
                }

                // Handle regular data files
                await HandleDataFileAsync(filePath, relativePath, isLocal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            }
        }

        private bool IsConfigPath(string relativePath)
        {
            return relativePath.StartsWith("Config", StringComparison.OrdinalIgnoreCase);
        }

        private async Task HandleConfigFileAsync(string filePath, string relativePath, bool isLocal)
        {
            if (_isServer && isLocal)
            {
                // Server updates network config
                var networkPath = Path.Combine(NetworkBaseFolder, relativePath);
                await CopyFileAsync(filePath, networkPath);
                _logger.LogInformation("Updated network config: {File}", relativePath);
            }
            else if (!_isServer && !isLocal && _machineConfig.CopyOnUpdate.Contains("Config"))
            {
                // Client copies updated config from network
                var localPath = Path.Combine(LocalBaseFolder, relativePath);
                var serverModTime = File.GetLastWriteTimeUtc(filePath);

                if (_configSyncTracker.NeedsUpdate(relativePath, serverModTime))
                {
                    await CopyFileAsync(filePath, localPath);
                    _configSyncTracker.UpdateTracking(relativePath, serverModTime);
                    _logger.LogInformation("Updated local config: {File}", relativePath);
                }
            }
        }

        private async Task HandleDataFileAsync(string filePath, string relativePath, bool isLocal)
        {
            var folderPath = Path.GetDirectoryName(relativePath);
            if (string.IsNullOrEmpty(folderPath)) return;

            // Determine if this is a one-way push folder
            bool isOneWayPush = _machineConfig.OneWayFoldersPush.Any(f =>
                folderPath.StartsWith(f, StringComparison.OrdinalIgnoreCase));

            if (_isServer)
            {
                if (!isLocal) // Server receiving from network
                {
                    var localPath = Path.Combine(LocalBaseFolder, relativePath);
                    await MoveFileAsync(filePath, localPath);
                    NotifyNewData(folderPath, localPath);
                }
            }
            else // Client
            {
                if (isLocal && isOneWayPush) // Client pushing to network
                {
                    var networkPath = Path.Combine(NetworkBaseFolder, relativePath);
                    await MoveFileAsync(filePath, networkPath);
                }
            }
        }

        private async Task CopyFileAsync(string sourcePath, string targetPath)
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var sourceStream = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream);
        }

        private async Task MoveFileAsync(string sourcePath, string targetPath)
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Wait for file to be completely written
            await WaitForFileAccessAsync(sourcePath);
            File.Move(sourcePath, targetPath, true);
        }

        private async Task WaitForFileAccessAsync(string filePath)
        {
            int attempts = 3;
            while (attempts > 0)
            {
                try
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    return;
                }
                catch (IOException)
                {
                    attempts--;
                    if (attempts == 0) throw;
                    await Task.Delay(1000);
                }
            }
        }

        private async Task StartConfigSyncMonitorAsync()
        {
            while (true)
            {
                try
                {
                    if (_machineConfig.CopyOnUpdate.Contains("Config"))
                    {
                        var networkConfigPath = Path.Combine(NetworkBaseFolder, "Config");
                        if (Directory.Exists(networkConfigPath))
                        {
                            foreach (var file in Directory.GetFiles(networkConfigPath, "*.*", SearchOption.AllDirectories))
                            {
                                var relativePath = GetRelativePath(file, NetworkBaseFolder);
                                var serverModTime = File.GetLastWriteTimeUtc(file);

                                if (_configSyncTracker.NeedsUpdate(relativePath, serverModTime))
                                {
                                    var localPath = Path.Combine(LocalBaseFolder, relativePath);
                                    await CopyFileAsync(file, localPath);
                                    _configSyncTracker.UpdateTracking(relativePath, serverModTime);
                                    _logger.LogInformation("Updated local config: {File}", relativePath);
                                }
                            }
                        }
                    }

                    // Also check folder definitions specific to this application
                    var definitionsNetworkPath = Path.Combine(NetworkBaseFolder, "Definitions", _applicationName);
                    if (Directory.Exists(definitionsNetworkPath))
                    {
                        foreach (var file in Directory.GetFiles(definitionsNetworkPath, "*.json"))
                        {
                            var fileName = Path.GetFileName(file);
                            var localPath = Path.Combine(_localDefinitionsPath, fileName);
                            var serverModTime = File.GetLastWriteTimeUtc(file);

                            // Use the same tracking mechanism for definitions
                            var relativePath = $"Definitions/{_applicationName}/{fileName}";
                            if (_configSyncTracker.NeedsUpdate(relativePath, serverModTime))
                            {
                                await CopyFileAsync(file, localPath);
                                _configSyncTracker.UpdateTracking(relativePath, serverModTime);
                                _logger.LogInformation("Updated folder definition {File} from server", fileName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in config sync");
                }

                // Check more frequently if desired (or keep at 1 minute)
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            return Path.GetRelativePath(basePath, fullPath)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void NotifyNewData(string folder, string filePath)
        {
            NewDataArrived?.Invoke(this, new NewDataEventArgs
            {
                Folder = folder,
                FilePath = filePath
            });
        }
        public async Task ProcessIncomingFilesAsync(CancellationToken cancellationToken = default)
        {
            if (!_isServer) return; // Only server processes incoming files
            try
            {
                var networkIncoming = Path.Combine(NetworkBaseFolder, "Incoming");
                if (!Directory.Exists(networkIncoming)) return;

                foreach (var file in Directory.GetFiles(networkIncoming, "*.*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var relativePath = GetRelativePath(file, NetworkBaseFolder);
                    var localPath = Path.Combine(LocalBaseFolder, relativePath);
                    await MoveFileAsync(file, localPath);

                    var folderPath = Path.GetDirectoryName(relativePath);
                    NotifyNewData(folderPath ?? string.Empty, localPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing incoming files");
            }
        }

        public async Task ProcessConfigUpdatesAsync(CancellationToken cancellationToken = default)
        {
            if (!_isServer) return; // Only server processes config updates

            try
            {
                var configFolder = Path.Combine(NetworkBaseFolder, "Config");
                if (!Directory.Exists(configFolder)) return;

                foreach (var file in Directory.GetFiles(configFolder, "*.*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var relativePath = GetRelativePath(file, NetworkBaseFolder);
                    var localPath = Path.Combine(LocalBaseFolder, relativePath);

                    await CopyFileAsync(file, localPath);
                    _logger.LogInformation("Updated local config: {File}", relativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing config updates");
            }
        }

        public async Task SyncConfigFilesAsync(CancellationToken cancellationToken = default)
        {
            if (_isServer) return; // Only clients sync config files

            try
            {
                // Always check defined paths that need to be kept up to date
                foreach (var configPath in _machineConfig.CopyOnUpdate)
                {
                    var networkPath = Path.Combine(NetworkBaseFolder, configPath);
                    if (!Directory.Exists(networkPath)) continue;

                    _logger.LogInformation("Checking for config updates in {Path}", configPath);

                    // Process all files in the directory and subdirectories
                    foreach (var file in Directory.GetFiles(networkPath, "*.*", SearchOption.AllDirectories))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var relativePath = GetRelativePath(file, NetworkBaseFolder);
                        var localPath = Path.Combine(LocalBaseFolder, relativePath);

                        bool needsUpdate = false;

                        if (!File.Exists(localPath))
                        {
                            needsUpdate = true;
                        }
                        else
                        {
                            var serverModTime = File.GetLastWriteTimeUtc(file);
                            var localModTime = File.GetLastWriteTimeUtc(localPath);
                            needsUpdate = serverModTime > localModTime;
                        }

                        if (needsUpdate)
                        {
                            _logger.LogInformation("Updating file {File} from server", relativePath);
                            await CopyFileAsync(file, localPath);
                            _configSyncTracker.UpdateTracking(relativePath, File.GetLastWriteTimeUtc(file));
                        }
                    }
                }

                // Also check specific folder definitions
                var definitionsNetworkPath = Path.Combine(NetworkBaseFolder, "Definitions", _applicationName);
                if (Directory.Exists(definitionsNetworkPath))
                {
                    foreach (var file in Directory.GetFiles(definitionsNetworkPath, "*.json"))
                    {
                        var fileName = Path.GetFileName(file);
                        var localPath = Path.Combine(_localDefinitionsPath, fileName);

                        bool needsUpdate = false;

                        if (!File.Exists(localPath))
                        {
                            needsUpdate = true;
                        }
                        else
                        {
                            var serverModTime = File.GetLastWriteTimeUtc(file);
                            var localModTime = File.GetLastWriteTimeUtc(localPath);
                            needsUpdate = serverModTime > localModTime;
                        }

                        if (needsUpdate)
                        {
                            _logger.LogInformation("Updating folder definition {File} from server", fileName);
                            await CopyFileAsync(file, localPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing config files");
            }
        }

        public async Task ProcessPendingTransfersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var outgoingFolder = Path.Combine(LocalBaseFolder, "Outgoing");
                if (!Directory.Exists(outgoingFolder)) return;

                foreach (var file in Directory.GetFiles(outgoingFolder, "*.*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var relativePath = GetRelativePath(file, LocalBaseFolder);
                    var targetPath = _isServer ?
                        Path.Combine(LocalBaseFolder, "Processed", relativePath) :
                        Path.Combine(NetworkBaseFolder, "Incoming", relativePath);

                    await MoveFileAsync(file, targetPath);
                    _logger.LogInformation("Processed file transfer: {File}", relativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending transfers");
            }
        }

        public async Task<IDataStorage<T>> GetStorageAsync<T>(string baseFolderName) where T : class, new()
        {
            // Keep the uniqueFolderName construction as is
            string uniqueFolderName = $"{_applicationName}_{baseFolderName}";

            _logger.LogDebug("Getting storage for folder: {FolderName}", uniqueFolderName);

            if (!_loadedFolders.ContainsKey(uniqueFolderName))
            {
                await LoadFolderDefinitionAsync(uniqueFolderName);
            }

            return _storageFactory.GetStorage<T>(uniqueFolderName);
        }

        private async Task LoadFolderDefinitionAsync(string folderName)
        {
            try
            {
                // First try to load from server
                var serverFolder = await LoadServerDefinitionAsync(folderName);

                // Then check local definition
                var localFolder = await LoadLocalDefinitionAsync(folderName);

                StorageFolder? folder = null;

                if (serverFolder != null && localFolder != null)
                {
                    // Both exist - check timestamps for newest
                    var serverPath = Path.Combine(_serverDefinitionsPath, $"{folderName}.json");
                    var localPath = Path.Combine(_localDefinitionsPath, $"{folderName}.json");

                    var serverModTime = File.GetLastWriteTimeUtc(serverPath);
                    var localModTime = File.GetLastWriteTimeUtc(localPath);

                    if (serverModTime > localModTime)
                    {
                        // Server is newer, copy to local
                        _logger.LogInformation("Server definition is newer. Updating local copy for {Folder}", folderName);
                        await CopyFileAsync(serverPath, localPath);
                        folder = serverFolder;
                    }
                    else
                    {
                        folder = localFolder;
                    }
                }
                else if (serverFolder != null)
                {
                    // Only server exists - copy to local
                    _logger.LogInformation("Found folder definition on server. Creating local copy for {Folder}", folderName);
                    var serverPath = Path.Combine(_serverDefinitionsPath, $"{folderName}.json");
                    var localPath = Path.Combine(_localDefinitionsPath, $"{folderName}.json");
                    await CopyFileAsync(serverPath, localPath);
                    folder = serverFolder;
                }
                else if (localFolder != null)
                {
                    folder = localFolder;
                }
                else
                {
                    // Neither exists - create new
                    folder = await CreateNewFolderDefinitionAsync(folderName);
                }

                if (folder == null)
                {
                    throw new InvalidOperationException($"Could not create folder definition for: {folderName}");
                }

                _loadedFolders[folderName] = folder;
                _storageFactory.RegisterFolder(folder);

                if (!await FolderExistsOnServerAsync(folderName))
                {
                    await SaveFolderDefinitionAsync(folderName, folder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load/create folder definition for {Folder}", folderName);
                throw;
            }
        }

        private async Task SaveFolderDefinitionAsync(string folderName, StorageFolder folder)
        {
            try
            {
                var localPath = Path.Combine(_localDefinitionsPath, $"{folderName}.json");
                await SaveDefinitionToPathAsync(localPath, folder);
                _logger.LogDebug("Saved local folder definition for {Folder}", folderName);

                var serverPath = Path.Combine(_serverDefinitionsPath, $"{folderName}.json");
                await SaveDefinitionToPathAsync(serverPath, folder);
                _logger.LogDebug("Saved server folder definition for {Folder}", folderName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save folder definition for {Folder}", folderName);
                throw;
            }
        }

        private async Task SaveDefinitionToPathAsync(string path, StorageFolder folder)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(folder, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            });
            await File.WriteAllTextAsync(path, json);
        }

        private async Task<StorageFolder?> CreateNewFolderDefinitionAsync(string folderName)
        {
            var machineConfig = await _machineConfigProvider.LoadConfigAsync();

            // Extract the base name without application prefix
            string shortFolderName = folderName;
            if (folderName.StartsWith($"{_applicationName}_"))
            {
                shortFolderName = folderName.Substring(_applicationName.Length + 1);
            }

            // Create a more intuitive path structure
            string path;

            // Special handling for config folders
            if (shortFolderName.Equals("Config", StringComparison.OrdinalIgnoreCase))
            {
                // Place app configs in Config/AppName
                path = $"Config/{_applicationName}";
            }
            else if (shortFolderName.Contains("Config", StringComparison.OrdinalIgnoreCase))
            {
                // If it has "Config" in the name but isn't exactly "Config"
                path = $"Config/{_applicationName}";
            }
            else
            {
                // For other folders, place them under the app folder
                path = _applicationName;
            }

            _logger.LogInformation("Creating folder definition for {FolderName} with path {Path}", folderName, path);

            return new StorageFolder
            {
                Name = folderName,
                Description = $"Auto-created folder for {_applicationName}",
                Type = StorageType.Json,
                Path = path,
                IsShared = false,
                CreateBackups = true,
                MaxBackupCount = 5,
                CreatedBy = $"{machineConfig.MachineName}_{_applicationName}"
            };
        }

        private async Task<bool> FolderExistsOnServerAsync(string folderName)
        {
            var path = Path.Combine(_serverDefinitionsPath, $"{folderName}.json");
            return await Task.Run(() => File.Exists(path));
        }

        private async Task<StorageFolder?> LoadLocalDefinitionAsync(string folderName)
        {
            var path = Path.Combine(_localDefinitionsPath, $"{folderName}.json");
            _logger.LogDebug("Checking local definition at: {Path}", path);

            if (!File.Exists(path))
            {
                _logger.LogDebug("No local definition found at: {Path}", path);
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonConvert.DeserializeObject<StorageFolder>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading local definition");
                return null;
            }
        }

        private async Task<StorageFolder?> LoadServerDefinitionAsync(string folderName)
        {
            var path = Path.Combine(_serverDefinitionsPath, $"{folderName}.json");
            _logger.LogDebug("Checking server definition at: {Path}", path);

            if (!File.Exists(path))
            {
                _logger.LogDebug("No server definition found at: {Path}", path);
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonConvert.DeserializeObject<StorageFolder>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading server definition");
                return null;
            }
        }
    }

    // Configuration classes for JSON
    public class MachineConfig
    {
        public string MachineRole { get; set; } = "Client";  // Default to Client
        public List<string> ExcludedFolders { get; set; } = new();
        public List<string> OneWayFolders { get; set; } = new();
        public List<string> OneWayFoldersPush { get; set; } = new();
        public List<string> CopyOnUpdate { get; set; } = new();
    }

    public class NewDataEventArgs : EventArgs
    {
        public string Folder { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}