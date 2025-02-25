namespace NewwaysAdmin.WebAdmin.Models
{
    public class RejectEntry
    {
        public DateTime Timestamp { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}