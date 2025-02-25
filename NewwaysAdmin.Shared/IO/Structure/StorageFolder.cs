namespace NewwaysAdmin.Shared.IO.Structure
{
    public class StorageFolder
    {
        public required string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public required StorageType Type { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsShared { get; set; }
        public bool CreateBackups { get; set; } = true;
        public int MaxBackupCount { get; set; } = 5;
        public DateTime Created { get; set; } = DateTime.Now;
        public string CreatedBy { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;

        // Unique identifier for conflict checking
        public string UniqueId => string.IsNullOrEmpty(Path)
            ? Name
            : $"{Path}/{Name}";
    }
}