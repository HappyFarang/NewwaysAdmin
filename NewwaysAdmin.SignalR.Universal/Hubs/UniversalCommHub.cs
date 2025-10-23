// File: NewwaysAdmin.SignalR.Universal/Hubs/UniversalCommHub.cs
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Universal.Models;
using NewwaysAdmin.SignalR.Universal.Services;
using System.Text.RegularExpressions;

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
                if (connection != null)
                {
                    connection.UserId = userId;
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");

                    _logger.LogInformation("User authenticated: {UserId} on connection {ConnectionId}",
                        userId, Context.ConnectionId);

                    await Clients.Caller.SendAsync("AuthenticationComplete", new { userId, serverTime = DateTime.UtcNow });
                }
                else
                {
                    await Clients.Caller.SendAsync("AuthenticationError", "Connection not registered");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating user: {UserId}", userId);
                await Clients.Caller.SendAsync("AuthenticationError", $"Authentication failed: {ex.Message}");
            }
        }

        // ===== GENERIC MESSAGING =====

        public async Task SendMessageAsync(UniversalMessage message)
        {
            try
            {
                // Set source info from connection
                var connection = _connectionManager.GetConnection(Context.ConnectionId);
                if (connection != null)
                {
                    message.SourceApp = connection.AppName;
                    message.UserId = connection.UserId;
                }

                _logger.LogDebug("Received message {MessageId} of type {MessageType} for {TargetApp}",
                    message.MessageId, message.MessageType, message.TargetApp);

                // Route message to appropriate handler
                var result = await _messageRouter.RouteMessageAsync(message, Context.ConnectionId);

                // Send response back to caller
                if (result.Success)
                {
                    if (result.ResponseData != null)
                    {
                        await Clients.Caller.SendAsync("MessageResponse", new
                        {
                            messageId = message.MessageId,
                            success = true,
                            data = result.ResponseData
                        });
                    }

                    // Handle broadcasting if requested
                    if (result.ShouldBroadcast)
                    {
                        await BroadcastMessageAsync(result.BroadcastMessageType!, result.ResponseData!,
                            message.TargetApp, result.TargetConnections);
                    }
                }
                else
                {
                    await Clients.Caller.SendAsync("MessageResponse", new
                    {
                        messageId = message.MessageId,
                        success = false,
                        error = result.ErrorMessage
                    });
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

        public async Task SendTypedMessageAsync<T>(string messageType, string targetApp, T data, bool requiresAck = false)
        {
            var message = new UniversalMessage<T>(messageType, "", targetApp, data)
            {
                RequiresAck = requiresAck
            };

            await SendMessageAsync(message);
        }

        // ===== BROADCASTING =====

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
                _logger.LogError(ex, "Error broadcasting message type {MessageType} to {TargetApp}", messageType, targetApp);
            }
        }

        public async Task BroadcastToAppAsync(string targetApp, string messageType, object data)
        {
            await BroadcastMessageAsync(messageType, data, targetApp, new List<string>());
        }

        public async Task BroadcastToUserAsync(string userId, string messageType, object data)
        {
            await Clients.Group($"User_{userId}").SendAsync("BroadcastMessage", new
            {
                messageType,
                targetUser = userId,
                data,
                timestamp = DateTime.UtcNow
            });
        }

        // ===== CONNECTION HEALTH =====

        public async Task HeartbeatAsync()
        {
            _connectionManager.UpdateHeartbeat(Context.ConnectionId);
            await Clients.Caller.SendAsync("HeartbeatAck", DateTime.UtcNow);
        }

        public async Task GetConnectionInfoAsync()
        {
            var connection = _connectionManager.GetConnection(Context.ConnectionId);
            await Clients.Caller.SendAsync("ConnectionInfo", connection);
        }

        // ===== ADMIN/MONITORING =====

        public async Task GetServerStatsAsync()
        {
            // Only allow this for admin connections - you can add authorization later
            var stats = new
            {
                totalConnections = _connectionManager.GetTotalConnectionCount(),
                appConnections = _connectionManager.GetAppConnectionCounts(),
                registeredApps = _messageRouter.GetRegisteredApps(),
                serverTime = DateTime.UtcNow
            };

            await Clients.Caller.SendAsync("ServerStats", stats);
        }
    }
}