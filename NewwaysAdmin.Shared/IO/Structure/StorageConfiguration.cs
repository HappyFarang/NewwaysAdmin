using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.Shared.IO.Structure
{
    public class StorageConfiguration
    {
        public const string DEFAULT_BASE_DIRECTORY_CONST = "C:/NewwaysAdmin";
        public static string DEFAULT_BASE_DIRECTORY { get; set; } = DEFAULT_BASE_DIRECTORY_CONST;

        public List<StorageFolder> RegisteredFolders { get; set; } = new();

        public static StorageConfiguration CreateDefault()
        {
            var config = new StorageConfiguration
            {
                RegisteredFolders = new List<StorageFolder>
                {
                    new StorageFolder
                    {
                        Name = "Config",
                        Description = "Shared configuration folder for all modules",
                        Type = StorageType.Json,
                        IsShared = true,
                        CreatedBy = "System",
                        CreateBackups = true,
                        MaxBackupCount = 5
                    }
                }
            };
            return config;
        }

        public void AddFolder(StorageFolder folder)
        {
            var existing = RegisteredFolders.FirstOrDefault(f => f.Name == folder.Name);

            if (existing != null)
            {
                // If folder exists and is shared, validate config matches
                if (existing.IsShared)
                {
                    ValidateSharedFolderConfig(existing, folder);

                    // Update LastModified and append CreatedBy if it's different
                    existing.LastModified = DateTime.Now.ToString("O");
                    if (!existing.CreatedBy.Contains(folder.CreatedBy))
                    {
                        existing.CreatedBy += $", {folder.CreatedBy}";
                    }
                    return;
                }

                // If folder exists and is not shared, throw error
                throw new StorageException(
                    $"Folder '{folder.Name}' is already registered and is not marked as shared",
                    folder.Name,
                    StorageOperation.Validate);
            }

            // New folder registration
            folder.Created = DateTime.Now;  // Created is DateTime type
            RegisteredFolders.Add(folder);
        }

        private void ValidateSharedFolderConfig(StorageFolder existing, StorageFolder newFolder)
        {
            if (existing.Type != newFolder.Type)
                throw new StorageException(
                    $"Storage type mismatch for shared folder '{existing.Name}'. " +
                    $"Expected {existing.Type}, got {newFolder.Type}",
                    existing.Name,
                    StorageOperation.Validate);

            if (existing.Path != newFolder.Path)
                throw new StorageException(
                    $"Path mismatch for shared folder '{existing.Name}'. " +
                    $"Expected {existing.Path}, got {newFolder.Path}",
                    existing.Name,
                    StorageOperation.Validate);

            if (existing.CreateBackups != newFolder.CreateBackups ||
                existing.MaxBackupCount != newFolder.MaxBackupCount)
            {
                throw new StorageException(
                    $"Backup settings mismatch for shared folder '{existing.Name}'",
                    existing.Name,
                    StorageOperation.Validate);
            }
        }
    }
}