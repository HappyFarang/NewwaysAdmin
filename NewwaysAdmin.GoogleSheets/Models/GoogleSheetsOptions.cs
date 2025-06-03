namespace NewwaysAdmin.GoogleSheets.Models
{
    public class GoogleSheetsOptions
    {
        public string CredentialsPath { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public bool AutoShareWithUser { get; set; } = false;
        public string DefaultShareEmail { get; set; } = string.Empty;
        public int RetryAttempts { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 30;
        public string DefaultFolderId { get; set; } = string.Empty;
    }
}