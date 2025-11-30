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
    /// </summary>
    public class CategorySyncHandler : IAppMessageHandler
    {
        private readonly CategoryService _categoryService;
        private readonly ILogger<CategorySyncHandler> _logger;

        public string AppName => "MAUI_ExpenseTracker";

        public IEnumerable<string> SupportedMessageTypes => new[]
        {
            "RequestCategorySync",
            "RequestMobileSyncData",
            "CategorySelected",
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
                    "RequestCategorySync" => await HandleCategorySyncRequestAsync(message, connectionId),
                    "RequestMobileSyncData" => await HandleMobileSyncDataRequestAsync(message, connectionId),
                    "CategorySelected" => await HandleCategorySelectedAsync(message, connectionId),
                    "HeartbeatCheck" => await HandleHeartbeatAsync(message, connectionId),
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

        // ===== SPECIFIC MESSAGE HANDLERS =====

        private async Task<MessageHandlerResult> HandleCategorySyncRequestAsync(UniversalMessage message, string connectionId)
        {
            _logger.LogDebug("Category sync requested by connection {ConnectionId}", connectionId);

            var syncData = await _categoryService.GetFullDataAsync();

            return MessageHandlerResult.CreateSuccess(syncData);
        }

        private async Task<MessageHandlerResult> HandleMobileSyncDataRequestAsync(UniversalMessage message, string connectionId)
        {
            _logger.LogDebug("Mobile sync data requested by connection {ConnectionId}", connectionId);

            var syncData = await _categoryService.GetFullDataAsync();

            return MessageHandlerResult.CreateSuccess(syncData);
        }

        private async Task<MessageHandlerResult> HandleCategorySelectedAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var selectionData = JsonSerializer.Deserialize<CategorySelectionData>(message.Data.GetRawText());

                if (selectionData == null)
                {
                    return MessageHandlerResult.CreateError("Invalid selection data format");
                }

                _logger.LogDebug("Category selected: {SubCategoryId} at location {LocationId} by person {PersonId}",
                    selectionData.SubCategoryId, selectionData.LocationId ?? "No location", selectionData.PersonId ?? "No person");

                // Note: Usage tracking removed - will be handled by project files after OCR processing

                return MessageHandlerResult.CreateSuccess(new { received = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in category selection message from {ConnectionId}", connectionId);
                return MessageHandlerResult.CreateError("Invalid message format");
            }
        }

        private async Task<MessageHandlerResult> HandleHeartbeatAsync(UniversalMessage message, string connectionId)
        {
            return MessageHandlerResult.CreateSuccess(new
            {
                serverTime = DateTime.UtcNow,
                connectionId = connectionId
            });
        }

        // ===== CONNECTION LIFECYCLE =====

        public async Task OnAppConnectedAsync(AppConnection connection)
        {
            _logger.LogInformation("MAUI app connected: Device {DeviceId} ({DeviceType}) - Version {AppVersion}",
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
                _logger.LogWarning("Message missing MessageType from connection");
                return false;
            }

            return true;
        }

        public async Task<object?> GetInitialDataAsync(AppConnection connection)
        {
            _logger.LogDebug("Getting initial data for device {DeviceId}", connection.DeviceId);

            var syncData = await _categoryService.GetFullDataAsync();
            return syncData;
        }
    }

    // ===== SUPPORTING DATA CLASSES =====

    public class CategorySelectionData
    {
        public string SubCategoryId { get; set; } = string.Empty;
        public string? LocationId { get; set; }
        public string? PersonId { get; set; }
        public string? DeviceId { get; set; }
    }
}