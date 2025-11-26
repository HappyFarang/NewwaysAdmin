// File: NewwaysAdmin.SignalR.Universal/Hubs/UniversalCommHub.cs
// FIXED: Removed generic methods that SignalR doesn't support

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Universal.Services;
using System.Text.Json;
using NewwaysAdmin.SignalR.Contracts.Models;
using NewwaysAdmin.SignalR.Contracts.Interfaces;

namespace NewwaysAdmin.SignalR.Universal.Hubs
{
    /// <summary>
    /// Universal communication hub for all apps in the Newways ecosystem
    /// Provides generic messaging with app-specific routing
    /// </summary>
    public class UniversalCommHub : Hub
    {
        private readonly ConnectionManager _connectionManager;
        private readonly AppMessageRouter _messageRouter;
        private readonly ILogger<UniversalCommHub> _logger;

        public UniversalCommHub(
            ConnectionManager connectionManager,
            AppMessageRouter messageRouter,
            ILogger<UniversalCommHub> logger)
        {
            _connectionManager = connectionManager;
            _messageRouter = messageRouter;
            _logger = logger;
        }

        // ===== CONNECTION LIFECYCLE =====

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var userAgent = httpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown";
            var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            _logger.LogInformation("New connection: {ConnectionId} from {IpAddress} - {UserAgent}",
                Context.ConnectionId, ipAddress, userAgent);

            // Add to general connections group
            await Groups.AddToGroupAsync(Context.ConnectionId, "AllConnections");

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connection = _connectionManager.GetConnection(Context.ConnectionId);

            if (exception != null)
            {
                _logger.LogWarning(exception, "Connection {ConnectionId} disconnected with error",
                    Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Connection {ConnectionId} disconnected normally",
                    Context.ConnectionId);
            }

            // Notify app handler if connection was registered
            if (connection != null)
            {
                await _messageRouter.NotifyConnectionAsync(connection.AppName, connection, false);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"App_{connection.AppName}");
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Device_{connection.DeviceType}");

