// File: NewwaysAdmin.SignalR.Universal/Services/IAppMessageHandler.cs
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.SignalR.Universal.Services
{
    /// <summary>
    /// Interface for app-specific message handlers
    /// Each app (MAUI, FaceScanning, etc.) implements this to handle their specific messages
    /// </summary>
    public interface IAppMessageHandler
    {
        /// <summary>
        /// Unique identifier for the app this handler serves
        /// </summary>
        string AppName { get; }

        /// <summary>
        /// Message types this handler can process
        /// </summary>
        IEnumerable<string> SupportedMessageTypes { get; }

        /// <summary>
        /// Handle an incoming message for this app
        /// </summary>
        Task<MessageHandlerResult> HandleMessageAsync(UniversalMessage message, string connectionId);

        /// <summary>
        /// Called when a new connection for this app is established
        /// </summary>
        Task OnAppConnectedAsync(AppConnection connection);

        /// <summary>
        /// Called when a connection for this app is lost
        /// </summary>
        Task OnAppDisconnectedAsync(AppConnection connection);

        /// <summary>
        /// Validate if a message is properly formatted for this app
        /// </summary>
        Task<bool> ValidateMessageAsync(UniversalMessage message);

        /// <summary>
        /// Get initial data to send to newly connected app instances
        /// </summary>
        Task<object?> GetInitialDataAsync(AppConnection connection);
    }

    /// <summary>
    /// Result of message handling
    /// </summary>
    public class MessageHandlerResult
    {
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public object? ResponseData { get; set; }
        public bool ShouldBroadcast { get; set; } = false;
        public string? BroadcastMessageType { get; set; }
        public List<string> TargetConnections { get; set; } = new();

        public static MessageHandlerResult CreateSuccess(object? responseData = null)
        {
            return new MessageHandlerResult
            {
                Success = true,
                ResponseData = responseData
            };
        }

        public static MessageHandlerResult CreateError(string errorMessage)
        {
            return new MessageHandlerResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }

        public static MessageHandlerResult CreateBroadcast(string messageType, object responseData, List<string>? targetConnections = null)
        {
            return new MessageHandlerResult
            {
                Success = true,
                ShouldBroadcast = true,
                BroadcastMessageType = messageType,
                ResponseData = responseData,
                TargetConnections = targetConnections ?? new()
            };
        }
    }
}