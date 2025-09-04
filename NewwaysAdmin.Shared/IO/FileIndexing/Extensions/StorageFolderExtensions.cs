// NewwaysAdmin.Shared/IO/FileIndexing/Extensions/StorageFolderExtensions.cs

using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Shared.IO.FileIndexing.Extensions
{
    public static class StorageFolderExtensions
    {
        /// <summary>
        /// Checks if a folder has indexing enabled
        /// </summary>
        public static bool IsIndexingEnabled(this StorageFolder folder)
        {
            return folder.IndexFiles;
        }

        /// <summary>
        /// Gets the extensions that should be indexed for this folder
        /// </summary>
        public static string[] GetIndexedExtensions(this StorageFolder folder)
        {
            return folder.IndexedExtensions ?? Array.Empty<string>();
        }

        /// <summary>
        /// Checks if a file extension should be indexed for this folder
        /// </summary>
        public static bool ShouldIndexExtension(this StorageFolder folder, string fileExtension)
        {
            if (!folder.IndexFiles) return false;

            // If IndexedExtensions is null, auto-detect from StorageType
            var extensionsToCheck = folder.IndexedExtensions ?? GetAutoExtensions(folder);

            if (extensionsToCheck.Length == 0) return false;

            return extensionsToCheck.Any(ext =>
                ext.Equals(fileExtension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the index storage name for this folder (for internal indexing)
        /// </summary>
        public static string GetIndexStorageName(this StorageFolder folder)
        {
            return $"{folder.Name}_Index";
        }

        /// <summary>
        /// Auto-detects file extensions based on StorageType when IndexedExtensions is null
        /// </summary>
        private static string[] GetAutoExtensions(StorageFolder folder)
        {
            return folder.Type switch
            {
                StorageType.Json => [".json"],
                StorageType.Binary => [".bin"],
                _ => Array.Empty<string>()
            };
        }
    }
}