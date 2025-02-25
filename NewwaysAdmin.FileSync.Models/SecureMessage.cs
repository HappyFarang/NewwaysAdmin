namespace NewwaysAdmin.FileSync.Models
{
    public class SecureMessage
    {
        public required string MessageId { get; set; }
        public required string ClientId { get; set; }
        public required string Timestamp { get; set; }
        public required string Signature { get; set; }
        public required string EncryptedPayload { get; set; }
    }
}