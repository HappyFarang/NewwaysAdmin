using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO;
using NewwaysAdmin.FileSync.Models;

namespace NewwaysAdmin.FileSync.Client
{
    public class FileSyncClient : IDisposable
    {
        private readonly ILogger<FileSyncClient> _logger;
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly string _clientId;
        private readonly string _clientName;
        private TcpClient? _tcpClient;
        private CancellationTokenSource? _cts;
        private bool _isConnected;

        public event EventHandler<Exception>? ConnectionLost;
        public event EventHandler<string>? MessageReceived;

        public FileSyncClient(
            ILogger<FileSyncClient> logger,
            string serverAddress,
            int serverPort,
            string clientId,
            string clientName)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverAddress = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
            _serverPort = serverPort;
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            _clientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
        }

        public bool IsConnected => _isConnected && _tcpClient?.Connected == true;

        public async Task ConnectAsync(HashSet<string> foldersToWatch, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                _logger.LogWarning("Already connected to server");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                _logger.LogInformation("Connecting to server at {Address}:{Port}", _serverAddress, _serverPort);

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_serverAddress, _serverPort, _cts.Token);

                var clientInfo = new ClientInfo
                {
                    ClientId = _clientId,
                    Name = _clientName,
                    SubscribedFolders = foldersToWatch,
                    LastSeen = DateTime.UtcNow,
                    Version = GetType().Assembly.GetName().Version?.ToString(),
                    Metadata = new Dictionary<string, string>
                    {
                        { "MachineName", Environment.MachineName },
                        { "OSVersion", Environment.OSVersion.ToString() }
                    }
                };

                _logger.LogInformation("Connected successfully. Registering client...");
                _isConnected = true;

                // Start the communication handling in a background task
                _ = Task.Run(() => HandleServerCommunicationAsync(_tcpClient, clientInfo, _cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to server");
                _isConnected = false;
                throw;
            }
        }

        private async Task HandleServerCommunicationAsync(TcpClient client, ClientInfo clientInfo, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { AutoFlush = true };

                // Send initial registration
                var registration = JsonConvert.SerializeObject(clientInfo);
                await writer.WriteLineAsync(registration);

                _logger.LogInformation("Client registration sent. Waiting for server messages...");

                // Keep reading messages from server
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var message = await reader.ReadLineAsync(cancellationToken);
                    if (message == null) break;

                    _logger.LogInformation("Received message from server: {Message}", message);
                    MessageReceived?.Invoke(this, message);

                    // Handle the message based on its type
                    await HandleMessageAsync(message, writer, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client shutdown requested");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in server communication");
                _isConnected = false;
                ConnectionLost?.Invoke(this, ex);
                throw;
            }
        }

        private async Task HandleMessageAsync(string message, StreamWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                // Deserialize the message to determine its type
                var messageObj = JsonConvert.DeserializeObject<dynamic>(message);

                // For now, just acknowledge receipt
                await writer.WriteLineAsync(JsonConvert.SerializeObject(new
                {
                    Type = "Acknowledgment",
                    OriginalMessageId = messageObj?.MessageId ?? "unknown"
                }));

                // TODO: Implement full message type handling in next iteration
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message: {Message}", message);
                // Send error response
                await writer.WriteLineAsync(JsonConvert.SerializeObject(new
                {
                    Type = "Error",
                    Error = ex.Message
                }));
            }
        }

        public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _tcpClient?.GetStream() == null)
            {
                throw new InvalidOperationException("Client is not connected");
            }

            try
            {
                using var writer = new StreamWriter(_tcpClient.GetStream()) { AutoFlush = true };
                await writer.WriteLineAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message");
                throw;
            }
        }

        public void Disconnect()
        {
            try
            {
                _cts?.Cancel();
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
                _isConnected = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnect");
            }
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}