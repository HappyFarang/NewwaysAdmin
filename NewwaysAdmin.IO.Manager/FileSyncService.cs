using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.IO.Manager
{
    public class FileSyncService : BackgroundService
    {
        private readonly ILogger<FileSyncService> _logger;
        private readonly IOManager _ioManager;
        private readonly ConfigSyncTracker _configSyncTracker;
        private readonly bool _isServer;

        public FileSyncService(
            ILogger<FileSyncService> logger,
            IOManager ioManager,
            ConfigSyncTracker configSyncTracker)
        {
            _logger = logger;
            _ioManager = ioManager;
            _configSyncTracker = configSyncTracker;
            _isServer = ioManager.IsServer; // We'd need to add this property to IOManager
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("File Sync Service starting...");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (_isServer)
                    {
                        await ProcessServerSyncAsync(stoppingToken);
                    }
                    else
                    {
                        await ProcessClientSyncAsync(stoppingToken);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in File Sync Service");
            }
            finally
            {
                _logger.LogInformation("File Sync Service stopping...");
            }
        }

        private async Task ProcessServerSyncAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Server collects files from network folder
                await _ioManager.ProcessIncomingFilesAsync(stoppingToken);

                // Server updates config files if needed
                await _ioManager.ProcessConfigUpdatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing server sync");
            }
        }

        private async Task ProcessClientSyncAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Check for config updates
                if (_configSyncTracker.HasPendingUpdates)
                {
                    await _ioManager.SyncConfigFilesAsync(stoppingToken);
                }

                // Process any pending file transfers
                await _ioManager.ProcessPendingTransfersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing client sync");
            }
        }
    }
}