                if (!string.IsNullOrEmpty(connection.UserId))
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{connection.UserId}");
                }
            }

            _connectionManager.RemoveConnection(Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "AllConnections");

            await base.OnDisconnectedAsync(exception);
        }

        // ===== APP REGISTRATION =====

        public async Task RegisterAppAsync(AppRegistration registration)
        {
            try
            {
                var httpContext = Context.GetHttpContext();
                var connection = new AppConnection
                {
                    ConnectionId = Context.ConnectionId,
                    AppName = registration.AppName,
                    AppVersion = registration.AppVersion,
                    DeviceId = registration.DeviceId,
                    DeviceType = registration.DeviceType,
                    UserAgent = httpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown",
                    IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                    ConnectedAt = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow,
                    Status = ConnectionStatus.Connected
                };

                _connectionManager.AddConnection(connection);

                // Join app-specific groups
                await Groups.AddToGroupAsync(Context.ConnectionId, $"App_{registration.AppName}");
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{registration.DeviceType}");

                _logger.LogInformation("App registered: {AppName} v{AppVersion} on {DeviceType} (Device: {DeviceId})",
                    registration.AppName, registration.AppVersion, registration.DeviceType, registration.DeviceId);

                // Notify app handler
                await _messageRouter.NotifyConnectionAsync(registration.AppName, connection, true);

                // Send initial data
                var initialData = await _messageRouter.GetInitialDataAsync(registration.AppName, connection);
                if (initialData != null)
                {
                    await Clients.Caller.SendAsync("InitialData", initialData);
                }

                // Send registration confirmation
                await Clients.Caller.SendAsync("RegistrationComplete", new
                {
                    connectionId = Context.ConnectionId,
                    serverTime = DateTime.UtcNow,
                    registeredApps = _messageRouter.GetRegisteredApps(),
                    supportedMessageTypes = await _messageRouter.GetSupportedMessageTypesAsync(registration.AppName)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering app: {AppName}", registration.AppName);
                await Clients.Caller.SendAsync("RegistrationError", $"Registration failed: {ex.Message}");
            }
        }

        public async Task AuthenticateUserAsync(string userId, string? authToken = null)
        {
            try
            {
                var connection = _connectionManager.GetConnection(Context.ConnectionId);
                if (connection == null)
                {
                    await Clients.Caller.SendAsync("AuthenticationError", "Connection not registered");
                    return;
                }

                // TODO: Implement actual authentication logic here
                // For now, just accept any userId

                connection.UserId = userId;
                connection.LastHeartbeat = DateTime.UtcNow;

                await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");

                _logger.LogInformation("User authenticated: {UserId} on connection {ConnectionId}",
                    userId, Context.ConnectionId);

                await Clients.Caller.SendAsync("AuthenticationComplete", new
                {
                    userId,
                    connectionId = Context.ConnectionId,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating user: {UserId}", userId);
                await Clients.Caller.SendAsync("AuthenticationError", $"Authentication failed: {ex.Message}");
            }
        }

        // ===== MESSAGING =====

        public async Task SendMessageAsync(UniversalMessage message)
        {
            try
            {
                _logger.LogDebug("Received message {MessageId} of type {MessageType} for app {TargetApp}",
                    message.MessageId, message.MessageType, message.TargetApp);

                // Route message to appropriate handler
                var result = await _messageRouter.RouteMessageAsync(message, Context.ConnectionId);

                // Send response back to caller
                await Clients.Caller.SendAsync("MessageResponse", new
                {
                    messageId = message.MessageId,
                    success = result.Success,
                    data = result.ResponseData,
                    error = result.ErrorMessage
                });

                // Handle broadcasting if requested
                if (result.ShouldBroadcast && !string.IsNullOrEmpty(result.BroadcastMessageType))
                {
                    await BroadcastMessageAsync(result.BroadcastMessageType, result.ResponseData, message.TargetApp, result.TargetConnections);
                }

                // Send acknowledgment if required
                if (message.RequiresAck)
                {
                    await Clients.Caller.SendAsync("MessageAck", new MessageAck
                    {
                        MessageId = message.MessageId,
                        ConnectionId = Context.ConnectionId,
                        Success = result.Success,
                        ErrorMessage = result.ErrorMessage
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
                await Clients.Caller.SendAsync("MessageResponse", new
                {
                    messageId = message.MessageId,
                    success = false,
                    error = $"Internal error: {ex.Message}"
                });
            }
        }

        // ===== HELPER METHOD FOR TYPED MESSAGES =====
        // Note: This is NOT a hub method - it's called from client-side code
        // Client will create UniversalMessage and call SendMessageAsync

        public async Task SendTypedMessage(string messageType, string targetApp, object data, bool requiresAck = false)
        {
            var message = new UniversalMessage
            {
                MessageType = messageType,
                TargetApp = targetApp,
                Data = JsonSerializer.SerializeToElement(data),
                RequiresAck = requiresAck,
                SourceApp = "Server" // Since this is called from server
            };

            await SendMessageAsync(message);
        }

        // ===== BROADCASTING =====

        public async Task BroadcastToAppAsync(string targetApp, string messageType, object data)
        {
            try
            {
                await Clients.Group($"App_{targetApp}").SendAsync("BroadcastMessage", new
                {
                    messageType,
                    targetApp,
                    data,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Broadcasted message type {MessageType} to app {TargetApp}", messageType, targetApp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting to app {TargetApp}", targetApp);
            }
        }

        public async Task BroadcastToUserAsync(string userId, string messageType, object data)
        {
            try
            {
                await Clients.Group($"User_{userId}").SendAsync("BroadcastMessage", new
                {
                    messageType,
                    userId,
                    data,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Broadcasted message type {MessageType} to user {UserId}", messageType, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting to user {UserId}", userId);
            }
        }

        private async Task BroadcastMessageAsync(string messageType, object data, string targetApp, List<string> specificConnections)
        {
            try
            {
                if (specificConnections.Any())
                {
                    // Broadcast to specific connections
                    await Clients.Clients(specificConnections).SendAsync("BroadcastMessage", new
                    {
                        messageType,
                        targetApp,
                        data,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    // Broadcast to all connections of target app
                    await Clients.Group($"App_{targetApp}").SendAsync("BroadcastMessage", new
                    {
                        messageType,
                        targetApp,
                        data,
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogDebug("Broadcasted message type {MessageType} to {TargetApp}", messageType, targetApp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting message", ex);
            }
        }

        // ===== CONNECTION HEALTH =====

        public async Task HeartbeatAsync()
        {
            var connection = _connectionManager.GetConnection(Context.ConnectionId);
            if (connection != null)
            {
                connection.LastHeartbeat = DateTime.UtcNow;
            }

            await Clients.Caller.SendAsync("HeartbeatAck", DateTime.UtcNow);
        }

        public async Task GetConnectionInfoAsync()
        {
            var connection = _connectionManager.GetConnection(Context.ConnectionId);
            await Clients.Caller.SendAsync("ConnectionInfo", connection);
        }

        // ===== MONITORING =====

        public async Task GetServerStatsAsync()
        {
            var stats = new
            {
                totalConnections = _connectionManager.GetActiveConnectionCount(),
                connectedApps = _connectionManager.GetConnectedApps(),
                registeredApps = _messageRouter.GetRegisteredApps(),
                serverTime = DateTime.UtcNow
            };

            await Clients.Caller.SendAsync("ServerStats", stats);
        }
    }
}