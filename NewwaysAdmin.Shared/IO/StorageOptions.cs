namespace NewwaysAdmin.Shared.IO
{
    public class StorageOptions
    {
        public required string BasePath { get; set; }
        public required string FileExtension { get; set; }
        public bool CreateBackups { get; set; } = true;
        public int MaxBackupCount { get; set; } = 5;
        public bool ValidateAfterSave { get; set; } = true;
    }
}
