// NewwaysAdmin.Shared/IO/IDataStorage.cs
// UPDATED: Added raw file methods for direct byte[] storage

using System.Threading.Tasks;

namespace NewwaysAdmin.Shared.IO
{
    public interface IDataStorageBase
    {
        // Common non-generic operations if needed
    }

    public interface IDataStorage<T> : IDataStorageBase where T : class, new()
    {
        // ===== EXISTING METHODS (unchanged) =====

        /// <summary>
        /// Load a typed object by identifier
        /// </summary>
        Task<T> LoadAsync(string identifier);

        /// <summary>
        /// Save a typed object with identifier
        /// </summary>
        Task SaveAsync(string identifier, T data);

        /// <summary>
        /// Check if an identifier exists
        /// </summary>
        Task<bool> ExistsAsync(string identifier);

        /// <summary>
        /// List all identifiers in storage
        /// </summary>
        Task<IEnumerable<string>> ListIdentifiersAsync();

        /// <summary>
        /// Delete by identifier
        /// </summary>
        Task DeleteAsync(string identifier);

        // ===== NEW: RAW FILE METHODS =====
        // For folders with RawFileMode = true
        // Identifier INCLUDES the file extension (e.g., "image_001.jpg")

        /// <summary>
        /// Save raw bytes directly to storage without serialization.
        /// Use for images, PDFs, or any binary data that should remain as-is.
        /// Identifier should include the file extension (e.g., "photo_001.jpg").
        /// </summary>
        /// <param name="identifier">Filename including extension</param>
        /// <param name="data">Raw bytes to save</param>
        Task SaveRawAsync(string identifier, byte[] data);

        /// <summary>
        /// Load raw bytes directly from storage without deserialization.
        /// Use for images, PDFs, or any binary data stored as-is.
        /// </summary>
        /// <param name="identifier">Filename including extension</param>
        /// <returns>Raw bytes or null if not found</returns>
        Task<byte[]?> LoadRawAsync(string identifier);

        /// <summary>
        /// Check if a raw file exists
        /// </summary>
        /// <param name="identifier">Filename including extension</param>
        Task<bool> ExistsRawAsync(string identifier);

        /// <summary>
        /// Delete a raw file
        /// </summary>
        /// <param name="identifier">Filename including extension</param>
        Task DeleteRawAsync(string identifier);

        /// <summary>
        /// List all raw files in storage (with extensions)
        /// </summary>
        /// <param name="searchPattern">Optional pattern like "*.jpg" or "project_*"</param>
        Task<IEnumerable<string>> ListRawFilesAsync(string? searchPattern = null);
    }
}