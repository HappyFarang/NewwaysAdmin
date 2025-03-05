using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using Newtonsoft.Json;
using NewwaysAdmin.Shared.Configuration;
using System.Threading;

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
        public static readonly string NetworkBaseFolder = @"X:\NewwaysAdmin";

        private readonly MachineConfig _machineConfig;

        public event EventHandler<NewDataEventArgs>? NewDataArrived;
        public bool IsServer => _isServer;
        public string LocalBaseFolder => _options.LocalBaseFolder;

        public IOManager(
    ILogger<IOManager> logger,
    IOManagerOptions options,
    MachineConfigProvider machineConfigProvider,
    EnhancedStorageFactory storageFactory)
        {
            _logger = logger;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _machineConfigProvider = machineConfigProvider ?? throw new ArgumentNullException(nameof(machineConfigProvider));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _applicationName = options.ApplicationName;

            // Load machine config from the fixed location outside IO system
            var sharedConfig = machineConfigProvider.LoadConfigAsync().Result;
            _machineId = sharedConfig.MachineName;
            _isServer = sharedConfig.MachineRole == "SERVER";

            // Use machine config's base folder if available, otherwise use the one from options
            var localBaseFolder = !string.IsNullOrEmpty(sharedConfig.LocalBaseFolder)
                ? sharedConfig.LocalBaseFolder
                : options.LocalBaseFolder;

            if (localBaseFolder != options.LocalBaseFolder)
            {
                _logger.LogInformation("Using machine config LocalBaseFolder: {Path} instead of options value: {OptionsPath}",
                    localBaseFolder, options.LocalBaseFolder);
            }

            // Continue with existing code...
            _machineConfig = new MachineConfig
            {
                MachineRole = sharedConfig.MachineRole,
                ExcludedFolders = new List<string> { "Machine" },
                OneWayFolders = new List<string>(),
                OneWayFoldersPush = new List<string>(),
                CopyOnUpdate = new List<string> { "Config" }
            };

            _localDefinitionsPath = Path.Combine(localBaseFolder, "Config", "Definitions", _applicationName);
            _serverDefinitionsPath = Path.Combine(NetworkBaseFolder, "Definitions", _applicationName);

            // Continue with the rest of your initialization...
            Directory.CreateDirectory(_localDefinitionsPath);
            if (_isServer)
            {
                try
                {
                    Directory.CreateDirectory(_serverDefinitionsPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not create server definitions directory. This is expected for client machines.");
                }
            }

            _configSyncTracker = new ConfigSyncTracker(localBaseFolder, logger);

            SetupWatchers();

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
                SetupRecursiveWatcher(NetworkBaseFolder, isLocal: false);
            }
            else
            {
                SetupRecursiveWatcher(LocalBaseFolder, isLocal: true);
            }
        }

        private void SetupRecursiveWatcher(string baseFolder, bool isLocal)
        {
            foreach (var dir in Directory.GetDirectories(baseFolder, "*", SearchOption.AllDirectories))
            {
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

                if (IsConfigPath(relativePath))
                {
                    await HandleConfigFileAsync(filePath, relativePath, isLocal);
                    return;
                }

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
                var networkPath = Path.Combine(NetworkBaseFolder, relativePath);
                await CopyFileAsync(filePath, networkPath);
                _logger.LogInformation("Updated network config: {File}", relativePath);
            }
            else if (!_isServer && !isLocal && _machineConfig.CopyOnUpdate.Contains("Config"))
            {
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

            bool isOneWayPush = _machineConfig.OneWayFoldersPush.Any(f =>
                folderPath.StartsWith(f, StringComparison.OrdinalIgnoreCase));

            if (_isServer)
            {
                if (!isLocal)
                {
                    var localPath = Path.Combine(LocalBaseFolder, relativePath);
                    await MoveFileAsync(filePath, localPath);
                    NotifyNewData(folderPath, localPath);
                }
            }
            else
            {
                if (isLocal && isOneWayPush)
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

                    var definitionsNetworkPath = Path.Combine(NetworkBaseFolder, "Definitions", _applicationName);
                    if (Directory.Exists(definitionsNetworkPath))
                    {
                        foreach (var file in Directory.GetFiles(definitionsNetworkPath, "*.json"))
                        {
                            var fileName = Path.GetFileName(file);
                            var localPath = Path.Combine(_localDefinitionsPath, fileName);
                            var serverModTime = File.GetLastWriteTimeUtc(file);

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

                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            return Path.GetRelativePath(basePath, fullPath)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal void NotifyNewData(string folder, string filePath)
        {
            NewDataArrived?.Invoke(this, new NewDataEventArgs
            {
                Folder = folder,
                FilePath = filePath
            });
        }

        public async Task ProcessIncomingFilesAsync(CancellationToken cancellationToken = default)
        {
            if (!_isServer) return;
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
            if (!_isServer) return;
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
            if (_isServer) return;
            try
            {
                string configPath = "Config";
                var networkPath = Path.Combine(NetworkBaseFolder, configPath);
                if (!Directory.Exists(networkPath))
                {
                    _logger.LogWarning("Server config path not found: {Path}", networkPath);
                    return;
                }

                _logger.LogInformation("Synchronizing config files from server path: {Path}", networkPath);

                foreach (var file in Directory.GetFiles(networkPath, "*.*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var relativePath = GetRelativePath(file, NetworkBaseFolder);
                    var localPath = Path.Combine(LocalBaseFolder, relativePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    _logger.LogInformation("Synchronizing file: {File}", relativePath);
                    await CopyFileAsync(file, localPath);
                    _configSyncTracker.UpdateTracking(relativePath, File.GetLastWriteTimeUtc(file));
                }

                _logger.LogInformation("Config synchronization complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing config files from server");
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

        public async Task<IDataStorage<T>> GetStorageAsync<T>(string folderName) where T : class, new()
        {
            return await _storageFactory.GetStorageAsync<T>(folderName);
        }
        /// <summary>
        /// Queues an object for transfer to the server by serializing it to JSON and saving it in the outgoing folder
        /// </summary>
        /// <typeparam name="T">The type of object to transfer</typeparam>
        /// <param name="folderPath">The folder path relative to base folder (e.g. "Data/PdfProcessor/Scans")</param>
        /// <param name="fileId">The unique identifier for the file</param>
        /// <param name="data">The data to serialize and transfer</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task QueueForServerTransferAsync<T>(string folderPath, string fileId, T data) where T : class
        {
            if (_isServer)
            {
                // If this is a server, just log the operation
                _logger.LogInformation("Running on server, not queueing data for transfer: {FileId}", fileId);
                return;
            }

            try
            {
                // First, serialize the data to JSON
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);

                // Create the outgoing directory structure
                string outgoingDir = Path.Combine(LocalBaseFolder, "Outgoing", folderPath);
                Directory.CreateDirectory(outgoingDir);

                // Save the file to outgoing directory with a JSON extension
                string outgoingPath = Path.Combine(outgoingDir, $"{fileId}.json");
                await File.WriteAllTextAsync(outgoingPath, json);

                _logger.LogInformation("Queued file for server transfer: {FilePath}", outgoingPath);

                // Try to process immediately if the server is available
                await ProcessPendingTransfersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queueing file for server transfer: {FileId}", fileId);
                throw;
            }
        }
    }

    public class NewDataEventArgs : EventArgs
    {
        public string Folder { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public class MachineConfig
    {
        public string MachineRole { get; set; } = "Client";  // Default to Client
        public List<string> ExcludedFolders { get; set; } = new();
        public List<string> OneWayFolders { get; set; } = new();
        public List<string> OneWayFoldersPush { get; set; } = new();
        public List<string> CopyOnUpdate { get; set; } = new();
    }
} 