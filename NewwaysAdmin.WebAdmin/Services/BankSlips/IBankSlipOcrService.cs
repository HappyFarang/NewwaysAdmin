// NewwaysAdmin.WebAdmin/Services/BankSlips/IBankSlipOcrService.cs
// 🔥 MODERNIZED: Direct dictionary results, no legacy BankSlipData

using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public interface IProgressReporter
    {
        void ReportProgress(int processedCount, int totalCount, string currentFileName = "");
    }

    public interface IBankSlipOcrService
    {
        /// <summary>
        /// Process a collection of slip images and return flexible dictionary results
        /// </summary>
        /// <param name="collectionId">Collection to process</param>
        /// <param name="startDate">Start date filter</param>
        /// <param name="endDate">End date filter</param>
        /// <param name="username">User performing the processing</param>
        /// <param name="progressReporter">Optional progress reporting</param>
        /// <returns>List of dictionaries with extracted field data</returns>
        Task<List<Dictionary<string, string>>> ProcessSlipCollectionAsync(
            string collectionId,
            DateTime startDate,
            DateTime endDate,
            string username,
            IProgressReporter? progressReporter = null);

        /// <summary>
        /// Process a single slip image and return dictionary result
        /// </summary>
        /// <param name="filePath">Path to image file</param>
        /// <param name="collection">Collection configuration</param>
        /// <param name="username">User performing the processing</param>
        /// <returns>Dictionary with extracted field data</returns>
        Task<Dictionary<string, string>?> ProcessSingleFileAsync(
            string filePath,
            SlipCollection collection,
            string username);

        /// <summary>
        /// Test processing a single file without saving results
        /// </summary>
        /// <param name="filePath">Path to test image</param>
        /// <param name="collection">Collection configuration</param>
        /// <returns>Dictionary with extracted field data</returns>
        Task<Dictionary<string, string>?> TestProcessSingleFileAsync(string filePath, SlipCollection collection);

        // Collection management methods (unchanged)
        Task<List<SlipCollection>> GetUserCollectionsAsync(string username);
        Task<SlipCollection?> GetCollectionAsync(string collectionId, string username);
        Task SaveCollectionAsync(SlipCollection collection, string username);
        Task DeleteCollectionAsync(string collectionId, string username);
        Task<List<SlipCollection>> GetAllCollectionsAsync();
    }
}