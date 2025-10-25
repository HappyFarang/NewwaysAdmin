// File: Mobile/NewwaysAdmin.Mobile/Services/SignalR/SignalRConnection.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Services.SignalR
{
    /// <summary>
    /// SignalR connection management only
    /// Single responsibility: Connect, disconnect, connection state
    /// </summary>
    public class SignalRConnection : IDisposable
    {
        private readonly ILogger<SignalRConnection> _logger;
        private HubConnection? _connection;
        private bool _isDisposed = false;

        public SignalRConnection(ILogger<SignalRConnection> logger)
        {
            _logger = logger;
        }

        // ===== CONNECTION ONLY =====

        public async Task<bool> ConnectAsync(string serverUrl)
        {
            if (_connection != null)
            {
                await DisconnectAsync();
            }

            try
            {
                _logger.LogInformation("Connecting to SignalR: {ServerUrl}", serverUrl);

                _connection = new HubConnectionBuilder()
                    .WithUrl($"{serverUrl}/hubs/universal")
                    .WithAutomaticReconnect()
                    .Build();

                await _connection.StartAsync();
                _logger.LogInformation("Connected to SignalR");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection failed");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_connection != null)
            {
                try
                {
                    await _connection.DisposeAsync();
                    _logger.LogInformation("Disconnected from SignalR");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during disconnect");
                }
                finally
                {
                    _connection = null;
                }
            }
        }

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public HubConnection? GetConnection() => _connection;

        // ===== DISPOSE =====

        public void Dispose()
        {
            if (!_isDisposed)
            {
                DisconnectAsync().GetAwaiter().GetResult();
                _isDisposed = true;
            }
        }
    }
}