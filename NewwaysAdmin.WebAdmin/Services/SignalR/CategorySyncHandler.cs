// File: NewwaysAdmin.WebAdmin/Services/SignalR/CategorySyncHandler.cs
using NewwaysAdmin.SignalR.Contracts.Models;
using NewwaysAdmin.SignalR.Universal.Services;
using NewwaysAdmin.WebAdmin.Services.Categories;
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NewwaysAdmin.WebAdmin.Services.SignalR
{
    /// <summary>
    /// Handles MAUI app communication for category synchronization
    /// Supports bidirectional version exchange and data sync
    /// </summary>
    public class CategorySyncHandler : IAppMessageHandler
    {
        private readonly CategoryService _categoryService;
        private readonly ILogger<CategorySyncHandler> _logger;

        public string AppName => "MAUI_ExpenseTracker";

        public IEnumerable<string> SupportedMessageTypes => new[]
        {
            "VersionExchange",      // Compare versions, sync if needed
            "RequestFullData",      // Force download full data
            "RequestVersion",       // Just get current version number
            "UploadData",
            "HeartbeatCheck"
        };

        public CategorySyncHandler(CategoryService categoryService, ILogger<CategorySyncHandler> logger)
        {
            _categoryService = categoryService;
            _logger = logger;
        }

        // ===== MESSAGE HANDLING =====

        public async Task<MessageHandlerResult> HandleMessageAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                _logger.LogDebug("Handling message type {MessageType} from connection {ConnectionId}",
                    message.MessageType, connectionId);

                return message.MessageType switch
                {
                    "VersionExchange" => await HandleVersionExchangeAsync(message, connectionId),
                    "RequestFullData" => await HandleRequestFullDataAsync(message, connectionId),
                    "RequestVersion" => await HandleRequestVersionAsync(message, connectionId),
                    "HeartbeatCheck" => await HandleHeartbeatAsync(message, connectionId),
                    "UploadData" => await HandleUploadDataAsync(message, connectionId),
                    _ => MessageHandlerResult.CreateError($"Unsupported message type: {message.MessageType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message {MessageType} from {ConnectionId}",
                    message.MessageType, connectionId);
                return MessageHandlerResult.CreateError($"Internal error: {ex.Message}");
            }
        }

        // ===== VERSION EXCHANGE =====

        private async Task<MessageHandlerResult> HandleVersionExchangeAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var request = JsonSerializer.Deserialize<VersionExchangeMessage>(message.Data.GetRawText());

                if (request == null)
                {
                    return MessageHandlerResult.CreateError("Invalid version exchange message");
                }

                _logger.LogInformation(
                    "Version exchange from {DeviceType} ({DeviceId}): Client v{ClientVersion}",
                    request.DeviceType, request.DeviceId, request.MyVersion);

                var serverData = await _categoryService.GetFullDataAsync();
                var serverVersion = serverData.DataVersion;

                var response = new VersionExchangeResponse
                {
                    ServerVersion = serverVersion,
                    YouNeedToDownload = serverVersion > request.MyVersion,
                    ServerNeedsYourData = request.MyVersion > serverVersion, // Future: mobile editing
                    Data = null
                };

                // If client needs to download, include the data
                if (response.YouNeedToDownload)
                {
                    response.Data = serverData;
                    _logger.LogInformation(
                        "Client needs update: v{ClientVersion} -> v{ServerVersion}. Sending full data.",
                        request.MyVersion, serverVersion);
                }
                else if (response.ServerNeedsYourData)
                {
                    // Future: handle mobile-to-server sync
                    _logger.LogInformation(
                        "Server needs update from client: v{ServerVersion} -> v{ClientVersion}. (Not implemented yet)",
                        serverVersion, request.MyVersion);
                }
                else
                {
                    _logger.LogDebug("Versions match (v{Version}), no sync needed", serverVersion);
                }

                return MessageHandlerResult.CreateSuccess(response);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in version exchange message");
                return MessageHandlerResult.CreateError("Invalid message format");
            }
        }

        // ===== REQUEST FULL DATA =====

        private async Task<MessageHandlerResult> HandleRequestFullDataAsync(UniversalMessage message, string connectionId)
        {
            _logger.LogDebug("Full data requested by connection {ConnectionId}", connectionId);

            var data = await _categoryService.GetFullDataAsync();

            _logger.LogInformation(
                "Sending full data (v{Version}): {CatCount} categories, {LocCount} locations, {PerCount} persons",
                data.DataVersion, data.Categories.Count, data.Locations.Count, data.Persons.Count);

            return MessageHandlerResult.CreateSuccess(data);
        }

        // ===== REQUEST VERSION ONLY =====

        private async Task<MessageHandlerResult> HandleRequestVersionAsync(UniversalMessage message, string connectionId)
        {
            var version = await _categoryService.GetCurrentVersionAsync();

            _logger.LogDebug("Version requested by {ConnectionId}: v{Version}", connectionId, version);

            return MessageHandlerResult.CreateSuccess(new { Version = version });
        }

        // ===== HEARTBEAT =====

        private async Task<MessageHandlerResult> HandleHeartbeatAsync(UniversalMessage message, string connectionId)
        {
            var version = await _categoryService.GetCurrentVersionAsync();

            return MessageHandlerResult.CreateSuccess(new
            {
                ServerTime = DateTime.UtcNow,
                ConnectionId = connectionId,
                CurrentVersion = version
            });
        }

        // ===== CONNECTION LIFECYCLE =====

        public async Task OnAppConnectedAsync(AppConnection connection)
        {
            _logger.LogInformation(
                "MAUI app connected: Device {DeviceId} ({DeviceType}) - Version {AppVersion}",
                connection.DeviceId, connection.DeviceType, connection.AppVersion);
        }

        public async Task OnAppDisconnectedAsync(AppConnection connection)
        {
            _logger.LogInformation("MAUI app disconnected: Device {DeviceId}", connection.DeviceId);
        }

        public async Task<bool> ValidateMessageAsync(UniversalMessage message)
        {
            if (string.IsNullOrEmpty(message.MessageType))
            {
                _logger.LogWarning("Message missing MessageType");
                return false;
            }

            return true;
        }

        private async Task<MessageHandlerResult> HandleUploadDataAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var uploadedData = JsonSerializer.Deserialize<FullCategoryData>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (uploadedData == null)
                {
                    return MessageHandlerResult.CreateError("Invalid data format");
                }

                _logger.LogInformation(
                    "Receiving data upload from MAUI: v{Version} - {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    uploadedData.DataVersion,
                    uploadedData.Categories.Count,
                    uploadedData.Locations.Count,
                    uploadedData.Persons.Count);

                // Save to server
                await _categoryService.SaveFullDataAsync(uploadedData);

                _logger.LogInformation("Data saved from MAUI upload. Server now at v{Version}", uploadedData.DataVersion);

                return MessageHandlerResult.CreateSuccess(new
                {
                    success = true,
                    newVersion = uploadedData.DataVersion
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling data upload");
                return MessageHandlerResult.CreateError($"Upload failed: {ex.Message}");
            }
        }

        public async Task<object?> GetInitialDataAsync(AppConnection connection)
        {
            _logger.LogDebug("Getting initial data for device {DeviceId}", connection.DeviceId);

            var data = await _categoryService.GetFullDataAsync();

            return new
            {
                MessageType = "InitialData",
                Version = data.DataVersion,
                Data = data
            };
        }
    }
}