namespace NewwaysAdmin.Shared.IO
{
    public class StorageNode
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StorageType Type { get; set; }
        public bool CreateBackups { get; set; }
        public int MaxBackupCount { get; set; }
        public List<StorageNode> Children { get; set; } = new();

        public StorageNode AddChild(string name, StorageType type)
        {
            var child = new StorageNode
            {
                Name = name,
                Type = type,
                CreateBackups = this.CreateBackups,
                MaxBackupCount = this.MaxBackupCount
            };
            Children.Add(child);
            return child;
        }

        public string GetFullPath(string basePath, List<string>? parentSegments = null)
        {
            parentSegments ??= new List<string>();
            parentSegments.Add(Name);
            return Path.Combine(basePath, Path.Combine(parentSegments.ToArray()));
        }
    }

    public enum StorageType
    {
        Binary,
        Json
    }
}
