using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.FileSync.Server
{
    public class FileSyncServerHost : IHostedService
    {
        private readonly ILogger<FileSyncServerHost> _logger;
        private readonly IFileSyncServer _server;

        public FileSyncServerHost(
            ILogger<FileSyncServerHost> logger,
            IFileSyncServer server)
        {
            _logger = logger;
            _server = server;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting FileSync server service...");
            await _server.StartAsync(cancellationToken);
            _logger.LogInformation("FileSync server service started successfully");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping FileSync server service...");
            await _server.StopAsync();
            _logger.LogInformation("FileSync server service stopped");
        }
    }
}