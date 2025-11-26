// File: NewwaysAdmin.SignalR.Contracts/Models/UniversalMessage.cs
using System.Text.Json;

namespace NewwaysAdmin.SignalR.Contracts.Models
{
    /// <summary>
    /// Universal message format for communication between apps through SignalR
    /// </summary>
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

    /// <summary>
    /// Strongly typed message wrapper for easier handling
    /// </summary>
    public class UniversalMessage<T> : UniversalMessage
    {
        public new T Data { get; set; } = default!;

        public UniversalMessage()
        {
        }

        public UniversalMessage(string messageType, string sourceApp, string targetApp, T data)
        {
            MessageType = messageType;
            SourceApp = sourceApp;
            TargetApp = targetApp;
            Data = data;
        }
    }

    /// <summary>
    /// Message priority for routing and processing
    /// </summary>
    public enum MessagePriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// App connection metadata
    /// </summary>
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

    /// <summary>
    /// Connection status tracking
    /// </summary>
    public enum ConnectionStatus
    {
        Connecting = 0,
        Connected = 1,
        Reconnecting = 2,
        Disconnected = 3,
        Banned = 4
    }

    /// <summary>
    /// App registration information
    /// </summary>
    public class AppRegistration
    {
        public string AppName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public List<string> SupportedMessageTypes { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Message acknowledgment
    /// </summary>
    public class MessageAck
    {
        public string MessageId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }
}