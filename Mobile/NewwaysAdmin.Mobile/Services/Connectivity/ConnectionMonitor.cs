// File: Mobile/NewwaysAdmin.Mobile/Services/Connectivity/ConnectionMonitor.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services;

namespace NewwaysAdmin.Mobile.Services.Connectivity
{
    /// <summary>
    /// Background monitor that checks server connectivity periodically
    /// Updates ConnectionState which UI components can observe
    /// </summary>
    public class ConnectionMonitor : IDisposable
    {
        private readonly ILogger<ConnectionMonitor> _logger;
        private readonly ConnectionState _connectionState;
        private readonly IConnectionService _connectionService;

        private CancellationTokenSource? _cts;
        private bool _isRunning = false;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(2); // Let app start first

        public ConnectionMonitor(
            ILogger<ConnectionMonitor> logger,
            ConnectionState connectionState,
            IConnectionService connectionService)
        {
            _logger = logger;
            _connectionState = connectionState;
            _connectionService = connectionService;
        }

        // ===== START/STOP =====

        public void Start()
        {
            if (_isRunning)
                return;

            _logger.LogInformation("ConnectionMonitor starting...");
            _isRunning = true;
            _cts = new CancellationTokenSource();

            // Fire and forget - runs in background
            _ = MonitorLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _logger.LogInformation("ConnectionMonitor stopping...");
            _cts?.Cancel();
            _isRunning = false;
        }

        // ===== MONITOR LOOP =====

        private async Task MonitorLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Initial delay - let the app start up
                await Task.Delay(_initialDelay, cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await CheckConnectionAsync();
                    await Task.Delay(_checkInterval, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Normal shutdown
                _logger.LogDebug("ConnectionMonitor loop cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectionMonitor loop error");
            }
        }

        private async Task CheckConnectionAsync()
        {
            try
            {
                _logger.LogDebug("Checking server connection...");

                var result = await _connectionService.TestConnectionAsync();
                _connectionState.SetState(result.Success);

                if (result.Success)
                {
                    _logger.LogDebug("Server is reachable");
                }
                else
                {
                    _logger.LogDebug("Server not reachable: {Message}", result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking connection");
                _connectionState.SetOffline();
            }
        }

        // ===== MANUAL CHECK =====

        /// <summary>
        /// Force an immediate connection check (useful after login, etc.)
        /// </summary>
        public async Task CheckNowAsync()
        {
            await CheckConnectionAsync();
        }

        // ===== DISPOSE =====

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}