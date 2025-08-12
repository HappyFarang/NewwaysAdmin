// Services/Security/Models/SecurityModels.cs
namespace NewwaysAdmin.WebAdmin.Services.Security.Models
{
    public class SecurityRequestRecord
    {
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; } = "";
        public string Path { get; set; } = "";
        public int ResponseCode { get; set; }
        public string UserAgent { get; set; } = "";
        public bool IsAuthenticated { get; set; }
    }

    public class SecurityBlockedClient
    {
        public string IpAddress { get; set; } = "";
        public DateTime BlockedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Reason { get; set; } = "";
        public int TotalRequests { get; set; }
        public string UserAgent { get; set; } = "";
        public bool IsPermanent { get; set; }
    }

    public class SecurityMetrics
    {
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public int TotalRequestsLastHour { get; set; }
        public int UniqueIPsLastHour { get; set; }
        public int CurrentlyBlockedIPs { get; set; }
        public int AutoBlocksToday { get; set; }
        public int PublicPageRequests { get; set; }
        public int AuthenticatedPageRequests { get; set; }
        public List<TopAttacker> TopAttackers { get; set; } = new();
        public List<string> MostTargetedPaths { get; set; } = new();
    }

    public class TopAttacker
    {
        public string IpAddress { get; set; } = "";
        public int RequestCount { get; set; }
        public bool IsCurrentlyBlocked { get; set; }
        public string LastUserAgent { get; set; } = "";
    }
}