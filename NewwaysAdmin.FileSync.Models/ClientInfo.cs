using System;
using System.Collections.Generic;

namespace NewwaysAdmin.FileSync.Models
{
    public class ClientInfo
    {
        public required string ClientId { get; set; }
        public required string Name { get; set; }
        public HashSet<string> SubscribedFolders { get; set; } = new();
        public DateTime LastSeen { get; set; }
        public string? IpAddress { get; set; }
        public string? Version { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class FileChangeNotification
    {
        public required string FileId { get; set; }
        public required string FolderName { get; set; }
        public required string Hash { get; set; }
        public required string SourceClientId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class SyncMessage
    {
        public required string Type { get; set; }
        public required string MessageId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Payload { get; set; } = new();
    }
}