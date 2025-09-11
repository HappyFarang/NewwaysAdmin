// NewwaysAdmin.Shared/Services/FileProcessing/IExternalFileProcessor.cs

namespace NewwaysAdmin.Shared.Services.FileProcessing
{
    /// <summary>
    /// Simple contract for processing files detected in external collections
    /// Designed to be expandable for future file types and processing needs
    /// </summary>
    public interface IExternalFileProcessor
    {
        /// <summary>
        /// Name of this processor for logging and identification
        /// </summary>
        string Name { get; }

        /// <summary>
        /// File extensions this processor can handle (e.g., [".jpg", ".png"])
        /// </summary>
        string[] Extensions { get; }

        /// <summary>
        /// Process a file detected in an external collection
        /// </summary>
        /// <param name="filePath">Full path to the file to process</param>
        /// <param name="collectionName">Name of the external collection</param>
        /// <returns>True if processing was successful</returns>
        Task<bool> ProcessAsync(string filePath, string collectionName);
    }
}