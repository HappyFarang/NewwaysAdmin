// File: NewwaysAdmin.WebAdmin/Services/SignalR/CategorySyncHandler.cs
using NewwaysAdmin.SignalR.Universal.Models;
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
            "RecordCategoryUsage",
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

            var syncData = await _categoryService.GetMobileSyncDataAsync();

            return MessageHandlerResult.CreateSuccess(syncData);
        }

        private async Task<MessageHandlerResult> HandleMobileSyncDataRequestAsync(UniversalMessage message, string connectionId)
        {
            // Same as category sync but with different logging for tracking
            _logger.LogDebug("Mobile sync data requested by connection {ConnectionId}", connectionId);

            var syncData = await _categoryService.GetMobileSyncDataAsync();

            return MessageHandlerResult.CreateSuccess(syncData);
        }
        
        private async Task<MessageHandlerResult> HandleCategorySelectedAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                // Parse selection data
                var selectionData = JsonSerializer.Deserialize<CategorySelectionData>(message.Data.GetRawText());

                if (selectionData == null)
                {
                    return MessageHandlerResult.CreateError("Invalid selection data format");
                }

                _logger.LogInformation("Category selected on MAUI: {CategoryPath} by device {DeviceId}",
                    selectionData.CategoryPath, selectionData.DeviceId);

                // Could record analytics here if needed
                // await _categoryService.RecordSelectionAnalyticsAsync(selectionData);

                return MessageHandlerResult.CreateSuccess(new { acknowledged = true });
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

            // Could send welcome message or initial sync here
            // For now, initial data is handled by GetInitialDataAsync
        }

        public async Task OnAppDisconnectedAsync(AppConnection connection)
        {
            _logger.LogInformation("MAUI app disconnected: Device {DeviceId}", connection.DeviceId);

            // Could clean up any connection-specific resources here
        }

        public async Task<bool> ValidateMessageAsync(UniversalMessage message)
        {
            // Basic validation - ensure message has required fields
            if (string.IsNullOrEmpty(message.MessageType))
            {
                _logger.LogWarning("Message missing MessageType from connection");
                return false;
            }

            if (message.Data.ValueKind == JsonValueKind.Undefined)
            {
                // Some message types might not need data
                if (message.MessageType is "RequestCategorySync" or "RequestMobileSyncData" or "HeartbeatCheck")
                {
                    return true;
                }

                _logger.LogWarning("Message missing Data for type {MessageType}", message.MessageType);
                return false;
            }

            return true;
        }

        public async Task<object?> GetInitialDataAsync(AppConnection connection)
        {
            try
            {
                // Send initial category data to newly connected MAUI app
                var syncData = await _categoryService.GetMobileSyncDataAsync();

                _logger.LogDebug("Sending initial category data to MAUI app: Device {DeviceId}", connection.DeviceId);

                return new
                {
                    messageType = "InitialCategoryData",
                    data = syncData,
                    serverInfo = new
                    {
                        serverTime = DateTime.UtcNow,
                        version = "1.0.0",
                        supportedFeatures = new[] { "categories", "usage_tracking", "realtime_sync" }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting initial data for MAUI app: Device {DeviceId}", connection.DeviceId);
                return null;
            }
        }
    }

    // ===== MESSAGE DATA MODELS =====

    public class CategoryUsageData
    {
        public string SubCategoryId { get; set; } = string.Empty;
        public string? LocationId { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string? TransactionNote { get; set; }
        public decimal? Amount { get; set; }
    }

    public class CategorySelectionData
    {
        public string SubCategoryId { get; set; } = string.Empty;
        public string CategoryPath { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public DateTime SelectedAt { get; set; } = DateTime.UtcNow;
        public string? LocationId { get; set; }
    }
}