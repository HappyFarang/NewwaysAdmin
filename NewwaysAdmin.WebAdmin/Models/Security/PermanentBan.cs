// Models/Security/PermanentBan.cs
namespace NewwaysAdmin.WebAdmin.Models.Security
{
    /// <summary>
    /// Represents a permanently banned IP address
    /// </summary>
    public class PermanentBan
    {
        public string IpAddress { get; set; } = "";
        public DateTime BannedAt { get; set; } = DateTime.UtcNow;
        public string Reason { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public string LastPath { get; set; } = "";
        public int TotalRequestsBeforeBan { get; set; }

        /// <summary>
        /// Optional notes for manual review
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Who banned it (System or Admin username)
        /// </summary>
        public string BannedBy { get; set; } = "System";
    }
}