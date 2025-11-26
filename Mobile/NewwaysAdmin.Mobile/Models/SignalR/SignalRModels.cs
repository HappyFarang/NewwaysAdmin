// File: Mobile/NewwaysAdmin.Mobile/Models/SignalR/SignalRModels.cs
// Copied from SignalR.Universal for mobile compatibility (mobile can't reference server-side projects)

using System.Text.Json;

namespace NewwaysAdmin.SignalR.Universal.Models
{
    public class UniversalMessage
    {
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        public string MessageType { get; set; } = string.Empty;
        public string SourceApp { get; set; } = string.Empty;
        public string TargetApp { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public JsonElement Data { get; set; }
        public MessagePriority Priority { get; set; } = MessagePriority.Normal;
        public bool RequiresAck { get; set; } = false;
        public string? CorrelationId { get; set; }
    }

    public enum MessagePriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public class AppConnection
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
        public string UserAgent { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Connected;
    }

    public enum ConnectionStatus
    {
        Connecting = 0,
        Connected = 1,
        Reconnecting = 2,
        Disconnected = 3,
        Banned = 4
    }

    public class AppRegistration
    {
        public string AppName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public List<string> SupportedMessageTypes { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class MessageAck
    {
        public string MessageId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }
}

namespace NewwaysAdmin.SignalR.Universal.Hubs
{
    public interface IUniversalCommHubClient
    {
        Task InitialData(object data);
        Task RegistrationComplete(object registrationInfo);
        Task RegistrationError(string error);
        Task AuthenticationComplete(object authInfo);
        Task AuthenticationError(string error);
        Task MessageResponse(object response);
        Task MessageAck(NewwaysAdmin.SignalR.Universal.Models.MessageAck ack);
        Task BroadcastMessage(object message);
        Task HeartbeatAck(DateTime serverTime);
        Task ConnectionInfo(NewwaysAdmin.SignalR.Universal.Models.AppConnection connectionInfo);
        Task ServerStats(object stats);
    }
}