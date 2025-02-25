using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NewwaysAdmin.FileSync.Models;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.FileSync.Server
{
    public interface IFileSyncServer
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
        Task NotifyClientsOfFileChangeAsync(FileChangeNotification notification);
        Task RegisterClientAsync(ClientInfo client);
        Task UnregisterClientAsync(string clientId);
    }

    public class FileSyncServer : IFileSyncServer
    {
        private readonly ILogger<FileSyncServer> _logger;
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<string, (ClientInfo Info, StreamWriter Writer)> _connectedClients;
        private readonly int _port;
        private CancellationTokenSource? _cts;

        public FileSyncServer(
            ILogger<FileSyncServer> logger,
            EnhancedStorageFactory storageFactory,
            int port)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _port = port;
            _listener = new TcpListener(IPAddress.Any, port);
            _connectedClients = new ConcurrentDictionary<string, (ClientInfo, StreamWriter)>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                _listener.Start();
                _logger.LogInformation("File sync server started on port {Port}", _port);

                // Start the client cleanup task
                _ = RunClientCleanupAsync(_cts.Token);

                while (!_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleClientConnectionAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Server shutdown requested");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in file sync server");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                await NotifyClientsOfShutdownAsync();
                _cts.Cancel();

                // Clean up connected clients
                foreach (var clientId in _connectedClients.Keys)
                {
                    await UnregisterClientAsync(clientId);
                }
            }

            _listener.Stop();
        }

        public async Task NotifyClientsOfFileChangeAsync(FileChangeNotification notification)
        {
            var relevantClients = _connectedClients
                .Where(kvp => kvp.Value.Info.SubscribedFolders.Contains(notification.FolderName))
                .Where(kvp => kvp.Key != notification.SourceClientId);

            var message = new SyncMessage
            {
                Type = "FileChange",
                MessageId = Guid.NewGuid().ToString(),
                Payload = new Dictionary<string, object>
                {
                    { "notification", notification }
                }
            };

            foreach (var (clientId, (info, writer)) in relevantClients)
            {
                try
                {
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(message));
                    _logger.LogInformation(
                        "Notified client {ClientId} about change in file {FileId}",
                        clientId,
                        notification.FileId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error notifying client {ClientId} about file change",
                        clientId);
                    await UnregisterClientAsync(clientId);
                }
            }
        }

        public async Task RegisterClientAsync(ClientInfo client)
        {
            // This method is now internal and called from HandleClientConnectionAsync
            // with the proper StreamWriter
            _logger.LogInformation(
                "Client {ClientId} ({Name}) registered with folders: {Folders}",
                client.ClientId,
                client.Name,
                string.Join(", ", client.SubscribedFolders));

            // Send registration confirmation
            if (_connectedClients.TryGetValue(client.ClientId, out var clientData))
            {
                var message = new SyncMessage
                {
                    Type = "RegistrationConfirmed",
                    MessageId = Guid.NewGuid().ToString(),
                    Payload = new Dictionary<string, object>
                    {
                        { "clientId", client.ClientId }
                    }
                };

                await clientData.Writer.WriteLineAsync(JsonConvert.SerializeObject(message));
            }
        }

        public async Task UnregisterClientAsync(string clientId)
        {
            if (_connectedClients.TryRemove(clientId, out var clientData))
            {
                _logger.LogInformation(
                    "Client {ClientId} ({Name}) unregistered",
                    clientId,
                    clientData.Info.Name);

                try
                {
                    clientData.Writer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing client writer for {ClientId}", clientId);
                }
            }
        }

        private async Task HandleClientConnectionAsync(TcpClient tcpClient)
        {
            try
            {
                using var stream = tcpClient.GetStream();
                using var reader = new StreamReader(stream);
                var writer = new StreamWriter(stream) { AutoFlush = true };

                var endpoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                _logger.LogInformation("New client connection from {IpAddress}",
                    endpoint?.Address.ToString() ?? "unknown");

                // Read initial registration message
                var registrationJson = await reader.ReadLineAsync(_cts?.Token ?? default);
                if (registrationJson == null) return;

                var registrationMessage = JsonConvert.DeserializeObject<SyncMessage>(registrationJson);
                if (registrationMessage?.Type != "Registration") return;

                var clientInfo = JsonConvert.DeserializeObject<ClientInfo>(
                    JsonConvert.SerializeObject(registrationMessage.Payload["clientInfo"]));

                if (clientInfo == null) return;

                // Add IP address information
                clientInfo.IpAddress = endpoint?.Address.ToString();

                // Store client information with its writer
                if (_connectedClients.TryAdd(clientInfo.ClientId, (clientInfo, writer)))
                {
                    await RegisterClientAsync(clientInfo);

                    // Handle ongoing communication
                    while (!_cts?.Token.IsCancellationRequested ?? false)
                    {
                        var messageJson = await reader.ReadLineAsync(_cts.Token);
                        if (messageJson == null) break;

                        await HandleClientMessageAsync(clientInfo.ClientId, messageJson);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client connection");
            }
            finally
            {
                tcpClient.Dispose();
            }
        }

        private async Task HandleClientMessageAsync(string clientId, string messageJson)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<SyncMessage>(messageJson);
                if (message == null) return;

                switch (message.Type)
                {
                    case "FileChange":
                        var notification = JsonConvert.DeserializeObject<FileChangeNotification>(
                            JsonConvert.SerializeObject(message.Payload["notification"]));
                        if (notification != null)
                        {
                            await NotifyClientsOfFileChangeAsync(notification);
                        }
                        break;

                    case "Heartbeat":
                        if (_connectedClients.TryGetValue(clientId, out var clientData))
                        {
                            clientData.Info.LastSeen = DateTime.UtcNow;
                        }
                        break;

                    default:
                        _logger.LogWarning("Unknown message type received: {Type}", message.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client message: {Message}", messageJson);
            }
        }

        private async Task RunClientCleanupAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var inactiveClients = _connectedClients
                        .Where(kvp => DateTime.UtcNow - kvp.Value.Info.LastSeen > TimeSpan.FromMinutes(5))
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var clientId in inactiveClients)
                    {
                        _logger.LogWarning("Client {ClientId} inactive for too long, disconnecting", clientId);
                        await UnregisterClientAsync(clientId);
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in client cleanup task");
                }
            }
        }

        private async Task NotifyClientsOfShutdownAsync()
        {
            var shutdownMessage = new SyncMessage
            {
                Type = "ServerShutdown",
                MessageId = Guid.NewGuid().ToString(),
                Payload = new Dictionary<string, object>
                {
                    { "message", "Server is shutting down" }
                }
            };

            foreach (var (clientId, (_, writer)) in _connectedClients)
            {
                try
                {
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(shutdownMessage));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying client {ClientId} of shutdown", clientId);
                }
            }
        }
    }
}