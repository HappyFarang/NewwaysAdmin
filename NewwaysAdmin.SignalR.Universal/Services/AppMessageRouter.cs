// File: NewwaysAdmin.SignalR.Universal/Services/AppMessageRouter.cs
using NewwaysAdmin.SignalR.Universal.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace NewwaysAdmin.SignalR.Universal.Services
{
    /// <summary>
    /// Routes messages to appropriate app-specific handlers
    /// Manages handler registration and message dispatch
    /// </summary>
    public class AppMessageRouter
    {
        private readonly ILogger<AppMessageRouter> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, Type> _handlerTypes = new();
        private readonly ConcurrentDictionary<string, IAppMessageHandler> _handlerInstances = new();

        public AppMessageRouter(ILogger<AppMessageRouter> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        // ===== HANDLER REGISTRATION =====

        public void RegisterHandler<T>(string appName) where T : class, IAppMessageHandler
        {
            _handlerTypes.TryAdd(appName, typeof(T));
            _logger.LogInformation("Registered message handler for app: {AppName} -> {HandlerType}",
                appName, typeof(T).Name);
        }

        public void RegisterHandler(string appName, Type handlerType)
        {
            if (!typeof(IAppMessageHandler).IsAssignableFrom(handlerType))
            {
                throw new ArgumentException($"Handler type {handlerType.Name} must implement IAppMessageHandler");
            }

            _handlerTypes.TryAdd(appName, handlerType);
            _logger.LogInformation("Registered message handler for app: {AppName} -> {HandlerType}",
                appName, handlerType.Name);
        }

        // ===== MESSAGE ROUTING =====

        public async Task<MessageHandlerResult> RouteMessageAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var handler = await GetHandlerAsync(message.TargetApp);
                if (handler == null)
                {
                    var errorMsg = $"No handler registered for app: {message.TargetApp}";
                    _logger.LogWarning(errorMsg);
                    return MessageHandlerResult.CreateError(errorMsg);
                }

                // Validate message format
                if (!await handler.ValidateMessageAsync(message))
                {
                    var errorMsg = $"Invalid message format for app: {message.TargetApp}";
                    _logger.LogWarning(errorMsg);
                    return MessageHandlerResult.CreateError(errorMsg);
                }

                // Check if handler supports this message type
                if (!handler.SupportedMessageTypes.Contains(message.MessageType))
                {
                    var errorMsg = $"Handler for {message.TargetApp} does not support message type: {message.MessageType}";
                    _logger.LogWarning(errorMsg);
                    return MessageHandlerResult.CreateError(errorMsg);
                }

                // Route to handler
                _logger.LogDebug("Routing message {MessageId} of type {MessageType} to {TargetApp}",
                    message.MessageId, message.MessageType, message.TargetApp);

                var result = await handler.HandleMessageAsync(message, connectionId);

                _logger.LogDebug("Message {MessageId} handled successfully: {Success}",
                    message.MessageId, result.Success);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error routing message {MessageId} to {TargetApp}",
                    message.MessageId, message.TargetApp);
                return MessageHandlerResult.CreateError($"Internal error: {ex.Message}");
            }
        }

        public async Task<object?> GetInitialDataAsync(string appName, AppConnection connection)
        {
            try
            {
                var handler = await GetHandlerAsync(appName);
                if (handler == null)
                {
                    _logger.LogWarning("No handler found for app {AppName} during initial data request", appName);
                    return null;
                }

                return await handler.GetInitialDataAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting initial data for app {AppName}", appName);
                return null;
            }
        }

        // ===== HANDLER LIFECYCLE =====

        public async Task NotifyConnectionAsync(string appName, AppConnection connection, bool isConnecting)
        {
            try
            {
                var handler = await GetHandlerAsync(appName);
                if (handler == null) return;

                if (isConnecting)
                {
                    await handler.OnAppConnectedAsync(connection);
                    _logger.LogDebug("Notified handler {AppName} of new connection: {ConnectionId}",
                        appName, connection.ConnectionId);
                }
                else
                {
                    await handler.OnAppDisconnectedAsync(connection);
                    _logger.LogDebug("Notified handler {AppName} of disconnection: {ConnectionId}",
                        appName, connection.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying handler {AppName} of connection change", appName);
            }
        }

        // ===== HANDLER MANAGEMENT =====

        private async Task<IAppMessageHandler?> GetHandlerAsync(string appName)
        {
            // Try to get cached instance
            if (_handlerInstances.TryGetValue(appName, out var cachedHandler))
            {
                return cachedHandler;
            }

            // Try to get handler type
            if (!_handlerTypes.TryGetValue(appName, out var handlerType))
            {
                return null;
            }

            // Create new instance using DI
            try
            {
                var handler = (IAppMessageHandler)_serviceProvider.GetRequiredService(handlerType);
                _handlerInstances.TryAdd(appName, handler);

                _logger.LogDebug("Created handler instance for app: {AppName}", appName);
                return handler;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create handler instance for app: {AppName}", appName);
                return null;
            }
        }

        public List<string> GetRegisteredApps()
        {
            return _handlerTypes.Keys.ToList();
        }

        public bool IsAppRegistered(string appName)
        {
            return _handlerTypes.ContainsKey(appName);
        }

        public async Task<List<string>> GetSupportedMessageTypesAsync(string appName)
        {
            var handler = await GetHandlerAsync(appName);
            return handler?.SupportedMessageTypes.ToList() ?? new List<string>();
        }
    }
}