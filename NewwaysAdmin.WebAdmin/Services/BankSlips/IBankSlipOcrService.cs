// NewwaysAdmin.WebAdmin/Services/BankSlips/IBankSlipOcrService.cs
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    // Add progress reporting interface
    public interface IProgressReporter
    {
        void ReportProgress(int processedCount, int totalCount, string currentFileName = "");
    }

    public interface IBankSlipOcrService
    {
        Task<BankSlipProcessingResult> ProcessSlipCollectionAsync(
            string collectionId,
            DateTime startDate,
            DateTime endDate,
            string username,
            IProgressReporter? progressReporter = null);
        Task<List<SlipCollection>> GetUserCollectionsAsync(string username);
        Task<SlipCollection?> GetCollectionAsync(string collectionId, string username);
        Task SaveCollectionAsync(SlipCollection collection, string username);
        Task DeleteCollectionAsync(string collectionId, string username);
        Task<BankSlipData?> TestProcessSingleFileAsync(string filePath, SlipCollection collection);
        Task<List<SlipCollection>> GetAllCollectionsAsync();
    }
}