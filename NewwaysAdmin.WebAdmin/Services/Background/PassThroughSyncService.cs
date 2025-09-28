// NewwaysAdmin.WebAdmin/Services/Background/PassThroughSyncService.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;

namespace NewwaysAdmin.WebAdmin.Services.Background
{
    /// <summary>
    /// Background service that synchronizes pre-processed files from external sources
    /// using PassThrough mode (direct file copying without re-serialization).
    /// Future: Will support signal-based sync for immediate updates.
    /// </summary>
    public class PassThroughSyncService : BackgroundService
    {
        private readonly ILogger<PassThroughSyncService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<SyncConfiguration> _syncConfigurations = new();

        private readonly TimeSpan _syncInterval = TimeSpan.FromSeconds(30); // 30-second sync cycle

        public PassThroughSyncService(
            ILogger<PassThroughSyncService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Register a sync path configuration (called during startup)
        /// </summary>
        public void RegisterSyncPath(string remotePath, string localFolderName, string description)
        {
            if (string.IsNullOrWhiteSpace(remotePath)) throw new ArgumentException("Remote path cannot be empty", nameof(remotePath));
            if (string.IsNullOrWhiteSpace(localFolderName)) throw new ArgumentException("Local folder name cannot be empty", nameof(localFolderName));

            var config = new SyncConfiguration
            {
                RemotePath = remotePath,
                LocalFolderName = localFolderName,
                Description = description ?? localFolderName
            };

            _syncConfigurations.Add(config);
            _logger.LogInformation("📂 Registered sync path: {RemotePath} → {LocalFolder} ({Description})",
                remotePath, localFolderName, description);
        }

        /// <summary>
        /// Main background sync loop (polling-based for now, signal-based in future)
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔄 PassThroughSyncService starting...");

            // Wait a bit for services to initialize
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await SyncAllConfiguredPathsAsync(stoppingToken);
                    await Task.Delay(_syncInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 PassThroughSyncService stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Critical error in PassThroughSyncService");
                throw; // Let the host handle restart
            }
        }

        /// <summary>
        /// Sync all registered paths
        /// </summary>
        private async Task SyncAllConfiguredPathsAsync(CancellationToken stoppingToken)
        {
            if (_syncConfigurations.Count == 0)
            {
                _logger.LogDebug("No sync paths configured, skipping sync cycle");
                return;
            }

            foreach (var config in _syncConfigurations)
            {
                if (stoppingToken.IsCancellationRequested) break;

                await SyncPathAsync(config, stoppingToken);
            }
        }

        /// <summary>
        /// Sync a specific path configuration
        /// </summary>
        private async Task SyncPathAsync(SyncConfiguration config, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var ioManager = scope.ServiceProvider.GetRequiredService<IOManager>();

                await ioManager.SyncRemotePathAsync(config.RemotePath, config.LocalFolderName, stoppingToken);
            }
            catch (Exception ex)
            {
                // Log the error but don't let it crash the entire service
                _logger.LogWarning(ex, "⚠️ Error syncing {Description} from {RemotePath} (remote source may be unavailable)",
                    config.Description, config.RemotePath);
            }
        }

        /// <summary>
        /// Future: Handle signal-based sync for immediate updates
        /// This method will be called when we receive signals from remote machines
        /// </summary>
        public async Task OnRemoteFileSignalAsync(string remotePath, string fileName)
        {
            try
            {
                _logger.LogInformation("📡 Received file signal: {FileName} from {RemotePath}", fileName, remotePath);

                // Find the matching sync configuration
                var config = _syncConfigurations.FirstOrDefault(c =>
                    string.Equals(c.RemotePath, remotePath, StringComparison.OrdinalIgnoreCase));

                if (config == null)
                {
                    _logger.LogWarning("No sync configuration found for remote path: {RemotePath}", remotePath);
                    return;
                }

                // Perform immediate sync for this path
                await SyncPathAsync(config, CancellationToken.None);

                _logger.LogDebug("✅ Signal-triggered sync completed for {Description}", config.Description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file signal from {RemotePath}", remotePath);
            }
        }

        /// <summary>
        /// Get current sync statistics (for monitoring/debugging)
        /// </summary>
        public SyncStatistics GetStatistics()
        {
            return new SyncStatistics
            {
                ConfiguredPaths = _syncConfigurations.Count,
                SyncInterval = _syncInterval,
                IsRunning = !ExecuteTask?.IsCompleted ?? false
            };
        }
    }

    /// <summary>
    /// Configuration for a sync path
    /// </summary>
    public class SyncConfiguration
    {
        public required string RemotePath { get; set; }
        public required string LocalFolderName { get; set; }
        public required string Description { get; set; }
    }

    /// <summary>
    /// Sync service statistics
    /// </summary>
    public class SyncStatistics
    {
        public int ConfiguredPaths { get; set; }
        public TimeSpan SyncInterval { get; set; }
        public bool IsRunning { get; set; }
    }
}