using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.FileSync.Client
{
    public class FileSyncClientHost : IHostedService
    {
        private readonly ILogger<FileSyncClientHost> _logger;
        private readonly FileSyncClient _client;
        private readonly HashSet<string> _foldersToWatch = new()
        {
            "Users",     // TODO: Make configurable
            "Reports"    // TODO: Make configurable
        };

        public FileSyncClientHost(
            ILogger<FileSyncClientHost> logger,
            FileSyncClient client)
        {
            _logger = logger;
            _client = client;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting FileSync client service...");

            try
            {
                await _client.ConnectAsync(_foldersToWatch, cancellationToken);
                _logger.LogInformation("FileSync client service started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting FileSync client service");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping FileSync client service...");
            _client.Disconnect();
            _logger.LogInformation("FileSync client service stopped");
            await Task.CompletedTask;
        }
    }
}